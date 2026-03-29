using AgentFlow.Application.Modules.Campaigns;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Storage;
using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.API.Controllers;

public record CampaignUploadRequest(string Name, Guid AgentId, DateTime? ScheduledAt);

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
    IBlobStorageService blobStorage,
    ICampaignRepository campaignRepo) : ControllerBase
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
