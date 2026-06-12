namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Motor de flujos — Fase 2. Política de autenticación de una acción, embebida en el
/// contrato (ActionConfigBundleJson, por tenant). Declara qué RESULTADO de la acción
/// marca al cliente como autenticado, por cuánto tiempo, y de dónde sale la identidad.
///
/// Ejemplo PASESA: una acción cuyo response tiene `status` ∈ {VALIDO, AUTO_VALIDADO}
/// autentica al cliente por 30 días, tomando la identidad de `idEntidad`:
///   { "whenPath":"status", "equalsAny":["VALIDO","AUTO_VALIDADO"],
///     "durationMinutes":43200, "identityPath":"idEntidad" }
///
/// El enforcement (bloquear acciones `requiresAuth` sin auth y setear el estado tras
/// autenticar) lo aplica ProcessIncomingMessageCommand de forma determinística —
/// NO depende del prompt/LLM.
/// </summary>
public class AuthPolicy
{
    /// <summary>Path en el JSON response a evaluar (dot-notation, ej: "status").</summary>
    public string WhenPath { get; init; } = "status";

    /// <summary>El valor del WhenPath debe estar (case-insensitive) en esta lista para autenticar.</summary>
    public List<string>? EqualsAny { get; init; }

    /// <summary>Duración de la sesión autenticada en minutos. Default 30 días (43200).</summary>
    public int DurationMinutes { get; init; } = 43200;

    /// <summary>Path opcional al identificador del asegurado en el response (ej: "idEntidad").</summary>
    public string? IdentityPath { get; init; }
}
