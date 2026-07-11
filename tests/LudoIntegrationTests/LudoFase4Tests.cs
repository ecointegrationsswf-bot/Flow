using System.Net;
using System.Text;
using System.Text.Json;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Domain.Provisioning;
using AgentFlow.Domain.Webhooks;
using AgentFlow.Infrastructure.Persistence;
using AgentFlow.Infrastructure.Provisioning;
using AgentFlow.Infrastructure.ScheduledJobs;
using AgentFlow.Infrastructure.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LudoIntegrationTests;

/// <summary>
/// Integración Ludo CRM — Fase 4 (Salida, Dirección B). Tests del seeder, el enricher
/// (etapa→faseId + teléfono→oportunidadId), el outbox drainer y el provisioning con
/// integrationToken. Sin red real: HTTP fake handler + EF InMemory.
/// </summary>
public class LudoFase4Tests
{
    private static AgentFlowDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AgentFlowDbContext>()
            .UseInMemoryDatabase($"ludo4-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    // ── HTTP fake ────────────────────────────────────────────────────────────────────
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) => new(code)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static WebhookEndpointConfig MoverFaseEndpoint(string apiKey = "jam_test") => new()
    {
        WebhookUrl = $"https://ludo.example.com/api/integration/oportunidad/{LudoIntegrationDefaults.OpportunityIdPlaceholder}/fase",
        WebhookMethod = "PUT",
        AuthType = "ApiKey",
        AuthValue = apiKey,
        ApiKeyHeaderName = "X-Api-Key",
        TimeoutSeconds = 10,
    };

    private const string ProspectoConOportunidad = """
        { "success": true, "data": { "prospectoId": 22, "telefono": "+50767003030",
          "oportunidades": [
            { "oportunidadId": 18, "objetivo": "ventas", "faseId": 7, "activa": true },
            { "oportunidadId": 11, "objetivo": "otros",  "faseId": 3, "activa": false }
          ] } }
        """;

