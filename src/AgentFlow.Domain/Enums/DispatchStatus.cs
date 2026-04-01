namespace AgentFlow.Domain.Enums;

public enum DispatchStatus
{
    Pending,    // Esperando ser procesado
    Claimed,    // n8n lo tomó — en proceso ahora mismo
    Sent,       // Mensaje entregado a UltraMsg exitosamente
    Error,      // Falló definitivamente (agotó reintentos)
    Retry,      // Falló, programado para reintento
    Skipped     // Teléfono inválido u omitido intencionalmente
}
