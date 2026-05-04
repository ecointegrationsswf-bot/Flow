using AgentFlow.Application.Modules.Campaigns;
using AgentFlow.Application.Modules.Campaigns.LaunchV2;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Storage;
using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

public record CampaignUploadRequest(string Name, Guid AgentId, DateTime? ScheduledAt);
public record LaunchV2Request(int WarmupDay = 0);
public record UploadFixedFormatRequest([Required] string Name, [Required] Guid AgentId, DateTime? ScheduledAt, Guid? CampaignTemplateId = null);

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
    // Devuelve el nombre completo del usuario autenticado (claim full_name),
    // con fallback a email y luego a "system".
    private string CurrentUser =>
        User.FindFirst("full_name")?.Value
        ?? User.FindFirst(JwtRegisteredClaimNames.Email)?.Value
        ?? User.Identity?.Name
        ?? "system";

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
            Status = c.Status.ToString(),
            c.LaunchedAt,
            c.CreatedByUserId,
            c.LaunchedByUserId,
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
    /// Lanza la campaña usando el flujo v2 (en proceso, sin n8n). Aplica
    /// dedup contra campañas activas del tenant y warm-up por día. El envío
    /// real lo realiza el CampaignWorker en el Worker Service.
    /// </summary>
    [HttpPost("{id:guid}/launch-v2")]
    public async Task<IActionResult> LaunchV2(Guid id, [FromBody] LaunchV2Request? body, CancellationToken ct)
    {
        var phone = User.FindFirst("phone")?.Value;
        var result = await mediator.Send(new LaunchCampaignV2Command(
            CampaignId: id,
            TenantId: tenantCtx.TenantId,
            LaunchedByUserId: CurrentUser,
            LaunchedByUserPhone: phone,
            WarmupDay: body?.WarmupDay ?? 0
        ), ct);

        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Lanza el envío de una campaña. Internamente delega al flujo v2 (CampaignWorker
    /// en el Worker Service) — el endpoint queda como alias por compatibilidad con
    /// clientes existentes. El parámetro <c>warmupDay</c> es opcional (default 0).
    /// </summary>
    [HttpPost("{id:guid}/start")]
    public async Task<IActionResult> StartCampaign(
        Guid id,
        [FromQuery] int warmupDay = 0,
        CancellationToken ct = default)
    {
        var phone = User.FindFirst("phone")?.Value;
        var result = await mediator.Send(new LaunchCampaignV2Command(
            CampaignId: id,
            TenantId: tenantCtx.TenantId,
            LaunchedByUserId: CurrentUser,
            LaunchedByUserPhone: phone,
            WarmupDay: warmupDay
        ), ct);

        return result.Success ? Ok(result) : BadRequest(result);
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
    /// Reactiva una campaña pausada. Solo marca <c>IsActive=true</c>; el CampaignWorker
    /// la recoge en el próximo tick (≤30s) si está en estado Running.
    /// </summary>
    [HttpPost("{id:guid}/resume")]
    public async Task<IActionResult> ResumeCampaign(Guid id, CancellationToken ct)
    {
        var campaign = await campaignRepo.GetByIdAsync(id, tenantCtx.TenantId, ct);
        if (campaign is null) return NotFound(new { error = "Campaña no encontrada." });

        campaign.IsActive = true;
        await campaignRepo.UpdateAsync(campaign, ct);

        return Ok(new { message = "Campaña reactivada. El Worker la recogerá en el próximo tick." });
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
            CurrentUser,
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
            CurrentUser,
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
            CurrentUser,
            req.ScheduledAt,
            req.CampaignTemplateId
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
    /// Lanza una campaña creada. Internamente delega al flujo v2 (CampaignWorker
    /// en el Worker Service); el frontend conserva la URL <c>/launch</c> para no
    /// requerir cambios en el cliente. Mantiene shape de respuesta compatible con
    /// el contrato anterior (campaignId, status, pendingContacts, launchedAt).
    /// </summary>
    [HttpPost("{id:guid}/launch")]
    public async Task<IActionResult> Launch(Guid id, CancellationToken ct)
    {
        var userIdStr = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                     ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        string? launcherPhone = null;
        if (Guid.TryParse(userIdStr, out var launcherId))
        {
            var launcher = await db.AppUsers.FindAsync([launcherId], ct);
            launcherPhone = launcher?.NotifyPhone;
        }

        var result = await mediator.Send(new LaunchCampaignV2Command(
            CampaignId: id,
            TenantId: tenantCtx.TenantId,
            LaunchedByUserId: CurrentUser,
            LaunchedByUserPhone: launcherPhone,
            WarmupDay: 0
        ), ct);

        if (!result.Success)
        {
            // 404 si la campaña no pertenece al tenant; 400 para el resto de fallos.
            if (string.Equals(result.Status, "NotFound", StringComparison.Ordinal))
                return NotFound(new { error = result.Error });
            return BadRequest(new { error = result.Error });
        }

        return Ok(new
        {
            campaignId      = result.CampaignId,
            status          = result.Status,
            pendingContacts = result.QueuedCount + result.DeferredCount,
            launchedAt      = result.LaunchedAt,
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

        // Si no viene mapping explícito, detectar automáticamente por nombre de columna
        if (mapping is null || mapping.Count == 0)
            mapping = AutoDetectMapping(headers);

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
            // Todo lo que no fue mapeado a un campo conocido va a ContactDataJson
            var extra = rowData
                .Where(kv => !mappedSourceCols.Contains(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            var pendingStr = GetMapped("pendingAmount");
            decimal? pendingAmount = decimal.TryParse(
                pendingStr?.Replace(",", "."),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var pa) ? pa : null;

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

    /// <summary>
    /// Detecta automáticamente qué columna del Excel corresponde a cada campo interno.
    /// Soporta nombres en español e inglés, con y sin tildes.
    /// El campo detectado es el nombre de la columna en el Excel; el valor es el campo interno.
    /// </summary>
    private static Dictionary<string, string> AutoDetectMapping(List<string> headers)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Alias por campo interno — orden de prioridad: primero el más específico
        var aliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["phone"] =
            [
                "celular", "telefono", "teléfono", "phone", "tel", "movil", "móvil",
                "whatsapp", "numero", "número", "cel", "numerodecell", "numerodecelular",
                "numerodecliente", "numerocliente"
            ],
            ["clientName"] =
            [
                "nombrecliente", "nombre_cliente", "nombre", "cliente", "name",
                "clientname", "razonsocial", "razón social", "nombredel cliente",
                "nombrecompleto", "nombresapellidos"
            ],
            ["email"] =
            [
                "email", "correo", "correoelectronico", "correo electronico",
                "correo_electronico", "e-mail", "emailcliente"
            ],
            ["policyNumber"] =
            [
                "poliza", "póliza", "numeropoliza", "númeropoliza", "numerodepoliza",
                "nopoliza", "policy", "policynumber", "numpoliza", "npoliza"
            ],
            ["insuranceCompany"] =
            [
                "aseguradora", "seguro", "compania", "compañia", "compañía",
                "insurance", "insurancecompany", "asegurado", "aseguradoracompania"
            ],
            ["pendingAmount"] =
            [
                "monto", "montodeuda", "deuda", "saldo", "balance", "amount",
                "pendingamount", "montoavencer", "montopendiente", "prima",
                "totaldeuda", "totalapagar"
            ],
        };

        foreach (var header in headers)
        {
            var normalized = header.ToLowerInvariant()
                .Replace(" ", "").Replace("_", "").Replace("-", "")
                .Replace("á","a").Replace("é","e").Replace("í","i")
                .Replace("ó","o").Replace("ú","u").Replace("ñ","n");

            foreach (var (field, aliasList) in aliases)
            {
                // Ya mapeamos este campo — no sobreescribir
                if (mapping.ContainsValue(field)) continue;

                var matched = aliasList.Any(alias =>
                {
                    var normAlias = alias.ToLowerInvariant()
                        .Replace(" ", "").Replace("_", "").Replace("-", "")
                        .Replace("á","a").Replace("é","e").Replace("í","i")
                        .Replace("ó","o").Replace("ú","u").Replace("ñ","n");
                    return normalized == normAlias || normalized.Contains(normAlias) || normAlias.Contains(normalized);
                });

                if (matched)
                {
                    mapping[header] = field;
                    break;
                }
            }
        }

        return mapping;
    }
}
