using Hangfire;
using Microsoft.Extensions.Hosting;

namespace AgentFlow.API;

/// <summary>
/// Wrapper de IHostedService para el servidor de Hangfire que absorbe
/// cualquier excepción en StartAsync/StopAsync para que el host de ASP.NET Core
/// nunca se caiga por un problema de inicialización de Hangfire.
/// </summary>
public sealed class SafeHangfireHostedService : IHostedService, IDisposable
{
    private BackgroundJobServer? _server;
    private readonly IServiceProvider _sp;

    public SafeHangfireHostedService(IServiceProvider sp)
    {
        _sp = sp;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var options = new BackgroundJobServerOptions
            {
                ServerName = $"agentflow-{Environment.MachineName}",
                WorkerCount = 2,
            };
            _server = new BackgroundJobServer(options);
            Console.WriteLine("[Hangfire] Servidor iniciado correctamente.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Hangfire] Error al iniciar servidor (la API continúa sin Hangfire): {ex.Message}");
            // No relanzamos — el API sigue funcionando sin Hangfire
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _server?.SendStop();
            _server?.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Hangfire] Error al detener servidor: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try { _server?.Dispose(); } catch { }
    }
}
