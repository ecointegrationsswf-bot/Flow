namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Describe cómo construir el request hacia el webhook del tenant.
/// Se persiste como JSON en CampaignTemplates.ActionConfigs[actionId].inputSchema.
/// El ActionExecutorService lo ejecuta en runtime para construir el payload dinámicamente.
/// </summary>
public class InputSchema
{
    /// <summary>application/json | application/x-www-form-urlencoded | multipart/form-data</summary>
    public string ContentType { get; set; } = "application/json";

    /// <summary>POST | GET | PUT | PATCH</summary>
    public string HttpMethod { get; set; } = "POST";

    /// <summary>flat | nested — aplica solo a application/json</summary>
    public string Structure { get; set; } = "flat";

    /// <summary>Lista de campos que conforman el payload.</summary>
    public List<InputField> Fields { get; set; } = [];
}
