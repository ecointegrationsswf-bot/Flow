namespace AgentFlow.Domain.Enums;

public enum GestionResult
{
    Pending, PaymentCommitted, PaymentReceived,
    Rejected, Rescheduled, NoAnswer, EscalatedToHuman
}
