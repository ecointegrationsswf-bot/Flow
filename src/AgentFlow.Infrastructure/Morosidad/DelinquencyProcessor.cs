using System.Text.Json;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Helpers;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
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
    ICampaignLauncher campaignLauncher,
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

                // Construir registro para ContactDataJson — mismo shape que FixedFormatCampaignService
                var registro = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["NombreCliente"] = item.ClientName ?? string.Empty,
                    ["KeyValue"]      = item.KeyValue ?? string.Empty,
                    ["KeyValueLabel"] = keyValueLabel
                };
                foreach (var kv in extracted)
                {
                    // Saltar los que ya están como semánticos para no duplicar
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

            // ── 6. Auto-crear UNA campaña con todos los grupos como contactos ─────
            if (config.AutoCrearCampanas && config.CampaignTemplateId.HasValue && groups.Count > 0)
            {
                var campaignId = await CreateSingleCampaignAsync(config, groups, groupRecords, execution, tenantId, ct);
                if (campaignId.HasValue)
                {
                    execution.CampaignsCreated = 1;
                    // Persistir antes de lanzar para que el launcher vea la campaña + contactos.
                    await db.SaveChangesAsync(ct);

                    // Auto-lanzar — el ejecutivo no debe presionar "Lanzar" para descargas automáticas.
                    var launchResult = await campaignLauncher.LaunchAsync(
                        campaignId.Value,
                        launchedByUserId: "system:download",
                        launchedByUserPhone: null,
                        ct);

                    if (!launchResult.Success)
                    {
                        logger.LogError("[Download] Auto-launch falló para campaign {CampaignId}: {Error}",
                            campaignId, launchResult.Error);
                        execution.ErrorMessage = $"Campaña creada pero el auto-launch falló: {launchResult.Error}";
                    }
                    else
                    {
                        logger.LogInformation("[Download] Campaign {CampaignId} auto-lanzada — status={Status}, pending={Pending}",
                            campaignId, launchResult.Status, launchResult.PendingContacts);
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
    /// Crea UNA campaña con todos los grupos como contactos. Trigger=DelinquencyEvent
    /// para que se distinga visualmente de las campañas manuales/Excel.
    /// </summary>
    private async Task<Guid?> CreateSingleCampaignAsync(
        ActionDelinquencyConfig config,
        List<ContactGroup> groups,
        Dictionary<string, List<Dictionary<string, object?>>> groupRecords,
        DelinquencyExecution execution,
        Guid tenantId,
        CancellationToken ct)
    {
        if (!config.CampaignTemplateId.HasValue) return null;

        // El agente lo aporta el maestro de campaña.
        var templateAgentId = await db.CampaignTemplates
            .Where(t => t.Id == config.CampaignTemplateId.Value)
            .Select(t => (Guid?)t.AgentDefinitionId)
            .FirstOrDefaultAsync(ct);

        if (templateAgentId == null || templateAgentId == Guid.Empty)
        {
            logger.LogWarning("[Download] El maestro {TemplateId} no tiene agente asignado — omitiendo creación de campaña", config.CampaignTemplateId);
            return null;
        }

        var action       = await db.ActionDefinitions.FindAsync([config.ActionDefinitionId], ct);
        var dateStr      = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var actionName   = action?.Description ?? action?.Name ?? "Descarga";
        var namePattern  = config.CampaignNamePattern ?? "{accion} {fecha}";
        var campaignName = namePattern
            .Replace("{fecha}", dateStr)
            .Replace("{fecha:yyyy-MM-dd}", dateStr)
            .Replace("{accion}", actionName)
            .Replace("{grupos}", groups.Count.ToString());

        var campaign = new Campaign
        {
            Id                 = Guid.NewGuid(),
            TenantId           = tenantId,
            Name               = campaignName,
            Status             = CampaignStatus.Pending,
            Trigger            = CampaignTrigger.DelinquencyEvent,
            Channel            = ChannelType.WhatsApp,
            CampaignTemplateId = config.CampaignTemplateId,
            AgentDefinitionId  = templateAgentId.Value,
            CreatedAt          = DateTime.UtcNow,
            CreatedByUserId    = "system:download",
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
