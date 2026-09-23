using MiraiNote.Core.Services.ChatModels;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Chat;
using Xunit;

namespace MiraiNote.Tests;

public class ChatImagePolicyTests
{
    [Theory]
    [InlineData("deepseek", "deepseek-v4-pro")]
    [InlineData("minimax", "MiniMax-M2.7")]
    public void Images_are_rejected_before_sending_to_a_text_only_model(string provider, string model)
    {
        var error = Assert.Throws<BusinessException>(() =>
            ChatImagePolicy.Validate(CreateImageRequest("data:image/png;base64,iVBORw=="), Connection(provider, model)));

        Assert.Contains("不支持图片", error.Message);
    }

    [Fact]
    public void Images_are_allowed_for_deepseek_flash()
    {
        ChatImagePolicy.Validate(CreateImageRequest("data:image/png;base64,iVBORw=="), Connection("deepseek", "deepseek-v4-flash"));
    }

    [Fact]
    public void Images_over_the_total_payload_limit_are_rejected()
    {
        var first = new string('A', 9 * 1024 * 1024);
        var request = CreateImageRequest($"data:image/png;base64,{first}");
        request.Attachments!.Add(new ChatAttachmentContent
        {
            FileName = "second.png", FileType = "图片", MimeType = "image/png", IsImage = true,
            DataUrl = $"data:image/png;base64,{first}"
        });

        var error = Assert.Throws<BusinessException>(() =>
            ChatImagePolicy.Validate(request, Connection("deepseek", "deepseek-v4-flash")));

        Assert.Contains("总大小", error.Message);
    }

    private static SendMessageRequest CreateImageRequest(string dataUrl) => new()
    {
        Content = "看图",
        Attachments = [new ChatAttachmentContent
        {
            FileName = "first.png", FileType = "图片", MimeType = "image/png", IsImage = true,
            DataUrl = dataUrl
        }]
    };

    private static ChatModelConnection Connection(string provider, string model) =>
        new(provider, model, "https://example.invalid/", "test-key", false);
}
