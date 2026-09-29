using Microsoft.Extensions.Hosting;

namespace MiraiNote.Core.Services.Household;

/// <summary>
/// 测试时钟只在非 Production 且配置显式打开时生效。Production 强制关闭。
/// </summary>
public static class HouseholdTestClockPolicy
{
    public static bool IsEnabled(HouseholdOptions options, IHostEnvironment environment) =>
        !environment.IsProduction() && options.TestClock.Enabled;
}
