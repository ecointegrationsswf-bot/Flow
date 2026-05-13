using AgentFlow.Domain.Helpers;
using Xunit;

namespace CampaignIntakeTests;

public class DeferredSchedulerTests
{
    [Fact]
    public void ComputeNextRunUtc_TZ_UTC_DevuelveMañana_BusinessHoursStart()
    {
        var nowUtc = new DateTime(2026, 5, 4, 14, 30, 0, DateTimeKind.Utc);
        var bhStart = new TimeOnly(8, 0);

        var result = DeferredScheduler.ComputeNextRunUtc("UTC", bhStart, nowUtc);

        Assert.Equal(new DateTime(2026, 5, 5, 8, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void ComputeNextRunUtc_TZInvalido_CaeAUtc()
    {
        var nowUtc = new DateTime(2026, 5, 4, 14, 30, 0, DateTimeKind.Utc);
        var result = DeferredScheduler.ComputeNextRunUtc("Foo/Bar", new TimeOnly(8, 0), nowUtc);
        Assert.Equal(new DateTime(2026, 5, 5, 8, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void ComputeNextRunUtc_TZNull_CaeAUtc()
    {
        var nowUtc = new DateTime(2026, 5, 4, 14, 30, 0, DateTimeKind.Utc);
        var result = DeferredScheduler.ComputeNextRunUtc(null, new TimeOnly(0, 0), nowUtc);
        Assert.Equal(new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc), result);
    }
}
