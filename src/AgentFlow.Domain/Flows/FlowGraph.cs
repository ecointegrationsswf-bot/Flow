using System.Text.Json;

namespace AgentFlow.Domain.Flows;

/// <summary>
/// Motor de flujos — Fase 3. Representación de SOLO LECTURA del grafo del lienzo
/// (TenantFlow.FlowJson, formato reactflow: { nodes:[{id,type,data}], edges:[{id,source,target,sourceHandle,label}] }).
/// Parser tolerante: cualquier JSON inválido o vacío produce un grafo vacío (el motor lo trata
/// como "sin flujo" y no hace nada). No tiene dependencias de infraestructura — es puro.
/// </summary>
public sealed class FlowGraph
{
    public List<FlowNode> Nodes { get; } = [];
    public List<FlowEdge> Edges { get; } = [];

    public FlowNode? NodeById(string id) =>
        Nodes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.Ordinal));

    public FlowNode? StartNode() =>
        Nodes.FirstOrDefault(n => string.Equals(n.Type, "start", StringComparison.OrdinalIgnoreCase));

    public IEnumerable<FlowEdge> OutEdges(string nodeId) =>
        Edges.Where(e => string.Equals(e.Source, nodeId, StringComparison.Ordinal));

    /// <summary>Parsea el FlowJson. Nunca lanza: ante error devuelve un grafo vacío.</summary>
    public static FlowGraph Parse(string? flowJson)
    {
        var g = new FlowGraph();
        if (string.IsNullOrWhiteSpace(flowJson)) return g;
        try
        {
            using var doc = JsonDocument.Parse(flowJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return g;

            if (root.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
                foreach (var n in nodes.EnumerateArray())
                    g.Nodes.Add(FlowNode.From(n));

            if (root.TryGetProperty("edges", out var edges) && edges.ValueKind == JsonValueKind.Array)
                foreach (var e in edges.EnumerateArray())
                    g.Edges.Add(FlowEdge.From(e));
        }
        catch
        {
            return new FlowGraph();
        }
        return g;
    }
}

/// <summary>
/// Un nodo del lienzo. `data` es libre (lo escribe el frontend); acá se exponen, tipados, los
/// campos que el motor necesita. `type` ∈ start | action | condition | llm | gate | wait | message | end.
/// </summary>
public sealed class FlowNode
{
    public string Id { get; private init; } = "";
    public string Type { get; private init; } = "";
    private readonly Dictionary<string, string?> _data = new(StringComparer.OrdinalIgnoreCase);

    private string? Str(string key) => _data.TryGetValue(key, out var v) ? v : null;
    private bool Flag(string key) => _data.TryGetValue(key, out var v) && string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);

    public string? Label => Str("label");
    public string? Slug => Str("actionSlug");          // dropdown de Acción en el lienzo
    public bool RequiresAuth => Flag("requiresAuth");   // checkbox "Confidencial"
    public bool IsAuthEntry => Flag("isAuthEntry");     // Inicio del sub-flujo de validación
    public string? Instruction => Str("instruction");   // nodo LLM
    public string? Text => Str("text");                 // nodo Mensaje
    public string? AuthMessage => Str("authMessage");   // nodo Gate
    public string? WaitFor => Str("waitFor");           // nodo Esperar
    public string? ConditionPath => Str("path");        // nodo Condición
    public string? ConditionOperator => Str("operator");
    public string? ConditionValue => Str("value");

    internal static FlowNode From(JsonElement n)
    {
        var node = new FlowNode
        {
            Id = n.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            Type = n.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
        };
        if (n.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            foreach (var p in data.EnumerateObject())
                node._data[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Number => p.Value.ToString(),
                    JsonValueKind.Null => null,
                    _ => p.Value.ToString(),
                };
        return node;
    }
}

/// <summary>Arista dirigida del grafo. `SourceHandle` distingue ramas (ej: yes/no de una Condición).</summary>
public sealed class FlowEdge
{
    public string Id { get; private init; } = "";
    public string Source { get; private init; } = "";
    public string Target { get; private init; } = "";
    public string? SourceHandle { get; private init; }
    public string? Label { get; private init; }

    internal static FlowEdge From(JsonElement e) => new()
    {
        Id = e.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
        Source = e.TryGetProperty("source", out var s) ? s.GetString() ?? "" : "",
        Target = e.TryGetProperty("target", out var t) ? t.GetString() ?? "" : "",
        SourceHandle = e.TryGetProperty("sourceHandle", out var sh) ? sh.GetString() : null,
        Label = e.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null,
    };
}
