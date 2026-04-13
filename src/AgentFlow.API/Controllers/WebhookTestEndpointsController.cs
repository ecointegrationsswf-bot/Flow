using System.Globalization;
using System.Text;
using AgentFlow.Infrastructure.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

/// <summary>
/// Endpoints de prueba para el Webhook Contract System.
///
/// Estos endpoints son públicos (sin auth) porque están diseñados para ser
/// llamados por el HttpDispatcher del propio sistema como si fueran webhooks
/// externos configurados en un CampaignTemplate del tenant.
///
/// Existen dos endpoints:
///   - POST /api/webhook-test-endpoints/generate-pdf
///     Recibe un JSON con el teléfono del contacto, genera un PDF mínimo
///     (embedido con ese teléfono) y devuelve el contenido en base64 más un
///     mensaje amigable. Pensado para probar outputAction = send_whatsapp_media.
///
///   - POST /api/webhook-test-endpoints/send-summary-email
///     Recibe un JSON con destinatario, asunto y resumen, envía el correo vía
///     SendGrid y devuelve un mensaje de confirmación. Pensado para probar
///     outputAction = send_to_agent / inject_context.
/// </summary>
[ApiController]
[Route("api/webhook-test-endpoints")]
[AllowAnonymous]
public class WebhookTestEndpointsController(
    IEmailService emailService,
    AgentFlow.Domain.Webhooks.IActionPromptBuilder actionPromptBuilder) : ControllerBase
{
    /// <summary>DIAGNÓSTICO TEMPORAL — probar qué produce el ActionPromptBuilder.</summary>
    [HttpGet("diag-catalog")]
    public async Task<IActionResult> DiagCatalog(
        [FromQuery] Guid tenantId, [FromQuery] Guid templateId, CancellationToken ct)
    {
        // Diagnóstico manual para ver qué pasa paso a paso
        var dbCtx = HttpContext.RequestServices.GetRequiredService<AgentFlow.Infrastructure.Persistence.AgentFlowDbContext>();

        var tenant = await dbCtx.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.WebhookContractEnabled })
            .FirstOrDefaultAsync(ct);

        var template = await dbCtx.CampaignTemplates
            .Where(t => t.Id == templateId && t.TenantId == tenantId)
            .Select(t => new { t.ActionConfigs, t.ActionIds })
            .FirstOrDefaultAsync(ct);

        var allIds = new HashSet<Guid>();
        if (template?.ActionIds is { Count: > 0 } ids)
            foreach (var id in ids) allIds.Add(id);

        var defs = await dbCtx.ActionDefinitions
            .Where(a => a.TenantId == tenantId && allIds.Contains(a.Id) && a.IsActive)
            .Select(a => new { a.Id, a.Name, HasContract = a.DefaultWebhookContract != null, HasTrigger = a.DefaultTriggerConfig != null })
            .ToListAsync(ct);

        var catalog = await actionPromptBuilder.GetCatalogAsync(templateId, tenantId, ct);

        return Ok(new
        {
            step1_webhookEnabled = tenant?.WebhookContractEnabled,
            step2_templateFound = template != null,
            step2_actionIdsCount = allIds.Count,
            step2_actionIds = allIds.Select(x => x.ToString()).ToList(),
            step3_defsFound = defs.Select(d => new { d.Id, d.Name, d.HasContract, d.HasTrigger }).ToList(),
            catalog_isActive = catalog.IsActive,
            catalog_slugCount = catalog.BySlug.Count,
            catalog_slugs = catalog.BySlug.Keys.ToList(),
            catalog_blockLength = catalog.Block.Length,
        });
    }
    /// <summary>DIAGNÓSTICO TEMPORAL — probar upload blob + envío WhatsApp media.</summary>
    [HttpGet("diag-media")]
    public async Task<IActionResult> DiagMedia(
        [FromQuery] Guid tenantId, [FromQuery] string phone, CancellationToken ct)
    {
        var logs = new List<string>();
        try
        {
            // 1. Generar un mini PDF de prueba
            var pdfBytes = MinimalPdfBuilder.BuildSimpleDocument(new[] { "Test media", $"Phone: {phone}", $"Time: {DateTime.UtcNow}" });
            logs.Add($"PDF generado: {pdfBytes.Length} bytes");

            // 2. Subir a Azure Blob
            var blobSvc = HttpContext.RequestServices.GetRequiredService<AgentFlow.Infrastructure.Storage.IBlobStorageService>();
            var filename = $"diag-test/{Guid.NewGuid()}.pdf";
            var url = await blobSvc.UploadWhatsAppMediaAsync(filename, pdfBytes, "application/pdf", ct);
            logs.Add($"Blob upload OK: {url}");

            // 3. Enviar por WhatsApp
            var channelFactory = HttpContext.RequestServices.GetRequiredService<AgentFlow.Domain.Interfaces.IChannelProviderFactory>();
            var provider = await channelFactory.GetProviderAsync(tenantId, ct);
            if (provider is null)
            {
                logs.Add("ERROR: No se encontró provider para el tenant");
                return Ok(new { success = false, logs });
            }
            logs.Add("Provider encontrado");

            var result = await provider.SendMessageAsync(new AgentFlow.Domain.Interfaces.SendMessageRequest(
                To: phone, Body: "Documento de prueba ATP", MediaUrl: url,
                MediaType: "document", Filename: "test-diag.pdf"), ct);

            logs.Add($"SendMessage: success={result.Success}, error={result.Error}, id={result.ExternalMessageId}");
            return Ok(new { success = result.Success, logs, blobUrl = url });
        }
        catch (Exception ex)
        {
            logs.Add($"EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException is not null)
                logs.Add($"INNER: {ex.InnerException.Message}");
            return Ok(new { success = false, logs });
        }
    }

    // ────────────────────────────────────────────────────────────────
    // 1) GENERATE PDF
    // ────────────────────────────────────────────────────────────────

    public record GeneratePdfRequest(
        string? Phone,
        string? ClientName,
        string? Title,
        string? Notes
    );

    public record GeneratePdfResponse(
        bool Success,
        string FileName,
        string ContentType,
        string FileBase64,
        long SizeBytes,
        string Message,
        DateTime GeneratedAtUtc
    );

    [HttpPost("generate-pdf")]
    public IActionResult GeneratePdf([FromBody] GeneratePdfRequest req)
    {
        var phone = (req.Phone ?? "").Trim();
        if (phone.Length == 0)
        {
            return BadRequest(new { success = false, error = "El campo 'phone' es requerido." });
        }

        var clientName = string.IsNullOrWhiteSpace(req.ClientName) ? "Cliente" : req.ClientName!.Trim();
        var title = string.IsNullOrWhiteSpace(req.Title) ? "Comprobante de prueba" : req.Title!.Trim();
        var notes = string.IsNullOrWhiteSpace(req.Notes) ? "Este es un documento generado automáticamente por AgentFlow para validar el flujo de webhooks." : req.Notes!.Trim();
        var nowPanama = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "SA Pacific Standard Time" : "America/Panama"));

        var lines = new List<string>
        {
            title,
            "",
            $"Cliente: {clientName}",
            $"Telefono: {phone}",
            $"Fecha: {nowPanama:dd/MM/yyyy HH:mm} (Panama)",
            "",
            "Observaciones:",
            notes,
            "",
            "--- AgentFlow / TalkIA ---"
        };

        var pdfBytes = MinimalPdfBuilder.BuildSimpleDocument(lines);
        var base64 = Convert.ToBase64String(pdfBytes);
        var fileName = $"prueba_{SanitizeForFile(phone)}_{nowPanama:yyyyMMdd_HHmmss}.pdf";

        return Ok(new GeneratePdfResponse(
            Success: true,
            FileName: fileName,
            ContentType: "application/pdf",
            FileBase64: base64,
            SizeBytes: pdfBytes.LongLength,
            Message: $"PDF generado correctamente para {clientName} ({phone}).",
            GeneratedAtUtc: DateTime.UtcNow
        ));
    }

    // ────────────────────────────────────────────────────────────────
    // 2) SEND SUMMARY EMAIL
    // ────────────────────────────────────────────────────────────────

    public record SendSummaryEmailRequest(
        string? ToEmail,
        string? Subject,
        string? Summary,
        string? ClientName,
        string? Phone
    );

    public record SendSummaryEmailResponse(
        bool Success,
        string Message,
        string SentTo,
        DateTime SentAtUtc
    );

    [HttpPost("send-summary-email")]
    public async Task<IActionResult> SendSummaryEmail([FromBody] SendSummaryEmailRequest req, CancellationToken ct)
    {
        var toEmail = (req.ToEmail ?? "").Trim();
        var summary = (req.Summary ?? "").Trim();

        if (toEmail.Length == 0)
            return BadRequest(new { success = false, error = "El campo 'toEmail' es requerido." });
        if (summary.Length == 0)
            return BadRequest(new { success = false, error = "El campo 'summary' es requerido." });

        var clientName = string.IsNullOrWhiteSpace(req.ClientName) ? "Cliente" : req.ClientName!.Trim();
        var phone = string.IsNullOrWhiteSpace(req.Phone) ? "-" : req.Phone!.Trim();
        var subject = string.IsNullOrWhiteSpace(req.Subject) ? $"Resumen solicitado - {clientName}" : req.Subject!.Trim();

        // Reutilizamos el template de SendConversationResume para tener un email con marca y
        // dentro del cuerpo inyectamos el resumen como único mensaje "agente".
        var messages = new List<(string Who, string Text)>
        {
            ("Agente", summary)
        };

        await emailService.SendConversationResumeAsync(
            toEmail: toEmail,
            ccEmail: null,
            clientName: clientName,
            clientPhone: phone,
            policyNumber: null,
            messages: messages,
            ct: ct);

        return Ok(new SendSummaryEmailResponse(
            Success: true,
            Message: $"Resumen enviado correctamente a {toEmail}.",
            SentTo: toEmail,
            SentAtUtc: DateTime.UtcNow
        ));
    }

    // ────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────

    private static string SanitizeForFile(string value)
    {
        var sb = new StringBuilder();
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch == '+' || ch == '-' || ch == '_') sb.Append(ch);
        }
        return sb.Length == 0 ? "doc" : sb.ToString();
    }
}

