using System.Text;
using System.Text.Json.Nodes;

namespace AgentFlow.Domain.Provisioning;

/// <summary>
/// Integración Ludo CRM — Fase 4. Construye el <b>lienzo</b> (TenantFlow.FlowJson, formato reactflow)
/// del pipeline de un tenant a partir de sus etapas (homologadas en StageLabelMap) y los criterios de
/// avance generados por el LLM. El Motor de Flujos lo interpreta: <c>FlowSession.BuildBlock()</c> lo
/// compila en el bloque "## FLUJO ACTIVO" que guía al agente a emitir <c>[ACTION:registrar_oportunidad]</c>
/// y <c>[ACTION:mover_fase]</c>. Es PURO (sin dependencias) y tolerante (si no hay etapas, devuelve un
/// lienzo vacío y el motor no hace nada).
///
/// <para>Grafo: start → (LLM calificar) → (acción registrar_oportunidad) → (LLM pipeline) →
/// (acción mover_fase) → end. Las acciones se referencian por slug; el contrato webhook que apunta a
/// la API de Ludo es independiente (TenantActionContract).</para>
/// </summary>
public static class LudoFlowBuilder
{
    public const string FlowJsonEmpty = "{\"nodes\":[],\"edges\":[]}";

    public static string Build(
        string nombreNegocio,
        string objetivo,
        IReadOnlyList<StageSeed> etapas,
        IReadOnlyList<StageCriterion>? criterios,
        string? objetivoKey = null)
    {
        if (etapas is null || etapas.Count == 0)
            return FlowJsonEmpty;

        var orderedStages = etapas.OrderBy(e => e.Orden).ToList();
        var criterioByEtapa = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in criterios ?? [])
            if (!string.IsNullOrWhiteSpace(c.Etapa) && !string.IsNullOrWhiteSpace(c.Criterio))
                criterioByEtapa[c.Etapa] = c.Criterio;

        // Instrucción del nodo "pipeline": etapas en orden + criterios de avance.
        var sb = new StringBuilder();
        sb.Append("El pipeline de oportunidades tiene estas etapas en orden: ");
        sb.Append(string.Join(", ", orderedStages.Select((e, i) => $"{i + 1}) {e.Nombre}")));
        sb.Append(". ");
        var withCriteria = orderedStages
            .Where(e => criterioByEtapa.ContainsKey(e.Nombre))
            .Select(e => $"{e.Nombre} — {criterioByEtapa[e.Nombre]}")
            .ToList();
        if (withCriteria.Count > 0)
            sb.Append("Criterios de avance: " + string.Join("; ", withCriteria) + ". ");
        sb.Append("Para avanzar la oportunidad de fase, emití [ACTION:mover_fase] [PARAM:etapa=<nombre exacto de la etapa>]");
        if (!string.IsNullOrWhiteSpace(objetivoKey))
            sb.Append($" [PARAM:objetivo={objetivoKey}]");
        sb.Append(" cuando se cumpla su criterio.");
        var pipelineInstruction = sb.ToString();

        var registrarParams = string.IsNullOrWhiteSpace(objetivoKey)
            ? "[PARAM:resumen=<resumen breve de lo que pide el cliente>]"
            : $"[PARAM:objetivo={objetivoKey}] [PARAM:resumen=<resumen breve de lo que pide el cliente>]";
        var califInstruction =
            $"Evaluá si el cliente muestra una intención CALIFICADA según el objetivo del agente: «{objetivo}». " +
            $"Si califica, registrá la oportunidad emitiendo [ACTION:registrar_oportunidad] {registrarParams}. " +
            "Si todavía no califica, seguí conversando sin registrar nada.";

        var nodes = new JsonArray
        {
            Node("start", "start", new JsonObject { ["label"] = "Inicio" }),
            Node("calificar", "llm", new JsonObject { ["label"] = "Calificar intención", ["instruction"] = califInstruction }),
            Node("registrar", "action", new JsonObject { ["label"] = "Registrar oportunidad", ["actionSlug"] = "registrar_oportunidad" }),
            Node("pipeline", "llm", new JsonObject { ["label"] = "Avanzar el pipeline", ["instruction"] = pipelineInstruction }),
            Node("mover", "action", new JsonObject { ["label"] = "Mover de fase", ["actionSlug"] = "mover_fase" }),
            Node("end", "end", new JsonObject { ["label"] = "Fin" }),
        };

        var edges = new JsonArray
        {
            Edge("e1", "start", "calificar"),
            Edge("e2", "calificar", "registrar"),
            Edge("e3", "registrar", "pipeline"),
            Edge("e4", "pipeline", "mover"),
            Edge("e5", "mover", "end"),
        };

        return new JsonObject { ["nodes"] = nodes, ["edges"] = edges }.ToJsonString();
    }

    private static JsonObject Node(string id, string type, JsonObject data) =>
        new() { ["id"] = id, ["type"] = type, ["data"] = data };

    private static JsonObject Edge(string id, string source, string target) =>
        new() { ["id"] = id, ["source"] = source, ["target"] = target };
}
