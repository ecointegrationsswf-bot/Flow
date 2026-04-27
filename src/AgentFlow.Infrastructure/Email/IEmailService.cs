namespace AgentFlow.Infrastructure.Email;

public interface IEmailService
{
    Task SendWelcomeAdminEmailAsync(string toEmail, string fullName, string password, CancellationToken ct = default);
    Task SendWelcomeTenantEmailAsync(string toEmail, string fullName, string password, string tenantName, CancellationToken ct = default);
    Task SendTwoFactorCodeAsync(string toEmail, string fullName, string code, CancellationToken ct = default);
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
    Task SendLabelingSummaryAsync(
        string toEmail,
        string fullName,
        string excelUrl,
        IReadOnlyList<(string CampaignName, IReadOnlyDictionary<string, int> CountsByLabel, int Unlabeled)> campaigns,
        IEnumerable<string>? bccEmails = null,
        CancellationToken ct = default);
}
