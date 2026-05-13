using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;

namespace AgentFlow.Infrastructure.Time;

/// <summary>
/// Implementación de <see cref="IBusinessHoursClock"/>. Usa <see cref="TimeZoneInfo"/>
/// con fallback a "America/Panama" cuando el ID configurado no se encuentra
/// (común en Windows que entiende IDs IANA solo desde .NET 6 con ICU).
///
/// Convenciones:
/// - <c>CampaignTemplate.AttentionDays</c> usa el formato ISO-8601 (Lunes=1, Domingo=7).
/// - <c>CampaignTemplate.SendFrom/SendUntil</c> son strings "HH:mm".
/// - Si <paramref name="template"/> no aporta horario, usamos el del tenant.
/// </summary>
public sealed class BusinessHoursClock : IBusinessHoursClock
{
    private static readonly int[] DefaultAttentionDays = [1, 2, 3, 4, 5]; // Lun–Vie

    public bool IsWithinBusinessHours(DateTime utcInstant, Tenant tenant, CampaignTemplate? template = null)
    {
        var (tz, start, end, days) = ResolveWindow(tenant, template);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc), tz);
        if (!days.Contains(IsoDay(local.DayOfWeek))) return false;
        var t = TimeOnly.FromDateTime(local);
        return t >= start && t < end;
    }

    public DateTime NextBusinessWindowStartUtc(DateTime utcAfter, Tenant tenant, CampaignTemplate? template = null)
    {
        var (tz, start, end, days) = ResolveWindow(tenant, template);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcAfter, DateTimeKind.Utc), tz);

        // Si ya estamos dentro de la ventana hoy, devolvemos utcAfter sin cambio.
        if (days.Contains(IsoDay(local.DayOfWeek)))
        {
            var t = TimeOnly.FromDateTime(local);
            if (t >= start && t < end)
                return utcAfter;
            if (t < start)
                return ToUtc(local.Date.Add(start.ToTimeSpan()), tz);
        }

        // Avanzar día por día hasta encontrar uno laboral.
        var probe = local.Date.AddDays(1);
        for (var i = 0; i < 14; i++) // 2 semanas de margen suficiente para feriados
        {
            if (days.Contains(IsoDay(probe.DayOfWeek)))
                return ToUtc(probe.Add(start.ToTimeSpan()), tz);
            probe = probe.AddDays(1);
        }
        // Fallback defensivo — no debería pasar con días default.
        return ToUtc(probe.Add(start.ToTimeSpan()), tz);
    }

    public DateTime AlignToBusinessHoursUtc(DateTime utcCandidate, Tenant tenant, CampaignTemplate? template = null)
        => IsWithinBusinessHours(utcCandidate, tenant, template)
            ? utcCandidate
            : NextBusinessWindowStartUtc(utcCandidate, tenant, template);

    // ── Helpers privados ─────────────────────────────────────────────────────

    private static (TimeZoneInfo Tz, TimeOnly Start, TimeOnly End, IReadOnlyList<int> Days)
        ResolveWindow(Tenant tenant, CampaignTemplate? template)
    {
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone ?? "America/Panama"); }
        catch
        {
            try { tz = TimeZoneInfo.FindSystemTimeZoneById("America/Panama"); }
            catch { tz = TimeZoneInfo.Utc; }
        }

        // Override por campaña > default por tenant.
        TimeOnly start = tenant.BusinessHoursStart;
        TimeOnly end   = tenant.BusinessHoursEnd;
        if (template is not null)
        {
            if (TimeOnly.TryParse(template.SendFrom, out var sf))  start = sf;
            if (TimeOnly.TryParse(template.SendUntil, out var su)) end   = su;
        }

        IReadOnlyList<int> days =
            template?.AttentionDays is { Count: > 0 } cfg ? cfg : DefaultAttentionDays;

        return (tz, start, end, days);
    }

    /// <summary>Convierte <see cref="DayOfWeek"/> (Sun=0 … Sat=6) a ISO-8601 (Mon=1 … Sun=7).</summary>
    private static int IsoDay(DayOfWeek d) => d == DayOfWeek.Sunday ? 7 : (int)d;

    private static DateTime ToUtc(DateTime localUnspecified, TimeZoneInfo tz)
    {
        var unspec = DateTime.SpecifyKind(localUnspecified, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspec, tz);
    }
}
