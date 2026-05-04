using AgentFlow.Application.Modules.Campaigns.LaunchV2;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Infrastructure.Campaigns.V2;
using AgentFlow.Infrastructure.Persistence;
using AgentFlow.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CampaignIntakeTests;

public class LaunchCampaignV2HandlerTests
{
    private static (AgentFlowDbContext db, LaunchCampaignV2Handler handler) BuildSut()
    {
        var opts = new DbContextOptionsBuilder<AgentFlowDbContext>()
            .UseInMemoryDatabase($"campaigns-v2-{Guid.NewGuid():N}")
            .Options;
        var db = new AgentFlowDbContext(opts);

        var repo = new CampaignRepository(db);
        var dup = new DuplicateChecker(db);
        var handler = new LaunchCampaignV2Handler(repo, dup, NullLogger<LaunchCampaignV2Handler>.Instance);
        return (db, handler);
    }

    private static Tenant SeedTenant(AgentFlowDbContext db, Guid id)
    {
        var t = new Tenant
        {
            Id = id,
            Name = "Tenant Test",
            Slug = $"t-{id:N}"[..8],
            TimeZone = "UTC",                   // simplifica el cálculo de ScheduledFor
            BusinessHoursStart = new TimeOnly(8, 0),
            BusinessHoursEnd = new TimeOnly(17, 0),
        };
        db.Tenants.Add(t);
        return t;
    }

