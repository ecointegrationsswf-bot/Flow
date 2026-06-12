using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Flows;

// Probe del motor de flujos F3a: carga el FlowJson real de PASESA y compila el WorkflowBlock
// en distintos estados de la conversación, más el round-trip del pendingRequest. Sin red ni BD.

var flowJson = File.ReadAllText(args.Length > 0 ? args[0] : @"C:\tmp\pasesa_flow.json");
var graph = FlowGraph.Parse(flowJson);
Console.WriteLine($"== Grafo parseado: {graph.Nodes.Count} nodos, {graph.Edges.Count} aristas ==");
foreach (var n in graph.Nodes)
    Console.WriteLine($"  [{n.Type}] {n.Label} slug={n.Slug ?? "-"} requiresAuth={n.RequiresAuth} isAuthEntry={n.IsAuthEntry}");

void Dump(string title, Conversation c)
{
    Console.WriteLine();
    Console.WriteLine($"################## {title} ##################");
    var s = new FlowSession(Guid.NewGuid(), "Consulta de pólizas PASESA", "2FA + pólizas + PDF", graph, c);
    Console.WriteLine(s.BuildBlock());
    Console.WriteLine($"---- FlowStateJson persistido: {s.SerializeState()}");
}

// Estado 1: cliente recién llega, NO validado, sin datos.
Dump("ESTADO 1 — sin validar, sin datos", new Conversation());

// Estado 2: ya validado (30 días) + pólizas en datos_consultados.
Dump("ESTADO 2 — validado + pólizas consultadas", new Conversation
{
    AuthenticatedUntil = DateTime.UtcNow.AddDays(30),
    AuthenticatedIdentityId = "1547725",
    ConversationDataJson = "{\"INSURED_INITIATE\":{\"status\":\"AUTO_VALIDADO\",\"polizas\":[{\"nroPoliza\":\"02-76-1000253\"}]}}"
});

// Estado 3: NO validado pero con pendingRequest (gate interceptó el PDF en un turno previo).
var c3 = new Conversation
{
    FlowStateJson = "{\"pendingRequest\":{\"actionSlug\":\"DOWNLOAD_POLICY_PDF\",\"params\":{\"idPoliza\":\"1286095\"}}}"
};
Dump("ESTADO 3 — sin validar, con PDF pendiente", c3);

// Round-trip del pending: set → serialize → re-parse → notify-executed → serialize.
Console.WriteLine();
Console.WriteLine("################## ROUND-TRIP pendingRequest ##################");
var conv = new Conversation();
var sess = new FlowSession(Guid.NewGuid(), "X", null, graph, conv);
sess.SetPending("DOWNLOAD_POLICY_PDF", new Dictionary<string, string?> { ["idPoliza"] = "1286095" });
var afterSet = sess.SerializeState();
Console.WriteLine($"tras SetPending:        {afterSet}");
conv.FlowStateJson = afterSet;
var sess2 = new FlowSession(Guid.NewGuid(), "X", null, graph, conv);
sess2.NotifyActionExecuted("DOWNLOAD_POLICY_PDF");
Console.WriteLine($"tras NotifyExecuted:    {sess2.SerializeState()}   (debe quedar {{}})");
var sess3 = new FlowSession(Guid.NewGuid(), "X", null, graph, new Conversation { FlowStateJson = afterSet });
sess3.NotifyActionExecuted("OTRA_ACCION");
Console.WriteLine($"NotifyExecuted(otra):   {sess3.SerializeState()}   (debe CONSERVAR el pending)");

Console.WriteLine();
Console.WriteLine("== Probe OK ==");
