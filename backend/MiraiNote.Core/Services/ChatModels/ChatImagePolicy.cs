using MiraiNote.Shared.Dtos.Chat;
using MiraiNote.Shared.Common;

namespace MiraiNote.Core.Services.ChatModels;

public static class ChatImagePolicy
{
    public const int MaxTotalDataUrlChars = 16 * 1024 * 1024;

    private static readonly HashSet<string> VisionModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "deepseek-flash", "deepseek-v4-flash", "deepseek-v4-flash-vision-exp"
    };

    public static void Validate(SendMessageRequest request, ChatModelConnection modelConnection) =>
        Validate(request, modelConnection.ProviderKey, modelConnection.ModelId);

    public static void Validate(SendMessageRequest request, string provider, string modelId)
    {
        var images = request.Attachments?.Where(attachment => attachment.IsImage).ToArray();
        if (images is not { Length: > 0 }) return;

        if (!string.Equals(provider, "deepseek", StringComparison.OrdinalIgnoreCase) ||
            !VisionModels.Contains(modelId))
            throw new BusinessException("当前模型不支持图片，请选择支持图片的 DeepSeek Flash 模型。", 400);

        long totalChars = 0;
        foreach (var image in images)
        {
            var dataUrl = image.DataUrl;
            if (string.IsNullOrWhiteSpace(dataUrl) ||
                !new[] { "data:image/jpeg;base64,", "data:image/png;base64,", "data:image/gif;base64,", "data:image/webp;base64," }
                    .Any(prefix => dataUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                throw new BusinessException("图片格式不受支持，请上传 JPEG、PNG、GIF 或 WebP。", 400);

            totalChars += dataUrl.Length;
            if (totalChars > MaxTotalDataUrlChars)
                throw new BusinessException("图片总大小超过限制，请移除部分图片后重试。", 400);
        }
    }
}
