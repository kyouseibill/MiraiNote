using Microsoft.Extensions.Options;

namespace MiraiNote.Core.Services.Household;

/// <summary>
/// 用 <see cref="HouseholdOptions.PublicBaseUrl"/> 拼事项页链接。
/// 通知模块（PR3）再消费这里的结果。没有配置根地址时返回 null，不回退到任何内置域名。
/// </summary>
public sealed class HouseholdLinkBuilder
{
    private readonly HouseholdOptions _options;

    public HouseholdLinkBuilder(IOptions<HouseholdOptions> options)
    {
        _options = options.Value;
    }

    public string? ItemPage(int itemId)
    {
        var baseUrl = _options.PublicBaseUrl?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
            return null;
        return $"{baseUrl}/household/items/{itemId}";
    }
}
