using AgentFlow.API.Hubs;
using AgentFlow.API.Middleware;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.AI;
using AgentFlow.Infrastructure.Persistence;
using AgentFlow.Infrastructure.Channels.UltraMsg;
using AgentFlow.Infrastructure.Session;
using AgentFlow.Infrastructure.Storage;
using AgentFlow.Infrastructure.Dispatching;
using AgentFlow.Infrastructure.Persistence.Repositories;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var cfg     = builder.Configuration;
var isDev   = builder.Environment.IsDevelopment();

// ── Persistencia ──────────────────────────────────────
builder.Services.AddDbContext<AgentFlowDbContext>(o =>
    o.UseSqlServer(cfg.GetConnectionString("DefaultConnection"),
        sql => sql.MigrationsAssembly("AgentFlow.Infrastructure")));

// ── Redis (sesiones activas) — opcional en dev ───────────
var redisConn = cfg.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(redisConn))
{
    try
    {
        var redis = ConnectionMultiplexer.Connect(redisConn + ",abortConnect=false,connectTimeout=3000");
        builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
        builder.Services.AddScoped<ISessionStore, RedisSessionStore>();
        Console.WriteLine("Redis conectado");
    }
    catch
    {
        Console.WriteLine("Redis no disponible. Usando InMemorySessionStore.");
        builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();
    }
}
else
{
    Console.WriteLine("Redis no configurado. Usando InMemorySessionStore.");
    builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();
}

// ── Anthropic Claude ───────────────────────────────────
var anthropicKey = cfg["Anthropic:ApiKey"];
if (!string.IsNullOrEmpty(anthropicKey) && anthropicKey != "YOUR_ANTHROPIC_API_KEY")
{
    builder.Services.AddSingleton(new Anthropic.SDK.AnthropicClient(anthropicKey));
    builder.Services.AddScoped<IAgentRunner, AnthropicAgentRunner>();
}
else
{
    Console.WriteLine("Anthropic no configurado. Usando NoOpAgentRunner.");
    builder.Services.AddScoped<IAgentRunner, NoOpAgentRunner>();
}

// ── MediatR ────────────────────────────────────────────
builder.Services.AddMediatR(c =>
    c.RegisterServicesFromAssemblies(
        typeof(AgentFlow.Application.Modules.Webhooks.ProcessIncomingMessageCommand).Assembly));

// ── SignalR (monitor en vivo) ──────────────────────────
builder.Services.AddSignalR();
builder.Services.AddSingleton<ConversationNotifier>();

// ── Hangfire — se inicializa pero no bloquea si la BD no tiene el schema ──
try
{
    builder.Services.AddHangfire(h => h
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseSqlServerStorage(cfg.GetConnectionString("DefaultConnection"),
            new SqlServerStorageOptions { SchemaName = "HF" }));
    builder.Services.AddHangfireServer();
}
catch (Exception ex)
{
    Console.WriteLine($"Hangfire: {ex.Message}");
}

// ── Auth — en dev permitimos requests sin JWT ───────────
if (!isDev)
{
    builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(o =>
        {
            o.Authority = cfg["Auth:Authority"];
            o.Audience  = cfg["Auth:Audience"];
        });
}
builder.Services.AddAuthorization();

// ── UltraMsg (gestion de instancias WhatsApp) ───────────
builder.Services.AddHttpClient<IUltraMsgInstanceService, UltraMsgInstanceService>();

// ── Azure Blob Storage (documentos de agentes) ──────────
builder.Services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();

// ── Repositorios y servicios de dominio ──────────────────
builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
builder.Services.AddScoped<IContextDispatcher, ContextDispatcher>();

// ── Tenant context ─────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ── CORS (dev: React en localhost:5173) ────────────────
builder.Services.AddCors(o => o.AddPolicy("dev", p =>
    p.WithOrigins("http://localhost:5173")
     .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

// ── Seed de tenant para desarrollo ──────────────────────
if (isDev)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AgentFlowDbContext>();
    var devTenantId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    if (!db.Tenants.Any(t => t.Id == devTenantId))
    {
        db.Tenants.Add(new AgentFlow.Domain.Entities.Tenant
        {
            Id = devTenantId,
            Name = "Somos Seguros (Dev)",
            Slug = "somos-seguros-dev",
            IsActive = true,
            WhatsAppProvider = AgentFlow.Domain.Enums.ProviderType.UltraMsg,
            WhatsAppPhoneNumber = "+50760001234",
        });
        db.SaveChanges();
        Console.WriteLine("Tenant de desarrollo creado: " + devTenantId);
    }

    // Seed usuario admin de desarrollo
    if (!db.AppUsers.Any(u => u.TenantId == devTenantId))
    {
        db.AppUsers.Add(new AgentFlow.Domain.Entities.AppUser
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            TenantId = devTenantId,
            FullName = "Admin AgentFlow",
            Email = "admin@agentflow.dev",
            PasswordHash = AgentFlow.API.Controllers.AuthController.HashPassword("admin123"),
            Role = AgentFlow.Domain.Entities.UserRole.Admin,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
        Console.WriteLine("Usuario admin de desarrollo creado: admin@agentflow.dev / admin123");
    }
}

app.UseCors("dev");
app.UseMiddleware<TenantMiddleware>();

if (!isDev)
{
    app.UseAuthentication();
}
app.UseAuthorization();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.MapHub<ConversationHub>("/hubs/conversations");

try { app.MapHangfireDashboard("/hangfire"); } catch { /* Hangfire no disponible */ }

app.Run();
