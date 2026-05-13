namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Gestiona el flujo de validación de identidad (Escenario C).
/// Lee configuración de VALIDATE_IDENTITY desde CampaignTemplates.ActionConfigs.
/// </summary>
public interface IValidationService
{
    /// <summary>Inicia el flujo: carga preguntas de validación y retorna la primera pregunta al cliente.</summary>
    Task<ValidationFlow> StartFlowAsync(Guid tenantId, Guid? campaignTemplateId, string intent, CancellationToken ct = default);

    /// <summary>Procesa la respuesta del cliente y retorna la siguiente pregunta o llama al webhook.</summary>
    Task<ValidationFlow> ContinueFlowAsync(ValidationContext currentState, string clientAnswer, CancellationToken ct = default);

    /// <summary>Llama al webhook del tenant con las respuestas acumuladas y evalúa el resultado.</summary>
    Task<ValidationResult> CallWebhookAsync(Guid tenantId, Guid? campaignTemplateId, ValidationContext ctx, CancellationToken ct = default);
}

public record ValidationFlow(
    /// <summary>Estado actualizado de la validación.</summary>
    ValidationContext State,

    /// <summary>Mensaje a enviar al cliente (pregunta o resultado).</summary>
    string? MessageToClient,

    /// <summary>True cuando todas las preguntas fueron respondidas y se debe llamar al webhook.</summary>
    bool ReadyToValidate,

    /// <summary>True cuando el flujo se completó (éxito o fallo).</summary>
    bool IsComplete
);

public record ValidationResult(
    bool IsValid,
    string? MessageToClient,
    /// <summary>Datos retornados por el webhook para inyectar al contexto del agente.</summary>
    Dictionary<string, string>? ResponseData
);
