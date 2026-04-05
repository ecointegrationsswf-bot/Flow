using System.Threading.RateLimiting;
using AgentFlow.API.Hubs;
using AgentFlow.API.Middleware;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.AI;
using AgentFlow.Infrastructure.Persistence;
using AgentFlow.Infrastructure.Channels.UltraMsg;
using AgentFlow.Infrastructure.Session;
using AgentFlow.Infrastructure.Storage;
using AgentFlow.Infrastructure.Dispatching;
using AgentFlow.Infrastructure.Persistence.Repositories;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var cfg     = builder.Configuration;
var isDev   = builder.Environment.IsDevelopment();

// ── Persistencia ──────────────────────────────────────
builder.Services.AddDbContext<AgentFlowDbContext>(o =>
    o.UseSqlServer(cfg.GetConnectionString("DefaultConnection"),
        sql => sql.MigrationsAssembly("AgentFlow.Infrastructure"))
     );

// ── Redis (sesiones activas) — opcional en dev ───────────
var redisConn = cfg.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(redisConn))
{
    try
    {
        var redis = ConnectionMultiplexer.Connect(redisConn + ",abortConnect=false,connectTimeout=3000");
        builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
        builder.Services.AddScoped<ISessionStore, RedisSessionStore>();
        Console.WriteLine("Redis conectado");
    }
    catch
    {
        Console.WriteLine("Redis no disponible. Usando InMemorySessionStore.");
        builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();
    }
}
else
{
    Console.WriteLine("Redis no configurado. Usando InMemorySessionStore.");
    builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();
}

// ── Anthropic Claude ───────────────────────────────────
// El sistema usa la API key del tenant (tabla Tenants.LlmApiKey).
// Si hay una key global en appsettings la usamos como fallback; si no, se usa siempre la del tenant.
var anthropicKey = cfg["Anthropic:ApiKey"];
var hasGlobalKey = !string.IsNullOrEmpty(anthropicKey) && anthropicKey != "YOUR_ANTHROPIC_API_KEY";
builder.Services.AddSingleton(new Anthropic.SDK.AnthropicClient(hasGlobalKey ? anthropicKey! : "no-global-key"));
builder.Services.AddSingleton(new AgentFlow.Infrastructure.AI.AnthropicSettings(hasGlobalKey));
builder.Services.AddScoped<IAgentRunner, AnthropicAgentRunner>();
Console.WriteLine(hasGlobalKey ? "Anthropic configurado con key global." : "Anthropic: usando API key por tenant.");

// ── Transcripción de audio (OpenAI Whisper) ─────────────
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.ITranscriptionService,
    AgentFlow.Infrastructure.AI.WhisperTranscriptionService>();
Console.WriteLine(!string.IsNullOrEmpty(cfg["OpenAI:ApiKey"]) && cfg["OpenAI:ApiKey"] != "YOUR_OPENAI_API_KEY"
    ? "OpenAI Whisper configurado — transcripción de notas de voz activa."
    : "OpenAI Whisper no configurado — notas de voz sin transcripción.");

// ── Email (SendGrid) ────────────────────────────────────
builder.Services.AddSingleton<AgentFlow.Infrastructure.Email.IEmailService, AgentFlow.Infrastructure.Email.SendGridEmailService>();

// ── MediatR ────────────────────────────────────────────
builder.Services.AddMediatR(c =>
    c.RegisterServicesFromAssemblies(
        typeof(AgentFlow.Application.Modules.Webhooks.ProcessIncomingMessageCommand).Assembly));

// ── SignalR (monitor en vivo) ──────────────────────────
builder.Services.AddSignalR();
builder.Services.AddSingleton<ConversationNotifier>();
builder.Services.AddSingleton<IConversationNotifier>(sp => sp.GetRequiredService<ConversationNotifier>());

