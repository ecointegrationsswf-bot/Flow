using System.Security.Cryptography;
using System.Text;

namespace AgentFlow.Infrastructure.Provisioning;

/// <summary>
/// Integración Ludo CRM — Fase 2. Validación de los webhooks inbound de Ludo
/// (/api/provisioning/tenant y, en Fase 5, /api/ludo/stages/sync).
///
/// <para><b>Esquema (el que esperamos que Ludo implemente — a confirmar con su contrato):</b>
/// el firmante calcula <c>HMAC-SHA256(secret, "{timestamp}.{rawBody}")</c> y lo envía en hex.
/// Headers:
/// <list type="bullet">
///   <item><c>X-Ludo-Timestamp</c>: epoch en segundos del momento de firma.</item>
///   <item><c>X-Ludo-Signature</c>: <c>sha256=&lt;hex&gt;</c> (o el hex pelado).</item>
/// </list>
/// Se incluye el timestamp dentro del payload firmado para anti-replay: si la firma es
/// válida pero el timestamp está fuera de la ventana de tolerancia, se rechaza igual.</para>
///
/// <para>Reusa exactamente la misma primitiva criptográfica que la validación de Meta
/// (HMAC-SHA256 + comparación en tiempo constante) — no introduce dependencias nuevas.</para>
/// </summary>
public static class LudoWebhookSignature
{
    /// <summary>Ventana de frescura por defecto para anti-replay (segundos).</summary>
    public const int DefaultToleranceSeconds = 300;

    /// <summary>
    /// Valida la firma y la frescura. Devuelve (ok, motivo). <paramref name="nowUnix"/> es el
    /// epoch actual en segundos (se inyecta para testeabilidad).
    /// </summary>
    public static (bool Ok, string Reason) Validate(
        string rawBody,
        string? signatureHeader,
        string? timestampHeader,
        string? secret,
        long nowUnix,
        int toleranceSeconds = DefaultToleranceSeconds)
    {
        if (string.IsNullOrWhiteSpace(secret))
            return (false, "secreto Ludo no configurado");
        if (string.IsNullOrWhiteSpace(signatureHeader))
            return (false, "falta header de firma");
        if (string.IsNullOrWhiteSpace(timestampHeader) || !long.TryParse(timestampHeader, out var ts))
            return (false, "timestamp ausente o inválido");

        // Anti-replay: rechazar firmas viejas o del futuro fuera de tolerancia.
        if (Math.Abs(nowUnix - ts) > toleranceSeconds)
            return (false, "timestamp fuera de la ventana de tolerancia (replay)");

        var provided = signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? signatureHeader["sha256=".Length..]
            : signatureHeader;

        var signedPayload = $"{timestampHeader}.{rawBody}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        var computed = Convert.ToHexString(hash).ToLowerInvariant();

        var ok = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(provided.ToLowerInvariant()));

        return ok ? (true, "ok") : (false, "firma HMAC inválida");
    }
}
