using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Channels.UltraMsg;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.Infrastructure.Channels;

/// <summary>
/// Implementación del factory que busca la línea WhatsApp activa del tenant
/// en la base de datos y construye un UltraMsgProvider con sus credenciales.
///
/// Flujo:
/// 1. Recibe tenantId
/// 2. Busca en BD: "¿cuál línea WhatsApp activa tiene este tenant?"
/// 3. Lee su InstanceId y Token
/// 4. Crea un UltraMsgProvider con esos datos
/// 5. Retorna el provider listo para enviar mensajes
///
/// Si el tenant no tiene línea activa, retorna null (no puede enviar).
/// </summary>
public class ChannelProviderFactory(AgentFlowDbContext db) : IChannelProviderFactory
{
    public async Task<IChannelProvider?> GetProviderAsync(Guid tenantId, CancellationToken ct = default)
    {
        // Buscar la primera línea activa del tenant
        var line = await db.WhatsAppLines
            .Where(l => l.TenantId == tenantId && l.IsActive)
            .OrderBy(l => l.CreatedAt)   // la más antigua primero (línea principal)
            .FirstOrDefaultAsync(ct);

        if (line is null)
            return null;   // tenant no tiene línea WhatsApp configurada

        return BuildProvider(line.InstanceId, line.ApiToken);
    }

    public async Task<IChannelProvider?> GetProviderByLineAsync(Guid lineId, CancellationToken ct = default)
    {
        var line = await db.WhatsAppLines
            .FirstOrDefaultAsync(l => l.Id == lineId && l.IsActive, ct);

        if (line is null)
            return null;

        return BuildProvider(line.InstanceId, line.ApiToken);
    }

    /// <summary>
    /// Construye un UltraMsgProvider con las credenciales de la línea.
    /// Crea un HttpClient nuevo — para envíos puntuales esto es suficiente.
    /// </summary>
    private static UltraMsgProvider BuildProvider(string instanceId, string apiToken)
    {
        // Normalizar: UltraMsg espera "instance{numero}" en la URL
        var normalizedId = instanceId.Trim();
        if (!normalizedId.StartsWith("instance", StringComparison.OrdinalIgnoreCase))
            normalizedId = $"instance{normalizedId}";

        var httpClient = new HttpClient();
        var options = new UltraMsgOptions(normalizedId, apiToken);

        return new UltraMsgProvider(httpClient, options);
    }
}
