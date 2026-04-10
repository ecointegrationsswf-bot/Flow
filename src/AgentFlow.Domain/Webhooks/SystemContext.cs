namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Contexto del sistema disponible en runtime para construir payloads de webhook.
/// Contiene todos los valores que los InputField pueden referenciar via SourceType="system"
/// y SourceKey="contact.phone", "session.id", "campaign.name", etc.
///
/// Se construye al momento de ejecutar una acción (ActionExecutorService)
/// leyendo de la BD las entidades relacionadas con la conversación actual.
/// </summary>
public class SystemContext
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resuelve un sourceKey como "contact.idNumber" → valor.</summary>
    public object? Get(string sourceKey) =>
        _values.TryGetValue(sourceKey, out var v) ? v : null;

    /// <summary>Agrega un valor al contexto. Los keys son case-insensitive.</summary>
    public void Set(string sourceKey, object? value) =>
        _values[sourceKey] = value;

    /// <summary>Snapshot inmutable del contexto (para logs y debug).</summary>
    public IReadOnlyDictionary<string, object?> Values => _values;
}
