namespace AgentFlow.Domain.Entities;

/// <summary>
/// Integración Ludo CRM — Fase 1. Homologación entre una ETAPA del pipeline de Ludo y
/// una <see cref="ConversationLabel"/> (etiqueta) de TalkIA. Por tenant (row-level).
///
/// La clave de homologación es <see cref="LudoStageId"/> (ID ESTABLE de Ludo), NO el
/// nombre: así, renombrar una etapa en Ludo actualiza <see cref="Nombre"/> sin romper las
/// oportunidades en curso ni los prompts ya generados. La re-sincronización (Dirección C)
/// reconcilia esta tabla: alta, renombrado, baja (IsActive=false, preserva historial) y reorden.
///
/// El motor de flujos lee estas etapas vigentes para construir el bloque "## FLUJO ACTIVO"
/// y traducir [PARAM:etapa=Calificado] → LudoStageId al emitir [ACTION:mover_fase].
/// </summary>
public class StageLabelMap
{
    public Guid Id { get; set; }

    /// <summary>Tenant dueño de la homologación.</summary>
    public Guid TenantId { get; set; }

    /// <summary>ID estable de la etapa en Ludo. Clave de homologación (único por tenant).</summary>
    public string LudoStageId { get; set; } = string.Empty;

    /// <summary>Etiqueta equivalente en TalkIA (FK lógica a ConversationLabel.Id).</summary>
    public Guid LabelId { get; set; }

    /// <summary>Nombre vigente de la etapa (se actualiza en renombrados).</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Orden de la etapa en el pipeline.</summary>
    public int Orden { get; set; }

    /// <summary>Falso si la etapa fue eliminada en Ludo (soft-delete, preserva historial).</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
