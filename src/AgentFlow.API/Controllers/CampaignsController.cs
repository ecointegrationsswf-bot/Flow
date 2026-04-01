using AgentFlow.Application.Modules.Campaigns;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Storage;
using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using AgentFlow.Domain.Enums;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

public record CampaignUploadRequest(string Name, Guid AgentId, DateTime? ScheduledAt);
public record UploadFixedFormatRequest([Required] string Name, [Required] Guid AgentId, DateTime? ScheduledAt);

public record CreateCampaignFromFileRequest(
    string Name,
    Guid AgentId,
    DateTime? ScheduledAt,
    string TempFilePath,
    Dictionary<string, string> ColumnMapping
);

[ApiController]
[Authorize]
[Route("api/campaigns")]
public class CampaignsController(
    IMediator mediator,
    ITenantContext tenantCtx,
    IExcelFileProcessor excelProcessor,
    IFixedFormatCampaignService fixedFormatService,
    IBlobStorageService blobStorage,
    ICampaignRepository campaignRepo,
    AgentFlowDbContext db,
    IConfiguration cfg,
    IHttpClientFactory httpClientFactory) : ControllerBase
{
    /// <summary>
    /// Lista todas las campañas del tenant autenticado.
    /// Muestra: nombre, estado, total contactos, fecha creación, etc.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var campaigns = await campaignRepo.ListByTenantAsync(tenantCtx.TenantId, ct);
        return Ok(campaigns.Select(c => new
        {
            c.Id,
            c.Name,
            Channel = c.Channel.ToString(),
            Trigger = c.Trigger.ToString(),
            c.IsActive,
            c.TotalContacts,
            c.ProcessedContacts,
            c.ScheduledAt,
            c.StartedAt,
            c.CompletedAt,
            c.CreatedAt,
            c.SourceFileName,
            // Progreso en porcentaje
            Progress = c.TotalContacts > 0
                ? Math.Round((double)c.ProcessedContacts / c.TotalContacts * 100, 1)
                : 0
        }));
    }

    /// <summary>
    /// Obtiene el detalle de una campaña con todos sus contactos.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var campaign = await campaignRepo.GetByIdAsync(id, tenantCtx.TenantId, ct);
        if (campaign is null) return NotFound(new { error = "Campaña no encontrada." });

        return Ok(new
        {
            campaign.Id,
            campaign.Name,
            Channel = campaign.Channel.ToString(),
            Trigger = campaign.Trigger.ToString(),
            campaign.IsActive,
            campaign.TotalContacts,
            campaign.ProcessedContacts,
            campaign.ScheduledAt,
            campaign.StartedAt,
            campaign.CompletedAt,
            campaign.CreatedAt,
            campaign.SourceFileName,
            Contacts = campaign.Contacts.Select(cc => new
            {
                cc.Id,
                cc.PhoneNumber,
                cc.ClientName,
                cc.PolicyNumber,
                cc.InsuranceCompany,
                cc.PendingAmount,
                cc.IsPhoneValid,
                cc.RetryCount,
                Result = cc.Result.ToString(),
                cc.LastContactAt
            })
        });
    }

    /// <summary>
    /// Lanza el envío de una campaña. Programa un job en Hangfire
    /// que enviará los mensajes de forma controlada (anti-ban).
    /// </summary>
    [HttpPost("{id:guid}/start")]
    public IActionResult StartCampaign(
        Guid id,
        [FromServices] Hangfire.IBackgroundJobClient? jobClient)
    {
        if (jobClient is null)
            return StatusCode(503, new { error = "Hangfire no disponible. El envío masivo requiere Hangfire." });

        // Programar el primer lote inmediatamente
        var jobId = jobClient.Enqueue<AgentFlow.Infrastructure.Campaigns.CampaignDispatcherJob>(
            job => job.ExecuteAsync(id, CancellationToken.None));

        return Ok(new
        {
            message = "Campaña iniciada. Los mensajes se enviarán de forma controlada.",
            hangfireJobId = jobId,
            campaignId = id
        });
    }

    /// <summary>
    /// Pausa una campaña activa (detiene el envío de mensajes).
    /// </summary>
    [HttpPost("{id:guid}/pause")]
    public async Task<IActionResult> PauseCampaign(Guid id, CancellationToken ct)
    {
        var campaign = await campaignRepo.GetByIdAsync(id, tenantCtx.TenantId, ct);
        if (campaign is null) return NotFound(new { error = "Campaña no encontrada." });

        campaign.IsActive = false;
        await campaignRepo.UpdateAsync(campaign, ct);

        return Ok(new { message = "Campaña pausada. Los envíos pendientes se detienen." });
    }

    /// <summary>
    /// Reactiva una campaña pausada.
    /// </summary>
    [HttpPost("{id:guid}/resume")]
    public IActionResult ResumeCampaign(
        Guid id,
        [FromServices] Hangfire.IBackgroundJobClient? jobClient)
    {
        if (jobClient is null)
            return StatusCode(503, new { error = "Hangfire no disponible." });

        // Reactivar: el job verificará si la campaña está activa
        var jobId = jobClient.Enqueue<AgentFlow.Infrastructure.Campaigns.CampaignDispatcherJob>(
            job => job.ExecuteAsync(id, CancellationToken.None));

        return Ok(new { message = "Campaña reactivada.", hangfireJobId = jobId });
    }

    [HttpPost("parse")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> ParseFile(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0)
            return BadRequest(new { error = "Archivo vacio." });

        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { error = "El archivo excede el limite de 10 MB." });

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (ext is not ".xlsx" and not ".xls" and not ".csv")
            return BadRequest(new { error = "Formato no soportado. Use Excel (.xlsx) o CSV." });

        var allowedMimeTypes = new[] {
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "application/vnd.ms-excel", "text/csv", "application/octet-stream"
        };
        if (!allowedMimeTypes.Contains(file.ContentType.ToLower()))
            return BadRequest(new { error = "Tipo de archivo no permitido." });

        using var stream = file.OpenReadStream();
        var result = excelProcessor.ParseExcel(stream, file.FileName);

        if (result.DetectedColumns.Count == 0)
            return BadRequest(new { error = "No se detectaron columnas en el archivo." });

        // Guardar archivo temporal en blob para paso 2
        var tempPath = $"temp/{tenantCtx.TenantId}/{Guid.NewGuid()}{ext}";
        using var uploadStream = file.OpenReadStream();
        await blobStorage.UploadAsync(tempPath, uploadStream, file.ContentType, ct);

        return Ok(new
        {
            result.DetectedColumns,
            result.PreviewRows,
            result.TotalRows,
            TempFilePath = tempPath
        });
    }

    [HttpPost("create")]
    public async Task<IActionResult> CreateFromFile([FromBody] CreateCampaignFromFileRequest req, CancellationToken ct)
    {
        var (fileStream, _) = await blobStorage.DownloadAsync(req.TempFilePath, ct);
        List<ContactRow> contactRows;
        using (fileStream)
        {
            contactRows = ParseWithMapping(fileStream, req.ColumnMapping);
        }

        if (contactRows.Count == 0)
            return BadRequest(new { error = "No se encontraron contactos validos." });

        var campaignId = await mediator.Send(new StartCampaignCommand(
            tenantCtx.TenantId,
            req.Name,
            req.AgentId,
            ChannelType.WhatsApp,
            CampaignTrigger.FileUpload,
            contactRows,
            User.Identity?.Name ?? "system",
            req.ScheduledAt
        ), ct);

        // Limpiar archivo temporal
        try { await blobStorage.DeleteAsync(req.TempFilePath, ct); } catch { }

        return Ok(new { campaignId, contactCount = contactRows.Count });
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadAndStart(
        [FromForm] CampaignUploadRequest req,
        IFormFile file,
        CancellationToken ct)
    {
        var contacts = new List<ContactRow>();
        var campaignId = await mediator.Send(new StartCampaignCommand(
            tenantCtx.TenantId,
            req.Name,
            req.AgentId,
            ChannelType.WhatsApp,
            CampaignTrigger.FileUpload,
            contacts,
            User.Identity?.Name ?? "system",
            req.ScheduledAt
        ), ct);

        return Ok(new { campaignId });
    }

    /// <summary>
    /// Parsea un Excel en formato fijo y devuelve una vista previa de los contactos
    /// consolidados sin crear la campaña. Permite al usuario revisar los datos
    /// antes de confirmar.
    /// </summary>
    [HttpPost("preview-fixed")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public IActionResult PreviewFixed(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Archivo vacío." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not ".xlsx" and not ".xls" and not ".csv")
            return BadRequest(new { error = "Formato no soportado. Use Excel (.xlsx/.xls) o CSV." });

        FixedFormatParseResult parsed;
        using (var stream = file.OpenReadStream())
            parsed = fixedFormatService.Parse(stream, file.FileName);

        if (parsed.Warnings.Count > 0 && parsed.Contacts.Count == 0)
            return BadRequest(new { error = parsed.Warnings[0], warnings = parsed.Warnings });

        var preview = parsed.Contacts.Select(c =>
        {
            // Extraer NombreCliente y KeyValue del primer registro del JSON
            string nombreCliente = c.ClientName ?? "";
            string keyValue = "";
            if (c.ContactDataJson is not null)
            {
                try
                {
                    var arr = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object>>>(c.ContactDataJson);
                    if (arr?.Count > 0)
                    {
                        keyValue = arr[0].GetValueOrDefault("KeyValue")?.ToString() ?? "";
                    }
                }
                catch { }
            }
            return new
            {
                phone = c.PhoneNumber,
                nombreCliente,
                keyValue,
                totalRegistros = GetRegistroCount(c.ContactDataJson),
                contactDataJson = c.ContactDataJson,
            };
        }).ToList();

        return Ok(new
        {
            contacts = preview,
            totalRowsRead = parsed.TotalRowsRead,
            extraColumns = parsed.ExtraColumns,
            warnings = parsed.Warnings,
        });
    }

    private static int GetRegistroCount(string? json)
    {
        if (json is null) return 1;
        try
        {
            var arr = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
            return arr.ValueKind == System.Text.Json.JsonValueKind.Array ? arr.GetArrayLength() : 1;
        }
        catch { return 1; }
    }

    /// <summary>
    /// Crea una campaña a partir de un Excel en formato fijo.
    ///
    /// Columnas requeridas: NombreCliente | Celular | CodigoPais | KeyValue
    /// Columnas adicionales variables: se capturan automáticamente en ContactDataJson.
    ///
    /// Múltiples filas del mismo número de teléfono se consolidan en un único
    /// contacto con un array "registros" en ContactDataJson.
    /// </summary>
    [HttpPost("upload-fixed")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadFixed(
        [FromForm] UploadFixedFormatRequest req,
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Archivo vacío." });

        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { error = "El archivo excede el límite de 10 MB." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not ".xlsx" and not ".xls" and not ".csv")
            return BadRequest(new { error = "Formato no soportado. Use Excel (.xlsx/.xls) o CSV." });

        FixedFormatParseResult parsed;
        using (var stream = file.OpenReadStream())
            parsed = fixedFormatService.Parse(stream, file.FileName);

        // Si hay errores de columnas faltantes los warnings lo indican
        if (parsed.Contacts.Count == 0)
            return BadRequest(new
            {
                error = "No se encontraron contactos válidos en el archivo.",
                warnings = parsed.Warnings
            });

        var campaignId = await mediator.Send(new StartCampaignCommand(
            tenantCtx.TenantId,
            req.Name,
            req.AgentId,
            ChannelType.WhatsApp,
            CampaignTrigger.FileUpload,
            parsed.Contacts,
            User.Identity?.Name ?? "system",
            req.ScheduledAt
        ), ct);

        return Ok(new
        {
            campaignId,
            contactCount = parsed.Contacts.Count,
            totalRowsRead = parsed.TotalRowsRead,
            extraColumns = parsed.ExtraColumns,
            warnings = parsed.Warnings
        });
    }

    /// <summary>
    /// Lanza una campaña creada: valida los requisitos, marca el estado
    /// y dispara el webhook de n8n para iniciar el procesamiento.
    /// </summary>
    [HttpPost("{id:guid}/launch")]
    public async Task<IActionResult> Launch(Guid id, CancellationToken ct)
    {
        var campaign = await db.Campaigns
            .Include(c => c.CampaignTemplate)
            .Include(c => c.Tenant)
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantCtx.TenantId, ct);

        if (campaign is null) return NotFound(new { error = "Campaña no encontrada." });
        if (campaign.Status == CampaignStatus.Running)
            return Conflict(new { error = "La campaña ya está en ejecución." });
        if (campaign.Status == CampaignStatus.Completed)
            return Conflict(new { error = "La campaña ya fue completada." });

        // Validar contactos válidos pendientes
        var pendingCount = await db.CampaignContacts
            .CountAsync(c => c.CampaignId == id
                          && c.IsPhoneValid
                          && c.DispatchStatus == DispatchStatus.Pending, ct);
        if (pendingCount == 0)
            return BadRequest(new { error = "La campaña no tiene contactos válidos pendientes." });

        // Validar maestro con prompt vinculado
        if (campaign.CampaignTemplate is null)
            return BadRequest(new { error = "La campaña no tiene un maestro de campaña asociado." });
        if (campaign.CampaignTemplate.PromptTemplateIds is null ||
            campaign.CampaignTemplate.PromptTemplateIds.Count == 0)
            return BadRequest(new { error = "El maestro de campaña no tiene un prompt vinculado." });

        // Validar WhatsApp del tenant
        if (string.IsNullOrEmpty(campaign.Tenant.WhatsAppInstanceId) ||
            string.IsNullOrEmpty(campaign.Tenant.WhatsAppApiToken))
            return BadRequest(new { error = "El tenant no tiene WhatsApp configurado (instanceId/token)." });

        // Marcar como Launching
        campaign.Status          = CampaignStatus.Launching;
        campaign.LaunchedAt      = DateTime.UtcNow;
        campaign.LaunchedByUserId = User.Identity?.Name ?? "system";
        await db.SaveChangesAsync(ct);

        // Disparar webhook n8n
        var webhookUrl = cfg["N8n:CampaignWebhookUrl"];
        if (!string.IsNullOrEmpty(webhookUrl))
        {
            try
            {
                var httpClient = httpClientFactory.CreateClient();
                var payload = JsonSerializer.Serialize(new
                {
                    campaignId    = campaign.Id,
                    tenantId      = campaign.TenantId,
                    totalContacts = pendingCount,
                    apiBaseUrl    = cfg["App:Url"] ?? "",
                    apiKey        = cfg["N8n:ApiKey"] ?? "",
                });
                await httpClient.PostAsync(webhookUrl,
                    new StringContent(payload, System.Text.Encoding.UTF8, "application/json"), ct);
                campaign.Status = CampaignStatus.Running;
            }
            catch (Exception ex)
            {
                campaign.Status = CampaignStatus.Failed;
                await db.SaveChangesAsync(ct);
                return StatusCode(502, new { error = $"No se pudo disparar n8n: {ex.Message}" });
            }
        }
        else
        {
            campaign.Status = CampaignStatus.Running; // dev: sin n8n configurado
        }

        await db.SaveChangesAsync(ct);
        return Ok(new
        {
            campaignId      = campaign.Id,
            status          = campaign.Status.ToString(),
            pendingContacts = pendingCount,
            launchedAt      = campaign.LaunchedAt,
        });
    }

    private static List<ContactRow> ParseWithMapping(Stream stream, Dictionary<string, string> mapping)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
        var ws = workbook.Worksheets.First();
        var range = ws.RangeUsed();
        if (range is null) return [];

        var lastRow = range.LastRow().RowNumber();
        var lastCol = range.LastColumn().ColumnNumber();

        var headers = new List<string>();
        for (var col = 1; col <= lastCol; col++)
            headers.Add(ws.Cell(1, col).GetString().Trim());

        var contacts = new List<ContactRow>();
        for (var row = 2; row <= lastRow; row++)
        {
            var rowData = new Dictionary<string, string>();
            for (var col = 0; col < headers.Count; col++)
                rowData[headers[col]] = ws.Cell(row, col + 1).GetString().Trim();

            string GetMapped(string field) =>
                mapping.FirstOrDefault(m => m.Value == field).Key is { } sourceCol
                    ? rowData.GetValueOrDefault(sourceCol, "")
                    : "";

            var phone = GetMapped("phone");
            if (string.IsNullOrWhiteSpace(phone)) continue;

            var mappedSourceCols = mapping.Keys.ToHashSet();
            var extra = rowData
                .Where(kv => !mappedSourceCols.Contains(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            var pendingStr = GetMapped("pendingAmount");
            decimal? pendingAmount = decimal.TryParse(pendingStr, out var pa) ? pa : null;

            contacts.Add(new ContactRow(
                PhoneNumber: phone,
                Email: GetMapped("email"),
                ClientName: GetMapped("clientName"),
                PolicyNumber: GetMapped("policyNumber"),
                InsuranceCompany: GetMapped("insuranceCompany"),
                PendingAmount: pendingAmount,
                Extra: extra
            ));
        }

        return contacts;
    }
}
