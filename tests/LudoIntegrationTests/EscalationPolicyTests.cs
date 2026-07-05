using AgentFlow.Domain.Conversations;
using Xunit;

namespace LudoIntegrationTests;

/// <summary>
/// Escalamiento robusto — Fases A+B. Verifica la política pura: la IA enmudece solo en la TOMA
/// humana, no en la auto-escalada (cuando el tenant activó KeepAiActiveUntilTakeover); y el bloque
/// de guía se inyecta solo mientras está escalada y sin tomar.
/// </summary>
public class EscalationPolicyTests
{
    // ── ShouldMuteOnAutoEscalation ──────────────────────────────────────────────────────

    [Fact]
    public void FlagOff_WithTransferChat_Mutes_LikeToday()
    {
        // Comportamiento histórico: con TRANSFER_CHAT (shouldPause) y sin flag → mutea.
        Assert.True(EscalationPolicy.ShouldMuteOnAutoEscalation(shouldPause: true, keepAiActiveUntilTakeover: false));
    }

    [Fact]
    public void FlagOn_WithTransferChat_DoesNotMute()
    {
        // Fix Bruce: con el flag on, la auto-escalada NO mutea (la IA sigue activa).
        Assert.False(EscalationPolicy.ShouldMuteOnAutoEscalation(shouldPause: true, keepAiActiveUntilTakeover: true));
    }

    [Fact]
    public void NoTransferChat_NeverMutes_RegardlessOfFlag()
    {
        Assert.False(EscalationPolicy.ShouldMuteOnAutoEscalation(shouldPause: false, keepAiActiveUntilTakeover: false));
        Assert.False(EscalationPolicy.ShouldMuteOnAutoEscalation(shouldPause: false, keepAiActiveUntilTakeover: true));
    }

    // ── ShouldInjectEscalationBlock ─────────────────────────────────────────────────────

    [Fact]
    public void Block_Injected_WhenFlagOn_Escalated_AndNotTaken()
    {
        Assert.True(EscalationPolicy.ShouldInjectEscalationBlock(
            keepAiActiveUntilTakeover: true, isEscalatedToHuman: true, isHumanHandled: false));
    }

    [Fact]
    public void Block_NotInjected_WhenHumanTookConversation()
    {
        // Si un humano tomó la conversación, la IA calla (no hay bloque, no hay turno IA).
        Assert.False(EscalationPolicy.ShouldInjectEscalationBlock(
            keepAiActiveUntilTakeover: true, isEscalatedToHuman: true, isHumanHandled: true));
    }

    [Fact]
    public void Block_NotInjected_WhenFlagOff_OrNotEscalated()
    {
        Assert.False(EscalationPolicy.ShouldInjectEscalationBlock(false, true, false));   // flag off
        Assert.False(EscalationPolicy.ShouldInjectEscalationBlock(true, false, false));   // no escalada
    }
}
