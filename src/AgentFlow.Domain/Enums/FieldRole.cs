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
    PolicyNumber,
    /// <summary>
    /// Email del ejecutivo de cobros responsable de la fila — opcional. Si está
    /// mapeado y el flag SplitCampaignsByExecutive está activo, el sistema matchea
    /// este email contra los AppUsers del tenant y crea una campaña por ejecutivo.
    /// Las filas que no matchean (o sin este mapeo) caen a la campaña "system:download".
    /// </summary>
    ExecutiveEmail,
    /// <summary>
    /// Celular del ejecutivo de cobros — opcional. Cuando el email del ejecutivo
    /// matchea un AppUser (split por ejecutivo activo), este número actualiza el
    /// NotifyPhone del perfil (normalizado a E.164) para que la acción de
    /// transferencia a humano sepa a qué número avisar. El archivo es la fuente
    /// de verdad: sobrescribe el NotifyPhone en cada descarga. Si viene vacío, no
    /// se toca el valor actual del perfil.
    /// </summary>
    ExecutivePhone
}
