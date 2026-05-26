using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.AI;

/// <summary>
/// Implementación de <see cref="IInitialMessageGenerator"/> que llama a Claude
/// con la API key del tenant. La lógica es idéntica a la del legacy n8n
/// (<c>N8nCallbackController.CampaignSend</c>) — extraída acá para que el
/// dispatcher v2 produzca los mismos mensajes que producía n8n.
/// </summary>
public class InitialMessageGenerator(
    AgentFlowDbContext db,
    ILogger<InitialMessageGenerator> logger) : IInitialMessageGenerator
{
    public async Task<MessageGenerationResult> GenerateAsync(
        Campaign campaign,
        CampaignContact contact,
        CancellationToken ct = default)
    {
        // Resolver prompt: SIEMPRE preferimos la copia local del maestro
        // (CampaignTemplate.SystemPrompt). Es lo que el usuario edita en la UI
        // bajo "Prompt del maestro (copia editable)" — ese debe ser el source
        // of truth. Solo si está vacío caemos al PromptTemplate global como
        // fallback (típicamente en maestros recién creados que no se han editado).
        if (campaign.CampaignTemplate is null)
        {
            campaign.CampaignTemplate = await db.CampaignTemplates
                .FirstOrDefaultAsync(t => t.Id == campaign.CampaignTemplateId, ct);
        }

        string? prompt = campaign.CampaignTemplate?.SystemPrompt;

        if (string.IsNullOrWhiteSpace(prompt))
        {
            var promptIds = campaign.CampaignTemplate?.PromptTemplateIds;
            if (promptIds is null || promptIds.Count == 0)
            {
                logger.LogWarning(
                    "Campaign {Id}: maestro sin SystemPrompt local ni PromptTemplateIds.",
                    campaign.Id);
                return MessageGenerationResult.Fail(MessageGenerationError.NoPrompt);
            }

            var promptId = promptIds[0];
            prompt = await db.PromptTemplates
                .Where(p => p.Id == promptId)
                .Select(p => p.SystemPrompt)
                .FirstOrDefaultAsync(ct);

            if (string.IsNullOrWhiteSpace(prompt))
            {
                logger.LogWarning(
                    "Campaign {Id}: PromptTemplate {Pid} sin SystemPrompt y maestro tampoco lo tiene local.",
                    campaign.Id, promptId);
                return MessageGenerationResult.Fail(MessageGenerationError.EmptyPromptTemplate,
                    $"PromptTemplate {promptId} vacío.");
            }

            logger.LogInformation(
                "Campaign {Id}: usando prompt global del PromptTemplate {Pid} (maestro local vacío).",
                campaign.Id, promptId);
        }

        // Tenant cargado para resolver LlmApiKey.
        if (campaign.Tenant is null)
        {
            campaign.Tenant = await db.Tenants.FindAsync([campaign.TenantId], ct);
        }
        var apiKey = campaign.Tenant?.LlmApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Campaign {Id}: tenant {TenantId} sin LlmApiKey.",
                campaign.Id, campaign.TenantId);
            return MessageGenerationResult.Fail(MessageGenerationError.NoApiKey);
        }

        // ContactDataJson DEBE venir poblado por el upstream (FixedFormatCampaignService
        // o DelinquencyProcessor) con todas las columnas del Excel. Si está vacío es
        // síntoma de un bug en el flujo de carga — no inventamos un JSON sintético acá
        // porque eso oculta el problema y produce mensajes con placeholders sin resolver.
        if (string.IsNullOrWhiteSpace(contact.ContactDataJson))
        {
            logger.LogWarning(
                "Campaign {Id}: contact {Phone} sin ContactDataJson. " +
                "Revisar upstream (FixedFormatCampaignService/DelinquencyProcessor).",
                campaign.Id, contact.PhoneNumber);
            return MessageGenerationResult.Fail(MessageGenerationError.NoContactData);
        }

        // Contexto = registros del JSON + campos directos del contact (resuelve {{NombreCliente}} etc.).
        var ctx = BuildContext(contact.ContactDataJson);
        ctx.TryAdd("NombreCliente",  contact.ClientName    ?? "");
        ctx.TryAdd("NumeroPoliza",   contact.PolicyNumber  ?? "");
        ctx.TryAdd("MontoDeuda",     contact.PendingAmount?.ToString("F2") ?? "0.00");
        ctx.TryAdd("Aseguradora",    contact.InsuranceCompany ?? "");
        ctx.TryAdd("Celular",        contact.PhoneNumber);
        ctx.TryAdd("Email",          contact.Email ?? "");

        var resolvedPrompt = ResolveVariables(prompt, ctx);
        var userMsg = BuildUserMessage(contact.ContactDataJson, ctx);

        try
        {
            var client = new AnthropicClient(apiKey);
            var resp = await client.Messages.GetClaudeMessageAsync(
                new MessageParameters
                {
                    Model       = "claude-haiku-4-5-20251001",
                    MaxTokens   = 512,
                    System      = [new SystemMessage(resolvedPrompt)],
                    Messages    = [new() { Role = RoleType.User, Content = [new TextContent { Text = userMsg }] }],
                    Stream      = false,
                    Temperature = new decimal(0.2),
                }, ct);

            var text = resp.Content.OfType<TextContent>().FirstOrDefault()?.Text?.Trim();
            return string.IsNullOrWhiteSpace(text)
                ? MessageGenerationResult.Fail(MessageGenerationError.EmptyLlmResponse)
                : MessageGenerationResult.Ok(text);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Campaign {Id}: error generando mensaje con Claude.", campaign.Id);
            return MessageGenerationResult.Fail(MessageGenerationError.LlmException,
                detail: ex.Message);
        }
    }

    // ── Helpers (idénticos a CallbackHelpers de N8nCallbackController) ────────

    private static Dictionary<string, string> BuildContext(string? json)
    {
        var ctx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return ctx;
        try
        {
            var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json);
            if (rows is null) return ctx;
            foreach (var row in rows)
                foreach (var (k, v) in row)
                {
                    var s = v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString();
                    ctx.TryAdd(k, s);
                }
            ctx["__TotalRegistros__"] = rows.Count.ToString();
        }
        catch { }
        return ctx;
    }

    private static string ResolveVariables(string template, Dictionary<string, string> ctx)
        => Regex.Replace(template, @"\{\{(\w+)\}\}",
            m => ctx.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    private static string BuildUserMessage(string? json, Dictionary<string, string> ctx)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "Redacta el mensaje para el cliente con los datos disponibles.";
        try
        {
            var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json);
            if (rows is null || rows.Count == 0)
                return "Redacta el mensaje para el cliente con los datos disponibles.";
            if (rows.Count == 1)
            {
                var lines = rows[0].Select(kv => $"- {kv.Key}: {kv.Value}");
                return $"Redacta el mensaje para el cliente:\n{string.Join('\n', lines)}";
            }
            var sb = new StringBuilder();
            sb.AppendLine($"Redacta el mensaje para el cliente con {rows.Count} pólizas:");
            for (var i = 0; i < rows.Count; i++)
            {
                sb.AppendLine($"\nPóliza {i + 1}:");
                foreach (var (k, v) in rows[i]) sb.AppendLine($"  - {k}: {v}");
            }
            return sb.ToString();
        }
        catch { return "Redacta el mensaje para el cliente con los datos disponibles."; }
    }

    /// <summary>
    /// Construye un ContactDataJson sintético cuando el contact no lo trae poblado.
    /// Garantiza que el user message que recibe Claude SIEMPRE contiene los datos
    /// del cliente (cuando existen) — evita que Claude responda "necesito el JSON".
    /// El formato es el mismo que produce n8n / FixedFormatCampaignService:
    /// un array JSON de 1 elemento con los campos disponibles.
    /// </summary>
    private static string BuildJsonFromContact(CampaignContact contact)
    {
        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(contact.ClientName))      data["NombreCliente"]    = contact.ClientName;
        if (!string.IsNullOrWhiteSpace(contact.PhoneNumber))     data["Celular"]          = contact.PhoneNumber;
        if (!string.IsNullOrWhiteSpace(contact.Email))           data["Email"]            = contact.Email;
        if (!string.IsNullOrWhiteSpace(contact.PolicyNumber))    data["NumeroPoliza"]     = contact.PolicyNumber;
        if (!string.IsNullOrWhiteSpace(contact.InsuranceCompany)) data["Aseguradora"]     = contact.InsuranceCompany;
        if (contact.PendingAmount is decimal amt && amt > 0)     data["MontoDeuda"]       = amt.ToString("F2");

        // ExtraData del archivo (columnas adicionales mapeadas como diccionario plano).
        if (contact.ExtraData is { Count: > 0 })
            foreach (var (k, v) in contact.ExtraData)
                data.TryAdd(k, v);

        // Array de 1 elemento — formato consistente con ContactDataJson real.
        return JsonSerializer.Serialize(new[] { data });
    }
}
