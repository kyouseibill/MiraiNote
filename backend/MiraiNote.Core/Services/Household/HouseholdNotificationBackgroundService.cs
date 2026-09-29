using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MiraiNote.Core.Services.Household;

/// <summary>
/// 每分钟看一次家务时钟。时间用 <see cref="IHouseholdClock"/>，Testing 环境的测试时钟因此也会生效。
/// </summary>
public sealed class HouseholdNotificationBackgroundService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);
    private readonly IServiceProvider _services;
    private readonly ILogger<HouseholdNotificationBackgroundService> _logger;
    private readonly HouseholdOptions _options;

    public HouseholdNotificationBackgroundService(
        IServiceProvider services,
        IOptions<HouseholdOptions> options,
        ILogger<HouseholdNotificationBackgroundService> logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Notifications.Enabled)
            _logger.LogInformation("家务通知调度已启动，扫描周期 {Interval}", ScanInterval);
        else
            _logger.LogInformation("家务通知模块未开启，到点不会发送。设置页和发送测试仍可用。");

        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IHouseholdNotificationDispatcher>();
                await dispatcher.DispatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError("家务通知扫描失败，类型 {ExceptionType}", ex.GetType().Name);
            }

            try { await Task.Delay(ScanInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
