namespace AgentFlow.Domain.Entities;

public enum UserRole { Admin, Supervisor, Cobros, ReadOnly }

/// <summary>
/// Usuario del portal (ejecutivos de cobros, supervisores, admins).
/// </summary>
public class AppUser
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public bool CanEditPhone { get; set; }

    /// <summary>
    /// IDs de agentes que este usuario puede monitorear.
    /// Lista vacia o null = puede ver todos los agentes.
    /// </summary>
    public List<Guid> AllowedAgentIds { get; set; } = [];

    /// <summary>
    /// Permisos granulares del usuario. Ej: ["view_monitor", "create_campaigns"].
    /// Lista vacia = sin permisos adicionales (solo los del rol base).
    /// </summary>
    public List<string> Permissions { get; set; } = [];

    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Número de WhatsApp del ejecutivo en formato E.164 (ej: +50768001234).
    /// Se usa para enviarle notificaciones cuando un cliente solicita atención humana.
    /// </summary>
    public string? NotifyPhone { get; set; }

    // Seguridad
    public bool MustChangePassword { get; set; } = true;
    public string? TwoFactorCode { get; set; }
    public DateTime? TwoFactorExpiry { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}
