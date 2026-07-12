using AgentFlow.Domain.Entities;
using AgentFlow.Infrastructure.Persistence;
using AgentFlow.Infrastructure.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LudoIntegrationTests;

/// <summary>
/// Escalamiento robusto — Fase D. Tests del cap de reintentos: el resolver lee
/// MaxCallsPerConversation/CallsExhaustedMessage del contrato (per-tenant → global).
/// Sin contrato o sin el campo → sin límite (comportamiento idéntico al actual).
/// </summary>
public class CallCapTests
{
    private static AgentFlowDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AgentFlowDbContext>()
            .UseInMemoryDatabase($"cap-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static ActionChainResolver NewResolver(AgentFlowDbContext db) =>
        new(db, NullLogger<ActionChainResolver>.Instance);

    [Fact]
    public async Task GetCallCap_ReadsMaxAndMessage_FromGlobalContract()
    {
        await using var db = NewDb();
        db.ActionDefinitions.Add(new ActionDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            Name = "INSURED_INITIATE",
            IsActive = true,
            DefaultWebhookContract =
                "{\"maxCallsPerConversation\":3,\"callsExhaustedMessage\":\"No pude reenviar el código tras varios intentos. Te conecto con un asesor.\"}"
        });
        await db.SaveChangesAsync();

        var cap = await NewResolver(db).GetCallCapAsync(Guid.NewGuid(), "INSURED_INITIATE", CancellationToken.None);

        Assert.Equal(3, cap.MaxCalls);
        Assert.Contains("asesor", cap.ExhaustedMessage);
    }

    [Fact]
    public async Task GetCallCap_NoContract_ReturnsNoLimit()
    {
        await using var db = NewDb();
        var cap = await NewResolver(db).GetCallCapAsync(Guid.NewGuid(), "UNKNOWN", CancellationToken.None);
        Assert.Null(cap.MaxCalls);
    }

    [Fact]
    public async Task GetCallCap_ContractWithoutField_ReturnsNoLimit()
    {
        await using var db = NewDb();
        db.ActionDefinitions.Add(new ActionDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            Name = "NO_CAP",
            IsActive = true,
            DefaultWebhookContract = "{\"webhookUrl\":\"https://example.com/hook\"}"
        });
        await db.SaveChangesAsync();

        var cap = await NewResolver(db).GetCallCapAsync(Guid.NewGuid(), "NO_CAP", CancellationToken.None);
        Assert.Null(cap.MaxCalls);
    }

    [Fact]
    public async Task GetCallCap_PerTenantContract_OverridesGlobal()
    {
        await using var db = NewDb();
        var tenantId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        db.ActionDefinitions.Add(new ActionDefinition
        {
            Id = actionId,
            TenantId = null,
            Name = "INSURED_INITIATE",
            IsActive = true,
            DefaultWebhookContract = "{\"maxCallsPerConversation\":3}"
        });
        db.TenantActionContracts.Add(new TenantActionContract
        {
            Id = Guid.NewGuid(),
            ActionDefinitionId = actionId,
            TenantId = tenantId,
            IsActive = true,
            ContractJson = "{\"maxCallsPerConversation\":5,\"callsExhaustedMessage\":\"tope tenant\"}"
        });
        await db.SaveChangesAsync();

        var cap = await NewResolver(db).GetCallCapAsync(tenantId, "INSURED_INITIATE", CancellationToken.None);

        Assert.Equal(5, cap.MaxCalls);
        Assert.Equal("tope tenant", cap.ExhaustedMessage);
    }
}