// ── Hangfire — opcional, no bloquea el inicio si la BD no responde ──
var hangfireEnabled = false;
try
{
    // Solo registrar Hangfire si la BD está accesible
    using var testConn = new Microsoft.Data.SqlClient.SqlConnection(cfg.GetConnectionString("DefaultConnection"));
    testConn.Open();
    testConn.Close();

    builder.Services.AddHangfire(h => h
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseSqlServerStorage(cfg.GetConnectionString("DefaultConnection"),
            new SqlServerStorageOptions { SchemaName = "HF" }));
    builder.Services.AddHangfireServer();
    hangfireEnabled = true;
    Console.WriteLine("Hangfire configurado correctamente.");
}
catch (Exception ex)
{
    Console.WriteLine($"Hangfire no disponible: {ex.Message}");
}

// ── Campaign Dispatcher (envío de campañas con rate limiting) ────────
builder.Services.AddScoped<AgentFlow.Infrastructure.Campaigns.CampaignDispatcherService>();
builder.Services.AddScoped<AgentFlow.Infrastructure.Campaigns.CampaignDispatcherJob>();
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.ITransferChatService,
    AgentFlow.Infrastructure.Campaigns.TransferChatService>();
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.ISendEmailResumeService,
    AgentFlow.Infrastructure.Campaigns.SendEmailResumeService>();

// ── Auth — JWT siempre configurado (necesario para super admin [Authorize]) ──
var jwtSecret = cfg["Jwt:Secret"] ?? "AgentFlow_Dev_Secret_Key_Min32Chars!!";
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "agentflow-api",
            ValidateAudience = true,
            ValidAudience = "agentflow-app",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(jwtSecret)) { KeyId = "talkia-key" },
            ValidateLifetime = true,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
        };
        o.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            // Necesario para SignalR: el token viaja como ?access_token= en LongPolling/SSE
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(token) && path.StartsWithSegments("/hubs"))
                    ctx.Token = token;
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = ctx =>
            {
                if (isDev) Console.WriteLine($"JWT AUTH FAILED: {ctx.Exception.GetType().Name}");
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();

// ── UltraMsg (gestion de instancias WhatsApp) ───────────
builder.Services.AddHttpClient<IUltraMsgInstanceService, UltraMsgInstanceService>();
builder.Services.AddHttpClient(); // IHttpClientFactory para descargar media de UltraMsg
builder.Services.AddScoped<IChannelProviderFactory, AgentFlow.Infrastructure.Channels.ChannelProviderFactory>();

// ── Azure Blob Storage (documentos de agentes) ──────────
builder.Services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();

// ── Procesador de archivos Excel ──────────────────────
builder.Services.AddScoped<AgentFlow.Application.Modules.Campaigns.IExcelFileProcessor,
    AgentFlow.Infrastructure.FileProcessing.ExcelFileProcessor>();
builder.Services.AddScoped<AgentFlow.Application.Modules.Campaigns.IFixedFormatCampaignService,
    AgentFlow.Infrastructure.FileProcessing.FixedFormatCampaignService>();

// ── Repositorios y servicios de dominio ──────────────────
builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
builder.Services.AddScoped<IAgentRepository, AgentRepository>();
builder.Services.AddScoped<ICampaignRepository, CampaignRepository>();
builder.Services.AddScoped<IContextDispatcher, ContextDispatcher>();

// ── Tenant context ─────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();

// ── Rate limiting ───────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("auth", httpCtx =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpCtx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
            }));
    options.AddPolicy("api", httpCtx =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpCtx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
            }));
});

// ── File upload limits (10 MB max) ──────────────────────
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 10 * 1024 * 1024);

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ── CORS ────────────────────────────────────────────────
var allowedOrigins = cfg.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? (isDev ? new[] { "http://localhost:5173" } : Array.Empty<string>());
builder.Services.AddCors(o => o.AddPolicy("dev", p =>
    p.WithOrigins(allowedOrigins)
     .WithHeaders("Authorization", "Content-Type", "X-Tenant-Id")
     .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
     .AllowCredentials()));

