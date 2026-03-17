namespace AgentFlow.Domain.Entities;

/// <summary>
/// Etiqueta configurable por tenant para clasificar el estado/resultado de conversaciones.
/// Ej: "Confirmo pago", "Cancelar póliza", "Asegurado errado", "Nivel 1", etc.
/// Las palabras clave permiten asignación automática por el agente IA.
/// </summary>
public class ConversationLabel
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#10B981";        // hex color
    public List<string> Keywords { get; set; } = [];       // palabras clave para auto-asignación
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
}
