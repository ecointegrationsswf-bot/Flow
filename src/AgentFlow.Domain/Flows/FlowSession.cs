using System.Text;
using System.Text.Json.Nodes;
using AgentFlow.Domain.Entities;

namespace AgentFlow.Domain.Flows;

/// <summary>Solicitud confidencial interceptada por el gate, a reanudar tras autenticar.</summary>
public sealed record PendingRequest(string ActionSlug, Dictionary<string, string?> Params, string? ReturnNodeId);

/// <summary>
/// Motor de flujos — Fase 3. Sesión de ejecución de UN flujo sobre UNA conversación, para UN turno.
/// La construye <see cref="IWorkflowEngine"/> a partir del grafo (TenantFlow) + el estado durable
/// (Conversation.FlowStateJson). Responsabilidades:
///   • <see cref="BuildBlock"/>  → compila el "## FLUJO ACTIVO" (guion + estado + reglas) que ve el LLM.
///   • <see cref="SetPending"/>  → el handler la llama cuando el gate intercepta una acción confidencial.
///   • <see cref="NotifyActionExecuted"/> → limpia el pending cuando esa acción finalmente corre.
///   • <see cref="SerializeState"/> → vuelca el estado para persistir en Conversation.FlowStateJson.
/// Es estado DERIVADO: "dónde va" la conversación se infiere de auth + datos_consultados + pending,
/// no de un puntero rígido (alineado con el modelo LLM-intérprete: el cliente puede salirse del guion).
/// </summary>
public sealed class FlowSession
{
    public Guid FlowId { get; }
    public string FlowName { get; }
    public string? FlowDescription { get; }

    private readonly FlowGraph _graph;
    private readonly Conversation _conversation;
    private PendingRequest? _pending;

    public FlowSession(Guid flowId, string flowName, string? description, FlowGraph graph, Conversation conversation)
    {
        FlowId = flowId;
        FlowName = flowName;
        FlowDescription = description;
        _graph = graph;
        _conversation = conversation;
        _pending = ParsePending(conversation.FlowStateJson);
    }

    /// <summary>Registra la solicitud confidencial que el gate frenó (para reanudarla tras validar).</summary>
    public void SetPending(string slug, IReadOnlyDictionary<string, string?> @params)
    {
        if (string.IsNullOrWhiteSpace(slug)) return;
        _pending = new PendingRequest(slug, new Dictionary<string, string?>(@params, StringComparer.OrdinalIgnoreCase), null);
    }

    /// <summary>Si la acción ejecutada es la que estaba pendiente, la solicitud se considera cumplida.</summary>
    public void NotifyActionExecuted(string slug)
    {
        if (_pending is not null && string.Equals(_pending.ActionSlug, slug, StringComparison.OrdinalIgnoreCase))
            _pending = null;
    }

    /// <summary>Estado durable para persistir en Conversation.FlowStateJson.</summary>
    public string SerializeState()
    {
        var obj = new JsonObject();
        if (_pending is not null)
        {
            var prms = new JsonObject();
            foreach (var (k, v) in _pending.Params) prms[k] = v;
            var p = new JsonObject { ["actionSlug"] = _pending.ActionSlug, ["params"] = prms };
            if (_pending.ReturnNodeId is not null) p["returnNodeId"] = _pending.ReturnNodeId;
            obj["pendingRequest"] = p;
        }
        return obj.Count == 0 ? "{}" : obj.ToJsonString();
    }

