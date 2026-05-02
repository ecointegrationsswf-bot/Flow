using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AgentFlow.Infrastructure.ScheduledJobs;

/// <summary>
/// Executor del slug DOWNLOAD_DELINQUENCY_DATA.
///
/// Diseñado para correr en cron (ej: "0 8 * * *" → diario 8am Panamá).
/// Scope esperado: AllTenants.
///
/// Por cada tenant que tenga un ActionDelinquencyConfig activo para esta acción:
///   1. Determina la URL de descarga:
///      a. config.DownloadWebhookUrl (override por tenant), si está configurado, O
///      b. ActionDefinition.WebhookUrl (global), como fallback.
///   2. Hace HTTP GET/POST a esa URL con los headers configurados.
///   3. Pasa el body JSON al IDelinquencyProcessor, que:
///      - Extrae campos según FieldMappings
///      - Normaliza teléfonos
///      - Agrupa contactos por teléfono
///      - Crea campañas automáticamente si config.AutoCrearCampanas=true
///   4. Registra el resultado en el historial de ejecuciones de morosidad.
///
/// Idempotencia: si el mismo tenant se procesa dos veces el mismo día,
/// DelinquencyProcessor crea un nuevo DelinquencyExecution en cada llamada —
/// el operador puede ver el historial completo. Campañas duplicadas se evitan
/// aguas abajo si el agente valida por teléfono.
/// </summary>
public class DelinquencyDownloadExecutor(
    AgentFlowDbContext db,
    IDelinquencyProcessor processor,
    IHttpClientFactory httpFactory,
    IJobExecutionItemRepository itemRepo,
    ILogger<DelinquencyDownloadExecutor> log) : IScheduledJobExecutor
{
    public string Slug => "DOWNLOAD_DELINQUENCY_DATA";

    public async Task<JobRunResult> ExecuteAsync(
        ScheduledWebhookJob job, ScheduledJobContext ctx, CancellationToken ct)
    {
        // 1. Cargar la ActionDefinition (para URL global y headers del contrato)
        var actionDef = job.ActionDefinition
            ?? await db.ActionDefinitions
                .FirstOrDefaultAsync(a => a.Name == Slug && a.IsActive, ct);

        if (actionDef is null)
            return JobRunResult.Failed("ActionDefinition DOWNLOAD_DELINQUENCY_DATA no encontrada.");

        // 2. Buscar todos los tenants con configuración activa de morosidad para esta acción
        var configs = await db.ActionDelinquencyConfigs
            .Where(c => c.ActionDefinitionId == actionDef.Id && c.IsActive)
            .ToListAsync(ct);

        if (configs.Count == 0)
            return JobRunResult.Skipped("No hay configuraciones activas de morosidad para ningún tenant.");

        log.LogInformation("DelinquencyDownload: {N} tenants configurados.", configs.Count);

        // Resolver nombres de tenant en una sola query para tener labels legibles en la UI.
        var tenantIds = configs.Select(c => c.TenantId).ToList();
        var tenantNames = await db.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var success = 0;
        var failed  = 0;
        var errors  = new List<string>();
        var items   = new List<ScheduledWebhookJobExecutionItem>();

        foreach (var config in configs)
        {
            if (ct.IsCancellationRequested) break;

            var startedAt = DateTime.UtcNow;
            var tenantLabel = tenantNames.TryGetValue(config.TenantId, out var n) ? n : null;
            try
            {
                await ProcessTenantAsync(config, actionDef, job.Id, ctx.ExecutionId, ct);
                success++;
                log.LogInformation("DelinquencyDownload: tenant {TenantId} OK.", config.TenantId);

                if (ctx.ExecutionId is Guid execIdSuccess)
                {
                    items.Add(new ScheduledWebhookJobExecutionItem
                    {
                        ExecutionId  = execIdSuccess,
                        TenantId     = config.TenantId,
                        ContextType  = "Tenant",
                        ContextId    = config.TenantId.ToString(),
                        ContextLabel = tenantLabel,
                        Status       = "Success",
                        DurationMs   = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds,
                    });
                }
            }
            catch (Exception ex)
            {
                failed++;
                var msg = $"Tenant {config.TenantId}: {ex.Message}";
                if (errors.Count < 5) errors.Add(msg);
                log.LogError(ex, "DelinquencyDownload: error procesando tenant {TenantId}.", config.TenantId);

                if (ctx.ExecutionId is Guid execIdFail)
                {
                    items.Add(new ScheduledWebhookJobExecutionItem
                    {
                        ExecutionId  = execIdFail,
                        TenantId     = config.TenantId,
                        ContextType  = "Tenant",
                        ContextId    = config.TenantId.ToString(),
                        ContextLabel = tenantLabel,
                        Status       = "Failed",
                        ErrorMessage = ex.Message,
                        DurationMs   = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds,
                    });
                }
            }
        }

        // Persistir items (one-shot al final del run para minimizar I/O).
        try { await itemRepo.AddBatchAsync(items, ct); }
        catch (Exception ex) { log.LogWarning(ex, "DelinquencyDownload: no se pudieron persistir items de auditoría."); }

        var total   = configs.Count;
        var summary = $"Tenants={total} · OK={success} · Fallos={failed}";
        if (errors.Count > 0) summary += " · " + string.Join(" | ", errors);
        if (summary.Length > 800) summary = summary[..800];

        if (success == 0 && failed == 0) return JobRunResult.Skipped(summary);
        if (failed  == 0)                return JobRunResult.Success(success, summary);
        if (success == 0)                return JobRunResult.Failed("Todos los tenants fallaron.", summary);
        return JobRunResult.Partial(total, success, failed, summary);
    }

    // ── Por tenant ────────────────────────────────────────────────────────────

    private async Task ProcessTenantAsync(
        ActionDelinquencyConfig config,
        ActionDefinition actionDef,
        Guid jobId,
        Guid? jobExecutionId,
        CancellationToken ct)
    {
        // Resolver URL: override por tenant tiene prioridad
        var url = !string.IsNullOrWhiteSpace(config.DownloadWebhookUrl)
            ? config.DownloadWebhookUrl
            : actionDef.WebhookUrl;

        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException(
                $"No hay URL de descarga configurada para tenant {config.TenantId}. " +
                "Configura el endpoint en Admin → Morosidad → Configuración o en la acción global.");

        // Descargar JSON
        var json = await DownloadJsonAsync(url, config, ct);

        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("El endpoint devolvió una respuesta vacía.");

        log.LogDebug("DelinquencyDownload: tenant {TenantId} — {Bytes} bytes descargados.",
            config.TenantId, json.Length);

        // Procesar con el DelinquencyProcessor
        var executionId = await processor.ProcessAsync(
            tenantId:          config.TenantId,
            actionDefinitionId: config.ActionDefinitionId,
            jsonPayload:       json,
            scheduledJobId:    jobId,
            ct:                ct);

        log.LogInformation(
            "DelinquencyDownload: tenant {TenantId} — ejecución {ExecutionId} completada.",
            config.TenantId, executionId);
    }

    // ── Descarga HTTP ─────────────────────────────────────────────────────────

    private async Task<string> DownloadJsonAsync(
        string url,
        ActionDelinquencyConfig config,
        CancellationToken ct)
    {
        var client = httpFactory.CreateClient("delinquency");

        using var request = new HttpRequestMessage(
            config.DownloadWebhookMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)
                ? HttpMethod.Post
                : HttpMethod.Get,
            url);

        // Aplicar headers adicionales si están configurados
        if (!string.IsNullOrWhiteSpace(config.DownloadWebhookHeaders))
        {
            try
            {
                var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    config.DownloadWebhookHeaders);
                if (headers != null)
                {
                    foreach (var (key, value) in headers)
                        request.Headers.TryAddWithoutValidation(key, value);
                }
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "DownloadWebhookHeaders mal formado para tenant {TenantId}. Ignorando.",
                    config.TenantId);
            }
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(ct);
    }
}
