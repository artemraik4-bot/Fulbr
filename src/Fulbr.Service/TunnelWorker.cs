using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fulbr.Service;

public sealed class TunnelWorker : BackgroundService
{
    private readonly NamedPipeServer _pipe;
    private readonly TunnelEngine _engine;
    private readonly ILogger<TunnelWorker> _logger;

    public TunnelWorker(NamedPipeServer pipe, TunnelEngine engine, ILogger<TunnelWorker> logger)
    {
        _pipe = pipe;
        _engine = engine;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Fulbr service starting");
        _engine.CleanupStale();

        try
        {
            await _pipe.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal service stop.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service loop failed");
        }
        finally
        {
            try
            {
                await _engine.StopAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Engine stop during service shutdown failed");
            }
            _logger.LogInformation("Fulbr service stopped");
        }
    }
}
