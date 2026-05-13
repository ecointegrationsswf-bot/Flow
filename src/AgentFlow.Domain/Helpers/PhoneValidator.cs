namespace AgentFlow.Domain.Helpers;

/// <summary>
/// Validador de teléfonos para el CampaignIntakeService v2.
///
/// Wrapper sobre PhoneNormalizer que añade el rechazo de doble código país
/// (ej: "+507507XXXXXXXX") — patrón documentado de TalkIA donde un dato ya
/// venía con prefijo y el sistema lo volvía a anteponer, generando números
/// que Meta bloquea por inválidos.
/// </summary>
public static class PhoneValidator
{
    /// <summary>
    /// Normaliza y valida un teléfono crudo. Retorna null si no es utilizable.
    /// </summary>
    public static string? Validate(string? rawPhone, string codigoPais)
    {
        var normalized = PhoneNormalizer.Normalize(rawPhone, codigoPais);
        if (normalized is null) return null;

        // Rechazo doble código país: "+{code}{code}..." no puede ser un teléfono real.
        var code = codigoPais.TrimStart('+').Trim();
        if (code.Length > 0 && normalized.StartsWith($"+{code}{code}", StringComparison.Ordinal))
            return null;

        return normalized;
    }
}
