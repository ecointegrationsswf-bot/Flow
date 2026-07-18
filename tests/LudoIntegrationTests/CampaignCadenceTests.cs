using AgentFlow.Domain.Entities;
using Xunit;

namespace LudoIntegrationTests;

/// <summary>
/// Cadencia efectiva del envío masivo (Tenant.GetCampaignBaseDelaySeconds):
/// CampaignSecondsBetweenMessages (si &gt;0) manda sobre CampaignMessagesPerMinute
/// y permite espaciamientos mayores a 1 minuto (ej. 180s = 1 msg cada 3 min).
/// </summary>
public class CampaignCadenceTests
{
    private const int Floor = 3;

    [Fact]
    public void SinSegundosFijos_UsaMsgPorMinuto()
    {
        var t = new Tenant { CampaignMessagesPerMinute = 6, CampaignSecondsBetweenMessages = null };
        Assert.Equal(10.0, t.GetCampaignBaseDelaySeconds(Floor));
    }

    [Fact]
    public void SegundosFijos_MandanSobreMsgPorMinuto()
    {
        var t = new Tenant { CampaignMessagesPerMinute = 6, CampaignSecondsBetweenMessages = 180 };
        Assert.Equal(180.0, t.GetCampaignBaseDelaySeconds(Floor)); // 1 mensaje cada 3 minutos
    }

    [Fact]
    public void SegundosFijos_RespetanPisoTecnico()
    {
        var t = new Tenant { CampaignMessagesPerMinute = 6, CampaignSecondsBetweenMessages = 1 };
        Assert.Equal(Floor, t.GetCampaignBaseDelaySeconds(Floor));
    }

    [Fact]
    public void SegundosFijosCero_EquivaleApagado()
    {
        var t = new Tenant { CampaignMessagesPerMinute = 4, CampaignSecondsBetweenMessages = 0 };
        Assert.Equal(15.0, t.GetCampaignBaseDelaySeconds(Floor)); // 60/4
    }

    [Fact]
    public void MsgPorMinutoInvalido_SeNormalizaAUno()
    {
        var t = new Tenant { CampaignMessagesPerMinute = 0, CampaignSecondsBetweenMessages = null };
        Assert.Equal(60.0, t.GetCampaignBaseDelaySeconds(Floor));
    }
}
