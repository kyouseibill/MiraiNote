namespace MiraiNote.Core.Services.Household;

/// <summary>
/// 家务周期配置。不提供默认公网地址，避免把真实域名写进仓库。
/// 环境变量：Household__PublicBaseUrl、Household__TestClock__Enabled。
/// </summary>
public sealed class HouseholdOptions
{
    public const string SectionName = "Household";

    /// <summary>
    /// 事项页的公网根地址，Bark 点击和邮件里的事项链接都从这里拼。
    /// 为空或不是 http/https 时不生成链接。不要在仓库里填写真实地址。
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    public HouseholdTestClockOptions TestClock { get; set; } = new();

    public HouseholdNotificationOptions Notifications { get; set; } = new();
}

/// <summary>
/// 家务通知。默认关闭：到点不发送，设置页和「发送测试」仍可用。
/// 环境变量：Household__Notifications__Enabled、Household__Notifications__ProtectionKey、
/// Household__Notifications__BarkAllowedHosts__0。
/// ProtectionKey 是 Bark 地址的加密密钥，只从配置读取，不写进代码。
/// </summary>
public sealed class HouseholdNotificationOptions
{
    /// <summary>默认 false。关闭时调度器不发送任何通知。</summary>
    public bool Enabled { get; set; }

    /// <summary>Bark 地址加密用的密钥材料。为空时不能保存 Bark 地址。</summary>
    public string? ProtectionKey { get; set; }

    /// <summary>
    /// 追加允许的 Bark 主机，不含协议。api.day.app 始终放行，这里不会把它拿掉。
    /// 环境变量从 Household__Notifications__BarkAllowedHosts__0 开始。默认空。
    /// </summary>
    public string[] BarkAllowedHosts { get; set; } = [];
}

public sealed class HouseholdTestClockOptions
{
    /// <summary>默认关闭。Production 环境即使设为 true 也不会生效。</summary>
    public bool Enabled { get; set; }
}
