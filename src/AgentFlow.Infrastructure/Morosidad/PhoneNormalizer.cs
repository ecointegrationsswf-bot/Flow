namespace AgentFlow.Infrastructure.Morosidad;

/// <summary>
/// Normaliza números de teléfono a formato E.164, agregando el código de país
/// si no está presente. Valida longitud y rechaza números inválidos.
/// </summary>
public static class PhoneNormalizer
{
    /// <summary>
    /// Normaliza un número de teléfono crudo a E.164.
    /// </summary>
    /// <param name="rawPhone">Teléfono como viene del payload (puede tener guiones, espacios, paréntesis).</param>
    /// <param name="codigoPais">Código de país sin '+'. Ej: "507" para Panamá.</param>
    /// <returns>Teléfono E.164 ("+507XXXXXXXX") o null si no es válido.</returns>
    public static string? Normalize(string? rawPhone, string codigoPais)
    {
        if (string.IsNullOrWhiteSpace(rawPhone)) return null;

        // Extraer solo dígitos
        var digits = new string(rawPhone.Where(char.IsDigit).ToArray());

        if (string.IsNullOrEmpty(digits)) return null;

        // Rechazar números que son solo ceros
        if (digits.All(c => c == '0')) return null;

        // Limpiar código de país (quitar '+' o espacios)
        var code = codigoPais.TrimStart('+').Trim();

        // Si ya empieza con el código de país y tiene más dígitos después
        if (digits.StartsWith(code) && digits.Length > code.Length)
        {
            var withCode = "+" + digits;
            return IsValidLength(withCode) ? withCode : null;
        }

        // Agregar código de país
        var normalized = "+" + code + digits;
        return IsValidLength(normalized) ? normalized : null;
    }

    /// <summary>
    /// Valida si un número ya normalizado tiene formato E.164 válido.
    /// </summary>
    public static bool IsValid(string? normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized)) return false;
        if (!normalized.StartsWith('+')) return false;

        var digits = normalized[1..];
        if (!digits.All(char.IsDigit)) return false;
        if (digits.All(c => c == '0')) return false;

        return IsValidLength(normalized);
    }

    // E.164: mínimo 7 dígitos (cód.país + número), máximo 15 dígitos
    private static bool IsValidLength(string e164) =>
        e164.Length >= 8 && e164.Length <= 16; // +XXXXXXX = 8, +XXXXXXXXXXXXXXX = 16
}
