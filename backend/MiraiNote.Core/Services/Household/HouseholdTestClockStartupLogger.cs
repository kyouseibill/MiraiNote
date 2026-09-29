using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MiraiNote.Core.Services.Household;

/// <summary>测试时钟开启时在启动日志里打一条警告。不记录任何地址或密钥。</summary>
public sealed class HouseholdTestClockStartupLogger : IHostedService
{
    private readonly ILogger<HouseholdTestClockStartupLogger> _logger;
    private readonly HouseholdOptions _options;
    private readonly IHostEnvironment _environment;

    public HouseholdTestClockStartupLogger(
        ILogger<HouseholdTestClockStartupLogger> logger,
        IOptions<HouseholdOptions> options,
        IHostEnvironment environment)
    {
        _logger = logger;
        _options = options.Value;
        _environment = environment;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (HouseholdTestClockPolicy.IsEnabled(_options, _environment))
        {
            _logger.LogWarning(
                "家务测试时钟已启用（环境 {Environment}）。仅 Development/Testing 允许；只影响家务模块的“今天”，认证和其他模块仍使用系统时间。",
                _environment.EnvironmentName);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
