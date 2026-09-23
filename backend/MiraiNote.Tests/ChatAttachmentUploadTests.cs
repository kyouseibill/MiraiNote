using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MiraiNote.API.Controllers;
using MiraiNote.Core.Services;
using MiraiNote.Core.Services.AgentRuns;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Chat;
using Moq;
using Xunit;

namespace MiraiNote.Tests;

public class ChatAttachmentUploadTests
{
    [Fact]
    public async Task UploadAttachment_rejects_an_image_format_the_model_cannot_read()
    {
        var controller = new ChatController(
            Mock.Of<IChatService>(), Mock.Of<ICurrentUserService>(),
            new ChatFileParserService(Options.Create(new DeepSeekOptions())),
            new ChatSessionRunGate(), Mock.Of<IAgentRunService>(),
            NullLogger<ChatController>.Instance);
        await using var stream = new MemoryStream(new byte[] { 0x42, 0x4d, 0, 0 });
        var file = new FormFile(stream, 0, stream.Length, "file", "sample.bmp");

        var action = await controller.UploadAttachment(file, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(action.Result);
    }

    [Fact]
    public async Task UploadAttachment_rejects_an_image_with_mismatched_contents()
    {
        var controller = CreateController();
        await using var stream = new MemoryStream(new byte[] { 0x42, 0x4d, 0, 0 });
        var file = new FormFile(stream, 0, stream.Length, "file", "not-a-png.png");

        var action = await controller.UploadAttachment(file, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(action.Result);
    }

    [Fact]
    public async Task UploadAttachment_rejects_an_image_over_the_image_limit()
    {
        var controller = CreateController();
        await using var stream = new MemoryStream(new byte[6 * 1024 * 1024 + 1]);
        var file = new FormFile(stream, 0, stream.Length, "file", "large.png");

        var action = await controller.UploadAttachment(file, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(action.Result);
    }

    [Fact]
    public async Task UploadAttachment_returns_a_data_url_for_a_png_image()
    {
        var controller = new ChatController(
            Mock.Of<IChatService>(),
            Mock.Of<ICurrentUserService>(),
            new ChatFileParserService(Options.Create(new DeepSeekOptions())),
            new ChatSessionRunGate(),
            Mock.Of<IAgentRunService>(),
            NullLogger<ChatController>.Instance);
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/tVkAAAAASUVORK5CYII=");
        await using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "file", "sample.png");

        var action = await controller.UploadAttachment(file, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<ApiResponse<ChatAttachmentResponseDto>>(ok.Value);
        var attachment = Assert.IsType<ChatAttachmentResponseDto>(response.Data);
        Assert.True(attachment.IsImage);
        Assert.Equal("image/png", attachment.MimeType);
        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(bytes)}", attachment.DataUrl);
    }

    private static ChatController CreateController() => new(
        Mock.Of<IChatService>(), Mock.Of<ICurrentUserService>(),
        new ChatFileParserService(Options.Create(new DeepSeekOptions())),
        new ChatSessionRunGate(), Mock.Of<IAgentRunService>(),
        NullLogger<ChatController>.Instance);
}
