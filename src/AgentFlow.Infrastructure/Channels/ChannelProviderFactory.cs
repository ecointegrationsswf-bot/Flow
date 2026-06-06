using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Channels.MetaCloudApi;
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

        return BuildProvider(line);
    }

    public async Task<IChannelProvider?> GetProviderByLineAsync(Guid lineId, CancellationToken ct = default)
    {
        var line = await db.WhatsAppLines
            .FirstOrDefaultAsync(l => l.Id == lineId && l.IsActive, ct);

        if (line is null)
            return null;

        return BuildProvider(line);
    }

    /// <summary>
    /// Umbral de "caída confirmada": mismo criterio CRÍTICO del job diario y los
    /// pre-checks de campaña (≥2 fallos consecutivos). Tolera 1 parpadeo puntual.
    /// </summary>
    private const int KnownDownFailureThreshold = 2;

    public async Task<bool> IsLineKnownDownAsync(Guid lineId, CancellationToken ct = default)
    {
        // Proyección mínima — solo los dos campos de salud, sin red ni el resto de la fila.
        var snap = await db.WhatsAppLines
            .Where(l => l.Id == lineId)
            .Select(l => new { l.LastStatus, l.ConsecutivePingFailures })
            .FirstOrDefaultAsync(ct);

        if (snap is null) return false;                       // línea desconocida → no bloquear
        if (string.IsNullOrEmpty(snap.LastStatus)) return false; // nunca pingeada → no bloquear

        var down = !string.Equals(snap.LastStatus, "authenticated", StringComparison.OrdinalIgnoreCase)
                   && snap.ConsecutivePingFailures >= KnownDownFailureThreshold;
        return down;
    }

    /// <summary>
    /// Construye el IChannelProvider correcto según line.Provider.
    /// Aditivo: UltraMsg sigue siendo el default; MetaCloudApi usa las credenciales
    /// Meta de la línea (InstanceId = phone_number_id). Crea un HttpClient nuevo —
    /// para envíos puntuales esto es suficiente. Timeout duro de 15s (el default de
    /// HttpClient es 100s; si el proveedor se cuelga, el flujo del agente esperaría
    /// 100s y dispararía el debouncer/dispatcher como fallido).
    /// </summary>
    private static IChannelProvider BuildProvider(WhatsAppLine line)
    {
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        if (line.Provider == ProviderType.MetaCloudApi)
        {
            // En Meta, InstanceId guarda el phone_number_id.
            var metaOptions = new MetaCloudApiOptions(
                PhoneNumberId: line.InstanceId.Trim(),
                AccessToken: line.MetaAccessToken ?? string.Empty,
                AppSecret: line.MetaAppSecret);
            return new MetaCloudApiProvider(httpClient, metaOptions);
        }

        // UltraMsg (default): espera "instance{numero}" en la URL.
        var normalizedId = line.InstanceId.Trim();
        if (!normalizedId.StartsWith("instance", StringComparison.OrdinalIgnoreCase))
            normalizedId = $"instance{normalizedId}";

        return new UltraMsgProvider(httpClient, new UltraMsgOptions(normalizedId, line.ApiToken));
    }
}
