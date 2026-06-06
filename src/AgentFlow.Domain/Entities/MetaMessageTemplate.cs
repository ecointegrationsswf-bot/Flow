namespace AgentFlow.Domain.Entities;

/// <summary>
/// Plantilla de mensaje de WhatsApp (HSM) de Meta Cloud API.
///
/// En Meta las plantillas viven a nivel de WABA (cuenta de WhatsApp Business) y se
/// reutilizan entre números/campañas. Por eso aquí PERTENECEN a una <see cref="WhatsAppLine"/>
/// (que guarda el WABA + token del cliente); el maestro de campaña solo las REFERENCIARÁ
/// en la Fase 2. Todas las llamadas al Graph API toman SIEMPRE el WABA/token de ESA línea
/// (credenciales del tenant/cliente), nunca valores globales.
///
/// Hay TRES estados independientes en juego (decisión del usuario):
///   • Estado de la campaña            → en Campaign/CampaignTemplate (no acá).
///   • Estado de aprobación de Meta     → <see cref="MetaStatus"/> (PENDING/APPROVED/REJECTED...).
///   • Habilitada para uso en TalkIA    → <see cref="IsEnabled"/> (nuestro flag activar/desactivar).
/// Una plantilla es utilizable en una campaña (Fase 2) solo si
/// <c>MetaStatus == APPROVED</c> Y <c>IsEnabled == true</c>.
/// </summary>
public class MetaMessageTemplate
{
    public Guid Id { get; set; }

    /// <summary>Tenant dueño — toda query filtra por esto (multitenancy).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Línea Meta dueña (su WABA + token). Fuente de credenciales del cliente.</summary>
    public Guid WhatsAppLineId { get; set; }
    public WhatsAppLine? WhatsAppLine { get; set; }

    /// <summary>
    /// Maestro de campaña al que pertenece esta plantilla (Fase 2). Las generadas desde
    /// el prompt y las creadas desde la pestaña del maestro se estampan con su id. La
    /// campaña Meta envía SOLO las plantillas de SU maestro (aprobadas + activas), en
    /// orden de <see cref="SequenceOrder"/>. Null = plantilla de línea sin maestro
    /// (ej. importada de Meta) — no se usa automáticamente en un envío de campaña.
    /// </summary>
    public Guid? CampaignTemplateId { get; set; }

    /// <summary>Nombre Meta: snake_case minúsculas. Único por (línea/WABA, idioma).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Idioma/locale Meta: "es", "es_MX", "en_US"...</summary>
    public string Language { get; set; } = "es";

    /// <summary>Categoría Meta: MARKETING | UTILITY | AUTHENTICATION.</summary>
    public string Category { get; set; } = MetaTemplateCategories.Utility;

    // ── Componentes (Fase 1: HEADER de texto, BODY, FOOTER) ──────────────
    /// <summary>Tipo de encabezado: "TEXT" o null (sin encabezado). Fase 1: solo texto.</summary>
    public string? HeaderType { get; set; }
    /// <summary>Texto del encabezado (puede contener {{1}}).</summary>
    public string? HeaderText { get; set; }
    /// <summary>Cuerpo con placeholders {{1}},{{2}}.. (requerido por Meta).</summary>
    public string BodyText { get; set; } = string.Empty;
    /// <summary>Pie estático opcional.</summary>
    public string? FooterText { get; set; }

    /// <summary>
    /// Ejemplos de cada variable, JSON array de strings (["Juan","$55.91"]).
    /// Meta los exige al enviar la plantilla (body_text examples). El orden corresponde
    /// a {{1}},{{2}}.. del header+body combinados según el componente.
    /// </summary>
    public string? VariableSamplesJson { get; set; }

    // ── Estado en Meta ───────────────────────────────────────────────────
    /// <summary>Id que devuelve Meta al crear la plantilla (para resolver eventos del webhook).</summary>
    public string? MetaTemplateId { get; set; }

    /// <summary>
    /// Estado de aprobación de Meta. Valores en <see cref="MetaTemplateStatuses"/>:
    /// DRAFT (local, sin enviar) | PENDING | APPROVED | REJECTED | PAUSED | DISABLED.
    /// </summary>
    public string MetaStatus { get; set; } = MetaTemplateStatuses.Draft;

    /// <summary>Motivo del rechazo (si MetaStatus == REJECTED).</summary>
    public string? MetaRejectedReason { get; set; }

    // ── Estado en TalkIA (independiente de Meta) ─────────────────────────
    /// <summary>Nuestro flag activar/desactivar para uso en campañas.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Uso de la plantilla: <see cref="MetaTemplatePurposes.Launch"/> (mensaje inicial
    /// de campaña, validado al lanzar) o <see cref="MetaTemplatePurposes.FollowUp"/>
    /// (seguimientos automáticos, seleccionables por índice en el maestro). Tipo único.
    /// </summary>
    public string Purpose { get; set; } = MetaTemplatePurposes.Launch;

    // ── Secuencia de burbujas (split por '~') ────────────────────────────
    /// <summary>
    /// Agrupa las plantillas que nacieron de un mismo mensaje partido en burbujas
    /// (split por '~'). Todas las de un set comparten este GroupId. Null = plantilla
    /// suelta (manual o de una sola burbuja). En Fase 2 el envío de campaña manda
    /// TODAS las del grupo, en orden de <see cref="SequenceOrder"/>, replicando la
    /// estructura original de burbujas.
    /// </summary>
    public Guid? BubbleGroupId { get; set; }

    /// <summary>Orden dentro del grupo de burbujas (1-based). 1 para plantillas sueltas.</summary>
    public int SequenceOrder { get; set; } = 1;

    // ── Auditoría ────────────────────────────────────────────────────────
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    /// <summary>Última vez que sincronizamos el estado con Meta (botón o webhook).</summary>
    public DateTime? LastSyncedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }

    // Fase 2: mapeo {{n}} → campo del JSON del contacto. Se deja preparado, sin usar aún.
    public string? ParameterMappingJson { get; set; }
}

/// <summary>Categorías válidas de plantilla Meta.</summary>
public static class MetaTemplateCategories
{
    public const string Marketing = "MARKETING";
    public const string Utility = "UTILITY";
    public const string Authentication = "AUTHENTICATION";

    public static bool IsValid(string? c) =>
        c is Marketing or Utility or Authentication;
}

/// <summary>Uso de la plantilla dentro del maestro de campaña.</summary>
public static class MetaTemplatePurposes
{
    public const string Launch = "Launch";       // mensaje inicial de campaña
    public const string FollowUp = "FollowUp";   // seguimientos automáticos

    public static bool IsValid(string? p) => p is Launch or FollowUp;
}

/// <summary>Estados de aprobación de Meta (más nuestro DRAFT local).</summary>
public static class MetaTemplateStatuses
{
    public const string Draft = "DRAFT";        // local, aún no enviada a Meta
    public const string Pending = "PENDING";    // enviada, en revisión
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
    public const string Paused = "PAUSED";
    public const string Disabled = "DISABLED";
}
