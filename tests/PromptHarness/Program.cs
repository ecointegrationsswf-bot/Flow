using System.Text;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;

namespace PromptHarness;

/// <summary>
/// Harness aislado que reproduce EXACTAMENTE el flujo de AnthropicAgentRunner
/// (BuildSystemPrompt + BuildMessagesAsync) pero invocando a Claude desde
/// fuera del pipeline ASP.NET. Permite iterar sobre el prompt sin necesidad
/// de redeploy y validar que las respuestas reflejen el contenido del PDF.
///
/// Modos:
///   --no-pdf          → simula el caso del template "Copia de Cobros Pasesa" (sin docs)
///   --with-pdf        → simula el caso del template "Reclamos" (con Beneficios_ASSA.pdf)
///   (sin flag)        → corre ambos modos para comparar
/// </summary>
public class Program
{
    private const string PdfPath = @"C:\Users\vanes\Downloads\Beneficios_ASSA.pdf";
    private const string LogDir  = @"C:\TalkIA\tests\PromptHarness\logs";

    // El system prompt del agente "Sofia/Mila" (cobros) — simulado.
    private const string AgentSystemPrompt = """
Eres Sofia, asesora virtual de Somos Seguros. Atiendes clientes vía WhatsApp con
tono cordial, profesional y empático. Tu objetivo principal es gestionar consultas
sobre pólizas de seguros, hospitales, beneficios y servicios disponibles para los
clientes del corredor.

- Saluda usando el nombre del cliente cuando lo conozcas.
- Usa emojis con moderación (😊 🏥 📞 📍).
- Responde SIEMPRE en español, en mensajes cortos para WhatsApp.
""";

    // Reglas de uso de documentos (idénticas al DocumentReferencePromptBuilder).
    private const string UsageRules = """
### REGLAS DE USO — obligatorias y prioritarias:

0. PROTOCOLO DE BÚSQUEDA EXHAUSTIVA (ANTES DE RESPONDER):
   Cada vez que el cliente haga una pregunta cuyo tema pueda estar
   cubierto por un documento adjunto, DEBES — antes de responder —
   ejecutar mentalmente este protocolo:

   PASO A · Identifica el TEMA de la pregunta del cliente, incluyendo
   los temas implícitos. Una sola palabra del cliente puede activar
   varios temas. Ejemplos:
   - "qué hay cerca en Veraguas" → tema implícito: red de hospitales /
     centros médicos / cobertura geográfica.
   - "se me dañó el carro" → asistencia vehicular / grúa / cobertura.
   - "me pidieron un electrocardiograma" → servicios médicos a
     domicilio / atenciones cubiertas.

   PASO B · Recorre TODA la lista de documentos disponibles (nombre +
   descripción) y todas las secciones internas relevantes (provincias,
   regiones, productos, beneficios, exclusiones, tablas, anexos). NO
   te detengas en la primera sección que parezca no contener la
   respuesta — los PDFs incluyen tablas y secciones diferenciadas; la
   información puede estar en una página posterior.

   PASO C · Solo después de haber recorrido el documento completo
   puedes concluir que la información NO está. Si encontraste la
   respuesta — aunque sea parcial — úsala. Una respuesta parcial pero
   precisa siempre es mejor que declinar.

1. JERARQUÍA DE FUENTES PARA RESPONDER:
   a) Primero las instrucciones del system prompt del agente.
   b) Luego los documentos de referencia adjuntos (búsqueda exhaustiva
      según protocolo del punto 0).
   c) Solo si tras el protocolo no hay nada útil, responde con
      transparencia y ofrece escalar a un ejecutivo humano.

2. PROHIBIDO DECLINAR PREMATURAMENTE.
   NO está permitido responder con frases como "no tengo información
   confiable", "no cuento con esos datos" o "te recomiendo verificar
   directamente" SIN haber recorrido los documentos según el protocolo
   del punto 0. Declinar cuando la respuesta SÍ está en un documento
   adjunto es una falla grave.

3. NUNCA INVENTES DATOS NO PRESENTES EN EL DOCUMENTO.

4. CITA CON PRECISIÓN. Reproduce teléfonos, direcciones, montos y
   plazos textualmente — sin redondear ni parafrasear.

5. NO EXPONGAS EL DOCUMENTO COMO FUENTE AL CLIENTE.

6. NO ENVÍES EL PDF AL CLIENTE.

7. SELECCIÓN INTELIGENTE CUANDO HAY VARIOS DOCUMENTOS.

8. LÍMITES DE LOS DOCUMENTOS DE REFERENCIA.

9. IDIOMA Y TONO.
""";