    /// <summary>
    /// Compila el bloque "## FLUJO ACTIVO" que se inyecta al system prompt del LLM. Combina el grafo
    /// (los pasos como guía) con el estado DERIVADO de esta conversación (auth, datos_consultados, pending).
    /// </summary>
    public string BuildBlock()
    {
        var isAuthed = _conversation.AuthenticatedUntil is { } until && until > DateTime.UtcNow;
        var consultedKeys = ParseConsultedKeys(_conversation.ConversationDataJson);
        var confidential = _graph.Nodes
            .Where(n => string.Equals(n.Type, "action", StringComparison.OrdinalIgnoreCase)
                        && n.RequiresAuth && !string.IsNullOrWhiteSpace(n.Slug))
            .Select(n => n.Slug!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"## FLUJO ACTIVO: {FlowName}");
        if (!string.IsNullOrWhiteSpace(FlowDescription)) sb.AppendLine(FlowDescription);
        sb.AppendLine();
        sb.AppendLine("Seguí estos pasos como GUÍA (el cliente puede salirse del orden; atendelo y luego volvés):");
        foreach (var node in OrderedNodes())
        {
            var line = DescribeNode(node);
            if (line is not null) sb.AppendLine("- " + line);
        }

        sb.AppendLine();
        sb.AppendLine("Estado actual de esta conversación:");
        sb.AppendLine(isAuthed
            ? $"- El cliente YA está validado (hasta {_conversation.AuthenticatedUntil:yyyy-MM-dd HH:mm} UTC). Podés ejecutar acciones confidenciales."
            : "- El cliente NO está validado todavía.");
        sb.AppendLine(consultedKeys.Count > 0
            ? $"- Datos ya consultados (disponibles en \"datos_consultados\"): {string.Join(", ", consultedKeys)}."
            : "- Aún no se consultó ningún dato de negocio.");
        if (_pending is not null)
            sb.AppendLine($"- PENDIENTE: el cliente pidió \"{_pending.ActionSlug}\". Apenas valide su identidad, cumplí esa solicitud (pedí los datos que falten, p. ej. cuál póliza).");

        sb.AppendLine();
        sb.AppendLine("REGLAS DURAS de este flujo:");
        sb.AppendLine("- Máximo UNA acción [ACTION:...] por turno.");
        sb.AppendLine("- En el MISMO turno en que una acción ya devolvió resultado, NO emitas otra acción: redactá la respuesta final al cliente usando esos datos.");
        if (confidential.Count > 0)
            sb.AppendLine($"- Acciones CONFIDENCIALES (requieren validación previa): {string.Join(", ", confidential)}. Si el cliente NO está validado, NO las ejecutes ni reveles datos: pedile la cédula para validar primero. Ya validado, cumplí lo que pidió.");
        sb.AppendLine("- No inventes datos: si te falta un parámetro para una acción, pedíselo al cliente.");

        return sb.ToString().TrimEnd();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    // Orden de lectura del guion: BFS desde el nodo Inicio siguiendo aristas; los nodos no
    // alcanzables se apendan al final en orden de declaración. Es solo para presentación.
    private IEnumerable<FlowNode> OrderedNodes()
    {
        var start = _graph.StartNode();
        if (start is null) return _graph.Nodes;

        var ordered = new List<FlowNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<FlowNode>();
        queue.Enqueue(start);
        seen.Add(start.Id);
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            ordered.Add(n);
            foreach (var e in _graph.OutEdges(n.Id))
                if (_graph.NodeById(e.Target) is { } tgt && seen.Add(tgt.Id))
                    queue.Enqueue(tgt);
        }
        foreach (var n in _graph.Nodes)
            if (seen.Add(n.Id)) ordered.Add(n);
        return ordered;
    }

    private static string? DescribeNode(FlowNode n)
    {
        var label = string.IsNullOrWhiteSpace(n.Label) ? n.Type : n.Label;
        return (n.Type?.ToLowerInvariant()) switch
        {
            "start" or "end" => null, // implícitos
            "action" => string.IsNullOrWhiteSpace(n.Slug)
                ? $"{label}: (acción sin configurar)"
                : $"{label}: emití [ACTION:{n.Slug}] cuando corresponda{(n.RequiresAuth ? " — CONFIDENCIAL (requiere validación)" : "")}.",
            "llm" => $"{label}: {(string.IsNullOrWhiteSpace(n.Instruction) ? "respondé libremente." : n.Instruction)}",
            "message" => $"{label}: enviá el mensaje: \"{n.Text}\".",
            "gate" => $"{label}: validá la identidad del cliente si todavía no está validado.",
            "wait" => $"{label}: esperá la respuesta del cliente{(string.IsNullOrWhiteSpace(n.WaitFor) ? "" : $" ({n.WaitFor})")}.",
            "condition" => $"{label}: decidí según \"{n.ConditionPath}\" {n.ConditionOperator} \"{n.ConditionValue}\" (sí → rama inferior, no → rama derecha).",
            _ => null,
        };
    }

    private static List<string> ParseConsultedKeys(string? json)
    {
        var keys = new List<string>();
        if (string.IsNullOrWhiteSpace(json)) return keys;
        try
        {
            if (JsonNode.Parse(json) is JsonObject o)
                foreach (var kv in o) keys.Add(kv.Key);
        }
        catch { /* bolsa corrupta → sin keys */ }
        return keys;
    }

    private static PendingRequest? ParsePending(string? flowStateJson)
    {
        if (string.IsNullOrWhiteSpace(flowStateJson)) return null;
        try
        {
            if (JsonNode.Parse(flowStateJson) is JsonObject o && o["pendingRequest"] is JsonObject p)
            {
                var slug = p["actionSlug"]?.ToString();
                if (string.IsNullOrWhiteSpace(slug)) return null;
                var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                if (p["params"] is JsonObject pr)
                    foreach (var kv in pr) dict[kv.Key] = kv.Value?.ToString();
                return new PendingRequest(slug, dict, p["returnNodeId"]?.ToString());
            }
        }
        catch { /* estado corrupto → sin pending */ }
        return null;
    }
}
