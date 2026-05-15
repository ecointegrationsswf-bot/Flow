using AgentFlow.Infrastructure.Email;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AgentFlow.API.Controllers;

/// <summary>
/// Endpoint interno usado por el Worker on-prem para delegar el envío de
/// correos al API. Centraliza credenciales y selección de proveedor en un
/// solo proceso (el API en Smartasp); el Worker no necesita SendGrid/Resend
/// instalado ni configurado.
///
/// Auth: header X-Internal-Email-Key debe coincidir con la config
/// EmailApi:InternalKey. Si la config está vacía o el header no coincide,
/// devuelve 401.
/// </summary>
[ApiController]
[Route("api/internal/email")]
[ServiceFilter(typeof(InternalEmailKeyFilter))]
public class InternalEmailController(IEmailService email) : ControllerBase
{
    public record WelcomeAdminBody(string ToEmail, string FullName, string Password, List<string>? BccEmails);
    public record WelcomeTenantBody(string ToEmail, string FullName, string Password, string TenantName, List<string>? BccEmails);
    public record TwoFactorBody(string ToEmail, string FullName, string Code);
    public record PasswordResetBody(string ToEmail, string FullName, string ResetToken);
    public record ConversationResumeBody(
        string ToEmail, string? CcEmail, string ClientName, string ClientPhone,
        string? PolicyNumber, List<MessageEntry> Messages);
    public record MessageEntry(string Who, string Text);
    public record LabelingSummaryBody(
        string ToEmail, string FullName, string ExcelUrl,
        List<CampaignSummary> Campaigns, List<string>? BccEmails,
        int OutboundEmailCount = 0);
    public record CampaignSummary(string CampaignName, Dictionary<string, int> CountsByLabel, int Unlabeled);
    public record CustomHtmlBody(string ToEmail, string? CcEmail, string Subject, string HtmlBody, string? TextBody);

    [HttpPost("welcome-admin")]
    public async Task<IActionResult> WelcomeAdmin([FromBody] WelcomeAdminBody b, CancellationToken ct)
    {
        await email.SendWelcomeAdminEmailAsync(b.ToEmail, b.FullName, b.Password, b.BccEmails, ct);
        return Ok();
    }

    [HttpPost("welcome-tenant")]
    public async Task<IActionResult> WelcomeTenant([FromBody] WelcomeTenantBody b, CancellationToken ct)
    {
        await email.SendWelcomeTenantEmailAsync(b.ToEmail, b.FullName, b.Password, b.TenantName, b.BccEmails, ct);
        return Ok();
    }

    [HttpPost("2fa")]
    public async Task<IActionResult> TwoFactor([FromBody] TwoFactorBody b, CancellationToken ct)
    {
        await email.SendTwoFactorCodeAsync(b.ToEmail, b.FullName, b.Code, ct);
        return Ok();
    }

    [HttpPost("password-reset")]
    public async Task<IActionResult> PasswordReset([FromBody] PasswordResetBody b, CancellationToken ct)
    {
        await email.SendPasswordResetAsync(b.ToEmail, b.FullName, b.ResetToken, ct);
        return Ok();
    }

    [HttpPost("conversation-resume")]
    public async Task<IActionResult> ConversationResume([FromBody] ConversationResumeBody b, CancellationToken ct)
    {
        var msgs = b.Messages.Select(m => (m.Who, m.Text)).ToList();
        await email.SendConversationResumeAsync(b.ToEmail, b.CcEmail, b.ClientName, b.ClientPhone, b.PolicyNumber, msgs, ct);
        return Ok();
    }

    [HttpPost("custom-html")]
    public async Task<IActionResult> CustomHtml([FromBody] CustomHtmlBody b, CancellationToken ct)
    {
        await email.SendCustomHtmlAsync(b.ToEmail, b.CcEmail, b.Subject, b.HtmlBody, b.TextBody, ct);
        return Ok();
    }

    [HttpPost("labeling-summary")]
    public async Task<IActionResult> LabelingSummary([FromBody] LabelingSummaryBody b, CancellationToken ct)
    {
        var campaigns = b.Campaigns
            .Select(c => (c.CampaignName,
                          (IReadOnlyDictionary<string, int>)c.CountsByLabel,
                          c.Unlabeled))
            .ToList();
        await email.SendLabelingSummaryAsync(b.ToEmail, b.FullName, b.ExcelUrl, campaigns, b.BccEmails, b.OutboundEmailCount, ct);
        return Ok();
    }
}

public class InternalEmailKeyFilter(IConfiguration config) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var configured = config["EmailApi:InternalKey"];
        var provided   = ctx.HttpContext.Request.Headers["X-Internal-Email-Key"].ToString();

        if (string.IsNullOrEmpty(configured) || !string.Equals(configured, provided, StringComparison.Ordinal))
        {
            ctx.Result = new UnauthorizedObjectResult(new { error = "Invalid internal email key." });
            return;
        }
        await next();
    }
}