    // ── Enricher ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Enricher_MoverFase_MapsEtapaToFaseId_AndResolvesOpportunityId()
    {
        await using var db = NewDb();
        var tenantId = Guid.NewGuid();
        db.StageLabelMaps.Add(new StageLabelMap
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LudoStageId = "8",
            LabelId = Guid.NewGuid(), Nombre = "Calificado", Orden = 2, IsActive = true,
        });
        await db.SaveChangesAsync();

        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, ProspectoConOportunidad));
        var enricher = new LudoActionEnricher(db, new FakeHttpClientFactory(handler), NullLogger<LudoActionEnricher>.Instance);

        var collected = new CollectedParams { Values = new() { ["etapa"] = "Calificado", ["objetivo"] = "ventas" } };
        var result = await enricher.EnrichAsync(tenantId, "mover_fase", "+50767003030", collected, MoverFaseEndpoint(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("8", result.Params.Values["faseId"]);          // mapeado por StageLabelMap
        Assert.Null(result.Params.Values["faseNombre"]);            // exactamente uno
        Assert.Contains("/oportunidad/18/fase", result.Endpoint.WebhookUrl); // placeholder resuelto
        // El GET /prospecto llevó la auth del contrato.
        Assert.Contains(handler.Requests, r => r.Headers.Contains("X-Api-Key"));
    }

    [Fact]
    public async Task Enricher_MoverFase_UnmappedEtapa_FallsBackToFaseNombre()
    {
        await using var db = NewDb();
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, ProspectoConOportunidad));
        var enricher = new LudoActionEnricher(db, new FakeHttpClientFactory(handler), NullLogger<LudoActionEnricher>.Instance);

        var collected = new CollectedParams { Values = new() { ["etapa"] = "En preparación" } };
        var result = await enricher.EnrichAsync(Guid.NewGuid(), "mover_fase", "+50767003030", collected, MoverFaseEndpoint(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.Params.Values["faseId"]);
        Assert.Equal("En preparación", result.Params.Values["faseNombre"]);
    }

    [Fact]
    public async Task Enricher_MoverFase_NoProspecto204_FailsWithFriendlyMessage()
    {
        await using var db = NewDb();
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var enricher = new LudoActionEnricher(db, new FakeHttpClientFactory(handler), NullLogger<LudoActionEnricher>.Instance);

        var collected = new CollectedParams { Values = new() { ["etapa"] = "Calificado" } };
        var result = await enricher.EnrichAsync(Guid.NewGuid(), "mover_fase", "+50767003030", collected, MoverFaseEndpoint(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("oportunidad", result.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Enricher_RegistrarOportunidad_PassesThroughUntouched()
    {
        await using var db = NewDb();
        var handler = new FakeHandler(_ => throw new InvalidOperationException("no debe llamar HTTP"));
        var enricher = new LudoActionEnricher(db, new FakeHttpClientFactory(handler), NullLogger<LudoActionEnricher>.Instance);

        var endpoint = new WebhookEndpointConfig { WebhookUrl = "https://ludo.example.com/api/integration/oportunidad" };
        var collected = new CollectedParams { Values = new() { ["objetivo"] = "ventas" } };
        var result = await enricher.EnrichAsync(Guid.NewGuid(), "registrar_oportunidad", "+507", collected, endpoint, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Empty(handler.Requests);
        Assert.Same(endpoint, result.Endpoint);
    }

    // ── Seeder ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Seeder_IsIdempotent_CreatesActionsAndDrainerCron()
    {
        await using var db = NewDb();
        await LudoActionSeeder.SeedAsync(db, NullLogger<LudoFase4Tests>.Instance);
        await LudoActionSeeder.SeedAsync(db, NullLogger<LudoFase4Tests>.Instance); // 2a corrida: no duplica

        var globals = await db.ActionDefinitions.Where(a => a.TenantId == null).ToListAsync();
        Assert.Equal(4, globals.Count); // 3 salida + drainer
        Assert.All(LudoIntegrationDefaults.AllSlugs,
            slug => Assert.Contains(globals, a => a.Name == slug && a.RequiresWebhook && !string.IsNullOrEmpty(a.DefaultWebhookContract)));
        Assert.Contains(globals, a => a.Name == LudoActionSeeder.DrainerSlug && a.IsProcess);

        Assert.Equal(1, await db.ScheduledWebhookJobs.CountAsync()); // un solo cron del drainer
    }

    [Fact]
    public void ContractJson_MoverFase_HasPlaceholderAndApiKey()
    {
        var json = LudoIntegrationDefaults.BuildContractJson("mover_fase", null, "jam_secret");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Contains(LudoIntegrationDefaults.OpportunityIdPlaceholder, root.GetProperty("webhookUrl").GetString());
        Assert.Equal("PUT", root.GetProperty("webhookMethod").GetString());
        Assert.Equal("ApiKey", root.GetProperty("authType").GetString());
        Assert.Equal("jam_secret", root.GetProperty("authValue").GetString());
        Assert.Equal("X-Api-Key", root.GetProperty("apiKeyHeaderName").GetString());
        Assert.True(root.GetProperty("inputSchema").GetProperty("fields").GetArrayLength() >= 3);
    }

    // ── Drainer ──────────────────────────────────────────────────────────────────────

    private sealed class StubExecutor(Func<string, ActionResult> respond) : IActionExecutorService
    {
        public List<string> Calls { get; } = [];
        public Task<ActionResult> ExecuteAsync(string actionSlug, Guid tenantId, Guid? campaignTemplateId,
            string contactPhone, Guid? conversationId, CollectedParams collectedParams, string? agentSlug = null,
            Guid? jobExecutionId = null, Guid? jobId = null,
            IReadOnlyDictionary<string, string?>? systemContextOverrides = null, CancellationToken ct = default)
        {
            Calls.Add(actionSlug);
            return Task.FromResult(respond(actionSlug));
        }
    }

    private static LudoOutboxItem PendingItem(Guid tenantId, int attempts = 0) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, PhoneE164 = "+50767003030",
        ActionSlug = "mover_fase", PayloadJson = """{"etapa":"Calificado"}""",
        Status = "Pending", Attempts = attempts, CreatedAt = DateTime.UtcNow.AddMinutes(-10),
    };

    [Fact]
    public async Task Drainer_Success_MarksSent()
    {
        await using var db = NewDb();
        var item = PendingItem(Guid.NewGuid());
        db.LudoOutboxItems.Add(item);
        await db.SaveChangesAsync();

        var stub = new StubExecutor(_ => ActionResult.Ok());
        var drainer = new LudoOutboxDrainerExecutor(db, stub, NullLogger<LudoOutboxDrainerExecutor>.Instance);
        var result = await drainer.ExecuteAsync(new ScheduledWebhookJob(), new ScheduledJobContext("Worker", null, DateTime.UtcNow), CancellationToken.None);

        Assert.Equal("Success", result.Status);
        Assert.Single(stub.Calls);
        var reloaded = await db.LudoOutboxItems.SingleAsync();
        Assert.Equal("Sent", reloaded.Status);
        Assert.Null(reloaded.LastError);
    }

    [Fact]
    public async Task Drainer_Failure_AppliesBackoff_ThenFailedAtMaxAttempts()
    {
        await using var db = NewDb();
        var fresh = PendingItem(Guid.NewGuid());                  // 0 intentos → backoff
        var exhausted = PendingItem(Guid.NewGuid(), attempts: 7); // 8º intento → Failed
        db.LudoOutboxItems.AddRange(fresh, exhausted);
        await db.SaveChangesAsync();

        var stub = new StubExecutor(_ => ActionResult.Fail("Ludo caído"));
        var drainer = new LudoOutboxDrainerExecutor(db, stub, NullLogger<LudoOutboxDrainerExecutor>.Instance);
        var result = await drainer.ExecuteAsync(new ScheduledWebhookJob(), new ScheduledJobContext("Worker", null, DateTime.UtcNow), CancellationToken.None);

        Assert.Equal("PartialFailure", result.Status);

        var freshR = await db.LudoOutboxItems.SingleAsync(o => o.Id == fresh.Id);
        Assert.Equal("Pending", freshR.Status);
        Assert.Equal(1, freshR.Attempts);
        Assert.NotNull(freshR.NextAttemptAt); // backoff programado

        var exhaustedR = await db.LudoOutboxItems.SingleAsync(o => o.Id == exhausted.Id);
        Assert.Equal("Failed", exhaustedR.Status);
        Assert.Equal(8, exhaustedR.Attempts);
    }

    [Fact]
    public async Task Drainer_NothingDue_Skips()
    {
        await using var db = NewDb();
        var future = PendingItem(Guid.NewGuid());
        future.NextAttemptAt = DateTime.UtcNow.AddHours(1); // aún no toca
        db.LudoOutboxItems.Add(future);
        await db.SaveChangesAsync();

        var stub = new StubExecutor(_ => ActionResult.Ok());
        var drainer = new LudoOutboxDrainerExecutor(db, stub, NullLogger<LudoOutboxDrainerExecutor>.Instance);
        var result = await drainer.ExecuteAsync(new ScheduledWebhookJob(), new ScheduledJobContext("Worker", null, DateTime.UtcNow), CancellationToken.None);

        Assert.Equal("Skipped", result.Status);
        Assert.Empty(stub.Calls);
    }

    // ── Provisioning con integrationToken ────────────────────────────────────────────

    [Fact]
    public async Task Provision_WithIntegrationToken_WiresOutboundContracts()
    {
        await using var db = NewDb();
        await LudoActionSeeder.SeedAsync(db, NullLogger<LudoFase4Tests>.Instance); // acciones globales

        var svc = new TenantProvisioningService(
            db,
            new CampaignTemplateGenerator(
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                NullLogger<CampaignTemplateGenerator>.Instance),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            NullLogger<TenantProvisioningService>.Instance);

        var req = new ProvisionTenantRequest(
            LudoTenantId: "ludo-f4-1", TipoNegocio: "seguro", NombreNegocio: "Corretaje F4",
            WhatsappInstanceId: null,
            Agentes: [new AgentSeed("ventas", "Vender pólizas", Welcome: true)],
            Etapas: [new StageSeed("6", "Nuevo", 1), new StageSeed("7", "Calificado", 2)],
            IntegrationToken: "jam_tenant_token",
            LudoApiBaseUrl: "https://ludo.example.com");

        var result = await svc.ProvisionAsync(req, CancellationToken.None);

        var tenant = await db.Tenants.FindAsync(result.TenantId);
        Assert.True(tenant!.WebhookContractEnabled);          // el executor puede correr
        Assert.Equal(3, tenant.AssignedActionIds.Count);

        var contracts = await db.TenantActionContracts.Where(c => c.TenantId == result.TenantId).ToListAsync();
        Assert.Equal(3, contracts.Count);
        Assert.All(contracts, c => Assert.Contains("jam_tenant_token", c.ContractJson));
        Assert.All(contracts, c => Assert.Contains("https://ludo.example.com", c.ContractJson));

        // Acciones vinculadas al maestro welcome (keys de ActionConfigs = ids Ludo).
        var master = await db.CampaignTemplates.SingleAsync(t => t.TenantId == result.TenantId);
        foreach (var id in tenant.AssignedActionIds)
            Assert.Contains(id.ToString(), master.ActionConfigs!);
    }

    [Fact]
    public async Task Provision_WithoutToken_NoOutboundWiring()
    {
        await using var db = NewDb();
        await LudoActionSeeder.SeedAsync(db, NullLogger<LudoFase4Tests>.Instance);

        var svc = new TenantProvisioningService(
            db,
            new CampaignTemplateGenerator(
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                NullLogger<CampaignTemplateGenerator>.Instance),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            NullLogger<TenantProvisioningService>.Instance);

        var req = new ProvisionTenantRequest(
            LudoTenantId: "ludo-f4-2", TipoNegocio: "seguro", NombreNegocio: "Corretaje sin token",
            WhatsappInstanceId: null,
            Agentes: [new AgentSeed("ventas", "Vender", Welcome: true)],
            Etapas: []);

        var result = await svc.ProvisionAsync(req, CancellationToken.None);

        var tenant = await db.Tenants.FindAsync(result.TenantId);
        Assert.False(tenant!.WebhookContractEnabled);
        Assert.Equal(0, await db.TenantActionContracts.CountAsync(c => c.TenantId == result.TenantId));
    }
}
