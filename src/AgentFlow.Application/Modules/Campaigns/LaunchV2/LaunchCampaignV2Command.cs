using MediatR;

namespace AgentFlow.Application.Modules.Campaigns.LaunchV2;

/// <summary>
/// Lanza una campaña usando el flujo v2 (en-proceso, sin n8n). Replica las fases
/// A+B+C del workflow_campana_v3:
/// (A) validación de teléfonos ya hecha en StartCampaignCommand al subir el archivo.
/// (B) detección de duplicados activos contra otras campañas del mismo tenant.
/// (C) aplicación del warm-up: contactos que excedan el límite del día → Deferred.
///
/// El envío real lo realiza el CampaignWorker (BackgroundService en el Worker)
/// consumiendo los CampaignContacts en estado Queued.
/// </summary>
public record LaunchCampaignV2Command(
    Guid CampaignId,
    Guid TenantId,
    string LaunchedByUserId,
    string? LaunchedByUserPhone,
    int WarmupDay = 0
) : IRequest<LaunchCampaignV2Result>;
