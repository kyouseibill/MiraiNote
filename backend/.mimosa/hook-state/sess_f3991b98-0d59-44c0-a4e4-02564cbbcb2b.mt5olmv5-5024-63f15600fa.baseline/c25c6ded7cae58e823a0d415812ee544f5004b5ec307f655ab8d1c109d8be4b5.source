namespace MiraiNote.Shared.Common;

/// <summary>
/// JWT 配置。对应 appsettings.json 中 Jwt 节。
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "MiraiNote";
    public string Audience { get; set; } = "MiraiNote";
    public int AccessTokenExpiryHours { get; set; } = 2;
    public int RefreshTokenExpiryDays { get; set; } = 1;
    public int RefreshTokenExpiryDaysRememberMe { get; set; } = 30;
}
