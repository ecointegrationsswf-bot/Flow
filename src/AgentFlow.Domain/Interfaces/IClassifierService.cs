namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Clasifica la intención del mensaje entrante y determina a qué agente rutear.
/// Usa Claude con prompt compacto + historial + AgentRegistry del tenant.
/// </summary>
public interface IClassifierService
{
    Task<ClassificationResult> ClassifyAsync(ClassifierInput input, CancellationToken ct = default);
}

public record ClassifierInput(
    Guid TenantId,
    string Message,
    List<string> RecentHistory,
    List<AgentEntry> AvailableAgents,
    string? ActiveAgentSlug = null,
    string? ActiveCampaignName = null
);

public record ClassificationResult(
    string Intent,
    string AgentSlug,
    double Confidence,
    bool RequiresValidation,
    bool ShouldEscalate
);
