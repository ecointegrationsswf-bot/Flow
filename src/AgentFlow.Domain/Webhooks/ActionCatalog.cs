namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Action Trigger Protocol — catálogo de acciones que el agente puede declarar
/// en un turno, junto con el bloque de texto ya renderizado para el system prompt.
///
/// Tanto la construcción del texto como la validación del tag [ACTION:slug] que
/// emita el agente se resuelven contra este mismo objeto, garantizando que el
/// conjunto que ve el agente y el conjunto que el sistema permite ejecutar son
/// idénticos.
///
/// BySlug usa la clave del slug en MAYÚSCULAS (coincide con ActionDefinition.Name).
/// </summary>
public class ActionCatalog
{
    /// <summary>Bloque Markdown preconstruido para inyectar en el system prompt. Puede ser vacío.</summary>
    public string Block { get; init; } = string.Empty;

    /// <summary>Diccionario slug → TriggerConfig correspondiente. Vacío si el tenant no usa Action Trigger Protocol.</summary>
    public IReadOnlyDictionary<string, TriggerConfig> BySlug { get; init; } =
        new Dictionary<string, TriggerConfig>();

    /// <summary>Helper: instancia vacía (feature off, template sin triggers, etc.)</summary>
    public static ActionCatalog Empty { get; } = new();

    /// <summary>¿El tenant/template tiene Action Trigger Protocol activo en al menos una acción?</summary>
    public bool IsActive => BySlug.Count > 0;

    /// <summary>
    /// ¿Este slug está dentro del catálogo? Case-insensitive.
    /// Si el catálogo está vacío devuelve false; el caller debe decidir si eso
    /// implica "deny" o "legacy fallback" según el contexto.
    /// </summary>
    public bool Contains(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return false;
        return BySlug.ContainsKey(slug.ToUpperInvariant());
    }

    /// <summary>Obtiene el TriggerConfig asociado al slug, o null si no existe.</summary>
    public TriggerConfig? Get(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        return BySlug.TryGetValue(slug.ToUpperInvariant(), out var tc) ? tc : null;
    }
}
