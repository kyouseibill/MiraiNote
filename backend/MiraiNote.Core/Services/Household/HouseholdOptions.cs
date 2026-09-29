namespace MiraiNote.Core.Services.Household;

/// <summary>
/// 家务周期配置。不提供默认公网地址，避免把真实域名写进仓库。
/// 环境变量：Household__PublicBaseUrl、Household__TestClock__Enabled。
/// </summary>
public sealed class HouseholdOptions
{
    public const string SectionName = "Household";

    /// <summary>
    /// 事项页的公网根地址，供以后的通知链接拼接。
    /// 为空时不生成链接。不要在仓库里填写真实地址。
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    public HouseholdTestClockOptions TestClock { get; set; } = new();
}

public sealed class HouseholdTestClockOptions
{
    /// <summary>默认关闭。Production 环境即使设为 true 也不会生效。</summary>
    public bool Enabled { get; set; }
}
