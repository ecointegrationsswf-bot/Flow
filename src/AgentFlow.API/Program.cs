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
     .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

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
            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID('CampaignContacts')
                  AND name = 'ContactDataJson'
            )
            BEGIN
                ALTER TABLE CampaignContacts ADD ContactDataJson nvarchar(MAX) NULL;
            END");
    }
    catch { /* Tabla aún no existe o columna ya creada */ }

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
