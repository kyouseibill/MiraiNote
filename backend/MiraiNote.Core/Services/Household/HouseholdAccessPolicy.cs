namespace MiraiNote.Core.Services.Household;

/// <summary>
/// 家务周期权限开关。
/// PRD 第 8 节仍待 Bill 确认「成员能否删除事项」。默认只有管理员能删。
/// 把 <see cref="OnlyAdminCanDeleteItems"/> 设为 false 即可放开，不必改调用方。
/// </summary>
public sealed class HouseholdAccessPolicy
{
    public bool OnlyAdminCanDeleteItems { get; init; } = true;

    public static HouseholdAccessPolicy Default { get; } = new();
}
