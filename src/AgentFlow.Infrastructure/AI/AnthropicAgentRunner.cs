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
/// Soporta visión de imágenes y lectura de PDFs.
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

        // SDK v5: System es List<SystemMessage>; Stream=false requerido para llamada síncrona
        var response = await client.Messages.GetClaudeMessageAsync(
            new MessageParameters
            {
                Model       = req.Agent.LlmModel,
                MaxTokens   = req.Agent.MaxTokens,
                System      = [new SystemMessage(systemPrompt)],
                Messages    = messages,
                Stream      = false,
                Temperature = (decimal)req.Agent.Temperature
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

        // Mensaje actual — construir el bloque de contenido según el tipo de media
        var userContent = new List<ContentBase>();

        if (req.MediaType == "image" && !string.IsNullOrEmpty(req.MediaUrl))
        {
            // ── Visión de imagen ──────────────────────────────────────────
            try
            {
                var http = httpClientFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(20);
                var imageBytes = await http.GetByteArrayAsync(req.MediaUrl, ct);
                var base64     = Convert.ToBase64String(imageBytes);

                var mimeType = req.MediaUrl.Contains(".png",  StringComparison.OrdinalIgnoreCase) ? "image/png"
                             : req.MediaUrl.Contains(".gif",  StringComparison.OrdinalIgnoreCase) ? "image/gif"
                             : req.MediaUrl.Contains(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp"
                             : "image/jpeg";

                userContent.Add(new ImageContent
                {
                    Source = new ImageSource { MediaType = mimeType, Data = base64 }
                });

                // Si el IncomingMessage es el caption real del cliente, usarlo.
                // Si solo es el marcador "📷 [Imagen]", usar texto genérico.
                var caption = string.IsNullOrWhiteSpace(req.IncomingMessage) ||
                              req.IncomingMessage is "📷 [Imagen]" or "📷 [Imagen recibida]"
                    ? "El cliente envió esta imagen. Descríbela y responde según el contexto."
                    : req.IncomingMessage;
                userContent.Add(new TextContent { Text = caption });

                Console.WriteLine($"[Vision] Imagen ({imageBytes.Length / 1024}KB, {mimeType}) → Claude");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Vision] Error descargando imagen: {ex.Message}");
                userContent.Add(new TextContent { Text = req.IncomingMessage });
            }
        }
        else if (req.MediaType == "document" && !string.IsNullOrEmpty(req.MediaUrl))
        {
            // ── Lectura de documento (PDF) ────────────────────────────────
            // UltraMsg puede enviar PDFs con extensión .bin — detectamos por magic bytes
            try
            {
                var http = httpClientFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(30);
                var docBytes = await http.GetByteArrayAsync(req.MediaUrl, ct);

                // Verificar si es un PDF por los primeros bytes: %PDF- = 0x25 0x50 0x44 0x46
                var isPdf = docBytes.Length > 4 &&
                            docBytes[0] == 0x25 && docBytes[1] == 0x50 &&
                            docBytes[2] == 0x44 && docBytes[3] == 0x46;

                if (isPdf)
                {
                    var base64 = Convert.ToBase64String(docBytes);
                    userContent.Add(new DocumentContent
                    {
                        Source = new DocumentSource
                        {
                            Type      = SourceType.base64,
                            MediaType = "application/pdf",
                            Data      = base64
                        }
                    });

                    var docCaption = string.IsNullOrWhiteSpace(req.IncomingMessage) ||
                                     req.IncomingMessage is "📄 [Documento]"
                        ? "El cliente envió este documento PDF. Léelo y responde según el contexto."
                        : req.IncomingMessage;
                    userContent.Add(new TextContent { Text = docCaption });

                    Console.WriteLine($"[DocVision] PDF ({docBytes.Length / 1024}KB) → Claude");
                }
                else
                {
                    // Documento no PDF (Word, etc.) — solo texto descriptivo
                    Console.WriteLine($"[DocVision] Documento no-PDF ({docBytes.Length / 1024}KB), enviando texto");
                    userContent.Add(new TextContent { Text = req.IncomingMessage });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DocVision] Error descargando documento: {ex.Message}");
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