    private static Campaign SeedCampaign(AgentFlowDbContext db, Guid tenantId, IEnumerable<(string Phone, bool Valid)> contacts)
    {
        var c = new Campaign
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "C1",
            AgentDefinitionId = Guid.NewGuid(),
            Channel = ChannelType.WhatsApp,
            Trigger = CampaignTrigger.FileUpload,
            CreatedByUserId = "tester",
            Status = CampaignStatus.Pending,
        };
        foreach (var (phone, valid) in contacts)
        {
            c.Contacts.Add(new CampaignContact
            {
                Id = Guid.NewGuid(),
                CampaignId = c.Id,
                PhoneNumber = phone,
                IsPhoneValid = valid,
                DispatchStatus = DispatchStatus.Pending,
                CreatedAt = DateTime.UtcNow,
            });
        }
        c.TotalContacts = c.Contacts.Count;
        db.Campaigns.Add(c);
        return c;
    }

    [Fact]
    public async Task Handle_TodosValidos_QueueaTodos()
    {
        var (db, handler) = BuildSut();
        var tenant = SeedTenant(db, Guid.NewGuid());
        var campaign = SeedCampaign(db, tenant.Id, [
            ("+50760001111", true),
            ("+50760002222", true),
            ("+50760003333", true),
        ]);
        await db.SaveChangesAsync();

        var res = await handler.Handle(new(campaign.Id, tenant.Id, "u1", null, WarmupDay: 0), default);

        Assert.True(res.Success);
        Assert.Equal(3, res.QueuedCount);
        Assert.Equal(0, res.DeferredCount);
        Assert.Equal(0, res.DuplicateCount);
        Assert.Equal(0, res.SkippedCount);
        Assert.Equal("Running", res.Status);

        var fresh = await db.Campaigns.Include(c => c.Contacts).SingleAsync(c => c.Id == campaign.Id);
        Assert.Equal(CampaignStatus.Running, fresh.Status);
        Assert.NotNull(fresh.LaunchedAt);
        Assert.All(fresh.Contacts, cc => Assert.Equal(DispatchStatus.Queued, cc.DispatchStatus));
    }

    [Fact]
    public async Task Handle_InvalidosSeMarcanComoSkipped()
    {
        var (db, handler) = BuildSut();
        var tenant = SeedTenant(db, Guid.NewGuid());
        var campaign = SeedCampaign(db, tenant.Id, [
            ("+50760001111", true),
            ("+50700000000", false),     // inválido
            ("+50760003333", true),
        ]);
        await db.SaveChangesAsync();

        var res = await handler.Handle(new(campaign.Id, tenant.Id, "u1", null, WarmupDay: 0), default);

        Assert.True(res.Success);
        Assert.Equal(2, res.QueuedCount);
        Assert.Equal(1, res.SkippedCount);

        var fresh = await db.Campaigns.Include(c => c.Contacts).SingleAsync(c => c.Id == campaign.Id);
        Assert.Equal(DispatchStatus.Skipped,
            fresh.Contacts.Single(c => !c.IsPhoneValid).DispatchStatus);
    }

    [Fact]
    public async Task Handle_DuplicadosActivosSeMarcanComoDuplicate()
    {
        var (db, handler) = BuildSut();
        var tenantId = Guid.NewGuid();
        SeedTenant(db, tenantId);

        // Campaña previa con un contacto activo
        var prev = SeedCampaign(db, tenantId, [("+50760009999", true)]);
        prev.Status = CampaignStatus.Running;
        prev.Contacts.First().DispatchStatus = DispatchStatus.Queued;

        // Campaña nueva con el mismo teléfono + uno nuevo
        var current = SeedCampaign(db, tenantId, [
            ("+50760009999", true),     // duplicado
            ("+50760001111", true),
        ]);
        await db.SaveChangesAsync();

        var res = await handler.Handle(new(current.Id, tenantId, "u1", null, WarmupDay: 0), default);

        Assert.True(res.Success);
        Assert.Equal(1, res.DuplicateCount);
        Assert.Equal(1, res.QueuedCount);

        var fresh = await db.Campaigns.Include(c => c.Contacts).SingleAsync(c => c.Id == current.Id);
        Assert.Equal(DispatchStatus.Duplicate,
            fresh.Contacts.Single(c => c.PhoneNumber == "+50760009999").DispatchStatus);
        Assert.Equal(DispatchStatus.Queued,
            fresh.Contacts.Single(c => c.PhoneNumber == "+50760001111").DispatchStatus);
    }

    [Fact]
    public async Task Handle_WarmupDay1_LimitaA20YDifierreElResto()
    {
        var (db, handler) = BuildSut();
        var tenant = SeedTenant(db, Guid.NewGuid());
        // 25 contactos válidos. WarmupDay=1 → límite 20 Queued, 5 Deferred.
        var contactos = Enumerable.Range(0, 25)
            .Select(i => ($"+5076000{i:D4}", true));
        var campaign = SeedCampaign(db, tenant.Id, contactos);
        await db.SaveChangesAsync();

        var res = await handler.Handle(new(campaign.Id, tenant.Id, "u1", null, WarmupDay: 1), default);

        Assert.True(res.Success);
        Assert.Equal(20, res.QueuedCount);
        Assert.Equal(5, res.DeferredCount);
        Assert.Equal(20, res.DailyLimit);

        var fresh = await db.Campaigns.Include(c => c.Contacts).SingleAsync(c => c.Id == campaign.Id);
        var deferred = fresh.Contacts.Where(c => c.DispatchStatus == DispatchStatus.Deferred).ToList();
        Assert.Equal(5, deferred.Count);
        Assert.All(deferred, c => Assert.NotNull(c.ScheduledFor));
    }

    [Fact]
    public async Task Handle_CampañaNoExiste_DevuelveFail()
    {
        var (_, handler) = BuildSut();
        var res = await handler.Handle(new(Guid.NewGuid(), Guid.NewGuid(), "u1", null, 0), default);
        Assert.False(res.Success);
        Assert.Equal("NotFound", res.Status);
    }

    [Fact]
    public async Task Handle_CampañaYaRunning_DevuelveFail()
    {
        var (db, handler) = BuildSut();
        var tenant = SeedTenant(db, Guid.NewGuid());
        var campaign = SeedCampaign(db, tenant.Id, [("+50760001111", true)]);
        campaign.Status = CampaignStatus.Running;
        await db.SaveChangesAsync();

        var res = await handler.Handle(new(campaign.Id, tenant.Id, "u1", null, 0), default);

        Assert.False(res.Success);
        Assert.Contains("ejecuci", res.Error);
    }

    [Fact]
    public async Task Handle_SinContactosValidos_DevuelveFail()
    {
        var (db, handler) = BuildSut();
        var tenant = SeedTenant(db, Guid.NewGuid());
        var campaign = SeedCampaign(db, tenant.Id, [
            ("+50700000000", false),
            ("+50711111111", false),
        ]);
        await db.SaveChangesAsync();

        var res = await handler.Handle(new(campaign.Id, tenant.Id, "u1", null, 0), default);

        Assert.False(res.Success);
        Assert.NotNull(res.Error);
        // Inválidos siguen quedando como Skipped aunque la campaña no se lance.
        var fresh = await db.Campaigns.Include(c => c.Contacts).SingleAsync(c => c.Id == campaign.Id);
        Assert.All(fresh.Contacts, c => Assert.Equal(DispatchStatus.Skipped, c.DispatchStatus));
    }
}
