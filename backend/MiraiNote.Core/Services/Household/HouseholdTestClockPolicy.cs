using Microsoft.Extensions.Hosting;

namespace MiraiNote.Core.Services.Household;

/// <summary>
/// 测试时钟白名单：仅 Development 或 Testing，且配置显式打开。
/// Staging、Production 以及任何其他环境名都关闭。
/// </summary>
public static class HouseholdTestClockPolicy
{
    public const string TestingEnvironmentName = "Testing";

    public static bool IsEnabled(HouseholdOptions options, IHostEnvironment environment)
    {
        if (!options.TestClock.Enabled)
            return false;

        var name = environment.EnvironmentName;
        return string.Equals(name, Environments.Development, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, TestingEnvironmentName, StringComparison.OrdinalIgnoreCase);
    }
}
