using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MiraiNote.Core;
using MiraiNote.Core.Services;
using MiraiNote.Shared.Dtos.Chat;
using MiraiNote.Shared.Dtos.Agent;
using Moq;
using Xunit;

namespace MiraiNote.Tests;

public class ChatContinuationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContinuesBeyondTwentyRoundsUntilFileIsDelivered(bool agent)
    {
        using var harness = new Harness(25, prematureStop: false);
        var result = await harness.RunAsync(agent);
        Assert.Contains("全部处理完成", result);
        Assert.Equal("step 25", await File.ReadAllTextAsync(harness.ResultPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlanningStopAutomaticallyContinuesToActualFileWrite(bool agent)
    {
        using var harness = new Harness(1, prematureStop: true);
        var result = await harness.RunAsync(agent);
        Assert.Contains("全部处理完成", result);
        Assert.Equal("step 1", await File.ReadAllTextAsync(harness.ResultPath));
    }

    [Fact]
    public async Task NonStreamingAlsoContinuesPastTwentyRounds()
    {
        using var harness = new Harness(25, prematureStop: true);
        Assert.Contains("全部处理完成", await harness.RunAsync(false, nonStreaming: true));
        Assert.Equal("step 25", await File.ReadAllTextAsync(harness.ResultPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TruncatedResponseContinues(bool agent)
    {
        using var harness = new Harness(1, prematureStop: true, firstFinish: "length");
        Assert.Contains("全部处理完成", await harness.RunAsync(agent));
        Assert.Equal("step 1", await File.ReadAllTextAsync(harness.ResultPath));
    }

    [Theory]
    [InlineData("needs_input")]
    [InlineData("blocked")]
    public async Task MissingInputOrExternalBlockDoesNotFabricateFile(string status)
    {
        using var harness = new Harness(1, true, reviewStatus: status);
        var result = await harness.RunAsync(true);
        Assert.Contains(status == "needs_input" ? "需要补充信息" : "尚未完成", result);
        Assert.False(File.Exists(harness.ResultPath));
    }

    [Fact]
    public async Task PostRunReflectionDoesNotRestartCompletedOperations()
    {
        using var harness = new Harness(1, false, reflectFollowUp: true);
        Assert.Contains("全部处理完成", await harness.RunAsync(true, persistentAgent: true));
        Assert.Equal("step 1", await File.ReadAllTextAsync(harness.ResultPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransientModelFailureRetriesAndFinishes(bool agent)
    {
        using var harness = new Harness(1, false, transientFailures: 1);
        Assert.Contains("全部处理完成", await harness.RunAsync(agent));
        Assert.Equal("step 1", await File.ReadAllTextAsync(harness.ResultPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrokenUpstreamStreamContinuesWithoutReplayingExecutedTools(bool agent)
    {
        using var harness = new Harness(2, false, streamFailures: 1);
        Assert.Contains("全部处理完成", await harness.RunAsync(agent));
        Assert.Equal(2, harness.ExecutedTools);
        Assert.Equal("step 2", await File.ReadAllTextAsync(harness.ResultPath));
    }

    private sealed class Harness : IDisposable
    {
        private readonly MiraiTestFixture _db = new();
        private readonly ServiceProvider _provider;
        private readonly string _root = Path.Combine(Path.GetTempPath(), "mirai-loop-" + Guid.NewGuid().ToString("N"));
        public string ResultPath => Path.Combine(_root, "users", "1", "result.txt");
        public int ExecutedTools { get; private set; }

        public Harness(int steps, bool prematureStop, string firstFinish = "stop", string? reviewStatus = null, bool reflectFollowUp = false, int transientFailures = 0, int streamFailures = 0)
        {
            var calls = 0;
            var writes = 0;
            var factory = MiraiTestFixture.MockDeepSeekFactory(request =>
            {
                using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                if (body.RootElement.TryGetProperty("response_format", out _))
                    return MiraiTestFixture.DeepSeekContentResponse(JsonSerializer.Serialize(new
                    {
                        status = reviewStatus ?? (writes == steps ? "completed" : "continue"),
                        reason = writes == steps ? "文件已写入，所有步骤完成。" : "仅给出计划，文件还未写入。",
                        next_step = writes == steps ? "" : "调用 write_file 写入 result.txt。"
                    }));

                calls++;
                if (transientFailures-- > 0)
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("temporary outage") };
                var stream = body.RootElement.TryGetProperty("stream", out var streamEl) && streamEl.GetBoolean();
                if (stream && writes == 1 && streamFailures-- > 0)
                {
                    Assert.Contains("call_1", body.RootElement.GetProperty("messages").ToString());
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StreamContent(new BrokenStream("data: " + JsonSerializer.Serialize(new
                        {
                            choices = new[] { new { delta = new { content = "正在核对", tool_calls = new[] {
                                new { index = 0, id = "incomplete_call", function = new { name = "write_file", arguments = "{\"path\":" } }
                            } } } }
                        }) + "\n\n"))
                    };
                }
                if (reflectFollowUp && writes >= steps &&
                    body.RootElement.GetProperty("messages").EnumerateArray().Last().GetProperty("role").GetString() == "user")
                    return Response("", "tool_calls", stream, new[] { new
                    {
                        index = 0, id = "replayed_call", type = "function",
                        function = new { name = "write_file", arguments = "{\"path\":\"result.txt\",\"content\":\"unwanted-replay\"}" }
                    }});
                if (prematureStop && calls == 1)
                    return Response("我会继续整理并写入结果。", firstFinish, stream);
                if (writes++ < steps)
                    return Response("", "tool_calls", stream, new[] { new
                    {
                        index = 0, id = "call_" + writes, type = "function",
                        function = new { name = "write_file", arguments = JsonSerializer.Serialize(new
                        { path = "result.txt", content = "step " + writes }) }
                    }});
                writes = steps;
                return Response("全部处理完成" + (reflectFollowUp ? new string('。', 110) : ""), "stop", stream);
            });
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOptions();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            var environment = new Mock<IHostEnvironment>();
            environment.SetupGet(e => e.ContentRootPath).Returns(_root);
            services.AddSingleton(environment.Object);
            services.AddSingleton(_db.CreateContext());
            services.AddCoreLayer();
            var memories = new Mock<IAgentMemoryService>();
            memories.Setup(m => m.GetRelevantMemoriesAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RelevantMemoryDto>());
            memories.Setup(m => m.AutoExtractAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            services.AddSingleton(memories.Object);
            var reflector = new Mock<IAgentReflectorService>();
            reflector.Setup(r => r.ReflectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
                .ReturnsAsync(new ReflectionResult { NeedsFollowUp = true, FollowUpAction = "再次检查结果" });
            services.AddSingleton(reflector.Object);
            services.AddSingleton(factory);
            services.Configure<DeepSeekOptions>(o => o.ApiKey = "test-only");
            services.Configure<FileSystemOptions>(o => o.WorkspaceRoot = _root);
            _provider = services.BuildServiceProvider();
        }

        public async Task<string> RunAsync(bool agent, bool nonStreaming = false, bool persistentAgent = false)
        {
            var service = _provider.GetRequiredService<IChatService>();
            var result = "";
            Task Callback(string type, string data)
            {
                if (type == "tool_call") ExecutedTools++;
                if (type == "done") result = JsonDocument.Parse(data).RootElement.GetProperty("content").GetString()!;
                return Task.CompletedTask;
            }
            var request = new TemporaryChatRequest { Content = "执行所有处理步骤，把最终结果写入 result.txt。", SkipConfirmation = true, EnablePlanner = false, EnableReflector = false };
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            if (persistentAgent)
            {
                var session = await service.CreateSessionAsync(1, new CreateSessionRequest(), cts.Token);
                await service.SendMessageAgentStreamAsync(1, session.Id, new SendMessageRequest
                { Content = request.Content, EnablePlanner = false, EnableReflector = true, SkipConfirmation = true }, Callback, ct: cts.Token);
                return result;
            }
            if (nonStreaming)
            {
                var session = await service.CreateSessionAsync(1, new CreateSessionRequest(), cts.Token);
                return (await service.SendMessageAsync(1, session.Id, new SendMessageRequest { Content = request.Content }, cts.Token)).Content;
            }
            if (agent) await service.SendTemporaryMessageAgentStreamAsync(1, request, Callback, ct: cts.Token);
            else await service.SendTemporaryMessageStreamAsync(1, request, Callback, cts.Token);
            return result;
        }

        private sealed class BrokenStream(string prefix) : MemoryStream(Encoding.UTF8.GetBytes(prefix))
        {
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
                => Position >= Length ? ValueTask.FromException<int>(new IOException("Upstream disconnected"))
                    : base.ReadAsync(buffer, cancellationToken);
        }

        private static HttpResponseMessage Response(string content, string finish, bool stream, object? toolCalls = null) => new(HttpStatusCode.OK)
        {
            Content = stream ? new StringContent("data: " + JsonSerializer.Serialize(new
            { choices = new[] { new { delta = new { content, tool_calls = toolCalls }, finish_reason = finish } } })
                + "\n\ndata: [DONE]\n\n", Encoding.UTF8, "text/event-stream")
                : new StringContent(JsonSerializer.Serialize(new
                { choices = new[] { new { message = new { content, tool_calls = toolCalls }, finish_reason = finish } } }))
        };

        public void Dispose()
        {
            _provider.Dispose();
            _db.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
