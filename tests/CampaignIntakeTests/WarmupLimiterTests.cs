using AgentFlow.Domain.Helpers;
using Xunit;

namespace CampaignIntakeTests;

public class WarmupLimiterTests
{
    [Theory]
    [InlineData(1, 20)]
    [InlineData(2, 50)]
    [InlineData(3, 100)]
    [InlineData(4, 150)]
    [InlineData(5, 200)]
    [InlineData(6, 300)]
    [InlineData(7, 500)]
    public void DailyLimitFor_ReplicaTablaN8n(int dia, int limite)
    {
        Assert.Equal(limite, WarmupLimiter.DailyLimitFor(dia));
    }

    [Theory]
    [InlineData(0)]      // sin warm-up
    [InlineData(8)]      // pasó la fase
    [InlineData(30)]
    [InlineData(-1)]     // valor anómalo
    public void DailyLimitFor_FueraDeRangoDevuelveDefault(int dia)
    {
        Assert.Equal(WarmupLimiter.Default, WarmupLimiter.DailyLimitFor(dia));
    }
}
