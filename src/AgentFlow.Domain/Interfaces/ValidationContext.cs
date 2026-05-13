namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Estado del flujo de validación de identidad dentro de la sesión Redis.
/// El Cerebro acumula respuestas del cliente hasta completar todas las preguntas.
/// </summary>
public record ValidationContext(
    /// <summary>Intent que disparó la validación.</summary>
    string Intent,

    /// <summary>Preguntas pendientes que el Cerebro debe formular al cliente.</summary>
    List<string> PendingQuestions,

    /// <summary>Respuestas acumuladas del cliente (pregunta → respuesta).</summary>
    Dictionary<string, string> CollectedAnswers,

    /// <summary>Momento en que se inició el flujo de validación.</summary>
    DateTime StartedAt
);
