using System.Text;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;

namespace AgentFlow.Infrastructure.AI;

/// <summary>
/// Ejecuta el agente llamando a Claude vía Anthropic SDK.
/// Usa la API key del tenant si está disponible; si no, usa la key global (fallback).
/// El system prompt incluye: definición del agente + contexto del cliente + historial.
/// </summary>
public class AnthropicAgentRunner(AnthropicClient anthropic) : IAgentRunner
{
    public async Task<AgentResponse> RunAsync(AgentRunRequest req, CancellationToken ct = default)
    {
        // Si el tenant tiene su propia API key, crear un cliente dedicado para esta petición.
        // Si no, usar el cliente global inyectado (key de appsettings).
        var client = !string.IsNullOrEmpty(req.TenantLlmApiKey)
            ? new AnthropicClient(req.TenantLlmApiKey)
            : anthropic;

        var systemPrompt = BuildSystemPrompt(req);
        var messages     = BuildMessages(req);

        var response = await client.Messages.GetClaudeMessageAsync(
            new MessageParameters
            {
                Model         = req.Agent.LlmModel,
                MaxTokens     = req.Agent.MaxTokens,
                SystemMessage = systemPrompt,
                Messages      = messages
            }, ct);

        var replyText = response.Content.OfType<TextContent>().FirstOrDefault()?.Text ?? string.Empty;

        // Analizar intent desde la respuesta (convención: [INTENT:cobros] al inicio)
        var intent = ExtractIntent(replyText);
        var clean  = CleanIntent(replyText);

        return new AgentResponse(
            clean,
            intent,
            0.9,
            intent == "humano",
            intent == "cierre",
            response.Usage?.InputTokens + response.Usage?.OutputTokens ?? 0
        );
    }

    private static string BuildSystemPrompt(AgentRunRequest req)
    {
        var sb = new StringBuilder();
        sb.AppendLine(req.Agent.SystemPrompt);
        sb.AppendLine();
        sb.AppendLine("## Instrucciones de clasificación");
        sb.AppendLine("Al inicio de tu respuesta incluye una de estas etiquetas:");
        sb.AppendLine("[INTENT:cobros] | [INTENT:reclamos] | [INTENT:renovaciones] | [INTENT:humano] | [INTENT:cierre]");
        sb.AppendLine();

        if (req.ClientContext?.Count > 0)
        {
            sb.AppendLine("## Contexto del cliente");
            foreach (var (k, v) in req.ClientContext)
                sb.AppendLine($"- {k}: {v}");
        }

        return sb.ToString();
    }

    private static List<Anthropic.SDK.Messaging.Message> BuildMessages(AgentRunRequest req)
    {
        var messages = req.RecentHistory
            .TakeLast(10)
            .Select(m => new Anthropic.SDK.Messaging.Message
            {
                Role    = m.IsFromAgent ? RoleType.Assistant : RoleType.User,
                Content = [new TextContent { Text = m.Content }]
            }).ToList();

        messages.Add(new Anthropic.SDK.Messaging.Message
        {
            Role    = RoleType.User,
            Content = [new TextContent { Text = req.IncomingMessage }]
        });

        return messages;
    }

    private static string ExtractIntent(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"\[INTENT:(\w+)\]");
        return match.Success ? match.Groups[1].Value.ToLower() : "cobros";
    }

    private static string CleanIntent(string text)
        => System.Text.RegularExpressions.Regex.Replace(text, @"\[INTENT:\w+\]\s*", "").Trim();
}
