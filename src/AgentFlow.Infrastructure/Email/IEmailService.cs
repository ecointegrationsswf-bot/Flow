namespace AgentFlow.Infrastructure.Email;

public interface IEmailService
{
    Task SendWelcomeAdminEmailAsync(string toEmail, string fullName, string password, CancellationToken ct = default);
    Task SendWelcomeTenantEmailAsync(string toEmail, string fullName, string password, string tenantName, CancellationToken ct = default);
    Task SendTwoFactorCodeAsync(string toEmail, string fullName, string code, CancellationToken ct = default);
    Task SendPasswordResetAsync(string toEmail, string fullName, string resetToken, CancellationToken ct = default);
}
