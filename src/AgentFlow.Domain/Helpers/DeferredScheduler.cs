namespace AgentFlow.Domain.Helpers;

/// <summary>
/// Calcula <c>ScheduledFor</c> para los contactos diferidos por warm-up. La regla replica
/// el nodo "Programar diferidos" del workflow n8n: el contacto se vuelve elegible
/// "mañana a primera hora hábil" en la zona horaria del tenant, expresada en UTC.
/// </summary>
public static class DeferredScheduler
{
    /// <summary>
    /// Devuelve el instante UTC en que un contacto diferido vuelve a estar elegible:
    /// el día siguiente al <paramref name="nowUtc"/> dado, a la hora
    /// <paramref name="businessHoursStart"/>, interpretada en la zona <paramref name="timeZoneId"/>.
    /// </summary>
    /// <remarks>
    /// Si el <paramref name="timeZoneId"/> no se reconoce, cae a UTC (mejor diferir
    /// con un tope conocido que romper el lanzamiento por una mala configuración).
    /// </remarks>
    public static DateTime ComputeNextRunUtc(
        string? timeZoneId,
        TimeOnly businessHoursStart,
        DateTime nowUtc)
    {
        var tz = ResolveTimeZone(timeZoneId);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
            tz);

        var tomorrowLocal = nowLocal.Date.AddDays(1)
            .Add(businessHoursStart.ToTimeSpan());

        // Tomorrow:start es Unspecified; lo tratamos como local al TZ del tenant.
        var tomorrowUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(tomorrowLocal, DateTimeKind.Unspecified),
            tz);

        return tomorrowUtc;
    }

    private static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }
}
