using System.Security.Cryptography;
using System.Text;
using AgentFlow.Domain.Provisioning;
using AgentFlow.Infrastructure.Persistence;
using AgentFlow.Infrastructure.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LudoIntegrationTests;

/// <summary>
/// Integración Ludo CRM — Fase 2. Tests del helper de firma (HMAC + anti-replay) y del
/// servicio de provisioning (idempotencia, transaccional, welcome único, homologación).
/// </summary>
public class LudoProvisioningTests
{
    // ── Firma HMAC ────────────────────────────────────────────────────────────────────
    private const string Secret = "ludo-test-secret";

    private static (string sig, string ts) Sign(string body, long ts)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{ts}.{body}"));
        return ("sha256=" + Convert.ToHexString(hash).ToLowerInvariant(), ts.ToString());
    }

    [Fact]
    public void Signature_Valid_Passes()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var body = "{\"hello\":\"world\"}";
        var (sig, ts) = Sign(body, now);

        var (ok, _) = LudoWebhookSignature.Validate(body, sig, ts, Secret, now);
        Assert.True(ok);
    }

    [Fact]
    public void Signature_TamperedBody_Fails()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var (sig, ts) = Sign("{\"hello\":\"world\"}", now);

        var (ok, _) = LudoWebhookSignature.Validate("{\"hello\":\"TAMPERED\"}", sig, ts, Secret, now);
        Assert.False(ok);
    }

    [Fact]
    public void Signature_OldTimestamp_FailsAsReplay()
    {
        var signedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 10_000; // muy viejo
        var body = "{}";
        var (sig, ts) = Sign(body, signedAt);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var (ok, reason) = LudoWebhookSignature.Validate(body, sig, ts, Secret, now);
        Assert.False(ok);
        Assert.Contains("replay", reason);
    }

    [Fact]
    public void Signature_NoSecret_Fails()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var (ok, _) = LudoWebhookSignature.Validate("{}", "sha256=abc", now.ToString(), null, now);
        Assert.False(ok);
    }

    // ── Provisioning ──────────────────────────────────────────────────────────────────
    private static AgentFlowDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AgentFlowDbContext>()
            .UseInMemoryDatabase($"ludo-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>
    /// Generador real con config SIN key Anthropic → ejercita el fallback determinista
    /// (UsedLlm=false), sin llamadas de red. Cubre la garantía de "el generador nunca lanza".
    /// </summary>
    private static CampaignTemplateGenerator NewGenerator() =>
        new(new ConfigurationBuilder().Build(), NullLogger<CampaignTemplateGenerator>.Instance);

    private static TenantProvisioningService NewService(AgentFlowDbContext db, IConfiguration? config = null) =>
        new(db, NewGenerator(), config ?? new ConfigurationBuilder().Build(), NullLogger<TenantProvisioningService>.Instance);

    private static IConfiguration ConfigWith(string key, string value) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();

    private static ProvisionTenantRequest SampleRequest(string ludoTenantId = "ludo-abc-1") => new(
        LudoTenantId: ludoTenantId,
        TipoNegocio: "seguro",
        NombreNegocio: "Corretaje XYZ",
        WhatsappInstanceId: "inst_8842",
        Agentes:
        [
            new AgentSeed("ventas", "Calificar y cerrar pólizas nuevas", Welcome: true),
            new AgentSeed("soporte", "Resolver dudas de pólizas vigentes", Welcome: false),
        ],
        Etapas:
        [
            new StageSeed("st_01", "Nuevo", 1),
            new StageSeed("st_02", "Calificado", 2),
            new StageSeed("st_03", "Cerrado", 3),
        ]);

    [Fact]
    public async Task Provision_CreatesTenantAgentsLabelsAndDraftMasters()
    {
        await using var db = NewDb();
        var svc = NewService(db);

        var result = await svc.ProvisionAsync(SampleRequest(), CancellationToken.None);

        Assert.False(result.AlreadyExisted);
        Assert.Equal(2, result.Masters.Count);

        var tenant = await db.Tenants.FindAsync(result.TenantId);
        Assert.NotNull(tenant);
        Assert.True(tenant!.LudoIntegrationEnabled);
        Assert.Equal(string.Empty, tenant.WhatsAppApiToken); // pendiente de conexión

        Assert.Equal(2, await db.AgentDefinitions.CountAsync(a => a.TenantId == result.TenantId));
        Assert.Equal(3, await db.ConversationLabels.CountAsync(l => l.TenantId == result.TenantId));
        Assert.Equal(3, await db.StageLabelMaps.CountAsync(s => s.TenantId == result.TenantId));

        var masters = await db.CampaignTemplates.Where(t => t.TenantId == result.TenantId).ToListAsync();
        Assert.Equal(2, masters.Count);
        Assert.All(masters, m => Assert.False(m.IsActive));          // BORRADOR
        Assert.All(masters, m => Assert.False(m.IsPrimaryForAgent)); // nunca primario
        Assert.All(masters, m => Assert.False(m.GeneratedByLlm));
        // Completitud operativa del alta externa: etiquetas de etapa asociadas + ventana de envío default.
        Assert.All(masters, m => Assert.Equal(3, m.LabelIds.Count));
        Assert.All(masters, m => Assert.Equal("08:00", m.SendFrom));
        Assert.All(masters, m => Assert.Equal("18:00", m.SendUntil));

        Assert.Equal(1, await db.LudoTenantMaps.CountAsync());
    }

    [Fact]
    public async Task Provision_IsIdempotent_NoDuplicates()
    {
        await using var db = NewDb();
        var svc = NewService(db);

        var first = await svc.ProvisionAsync(SampleRequest(), CancellationToken.None);
        var second = await svc.ProvisionAsync(SampleRequest(), CancellationToken.None);

        Assert.False(first.AlreadyExisted);
        Assert.True(second.AlreadyExisted);
        Assert.Equal(first.TenantId, second.TenantId);

        // No se duplicó nada.
        Assert.Equal(1, await db.Tenants.CountAsync());
        Assert.Equal(1, await db.LudoTenantMaps.CountAsync());
        Assert.Equal(2, await db.AgentDefinitions.CountAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Provision_WelcomeNotUnique_Throws(int welcomeCount)
    {
        await using var db = NewDb();
        var svc = NewService(db);

        var agents = new List<AgentSeed>
        {
            new("ventas", "obj1", Welcome: welcomeCount >= 1),
            new("soporte", "obj2", Welcome: welcomeCount >= 2),
        };
        var req = SampleRequest() with { Agentes = agents };

        await Assert.ThrowsAsync<ProvisioningValidationException>(
            () => svc.ProvisionAsync(req, CancellationToken.None));

        // Nada se creó (validación antes de la transacción).
        Assert.Equal(0, await db.Tenants.CountAsync());
    }

    // ── Fase 3: generador + plantilla vetada ────────────────────────────────────────────

    [Fact]
    public async Task Generator_Seguro_AlwaysIncludesAuthGate()
    {
        var gen = NewGenerator(); // sin key → fallback determinista
        var result = await gen.GenerateAsync(
            new GenerateTemplateRequest("seguro", "ventas", "Vender pólizas", [new StageInfo("Nuevo", 1)]),
            tenantApiKey: null, CancellationToken.None);

        Assert.False(result.UsedLlm); // fallback
        Assert.Contains("GATE DE AUTENTICACIÓN", result.SystemPrompt);
        Assert.Contains(CampaignTemplateGenerator.VettedSeguroAuthGate, result.SystemPrompt);
    }

    [Fact]
    public async Task Generator_NonSeguro_HasNoAuthGate()
    {
        var gen = NewGenerator();
        var result = await gen.GenerateAsync(
            new GenerateTemplateRequest("restaurante", "reservas", "Tomar reservas", [new StageInfo("Nuevo", 1)]),
            tenantApiKey: null, CancellationToken.None);

        Assert.DoesNotContain("GATE DE AUTENTICACIÓN", result.SystemPrompt);
    }

    [Fact]
    public async Task Provision_Seguro_MasterPromptHasAuthGate()
    {
        await using var db = NewDb();
        var svc = NewService(db);

        var result = await svc.ProvisionAsync(SampleRequest(), CancellationToken.None);

        var masters = await db.CampaignTemplates.Where(t => t.TenantId == result.TenantId).ToListAsync();
        Assert.All(masters, m => Assert.Contains("GATE DE AUTENTICACIÓN", m.SystemPrompt));
    }

    // ── Fase 4: lienzo Ludo + LlmApiKey dedicada ─────────────────────────────────────────

    [Fact]
    public async Task Provision_CreatesLudoFlow_BoundToWelcomeMasterOnly()
    {
        await using var db = NewDb();
        var svc = NewService(db);

        var result = await svc.ProvisionAsync(SampleRequest(), CancellationToken.None);

        // Se creó exactamente un lienzo para el tenant.
        var flow = await db.TenantFlows.SingleAsync(f => f.TenantId == result.TenantId);
        Assert.True(flow.IsActive);
        Assert.Contains("registrar_oportunidad", flow.FlowJson);
        Assert.Contains("mover_fase", flow.FlowJson);

        // Solo el maestro del agente welcome (ventas) queda vinculado al lienzo.
        var masters = await db.CampaignTemplates.Where(t => t.TenantId == result.TenantId).ToListAsync();
        var ventas = masters.Single(m => m.Name.EndsWith("ventas"));
        var soporte = masters.Single(m => m.Name.EndsWith("soporte"));
        Assert.Equal(flow.Id, ventas.ActiveFlowId);
        Assert.Null(soporte.ActiveFlowId);
    }

    [Fact]
    public async Task Provision_NoEtapas_CreatesNoFlow()
    {
        await using var db = NewDb();
        var svc = NewService(db);

        var req = SampleRequest() with { Etapas = [] };
        var result = await svc.ProvisionAsync(req, CancellationToken.None);

        Assert.Equal(0, await db.TenantFlows.CountAsync(f => f.TenantId == result.TenantId));
        var masters = await db.CampaignTemplates.Where(t => t.TenantId == result.TenantId).ToListAsync();
        Assert.All(masters, m => Assert.Null(m.ActiveFlowId));
    }

    [Fact]
    public async Task Provision_AssignsDedicatedLudoLlmApiKey_WhenConfigured()
    {
        await using var db = NewDb();
        var svc = NewService(db, ConfigWith("Ludo:DefaultLlmApiKey", "sk-ant-ludo-dedicated"));

        var result = await svc.ProvisionAsync(SampleRequest(), CancellationToken.None);

        var tenant = await db.Tenants.FindAsync(result.TenantId);
        Assert.Equal("sk-ant-ludo-dedicated", tenant!.LlmApiKey);
    }

    [Fact]
    public async Task Provision_NoKeyConfigured_LeavesLlmApiKeyNull()
    {
        await using var db = NewDb();
        var svc = NewService(db); // config vacía

        var result = await svc.ProvisionAsync(SampleRequest(), CancellationToken.None);

        var tenant = await db.Tenants.FindAsync(result.TenantId);
        Assert.True(string.IsNullOrEmpty(tenant!.LlmApiKey));
    }
}
