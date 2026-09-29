using MiraiNote.Shared.Common;

namespace MiraiNote.Core.Services.Household;

/// <summary>
/// 家务周期权限。PRD v1.2：成员可查看、新建事项、标记完成；
/// 编辑、暂停、删除事项，以及删除耗材，只有管理员能做。
/// 开关集中在这里，调用方不要各自判断角色。
/// </summary>
public sealed class HouseholdAccessPolicy
{
    public bool OnlyAdminCanUpdateItems { get; init; } = true;
    public bool OnlyAdminCanPauseItems { get; init; } = true;
    public bool OnlyAdminCanDeleteItems { get; init; } = true;
    public bool OnlyAdminCanDeleteConsumables { get; init; } = true;
    public bool OnlyAdminCanRestoreArchivedItems { get; init; } = true;

    public static HouseholdAccessPolicy Default { get; } = new();

    public void EnsureCanUpdateItem(bool isAdmin) =>
        Ensure(OnlyAdminCanUpdateItems, isAdmin, "只有管理员可以编辑事项");

    public void EnsureCanPauseItem(bool isAdmin) =>
        Ensure(OnlyAdminCanPauseItems, isAdmin, "只有管理员可以暂停或恢复事项");

    public void EnsureCanDeleteItem(bool isAdmin) =>
        Ensure(OnlyAdminCanDeleteItems, isAdmin, "只有管理员可以删除事项");

    public void EnsureCanDeleteConsumable(bool isAdmin) =>
        Ensure(OnlyAdminCanDeleteConsumables, isAdmin, "只有管理员可以删除耗材");

    public void EnsureCanRestoreArchivedItem(bool isAdmin) =>
        Ensure(OnlyAdminCanRestoreArchivedItems, isAdmin, "只有管理员可以恢复已归档事项");

    private static void Ensure(bool restricted, bool isAdmin, string message)
    {
        if (restricted && !isAdmin)
            throw new BusinessException(message, 403);
    }
}
