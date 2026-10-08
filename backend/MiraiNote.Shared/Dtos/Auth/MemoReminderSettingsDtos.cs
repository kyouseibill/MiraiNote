namespace MiraiNote.Shared.Dtos.Auth;

/// <summary>备忘手机提醒设置。响应里只有是否已填写，不回传 key。</summary>
public class MemoReminderSettingsDto
{
    public bool BarkConfigured { get; set; }
}

public class UpdateMemoReminderSettingsRequest
{
    /// <summary>留空表示关闭推送。只接受 key，不接受服务器地址。</summary>
    public string? BarkKey { get; set; }
}
