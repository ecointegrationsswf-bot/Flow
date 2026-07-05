namespace AgentFlow.Domain.Provisioning;

/// <summary>
/// Integración Ludo CRM — Fase 2 (Dirección A, inbound). Payload que Ludo envía a
/// POST /api/provisioning/tenant cuando crea un tenant con el flag de creación en TalkIA.
/// Corresponde al §4.1 del documento de diseño. La clave de idempotencia es <see cref="LudoTenantId"/>.
/// </summary>
public sealed record ProvisionTenantRequest(
    string LudoTenantId,
    string TipoNegocio,          // seguro | restaurante | inmobiliario
    string NombreNegocio,
    string? WhatsappInstanceId,  // instancia UltraMsg (la línea aún se conecta a mano)
    IReadOnlyList<AgentSeed> Agentes,
    IReadOnlyList<StageSeed> Etapas);

/// <summary>Agente a sembrar. Exactamente uno debe tener <see cref="Welcome"/>=true.</summary>
public sealed record AgentSeed(string Slug, string Objetivo, bool Welcome);

/// <summary>Etapa del pipeline de Ludo a homologar como etiqueta. Clave estable: <see cref="LudoStageId"/>.</summary>
public sealed record StageSeed(string LudoStageId, string Nombre, int Orden);

/// <summary>Maestro de campaña generado (en estado Borrador) durante el aprovisionamiento.</summary>
public sealed record ProvisionedMaster(string AgentSlug, Guid TemplateId, string Name);

/// <summary>
/// Resultado del aprovisionamiento. <see cref="AlreadyExisted"/>=true cuando la idempotencia
/// detectó que el ludoTenantId ya estaba mapeado (reenvío seguro de Ludo) → no se duplica nada.
/// </summary>
public sealed record ProvisionResult(
    bool AlreadyExisted,
    Guid TenantId,
    IReadOnlyList<ProvisionedMaster> Masters)
{
    public static ProvisionResult AlreadyExists(Guid tenantId) =>
        new(true, tenantId, []);

    public static ProvisionResult Created(Guid tenantId, IReadOnlyList<ProvisionedMaster> masters) =>
        new(false, tenantId, masters);
}

/// <summary>
/// Aprovisiona un tenant en TalkIA a partir de un evento de Ludo, de forma
/// <b>idempotente</b> (por ludoTenantId) y <b>transaccional</b> (todo-o-nada: un fallo a mitad
/// revierte y permite reintento limpio). No toca ningún tenant existente.
/// </summary>
public interface ITenantProvisioningService
{
    Task<ProvisionResult> ProvisionAsync(ProvisionTenantRequest req, CancellationToken ct);
}

/// <summary>
/// Excepción de validación del payload de provisioning (ej: ningún agente welcome o más de uno).
/// El controller la mapea a HTTP 400. No es un error de servidor — es input inválido de Ludo.
/// </summary>
public sealed class ProvisioningValidationException(string message) : Exception(message);
