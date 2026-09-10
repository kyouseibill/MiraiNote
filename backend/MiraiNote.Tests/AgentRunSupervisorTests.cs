using MiraiNote.Core.Services;
using MiraiNote.Shared.Agent;
using Moq;
using Xunit;

namespace MiraiNote.Tests;

public class AgentRunSupervisorTests
{
    [Theory]
    [InlineData("completed")]
    [InlineData("needs_input")]
    [InlineData("blocked")]
    public async Task AcceptsExplicitTerminalReason(string status)
    {
        var factory = MiraiTestFixture.MockDeepSeekFactory(_ => MiraiTestFixture.DeepSeekContentResponse(
            $$"""{"status":"{{status}}","reason":"依据真实结果作出结论"}"""));
        using var client = factory.CreateClient("test");
        client.BaseAddress = new Uri("https://test.invalid");
        var decision = await new AgentRunSupervisor().ReviewAsync(client, "test", [], "候选结果", "stop", default);
        Assert.False(decision.Continue);
        Assert.Equal(status == "completed", decision.Completed);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"status\":\"completed\"}")]
    [InlineData("{\"status\":\"completed\",\"reason\":null}")]
    [InlineData("{\"status\":\"continue\",\"reason\":\"未完成\"}")]
    [InlineData("{\"status\":\"unknown\",\"reason\":\"x\"}")]
    public void RejectsMalformedCompletionVerdict(string json) => Assert.Null(AgentRunSupervisor.ParseDecision(json));

    [Fact]
    public async Task FailedReviewNeverSilentlyCompletesOrLoopsForever()
    {
        var factory = MiraiTestFixture.MockDeepSeekFactory(_ => MiraiTestFixture.DeepSeekError());
        using var client = factory.CreateClient("test");
        client.BaseAddress = new Uri("https://test.invalid");
        var supervisor = new AgentRunSupervisor();
        for (var i = 0; i < 3; i++)
            Assert.True((await supervisor.ReviewAsync(client, "test", [], "计划", "stop", default)).Continue);
        var stopped = await supervisor.ReviewAsync(client, "test", [], "计划", "stop", default);
        Assert.False(stopped.Completed);
        Assert.False(stopped.Continue);
        Assert.Contains("尚未完成", stopped.Reason);
    }

    [Fact]
    public async Task LengthLimitContinuesWithoutClaimingCompletion()
    {
        using var client = new HttpClient(); // length handling must not need another network call
        var supervisor = new AgentRunSupervisor();
        Assert.True((await supervisor.ReviewAsync(client, "test", [], "半句话", "length", default)).Continue);
    }

    [Fact]
    public async Task LongTextKeepsContinuingWhileNewChunksArrive()
    {
        using var client = new HttpClient();
        var supervisor = new AgentRunSupervisor();
        for (var i = 0; i < 8; i++)
            Assert.True((await supervisor.ReviewAsync(client, "test", [], "报告正文第 " + i + " 部分", "length", default)).Continue);
    }

    [Fact]
    public void NewToolResultsResetStallDetectionButRepeatedResultsStop()
    {
        var supervisor = new AgentRunSupervisor();
        for (var i = 0; i < 50; i++)
        {
            supervisor.ObserveTool("read_file", "{}", "page " + i);
            Assert.Null(supervisor.CheckStalled());
        }
        for (var i = 0; i < 6; i++) supervisor.ObserveTool("read_file", "{}", "page 49");
        Assert.False(supervisor.CheckStalled()!.Completed);
        supervisor.ObserveTool("read_file", "{}", "page 50");
        Assert.Null(supervisor.CheckStalled());
    }

    [Fact]
    public async Task ToolRegistryPropagatesUserCancellation()
    {
        using var cts = new CancellationTokenSource();
        var tool = new Mock<IServerAgentTool>();
        tool.SetupGet(t => t.Name).Returns("cancel_test");
        tool.Setup(t => t.ExecuteAsync(1, "{}", cts.Token)).Returns(() =>
        {
            cts.Cancel();
            return Task.FromCanceled<string>(cts.Token);
        });
        var registry = new ServerAgentToolRegistry();
        registry.Register(tool.Object);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => registry.ExecuteAsync(1, "cancel_test", "{}", cts.Token));
    }
}
