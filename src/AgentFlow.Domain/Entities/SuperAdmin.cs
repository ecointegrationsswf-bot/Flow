namespace AgentFlow.Domain.Entities;

public class SuperAdmin
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    // Seguridad
    public bool MustChangePassword { get; set; } = true;
    public string? TwoFactorCode { get; set; }
    public DateTime? TwoFactorExpiry { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}
