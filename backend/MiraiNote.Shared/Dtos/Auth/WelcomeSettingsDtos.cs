namespace MiraiNote.Shared.Dtos.Auth;

/// <summary>工作台欢迎语设置。国家-城市不是密钥，可以回显。</summary>
public class WelcomeSettingsDto
{
    /// <summary>「国家-城市」，例如「中国-上海」。空表示不查天气。</summary>
    public string? Place { get; set; }
}

public class UpdateWelcomeSettingsRequest
{
    /// <summary>留空表示不显示天气。格式为「国家-城市」。</summary>
    public string? Place { get; set; }
}
