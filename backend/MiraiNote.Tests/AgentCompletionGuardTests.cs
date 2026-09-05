using MiraiNote.Core.Services;
using Xunit;

namespace MiraiNote.Tests;

public class AgentCompletionGuardTests
{
    [Theory]
    [InlineData("请帮我下载张山口智子年轻时的照片", "我再抓取一个图册页，尝试解析出原图直链：")]
    [InlineData("帮我下载一张猫的照片", "")]
    public void RequiresContinuation_WhenImageDownloadEndsBeforeDelivery(string request, string content)
    {
        Assert.True(AgentCompletionGuard.RequiresContinuation(request, content, hasDeliveredImage: false));
    }

    [Fact]
    public void DoesNotRequireContinuation_AfterImageDelivery()
    {
        Assert.False(AgentCompletionGuard.RequiresContinuation(
            "请下载一张猫的照片", "图片已下载", hasDeliveredImage: true));
    }

    [Fact]
    public void DoesNotRequireContinuation_ForNormalTextReply()
    {
        Assert.False(AgentCompletionGuard.RequiresContinuation(
            "解释一下番茄工作法", "番茄工作法是把工作分成固定时段的方法。", hasDeliveredImage: false));
    }

    [Fact]
    public void RecognizesSuccessfulPublishedImage()
    {
        const string result = "{\"url\":\"/uploads/agent/1/photo.jpg\",\"markdown\":\"![照片](/uploads/agent/1/photo.jpg)\"}";

        Assert.True(AgentCompletionGuard.IsPublishedImageResult("publish_workspace_file", result));
        Assert.False(AgentCompletionGuard.IsPublishedImageResult("publish_workspace_file", "发布失败：文件不存在"));
    }
}
