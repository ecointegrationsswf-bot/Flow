using AgentFlow.Domain.Interfaces;
using AgentFlow.Domain.Webhooks;
using AgentFlow.Infrastructure.AI;
using AgentFlow.Infrastructure.Persistence;
using AgentFlow.Infrastructure.Session;
using AgentFlow.Infrastructure.Storage;
using Anthropic.SDK;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

// AgentFlow.Worker — proceso independiente que ejecuta el ScheduledWebhookWorker
// y todos sus IScheduledJobExecutor sin levantar el pipeline HTTP del API.
// Comparte appsettings con el API (mismo SQL, Redis, Anthropic, SendGrid, Blob).
//
// Despliegue recomendado: en un host distinto al API. Si se despliega junto al
// API, asegurarse de QUITAR la línea AddHostedService<ScheduledWebhookWorker>
// del API para evitar correr el worker dos veces (la concurrencia optimista
// MarkRunningAsync evita ejecuciones simultáneas, pero duplica carga BD/CPU).
var builder = Host.CreateApplicationBuilder(args);
var cfg = builder.Configuration;

// ── Windows Service (no-op en Linux/macOS) ───────────────
// Cuando se ejecuta bajo el SCM (Service Control Manager) de Windows, esta
// llamada cambia el ContentRoot al directorio del .exe en lugar del cwd y
// hace que el host responda a Stop/Start del servicio. En consola es no-op.
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "AgentFlow Worker";
});

