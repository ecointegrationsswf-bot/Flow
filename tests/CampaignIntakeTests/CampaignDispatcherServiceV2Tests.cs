using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Infrastructure.Campaigns;
using AgentFlow.Infrastructure.Persistence;
using CampaignIntakeTests.Stubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CampaignIntakeTests;

/// <summary>
/// Tests smoke del CampaignDispatcherService refactorizado en Fase 3.
///
/// IMPORTANTE: la pieza central del dispatcher es el claim atómico Queued→Claimed
/// vía <c>ExecuteUpdateAsync</c>, que el provider EF.InMemory no implementa. Por eso
/// los tests acá cubren solo las ramas que NO ejecutan el ExecuteUpdate (off-switch
/// del tenant, dispatch enabled=false, sin candidatos elegibles → Completed).
///
/// La validación end-to-end del claim atómico se hace en producción contra SQL
/// Server real (es el provider que sí soporta ExecuteUpdate).
/// </summary>
public class CampaignDispatcherServiceV2Tests
{
    private static (AgentFlowDbContext db, CampaignDispatcherService dispatcher,
                    StubChannelProvider provider, StubWebhookEventDispatcher events)
        BuildSut()
    {
        var opts = new DbContextOptionsBuilder<AgentFlowDbContext>()
            .UseInMemoryDatabase($"dispatcher-{Guid.NewGuid():N}")
            .Options;
        var db = new AgentFlowDbContext(opts);
        var provider = new StubChannelProvider();
        var factory = new StubChannelProviderFactory(provider);
        var events = new StubWebhookEventDispatcher();
        var jobs = new StubScheduledJobRepository();
        var dispatcher = new CampaignDispatcherService(
            db, factory, events, jobs,
            NullLogger<CampaignDispatcherService>.Instance);
        return (db, dispatcher, provider, events);
    }

    private static Tenant SeedTenant(AgentFlowDbContext db, bool dispatchEnabled = true)
    {
        var t = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "T",
            Slug = $"t-{Guid.NewGuid():N}"[..8],
            TimeZone = "UTC",
            BusinessHoursStart = new TimeOnly(0, 0),
            BusinessHoursEnd = new TimeOnly(23, 59),
            CampaignDispatchEnabled = dispatchEnabled,
        };
        db.Tenants.Add(t);
        return t;
    }

    [Fact]
    public async Task Dispatch_DispatchEnabledFalse_NoEnviaYDevuelveCampaignInactive()
    {
        var (db, dispatcher, provider, _) = BuildSut();
        var tenant = SeedTenant(db, dispatchEnabled: false);
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "C",
            AgentDefinitionId = Guid.NewGuid(),
            Channel = ChannelType.WhatsApp,
            Trigger = CampaignTrigger.FileUpload,
            Status = CampaignStatus.Running,
            IsActive = true,
            CreatedByUserId = "tester",
        };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();

        var result = await dispatcher.DispatchBatchAsync(campaign.Id);

        Assert.Equal(0, result.Sent);
        Assert.Equal(DispatchStopReason.CampaignInactive, result.StopReason);
        Assert.Empty(provider.Sent);
    }

    [Fact]
    public async Task Dispatch_SinContactosQueued_MarcaCompleted()
    {
        // No invoca ExecuteUpdate (candidateIds=0 → rama Completed).
        var (db, dispatcher, _, events) = BuildSut();
        var tenant = SeedTenant(db);
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "C",
            AgentDefinitionId = Guid.NewGuid(),
            Channel = ChannelType.WhatsApp,
            Trigger = CampaignTrigger.FileUpload,
            Status = CampaignStatus.Running,
            IsActive = true,
            CreatedByUserId = "tester",
        };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();

        var result = await dispatcher.DispatchBatchAsync(campaign.Id);

        Assert.Equal(DispatchStopReason.AllContactsProcessed, result.StopReason);
        var fresh = await db.Campaigns.FirstAsync(c => c.Id == campaign.Id);
        Assert.Equal(CampaignStatus.Completed, fresh.Status);
        Assert.False(fresh.IsActive);
        Assert.NotNull(fresh.CompletedAt);
        Assert.Contains(events.Events, e => e.Event == "CampaignFinished");
    }

    [Fact]
    public async Task Dispatch_TodosDeferredFuturo_DevuelveBatchCompletedSinCerrar()
    {
        // Hay contactos pero todos en Deferred con ScheduledFor en el futuro:
        // candidateIds=0, hasMore=true → batch vacío sin cerrar la campaña.
        var (db, dispatcher, _, _) = BuildSut();
        var tenant = SeedTenant(db);
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "C",
            AgentDefinitionId = Guid.NewGuid(),
            Channel = ChannelType.WhatsApp,
            Trigger = CampaignTrigger.FileUpload,
            Status = CampaignStatus.Running,
            IsActive = true,
            CreatedByUserId = "tester",
        };
        db.Campaigns.Add(campaign);
        for (int i = 0; i < 3; i++)
        {
            db.CampaignContacts.Add(new CampaignContact
            {
                Id = Guid.NewGuid(),
                CampaignId = campaign.Id,
                PhoneNumber = $"+50760000{i:D4}",
                IsPhoneValid = true,
                DispatchStatus = DispatchStatus.Deferred,
                ScheduledFor = DateTime.UtcNow.AddHours(2),
                CreatedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();

        var result = await dispatcher.DispatchBatchAsync(campaign.Id);

        Assert.Equal(DispatchStopReason.BatchCompleted, result.StopReason);
        var fresh = await db.Campaigns.FirstAsync(c => c.Id == campaign.Id);
        Assert.Equal(CampaignStatus.Running, fresh.Status);   // sigue Running
        Assert.True(fresh.IsActive);
    }
}
