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
        builder.Services.AddScoped<IMessageBufferStore, AgentFlow.Infrastructure.Messaging.RedisMessageBufferStore>();
        Console.WriteLine("Redis conectado");
    }
    catch
    {
        Console.WriteLine("Redis no disponible. Usando InMemorySessionStore.");
        builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();
        builder.Services.AddSingleton<IMessageBufferStore, AgentFlow.Infrastructure.Messaging.InMemoryMessageBufferStore>();
    }
}
else
{
    Console.WriteLine("Redis no configurado. Usando InMemorySessionStore.");
    builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();
    builder.Services.AddSingleton<IMessageBufferStore, AgentFlow.Infrastructure.Messaging.InMemoryMessageBufferStore>();
}

// MessageBufferFlushJob — LEGACY (Hangfire). Se mantiene registrado por si quedan
// jobs viejos en BD pero el flujo activo usa InProcessMessageDebouncer.
builder.Services.AddScoped<AgentFlow.Infrastructure.Messaging.MessageBufferFlushJob>();

// Debouncer in-process — singleton, sin dependencia de Hangfire/Redis.
// Reemplaza el approach anterior que dependía de Hangfire+Redis para timing,
// que en Smartasp fallaba por reciclajes de AppPool y silenciosamente caía
// al flujo directo (mensajes procesados uno por uno sin agrupar).
builder.Services.AddSingleton<AgentFlow.Infrastructure.Messaging.InProcessMessageDebouncer>();

// Cola durable de mensajes entrantes (Día 1 — doble escritura). Sobrevive
// reciclados de AppPool y permite que el Worker on-prem sea la fuente
// autoritativa de procesamiento. El debouncer in-memory queda como
// acelerador del hot-path.
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.IInboundMessageQueue,
    AgentFlow.Infrastructure.Messaging.SqlInboundMessageQueue>();

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

