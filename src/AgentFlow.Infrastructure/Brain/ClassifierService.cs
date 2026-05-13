using System.Text;
using System.Text.Json;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.Infrastructure.Brain;

/// <summary>
/// Clasifica la intención del mensaje llamando a Claude con un prompt compacto.
/// Recibe el mensaje actual, historial reciente y la lista de agentes del tenant.
/// Devuelve: intent, agentSlug, confidence, requiresValidation, shouldEscalate.
/// </summary>
public class ClassifierService(
    AnthropicClient anthropic,
    AI.AnthropicSettings settings,
    AgentFlowDbContext db) : IClassifierService
{
    // Haiku 4.5 — el más rápido para respuestas rápidas (ideal para clasificación)
    private const string Model = "claude-haiku-4-5";
    private const int MaxTokens = 256;
    private const decimal Temperature = 0.0m;

    public async Task<ClassificationResult> ClassifyAsync(ClassifierInput input, CancellationToken ct = default)
    {
        // Resolver API key del tenant si existe
        var tenantKey = await db.Tenants
            .Where(t => t.Id == input.TenantId)
            .Select(t => t.LlmApiKey)
            .FirstOrDefaultAsync(ct);

        AnthropicClient client;
        if (!string.IsNullOrEmpty(tenantKey))
            client = new AnthropicClient(tenantKey);
        else if (settings.HasGlobalKey)
            client = anthropic;
        else
            return Fallback(input);

        var systemPrompt = BuildPrompt(input);
        var messages = new List<Anthropic.SDK.Messaging.Message>
        {
            new() { Role = RoleType.User, Content = [new TextContent { Text = input.Message }] }
        };

        try
        {
            var response = await client.Messages.GetClaudeMessageAsync(
                new MessageParameters
                {
                    Model = Model,
                    MaxTokens = MaxTokens,
                    System = [new SystemMessage(systemPrompt)],
                    Messages = messages,
                    Stream = false,
                    Temperature = Temperature
                }, ct);

            var raw = response.Content.OfType<TextContent>().FirstOrDefault()?.Text ?? "";
            return ParseResponse(raw, input);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ClassifierService] Error: {ex.Message} | {ex.InnerException?.Message}");
            return Fallback(input);
        }
    }

    private static string BuildPrompt(ClassifierInput input)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Eres un clasificador de intenciones para un sistema de atención por WhatsApp.");
        sb.AppendLine("Tu trabajo es analizar el mensaje del cliente y decidir qué agente debe atenderlo.");
        sb.AppendLine();
        sb.AppendLine("## Agentes disponibles");
        foreach (var agent in input.AvailableAgents)
        {
            sb.AppendLine($"- slug: \"{agent.Slug}\" | {agent.Name}: {agent.Capabilities}");
        }
        sb.AppendLine();

        if (input.ActiveAgentSlug is not null)
        {
            sb.AppendLine($"## Contexto actual");
            sb.AppendLine($"- Agente activo: {input.ActiveAgentSlug}");
            if (input.ActiveCampaignName is not null)
                sb.AppendLine($"- Campaña activa: {input.ActiveCampaignName}");
            sb.AppendLine();
        }

        if (input.RecentHistory.Count > 0)
        {
            sb.AppendLine("## Últimos mensajes");
            foreach (var msg in input.RecentHistory.TakeLast(3))
            {
                var trimmed = msg.Length > 150 ? msg.Substring(0, 150) + "..." : msg;
                sb.AppendLine($"- {trimmed}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Instrucciones");
        sb.AppendLine("Responde SOLO con un JSON válido, sin texto adicional:");
        sb.AppendLine("""
        {
          "intent": "descripción corta de la intención",
          "agentSlug": "slug del agente más adecuado",
          "confidence": 0.0 a 1.0,
          "requiresValidation": true si pide datos confidenciales (saldos, contratos, pagos),
          "shouldEscalate": true si pide hablar con humano o expresa frustración
        }
        """);
        sb.AppendLine("Si no estás seguro, usa el agente con isWelcome=true y confidence baja.");

        return sb.ToString();
    }

    private static ClassificationResult ParseResponse(string raw, ClassifierInput input)
    {
        try
        {
            // Extraer JSON del response (puede venir con markdown ```json ... ```)
            var json = raw.Trim();
            if (json.StartsWith("```"))
            {
                var start = json.IndexOf('{');
                var end = json.LastIndexOf('}');
                if (start >= 0 && end > start)
                    json = json[start..(end + 1)];
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var intent = root.GetProperty("intent").GetString() ?? "general";
            var agentSlug = root.GetProperty("agentSlug").GetString() ?? "";
            var confidence = root.TryGetProperty("confidence", out var c) ? c.GetDouble() : 0.5;
            var requiresValidation = root.TryGetProperty("requiresValidation", out var rv) && rv.GetBoolean();
            var shouldEscalate = root.TryGetProperty("shouldEscalate", out var se) && se.GetBoolean();

            // Validar que el slug existe en los agentes disponibles
            if (!input.AvailableAgents.Any(a => a.Slug == agentSlug))
            {
                var welcome = input.AvailableAgents.FirstOrDefault(a => a.IsWelcome);
                agentSlug = welcome?.Slug ?? input.AvailableAgents.FirstOrDefault()?.Slug ?? "";
                confidence = Math.Min(confidence, 0.3);
            }

            return new ClassificationResult(intent, agentSlug, confidence, requiresValidation, shouldEscalate);
        }
        catch
        {
            return Fallback(input);
        }
    }

    private static ClassificationResult Fallback(ClassifierInput input)
    {
        var welcome = input.AvailableAgents.FirstOrDefault(a => a.IsWelcome);
        return new ClassificationResult(
            "general",
            welcome?.Slug ?? input.AvailableAgents.FirstOrDefault()?.Slug ?? "welcome",
            0.0,
            false,
            false);
    }
}
