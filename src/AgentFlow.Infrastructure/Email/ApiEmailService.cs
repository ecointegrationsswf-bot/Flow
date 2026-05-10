using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Email;

/// <summary>
/// Implementación de <see cref="IEmailService"/> que delega el envío al API
/// vía endpoints internos /api/internal/email/*. Pensada para el Worker
/// on-prem: las credenciales del proveedor (Resend/SendGrid/SMTP) viven
/// solo en el API. Si mañana cambia el proveedor, se redespliega únicamente
/// el API y el Worker queda intacto.
///
/// Config requerida (Worker):
///   EmailApi:BaseUrl     = http://jamconsulting-004-site12.site4future.com
///   EmailApi:InternalKey = &lt;mismo valor que EmailApi:InternalKey en API&gt;
/// </summary>
public class ApiEmailService(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<ApiEmailService> log) : IEmailService
{
    private string BaseUrl =>
        (config["EmailApi:BaseUrl"] ?? throw new InvalidOperationException("EmailApi:BaseUrl no configurada."))
            .TrimEnd('/');
    private string InternalKey =>
        config["EmailApi:InternalKey"] ?? throw new InvalidOperationException("EmailApi:InternalKey no configurada.");

    public Task SendWelcomeAdminEmailAsync(string toEmail, string fullName, string password,
        IEnumerable<string>? bccEmails = null, CancellationToken ct = default)
        => PostAsync("/api/internal/email/welcome-admin",
            new { toEmail, fullName, password, bccEmails = bccEmails?.ToList() }, ct);

    public Task SendWelcomeTenantEmailAsync(string toEmail, string fullName, string password, string tenantName,
        IEnumerable<string>? bccEmails = null, CancellationToken ct = default)
        => PostAsync("/api/internal/email/welcome-tenant",
            new { toEmail, fullName, password, tenantName, bccEmails = bccEmails?.ToList() }, ct);

    public Task SendTwoFactorCodeAsync(string toEmail, string fullName, string code, CancellationToken ct = default)
        => PostAsync("/api/internal/email/2fa", new { toEmail, fullName, code }, ct);

    public Task SendPasswordResetAsync(string toEmail, string fullName, string resetToken, CancellationToken ct = default)
        => PostAsync("/api/internal/email/password-reset", new { toEmail, fullName, resetToken }, ct);

    public Task SendConversationResumeAsync(string toEmail, string? ccEmail,
        string clientName, string clientPhone, string? policyNumber,
        List<(string Who, string Text)> messages, CancellationToken ct = default)
        => PostAsync("/api/internal/email/conversation-resume",
            new
            {
                toEmail, ccEmail, clientName, clientPhone, policyNumber,
                messages = messages.Select(m => new { who = m.Who, text = m.Text }).ToList()
            }, ct);

    public Task SendLabelingSummaryAsync(string toEmail, string fullName, string excelUrl,
        IReadOnlyList<(string CampaignName, IReadOnlyDictionary<string, int> CountsByLabel, int Unlabeled)> campaigns,
        IEnumerable<string>? bccEmails = null, CancellationToken ct = default)
        => PostAsync("/api/internal/email/labeling-summary",
            new
            {
                toEmail, fullName, excelUrl,
                campaigns = campaigns.Select(c => new
                {
                    campaignName = c.CampaignName,
                    countsByLabel = c.CountsByLabel.ToDictionary(k => k.Key, v => v.Value),
                    unlabeled = c.Unlabeled
                }).ToList(),
                bccEmails = bccEmails?.ToList()
            }, ct);

    private async Task PostAsync(string path, object payload, CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(60);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{path}")
        {
            Content = JsonContent.Create(payload),
        };
        req.Headers.Add("X-Internal-Email-Key", InternalKey);

        HttpResponseMessage resp;
        try
        {
            resp = await http.SendAsync(req, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "[ApiEmail] HTTP error contra {Url}.", path);
            throw;
        }

        if (!resp.IsSuccessStatusCode)
        {
            string body;
            try { body = await resp.Content.ReadAsStringAsync(ct); }
            catch { body = "(sin cuerpo)"; }
            var preview = body.Length > 400 ? body[..400] : body;
            log.LogError("[ApiEmail] FAIL {Code} {Url}: {Body}", (int)resp.StatusCode, path, preview);
            throw new InvalidOperationException(
                $"API rechazó el envío en {path}: HTTP {(int)resp.StatusCode}. {preview}");
        }
    }
}
