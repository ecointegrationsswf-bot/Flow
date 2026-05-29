namespace AgentFlow.Domain.Entities;

/// <summary>
/// Contrato webhook de una acción PARA UN TENANT específico.
///
/// Arquitectura "Acción (encabezado global) → Tenant → Contrato" — ver
/// docs/arquitectura-acciones-contratos.md y la sección de CLAUDE.md.
///
/// La <see cref="ActionDefinition"/> es global y única por slug; cada tenant que
/// use esa acción tiene aquí SU contrato. Editar el contrato de un tenant nunca
/// pisa el de otro (problema que tenía el viejo "clon de la acción por tenant").
///
/// <para><b>ContractJson</b>: mismo formato que
/// <c>ActionDefinition.DefaultWebhookContract</c> (deserializa a
/// <c>ActionConfigBundleJson</c>): { webhookUrl, webhookMethod, contentType,
/// structure, authType, authValue, apiKeyHeaderName, webhookHeaders,
/// timeoutSeconds, inputSchema, outputSchema, triggerConfig, chainRules }.
/// Guardarlo como un solo blob mantiene intacto todo el parsing downstream —
/// el resolver solo decide QUÉ string entregar (este o el default global).</para>
///
/// UNIQUE (ActionDefinitionId, TenantId): un contrato por acción-tenant.
/// </summary>
public class TenantActionContract
{
    public Guid Id { get; set; }

    /// <summary>FK a la acción GLOBAL (el "encabezado"). No se clona la acción.</summary>
    public Guid ActionDefinitionId { get; set; }

    /// <summary>FK al tenant dueño de este contrato.</summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Contrato completo en JSON — mismo shape que DefaultWebhookContract.
    /// </summary>
    public string ContractJson { get; set; } = "{}";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
