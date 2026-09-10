using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MiraiNote.Core.Services.AgentRuns;

public sealed class AgentRunBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly AgentRunDispatcher _dispatcher;
    private readonly ILogger<AgentRunBackgroundService> _logger;

    public AgentRunBackgroundService(IServiceProvider services, AgentRunDispatcher dispatcher, ILogger<AgentRunBackgroundService> logger)
    {
        _services = services;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var startupScope = _services.CreateScope())
            await startupScope.ServiceProvider.GetRequiredService<IAgentRunService>().RecoverInterruptedRunsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var runId = await _dispatcher.DequeueAsync(stoppingToken);
                using var scope = _services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IAgentRunService>().ExecuteAsync(runId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Agent 后台执行器循环失败"); }
        }
    }
}
