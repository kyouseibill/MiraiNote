using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MiraiNote.Core.Services.AgentRuns;

public sealed class AgentRunBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly AgentRunAdmissionQueue _admission;
    private readonly ILogger<AgentRunBackgroundService> _logger;

    public AgentRunBackgroundService(
        IServiceProvider services,
        AgentRunAdmissionQueue admission,
        ILogger<AgentRunBackgroundService> logger)
    {
        _services = services;
        _admission = admission;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var startupScope = _services.CreateScope();
            await startupScope.ServiceProvider.GetRequiredService<IAgentRunService>()
                .RecoverInterruptedRunsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复中断的 Agent 任务失败");
        }

        var inFlight = new List<Task>();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var version = _admission.Version;
                while (_admission.TryAdmit(out var pending))
                    inFlight.Add(ExecuteOneAsync(pending, stoppingToken));

                inFlight.RemoveAll(task => task.IsCompleted);
                await _admission.WaitForChangeAsync(version, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Agent 后台执行器循环失败");
                try { await Task.Delay(TimeSpan.FromMilliseconds(200), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        try { await Task.WhenAll(inFlight); }
        catch (Exception ex) { _logger.LogDebug(ex, "停止期间仍有 Agent 任务在收尾"); }
    }

    private async Task ExecuteOneAsync(AgentRunAdmissionQueue.PendingRun pending, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IAgentRunService>()
                .ExecuteAsync(pending.RunId, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 进程停止。任务保持非终态，下次启动再标成可恢复。
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent run {RunId} 执行失败", pending.RunId);
        }
        finally
        {
            // 失败、取消、正常结束都立刻让出名额，避免堵住其他用户。
            _admission.Release(pending.RunId, pending.UserId);
        }
    }
}
