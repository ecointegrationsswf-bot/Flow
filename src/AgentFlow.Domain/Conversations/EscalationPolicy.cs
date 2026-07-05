namespace AgentFlow.Domain.Conversations;

/// <summary>
/// Escalamiento robusto (genérico, opt-in por tenant vía Tenant.KeepAiActiveUntilTakeover).
/// Política PURA (sin dependencias) que decide el comportamiento de la IA frente a una
/// escalada. Extraída para ser testeable y auto-documentada; la usa ProcessIncomingMessageCommand.
///
/// Principio: la IA enmudece cuando un HUMANO TOMA la conversación (HandledByUserId), NO cuando
/// ocurre la auto-escalada. Con el flag apagado (default), el comportamiento es el histórico
/// (la auto-escalada con TRANSFER_CHAT pausa la IA).
/// </summary>
public static class EscalationPolicy
{
    /// <summary>
    /// ¿La auto-escalada debe SILENCIAR a la IA (setear IsHumanHandled)?
    /// Solo si el template tiene TRANSFER_CHAT (<paramref name="shouldPause"/>) Y el tenant NO
    /// activó <paramref name="keepAiActiveUntilTakeover"/>. Con el flag on, la IA sigue activa
    /// hasta que un humano tome la conversación.
    /// </summary>
    public static bool ShouldMuteOnAutoEscalation(bool shouldPause, bool keepAiActiveUntilTakeover)
        => shouldPause && !keepAiActiveUntilTakeover;

    /// <summary>
    /// ¿Inyectar el bloque "## ESCALADA EN CURSO" al prompt? Solo cuando el tenant activó el flag,
    /// la conversación está escalada (<paramref name="isEscalatedToHuman"/>) y ningún humano la
    /// tomó todavía (<paramref name="isHumanHandled"/>=false). En ese estado la IA sigue activa y
    /// necesita la guía para no prometer resolver lo escalado.
    /// </summary>
    public static bool ShouldInjectEscalationBlock(
        bool keepAiActiveUntilTakeover, bool isEscalatedToHuman, bool isHumanHandled)
        => keepAiActiveUntilTakeover && isEscalatedToHuman && !isHumanHandled;
}
