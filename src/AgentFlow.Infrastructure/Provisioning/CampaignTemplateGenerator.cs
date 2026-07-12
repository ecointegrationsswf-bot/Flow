using System.Text;
using System.Text.Json;
using AgentFlow.Domain.Provisioning;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Provisioning;

/// <summary>
/// Integración Ludo CRM — Fase 3. Genera el maestro de campaña (system prompt + acciones
/// sugeridas + criterios de etapa) asistido por LLM. Reusa el mismo patrón de llamada que
/// <c>InitialMessageGenerator</c> (Anthropic.SDK con la key del tenant o la global).
///
/// <para><b>Vertical "seguro": plantilla vetada.</b> El system prompt SIEMPRE arranca con el
/// gate de autenticación transversal (datos confidenciales, validación de identidad, no revelar
/// pólizas sin validar). El LLM solo ESPECIALIZA el cuerpo (tono, objetivo, criterios) — no puede
/// quitar el gate porque se antepone de forma determinista. Es la lección de PASESA: para seguros
/// nunca dejamos un prompt 100% autogenerado sin la barrera de auth.</para>
///
/// <para>NUNCA lanza: ante cualquier fallo (LLM caído, JSON inválido, sin key) devuelve un
/// fallback determinista para no romper el provisioning transaccional.</para>
/// </summary>
public sealed class CampaignTemplateGenerator(
    IConfiguration config,
    ILogger<CampaignTemplateGenerator> log) : ICampaignTemplateGenerator
{
    /// <summary>Gate de autenticación vetado para la vertical seguro (se antepone siempre).</summary>
    public const string VettedSeguroAuthGate =
        """
        ## GATE DE AUTENTICACIÓN (OBLIGATORIO — NO OMITIR)
        Eres un asistente de un corredor de seguros. Manejas datos CONFIDENCIALES (pólizas,
        coberturas, montos, datos personales del asegurado).

        Reglas inquebrantables:
        1. ANTES de revelar o procesar CUALQUIER dato confidencial (pólizas, saldos, documentos,
           datos del asegurado), verifica que el cliente esté AUTENTICADO en esta conversación.
        2. Si NO está autenticado, pide primero que valide su identidad (cédula) y completa el
           flujo de validación. No entregues información hasta que la validación sea exitosa.
        3. Nunca entregues datos de una identidad distinta a la validada en esta conversación.
        4. Tras validar, RETOMA la solicitud original del cliente sin pedírsela de nuevo.
        5. La validación aplica a TODA acción confidencial, no solo a la primera (es transversal).
        """;

    public async Task<GeneratedTemplate> GenerateAsync(
        GenerateTemplateRequest req, string? tenantApiKey, CancellationToken ct)
    {
        var apiKey = !string.IsNullOrWhiteSpace(tenantApiKey)
            ? tenantApiKey
            : config["Anthropic:ApiKey"];

        var isSeguro = string.Equals(req.TipoNegocio, "seguro", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            log.LogWarning("CampaignTemplateGenerator: sin API key (ni tenant ni global). Usando fallback.");
            return Fallback(req, isSeguro);
        }

        try
        {
            var meta = BuildMetaPrompt(req);
            var client = new AnthropicClient(apiKey);
            var resp = await client.Messages.GetClaudeMessageAsync(new MessageParameters
            {
                Model = "claude-sonnet-4-6",
                MaxTokens = 1500,
                System = [new SystemMessage(meta.System)],
                Messages = [new() { Role = RoleType.User, Content = [new TextContent { Text = meta.User }] }],
                Stream = false,
                Temperature = new decimal(0.3),
            }, ct);

            var text = resp.Content.OfType<TextContent>().FirstOrDefault()?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                log.LogWarning("CampaignTemplateGenerator: respuesta LLM vacía. Fallback.");
                return Fallback(req, isSeguro);
            }

            var parsed = ParseJson(text);
            if (parsed is null)
            {
                log.LogWarning("CampaignTemplateGenerator: JSON no parseable. Fallback.");
                return Fallback(req, isSeguro);
            }

            var body = string.IsNullOrWhiteSpace(parsed.SystemPrompt)
                ? DefaultBody(req)
                : parsed.SystemPrompt!;

            var systemPrompt = isSeguro ? $"{VettedSeguroAuthGate}\n\n{body}" : body;

            return new GeneratedTemplate(
                SystemPrompt: systemPrompt,
                SuggestedActionSlugs: parsed.SuggestedActionSlugs ?? [],
                StageCriteria: parsed.StageCriteria?.Select(c => new StageCriterion(c.Etapa ?? "", c.Criterio ?? "")).ToList() ?? [],
                UsedLlm: true);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "CampaignTemplateGenerator: error llamando al LLM. Fallback determinista.");
            return Fallback(req, isSeguro);
        }
    }

    // ── Meta-prompt ─────────────────────────────────────────────────────────────────────
    private static (string System, string User) BuildMetaPrompt(GenerateTemplateRequest req)
    {
        var system =
            "Eres un diseñador experto de agentes conversacionales para WhatsApp. Generas el " +
            "maestro (system prompt) de un agente de negocio. Respondes ÚNICAMENTE con un objeto " +
            "JSON válido, sin texto adicional ni markdown, con esta forma exacta:\n" +
            "{\"systemPrompt\": string, \"suggestedActionSlugs\": string[], " +
            "\"stageCriteria\": [{\"etapa\": string, \"criterio\": string}]}";

        var etapas = req.Etapas is { Count: > 0 }
            ? string.Join("\n", req.Etapas.OrderBy(e => e.Orden).Select(e => $"  {e.Orden}. {e.Nombre}"))
            : "  (sin etapas definidas)";

        var user =
            $"Vertical de negocio: {req.TipoNegocio}\n" +
            $"Agente: {req.AgentSlug}\n" +
            $"Objetivo del agente (en lenguaje natural): {req.Objetivo}\n" +
            $"Pipeline de etapas (en orden):\n{etapas}\n\n" +
            "Genera:\n" +
            "- systemPrompt: instrucciones claras en español para el agente, orientadas al objetivo. " +
            "NO incluyas reglas de autenticación (se agregan aparte si aplica).\n" +
            "- suggestedActionSlugs: nombres de acciones sugeridas (ej: registrar_oportunidad, mover_fase).\n" +
            "- stageCriteria: para cada etapa, el criterio conversacional que indica que se debe avanzar a ella.";

        return (system, user);
    }

    private sealed class LlmJson
    {
        public string? SystemPrompt { get; set; }
        public List<string>? SuggestedActionSlugs { get; set; }
        public List<LlmCriterion>? StageCriteria { get; set; }
    }
    private sealed class LlmCriterion
    {
        public string? Etapa { get; set; }
        public string? Criterio { get; set; }
    }

    private static LlmJson? ParseJson(string text)
    {
        // Tolerar que el modelo envuelva en ```json ... ``` o agregue texto alrededor.
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        var json = text.Substring(start, end - start + 1);
        try
        {
            return JsonSerializer.Deserialize<LlmJson>(json,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch { return null; }
    }

    // ── Fallback determinista (nunca lanza) ──────────────────────────────────────────────
    private static GeneratedTemplate Fallback(GenerateTemplateRequest req, bool isSeguro)
    {
        var body = DefaultBody(req);
        var systemPrompt = isSeguro ? $"{VettedSeguroAuthGate}\n\n{body}" : body;
        var criteria = (req.Etapas ?? [])
            .OrderBy(e => e.Orden)
            .Select(e => new StageCriterion(e.Nombre, $"Avanzar a '{e.Nombre}' cuando la conversación lo justifique."))
            .ToList();
        return new GeneratedTemplate(systemPrompt, ["registrar_oportunidad", "mover_fase"], criteria, UsedLlm: false);
    }

    private static string DefaultBody(GenerateTemplateRequest req)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Eres el agente '{req.AgentSlug}' de un negocio de tipo '{req.TipoNegocio}'.");
        sb.AppendLine($"Objetivo: {req.Objetivo}");
        if (req.Etapas is { Count: > 0 })
        {
            sb.AppendLine("Guía la conversación por estas etapas:");
            foreach (var e in req.Etapas.OrderBy(e => e.Orden))
                sb.AppendLine($"  {e.Orden}. {e.Nombre}");
        }
        sb.AppendLine("Cuando detectes intención calificada, registra la oportunidad y avanza la fase correspondiente.");
        return sb.ToString().TrimEnd();
    }
}