var app = builder.Build();

// ── Seed de tenant para desarrollo ──────────────────────
if (isDev)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AgentFlowDbContext>();
    db.Database.Migrate();

    // Agregar columna ActionConfigs si no existe (evita depender de migraciones con Designer)
    db.Database.ExecuteSqlRaw(@"
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'ActionConfigs')
        BEGIN
            ALTER TABLE CampaignTemplates ADD ActionConfigs nvarchar(max) NULL;
        END");

    var devTenantId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    if (!db.Tenants.Any(t => t.Id == devTenantId))
    {
        db.Tenants.Add(new AgentFlow.Domain.Entities.Tenant
        {
            Id = devTenantId,
            Name = "Somos Seguros (Dev)",
            Slug = "somos-seguros-dev",
            IsActive = true,
            WhatsAppProvider = AgentFlow.Domain.Enums.ProviderType.UltraMsg,
            WhatsAppPhoneNumber = "+50760001234",
        });
        db.SaveChanges();
        Console.WriteLine("Tenant de desarrollo creado: " + devTenantId);
    }

    // Seed usuario admin de desarrollo
    if (!db.AppUsers.Any(u => u.TenantId == devTenantId))
    {
        db.AppUsers.Add(new AgentFlow.Domain.Entities.AppUser
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            TenantId = devTenantId,
            FullName = "Admin AgentFlow",
            Email = "admin@agentflow.dev",
            PasswordHash = AgentFlow.API.Controllers.AuthController.HashPassword("admin123"),
            Role = AgentFlow.Domain.Entities.UserRole.Admin,
            IsActive = true,
            MustChangePassword = false,
            CreatedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
        if (isDev) Console.WriteLine("Usuario admin de desarrollo creado.");
    }

    // ── Seed super admin (solo en dev) ──────
    if (!db.SuperAdmins.Any())
    {
        db.SuperAdmins.Add(new AgentFlow.Domain.Entities.SuperAdmin
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000099"),
            FullName = "Super Admin",
            Email = "superadmin@agentflow.dev",
            PasswordHash = AgentFlow.API.Controllers.AuthController.HashPassword("superadmin123"),
            IsActive = true,
            MustChangePassword = false,
        });
        db.SaveChanges();
        if (isDev) Console.WriteLine("Super Admin seed creado.");
    }

    // Seed categorias de agentes
    if (!db.AgentCategories.Any())
    {
        db.AgentCategories.AddRange(
            new AgentFlow.Domain.Entities.AgentCategory { Id = Guid.NewGuid(), Name = "Cobros" },
            new AgentFlow.Domain.Entities.AgentCategory { Id = Guid.NewGuid(), Name = "Reclamos" },
            new AgentFlow.Domain.Entities.AgentCategory { Id = Guid.NewGuid(), Name = "Renovaciones" },
            new AgentFlow.Domain.Entities.AgentCategory { Id = Guid.NewGuid(), Name = "General" }
        );
        db.SaveChanges();
    }
}