    // Turnos de la conversación real reportada.
    private static readonly (string Question, string[] MustContain, string[] MustNotContain)[] Scenarios = new[]
    {
        // Test 1 — Veraguas: debe listar los 3 hospitales
        (
            "Estoy en Veraguas y necesito lista de hospitales assa",
            new[] { "Jesús Nazareno", "998-1581", "Médica Norte", "950-0047", "San Juan de Dios", "998-5583" },
            new[] { "no tengo información", "no cuento", "te recomiendo verificar directamente" }
        ),
        // Test 2 — Minimed: dirección EXACTA del PDF
        (
            "Puedes darme la dirección de Minimed",
            new[] { "Ricardo J. Alfaro", "Tumba Muerto", "Golden Point", "263-6464" },
            new[] { "Vía España", "Calle 78", "La Alameda" } // direcciones inventadas que vimos
        ),
        // Test 3 — beneficios ASSA Medic Móvil
        (
            "Qué beneficios me da assa medic",
            new[] { "Paramédico Motorizado", "Ambulancia", "Orientación Médica", "Visita Médica" },
            new[] { "no tengo detalle", "no cuento", "te recomiendo contactar" }
        ),
        // Test 4 — el cliente cuestiona; el agente NO debe retractar un dato real del PDF
        (
            "Seguro es esa la dirección de Minimed?",
            new[] { "Ricardo J. Alfaro", "Tumba Muerto", "Golden Point" }, // debe REPETIRLA, no retractarse
            new[] { "no estoy 100% seguro", "no estoy seguro", "no quiero darte una dirección incorrecta" }
        ),
    };

