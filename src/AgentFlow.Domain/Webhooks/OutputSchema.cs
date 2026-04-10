namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Describe cómo interpretar la respuesta del webhook y qué hacer con cada campo.
/// Se persiste como JSON en CampaignTemplates.ActionConfigs[actionId].outputSchema.
/// El OutputInterpreter lo usa en runtime — sin lógica hardcodeada.
/// </summary>
public class OutputSchema
{
    public List<OutputField> Fields { get; set; } = [];
}