/// <summary>
/// Generador mínimo de PDF (PDF 1.4) sin dependencias externas.
/// Escribe un documento de una página con varias líneas de texto usando
/// la fuente Helvetica estándar. Escapa paréntesis y backslashes en el
/// contenido. No soporta imágenes ni UTF-8 avanzado, solo ASCII/Latin-1
/// suficiente para un comprobante de prueba.
/// </summary>
internal static class MinimalPdfBuilder
{
    public static byte[] BuildSimpleDocument(IList<string> lines)
    {
        // Construir el content stream primero.
        // 72 puntos = 1 pulgada. Hoja 612x792 (Letter).
        var contentSb = new StringBuilder();
        contentSb.Append("BT\n");
        contentSb.Append("/F1 14 Tf\n");            // Helvetica 14
        contentSb.Append("1 0 0 1 72 740 Tm\n");   // Posición de inicio (x=72, y=740)
        contentSb.Append("16 TL\n");                // Line spacing 16pt
        for (int i = 0; i < lines.Count; i++)
        {
            var line = EscapePdfString(lines[i] ?? "");
            if (i == 0)
                contentSb.Append($"({line}) Tj\n");
            else
                contentSb.Append($"T*\n({line}) Tj\n");
        }
        contentSb.Append("ET\n");
        var contentBytes = Encoding.Latin1.GetBytes(contentSb.ToString());

        // Objetos PDF
        // 1: Catalog, 2: Pages, 3: Page, 4: Font, 5: Contents (stream binario)
        var staticObjects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 5 0 R /Resources << /Font << /F1 4 0 R >> >> >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"
        };
        const int totalObjects = 5;

        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.Latin1.GetBytes(s));
        void WriteBytes(byte[] b) => ms.Write(b);

        Write("%PDF-1.4\n");
        Write("%\xE2\xE3\xCF\xD3\n"); // Marker binario requerido

        var offsets = new long[totalObjects];

        // Objetos estáticos 1..4
        for (int i = 0; i < staticObjects.Length; i++)
        {
            offsets[i] = ms.Position;
            Write($"{i + 1} 0 obj\n");
            Write(staticObjects[i]);
            Write("\nendobj\n");
        }

        // Objeto 5: contents con stream binario
        offsets[4] = ms.Position;
        Write("5 0 obj\n");
        Write($"<< /Length {contentBytes.Length} >>\n");
        Write("stream\n");
        WriteBytes(contentBytes);
        Write("\nendstream\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n");
        Write($"0 {totalObjects + 1}\n");
        Write("0000000000 65535 f \n");
        foreach (var off in offsets)
            Write($"{off.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");

        Write("trailer\n");
        Write($"<< /Size {totalObjects + 1} /Root 1 0 R >>\n");
        Write("startxref\n");
        Write($"{xrefOffset}\n");
        Write("%%EOF\n");

        return ms.ToArray();
    }

    private static string EscapePdfString(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '(': sb.Append("\\("); break;
                case ')': sb.Append("\\)"); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 32 || ch > 126)
                    {
                        // Mapeo simple de tildes/ñ al equivalente ASCII para el Helvetica básico.
                        var replacement = ch switch
                        {
                            'á' => "a", 'é' => "e", 'í' => "i", 'ó' => "o", 'ú' => "u",
                            'Á' => "A", 'É' => "E", 'Í' => "I", 'Ó' => "O", 'Ú' => "U",
                            'ñ' => "n", 'Ñ' => "N",
                            'ü' => "u", 'Ü' => "U",
                            _ => "?"
                        };
                        sb.Append(replacement);
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }
        return sb.ToString();
    }
}
