namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Envío de alertas operacionales a un canal de Microsoft Teams vía
/// Power Automate webhook. Cero retornos — best-effort fire-and-forget.
///
/// El payload del webhook es un JSON con shape <c>{"message": "&lt;texto&gt;"}</c>
/// que el flow de Power Automate procesa y postea como tarjeta en el canal
/// configurado.
///
/// Se considera "best-effort": si el webhook está mal configurado o cae,
/// el método NO debe lanzar excepción al caller (las notificaciones de error
/// no deben romper el flujo principal que las dispara — son ruido informativo
/// para el equipo, no críticas para el negocio).
/// </summary>
public interface ITeamsNotifier
{
    /// <summary>
    /// Envía un mensaje al canal de Teams configurado.
    /// </summary>
    /// <param name="message">Texto plano o Markdown. Se incrustará en el payload <c>{"message": "..."}</c>.</param>
    /// <param name="ct">Cancellation token. Si expira, la notificación se descarta sin error.</param>
    Task NotifyAsync(string message, CancellationToken ct = default);

    /// <summary>True si el notificador está configurado (hay URL del webhook).</summary>
    bool IsEnabled { get; }
}
