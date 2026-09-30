using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Core.Services.Household;

/// <summary>
/// 通道是否可送达。调度改走和设置页提示必须用同一套判断。
/// </summary>
public static class HouseholdNotificationAvailability
{
    public static bool CanDeliver(
        HouseholdNotificationChannel channel,
        bool barkEnabled,
        string? barkAddressProtected,
        bool emailEnabled,
        string? accountEmail,
        bool protectorConfigured)
    {
        if (channel == HouseholdNotificationChannel.Bark)
            return barkEnabled && protectorConfigured && !string.IsNullOrEmpty(barkAddressProtected);
        return emailEnabled && !string.IsNullOrWhiteSpace(accountEmail);
    }

    public static bool HasDeliverableChannel(
        bool barkEnabled,
        string? barkAddressProtected,
        bool emailEnabled,
        string? accountEmail,
        bool protectorConfigured) =>
        CanDeliver(HouseholdNotificationChannel.Email, barkEnabled, barkAddressProtected, emailEnabled, accountEmail, protectorConfigured)
        || CanDeliver(HouseholdNotificationChannel.Bark, barkEnabled, barkAddressProtected, emailEnabled, accountEmail, protectorConfigured);

    /// <summary>
    /// 选中的通道不可用时改走另一个可用通道。选中的通道可用时返回它自己，即使随后投递失败也不改走。
    /// </summary>
    public static HouseholdNotificationChannel? Resolve(
        HouseholdNotificationChannel preferred,
        bool barkEnabled,
        string? barkAddressProtected,
        bool emailEnabled,
        string? accountEmail,
        bool protectorConfigured)
    {
        if (CanDeliver(preferred, barkEnabled, barkAddressProtected, emailEnabled, accountEmail, protectorConfigured))
            return preferred;
        var other = preferred == HouseholdNotificationChannel.Bark
            ? HouseholdNotificationChannel.Email
            : HouseholdNotificationChannel.Bark;
        return CanDeliver(other, barkEnabled, barkAddressProtected, emailEnabled, accountEmail, protectorConfigured)
            ? other
            : null;
    }
}
