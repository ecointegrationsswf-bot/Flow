namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Resultado del clasificador IA al etiquetar una conversación. Producido por
/// el ConversationLabelingJob a partir de la respuesta de Claude.
/// </summary>
public sealed record LabelingResult(
    string LabelName,         // matchea con ConversationLabel.Name del tenant
    double Confidence,        // 0.0 a 1.0
    string? Reasoning,        // justificación humana del clasificador
    string? ExtractedDate);   // fecha extraída del diálogo si aplica (compromiso de pago, etc.)