    public static async Task<int> Main(string[] args)
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            Console.Error.WriteLine("ERROR: ANTHROPIC_API_KEY no está definida.");
            return 1;
        }

        Directory.CreateDirectory(LogDir);
        var runStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        var modes = args.Contains("--no-pdf") ? new[] { false }
                   : args.Contains("--with-pdf") ? new[] { true }
                   : new[] { false, true };

        byte[]? pdfBytes = null;
        if (modes.Contains(true))
        {
            if (!File.Exists(PdfPath))
            {
                Console.Error.WriteLine($"ERROR: PDF no encontrado en {PdfPath}");
                return 1;
            }
            pdfBytes = await File.ReadAllBytesAsync(PdfPath);
            Console.WriteLine($"PDF cargado: {pdfBytes.Length / 1024} KB");
        }

        var summary = new StringBuilder();
        summary.AppendLine($"# Run {runStamp}");
        summary.AppendLine();

        var totalPass = 0;
        var totalFail = 0;

        foreach (var withPdf in modes)
        {
            var modeLabel = withPdf ? "WITH-PDF" : "NO-PDF";
            Console.WriteLine();
            Console.WriteLine(new string('=', 70));
            Console.WriteLine($" MODO: {modeLabel}");
            Console.WriteLine(new string('=', 70));
            summary.AppendLine($"## {modeLabel}");
            summary.AppendLine();

            var systemPrompt = BuildSystemPrompt(withPdf);

            // Historial conversacional: arranca con un saludo (como pasó en producción)
            var history = new List<Anthropic.SDK.Messaging.Message>();

            // PDFs adjuntos como turno fijo al inicio (réplica fiel del AgentRunner)
            if (withPdf && pdfBytes is not null)
            {
                history.Add(new Anthropic.SDK.Messaging.Message
                {
                    Role = RoleType.User,
                    Content = new List<ContentBase>
                    {
                        new DocumentContent
                        {
                            Source = new DocumentSource
                            {
                                Type = SourceType.base64,
                                MediaType = "application/pdf",
                                Data = Convert.ToBase64String(pdfBytes)
                            }
                        },
                        new TextContent { Text =
                            "Adjunto 1 documento(s) PDF de referencia oficial de la campaña. " +
                            "Estos documentos son tu fuente autorizada para responder preguntas del cliente sobre productos, " +
                            "coberturas, beneficios, redes, ubicaciones, teléfonos, direcciones, plazos y procedimientos.\n\n" +
                            "Antes de responder cualquier pregunta cuyo tema pueda estar cubierto por un documento, " +
                            "DEBES recorrer el documento completo (todas sus secciones, tablas y anexos) según el protocolo " +
                            "de búsqueda exhaustiva definido en el system prompt. Está PROHIBIDO declinar diciendo 'no tengo " +
                            "información confiable' sin haber buscado primero — declinar cuando la respuesta sí está en el " +
                            "documento es una falla grave.\n\n" +
                            "Listado de documentos disponibles:\n" +
                            "1. Beneficios_ASSA.pdf — Red de hospitales y beneficios de ASSA Compañía de Seguros." }
                    }
                });
                history.Add(new Anthropic.SDK.Messaging.Message
                {
                    Role = RoleType.Assistant,
                    Content = new List<ContentBase> { new TextContent { Text =
                        "Confirmado. Revisé los documentos adjuntos y los tengo disponibles como fuente autorizada. " +
                        "Antes de responder cualquier pregunta del cliente recorreré exhaustivamente las secciones " +
                        "relevantes (provincias, productos, beneficios, tablas, etc.) y solo declinaré si tras la " +
                        "búsqueda completa la información no está. Citaré nombres, teléfonos y direcciones tal cual " +
                        "aparecen en el documento, sin exponerlo como fuente al cliente." } }
                });
            }

            var client = new AnthropicClient(apiKey);
            var logFile = Path.Combine(LogDir, $"run-{runStamp}-{modeLabel}.log");
            await using var logWriter = new StreamWriter(logFile, append: false, Encoding.UTF8);

            await logWriter.WriteLineAsync($"=== {modeLabel} === {DateTime.Now:O}");
            await logWriter.WriteLineAsync();
            await logWriter.WriteLineAsync("--- SYSTEM PROMPT ---");
            await logWriter.WriteLineAsync(systemPrompt);
            await logWriter.WriteLineAsync("--- /SYSTEM PROMPT ---");
            await logWriter.WriteLineAsync();

            for (int i = 0; i < Scenarios.Length; i++)
            {
                var (question, mustContain, mustNotContain) = Scenarios[i];
                Console.WriteLine();
                Console.WriteLine($"[{modeLabel}] TEST {i + 1}: {question}");

                history.Add(new Anthropic.SDK.Messaging.Message
                {
                    Role = RoleType.User,
                    Content = new List<ContentBase> { new TextContent { Text = question } }
                });

                MessageResponse resp;
                try
                {
                    resp = await client.Messages.GetClaudeMessageAsync(new MessageParameters
                    {
                        Model       = "claude-sonnet-4-6",
                        MaxTokens   = 1024,
                        System      = new List<SystemMessage> { new SystemMessage(systemPrompt) },
                        Messages    = history,
                        Stream      = false,
                        Temperature = 0.3m
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ Error API: {ex.Message}");
                    await logWriter.WriteLineAsync($"TEST {i + 1} ERROR: {ex.Message}");
                    totalFail++;
                    continue;
                }

                var reply = resp.Content.OfType<TextContent>().FirstOrDefault()?.Text ?? "";

                history.Add(new Anthropic.SDK.Messaging.Message
                {
                    Role = RoleType.Assistant,
                    Content = new List<ContentBase> { new TextContent { Text = reply } }
                });

                await logWriter.WriteLineAsync($"=== TEST {i + 1} ===");
                await logWriter.WriteLineAsync($"USER: {question}");
                await logWriter.WriteLineAsync($"ASSISTANT: {reply}");
                await logWriter.WriteLineAsync();

                var hits   = mustContain.Where(s => reply.Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();
                var misses = mustContain.Except(hits).ToList();
                var bads   = mustNotContain.Where(s => reply.Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();

                var pass = misses.Count == 0 && bads.Count == 0;
                var icon = pass ? "✅" : "❌";
                Console.WriteLine($"  {icon} hits=[{string.Join(", ", hits)}]");
                if (misses.Count > 0) Console.WriteLine($"     MISSING: [{string.Join(", ", misses)}]");
                if (bads.Count > 0)   Console.WriteLine($"     FORBIDDEN PRESENT: [{string.Join(", ", bads)}]");
                Console.WriteLine($"  ↳ reply: {Truncate(reply, 200)}");

                await logWriter.WriteLineAsync($"VERDICT: {(pass ? "PASS" : "FAIL")}");
                await logWriter.WriteLineAsync($"  hits     = [{string.Join(", ", hits)}]");
                await logWriter.WriteLineAsync($"  missing  = [{string.Join(", ", misses)}]");
                await logWriter.WriteLineAsync($"  bad-hits = [{string.Join(", ", bads)}]");
                await logWriter.WriteLineAsync();

                summary.AppendLine($"- TEST {i + 1} ({modeLabel}): {(pass ? "✅ PASS" : "❌ FAIL")} — `{question}`");
                if (!pass)
                {
                    if (misses.Count > 0) summary.AppendLine($"  - missing: {string.Join(", ", misses)}");
                    if (bads.Count > 0)   summary.AppendLine($"  - forbidden present: {string.Join(", ", bads)}");
                }

                if (pass) totalPass++; else totalFail++;
            }

            Console.WriteLine();
            Console.WriteLine($"[{modeLabel}] log: {logFile}");
        }

        var summaryFile = Path.Combine(LogDir, $"summary-{runStamp}.md");
        summary.AppendLine();
        summary.AppendLine($"**Total**: {totalPass} pass / {totalFail} fail");
        await File.WriteAllTextAsync(summaryFile, summary.ToString());

        Console.WriteLine();
        Console.WriteLine(new string('=', 70));
        Console.WriteLine($" RESUMEN: {totalPass} pass / {totalFail} fail");
        Console.WriteLine($" {summaryFile}");
        Console.WriteLine(new string('=', 70));

        return totalFail == 0 ? 0 : 2;
    }

    private static string BuildSystemPrompt(bool withPdf)
    {
        var sb = new StringBuilder();
        sb.AppendLine(AgentSystemPrompt);
        sb.AppendLine();
        sb.AppendLine("## Instrucciones de clasificación");
        sb.AppendLine("Al inicio de tu respuesta incluye UNA de estas etiquetas según el contexto:");
        sb.AppendLine("[INTENT:cobros] | [INTENT:reclamos] | [INTENT:renovaciones] | [INTENT:humano] | [INTENT:cierre]");
        sb.AppendLine();
        sb.AppendLine("## Contexto temporal");
        sb.AppendLine($"- Fecha y hora actual (Panamá): {DateTime.Now:dddd dd 'de' MMMM 'de' yyyy, HH:mm}");
        sb.AppendLine();
        sb.AppendLine("## Contexto del cliente");
        sb.AppendLine("- nombre: Jeferson");
        sb.AppendLine("- telefono: +50768777386");
        sb.AppendLine("- aseguradora: ASSA");
        sb.AppendLine("- producto: Auto");
        sb.AppendLine("- poliza: BB-01922-01");

        if (withPdf)
        {
            sb.AppendLine();
            sb.AppendLine("## DOCUMENTOS DE REFERENCIA DEL TENANT");
            sb.AppendLine();
            sb.AppendLine("Tienes acceso a 1 documento(s) PDF de referencia oficial del");
            sb.AppendLine("corredor. Son tu fuente autorizada para responder consultas del cliente");
            sb.AppendLine("sobre productos, coberturas, redes, hospitales, beneficios, ubicaciones,");
            sb.AppendLine("teléfonos, direcciones, plazos y procedimientos. Aplica a TODOS los temas");
            sb.AppendLine("aunque la conversación se haya iniciado en un flujo distinto (cobros,");
            sb.AppendLine("renovaciones, etc.) — los documentos cubren el portafolio completo del");
            sb.AppendLine("corredor, no un único agente.");
            sb.AppendLine();
            sb.AppendLine("### Documentos disponibles:");
            sb.AppendLine("1. Beneficios_ASSA.pdf — Red de hospitales y beneficios de ASSA Compañía de Seguros.");
            sb.AppendLine();
            sb.Append(UsageRules);
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("### CHECKLIST OBLIGATORIO ANTES DE TU PRÓXIMA RESPUESTA");
            sb.AppendLine("Antes de redactar tu respuesta al último mensaje del cliente, ejecuta");
            sb.AppendLine("internamente este chequeo:");
            sb.AppendLine("[ ] ¿La pregunta del cliente toca algún tema cubierto por los documentos");
            sb.AppendLine("    adjuntos (productos, redes, hospitales, beneficios, ubicaciones,");
            sb.AppendLine("    teléfonos, direcciones, coberturas, exclusiones, plazos, etc.)?");
            sb.AppendLine("[ ] Si SÍ → recorre TODAS las secciones del documento (no solo la primera");
            sb.AppendLine("    que parezca relevante) antes de decidir si la información existe.");
            sb.AppendLine("[ ] Si encontraste el dato → respóndelo con precisión literal (nombres,");
            sb.AppendLine("    teléfonos y direcciones tal cual aparecen).");
            sb.AppendLine("[ ] Si NO encontraste el dato tras búsqueda exhaustiva → solo entonces");
            sb.AppendLine("    puedes declinar y ofrecer escalar.");
            sb.AppendLine();
            sb.AppendLine("REGLA DURA: Está PROHIBIDO emitir frases como \"no tengo información");
            sb.AppendLine("confiable\", \"no cuento con esos datos\" o \"te recomiendo verificar");
            sb.AppendLine("directamente\" cuando la respuesta efectivamente está en un documento");
            sb.AppendLine("adjunto.");
        }

        return sb.ToString();
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s.Replace("\n", " ") : s.Substring(0, max).Replace("\n", " ") + "...";
}
