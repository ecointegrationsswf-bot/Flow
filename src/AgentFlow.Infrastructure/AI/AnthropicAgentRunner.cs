using System.Text;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Caching.Memory;

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
    System.Net.Http.IHttpClientFactory httpClientFactory,
    IMemoryCache cache) : IAgentRunner
{
    // Caché de PDFs de referencia descargados. Clave: BlobUrl. TTL 30 min.
    // Evita descargar el mismo PDF del maestro de campaña en cada mensaje.
    private static readonly TimeSpan RefDocCacheTtl = TimeSpan.FromMinutes(30);
    private const long RefDocMaxBytes = 10L * 1024 * 1024;   // 10 MB por PDF
    private const int RefDocMaxCount = 5;                    // tope defensivo por turno


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
        sb.AppendLine("Al inicio de tu respuesta incluye UNA de estas etiquetas según el contexto:");
        sb.AppendLine("[INTENT:cobros]      → gestión de cobro/pago activa (cliente aún no ha pagado ni cerrado)");
        sb.AppendLine("[INTENT:reclamos]    → el cliente tiene un reclamo de seguro");
        sb.AppendLine("[INTENT:renovaciones]→ el cliente quiere renovar su póliza");
        sb.AppendLine("[INTENT:humano]      → el cliente pide hablar con una persona");
        sb.AppendLine("[INTENT:cierre]      → USA ESTA ETIQUETA cuando la conversación llega a su FIN NATURAL:");
        sb.AppendLine("  • El cliente confirmó que va a pagar (con monto y fecha) y se despidió");
        sb.AppendLine("  • El cliente confirmó que ya pagó o que enviará el comprobante");
        sb.AppendLine("  • La solicitud del cliente fue atendida y no hay nada más que gestionar");
        sb.AppendLine("  • El cliente indicó explícitamente que ya no necesita nada más y se despidió");
        sb.AppendLine("IMPORTANTE: Cuando uses [INTENT:cierre], colócalo al inicio de tu respuesta como las demás etiquetas.");
        sb.AppendLine();

        // Inyectar fecha/hora actual en zona horaria de Panamá para que el agente
        // pueda resolver correctamente expresiones relativas del cliente
        // ("mañana", "pasado mañana", "el próximo lunes", etc.)
        var panamaZone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "SA Pacific Standard Time" : "America/Panama");
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, panamaZone);

        var culture = new System.Globalization.CultureInfo("es-PA");
        sb.AppendLine("## Contexto temporal");
        sb.AppendLine($"- Fecha y hora actual (Panamá): {now.ToString("dddd d 'de' MMMM 'de' yyyy, HH:mm", culture)}");
        sb.AppendLine($"- Mañana: {now.AddDays(1).ToString("dddd d/MM/yyyy", culture)}");
        sb.AppendLine($"- Pasado mañana: {now.AddDays(2).ToString("dddd d/MM/yyyy", culture)}");
        sb.AppendLine($"- Próximo lunes: {NextWeekday(now, DayOfWeek.Monday):dd/MM/yyyy}");
        sb.AppendLine($"- Próximo viernes: {NextWeekday(now, DayOfWeek.Friday):dd/MM/yyyy}");
        sb.AppendLine("Cuando el cliente indique una fecha relativa (mañana, pasado mañana, el lunes,");
        sb.AppendLine("la próxima semana, a fin de mes, etc.), calcula la fecha exacta y confírmala");
        sb.AppendLine("al cliente en formato legible (ej: lunes 7 de abril). Nunca confirmes");
        sb.AppendLine("un compromiso usando solo la expresión relativa del cliente.");
        sb.AppendLine();

        // Inyectar horario de atención configurado en el maestro de campaña
        if (req.AttentionDays?.Count > 0 && req.AttentionStartTime is not null && req.AttentionEndTime is not null)
        {
            var dayNames = new[] { "domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado" };
            var days = req.AttentionDays.OrderBy(d => d).Select(d => dayNames[d % 7]);
            var daysStr = string.Join(", ", days);
            var nextAttention = NextAttentionDay(now, req.AttentionDays);

            sb.AppendLine("## Horario de atención de asesores");
            sb.AppendLine($"- Días de atención: {daysStr}");
            sb.AppendLine($"- Horario: {req.AttentionStartTime} – {req.AttentionEndTime} (hora de Panamá)");

            // Evaluar si actualmente está en horario de atención
            var currentDay = (int)now.DayOfWeek;
            var currentTime = now.ToString("HH:mm");
            var inAttentionDay  = req.AttentionDays.Contains(currentDay);
            var inAttentionHour = string.Compare(currentTime, req.AttentionStartTime) >= 0
                               && string.Compare(currentTime, req.AttentionEndTime) < 0;

            if (inAttentionDay && inAttentionHour)
            {
                sb.AppendLine("- Estado actual: DENTRO de horario de atención.");
                sb.AppendLine("Si el cliente pide hablar con un asesor, puedes indicarle que un asesor lo atenderá en breve.");
            }
            else
            {
                sb.AppendLine("- Estado actual: FUERA de horario de atención.");
                sb.AppendLine($"- Próxima atención disponible: {nextAttention.ToString("dddd d 'de' MMMM", culture)}, a partir de las {req.AttentionStartTime}.");
                sb.AppendLine("Si el cliente pide hablar con un asesor, indícale que actualmente estamos fuera de horario");
                sb.AppendLine($"y que un asesor lo contactará el {nextAttention.ToString("dddd d 'de' MMMM", culture)} a partir de las {req.AttentionStartTime}.");
            }
            sb.AppendLine();
        }

        if (req.ClientContext?.Count > 0)
        {
            sb.AppendLine("## Contexto del cliente");
            foreach (var (k, v) in req.ClientContext)
                sb.AppendLine($"- {k}: {v}");
        }

        // ── Action Trigger Protocol Fase 4 — resultado de acción previa ────
        // Si en el turno anterior se ejecutó una acción y devolvió datos útiles
        // (link de pago generado, datos del cliente consultados, etc.), el
        // handler los pasa aquí para que el agente los incluya en su respuesta.
        // El handler ya valida freshness, así que si llega, se inyecta sin más.
        if (req.LastActionResult is { DataForAgent: { Length: > 0 } data } last)
        {
            sb.AppendLine();
            sb.AppendLine("## RESULTADO DE ACCIÓN EJECUTADA");
            sb.AppendLine($"{last.Slug}: {data}");
            sb.AppendLine("Usa esta información en tu respuesta al cliente si es relevante. No repitas todo el bloque, solo el dato útil.");
        }

        // ── Action Trigger Protocol — bloque ACCIONES DISPONIBLES ──────────
        // Lo preconstruye IActionPromptBuilder en la capa de aplicación y
        // lo pasa vía AgentRunRequest.ActionsBlock. En Fase 0 siempre viene
        // vacío (NoOp), por lo que el prompt queda idéntico al histórico.
        if (!string.IsNullOrEmpty(req.ActionsBlock))
        {
            sb.AppendLine();
            sb.AppendLine(req.ActionsBlock);
        }

        return sb.ToString();
    }

    /// <summary>Calcula la próxima fecha en que habrá atención (puede ser hoy si aún no ha pasado el horario).</summary>
    private static DateTime NextAttentionDay(DateTime from, List<int> attentionDays)
    {
        for (var i = 0; i <= 7; i++)
        {
            var candidate = from.AddDays(i).Date;
            if (attentionDays.Contains((int)candidate.DayOfWeek))
                return candidate;
        }
        return from.Date;
    }

    /// <summary>Calcula la fecha del próximo día de la semana indicado (nunca el día actual).</summary>
    private static DateTime NextWeekday(DateTime from, DayOfWeek target)
    {
        var daysUntil = ((int)target - (int)from.DayOfWeek + 7) % 7;
        if (daysUntil == 0) daysUntil = 7; // si hoy es ese día, ir a la próxima semana
        return from.AddDays(daysUntil).Date;
    }

    private async Task<List<Anthropic.SDK.Messaging.Message>> BuildMessagesAsync(
        AgentRunRequest req, CancellationToken ct)
    {
        var messages = new List<Anthropic.SDK.Messaging.Message>();

        // ── PDFs de referencia del maestro de campaña ─────────────────────
        // Se inyectan como un "turno fijo" al inicio: user con los PDFs +
        // assistant que reconoce haberlos leído. Así el agente los tiene en
        // contexto durante toda la conversación y Claude puede reusar el prefijo
        // en cache entre turnos de la misma campaña.
        if (req.ReferenceDocuments is { Count: > 0 })
        {
            Console.WriteLine($"[RefDocs] Intentando cargar {req.ReferenceDocuments.Count} documento(s) de referencia");
            var refContent = new List<ContentBase>();
            var loadedNames = new List<string>();

            var docsToLoad = req.ReferenceDocuments.Take(RefDocMaxCount);
            foreach (var refDoc in docsToLoad)
            {
                var bytes = await GetReferenceDocumentBytesAsync(refDoc.BlobUrl, ct);
                if (bytes is null || bytes.Length == 0) continue;
                if (bytes.Length > RefDocMaxBytes)
                {
                    Console.WriteLine($"[RefDocs] Omitido {refDoc.FileName} ({bytes.Length / 1024}KB > límite)");
                    continue;
                }

                refContent.Add(new DocumentContent
                {
                    Source = new DocumentSource
                    {
                        Type      = SourceType.base64,
                        MediaType = "application/pdf",
                        Data      = Convert.ToBase64String(bytes)
                    }
                });
                loadedNames.Add(refDoc.FileName);
            }

            if (refContent.Count > 0)
            {
                var namesList = string.Join(", ", loadedNames);
                refContent.Add(new TextContent
                {
                    Text =
                        $"A continuación se adjuntan {loadedNames.Count} documento(s) PDF como material de referencia oficial del maestro de campaña ({namesList}). " +
                        "INSTRUCCIONES SOBRE ESTOS DOCUMENTOS:\n" +
                        "1. Cuando el cliente haga cualquier pregunta cuya respuesta esté dentro de estos documentos, RESPÓNDELA usando la información del documento, aunque parezca no relacionada con la campaña. Ese es el propósito de cargarlos.\n" +
                        "2. Cita datos concretos del documento (nombres, fechas, cifras, definiciones) con naturalidad, sin decir 'según el documento' ni leer literalmente.\n" +
                        "3. Si la pregunta no tiene respuesta en los documentos ni en el contexto de la campaña, dilo con honestidad y reconduce al tema principal."
                });

                messages.Add(new Anthropic.SDK.Messaging.Message
                {
                    Role = RoleType.User,
                    Content = refContent
                });
                messages.Add(new Anthropic.SDK.Messaging.Message
                {
                    Role = RoleType.Assistant,
                    Content = [new TextContent { Text = "Entendido. He revisado los documentos de referencia y los tendré en cuenta para responder al cliente." }]
                });

                Console.WriteLine($"[RefDocs] Inyectados {loadedNames.Count} PDFs al contexto: {namesList}");
            }
        }

        // Historial extendido a 20 mensajes para mantener contexto en conversaciones largas
        messages.AddRange(req.RecentHistory
            .TakeLast(20)
            .Select(m => new Anthropic.SDK.Messaging.Message
            {
                Role    = m.IsFromAgent ? RoleType.Assistant : RoleType.User,
                Content = [new TextContent { Text = StripMediaTag(m.Content) }]
            }));

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

    /// <summary>
    /// Descarga (con cache de 30 min) los bytes de un PDF de referencia desde Azure Blob.
    /// Devuelve null si la descarga falla — el flujo sigue sin inyectar ese documento.
    /// </summary>
    private async Task<byte[]?> GetReferenceDocumentBytesAsync(string blobUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(blobUrl)) return null;
        var key = $"refdoc:{blobUrl}";

        if (cache.TryGetValue<byte[]>(key, out var cached) && cached is not null)
            return cached;

        try
        {
            // Encode espacios y otros caracteres no-URL-safe en el path antes de descargar.
            // Azure a veces devuelve la URL con espacios literales y HttpClient
            // no los codifica automáticamente.
            var safeUrl = EncodeBlobUrl(blobUrl);
            var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            var bytes = await http.GetByteArrayAsync(safeUrl, ct);

            // Verificar magic bytes de PDF (%PDF-)
            if (bytes.Length < 4 ||
                bytes[0] != 0x25 || bytes[1] != 0x50 ||
                bytes[2] != 0x44 || bytes[3] != 0x46)
            {
                Console.WriteLine($"[RefDocs] {blobUrl} no es un PDF válido, omitiendo");
                return null;
            }

            cache.Set(key, bytes, RefDocCacheTtl);
            return bytes;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RefDocs] Error descargando {blobUrl}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Codifica un blob URL preservando scheme/host y encodeando solo los segmentos del path.
    /// Necesario porque Azure puede devolver URLs con espacios literales que HttpClient
    /// no codifica automáticamente al hacer GetByteArrayAsync.
    /// </summary>
    private static string EncodeBlobUrl(string rawUrl)
    {
        try
        {
            var u = new Uri(rawUrl);
            var encodedPath = string.Join('/', u.AbsolutePath.Split('/')
                .Select(seg => string.IsNullOrEmpty(seg) ? seg : Uri.EscapeDataString(Uri.UnescapeDataString(seg))));
            return $"{u.Scheme}://{u.Host}{(u.IsDefaultPort ? "" : ":" + u.Port)}{encodedPath}{u.Query}";
        }
        catch
        {
            return rawUrl;
        }
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
