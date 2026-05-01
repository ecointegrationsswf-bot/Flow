using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Entities;

/// <summary>
/// Definición de una columna a extraer del JSON de respuesta de un webhook de descarga.
/// Cada acción tiene su propio set de columnas — ya no depende de un catálogo global.
///
/// Restricciones que impone la capa de aplicación:
/// — Una acción debe tener exactamente 1 mapping con Role=Phone, 1 con Role=ClientName y 1 con Role=KeyValue.
/// — Cualquier otro Role aparece como máximo 1 vez.
/// — Cuando Role=KeyValue, RoleLabel es obligatorio (ej: "Número de póliza").
/// </summary>
public class ActionFieldMapping
{
    public Guid Id { get; set; }

    public Guid ActionDefinitionId { get; set; }
    public ActionDefinition? ActionDefinition { get; set; }

    /// <summary>
    /// Identificador interno de la columna dentro de la acción (slug — ej: "celular", "saldo", "agente").
    /// Único por ActionDefinitionId. Reemplaza al antiguo LogicalFieldKey acoplado al catálogo global.
    /// </summary>
    public string ColumnKey { get; set; } = string.Empty;

    /// <summary>Etiqueta visible al usuario en la UI y en los exports. Ej: "Saldo pendiente".</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>JsonPath para extraer el valor. Ej: "$.Saldo", "$.cliente.nombre", "$.items[0].telefono".</summary>
    public string JsonPath { get; set; } = string.Empty;

    /// <summary>Rol semántico del campo. None = campo extra sin significado especial.</summary>
    public FieldRole Role { get; set; } = FieldRole.None;

    /// <summary>
    /// Sólo aplica cuando Role=KeyValue. Describe qué representa el KeyValue para esta acción.
    /// Ej: "Número de póliza", "Cédula", "ID cliente". Lo lee el webhook de gestión y la UI.
    /// </summary>
    public string? RoleLabel { get; set; }

    /// <summary>Tipo de dato: "string" | "number" | "phone" | "currency" | "date".</summary>
    public string DataType { get; set; } = "string";

    /// <summary>Orden de aparición en la UI y en el export.</summary>
    public int SortOrder { get; set; }

    /// <summary>Valor por defecto si el JsonPath no encuentra el campo.</summary>
    public string? DefaultValue { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
