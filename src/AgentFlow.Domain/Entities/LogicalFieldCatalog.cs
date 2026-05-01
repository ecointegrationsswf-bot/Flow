namespace AgentFlow.Domain.Entities;

/// <summary>
/// Catálogo global de campos lógicos que el módulo de morosidad puede extraer
/// de la respuesta del webhook. Cada campo tiene una clave semántica (Key) que
/// los FieldMappings usan para asociarla a un JsonPath concreto por acción.
/// </summary>
public class LogicalFieldCatalog
{
    public Guid Id { get; set; }

    /// <summary>
    /// Identificador semántico único. Ej: "PhoneNumber", "ClientName", "Amount", "PolicyNumber".
    /// Usado como FK lógica en ActionFieldMapping — se guarda como string para legibilidad.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Nombre legible para mostrar en la UI de configuración.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Tipo de dato esperado: "string" | "number" | "phone".
    /// Usado para validaciones y conversiones al extraer el valor del JSON.
    /// </summary>
    public string DataType { get; set; } = "string";

    /// <summary>
    /// Si true, el módulo de morosidad requiere que esta acción tenga
    /// un mapping configurado para este campo antes de procesar.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>Orden de aparición en la UI de configuración de mappings.</summary>
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
