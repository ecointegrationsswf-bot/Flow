namespace AgentFlow.Infrastructure.ScheduledJobs;

/// <summary>
/// Zona horaria de Panamá (UTC-5, sin DST). Las expresiones cron de los
/// ScheduledWebhookJobs se interpretan en esta zona para que el admin no
/// tenga que calcular UTC mentalmente. Cronos.GetNextOccurrence(from, tz)
/// recibe el DateTime UTC y la zona, y devuelve el próximo run en UTC.
///
/// Cross-platform: Windows usa "SA Pacific Standard Time", Linux/macOS usa
/// "America/Panama". .NET 8+ soporta IANA en Windows si se habilita el
/// runtime config, pero conservamos la rama explícita por compatibilidad.
/// </summary>
public static class PanamaTimeZone
{
    private static readonly Lazy<TimeZoneInfo> _instance = new(() =>
    {
        var id = OperatingSystem.IsWindows() ? "SA Pacific Standard Time" : "America/Panama";
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch
        {
            // Último fallback: zona fija UTC-5. Panamá no aplica DST.
            return TimeZoneInfo.CreateCustomTimeZone("UTC-05", TimeSpan.FromHours(-5), "UTC-05", "UTC-05");
        }
    });

    public static TimeZoneInfo Instance => _instance.Value;
}
