namespace AgentFlow.Domain.Entities;

/// <summary>
/// Motor de flujos — Fase 4 (lienzo visual). Un flujo (workflow) visual definido por
/// el super-admin PARA UN TENANT. Se diseña en el lienzo (reactflow) y se guarda como
/// grafo JSON. El motor de ejecución (Fase 3) lo interpretará en runtime; por ahora esta
/// entidad es la persistencia del DISEÑO (autoría), aditiva y sin tocar nada existente.
///
/// <para><b>FlowJson</b>: grafo serializado del lienzo — forma
/// { "nodes": [ { id, type, position:{x,y}, data:{...} } ], "edges": [ { id, source, target, sourceHandle? } ] }.
/// `type` ∈ start | action | condition | llm | gate | wait | message | end. Se guarda como
/// blob: el backend no lo parsea todavía (lo hará el FlowEngine de la Fase 3).</para>
/// </summary>
public class TenantFlow
{
    public Guid Id { get; set; }

    /// <summary>Tenant dueño del flujo.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Nombre legible del flujo (ej: "Atención reclamos PASESA").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Descripción opcional.</summary>
    public string? Description { get; set; }

    /// <summary>Grafo del lienzo serializado (nodes + edges). Default = lienzo vacío.</summary>
    public string FlowJson { get; set; } = "{\"nodes\":[],\"edges\":[]}";

    /// <summary>Si false, el flujo está guardado pero no se considera activo para ejecución.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
