namespace AgentFlow.Domain.Enums;

public enum ContactGroupStatus
{
    /// <summary>Grupo creado, esperando procesamiento.</summary>
    Pending,
    /// <summary>Campaña de WhatsApp creada exitosamente para este grupo.</summary>
    CampaignCreated,
    /// <summary>Se notificó al operador por email/SignalR — modo manual.</summary>
    Notified,
    /// <summary>Omitido (ej: contacto ya tiene campaña activa).</summary>
    Skipped,
    /// <summary>Mensaje de WhatsApp enviado al cliente (n8n confirmó dispatch). Sólo aplica con autoCrearCampanas=true.</summary>
    MessageSent,
    /// <summary>El cliente respondió al menos una vez. Sólo aplica con autoCrearCampanas=true.</summary>
    ClientReplied
}
