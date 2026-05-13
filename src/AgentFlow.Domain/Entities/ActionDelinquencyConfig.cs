namespace AgentFlow.Domain.Entities;

/// <summary>
/// Configuración del módulo de morosidad para una ActionDefinition específica de un tenant.
/// Define cómo procesar la respuesta del webhook: código de país, auto-creación de campañas,
/// template de campaña, etc.
/// Un registro por (TenantId, ActionDefinitionId).
/// </summary>
public class ActionDelinquencyConfig
{
    public Guid Id { get; set; }

    public Guid ActionDefinitionId { get; set; }
    public ActionDefinition? ActionDefinition { get; set; }

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    /// <summary>
    /// Código de país numérico para normalizar teléfonos. Ej: "507" (Panamá).
    /// Se agrega como prefijo si el número extraído del payload no lo incluye.
    /// </summary>
    public string CodigoPais { get; set; } = "507";

    /// <summary>
    /// JsonPath para llegar al array de ítems dentro de la respuesta del webhook.
    /// Null = la raíz de la respuesta ES el array.
    /// Ej: "$.data", "$.resultado.morosidades".
    /// </summary>
    public string? ItemsJsonPath { get; set; }

    /// <summary>
    /// Si true, el sistema crea campañas de WhatsApp automáticamente por cada
    /// grupo de contactos (un número = un contacto en la campaña).
    /// Si false, notifica al operador vía SignalR/email para revisión manual.
    /// </summary>
    public bool AutoCrearCampanas { get; set; }

    /// <summary>Maestro de campaña a usar al auto-crear campañas. Requerido si AutoCrearCampanas=true.</summary>
    public Guid? CampaignTemplateId { get; set; }
    public CampaignTemplate? CampaignTemplate { get; set; }

    /// <summary>Agente asignado a las campañas creadas automáticamente.</summary>
    public Guid? AgentDefinitionId { get; set; }
    public AgentDefinition? AgentDefinition { get; set; }

    /// <summary>
    /// Patrón para el nombre de las campañas creadas. Soporta placeholders:
    /// {fecha:yyyy-MM-dd}, {accion} (nombre de la acción), {grupos} (cantidad de grupos).
    /// Ej: "Morosidad {accion} {fecha:yyyy-MM-dd}".
    /// </summary>
    public string? CampaignNamePattern { get; set; }

    /// <summary>Email al que se notifica cuando hay nuevos grupos (modo manual o errores).</summary>
    public string? NotificationEmail { get; set; }

    /// <summary>
    /// URL del endpoint externo del que se descarga el JSON de morosidad.
    /// Cada tenant tiene su propia URL (ej: endpoint de Tobroker, SURA, ASSA).
    /// Si null, el job no puede ejecutarse en modo automático.
    /// </summary>
    public string? DownloadWebhookUrl { get; set; }

    /// <summary>Método HTTP para la descarga. Defecto GET.</summary>
    public string DownloadWebhookMethod { get; set; } = "GET";

    /// <summary>
    /// Headers adicionales para la petición de descarga en formato JSON.
    /// Ej: {"Authorization":"Bearer token123","X-ApiKey":"abc"}.
    /// </summary>
    public string? DownloadWebhookHeaders { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