// ── Seed super admin en producción (si no existe ninguno) ───────────
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AgentFlowDbContext>();

    // Expandir columna AvatarUrl a nvarchar(MAX) para almacenar data URLs base64
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID('AppUsers')
                  AND name = 'AvatarUrl'
                  AND max_length <> -1
            )
            BEGIN
                ALTER TABLE AppUsers ALTER COLUMN AvatarUrl nvarchar(MAX) NULL;
            END");
    }
    catch { /* Columna ya es MAX o tabla aún no existe */ }

    // Agregar columna ContactDataJson para campañas en formato fijo
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignContacts') AND name = 'ContactDataJson')
            BEGIN ALTER TABLE CampaignContacts ADD ContactDataJson nvarchar(MAX) NULL; END");
    }
    catch { }

    // Columnas de dispatch en CampaignContacts
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignContacts') AND name = 'DispatchStatus')
            BEGIN ALTER TABLE CampaignContacts ADD DispatchStatus nvarchar(30) NOT NULL DEFAULT 'Pending'; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignContacts') AND name = 'ClaimedAt')
            BEGIN ALTER TABLE CampaignContacts ADD ClaimedAt datetime2 NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignContacts') AND name = 'SentAt')
            BEGIN ALTER TABLE CampaignContacts ADD SentAt datetime2 NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignContacts') AND name = 'DispatchAttempts')
            BEGIN ALTER TABLE CampaignContacts ADD DispatchAttempts int NOT NULL DEFAULT 0; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignContacts') AND name = 'GeneratedMessage')
            BEGIN ALTER TABLE CampaignContacts ADD GeneratedMessage nvarchar(MAX) NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignContacts') AND name = 'ExternalMessageId')
            BEGIN ALTER TABLE CampaignContacts ADD ExternalMessageId nvarchar(200) NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignContacts') AND name = 'DispatchError')
            BEGIN ALTER TABLE CampaignContacts ADD DispatchError nvarchar(2000) NULL; END");
    }
    catch { }

    // Columnas de lanzamiento en Campaigns
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'Status')
            BEGIN ALTER TABLE Campaigns ADD Status nvarchar(30) NOT NULL DEFAULT 'Pending'; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'LaunchedAt')
            BEGIN ALTER TABLE Campaigns ADD LaunchedAt datetime2 NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'LaunchedByUserId')
            BEGIN ALTER TABLE Campaigns ADD LaunchedByUserId nvarchar(100) NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'CampaignMessageDelaySeconds')
            BEGIN ALTER TABLE Tenants ADD CampaignMessageDelaySeconds int NOT NULL DEFAULT 10; END");
    }
    catch { }

    // Tabla CampaignDispatchLogs
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CampaignDispatchLogs')
            BEGIN
                CREATE TABLE CampaignDispatchLogs (
                    Id                  uniqueidentifier NOT NULL PRIMARY KEY DEFAULT NEWID(),
                    CampaignId          uniqueidentifier NOT NULL,
                    CampaignContactId   uniqueidentifier NOT NULL,
                    TenantId            uniqueidentifier NOT NULL,
                    AttemptNumber       int NOT NULL DEFAULT 1,
                    PromptSnapshot      nvarchar(MAX) NULL,
                    ContactDataSnapshot nvarchar(MAX) NULL,
                    GeneratedMessage    nvarchar(MAX) NULL,
                    PhoneNumber         nvarchar(20) NOT NULL,
                    UltraMsgResponse    nvarchar(MAX) NULL,
                    ExternalMessageId   nvarchar(200) NULL,
                    Status              nvarchar(20) NOT NULL,
                    ErrorDetail         nvarchar(2000) NULL,
                    DurationMs          int NOT NULL DEFAULT 0,
                    OccurredAt          datetime2 NOT NULL DEFAULT GETUTCDATE()
                );
                CREATE INDEX IX_CampaignDispatchLogs_Campaign ON CampaignDispatchLogs (CampaignId, OccurredAt);
                CREATE INDEX IX_CampaignDispatchLogs_Contact  ON CampaignDispatchLogs (CampaignContactId);
            END");
    }
    catch { }

    // ── Columnas de CampaignTemplates (migraciones que pueden no haberse aplicado en prod) ──
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'ActionIds')
            BEGIN ALTER TABLE CampaignTemplates ADD ActionIds nvarchar(2000) NOT NULL DEFAULT '[]'; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'PromptTemplateIds')
            BEGIN ALTER TABLE CampaignTemplates ADD PromptTemplateIds nvarchar(2000) NOT NULL DEFAULT '[]'; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'ActionConfigs')
            BEGIN ALTER TABLE CampaignTemplates ADD ActionConfigs nvarchar(MAX) NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'SystemPrompt')
            BEGIN ALTER TABLE CampaignTemplates ADD SystemPrompt nvarchar(MAX) NOT NULL DEFAULT ''; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'SendFrom')
            BEGIN ALTER TABLE CampaignTemplates ADD SendFrom nvarchar(MAX) NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'SendUntil')
            BEGIN ALTER TABLE CampaignTemplates ADD SendUntil nvarchar(MAX) NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'MaxRetries')
            BEGIN ALTER TABLE CampaignTemplates ADD MaxRetries int NOT NULL DEFAULT 3; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'RetryIntervalHours')
            BEGIN ALTER TABLE CampaignTemplates ADD RetryIntervalHours int NOT NULL DEFAULT 24; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'InactivityCloseHours')
            BEGIN ALTER TABLE CampaignTemplates ADD InactivityCloseHours int NOT NULL DEFAULT 72; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'CloseConditionKeyword')
            BEGIN ALTER TABLE CampaignTemplates ADD CloseConditionKeyword nvarchar(MAX) NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'MaxTokens')
            BEGIN ALTER TABLE CampaignTemplates ADD MaxTokens int NOT NULL DEFAULT 1024; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'AttentionDays')
            BEGIN ALTER TABLE CampaignTemplates ADD AttentionDays nvarchar(100) NOT NULL DEFAULT '[1,2,3,4,5]'; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'AttentionStartTime')
            BEGIN ALTER TABLE CampaignTemplates ADD AttentionStartTime nvarchar(5) NOT NULL DEFAULT '08:00'; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'AttentionEndTime')
            BEGIN ALTER TABLE CampaignTemplates ADD AttentionEndTime nvarchar(5) NOT NULL DEFAULT '17:00'; END");
    }
    catch (Exception ex) { Console.WriteLine($"[Schema] CampaignTemplates columns: {ex.Message}"); }

    // ── Columnas de Tenants (migraciones MoveSendGridToTenant, AddTenantLlmConfiguration) ──
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'SendGridApiKey')
            BEGIN ALTER TABLE Tenants ADD SendGridApiKey nvarchar(500) NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'SenderEmail')
            BEGIN ALTER TABLE Tenants ADD SenderEmail nvarchar(200) NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'LlmApiKey')
            BEGIN ALTER TABLE Tenants ADD LlmApiKey nvarchar(500) NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'LlmModel')
            BEGIN ALTER TABLE Tenants ADD LlmModel nvarchar(100) NOT NULL DEFAULT ''; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'LlmProvider')
            BEGIN ALTER TABLE Tenants ADD LlmProvider nvarchar(50) NOT NULL DEFAULT ''; END");
    }
    catch (Exception ex) { Console.WriteLine($"[Schema] Tenants columns: {ex.Message}"); }

    // ── Columnas adicionales en Campaigns y AppUsers ──────────────────────
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'LaunchedByUserPhone')
            BEGIN ALTER TABLE Campaigns ADD LaunchedByUserPhone nvarchar(20) NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AppUsers') AND name = 'NotifyPhone')
            BEGIN ALTER TABLE AppUsers ADD NotifyPhone nvarchar(20) NULL; END");
    }
    catch (Exception ex) { Console.WriteLine($"[Schema] Campaigns/AppUsers columns: {ex.Message}"); }

    // ── Tabla ActionDefinitions (migración AddActionDefinitions) ──────────
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ActionDefinitions')
            BEGIN
                CREATE TABLE ActionDefinitions (
                    Id              uniqueidentifier NOT NULL PRIMARY KEY DEFAULT NEWID(),
                    Name            nvarchar(100) NOT NULL,
                    Description     nvarchar(500) NULL,
                    RequiresWebhook bit NOT NULL DEFAULT 0,
                    SendsEmail      bit NOT NULL DEFAULT 0,
                    SendsSms        bit NOT NULL DEFAULT 0,
                    WebhookUrl      nvarchar(500) NULL,
                    WebhookMethod   nvarchar(10) NULL,
                    IsActive        bit NOT NULL DEFAULT 1,
                    CreatedAt       datetime2 NOT NULL DEFAULT GETUTCDATE(),
                    UpdatedAt       datetime2 NULL
                );
                CREATE UNIQUE INDEX IX_ActionDefinitions_Name ON ActionDefinitions (Name);
            END");
    }
    catch (Exception ex) { Console.WriteLine($"[Schema] ActionDefinitions table: {ex.Message}"); }

    // ── Tabla PromptTemplates (migración AddPromptTemplates) ──────────────
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PromptTemplates')
            BEGIN
                CREATE TABLE PromptTemplates (
                    Id              uniqueidentifier NOT NULL PRIMARY KEY DEFAULT NEWID(),
                    Name            nvarchar(200) NOT NULL,
                    Description     nvarchar(500) NULL,
                    CategoryId      uniqueidentifier NULL,
                    SystemPrompt    nvarchar(MAX) NULL,
                    ResultPrompt    nvarchar(MAX) NULL,
                    AnalysisPrompts nvarchar(MAX) NULL,
                    FieldMapping    nvarchar(MAX) NULL,
                    IsActive        bit NOT NULL DEFAULT 1,
                    CreatedAt       datetime2 NOT NULL DEFAULT GETUTCDATE(),
                    UpdatedAt       datetime2 NULL,
                    CONSTRAINT FK_PromptTemplates_AgentCategories
                        FOREIGN KEY (CategoryId) REFERENCES AgentCategories(Id) ON DELETE SET NULL
                );
                CREATE INDEX IX_PromptTemplates_CategoryId ON PromptTemplates (CategoryId);
            END");
    }
    catch (Exception ex) { Console.WriteLine($"[Schema] PromptTemplates table: {ex.Message}"); }

    // ── Seed acción SEND_EMAIL_RESUME si no existe ─────────────────────────
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM ActionDefinitions WHERE Name = 'SEND_EMAIL_RESUME')
            BEGIN
                INSERT INTO ActionDefinitions (Id, Name, Description, RequiresWebhook, SendsEmail, SendsSms, IsActive, CreatedAt)
                VALUES ('d221c23d-fa0c-41ad-b356-60df013c877f', 'SEND_EMAIL_RESUME',
                        'Envía un email con el resumen de la gestión al cerrar la conversación',
                        0, 1, 0, 1, GETUTCDATE());
            END");
    }
    catch (Exception ex) { Console.WriteLine($"[Schema] SEND_EMAIL_RESUME seed: {ex.Message}"); }

    if (!db.SuperAdmins.Any())
    {
        db.SuperAdmins.Add(new AgentFlow.Domain.Entities.SuperAdmin
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000099"),
            FullName = "Super Admin",
            Email = "admin@talkia.app",
            PasswordHash = AgentFlow.API.Controllers.AuthController.HashPassword("TalkIA2026!"),
            IsActive = true,
            MustChangePassword = false,
        });
        db.SaveChanges();
        Console.WriteLine("[Seed] Super Admin creado en producción.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[Startup] Error en bloque de seed/schema: {ex.Message}");
    // No lanzar — el API debe arrancar aunque el schema check falle
}

// ── Security headers ─────────────────────────────────
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    if (!isDev)
    {
        context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    }
    await next();
});

app.UseRateLimiter();
app.UseCors("dev");

app.UseAuthentication();
app.UseMiddleware<TenantMiddleware>();   // DESPUÉS de auth, para que el JWT ya esté validado
app.UseAuthorization();

if (isDev)
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHub<ConversationHub>("/hubs/conversations");

if (hangfireEnabled) { try { app.MapHangfireDashboard("/hangfire"); } catch { } }

app.Run();
