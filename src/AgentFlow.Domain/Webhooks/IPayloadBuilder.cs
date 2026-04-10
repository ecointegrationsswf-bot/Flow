namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Construye el objeto payload del webhook a partir del InputSchema del tenant.
/// Resuelve cada InputField según su SourceType (system/conversation/static)
/// y lo serializa respetando la Structure (flat/nested).
/// </summary>
public interface IPayloadBuilder
{
    /// <summary>
    /// Construye el payload final como Dictionary (flat) o estructura anidada.
    /// El HttpDispatcher se encarga luego de serializarlo al ContentType indicado.
    /// </summary>
    object Build(InputSchema schema, CollectedParams collected, SystemContext systemContext);
}
