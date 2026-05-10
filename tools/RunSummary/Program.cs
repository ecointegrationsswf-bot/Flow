using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Domain.Webhooks;
using AgentFlow.Infrastructure.AI;
using AgentFlow.Infrastructure.Persistence;
using AgentFlow.Infrastructure.ScheduledJobs;
using AgentFlow.Infrastructure.Session;
using AgentFlow.Infrastructure.Storage;
using Anthropic.SDK;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// One-shot runner: invoca SendLabelingSummaryExecutor directamente contra la
// BD productiva, con el código actualizado (fix cae958e). NO levanta el
// BackgroundService completo, así no compite con el worker on-prem por
// claim de jobs ni dispara otros ticks paralelos.

var settings = new HostApplicationBuilderSettings
{
    Args = args,
    EnvironmentName = Environments.Production,
};
var builder = Host.CreateApplicationBuilder(settings);

// Cargar appsettings.json del Worker original (mismas connection strings).
builder.Configuration
    .SetBasePath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "AgentFlow.Worker"))
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables();

var cfg = builder.Configuration;

// ── DI mínimo (mismo patrón que Worker/Program.cs) ─────────────
builder.Services.AddDbContext<AgentFlowDbContext>(o =>
    o.UseSqlServer(cfg.GetConnectionString("DefaultConnection"))
     .ConfigureWarnings(w => w.Ignore(
         Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

var anthropicKey = cfg["Anthropic:ApiKey"];
var hasGlobalKey = !string.IsNullOrEmpty(anthropicKey) && anthropicKey != "YOUR_ANTHROPIC_API_KEY";
builder.Services.AddSingleton(new AnthropicClient(hasGlobalKey ? anthropicKey! : "no-global-key"));
builder.Services.AddSingleton(new AnthropicSettings(hasGlobalKey));

var emailProvider = cfg["Email:Provider"] ?? "SendGrid";
if (string.Equals(emailProvider, "Smtp", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<AgentFlow.Infrastructure.Email.IEmailService,
        AgentFlow.Infrastructure.Email.SmtpEmailService>();
    Console.WriteLine($"[RunSummary] Email provider: SMTP ({cfg["Smtp:Host"]}:{cfg["Smtp:Port"] ?? "587"}).");
}
else
{
    builder.Services.AddSingleton<AgentFlow.Infrastructure.Email.IEmailService,
        AgentFlow.Infrastructure.Email.SendGridEmailService>();
    Console.WriteLine("[RunSummary] Email provider: SendGrid.");
}
builder.Services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();
builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddLogging(b => b.AddConsole());

// Repos requeridos por JobExecutionAuditor + executor
builder.Services.AddScoped<IJobExecutionRepository,
    AgentFlow.Infrastructure.Persistence.Repositories.JobExecutionRepository>();
builder.Services.AddScoped<IJobExecutionItemRepository,
    AgentFlow.Infrastructure.Persistence.Repositories.JobExecutionItemRepository>();
builder.Services.AddScoped<JobExecutionAuditor>();
builder.Services.AddScoped<SendLabelingSummaryExecutor>();

var host = builder.Build();
using var scope = host.Services.CreateScope();
var sp = scope.ServiceProvider;
var db = sp.GetRequiredService<AgentFlowDbContext>();
var executor = sp.GetRequiredService<SendLabelingSummaryExecutor>();
var execRepo = sp.GetRequiredService<IJobExecutionRepository>();

// JobId esperado como primer argumento posicional o desde env var RUNSUMMARY_JOBID.
// Sin valor por defecto (no asumir un tenant/job específico).
var jobIdStr = args.FirstOrDefault()
               ?? Environment.GetEnvironmentVariable("RUNSUMMARY_JOBID");
if (string.IsNullOrWhiteSpace(jobIdStr))
{
    Console.WriteLine("Uso: dotnet run -- <jobId>");
    Console.WriteLine("  o:  RUNSUMMARY_JOBID=<jobId> dotnet run");
    Console.WriteLine();
    Console.WriteLine("Para obtener el JobId del SEND_LABELING_SUMMARY de un tenant:");
    Console.WriteLine("  SELECT j.Id FROM ScheduledWebhookJobs j");
    Console.WriteLine("  INNER JOIN ActionDefinitions ad ON ad.Id = j.ActionDefinitionId");
    Console.WriteLine("  WHERE ad.Name = 'SEND_LABELING_SUMMARY'");
    Console.WriteLine("    AND ad.TenantId = '<TenantId>';");
    return 1;
}
if (!Guid.TryParse(jobIdStr, out var jobId))
{
    Console.WriteLine($"ERROR: '{jobIdStr}' no es un GUID válido.");
    return 1;
}

var job = await db.Set<ScheduledWebhookJob>()
    .FirstOrDefaultAsync(j => j.Id == jobId);

if (job is null)
{
    Console.WriteLine($"ERROR: Job {jobIdStr} no encontrado.");
    return 1;
}

Console.WriteLine($"=========================================================");
Console.WriteLine($"  RunSummary tool — invocación directa de executor");
Console.WriteLine($"=========================================================");
Console.WriteLine($"  Job        : {job.Id}");
Console.WriteLine($"  IsActive   : {job.IsActive}");
Console.WriteLine($"  LastRun    : {job.LastRunAt:yyyy-MM-dd HH:mm:ss} UTC");
Console.WriteLine($"  LastStatus : {job.LastRunStatus}");
Console.WriteLine();

// Crear ejecución pending para que el auditor la enlace correctamente.
var executionId = await execRepo.InsertPendingAsync(
    job.Id, DateTime.UtcNow, "Manual:RunSummary-tool", contextId: null, default);

Console.WriteLine($"  ExecutionId: {executionId}");
Console.WriteLine($"  Arrancando ExecuteAsync()...");
Console.WriteLine();

var ctx = new ScheduledJobContext(
    TriggeredBy: "Manual:RunSummary-tool",
    ContextId: null,
    RunStartedAt: DateTime.UtcNow,
    ExecutionId: executionId);

var sw = System.Diagnostics.Stopwatch.StartNew();
var result = await executor.ExecuteAsync(job, ctx, default);
sw.Stop();

Console.WriteLine();
Console.WriteLine($"=========================================================");
Console.WriteLine($"  Resultado");
Console.WriteLine($"=========================================================");
Console.WriteLine($"  Status       : {result.Status}");
Console.WriteLine($"  TotalRecords : {result.TotalRecords}");
Console.WriteLine($"  Success      : {result.SuccessCount}");
Console.WriteLine($"  Failed       : {result.FailureCount}");
Console.WriteLine($"  Summary      : {result.Summary}");
Console.WriteLine($"  ErrorDetail  : {result.ErrorDetail}");
Console.WriteLine($"  Duracion     : {sw.ElapsedMilliseconds}ms");
Console.WriteLine();

// Persistir resultado en la ejecución
await execRepo.UpdateStatusAsync(
    executionId,
    completedAt: DateTime.UtcNow,
    status: result.Status,
    totalRecords: result.TotalRecords,
    successCount: result.SuccessCount,
    failureCount: result.FailureCount,
    errorDetail: result.ErrorDetail,
    ct: default);

return 0;