// ── Email provider seleccionable ────────────────────────
// "Resend" → ResendEmailService (HTTP a api.resend.com)
// "Smtp"   → SmtpEmailService (SMTP estándar — Gmail, Office365, Brevo)
// otro/null → SendGridEmailService (default histórico)
{
    var emailProvider = cfg["Email:Provider"] ?? "SendGrid";
    if (string.Equals(emailProvider, "Resend", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddSingleton<AgentFlow.Infrastructure.Email.IEmailService,
            AgentFlow.Infrastructure.Email.ResendEmailService>();
        Console.WriteLine($"Email provider: Resend (from={cfg["Resend:FromEmail"]}).");
    }
    else if (string.Equals(emailProvider, "Smtp", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddSingleton<AgentFlow.Infrastructure.Email.IEmailService,
            AgentFlow.Infrastructure.Email.SmtpEmailService>();
        Console.WriteLine($"Email provider: SMTP ({cfg["Smtp:Host"]}:{cfg["Smtp:Port"] ?? "587"}).");
    }
    else
    {
        builder.Services.AddSingleton<AgentFlow.Infrastructure.Email.IEmailService,
            AgentFlow.Infrastructure.Email.SendGridEmailService>();
        Console.WriteLine("Email provider: SendGrid.");
    }
}

// Filter del controller InternalEmailController — valida X-Internal-Email-Key.
builder.Services.AddScoped<AgentFlow.API.Controllers.InternalEmailKeyFilter>();

// EmailTemplateRenderer — usado por SendEmailResumeService (y futuras acciones)
// para renderizar plantillas HTML del maestro con variables {{cliente.x}}.
builder.Services.AddSingleton<AgentFlow.Infrastructure.Email.EmailTemplateRenderer>();

// ── MediatR ────────────────────────────────────────────
builder.Services.AddMediatR(c =>
    c.RegisterServicesFromAssemblies(
        typeof(AgentFlow.Application.Modules.Webhooks.ProcessIncomingMessageCommand).Assembly));

// ── SignalR (monitor en vivo) ──────────────────────────
builder.Services.AddSignalR();
builder.Services.AddSingleton<ConversationNotifier>();
builder.Services.AddSingleton<IConversationNotifier>(sp => sp.GetRequiredService<ConversationNotifier>());

// ── Hangfire — opcional, no bloquea el inicio si la BD no responde ──
// NOTA: AddHangfireServer() registra un IHostedService cuyo StartAsync() puede lanzar
// una excepción no capturada que mata el host. Lo envolvemos en un safe wrapper.
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
// Red de seguridad global contra Claimed huérfanos. La capa 1 vive en el
// dispatcher del Worker (release inline al cancelar + recovery sweep por
// campaña). Este job es la capa 2: corre en Hangfire del API cada 5 min
// y libera Claimed > 5 min de TODAS las campañas, sin depender del Worker.
builder.Services.AddScoped<AgentFlow.Infrastructure.Campaigns.CampaignContactOrphanReleaseJob>();
// Business hours en TZ del tenant — necesario para programar follow-ups
// dentro del horario laboral del cliente.
builder.Services.AddSingleton<AgentFlow.Domain.Interfaces.IBusinessHoursClock,
    AgentFlow.Infrastructure.Time.BusinessHoursClock>();
// Generador de mensaje con Claude (paridad con n8n) — usado por el dispatcher v2.
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.IInitialMessageGenerator,
    AgentFlow.Infrastructure.AI.InitialMessageGenerator>();

// ── Reportes ─────────────────────────────────────────────
// QuestPDF Community License (free para uso comercial < $1M revenue).
// Settings.License debe configurarse UNA vez al inicializar la app.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
builder.Services.AddScoped<AgentFlow.Application.Modules.Reports.IEffectivenessReportService,
    AgentFlow.Infrastructure.Reports.EffectivenessReportService>();
builder.Services.AddScoped<AgentFlow.Infrastructure.Reports.ConversationDetailsExcelExporter>();

// ── CampaignIntakeService v2 (reemplazo de la fase A+B+C de n8n) ─────
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.IDuplicateChecker,
    AgentFlow.Infrastructure.Campaigns.V2.DuplicateChecker>();
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.ITransferChatService,
    AgentFlow.Infrastructure.Campaigns.TransferChatService>();
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.ISendEmailResumeService,
    AgentFlow.Infrastructure.Campaigns.SendEmailResumeService>();
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.ICampaignLauncher,
    AgentFlow.Infrastructure.Campaigns.CampaignLauncher>();

// ── Scheduled Webhook Worker (Fase 1 Campaign Automation) ────────────
// Repositorios + dispatcher de eventos + executor genérico + BackgroundService.
// Independiente de Hangfire — no compite por sus 2 workers.
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.IScheduledJobRepository,
    AgentFlow.Infrastructure.Persistence.Repositories.ScheduledJobRepository>();
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.IJobExecutionRepository,
    AgentFlow.Infrastructure.Persistence.Repositories.JobExecutionRepository>();
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.IJobExecutionItemRepository,
    AgentFlow.Infrastructure.Persistence.Repositories.JobExecutionItemRepository>();
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.IWebhookEventDispatcher,
    AgentFlow.Infrastructure.ScheduledJobs.WebhookEventDispatcher>();

// Auditor compartido — ver skill `scheduled-jobs`.
builder.Services.AddScoped<AgentFlow.Infrastructure.ScheduledJobs.JobExecutionAuditor>();

builder.Services.AddScoped<AgentFlow.Domain.Interfaces.IScheduledJobExecutor,
    AgentFlow.Infrastructure.ScheduledJobs.DefaultWebhookExecutor>();

// Fase 2 executors — slugs FOLLOW_UP_MESSAGE y AUTO_CLOSE_CAMPAIGN.
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.IScheduledJobExecutor,
    AgentFlow.Infrastructure.ScheduledJobs.FollowUpExecutor>();
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.IScheduledJobExecutor,
    AgentFlow.Infrastructure.ScheduledJobs.CampaignAutoCloseExecutor>();

// Fase 3 executor — slug LABEL_CONVERSATIONS (etiquetado IA + webhook resultado).
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.IScheduledJobExecutor,
    AgentFlow.Infrastructure.ScheduledJobs.ConversationLabelingJob>();

// Fase 3 executor — slug SEND_LABELING_SUMMARY (Excel + Azure Blob + email).
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.IScheduledJobExecutor,
    AgentFlow.Infrastructure.ScheduledJobs.SendLabelingSummaryExecutor>();

// Slug NOTIFY_GESTION — batch cron que dispara el webhook de gestión por cada
// conversación etiquetada desde la última corrida (cutoff = job.LastRunAt).
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.IScheduledJobExecutor,
    AgentFlow.Infrastructure.ScheduledJobs.NotifyGestionBatchExecutor>();

// Slug DOWNLOAD_DELINQUENCY_DATA — descarga JSON de morosidad por tenant y lo procesa.
// Cron configurado en /admin/scheduled-jobs (ej: "0 8 * * *" → diario 8am Panamá).
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.IScheduledJobExecutor,
    AgentFlow.Infrastructure.ScheduledJobs.DelinquencyDownloadExecutor>();

// NOTA: ScheduledWebhookWorker corre en AgentFlow.Worker on-premise (Windows Service).
// Si se registra también acá se ejecuta dos veces — los locks optimistas evitan
// duplicar la ejecución del MISMO job, pero generan contención sobre la tabla
// ScheduledWebhookJobs y duplican carga de CPU/BD. Para mantener una sola fuente
// de procesamiento, solo se registra acá si se setea Worker:EnableInApi=true en
// appsettings (escenario: deploys que NO tengan Worker on-prem corriendo).
if (cfg.GetValue("Worker:EnableInApi", false))
{
    Console.WriteLine("[Startup] ScheduledWebhookWorker activo dentro del API (Worker:EnableInApi=true).");
    builder.Services.AddHostedService<AgentFlow.Infrastructure.ScheduledJobs.ScheduledWebhookWorker>();
}
else
{
    Console.WriteLine("[Startup] ScheduledWebhookWorker delegado al Worker on-prem (set Worker:EnableInApi=true para correrlo aquí).");
}

// ── Módulo Morosidad ─────────────────────────────────────────────────────
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.IDelinquencyProcessor,
    AgentFlow.Infrastructure.Morosidad.DelinquencyProcessor>();

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

// Validador de números WhatsApp (lista negra + UltraMsg /contacts/check)
builder.Services.AddHttpClient<AgentFlow.Infrastructure.Channels.UltraMsg.UltraMsgContactsChecker>();
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.IWhatsAppNumberValidator,
    AgentFlow.Infrastructure.Channels.UltraMsg.WhatsAppNumberValidator>();

// ── Notificaciones a Microsoft Teams (Power Automate webhook) ──
// Alertas operacionales: lineas caidas, campanas auto-pausadas, errores criticos.
builder.Services.AddHttpClient<AgentFlow.Domain.Interfaces.ITeamsNotifier,
    AgentFlow.Infrastructure.Notifications.PowerAutomateTeamsNotifier>();

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

// ── Cerebro ─────────────────────────────────────────────
builder.Services.AddScoped<IAgentRegistry, AgentFlow.Infrastructure.Brain.AgentRegistryService>();
builder.Services.AddScoped<IClassifierService, AgentFlow.Infrastructure.Brain.ClassifierService>();
builder.Services.AddScoped<IValidationService, AgentFlow.Infrastructure.Brain.ValidationService>();
builder.Services.AddScoped<IBrainService, AgentFlow.Infrastructure.Brain.BrainService>();

// ── Webhook Contract System ────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.AddScoped<AgentFlow.Domain.Webhooks.ISystemContextBuilder,
    AgentFlow.Infrastructure.Webhooks.SystemContextBuilder>();
builder.Services.AddSingleton<AgentFlow.Domain.Webhooks.IPayloadBuilder,
    AgentFlow.Infrastructure.Webhooks.PayloadBuilder>();
builder.Services.AddScoped<AgentFlow.Domain.Webhooks.IHttpDispatcher,
    AgentFlow.Infrastructure.Webhooks.HttpDispatcher>();
builder.Services.AddScoped<AgentFlow.Domain.Webhooks.IOutputInterpreter,
    AgentFlow.Infrastructure.Webhooks.OutputInterpreter>();
builder.Services.AddScoped<AgentFlow.Infrastructure.Webhooks.ActionConfigReader>();
builder.Services.AddScoped<AgentFlow.Domain.Webhooks.IActionExecutorService,
    AgentFlow.Infrastructure.Webhooks.ActionExecutorService>();
// Auto-encadenamiento server-side de acciones (Paso 5 del Webhook Builder).
builder.Services.AddScoped<AgentFlow.Domain.Webhooks.IActionChainResolver,
    AgentFlow.Infrastructure.Webhooks.ActionChainResolver>();
// Action Trigger Protocol — Fase 0 (NoOp). En Fase 2 se reemplaza la implementación.
builder.Services.AddScoped<AgentFlow.Domain.Webhooks.IActionPromptBuilder,
    AgentFlow.Infrastructure.Webhooks.ActionPromptBuilder>();

// Documentos de referencia por campaña — Fase 1 (builder + fetch).
// La inyección en el runtime del agente se hace en Fase 2.
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.IDocumentReferencePromptBuilder,
    AgentFlow.Infrastructure.AI.DocumentReferencePromptBuilder>();

// ── RAG: pipeline de indexado y retrieval de documentos ─────────────────────
// Servicios sin estado, registrados como singleton para reutilizar HttpClient.
// EmbeddingService llama a OpenAI; PdfPig y el chunker son in-process.
builder.Services.AddSingleton<AgentFlow.Domain.Interfaces.IEmbeddingService,
    AgentFlow.Infrastructure.Embeddings.OpenAIEmbeddingService>();
builder.Services.AddSingleton<AgentFlow.Domain.Interfaces.IPdfTextExtractor,
    AgentFlow.Infrastructure.Documents.PdfPigTextExtractor>();
builder.Services.AddSingleton<AgentFlow.Domain.Interfaces.ITextChunker,
    AgentFlow.Infrastructure.Documents.SentenceAwareChunker>();
// El indexer es Scoped porque depende de AgentFlowDbContext (Scoped).
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.IDocumentIndexer,
    AgentFlow.Infrastructure.Documents.DocumentIndexer>();
// Wrapper Hangfire — Hangfire necesita instanciar el job via DI.
builder.Services.AddScoped<AgentFlow.Infrastructure.Documents.DocumentIndexerHangfireJob>();
// Retriever (lectura): se consulta en cada mensaje cuando Tenant.UseRagRetrieval=true.
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.IDocumentRetriever,
    AgentFlow.Infrastructure.Documents.ChunkBasedDocumentRetriever>();
// Auditoría centralizada — Singleton porque internamente abre scopes nuevos.
builder.Services.AddSingleton<AgentFlow.Domain.Interfaces.ISystemAuditLogger,
    AgentFlow.Infrastructure.Auditing.SystemAuditLogger>();

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

// ── File upload limits (100 MB techo global) ──────────────────────
// Esto es el techo absoluto del servidor. Cada endpoint puede acotar más
// con [RequestSizeLimit(...)]; los endpoints sin attr quedan limitados aquí.
// El upload de PDFs de referencia usa el techo completo (100 MB).
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 100L * 1024 * 1024);

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

// ── Migraciones automáticas (todos los entornos) ─────────
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AgentFlowDbContext>();
    try
    {
        db.Database.Migrate();
        Console.WriteLine("[Startup] Migraciones aplicadas correctamente.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Error al aplicar migraciones: {ex.Message}");
    }

    // Garantizar columnas críticas aunque la migración haya fallado parcialmente
    try
    {
        using var scope2 = app.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AgentFlowDbContext>();
        db2.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AppUsers') AND name = 'Permissions')
            BEGIN
                ALTER TABLE AppUsers ADD Permissions nvarchar(2000) NOT NULL DEFAULT '[]';
            END");
        db2.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'ActionConfigs')
            BEGIN
                ALTER TABLE CampaignTemplates ADD ActionConfigs nvarchar(max) NULL;
            END");
        // Flag de "partir campañas de descarga por ejecutivo" — se agrega vía guard
        // idempotente (no migración EF) por el drift de schema existente en prod.
        db2.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ActionDelinquencyConfigs') AND name = 'SplitCampaignsByExecutive')
            BEGIN
                ALTER TABLE ActionDelinquencyConfigs ADD SplitCampaignsByExecutive bit NOT NULL DEFAULT 0;
            END");
        // AutoLaunchCampaigns: default 1 para que las configs existentes sigan
        // lanzando automaticamente (comportamiento historico). El operador puede
        // apagarlo para crear campanas en Pending y lanzarlas a mano.
        db2.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ActionDelinquencyConfigs') AND name = 'AutoLaunchCampaigns')
            BEGIN
                ALTER TABLE ActionDelinquencyConfigs ADD AutoLaunchCampaigns bit NOT NULL DEFAULT 1;
            END");
        // TenantActionContracts: contrato webhook por (accion, tenant). Tabla creada
        // via guard (no migracion EF por el drift). Las columnas matchean las
        // convenciones EF para que el DbSet mapee sin migracion.
        db2.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TenantActionContracts')
            BEGIN
                CREATE TABLE TenantActionContracts (
                    Id uniqueidentifier NOT NULL CONSTRAINT PK_TenantActionContracts PRIMARY KEY,
                    ActionDefinitionId uniqueidentifier NOT NULL,
                    TenantId uniqueidentifier NOT NULL,
                    ContractJson nvarchar(max) NOT NULL,
                    IsActive bit NOT NULL DEFAULT 1,
                    CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    UpdatedAt datetime2 NULL
                );
                CREATE UNIQUE INDEX UX_TenantActionContract_Action_Tenant
                    ON TenantActionContracts (ActionDefinitionId, TenantId);
            END");
        Console.WriteLine("[Startup] Columnas de seguridad verificadas.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Error verificando columnas: {ex.Message}");
    }
}

