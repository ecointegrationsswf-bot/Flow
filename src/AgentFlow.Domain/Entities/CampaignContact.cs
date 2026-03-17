using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Entities;

/// <summary>
/// Contacto individual dentro de una campaña.
/// Contiene los datos del cliente y el estado de su gestión.
/// </summary>
public class CampaignContact
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public Campaign Campaign { get; set; } = null!;

    // Datos del cliente
    public string PhoneNumber { get; set; } = string.Empty;     // normalizado E.164
    public string? Email { get; set; }
    public string? ClientName { get; set; }
    public string? PolicyNumber { get; set; }
    public string? InsuranceCompany { get; set; }
    public decimal? PendingAmount { get; set; }

    // Metadatos extra del archivo (columnas adicionales)
    public Dictionary<string, string> ExtraData { get; set; } = [];

    // Estado
    public bool IsPhoneValid { get; set; } = true;
    public int RetryCount { get; set; }
    public GestionResult Result { get; set; } = GestionResult.Pending;
    public DateTime? LastContactAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
