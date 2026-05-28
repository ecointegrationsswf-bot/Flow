using System.Text.Json;
using AgentFlow.Application.Modules.Campaigns.LaunchV2;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Helpers;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Morosidad;

/// <summary>
/// Procesador genérico de descargas. Recibe un payload JSON, lee el mapping de columnas
/// definido para la acción (con roles semánticos Phone/ClientName/KeyValue obligatorios y
/// Amount/PolicyNumber opcionales) y persiste:
///   — DelinquencyExecution (1 registro por ejecución)
///   — DelinquencyItem (1 por fila del array, con todos los campos extraídos en ExtractedDataJson)
///   — ContactGroup (1 por teléfono normalizado, con totales agregados)
///   — Opcional: Campaign + CampaignContact si AutoCrearCampanas=true
/// </summary>
public class DelinquencyProcessor(
    AgentFlowDbContext db,
    IMediator mediator,
    ILogger<DelinquencyProcessor> logger) : IDelinquencyProcessor
{
    public async Task<Guid> ProcessAsync(
        Guid tenantId,
        Guid actionDefinitionId,
        string jsonPayload,
        Guid? scheduledJobId = null,
        CancellationToken ct = default)
    {
        // ── 1. Cargar configuración y mappings ──────────────────────────────
        var config = await db.ActionDelinquencyConfigs
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.ActionDefinitionId == actionDefinitionId && c.IsActive, ct);

        if (config == null)
        {
            logger.LogWarning("[Download] Sin ActionDelinquencyConfig para action {ActionId} tenant {TenantId}", actionDefinitionId, tenantId);
            throw new InvalidOperationException($"No hay configuración de descarga activa para la acción {actionDefinitionId}.");
        }

        var mappings = await db.ActionFieldMappings
            .Where(m => m.ActionDefinitionId == actionDefinitionId && m.IsEnabled)
            .OrderBy(m => m.SortOrder)
            .ToListAsync(ct);

        // Resolución por rol — los 3 obligatorios deben existir
        var phoneMapping     = mappings.FirstOrDefault(m => m.Role == FieldRole.Phone);
        var nameMapping      = mappings.FirstOrDefault(m => m.Role == FieldRole.ClientName);
        var keyValueMapping  = mappings.FirstOrDefault(m => m.Role == FieldRole.KeyValue);
        var amountMapping    = mappings.FirstOrDefault(m => m.Role == FieldRole.Amount);
        var policyMapping    = mappings.FirstOrDefault(m => m.Role == FieldRole.PolicyNumber);
        // Opcional — email del ejecutivo de cobros. Solo se usa si SplitCampaignsByExecutive=true.
        var execEmailMapping = mappings.FirstOrDefault(m => m.Role == FieldRole.ExecutiveEmail);
        // Opcional — celular del ejecutivo. Se usa para actualizar el NotifyPhone del perfil matcheado.
        var execPhoneMapping = mappings.FirstOrDefault(m => m.Role == FieldRole.ExecutivePhone);

        if (phoneMapping == null)
            throw new InvalidOperationException("La acción no tiene un campo con Rol=Phone configurado.");
        if (nameMapping == null)
            throw new InvalidOperationException("La acción no tiene un campo con Rol=ClientName configurado.");
        if (keyValueMapping == null)
            throw new InvalidOperationException("La acción no tiene un campo con Rol=KeyValue configurado.");

        var keyValueLabel = keyValueMapping.RoleLabel ?? keyValueMapping.DisplayName;

        // ── 2. Crear registro de ejecución ──────────────────────────────────
        var execution = new DelinquencyExecution
        {
            Id                    = Guid.NewGuid(),
            ActionDefinitionId    = actionDefinitionId,
            TenantId              = tenantId,
            ScheduledWebhookJobId = scheduledJobId,
            Status                = DelinquencyExecutionStatus.Running,
            StartedAt             = DateTime.UtcNow
        };

        db.DelinquencyExecutions.Add(execution);
        await db.SaveChangesAsync(ct);

        try
        {
            // ── 3. Parsear el JSON ──────────────────────────────────────────
            JsonElement[] items = ExtractItemsArray(jsonPayload, config.ItemsJsonPath);
            execution.TotalItems = items.Length;

            // ── 4. Procesar ítems ───────────────────────────────────────────
            var itemEntities = new List<DelinquencyItem>(items.Length);
            var groupMap     = new Dictionary<string, ContactGroup>();
            // Para cada grupo acumulamos los registros (NombreCliente + KeyValue + extras) que luego
            // se serializan como ContactDataJson — mismo formato que FixedFormatCampaignService.
            var groupRecords = new Dictionary<string, List<Dictionary<string, object?>>>();
            // Email del ejecutivo de cobros por teléfono (primer valor no vacío gana).
            // Solo se llena si hay columna mapeada con Rol=ExecutiveEmail. Se usa al
            // final para partir las campañas por ejecutivo (si el flag está activo).
            var groupExecutiveEmail = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            // Celular del ejecutivo por teléfono (primer valor no vacío gana) — para
            // actualizar el NotifyPhone del perfil matcheado.
            var groupExecutivePhone = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < items.Length; i++)
            {
                var element = items[i];
                var item = new DelinquencyItem
                {
                    Id          = Guid.NewGuid(),
                    ExecutionId = execution.Id,
                    RowIndex    = i,
                    RawData     = element.GetRawText()
                };

                // ── Extraer TODOS los campos del mapping ──────────────────
                var extracted = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in mappings)
                {
                    var value = FieldMappingExtractor.Extract(element, m.JsonPath, m.DefaultValue);
                    extracted[m.ColumnKey] = value;
                }

                // Asignar campos tipados desde los roles
                item.PhoneRaw     = extracted.GetValueOrDefault(phoneMapping.ColumnKey);
                item.ClientName   = extracted.GetValueOrDefault(nameMapping.ColumnKey);
                item.KeyValue     = extracted.GetValueOrDefault(keyValueMapping.ColumnKey);
                item.PolicyNumber = policyMapping != null ? extracted.GetValueOrDefault(policyMapping.ColumnKey) : null;

                if (amountMapping != null)
                {
                    var amountStr = extracted.GetValueOrDefault(amountMapping.ColumnKey);
                    if (!string.IsNullOrEmpty(amountStr) &&
                        decimal.TryParse(amountStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amount))
                        item.Amount = amount;
                }

                item.ExtractedDataJson = JsonSerializer.Serialize(extracted);

                // Normalizar teléfono
                item.PhoneNormalized = PhoneNormalizer.Normalize(item.PhoneRaw, config.CodigoPais);

                if (!PhoneNormalizer.IsValid(item.PhoneNormalized))
                {
                    item.Status        = DelinquencyItemStatus.Discarded;
                    item.DiscardReason = string.IsNullOrWhiteSpace(item.PhoneRaw)
                        ? "Campo Teléfono vacío"
                        : $"Teléfono inválido: '{item.PhoneRaw}'";
                    execution.DiscardedItems++;
                    itemEntities.Add(item);
                    continue;
                }

                // Agrupar por teléfono
                var phone = item.PhoneNormalized!;
                if (!groupMap.TryGetValue(phone, out var group))
                {
                    group = new ContactGroup
                    {
                        Id              = Guid.NewGuid(),
                        ExecutionId     = execution.Id,
                        TenantId        = tenantId,
                        PhoneNormalized = phone,
                        ClientName      = item.ClientName,
                        CreatedAt       = DateTime.UtcNow
                    };
                    groupMap[phone]     = group;
                    groupRecords[phone] = [];
                }
                else if (string.IsNullOrEmpty(group.ClientName) && !string.IsNullOrEmpty(item.ClientName))
                {
                    group.ClientName = item.ClientName;
                }

                group.TotalAmount += item.Amount ?? 0;
                group.ItemCount++;

                // Capturar email del ejecutivo (si está mapeado) — primer no-vacío gana.
                if (execEmailMapping != null && !groupExecutiveEmail.ContainsKey(phone))
                {
                    var execEmail = extracted.GetValueOrDefault(execEmailMapping.ColumnKey);
                    if (!string.IsNullOrWhiteSpace(execEmail))
                        groupExecutiveEmail[phone] = execEmail.Trim();
                }
                // Capturar celular del ejecutivo (si está mapeado) — primer no-vacío gana.
                if (execPhoneMapping != null && !groupExecutivePhone.ContainsKey(phone))
                {
                    var execPhone = extracted.GetValueOrDefault(execPhoneMapping.ColumnKey);
                    if (!string.IsNullOrWhiteSpace(execPhone))
                        groupExecutivePhone[phone] = execPhone.Trim();
                }

                // Construir registro para ContactDataJson — mismo shape que FixedFormatCampaignService.
                // Estrategia: incluir TODAS las propiedades del JSON original descargado para
                // que Claude tenga acceso a cualquier campo que el prompt del template necesite
                // (ej: "telefono ejecutivo de Cobro", "Ramo", "Link de pago", etc.). Después los
                // mapeados sobrescriben/agregan con sus ColumnKey legibles definidos por el admin.
                var registro = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["NombreCliente"] = item.ClientName ?? string.Empty,
                    ["KeyValue"]      = item.KeyValue ?? string.Empty,
                    ["KeyValueLabel"] = keyValueLabel
                };

                // 1) Volcar TODAS las propiedades del JSON original — sin filtrar por mapping.
                if (element.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in element.EnumerateObject())
                    {
                        var stringValue = prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => prop.Value.GetString(),
                            JsonValueKind.Null   => null,
                            JsonValueKind.True   => "true",
                            JsonValueKind.False  => "false",
                            JsonValueKind.Number => prop.Value.ToString(),
                            _                    => prop.Value.GetRawText()
                        };
                        if (!string.IsNullOrEmpty(stringValue))
                            registro[prop.Name] = stringValue;
                    }
                }

                // 2) Los mapeados sobrescriben con su ColumnKey legible (alias del admin).
                foreach (var kv in extracted)
                {
                    if (kv.Key == phoneMapping.ColumnKey ||
                        kv.Key == nameMapping.ColumnKey  ||
                        kv.Key == keyValueMapping.ColumnKey)
                        continue;
                    if (!string.IsNullOrEmpty(kv.Value))
                        registro[kv.Key] = kv.Value;
                }
                groupRecords[phone].Add(registro);

                item.Status  = DelinquencyItemStatus.Grouped;
                item.GroupId = group.Id;

                execution.ProcessedItems++;
                itemEntities.Add(item);
            }

            // ── 5. Persistir grupos e ítems ─────────────────────────────────
            var groups = groupMap.Values.ToList();
            execution.GroupsCreated = groups.Count;

            db.ContactGroups.AddRange(groups);
            db.DelinquencyItems.AddRange(itemEntities);

            // ── 6. Auto-crear campañas con los grupos como contactos ────────────
            //   Default: UNA campaña con todos los grupos (LaunchedByUserId=system:download).
            //   Si SplitCampaignsByExecutive=true: UNA campaña por ejecutivo de cobros
            //   (match del email contra AppUsers); las filas sin match caen a system:download.
            if (config.AutoCrearCampanas && config.CampaignTemplateId.HasValue && groups.Count > 0)
            {
                // Resolver agente del maestro una sola vez (lo comparten todas las campañas).
                var templateAgentId = await db.CampaignTemplates
                    .Where(t => t.Id == config.CampaignTemplateId!.Value)
                    .Select(t => (Guid?)t.AgentDefinitionId)
                    .FirstOrDefaultAsync(ct);

                if (templateAgentId is null || templateAgentId == Guid.Empty)
                {
                    logger.LogWarning("[Download] El maestro {TemplateId} no tiene agente asignado — omitiendo creación de campaña", config.CampaignTemplateId);
                }
                else
                {
                    // Construir el plan de campañas: lista de (LaunchedByUserId, LaunchedByPhone, grupos).
                    var plan = config.SplitCampaignsByExecutive
                        ? await BuildExecutivePlanAsync(groups, groupExecutiveEmail, groupExecutivePhone, config.CodigoPais, tenantId, ct)
                        : new List<CampaignPlanGroup> { new("system:download", null, null, groups) };

                    var createdCampaigns = new List<(Guid CampaignId, string LaunchedBy, string? LaunchedByPhone)>();
                    foreach (var planGroup in plan)
                    {
                        if (planGroup.Groups.Count == 0) continue;
                        var campaignId = await CreateCampaignAsync(
                            config, templateAgentId.Value, planGroup, groupRecords, execution, tenantId, ct);
                        if (campaignId.HasValue)
                            createdCampaigns.Add((campaignId.Value, planGroup.LaunchedByUserId, planGroup.LaunchedByPhone));
                    }

                    execution.CampaignsCreated = createdCampaigns.Count;
                    // Persistir antes de lanzar para que el handler vea las campañas + contactos
                    // (y persiste también los NotifyPhone actualizados de los ejecutivos).
                    await db.SaveChangesAsync(ct);

                    // Auto-lanzar SOLO si el flag está activo. Si AutoLaunchCampaigns=false,
                    // las campañas quedan en Pending (inertes) para revisión/lanzamiento manual.
                    if (!config.AutoLaunchCampaigns)
                    {
                        logger.LogInformation(
                            "[Download] {N} campañas creadas en Pending SIN lanzar (AutoLaunchCampaigns=false). El operador las lanza desde el portal.",
                            createdCampaigns.Count);
                    }
                    else
                    {
                        // Auto-lanzar v2 cada campaña (en proceso, sin n8n).
                        foreach (var (campaignId, launchedBy, launchedByPhone) in createdCampaigns)
                        {
                            var launchResult = await mediator.Send(new LaunchCampaignV2Command(
                                CampaignId: campaignId,
                                TenantId: tenantId,
                                LaunchedByUserId: launchedBy,
                                LaunchedByUserPhone: launchedByPhone,
                                WarmupDay: 0
                            ), ct);

                            if (!launchResult.Success)
                            {
                                logger.LogError("[Download] Auto-launch v2 falló para campaign {CampaignId} ({LaunchedBy}): {Error}",
                                    campaignId, launchedBy, launchResult.Error);
                                execution.ErrorMessage = $"Campaña {campaignId} creada pero el auto-launch v2 falló: {launchResult.Error}";
                            }
                            else
                            {
                                logger.LogInformation(
                                    "[Download] Campaign {CampaignId} ({LaunchedBy}) auto-lanzada v2 — queued={Queued}, deferred={Deferred}, duplicate={Duplicate}, skipped={Skipped}",
                                    campaignId, launchedBy, launchResult.QueuedCount, launchResult.DeferredCount,
                                    launchResult.DuplicateCount, launchResult.SkippedCount);
                            }
                        }
                    }
                }
            }

            execution.Status      = execution.DiscardedItems > 0 && execution.ProcessedItems == 0
                ? DelinquencyExecutionStatus.Failed
                : execution.DiscardedItems > 0
                    ? DelinquencyExecutionStatus.PartiallyFailed
                    : DelinquencyExecutionStatus.Completed;
            execution.CompletedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);

            logger.LogInformation("[Download] Ejecución {ExId} completada — {Items} ítems, {Groups} grupos, {Campaigns} campañas, {Discarded} descartados",
                execution.Id, execution.TotalItems, execution.GroupsCreated, execution.CampaignsCreated, execution.DiscardedItems);

            return execution.Id;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Download] Error procesando ejecución {ExId}", execution.Id);
            execution.Status       = DelinquencyExecutionStatus.Failed;
            execution.ErrorMessage = ex.Message;
            execution.CompletedAt  = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            throw;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static JsonElement[] ExtractItemsArray(string jsonPayload, string? itemsJsonPath)
    {
        var doc  = JsonDocument.Parse(jsonPayload);
        var root = doc.RootElement;

        if (!string.IsNullOrWhiteSpace(itemsJsonPath))
        {
            root = NavigateToElement(root, itemsJsonPath) ?? root;
        }

        if (root.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("El payload del webhook no es un array JSON ni el ItemsJsonPath apunta a uno.");

        return root.EnumerateArray().ToArray();
    }

    private static JsonElement? NavigateToElement(JsonElement element, string path)
    {
        try
        {
            var normalized = path.Trim().TrimStart('$').TrimStart('.');
            if (string.IsNullOrEmpty(normalized)) return element;

            var parts = normalized.Split('.');
            var current = element;
            foreach (var part in parts)
            {
                if (current.ValueKind != JsonValueKind.Object) return null;
                if (!current.TryGetProperty(part, out current)) return null;
            }
            return current;
        }
        catch { return null; }
    }

    /// <summary>
    /// Plan de una campaña a crear: a quién se le atribuye (LaunchedByUserId),
    /// su teléfono de notificación opcional, el nombre del ejecutivo para el
    /// título de la campaña, y los grupos (teléfonos) que la componen.
    /// </summary>
    private sealed record CampaignPlanGroup(
        string LaunchedByUserId,
        string? LaunchedByPhone,
        string? ExecutiveName,
        List<ContactGroup> Groups);

    /// <summary>
    /// Arma el plan de campañas por ejecutivo. Matchea el email de cada grupo
    /// (capturado del archivo) contra los AppUsers del tenant por Email
    /// (case-insensitive). Los grupos cuyo email matchea un usuario van a la
    /// campaña de ese usuario (LaunchedByUserId = su Email, para mantener el
    /// mismo formato que las campañas manuales). Los que no matchean — o sin
    /// email — caen al grupo "system:download".
    /// </summary>
    private async Task<List<CampaignPlanGroup>> BuildExecutivePlanAsync(
        List<ContactGroup> groups,
        Dictionary<string, string?> groupExecutiveEmail,
        Dictionary<string, string?> groupExecutivePhone,
        string codigoPais,
        Guid tenantId,
        CancellationToken ct)
    {
        // Usuarios del tenant — TRACKED (los modificamos para actualizar NotifyPhone).
        // Incluimos inactivos: se les atribuye igual para histórico.
        var users = await db.AppUsers
            .Where(u => u.TenantId == tenantId && u.Email != null && u.Email != "")
            .ToListAsync(ct);
        var userByEmail = users
            .GroupBy(u => u.Email!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Agrupar los grupos por la clave de atribución.
        var byKey = new Dictionary<string, CampaignPlanGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            string launchedBy = "system:download";
            string? launchedPhone = null;
            string? execName = null;

            var rawEmail = groupExecutiveEmail.GetValueOrDefault(group.PhoneNormalized);
            if (!string.IsNullOrWhiteSpace(rawEmail)
                && userByEmail.TryGetValue(rawEmail.Trim(), out var matchedUser))
            {
                // Actualizar el NotifyPhone del ejecutivo desde el archivo (fuente de
                // verdad → sobrescribe). PhoneNormalizer agrega +507 si no viene.
                // Si el archivo trae el celular vacío, NO se toca el valor actual.
                var rawPhone = groupExecutivePhone.GetValueOrDefault(group.PhoneNormalized);
                if (!string.IsNullOrWhiteSpace(rawPhone))
                {
                    var normalized = PhoneNormalizer.Normalize(rawPhone, codigoPais);
                    if (!string.IsNullOrWhiteSpace(normalized) && normalized != matchedUser.NotifyPhone)
                    {
                        logger.LogInformation(
                            "[Download] NotifyPhone de {Email} actualizado: '{Old}' -> '{New}' (desde archivo)",
                            matchedUser.Email, matchedUser.NotifyPhone ?? "(vacío)", normalized);
                        matchedUser.NotifyPhone = normalized;
                    }
                }

                launchedBy    = matchedUser.Email!.Trim();
                launchedPhone = matchedUser.NotifyPhone;
                execName      = matchedUser.FullName;
            }

            if (!byKey.TryGetValue(launchedBy, out var plan))
            {
                plan = new CampaignPlanGroup(launchedBy, launchedPhone, execName, []);
                byKey[launchedBy] = plan;
            }
            plan.Groups.Add(group);
        }

        // Los cambios en NotifyPhone se persisten con el SaveChangesAsync que el
        // caller (ProcessAsync) hace antes de lanzar las campañas.
        return byKey.Values.ToList();
    }

    /// <summary>
    /// Crea UNA campaña con los grupos del plan como contactos. Trigger=DelinquencyEvent
    /// para que se distinga visualmente de las campañas manuales/Excel. El
    /// LaunchedByUserId/CreatedByUserId sale del plan (email del ejecutivo o
    /// "system:download"). El nombre incluye al ejecutivo cuando matcheó.
    /// </summary>
    private async Task<Guid?> CreateCampaignAsync(
        ActionDelinquencyConfig config,
        Guid templateAgentId,
        CampaignPlanGroup plan,
        Dictionary<string, List<Dictionary<string, object?>>> groupRecords,
        DelinquencyExecution execution,
        Guid tenantId,
        CancellationToken ct)
    {
        if (!config.CampaignTemplateId.HasValue) return null;

        var groups       = plan.Groups;
        var action       = await db.ActionDefinitions.FindAsync([config.ActionDefinitionId], ct);
        var dateStr      = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var actionName   = action?.Description ?? action?.Name ?? "Descarga";
        var namePattern  = config.CampaignNamePattern ?? "{accion} {fecha}";
        var campaignName = namePattern
            .Replace("{fecha}", dateStr)
            .Replace("{fecha:yyyy-MM-dd}", dateStr)
            .Replace("{accion}", actionName)
            .Replace("{grupos}", groups.Count.ToString());
        // Sufijo con el ejecutivo cuando la campaña es de un usuario matcheado.
        if (!string.IsNullOrWhiteSpace(plan.ExecutiveName))
            campaignName = $"{campaignName} — {plan.ExecutiveName}";

        var campaign = new Campaign
        {
            Id                 = Guid.NewGuid(),
            TenantId           = tenantId,
            Name               = campaignName,
            Status             = CampaignStatus.Pending,
            Trigger            = CampaignTrigger.DelinquencyEvent,
            Channel            = ChannelType.WhatsApp,
            CampaignTemplateId = config.CampaignTemplateId,
            AgentDefinitionId  = templateAgentId,
            CreatedAt          = DateTime.UtcNow,
            CreatedByUserId    = plan.LaunchedByUserId,
            TotalContacts      = groups.Count
        };
        db.Campaigns.Add(campaign);

        var contactsToInsert = new List<CampaignContact>(groups.Count);
        foreach (var group in groups)
        {
            // ContactDataJson — array de registros con NombreCliente + KeyValue + extras.
            // Mismo formato que FixedFormatCampaignService — el dispatcher no necesita ramificar.
            var registros = groupRecords.GetValueOrDefault(group.PhoneNormalized) ?? [];
            var contactDataJson = JsonSerializer.Serialize(registros, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = null
            });

            contactsToInsert.Add(new CampaignContact
            {
                Id              = Guid.NewGuid(),
                CampaignId      = campaign.Id,
                PhoneNumber     = group.PhoneNormalized,
                ClientName      = group.ClientName,
                PendingAmount   = group.TotalAmount,
                ContactDataJson = contactDataJson,
                ExtraData       = new Dictionary<string, string>
                {
                    ["itemCount"]   = group.ItemCount.ToString(),
                    ["executionId"] = execution.Id.ToString()
                }
            });

            group.CampaignId = campaign.Id;
            group.Status     = ContactGroupStatus.CampaignCreated;
        }
        db.CampaignContacts.AddRange(contactsToInsert);

        return campaign.Id;
    }
}
