using AgentFlow.Application.Modules.Webhooks;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Messaging;
using AgentFlow.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MessageBufferTests;

/// <summary>
/// Reproduce el escenario reportado por el cliente:
///   "hola" [send]  /  "como estas" [send]  /  "cual es mi saldo" [send]
///   — los tres mensajes en pocos segundos, MessageBufferSeconds=2.
///
/// La invariante que probamos: independientemente de cuántos jobs Hangfire
/// dispare en paralelo, el mediator recibe UN SOLO ProcessIncomingMessageCommand
/// con los 3 mensajes concatenados.
/// </summary>
public class MessageBufferFlushJobTests
{
    private static AgentFlowDbContext CreateDb(Guid tenantId, int bufferSec)
    {
        var opts = new DbContextOptionsBuilder<AgentFlowDbContext>()
            .UseInMemoryDatabase($"buffer-{Guid.NewGuid():N}")
            .Options;
        var db = new AgentFlowDbContext(opts);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Test",
            MessageBufferSeconds = bufferSec
        });
        db.SaveChanges();
        return db;
    }

    private sealed class CapturingMediator : IMediator
    {
        public List<object> SentCommands { get; } = new();

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            SentCommands.Add(request!);
            // El handler real devuelve un ProcessIncomingMessageResult — no nos interesa el contenido.
            return Task.FromResult(default(TResponse)!);
        }

        public Task<object?> Send(object request, CancellationToken ct = default)
        {
            SentCommands.Add(request);
            return Task.FromResult<object?>(null);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest
        {
            SentCommands.Add(request!);
            return Task.CompletedTask;
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification
            => Task.CompletedTask;
    }

    [Fact]
    public async Task ThreeMessagesInBurst_ProducesSingleMediatorCall()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var phone = "+50760000001";
        var bufferSec = 2;

        var store = new InMemoryMessageBufferStore();
        var db = CreateDb(tenantId, bufferSec);
        var mediator = new CapturingMediator();
        var job = new MessageBufferFlushJob(store, db, mediator, NullLogger<MessageBufferFlushJob>.Instance);

        var t0 = DateTime.UtcNow.AddSeconds(-5).Ticks; // simulamos que el primero entró hace 5s
        await store.AppendAsync(tenantId, phone, new BufferedMessage(
            "hola", "WhatsApp", "Cliente", "id-1", null, null, t0));

        await store.AppendAsync(tenantId, phone, new BufferedMessage(
            "como estas", "WhatsApp", "Cliente", "id-2", null, null, t0 + TimeSpan.FromSeconds(1).Ticks));

        await store.AppendAsync(tenantId, phone, new BufferedMessage(
            "cual es el saldo de mi poliza", "WhatsApp", "Cliente", "id-3", null, null, t0 + TimeSpan.FromSeconds(2).Ticks));

        // Act — Hangfire programó 3 jobs (uno por cada mensaje). Simulamos los 3 disparos.
        // Job A y B salen porque el último mensaje aún era reciente. Job C drena.
        // Como en la simulación el último msg fue hace ~3s, ya pasó el threshold (2s - 0.5s).
        await job.RunAsync(tenantId, phone, CancellationToken.None);
        await job.RunAsync(tenantId, phone, CancellationToken.None);
        await job.RunAsync(tenantId, phone, CancellationToken.None);

        // Assert
        Assert.Single(mediator.SentCommands);
        var cmd = Assert.IsType<ProcessIncomingMessageCommand>(mediator.SentCommands[0]);
        Assert.Equal("hola\ncomo estas\ncual es el saldo de mi poliza", cmd.Message);
        Assert.Equal(phone, cmd.FromPhone);
        Assert.Equal(tenantId, cmd.TenantId);
    }

    [Fact]
    public async Task BurstStillArriving_FirstJobAbortsAndDoesNotProcess()
    {
        // Arrange — el último mensaje llegó hace 0.5s, threshold es 1.5s → debe abortar
        var tenantId = Guid.NewGuid();
        var phone = "+50760000002";
        var bufferSec = 2;

        var store = new InMemoryMessageBufferStore();
        var db = CreateDb(tenantId, bufferSec);
        var mediator = new CapturingMediator();
        var job = new MessageBufferFlushJob(store, db, mediator, NullLogger<MessageBufferFlushJob>.Instance);

        var lastTicks = DateTime.UtcNow.AddMilliseconds(-500).Ticks;
        await store.AppendAsync(tenantId, phone, new BufferedMessage(
            "hola", "WhatsApp", "Cliente", "id-1", null, null, lastTicks));

        // Act
        await job.RunAsync(tenantId, phone, CancellationToken.None);

        // Assert — no se envía nada al mediator porque aún esperamos más mensajes
        Assert.Empty(mediator.SentCommands);
    }

    [Fact]
    public async Task ConcurrentJobs_OnlyOneAcquiresLockAndProcesses()
    {
        // Arrange — simulamos que múltiples jobs disparan EXACTAMENTE al mismo tiempo,
        // todos pasaron el threshold de debounce. El lock optimista debe garantizar
        // que solo uno procese y los demás salgan limpios.
        var tenantId = Guid.NewGuid();
        var phone = "+50760000003";
        var bufferSec = 1;

        var store = new InMemoryMessageBufferStore();
        var db = CreateDb(tenantId, bufferSec);
        var mediator = new CapturingMediator();
        var job = new MessageBufferFlushJob(store, db, mediator, NullLogger<MessageBufferFlushJob>.Instance);

        var t0 = DateTime.UtcNow.AddSeconds(-3).Ticks;
        await store.AppendAsync(tenantId, phone, new BufferedMessage("a", "WhatsApp", "C", "1", null, null, t0));
        await store.AppendAsync(tenantId, phone, new BufferedMessage("b", "WhatsApp", "C", "2", null, null, t0 + 1));
        await store.AppendAsync(tenantId, phone, new BufferedMessage("c", "WhatsApp", "C", "3", null, null, t0 + 2));

        // Act — 5 jobs en paralelo, todos compiten por el lock
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => job.RunAsync(tenantId, phone, CancellationToken.None))
            .ToArray();
        await Task.WhenAll(tasks);

        // Assert — exactamente 1 procesamiento, los otros 4 fueron no-op por el lock o por buffer vacío
        Assert.Single(mediator.SentCommands);
        var cmd = Assert.IsType<ProcessIncomingMessageCommand>(mediator.SentCommands[0]);
        Assert.Equal("a\nb\nc", cmd.Message);
    }

    [Fact]
    public async Task SingleMessage_ProducesSingleCallWithThatMessage()
    {
        // Caso baseline: un mensaje aislado debe procesarse normalmente
        var tenantId = Guid.NewGuid();
        var phone = "+50760000004";
        var bufferSec = 1;

        var store = new InMemoryMessageBufferStore();
        var db = CreateDb(tenantId, bufferSec);
        var mediator = new CapturingMediator();
        var job = new MessageBufferFlushJob(store, db, mediator, NullLogger<MessageBufferFlushJob>.Instance);

        var t0 = DateTime.UtcNow.AddSeconds(-2).Ticks;
        await store.AppendAsync(tenantId, phone, new BufferedMessage(
            "Solo un mensaje", "WhatsApp", "Cliente", "id-1", null, null, t0));

        await job.RunAsync(tenantId, phone, CancellationToken.None);

        Assert.Single(mediator.SentCommands);
        var cmd = Assert.IsType<ProcessIncomingMessageCommand>(mediator.SentCommands[0]);
        Assert.Equal("Solo un mensaje", cmd.Message);
    }
}
