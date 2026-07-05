using AgentFlow.Domain.Flows;
using AgentFlow.Domain.Provisioning;
using Xunit;

namespace LudoIntegrationTests;

/// <summary>
/// Integración Ludo CRM — Fase 4. Tests del <see cref="LudoFlowBuilder"/>: el FlowJson generado
/// debe ser parseable por el Motor de Flujos (<see cref="FlowGraph"/>) y rendir las acciones
/// del pipeline. Son puros (sin DB ni red).
/// </summary>
public class LudoFlowBuilderTests
{
    private static readonly List<StageSeed> Etapas =
    [
        new("st_01", "Nuevo", 1),
        new("st_02", "Calificado", 2),
        new("st_03", "Cerrado", 3),
    ];

    private static readonly List<StageCriterion> Criterios =
    [
        new("Calificado", "el cliente confirma interés y presupuesto"),
        new("Cerrado", "se concreta la póliza"),
    ];

    [Fact]
    public void Build_NoEtapas_ReturnsEmptyFlow()
    {
        var json = LudoFlowBuilder.Build("XYZ", "vender", [], Criterios);
        Assert.Equal(LudoFlowBuilder.FlowJsonEmpty, json);

        var graph = FlowGraph.Parse(json);
        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void Build_ProducesGraphParseableByEngine_WithBothActions()
    {
        var json = LudoFlowBuilder.Build("Corretaje XYZ", "Calificar y cerrar pólizas", Etapas, Criterios);
        var graph = FlowGraph.Parse(json);

        // Tiene nodo de inicio y las dos acciones del pipeline, referenciadas por slug.
        Assert.NotNull(graph.StartNode());
        var actionSlugs = graph.Nodes
            .Where(n => n.Type == "action")
            .Select(n => n.Slug)
            .ToList();
        Assert.Contains("registrar_oportunidad", actionSlugs);
        Assert.Contains("mover_fase", actionSlugs);
    }

    [Fact]
    public void Build_PipelineNode_ListsStagesInOrderWithCriteria()
    {
        var json = LudoFlowBuilder.Build("XYZ", "vender", Etapas, Criterios);
        var graph = FlowGraph.Parse(json);

        var pipeline = graph.Nodes.Single(n => n.Type == "llm" && n.Label == "Avanzar el pipeline");
        var instr = pipeline.Instruction!;

        // Etapas presentes y ordenadas; criterios incluidos.
        Assert.Contains("Nuevo", instr);
        Assert.Contains("Calificado", instr);
        Assert.Contains("Cerrado", instr);
        Assert.True(instr.IndexOf("Nuevo", StringComparison.Ordinal) < instr.IndexOf("Cerrado", StringComparison.Ordinal));
        Assert.Contains("confirma interés", instr);
        Assert.Contains("[ACTION:mover_fase]", instr);
    }

    [Fact]
    public void Build_ToleratesNullCriteria()
    {
        var json = LudoFlowBuilder.Build("XYZ", "vender", Etapas, null);
        var graph = FlowGraph.Parse(json);
        Assert.NotNull(graph.StartNode());
        // Sigue listando las etapas aunque no haya criterios.
        var pipeline = graph.Nodes.Single(n => n.Type == "llm" && n.Label == "Avanzar el pipeline");
        Assert.Contains("Calificado", pipeline.Instruction!);
    }
}
