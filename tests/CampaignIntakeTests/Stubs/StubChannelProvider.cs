using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;

namespace CampaignIntakeTests.Stubs;

/// <summary>
/// IChannelProvider de prueba: registra los envíos en una lista y permite simular fallos.
/// </summary>
public class StubChannelProvider : IChannelProvider
{
    public ChannelType ChannelType => ChannelType.WhatsApp;
    public ProviderType ProviderType => ProviderType.UltraMsg;

    public List<SendMessageRequest> Sent { get; } = new();
    public Func<SendMessageRequest, SendResult>? Behaviour { get; set; }

    public Task<SendResult> SendMessageAsync(SendMessageRequest request, CancellationToken ct = default)
    {
        Sent.Add(request);
        var result = Behaviour?.Invoke(request)
            ?? new SendResult(true, $"ext-{Guid.NewGuid():N}");
        return Task.FromResult(result);
    }

    public Task<MessageStatusResult> GetMessageStatusAsync(string externalMessageId, CancellationToken ct = default)
        => Task.FromResult(new MessageStatusResult(externalMessageId, "delivered"));

    public bool ValidateWebhookSignature(string payload, string signature) => true;
}

public class StubChannelProviderFactory(IChannelProvider provider) : IChannelProviderFactory
{
    public Task<IChannelProvider?> GetProviderAsync(Guid tenantId, CancellationToken ct = default)
        => Task.FromResult<IChannelProvider?>(provider);

    public Task<IChannelProvider?> GetProviderByLineAsync(Guid lineId, CancellationToken ct = default)
        => Task.FromResult<IChannelProvider?>(provider);
}
