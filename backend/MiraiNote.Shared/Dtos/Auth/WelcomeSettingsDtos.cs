namespace MiraiNote.Shared.Dtos.Auth;

/// <summary>工作台欢迎语设置。国家-城市和昵称都不是密钥，可以回显。不含 Bark key 或密码。</summary>
public class WelcomeSettingsDto
{
    /// <summary>「国家-城市」，例如「中国-上海」。空表示不查天气。</summary>
    public string? Place { get; set; }

    /// <summary>欢迎语大标题的称呼。空表示继续用用户名。</summary>
    public string? Nickname { get; set; }
}

public class UpdateWelcomeSettingsRequest
{
    /// <summary>留空表示不显示天气。格式为「国家-城市」。</summary>
    public string? Place { get; set; }

    /// <summary>留空表示改回用户名。最长 20 个字。</summary>
    public string? Nickname { get; set; }
}