// ── Seed de tenant para desarrollo ──────────────────────
if (isDev)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AgentFlowDbContext>();

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

        // Delivery status real (UltraMsg webhook message_ack)
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignContacts') AND name = 'DeliveryStatus')
            BEGIN ALTER TABLE CampaignContacts ADD DeliveryStatus nvarchar(20) NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignContacts') AND name = 'DeliveredAt')
            BEGIN ALTER TABLE CampaignContacts ADD DeliveredAt datetime2 NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignContacts') AND name = 'ReadAt')
            BEGIN ALTER TABLE CampaignContacts ADD ReadAt datetime2 NULL; END");

        // Delivery status real en Messages (UltraMsg webhook message_ack)
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Messages') AND name = 'DeliveryStatus')
            BEGIN ALTER TABLE Messages ADD DeliveryStatus nvarchar(20) NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Messages') AND name = 'LastAck')
            BEGIN ALTER TABLE Messages ADD LastAck int NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Messages') AND name = 'DeliveredAt')
            BEGIN ALTER TABLE Messages ADD DeliveredAt datetime2 NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Messages') AND name = 'ReadAt')
            BEGIN ALTER TABLE Messages ADD ReadAt datetime2 NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Messages') AND name = 'DeliveryUpdatedAt')
            BEGIN ALTER TABLE Messages ADD DeliveryUpdatedAt datetime2 NULL; END");
    }
    catch { }

    // Cooldown de TRANSFER_CHAT — evita que se notifique al ejecutivo más de una
    // vez por la misma conversación. Lo setea TransferChatService la primera vez
    // que escala; las siguientes escalaciones lo respetan y no spamean.
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Conversations') AND name = 'LastTransferChatSentAt')
            BEGIN ALTER TABLE Conversations ADD LastTransferChatSentAt datetime2 NULL; END");
    }
    catch { }

    // Monitor diario de salud de líneas WhatsApp (job WHATSAPP_LINE_HEALTH_CHECK).
    // Persiste el último estado pingeado para que la UI muestre el badge real
    // sin tener que llamar a UltraMsg en cada render.
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('WhatsAppLines') AND name = 'LastStatus')
            BEGIN ALTER TABLE WhatsAppLines ADD LastStatus nvarchar(40) NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('WhatsAppLines') AND name = 'LastStatusCheckedAt')
            BEGIN ALTER TABLE WhatsAppLines ADD LastStatusCheckedAt datetime2 NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('WhatsAppLines') AND name = 'ConsecutivePingFailures')
            BEGIN ALTER TABLE WhatsAppLines ADD ConsecutivePingFailures int NOT NULL DEFAULT 0; END");
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
        // Plantilla de email personalizable (todas opcionales para no romper maestros antiguos).
        // EmailSubject es nvarchar(1000) para soportar directivas Scriban condicionales
        // (la plantilla sugerida con {{ if ... }}{{ else }}{{ end }} supera 300 chars).
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'EmailSubject')
            BEGIN ALTER TABLE CampaignTemplates ADD EmailSubject nvarchar(1000) NULL; END");
        // Si la columna ya existe con un size menor, agrandarla. Idempotente.
        db.Database.ExecuteSqlRaw(@"
            IF EXISTS (
                SELECT 1 FROM sys.columns c
                JOIN sys.types t ON t.user_type_id = c.user_type_id
                WHERE c.object_id = OBJECT_ID('CampaignTemplates')
                  AND c.name = 'EmailSubject'
                  AND t.name = 'nvarchar'
                  AND c.max_length BETWEEN 0 AND 1998 -- nvarchar(N) → max_length = 2*N
            )
            BEGIN ALTER TABLE CampaignTemplates ALTER COLUMN EmailSubject nvarchar(1000) NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'EmailBodyHtml')
            BEGIN ALTER TABLE CampaignTemplates ADD EmailBodyHtml nvarchar(MAX) NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'EmailBodyText')
            BEGIN ALTER TABLE CampaignTemplates ADD EmailBodyText nvarchar(MAX) NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'EmailTemplateUpdatedAt')
            BEGIN ALTER TABLE CampaignTemplates ADD EmailTemplateUpdatedAt datetime2 NULL; END");
        // Layout adaptativo del correo (Fase A): umbral + mapeo del dataset.
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'UmbralCorporativo')
            BEGIN ALTER TABLE CampaignTemplates ADD UmbralCorporativo int NOT NULL DEFAULT 10; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'ItemsConfig')
            BEGIN ALTER TABLE CampaignTemplates ADD ItemsConfig nvarchar(MAX) NULL; END");
        // SampleDataJson — archivo modelo parseado para alimentar dropdowns y previews.
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'SampleDataJson')
            BEGIN ALTER TABLE CampaignTemplates ADD SampleDataJson nvarchar(MAX) NULL; END");
    }
    catch (Exception ex) { Console.WriteLine($"[Schema] CampaignTemplates columns: {ex.Message}"); }

    // ── Phase 1: outbound email tracking. Persistimos cada email enviado como
    //    un Message en su Conversation. Estos campos permiten distinguir el
    //    canal de cada mensaje individual y exponer subject/destinatario en
    //    el monitor (que hoy es WhatsApp-centric). Channel NULL = hereda de
    //    Conversation.Channel (compat con mensajes históricos).
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Messages') AND name = 'Channel')
            BEGIN ALTER TABLE Messages ADD Channel int NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Messages') AND name = 'Subject')
            BEGIN ALTER TABLE Messages ADD Subject nvarchar(1000) NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Messages') AND name = 'Recipient')
            BEGIN ALTER TABLE Messages ADD Recipient nvarchar(500) NULL; END");
    }
    catch (Exception ex) { Console.WriteLine($"[Schema] Messages columns: {ex.Message}"); }

    // ── Resincronización Conversation.Channel ──────────────────────────────
    // Conversaciones creadas con el modelo viejo (Channel = campaign.Channel)
    // pueden haber quedado mal clasificadas — por ejemplo Channel=WhatsApp en
    // conversaciones que en realidad solo tienen mensajes de Email. Reseteamos
    // Channel de cada conversación al canal del PRIMER mensaje de esa
    // conversación. Idempotente — solo afecta a las que están desalineadas.
    //
    // Conversations.Channel se guarda como string ('WhatsApp', 'Email', 'Sms')
    // por HasConversion<string>(). Messages.Channel se guarda como int (enum).
    // Por eso convertimos con CASE.
    try
    {
        db.Database.ExecuteSqlRaw(@"
            UPDATE c
            SET c.Channel = CASE sub.FirstChannel
                              WHEN 0 THEN 'WhatsApp'
                              WHEN 1 THEN 'Email'
                              WHEN 2 THEN 'Sms'
                              ELSE c.Channel
                            END
            FROM Conversations c
            INNER JOIN (
                SELECT m.ConversationId, m.Channel AS FirstChannel
                FROM Messages m
                INNER JOIN (
                    SELECT ConversationId, MIN(SentAt) AS FirstSentAt
                    FROM Messages
                    WHERE Channel IS NOT NULL
                    GROUP BY ConversationId
                ) f ON f.ConversationId = m.ConversationId AND f.FirstSentAt = m.SentAt
            ) sub ON sub.ConversationId = c.Id
            WHERE c.Channel <> CASE sub.FirstChannel
                                 WHEN 0 THEN 'WhatsApp'
                                 WHEN 1 THEN 'Email'
                                 WHEN 2 THEN 'Sms'
                                 ELSE c.Channel
                               END;");
    }
    catch (Exception ex) { Console.WriteLine($"[Schema] Conversation.Channel resync: {ex.Message}"); }

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
        // Logo del corredor — URL pública para insertarlo en el header del email.
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'LogoUrl')
            BEGIN ALTER TABLE Tenants ADD LogoUrl nvarchar(500) NULL; END");
        // Feature flag RAG por tenant — opt-in para usar retrieval de chunks
        // en vez de inyectar los PDFs enteros al prompt cada turno.
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'UseRagRetrieval')
            BEGIN ALTER TABLE Tenants ADD UseRagRetrieval bit NOT NULL DEFAULT 0; END");

        // Batching + Circuit Breaker (Phase 3) — anti-restricción de WhatsApp.
        // Cuántos contactos por batch, minutos de pausa entre batches y umbral
        // de fallos para auto-pausar la campaña + notificar al admin.
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'CampaignBatchSize')
            BEGIN ALTER TABLE Tenants ADD CampaignBatchSize int NOT NULL DEFAULT 15; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'CampaignBatchCoolDownMinutes')
            BEGIN ALTER TABLE Tenants ADD CampaignBatchCoolDownMinutes int NOT NULL DEFAULT 20; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'CampaignAutoPauseFailureRate')
            BEGIN ALTER TABLE Tenants ADD CampaignAutoPauseFailureRate decimal(5,2) NOT NULL DEFAULT 30.0; END");

        // Cool-down timestamp por campaña — el dispatcher salta esta campaña
        // hasta que ahora >= NextBatchAfterUtc.
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'NextBatchAfterUtc')
            BEGIN ALTER TABLE Campaigns ADD NextBatchAfterUtc datetime2 NULL; END");
    }
    catch (Exception ex) { Console.WriteLine($"[Schema] Tenants columns: {ex.Message}"); }

    // ── RAG: indexado de PDFs (CampaignTemplateDocuments + Chunks) ──────────
    // Día 1 del rollout RAG. CampaignTemplateDocuments existe; le agregamos columnas
    // de estado de indexado. La tabla Chunks la creamos desde cero si no existe
    // (no hay migración EF dedicada — self-healing schema mantiene paridad con la
    // entidad sin requerir dotnet ef en producción).
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplateDocuments') AND name = 'IndexedAt')
            BEGIN ALTER TABLE CampaignTemplateDocuments ADD IndexedAt datetime2 NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplateDocuments') AND name = 'IndexedTokenCount')
            BEGIN ALTER TABLE CampaignTemplateDocuments ADD IndexedTokenCount int NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplateDocuments') AND name = 'IndexingError')
            BEGIN ALTER TABLE CampaignTemplateDocuments ADD IndexingError nvarchar(500) NULL; END");
    }
    catch (Exception ex) { Console.WriteLine($"[Schema] CampaignTemplateDocuments RAG columns: {ex.Message}"); }

    try
    {
        // Tabla de chunks. Cascade delete vía Document. El embedding se guarda como
        // VARBINARY(MAX) — 1536 floats × 4 bytes para text-embedding-3-small.
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('CampaignTemplateDocumentChunks') AND type = 'U')
            BEGIN
                CREATE TABLE CampaignTemplateDocumentChunks (
                    Id                  uniqueidentifier NOT NULL PRIMARY KEY,
                    DocumentId          uniqueidentifier NOT NULL,
                    TenantId            uniqueidentifier NOT NULL,
                    CampaignTemplateId  uniqueidentifier NOT NULL,
                    PageNumber          int              NOT NULL,
                    ChunkIndex          int              NOT NULL,
                    Text                nvarchar(max)    NOT NULL,
                    TextHash            nvarchar(64)     NOT NULL,
                    Embedding           varbinary(max)   NOT NULL,
                    TokenCount          int              NOT NULL,
                    CreatedAt           datetime2        NOT NULL,
                    CONSTRAINT FK_DocChunks_Document
                        FOREIGN KEY (DocumentId)
                        REFERENCES CampaignTemplateDocuments(Id)
                        ON DELETE CASCADE
                );
                CREATE NONCLUSTERED INDEX IX_DocChunks_TenantTemplate
                    ON CampaignTemplateDocumentChunks (TenantId, CampaignTemplateId);
                CREATE NONCLUSTERED INDEX IX_DocChunks_Document
                    ON CampaignTemplateDocumentChunks (DocumentId);
                CREATE NONCLUSTERED INDEX IX_DocChunks_DocumentHash
                    ON CampaignTemplateDocumentChunks (DocumentId, TextHash);
            END");
    }
    catch (Exception ex) { Console.WriteLine($"[Schema] CampaignTemplateDocumentChunks: {ex.Message}"); }

    // ── SystemAuditLog — auditoría general de errores/eventos sistémicos ──
    // Cualquier error operacional (RAG, providers, blobs, etc.) que no encaje
    // en una entidad específica se guarda aquí. Tabla agnóstica del dominio:
    // se filtra por OccurredAtUtc, TenantId, Category.
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('SystemAuditLog') AND type = 'U')
            BEGIN
                CREATE TABLE SystemAuditLog (
                    Id                 uniqueidentifier NOT NULL PRIMARY KEY,
                    OccurredAtUtc      datetime2        NOT NULL,
                    TenantId           uniqueidentifier NULL,
                    Category           nvarchar(50)     NOT NULL,
                    Severity           nvarchar(20)     NOT NULL,
                    Message            nvarchar(1000)   NOT NULL,
                    StackTrace         nvarchar(max)    NULL,
                    RelatedEntityType  nvarchar(50)     NULL,
                    RelatedEntityId    uniqueidentifier NULL,
                    Context            nvarchar(max)    NULL
                );
                CREATE NONCLUSTERED INDEX IX_SystemAudit_Time
                    ON SystemAuditLog (OccurredAtUtc DESC);
                CREATE NONCLUSTERED INDEX IX_SystemAudit_TenantTime
                    ON SystemAuditLog (TenantId, OccurredAtUtc DESC);
                CREATE NONCLUSTERED INDEX IX_SystemAudit_CategoryTime
                    ON SystemAuditLog (Category, OccurredAtUtc DESC);
            END");
    }
    catch (Exception ex) { Console.WriteLine($"[Schema] SystemAuditLog: {ex.Message}"); }

    // ── Columnas adicionales en Campaigns y AppUsers ──────────────────────
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'LaunchedByUserPhone')
            BEGIN ALTER TABLE Campaigns ADD LaunchedByUserPhone nvarchar(20) NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AppUsers') AND name = 'NotifyPhone')
            BEGIN ALTER TABLE AppUsers ADD NotifyPhone nvarchar(20) NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AppUsers') AND name = 'BypassTwoFactor')
            BEGIN ALTER TABLE AppUsers ADD BypassTwoFactor bit NOT NULL DEFAULT 0; END");
    }
    catch (Exception ex) { Console.WriteLine($"[Schema] Campaigns/AppUsers columns: {ex.Message}"); }

    // ── ActionDefinitions.IsProcess (Fase 3 — acciones internas) ──────────
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ActionDefinitions') AND name = 'IsProcess')
            BEGIN ALTER TABLE ActionDefinitions ADD IsProcess bit NOT NULL DEFAULT 0; END");
    }
    catch (Exception ex) { Console.WriteLine($"[Schema] ActionDefinitions.IsProcess: {ex.Message}"); }

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

    // ── Fase 1: tablas ScheduledWebhookJobs (self-healing) ────────────────
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ScheduledWebhookJobs')
            BEGIN
                CREATE TABLE ScheduledWebhookJobs (
                    Id                  uniqueidentifier NOT NULL PRIMARY KEY DEFAULT NEWID(),
                    ActionDefinitionId  uniqueidentifier NOT NULL,
                    TriggerType         nvarchar(20) NOT NULL,
                    CronExpression      nvarchar(100) NULL,
                    TriggerEvent        nvarchar(100) NULL,
                    DelayMinutes        int NULL,
                    Scope               nvarchar(20) NOT NULL DEFAULT 'AllTenants',
                    IsActive            bit NOT NULL DEFAULT 1,
                    NextRunAt           datetime2 NULL,
                    LastRunAt           datetime2 NULL,
                    LastRunStatus       nvarchar(20) NULL,
                    LastRunSummary      nvarchar(1000) NULL,
                    ConsecutiveFailures int NOT NULL DEFAULT 0,
                    CreatedAt           datetime2 NOT NULL DEFAULT GETUTCDATE(),
                    UpdatedAt           datetime2 NOT NULL DEFAULT GETUTCDATE(),
                    CONSTRAINT FK_SWJ_Action FOREIGN KEY (ActionDefinitionId) REFERENCES ActionDefinitions(Id)
                );
                CREATE INDEX IX_SWJ_Active_Next ON ScheduledWebhookJobs (IsActive, NextRunAt);
                CREATE INDEX IX_SWJ_TriggerEvent ON ScheduledWebhookJobs (TriggerEvent);
            END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ScheduledWebhookJobExecutions')
            BEGIN
                CREATE TABLE ScheduledWebhookJobExecutions (
                    Id              uniqueidentifier NOT NULL PRIMARY KEY DEFAULT NEWID(),
                    JobId           uniqueidentifier NOT NULL,
                    StartedAt       datetime2 NOT NULL,
                    CompletedAt     datetime2 NOT NULL,
                    Status          nvarchar(20) NOT NULL,
                    TotalRecords    int NOT NULL DEFAULT 0,
                    SuccessCount    int NOT NULL DEFAULT 0,
                    FailureCount    int NOT NULL DEFAULT 0,
                    ErrorDetail     nvarchar(MAX) NULL,
                    TriggeredBy     nvarchar(50) NULL,
                    ContextId       nvarchar(200) NULL,
                    CONSTRAINT FK_SWJE_Job FOREIGN KEY (JobId) REFERENCES ScheduledWebhookJobs(Id) ON DELETE CASCADE
                );
                CREATE INDEX IX_SWJE_Job_Started ON ScheduledWebhookJobExecutions (JobId, StartedAt);
            END");
        // Detalle granular por sub-item (tenant/conversación/usuario) de cada ejecución.
        // Permite mostrar atribución por item en la UI cuando un job AllTenants falla
        // solo en algunos sub-elementos.
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ScheduledWebhookJobExecutionItems')
            BEGIN
                CREATE TABLE ScheduledWebhookJobExecutionItems (
                    Id              uniqueidentifier NOT NULL PRIMARY KEY DEFAULT NEWID(),
                    ExecutionId     uniqueidentifier NOT NULL,
                    TenantId        uniqueidentifier NULL,
                    ContextType     nvarchar(30) NOT NULL,
                    ContextId       nvarchar(200) NULL,
                    ContextLabel    nvarchar(300) NULL,
                    Status          nvarchar(20) NOT NULL,
                    ErrorMessage    nvarchar(MAX) NULL,
                    DurationMs      int NULL,
                    CreatedAt       datetime2 NOT NULL,
                    CONSTRAINT FK_SWJEI_Execution FOREIGN KEY (ExecutionId)
                        REFERENCES ScheduledWebhookJobExecutions(Id) ON DELETE CASCADE
                );
                CREATE INDEX IX_SWJEI_Execution_Status ON ScheduledWebhookJobExecutionItems (ExecutionId, Status);
                CREATE INDEX IX_SWJEI_Tenant ON ScheduledWebhookJobExecutionItems (TenantId);
            END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ActionDefinitions') AND name = 'ScheduleConfig')
            BEGIN ALTER TABLE ActionDefinitions ADD ScheduleConfig nvarchar(MAX) NULL; END");
    }
    catch (Exception ex) { Console.WriteLine($"[Schema] ScheduledWebhookJobs: {ex.Message}"); }

    // ── Fase 2: columnas de Follow-up + AutoClose (self-healing) ──────────
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'FollowUpMessagesJson')
            BEGIN ALTER TABLE CampaignTemplates ADD FollowUpMessagesJson nvarchar(MAX) NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'AutoCloseMessage')
            BEGIN ALTER TABLE CampaignTemplates ADD AutoCloseMessage nvarchar(1000) NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignContacts') AND name = 'FollowUpsSentJson')
            BEGIN ALTER TABLE CampaignContacts ADD FollowUpsSentJson nvarchar(200) NULL DEFAULT '[]'; END");
    }
    catch (Exception ex) { Console.WriteLine($"[Schema] Fase 2 columns: {ex.Message}"); }

    // ── InboundMessageQueueItems — cola durable de mensajes entrantes (Día 1) ──
    // Patrón self-healing igual que el resto del schema: idempotente con IF NOT EXISTS.
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'InboundMessageQueueItems')
            BEGIN
                CREATE TABLE InboundMessageQueueItems (
                    Id                  uniqueidentifier NOT NULL PRIMARY KEY DEFAULT NEWID(),
                    TenantId            uniqueidentifier NOT NULL,
                    FromPhone           varchar(30) NOT NULL,
                    Channel             varchar(20) NOT NULL DEFAULT 'WhatsApp',
                    WhatsAppLineId      uniqueidentifier NULL,
                    ClientName          nvarchar(200) NULL,
                    ExternalMessageId   varchar(100) NULL,
                    MediaUrl            nvarchar(MAX) NULL,
                    MediaType           varchar(30) NULL,
                    MessagesJson        nvarchar(MAX) NOT NULL DEFAULT '[]',
                    FirstReceivedAt     datetime2 NOT NULL,
                    LastReceivedAt      datetime2 NOT NULL,
                    BufferSeconds       int NOT NULL DEFAULT 12,
                    Status              varchar(20) NOT NULL DEFAULT 'Pending',
                    ClaimedAt           datetime2 NULL,
                    ClaimedBy           varchar(80) NULL,
                    StartedAt           datetime2 NULL,
                    CompletedAt         datetime2 NULL,
                    AttemptCount        int NOT NULL DEFAULT 0,
                    LastError           nvarchar(MAX) NULL,
                    LastErrorStep       varchar(50) NULL,
                    OutboundMessageId   uniqueidentifier NULL,
                    EscalatedToUserId   uniqueidentifier NULL,
                    EscalatedAt         datetime2 NULL
                );
                CREATE INDEX IX_IMQ_Status_LastAt ON InboundMessageQueueItems (Status, LastReceivedAt);
                CREATE INDEX IX_IMQ_Tenant_Phone_Status ON InboundMessageQueueItems (TenantId, FromPhone, Status);
                CREATE INDEX IX_IMQ_FirstReceived ON InboundMessageQueueItems (FirstReceivedAt);
            END");
    }
    catch (Exception ex) { Console.WriteLine($"[Schema] InboundMessageQueueItems: {ex.Message}"); }

    // ── InvalidWhatsAppNumbers — lista negra de números sin WhatsApp ──
    // Auto-aprende de UltraMsg /contacts/check (pre-envío) y de errores de
    // dispatch. Cross-tenant por default (TenantId NULL). Soft-delete via IsActive
    // para no perder histórico cuando un admin restaura un falso positivo.
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'InvalidWhatsAppNumbers')
            BEGIN
                CREATE TABLE InvalidWhatsAppNumbers (
                    Id                   uniqueidentifier NOT NULL PRIMARY KEY DEFAULT NEWID(),
                    PhoneNumber          varchar(30)  NOT NULL,
                    Reason               nvarchar(500) NOT NULL,
                    Source               varchar(40)  NOT NULL,
                    FirstDetectedAt      datetime2    NOT NULL DEFAULT SYSUTCDATETIME(),
                    LastCheckedAt        datetime2    NOT NULL DEFAULT SYSUTCDATETIME(),
                    OccurrenceCount      int          NOT NULL DEFAULT 1,
                    TenantId             uniqueidentifier NULL,
                    LastTenantId         uniqueidentifier NULL,
                    LastCampaignId       uniqueidentifier NULL,
                    Notes                nvarchar(1000) NULL,
                    IsActive             bit          NOT NULL DEFAULT 1,
                    CreatedByUserId      uniqueidentifier NULL,
                    DeactivatedByUserId  uniqueidentifier NULL,
                    DeactivatedAt        datetime2    NULL
                );
                -- Único por (phone, tenant) — permite entradas globales (TenantId=NULL)
                -- coexistiendo con entradas específicas por tenant. SQL Server permite
                -- múltiples NULL en un índice único por default (a diferencia de ANSI).
                CREATE UNIQUE INDEX UX_InvalidWANumber_Phone_Tenant
                    ON InvalidWhatsAppNumbers (PhoneNumber, TenantId);
                CREATE INDEX IX_InvalidWANumber_Active_LastCheck
                    ON InvalidWhatsAppNumbers (IsActive, LastCheckedAt DESC);
            END");
    }
    catch (Exception ex) { Console.WriteLine($"[Schema] InvalidWhatsAppNumbers: {ex.Message}"); }

    // ── Fase 3: columnas de etiquetado IA (self-healing) ──
    // Solo persistimos en Conversations el resultado de la clasificación.
    // El horario y el webhook de resultado son ScheduledWebhookJobs en /admin/scheduled-jobs.
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Conversations') AND name = 'LabelId')
            BEGIN ALTER TABLE Conversations ADD LabelId uniqueidentifier NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Conversations') AND name = 'LabeledAt')
            BEGIN ALTER TABLE Conversations ADD LabeledAt datetime2 NULL; END");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'LabelingSummarySentAt')
            BEGIN ALTER TABLE Campaigns ADD LabelingSummarySentAt datetime2 NULL; END");
        // Si quedaron residuos de iteraciones previas, los dropeamos para mantener el schema limpio.
        db.Database.ExecuteSqlRaw(@"
            IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'LabelingJobHourUtc')
            BEGIN ALTER TABLE CampaignTemplates DROP COLUMN LabelingJobHourUtc; END");
    }
    catch (Exception ex) { Console.WriteLine($"[Schema] Fase 3 columns: {ex.Message}"); }

    // ── Fase 2/3: seed ActionDefinitions globales (idempotente) ──
    try
    {
        var seedLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CampaignAutomationSeeder");
        AgentFlow.Infrastructure.ScheduledJobs.CampaignAutomationSeeder
            .SeedAsync(db, seedLogger).GetAwaiter().GetResult();
    }
    catch (Exception ex) { Console.WriteLine($"[Schema] CampaignAutomationSeeder: {ex.Message}"); }

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

// ── Recurring: red de seguridad para Claimed huérfanos ────────────────
// Hangfire dispara cada 5 min CampaignContactOrphanReleaseJob.ReleaseAllAsync()
// que libera contactos atascados en DispatchStatus=Claimed > 5 min globalmente.
// Defense in depth: aunque el fix in-dispatcher del Worker on-prem no esté
// activo (Worker viejo, caído, no actualizado), las campañas no se atascan
// porque esta sweep las recupera en menos de 5 min.
if (hangfireEnabled)
{
    try
    {
        RecurringJob.AddOrUpdate<AgentFlow.Infrastructure.Campaigns.CampaignContactOrphanReleaseJob>(
            recurringJobId: "campaign-orphan-release",
            methodCall: job => job.ReleaseAllAsync(CancellationToken.None),
            cronExpression: "*/5 * * * *",
            options: new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        Console.WriteLine("[Startup] Hangfire recurring 'campaign-orphan-release' programado cada 5 min.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] No se pudo registrar recurring 'campaign-orphan-release': {ex.Message}");
    }
}

app.Run();
