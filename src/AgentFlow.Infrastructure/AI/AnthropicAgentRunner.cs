using System.Text;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;

namespace AgentFlow.Infrastructure.AI;

/// <summary>Configuración global de Anthropic — indica si hay key global disponible.</summary>
public record AnthropicSettings(bool HasGlobalKey);

/// <summary>
/// Ejecuta el agente llamando a Claude vía Anthropic SDK.
/// Usa la API key del tenant si está disponible; si no, usa la key global (fallback).
/// El system prompt incluye: definición del agente + contexto del cliente + historial.
/// </summary>
public class AnthropicAgentRunner(
    AnthropicClient anthropic,
    AnthropicSettings settings,
    System.Net.Http.IHttpClientFactory httpClientFactory) : IAgentRunner
{
    public async Task<AgentResponse> RunAsync(AgentRunRequest req, CancellationToken ct = default)
    {
        // Si el tenant tiene su propia API key, crear un cliente dedicado para esta petición.
        // Si no, usar el cliente global inyectado (key de appsettings).
        AnthropicClient client;
        if (!string.IsNullOrEmpty(req.TenantLlmApiKey))
        {
            client = new AnthropicClient(req.TenantLlmApiKey);
        }
        else if (settings.HasGlobalKey)
        {
            client = anthropic;
        }
        else
        {
            // Ni key de tenant ni key global — el tenant no tiene LLM configurado
            const string noKey = "Hola, estoy aquí para ayudarte. Un ejecutivo se comunicará contigo pronto.";
            return new AgentResponse(noKey, "general", 0, false, false, 0);
        }

        var systemPrompt = BuildSystemPrompt(req);
        var messages     = await BuildMessagesAsync(req, ct);

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

    private async Task<List<Anthropic.SDK.Messaging.Message>> BuildMessagesAsync(
        AgentRunRequest req, CancellationToken ct)
    {
        var messages = req.RecentHistory
            .TakeLast(10)
            .Select(m => new Anthropic.SDK.Messaging.Message
            {
                Role    = m.IsFromAgent ? RoleType.Assistant : RoleType.User,
                Content = [new TextContent { Text = StripMediaTag(m.Content) }]
            }).ToList();

        // Mensaje actual — si es imagen, incluir bloque de visión
        var userContent = new List<ContentBase>();

        if (req.MediaType == "image" && !string.IsNullOrEmpty(req.MediaUrl))
        {
            try
            {
                var http = httpClientFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(15);
                var imageBytes = await http.GetByteArrayAsync(req.MediaUrl, ct);
                var base64 = Convert.ToBase64String(imageBytes);

                // Detectar media type por URL o usar jpeg como fallback
                var mimeType = req.MediaUrl.Contains(".png") ? "image/png"
                    : req.MediaUrl.Contains(".gif") ? "image/gif"
                    : req.MediaUrl.Contains(".webp") ? "image/webp"
                    : "image/jpeg";

                userContent.Add(new ImageContent
                {
                    Source = new ImageSource
                    {
                        MediaType = mimeType,
                        Data      = base64
                    }
                });

                // Añadir texto descriptivo
                var caption = string.IsNullOrWhiteSpace(req.IncomingMessage) ||
                              req.IncomingMessage.StartsWith("📷")
                    ? "El cliente envió esta imagen."
                    : req.IncomingMessage;
                userContent.Add(new TextContent { Text = caption });

                Console.WriteLine($"[Vision] Imagen enviada a Claude ({imageBytes.Length / 1024}KB, {mimeType})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Vision] No se pudo descargar imagen: {ex.Message}");
                userContent.Add(new TextContent { Text = req.IncomingMessage });
            }
        }
        else
        {
            userContent.Add(new TextContent { Text = req.IncomingMessage });
        }

        messages.Add(new Anthropic.SDK.Messaging.Message
        {
            Role    = RoleType.User,
            Content = userContent
        });

        return messages;
    }

    /// <summary>Limpia el tag [media:URL] del contenido antes de enviarlo al historial del LLM.</summary>
    private static string StripMediaTag(string content)
    {
        var idx = content.IndexOf("\n[media:", StringComparison.Ordinal);
        return idx >= 0 ? content[..idx].Trim() : content;
    }

    private static string ExtractIntent(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"\[INTENT:(\w+)\]");
        return match.Success ? match.Groups[1].Value.ToLower() : "cobros";
    }

    private static string CleanIntent(string text)
        => System.Text.RegularExpressions.Regex.Replace(text, @"\[INTENT:\w+\]\s*", "").Trim();
}