// ── Persistencia ──────────────────────────────────────────
builder.Services.AddDbContext<AgentFlowDbContext>(o =>
    o.UseSqlServer(cfg.GetConnectionString("DefaultConnection"),
        sql => sql.MigrationsAssembly("AgentFlow.Infrastructure"))
     .ConfigureWarnings(w => w.Ignore(
         Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

// ── Redis (opcional) ─────────────────────────────────────
var redisConn = cfg.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(redisConn))
{
    try
    {
        var redis = ConnectionMultiplexer.Connect(redisConn + ",abortConnect=false,connectTimeout=3000");
        builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
        builder.Services.AddScoped<ISessionStore, RedisSessionStore>();
        builder.Services.AddScoped<IMessageBufferStore,
            AgentFlow.Infrastructure.Messaging.RedisMessageBufferStore>();
        Console.WriteLine("[Worker] Redis conectado.");
    }
    catch
    {
        Console.WriteLine("[Worker] Redis no disponible. Usando InMemorySessionStore.");
        builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();
        builder.Services.AddSingleton<IMessageBufferStore,
            AgentFlow.Infrastructure.Messaging.InMemoryMessageBufferStore>();
    }
}
else
{
    builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();
    builder.Services.AddSingleton<IMessageBufferStore,
        AgentFlow.Infrastructure.Messaging.InMemoryMessageBufferStore>();
}

// ── Anthropic ────────────────────────────────────────────
var anthropicKey = cfg["Anthropic:ApiKey"];
var hasGlobalKey = !string.IsNullOrEmpty(anthropicKey) && anthropicKey != "YOUR_ANTHROPIC_API_KEY";
builder.Services.AddSingleton(new AnthropicClient(hasGlobalKey ? anthropicKey! : "no-global-key"));
builder.Services.AddSingleton(new AnthropicSettings(hasGlobalKey));
Console.WriteLine(hasGlobalKey
    ? "[Worker] Anthropic con key global."
    : "[Worker] Anthropic: usando API key por tenant.");

// ── Email + Storage ──────────────────────────────────────
builder.Services.AddSingleton<AgentFlow.Infrastructure.Email.IEmailService,
    AgentFlow.Infrastructure.Email.SendGridEmailService>();
builder.Services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();

// ── HTTP + Cache ─────────────────────────────────────────
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();

// ── Webhook Contract System ──────────────────────────────
builder.Services.AddScoped<ISystemContextBuilder,
    AgentFlow.Infrastructure.Webhooks.SystemContextBuilder>();
builder.Services.AddSingleton<IPayloadBuilder,
    AgentFlow.Infrastructure.Webhooks.PayloadBuilder>();
builder.Services.AddScoped<IHttpDispatcher,
    AgentFlow.Infrastructure.Webhooks.HttpDispatcher>();
builder.Services.AddScoped<IOutputInterpreter,
    AgentFlow.Infrastructure.Webhooks.OutputInterpreter>();
builder.Services.AddScoped<AgentFlow.Infrastructure.Webhooks.ActionConfigReader>();
builder.Services.AddScoped<IActionExecutorService,
    AgentFlow.Infrastructure.Webhooks.ActionExecutorService>();
builder.Services.AddScoped<IActionPromptBuilder,
    AgentFlow.Infrastructure.Webhooks.ActionPromptBuilder>();

// ── Scheduled Jobs (CORE del worker) ─────────────────────
builder.Services.AddScoped<IScheduledJobRepository,
    AgentFlow.Infrastructure.Persistence.Repositories.ScheduledJobRepository>();
builder.Services.AddScoped<IJobExecutionRepository,
    AgentFlow.Infrastructure.Persistence.Repositories.JobExecutionRepository>();
builder.Services.AddScoped<IJobExecutionItemRepository,
    AgentFlow.Infrastructure.Persistence.Repositories.JobExecutionItemRepository>();
builder.Services.AddScoped<IWebhookEventDispatcher,
    AgentFlow.Infrastructure.ScheduledJobs.WebhookEventDispatcher>();

// Canal — necesario para OutputInterpreter, FollowUpExecutor y CampaignAutoCloseExecutor
builder.Services.AddScoped<IChannelProviderFactory,
    AgentFlow.Infrastructure.Channels.ChannelProviderFactory>();

// Morosidad
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.IDelinquencyProcessor,
    AgentFlow.Infrastructure.Morosidad.DelinquencyProcessor>();
builder.Services.AddScoped<AgentFlow.Domain.Interfaces.ICampaignLauncher,
    AgentFlow.Infrastructure.Campaigns.CampaignLauncher>();

// ConversationNotifier — no-op en el Worker (sin SignalR Hub).
builder.Services.AddSingleton<AgentFlow.Domain.Interfaces.IConversationNotifier,
    AgentFlow.Infrastructure.ScheduledJobs.NoOpConversationNotifier>();

// Executor genérico (slug "*") + executors específicos.
builder.Services.AddScoped<IScheduledJobExecutor,
    AgentFlow.Infrastructure.ScheduledJobs.DefaultWebhookExecutor>();
builder.Services.AddScoped<IScheduledJobExecutor,
    AgentFlow.Infrastructure.ScheduledJobs.FollowUpExecutor>();
builder.Services.AddScoped<IScheduledJobExecutor,
    AgentFlow.Infrastructure.ScheduledJobs.CampaignAutoCloseExecutor>();
builder.Services.AddScoped<IScheduledJobExecutor,
    AgentFlow.Infrastructure.ScheduledJobs.ConversationLabelingJob>();
builder.Services.AddScoped<IScheduledJobExecutor,
    AgentFlow.Infrastructure.ScheduledJobs.SendLabelingSummaryExecutor>();
builder.Services.AddScoped<IScheduledJobExecutor,
    AgentFlow.Infrastructure.ScheduledJobs.NotifyGestionBatchExecutor>();
// Slug DOWNLOAD_DELINQUENCY_DATA
builder.Services.AddScoped<IScheduledJobExecutor,
    AgentFlow.Infrastructure.ScheduledJobs.DelinquencyDownloadExecutor>();

// BackgroundService: tick cada 60s.
builder.Services.AddHostedService<
    AgentFlow.Infrastructure.ScheduledJobs.ScheduledWebhookWorker>();

// ── Campaign Dispatcher v2 (orquestación de campañas en proceso, sin n8n) ─
// El CampaignDispatcherService se reusa con cambios v2 (Queued + claim atómico).
// El CampaignWorker es el BackgroundService que tick cada 30s y procesa por tenant.
builder.Services.AddScoped<AgentFlow.Infrastructure.Campaigns.CampaignDispatcherService>();
builder.Services.AddHostedService<
    AgentFlow.Worker.Campaigns.Orchestration.CampaignWorker>();

// ── Run ──────────────────────────────────────────────────
var host = builder.Build();
Console.WriteLine("[Worker] AgentFlow.Worker arrancando…");
await host.RunAsync();
