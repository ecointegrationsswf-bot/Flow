namespace AgentFlow.Infrastructure.Email;

public interface IEmailService
{
    Task SendWelcomeAdminEmailAsync(string toEmail, string fullName, string password, IEnumerable<string>? bccEmails = null, CancellationToken ct = default);
    Task SendWelcomeTenantEmailAsync(string toEmail, string fullName, string password, string tenantName, IEnumerable<string>? bccEmails = null, CancellationToken ct = default);
    /// <summary>
    /// Envía el código 2FA. Si se pasan tenantName/tenantLogoUrl, la plantilla
    /// usa el branding del corredor (logo en el header + nombre en subject/body).
    /// Si no, fallback al branding genérico "TalkIA".
    /// </summary>
    Task SendTwoFactorCodeAsync(
        string toEmail, string fullName, string code,
        string? tenantName = null, string? tenantLogoUrl = null,
        CancellationToken ct = default);
    Task SendPasswordResetAsync(string toEmail, string fullName, string resetToken, CancellationToken ct = default);

    Task SendConversationResumeAsync(
        string toEmail,
        string? ccEmail,
        string clientName,
        string clientPhone,
        string? policyNumber,
        List<(string Who, string Text)> messages,
        CancellationToken ct = default);

    /// <summary>
    /// Envía el resumen del etiquetado IA al usuario que cargó las campañas.
    /// Incluye conteo por etiqueta + botón "Ver resumen" → Excel en Azure Blob.
    /// Los emails en bccEmails reciben copia oculta (típicamente todos los super admins).
    /// </summary>
    /// <param name="outboundEmailCount">
    /// Total de correos salientes (Direction=Outbound, Channel=Email) emitidos por
    /// las campañas del reporte desde el último LabelingSummarySentAt. Se muestra
    /// como una 4ª tarjeta KPI ("Emails enviados") en el correo. Default 0 si no
    /// se quiere mostrar — la tarjeta sigue apareciendo con valor 0.
    /// </param>
    Task SendLabelingSummaryAsync(
        string toEmail,
        string fullName,
        string excelUrl,
        IReadOnlyList<(string CampaignName, IReadOnlyDictionary<string, int> CountsByLabel, int Unlabeled)> campaigns,
        IEnumerable<string>? bccEmails = null,
        int outboundEmailCount = 0,
        CancellationToken ct = default);

    /// <summary>
    /// Envía un correo con HTML libre — usado por la plantilla personalizable del
    /// maestro de campaña (EmailBodyHtml ya renderizado con variables resueltas).
    /// El asunto y el cuerpo ya vienen renderizados; el método solo despacha.
    /// Devuelve el ID externo del provider (Resend id, SendGrid Message-Id, etc.)
    /// para persistirlo en Message.ExternalMessageId y usarlo con webhooks de
    /// delivery/bounce. Si el provider no expone id, devolver null.
    /// </summary>
    Task<string?> SendCustomHtmlAsync(
        string toEmail,
        string? ccEmail,
        string subject,
        string htmlBody,
        string? textBody,
        CancellationToken ct = default);
}
