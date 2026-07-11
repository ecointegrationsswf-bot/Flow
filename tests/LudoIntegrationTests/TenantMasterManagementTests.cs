using System.Linq.Expressions;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Provisioning;
using AgentFlow.Infrastructure.Persistence;
using AgentFlow.Infrastructure.Provisioning;
using AgentFlow.Infrastructure.Storage;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LudoIntegrationTests;

/// <summary>
/// API de gestión de maestros para partners (Ludo) — tests del servicio genérico:
/// prompt literal con gate vetado, regeneración con versionado seguro, horarios,
/// activación con promoción a primario, y documentos (blob stub).
/// </summary>
public class TenantMasterManagementTests
{
    // ── Stubs ────────────────────────────────────────────────────────────────────
    private sealed class StubBlob : IBlobStorageService
    {
        public List<string> Uploaded { get; } = [];
        public List<string> Deleted { get; } = [];
        public Task<string> UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default)
        { Uploaded.Add(path); return Task.FromResult($"https://test.blob.core.windows.net/docs/{path}"); }
        public Task<(Stream Content, string ContentType)> DownloadAsync(string path, CancellationToken ct = default)
            => Task.FromResult(((Stream)new MemoryStream(), "application/pdf"));
        public Task DeleteAsync(string path, CancellationToken ct = default)
        { Deleted.Add(path); return Task.CompletedTask; }
        public Task<string> UploadWhatsAppMediaAsync(string fileName, byte[] content, string contentType, CancellationToken ct = default)
            => Task.FromResult($"https://test.blob.core.windows.net/wa/{fileName}");
        public Task<string> UploadToContainerAsync(string container, string path, byte[] content, string contentType, CancellationToken ct = default)
            => Task.FromResult($"https://test.blob.core.windows.net/{container}/{path}");
        public Task<string> UploadAndGetSasUrlAsync(string container, string path, byte[] content, string contentType, TimeSpan validity, CancellationToken ct = default)
            => Task.FromResult($"https://test.blob.core.windows.net/{container}/{path}?sas=1");
    }

    private sealed class StubJobs : IBackgroundJobClient
    {
        public int Enqueued;
        public string Create(Job job, IState state) { Enqueued++; return Guid.NewGuid().ToString("N"); }
        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    private static AgentFlowDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AgentFlowDbContext>()
            .UseInMemoryDatabase($"mgmt-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static TenantMasterManagementService NewSvc(AgentFlowDbContext db, StubBlob? blob = null, StubJobs? jobs = null) =>
        new(db,
            new CampaignTemplateGenerator(new ConfigurationBuilder().Build(), NullLogger<CampaignTemplateGenerator>.Instance),
            blob ?? new StubBlob(), jobs ?? new StubJobs(),
            NullLogger<TenantMasterManagementService>.Instance);

    /// <summary>Tenant "seguro" con agente ventas y un maestro (borrador por default).</summary>
    private static (Guid tenantId, Guid agentId, CampaignTemplate master) Seed(
        AgentFlowDbContext db, bool masterActivo = false, bool masterPrimario = false)
    {
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tenantId, Name = "T", Slug = $"t{Guid.NewGuid():N}"[..8], TimeZone = "America/Panama" });
        db.LudoTenantMaps.Add(new LudoTenantMap { Id = Guid.NewGuid(), LudoTenantId = $"lt-{Guid.NewGuid():N}"[..10], TenantId = tenantId, TipoNegocio = "seguro", CreatedAt = DateTime.UtcNow });
        var agent = new AgentDefinition { Id = Guid.NewGuid(), TenantId = tenantId, Name = "ventas", IsActive = true, SystemPrompt = "x", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.AgentDefinitions.Add(agent);
        var master = new CampaignTemplate
        {
            Id = Guid.NewGuid(), TenantId = tenantId, AgentDefinitionId = agent.Id,
            Name = "T — ventas", SystemPrompt = "prompt original", Objetivo = "vender",
            IsActive = masterActivo, IsPrimaryForAgent = masterPrimario,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.CampaignTemplates.Add(master);
        db.SaveChanges();
        return (tenantId, agent.Id, master);
    }

    [Fact]
    public async Task UpdateMaster_LiteralPrompt_SeguroGetsVettedGate()
    {
        await using var db = NewDb();
        var (tid, _, master) = Seed(db);
        var svc = NewSvc(db);

        var r = await svc.UpdateMasterAsync(tid, new UpdateMasterRequest("ventas", SystemPrompt: "Nuevo guion del corredor"), CancellationToken.None);

        Assert.True(r.Ok);
        var updated = await db.CampaignTemplates.FindAsync(master.Id);
        Assert.StartsWith(CampaignTemplateGenerator.VettedSeguroAuthGate, updated!.SystemPrompt);
        Assert.Contains("Nuevo guion del corredor", updated.SystemPrompt);
    }

    [Fact]
    public async Task UpdateMaster_ObjetivoOnActiveMaster_CreatesDraft_DoesNotTouchLive()
    {
        await using var db = NewDb();
        var (tid, agentId, master) = Seed(db, masterActivo: true, masterPrimario: true);
        var originalPrompt = master.SystemPrompt;
        var svc = NewSvc(db);

        var r = await svc.UpdateMasterAsync(tid, new UpdateMasterRequest("ventas", Objetivo: "Renovaciones de pólizas"), CancellationToken.None);

        Assert.True(r.Ok);
        Assert.Contains("BORRADOR", r.Message);
        var live = await db.CampaignTemplates.FindAsync(master.Id);
        Assert.Equal(originalPrompt, live!.SystemPrompt); // el activo NO se tocó
        var draft = await db.CampaignTemplates.SingleAsync(t => t.TenantId == tid && t.AgentDefinitionId == agentId && t.Id != master.Id);
        Assert.False(draft.IsActive);
        Assert.Contains("GATE DE AUTENTICACIÓN", draft.SystemPrompt); // vertical seguro
    }

    [Fact]
    public async Task UpdateMaster_Activar_PromotesToPrimaryWhenNoOther()
    {
        await using var db = NewDb();
        var (tid, _, master) = Seed(db, masterActivo: false);
        var svc = NewSvc(db);

        var r = await svc.UpdateMasterAsync(tid, new UpdateMasterRequest("ventas", Activar: true), CancellationToken.None);

        Assert.True(r.Ok);
        Assert.True(r.IsActive);
        Assert.True(r.IsPrimary);
    }

    [Fact]
    public async Task UpdateMaster_Horarios_ValidatesFormat()
    {
        await using var db = NewDb();
        var (tid, _, master) = Seed(db);
        var svc = NewSvc(db);

        var bad = await svc.UpdateMasterAsync(tid, new UpdateMasterRequest("ventas", SendFrom: "25:99"), CancellationToken.None);
        Assert.False(bad.Ok);

        var ok = await svc.UpdateMasterAsync(tid, new UpdateMasterRequest("ventas", SendFrom: "08:00", SendUntil: "18:00"), CancellationToken.None);
        Assert.True(ok.Ok);
        var updated = await db.CampaignTemplates.FindAsync(master.Id);
        Assert.Equal("08:00", updated!.SendFrom);
        Assert.Equal("18:00", updated.SendUntil);
    }

    [Fact]
    public async Task UpdateMaster_UnknownAgent_Fails()
    {
        await using var db = NewDb();
        var (tid, _, _) = Seed(db);
        var svc = NewSvc(db);
        var r = await svc.UpdateMasterAsync(tid, new UpdateMasterRequest("noexiste", SystemPrompt: "x"), CancellationToken.None);
        Assert.False(r.Ok);
    }

    [Fact]
    public async Task AddDocument_ValidPdf_UploadsAndQueuesIndexing()
    {
        await using var db = NewDb();
        var (tid, _, master) = Seed(db);
        var blob = new StubBlob(); var jobs = new StubJobs();
        var svc = NewSvc(db, blob, jobs);

        var pdf = Convert.ToBase64String("%PDF-1.7 contenido de prueba"u8.ToArray());
        var r = await svc.AddDocumentAsync(tid, new UploadDocumentRequest("ventas", "condiciones.pdf", pdf, "Condiciones 2026"), CancellationToken.None);

        Assert.True(r.Ok);
        Assert.NotNull(r.DocumentId);
        Assert.Single(blob.Uploaded);
        Assert.Equal(1, jobs.Enqueued); // indexado RAG encolado
        Assert.Equal(1, await db.CampaignTemplateDocuments.CountAsync(d => d.CampaignTemplateId == master.Id));
    }

    [Fact]
    public async Task AddDocument_NonPdf_Rejected()
    {
        await using var db = NewDb();
        var (tid, _, _) = Seed(db);
        var svc = NewSvc(db);
        var notPdf = Convert.ToBase64String("hola esto no es un pdf"u8.ToArray());
        var r = await svc.AddDocumentAsync(tid, new UploadDocumentRequest("ventas", "x.pdf", notPdf), CancellationToken.None);
        Assert.False(r.Ok);
    }

    [Fact]
    public async Task RemoveDocument_DeletesRowAndBlob()
    {
        await using var db = NewDb();
        var (tid, _, master) = Seed(db);
        var blob = new StubBlob();
        var svc = NewSvc(db, blob);
        var pdf = Convert.ToBase64String("%PDF-1.7 x"u8.ToArray());
        var add = await svc.AddDocumentAsync(tid, new UploadDocumentRequest("ventas", "a.pdf", pdf), CancellationToken.None);

        var r = await svc.RemoveDocumentAsync(tid, "ventas", add.DocumentId!.Value, CancellationToken.None);

        Assert.True(r.Ok);
        Assert.Single(blob.Deleted);
        Assert.Equal(0, await db.CampaignTemplateDocuments.CountAsync());
    }

    [Fact]
    public async Task UpdateTenantHours_SetsBusinessHours()
    {
        await using var db = NewDb();
        var (tid, _, _) = Seed(db);
        var svc = NewSvc(db);

        var r = await svc.UpdateTenantHoursAsync(tid, new UpdateTenantHoursRequest("07:30", "19:00"), CancellationToken.None);

        Assert.True(r.Ok);
        var tenant = await db.Tenants.FindAsync(tid);
        Assert.Equal(new TimeOnly(7, 30), tenant!.BusinessHoursStart);
        Assert.Equal(new TimeOnly(19, 0), tenant.BusinessHoursEnd);
    }

    [Fact]
    public async Task ListDocuments_ReturnsIdsForDeletion()
    {
        await using var db = NewDb();
        var (tid, _, _) = Seed(db);
        var svc = NewSvc(db);
        var pdf = Convert.ToBase64String("%PDF-1.7 x"u8.ToArray());
        var add = await svc.AddDocumentAsync(tid, new UploadDocumentRequest("ventas", "tarifario.pdf", pdf, "Tarifario 2026"), CancellationToken.None);

        var docs = await svc.ListDocumentsAsync(tid, "ventas", CancellationToken.None);

        Assert.NotNull(docs);
        var d = Assert.Single(docs!);
        Assert.Equal(add.DocumentId, d.DocumentId);
        Assert.Equal("tarifario.pdf", d.FileName);
        Assert.Equal("Tarifario 2026", d.Description);

        Assert.Null(await svc.ListDocumentsAsync(tid, "noexiste", CancellationToken.None));
    }

    [Fact]
    public async Task GetMasters_ReturnsState()
    {
        await using var db = NewDb();
        var (tid, _, master) = Seed(db, masterActivo: true, masterPrimario: true);
        var svc = NewSvc(db);

        var list = await svc.GetMastersAsync(tid, CancellationToken.None);

        var m = Assert.Single(list);
        Assert.Equal("ventas", m.AgentSlug);
        Assert.Equal(master.Id, m.TemplateId);
        Assert.True(m.IsActive);
        Assert.True(m.IsPrimaryForAgent);
    }
}
