namespace AgentFlow.Domain.Enums;

/// <summary>
/// Rol semántico de un campo dentro de la definición de una acción de descarga.
/// Marca a qué slot del flujo de campañas/gestión corresponde la columna.
/// </summary>
public enum FieldRole
{
    /// <summary>Campo extra — sólo se persiste en ExtractedDataJson y se exporta. Sin uso semántico.</summary>
    None,
    /// <summary>Teléfono del cliente — obligatorio. Identidad del contacto y clave de agrupación.</summary>
    Phone,
    /// <summary>Nombre del cliente — obligatorio. Saludo del agente IA y display en monitor.</summary>
    ClientName,
    /// <summary>Referencia externa del registro (póliza, cédula, ID cliente) — obligatorio. Lo usa el webhook de gestión de regreso.</summary>
    KeyValue,
    /// <summary>Monto pendiente — opcional. El agente IA lo cita en el mensaje al cliente.</summary>
    Amount,
    /// <summary>Número de póliza — opcional. Persistido aparte para queries específicas.</summary>
    PolicyNumber
}
