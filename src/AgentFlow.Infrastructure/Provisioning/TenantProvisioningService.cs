using System.Text;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Provisioning;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Provisioning;

/// <summary>
/// Integración Ludo CRM — Fase 2 (Dirección A). Aprovisiona un tenant desde un evento de Ludo.
///
/// Garantías:
/// <list type="bullet">
///   <item><b>Idempotente</b> por <c>LudoTenantMap.LudoTenantId</c>: reenvíos seguros.</item>
///   <item><b>Transaccional</b> todo-o-nada: un fallo a mitad revierte y permite reintento limpio.</item>
///   <item><b>Aditivo</b>: no toca ningún tenant existente; solo crea filas nuevas.</item>
/// </list>
///
/// <para>El maestro de campaña se crea en estado <b>Borrador</b> (IsActive=false,
/// IsPrimaryForAgent=false) con un SystemPrompt placeholder. La generación real asistida por
/// LLM (y la plantilla vetada para la vertical "seguro" con el gate de autenticación) es de la
/// <b>Fase 3</b>; aquí queda como stub para no dejar un prompt autogenerado en vivo.</para>
/// </summary>
public sealed class TenantProvisioningService(
    AgentFlowDbContext db,
    ICampaignTemplateGenerator generator,
    IConfiguration config,
    ILogger<TenantProvisioningService> log) : ITenantProvisioningService
{
    public async Task<ProvisionResult> ProvisionAsync(ProvisionTenantRequest req, CancellationToken ct)
    {
        // ── 1. Idempotencia ──────────────────────────────────────────────────────────
        var existing = await db.LudoTenantMaps
            .FirstOrDefaultAsync(m => m.LudoTenantId == req.LudoTenantId, ct);
        if (existing is not null)
        {
            log.LogInformation("Provisioning idempotente: ludoTenantId={LudoTenantId} ya mapeado a {TenantId}",
                req.LudoTenantId, existing.TenantId);
            return ProvisionResult.AlreadyExists(existing.TenantId);
        }

        // ── 2. Validación de input (welcome único) ───────────────────────────────────
        if (req.Agentes is null || req.Agentes.Count == 0)
            throw new ProvisioningValidationException("Se requiere al menos un agente.");
        var welcomeCount = req.Agentes.Count(a => a.Welcome);
        if (welcomeCount != 1)
            throw new ProvisioningValidationException(
                $"Debe haber exactamente un agente welcome=true (se recibieron {welcomeCount}).");

        // ── 2b. Generar maestros (LLM) ANTES de la transacción ───────────────────────
        // Se hace fuera de la transacción para no sostenerla durante llamadas de red. El
        // generador NUNCA lanza (fallback determinista) → no rompe el provisioning. El tenant
        // es net-new sin LlmApiKey → el generador usa la key global de config.
        var etapasInfo = (req.Etapas ?? [])
            .Select(e => new StageInfo(e.Nombre, e.Orden)).ToList();
        var generated = new Dictionary<string, GeneratedTemplate>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in req.Agentes)
        {
            generated[seed.Slug] = await generator.GenerateAsync(
                new GenerateTemplateRequest(req.TipoNegocio, seed.Slug, seed.Objetivo, etapasInfo),
                tenantApiKey: null, ct);
        }

        // ── 3. Transacción todo-o-nada ───────────────────────────────────────────────
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var tenantId = Guid.NewGuid();

            // 3.1 Tenant. WhatsApp queda PENDIENTE de conexión (sin token → no envía hasta
            // que un humano conecte la línea UltraMsg). El flag Ludo arranca ACTIVO para el
            // tenant nuevo (es net-new; no afecta a ningún tenant existente).
            var tenant = new Tenant
            {
                Id = tenantId,
                Name = req.NombreNegocio,
                Slug = await UniqueSlugAsync(req.NombreNegocio, req.LudoTenantId, ct),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                WhatsAppProvider = ProviderType.UltraMsg,
                WhatsAppInstanceId = req.WhatsappInstanceId,
                WhatsAppApiToken = string.Empty,   // pendiente de conexión manual
                WhatsAppPhoneNumber = string.Empty,
                LudoIntegrationEnabled = true,
                // API key Anthropic dedicada a los tenants Ludo (inyectada en deploy como
                // Ludo:DefaultLlmApiKey). Si no está configurada → null → el tenant hereda la
                // key global de config, igual que cualquier tenant sin key propia.
                LlmApiKey = config["Ludo:DefaultLlmApiKey"],
            };
            db.Tenants.Add(tenant);

            // 3.2 Sembrar agentes (welcome único ya validado).
            var agentBySlug = new Dictionary<string, AgentDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var seed in req.Agentes)
            {
                var agent = new AgentDefinition
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Name = seed.Slug,
                    Type = MapAgentType(seed.Slug),
                    IsActive = true,
                    SystemPrompt = generated[seed.Slug].SystemPrompt,
                    Language = "es",
                    EnabledChannels = [ChannelType.WhatsApp],
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };
                db.AgentDefinitions.Add(agent);
                agentBySlug[seed.Slug] = agent;
            }

            // 3.2b Lienzo Ludo (pipeline de oportunidades). Se auto-genera desde las etapas
            // (StageLabelMap) + los criterios de avance del agente welcome, y se vincula al
            // maestro welcome vía ActiveFlowId. El Motor de Flujos lo inyecta como "## FLUJO
            // ACTIVO" → el agente emite [ACTION:registrar_oportunidad] / [ACTION:mover_fase].
            // Sin etapas no hay pipeline → no se crea lienzo (LudoFlowBuilder devuelve vacío).
            var welcomeSlug = req.Agentes.First(a => a.Welcome).Slug;
            Guid? ludoFlowId = null;
            if (req.Etapas is { Count: > 0 })
            {
                var flowJson = LudoFlowBuilder.Build(
                    req.NombreNegocio,
                    req.Agentes.First(a => a.Welcome).Objetivo,
                    req.Etapas,
                    generated[welcomeSlug].StageCriteria);
                ludoFlowId = Guid.NewGuid();
                db.TenantFlows.Add(new TenantFlow
                {
                    Id = ludoFlowId.Value,
                    TenantId = tenantId,
                    Name = $"Pipeline Ludo — {req.NombreNegocio}",
                    Description = $"Flujo de oportunidades del agente '{welcomeSlug}'. Generado al aprovisionar desde Ludo.",
                    FlowJson = flowJson,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                });
            }

            // 3.3 Maestros de campaña en BORRADOR (generados con LLM — ver Fase 3).
            var masters = new List<ProvisionedMaster>();
            foreach (var seed in req.Agentes)
            {
                var agent = agentBySlug[seed.Slug];
                var gen = generated[seed.Slug];
                var templateId = Guid.NewGuid();
                var name = $"{req.NombreNegocio} — {seed.Slug}";
                db.CampaignTemplates.Add(new CampaignTemplate
                {
                    Id = templateId,
                    TenantId = tenantId,
                    AgentDefinitionId = agent.Id,
                    Name = name,
                    Objetivo = seed.Objetivo,
                    GeneratedByLlm = gen.UsedLlm,      // true si vino del LLM; false si fallback
                    SystemPrompt = gen.SystemPrompt,
                    IsActive = false,                  // BORRADOR: no entra en producción
                    IsPrimaryForAgent = false,         // un borrador nunca es el primario orgánico
                    // El maestro welcome conduce el pipeline → lleva el lienzo Ludo. Los demás no.
                    ActiveFlowId = seed.Welcome ? ludoFlowId : null,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                });
                masters.Add(new ProvisionedMaster(seed.Slug, templateId, name));
            }

            // 3.4 Homologar etapas como etiquetas + poblar StageLabelMap.
            foreach (var etapa in req.Etapas ?? [])
            {
                // Etiqueta (dedupe por nombre dentro del tenant nuevo — recién creado, así que
                // solo puede chocar con otra etapa del mismo payload con igual nombre).
                var label = await db.ConversationLabels
                    .FirstOrDefaultAsync(l => l.TenantId == tenantId && l.Name == etapa.Nombre, ct);
                if (label is null)
                {
                    label = new ConversationLabel
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        Name = etapa.Nombre,
                        Color = "#6366F1",
                        Keywords = [],
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                    };
                    db.ConversationLabels.Add(label);
                }

                db.StageLabelMaps.Add(new StageLabelMap
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    LudoStageId = etapa.LudoStageId,
                    LabelId = label.Id,
                    Nombre = etapa.Nombre,
                    Orden = etapa.Orden,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                });
            }

            // 3.5 Mapeo idempotente (cierra el ciclo: futuros reenvíos caen en el paso 1).
            db.LudoTenantMaps.Add(new LudoTenantMap
            {
                Id = Guid.NewGuid(),
                LudoTenantId = req.LudoTenantId,
                TenantId = tenantId,
                TipoNegocio = req.TipoNegocio,
                CreatedAt = DateTime.UtcNow,
            });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            log.LogInformation(
                "Tenant aprovisionado desde Ludo: ludoTenantId={LudoTenantId} → tenantId={TenantId}, {Agents} agentes, {Stages} etapas, {Masters} maestros borrador",
                req.LudoTenantId, tenantId, req.Agentes.Count, req.Etapas?.Count ?? 0, masters.Count);

            return ProvisionResult.Created(tenantId, masters);
        }
        catch (Exception ex) when (ex is not ProvisioningValidationException)
        {
            await tx.RollbackAsync(ct);
            log.LogError(ex, "Fallo aprovisionando ludoTenantId={LudoTenantId}; rollback aplicado", req.LudoTenantId);
            throw;
        }
    }

    /// <summary>Mapea el slug del agente al AgentType por heurística simple; default General.</summary>
    private static AgentType MapAgentType(string slug)
    {
        var s = slug.ToLowerInvariant();
        if (s.Contains("cobro")) return AgentType.Cobros;
        if (s.Contains("reclamo")) return AgentType.Reclamos;
        if (s.Contains("renovac")) return AgentType.Renovaciones;
        return AgentType.General;
    }

    /// <summary>Genera un Slug único para el tenant: slugify(nombre)-<6 de ludoTenantId>.</summary>
    private async Task<string> UniqueSlugAsync(string nombre, string ludoTenantId, CancellationToken ct)
    {
        var baseSlug = Slugify(nombre);
        var suffix = new string(ludoTenantId.Where(char.IsLetterOrDigit).Take(6).ToArray());
        var candidate = string.IsNullOrEmpty(suffix) ? baseSlug : $"{baseSlug}-{suffix}".ToLowerInvariant();
        // Colisión improbable; si ocurre, agregar un sufijo incremental corto.
        var slug = candidate;
        var i = 1;
        while (await db.Tenants.AnyAsync(t => t.Slug == slug, ct))
            slug = $"{candidate}-{i++}";
        return slug;
    }

    private static string Slugify(string input)
    {
        var sb = new StringBuilder();
        foreach (var ch in input.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch is '-' or '_') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return string.IsNullOrEmpty(slug) ? "tenant" : slug;
    }
}
