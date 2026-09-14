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
    [Fact]
    public async Task Work_continues_beyond_twenty_rounds_until_file_is_delivered()
    {
        using var harness = new Harness(25, prematureStop: false);
        var result = await harness.RunAsync(agent: true);
        Assert.Contains("全部处理完成", result);
        Assert.Equal("step 25", await File.ReadAllTextAsync(harness.ResultPath));
    }

    [Fact]
    public async Task Work_planning_stop_automatically_continues_to_actual_file_write()
    {
        using var harness = new Harness(1, prematureStop: true);
        var result = await harness.RunAsync(agent: true);
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

    [Fact]
    public async Task Work_truncated_response_continues()
    {
        using var harness = new Harness(1, prematureStop: true, firstFinish: "length");
        Assert.Contains("全部处理完成", await harness.RunAsync(agent: true));
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

    [Fact]
    public async Task Work_transient_model_failure_retries_and_finishes()
    {
        using var harness = new Harness(1, false, transientFailures: 1);
        Assert.Contains("全部处理完成", await harness.RunAsync(agent: true));
        Assert.Equal("step 1", await File.ReadAllTextAsync(harness.ResultPath));
    }

    [Fact]
    public async Task Work_broken_upstream_stream_continues_without_replaying_executed_tools()
    {
        using var harness = new Harness(2, false, streamFailures: 1);
        Assert.Contains("全部处理完成", await harness.RunAsync(agent: true));
        Assert.Equal(2, harness.ExecutedTools);
        Assert.Equal("step 2", await File.ReadAllTextAsync(harness.ResultPath));
    }

    [Fact]
    public async Task Ordinary_chat_stops_after_the_model_answer_without_calling_a_tool_or_completion_review()
    {
        using var harness = new Harness(steps: 0, prematureStop: false, ordinaryChat: true);

        var result = await harness.RunAsync(agent: false);

        Assert.Equal("这是直接回答。", result);
        Assert.Equal(0, harness.ExecutedTools);
        Assert.Equal(0, harness.CompletionReviewRequestCount);
    }

    [Fact]
    public async Task Ordinary_chat_can_read_workspace_files_but_cannot_see_write_tools()
    {
        using var harness = new Harness(
            steps: 0,
            prematureStop: false,
            ordinaryChatReadFile: true,
            userContent: "请读取 input.txt 并告诉我内容。");

        var result = await harness.RunAsync(agent: false);

        Assert.Contains("workspace evidence", result);
        Assert.True(harness.ToolRequestCount >= 1);
        Assert.Equal(0, harness.CompletionReviewRequestCount);
    }

    [Fact]
    public async Task Ordinary_chat_rejects_a_fabricated_write_tool_call_without_looping_or_side_effects()
    {
        using var harness = new Harness(
            steps: 0,
            prematureStop: false,
            ordinaryChatForbiddenTool: true,
            userContent: "聊聊如何保存笔记。");

        var result = await harness.RunAsync(agent: false);

        Assert.Contains("切换到工作模式", result);
        Assert.Equal(1, harness.ToolRequestCount);
        Assert.Equal(0, harness.ExecutedTools);
    }

    [Theory]
    [InlineData("告诉我今天的天气")]
    [InlineData("解释一下这段代码")]
    [InlineData("读取工作区里的说明文件")]
    public void Work_informational_requests_skip_completion_review_even_after_read_only_tools(string prompt)
    {
        Assert.False(ChatService.ShouldReviewWorkCompletion(
            new SendMessageRequest { Content = prompt }, finishReason: "stop"));
    }

    [Theory]
    [InlineData("继续")]
    [InlineData("帮我做个网页")]
    [InlineData("请登录网址并产出交接文件")]
    [InlineData("请解释这段代码并修复问题")]
    [InlineData("告诉我结果并生成报告")]
    public void Work_action_or_deliverable_requests_keep_completion_review(string prompt)
    {
        Assert.True(ChatService.ShouldReviewWorkCompletion(
            new SendMessageRequest { Content = prompt }, finishReason: "stop"));
    }

    [Theory]
    [InlineData("为什么天空是蓝色的？")]
    [InlineData("如何读取工作区文件？")]
    public async Task Work_direct_question_finishes_without_completion_review(string question)
    {
        using var harness = new Harness(
            steps: 0,
            prematureStop: false,
            userContent: question);

        var result = await harness.RunAsync(agent: true);

        Assert.Contains("全部处理完成", result);
        Assert.Equal(0, harness.CompletionReviewRequestCount);
    }

    [Fact]
    public async Task Work_emits_done_before_starting_noncritical_memory_extraction()
    {
        using var harness = new Harness(steps: 0, prematureStop: false);

        await harness.RunAsync(agent: true, persistentAgent: true);

        Assert.True(harness.EventOrder.IndexOf("done") >= 0);
        Assert.True(harness.EventOrder.IndexOf("memory") > harness.EventOrder.IndexOf("done"));
    }

    private sealed class Harness : IDisposable
    {
        private readonly MiraiTestFixture _db = new();
        private readonly ServiceProvider _provider;
        private readonly string _root = Path.Combine(Path.GetTempPath(), "mirai-loop-" + Guid.NewGuid().ToString("N"));
        public string ResultPath => Path.Combine(_root, "users", "1", "result.txt");
        public int ExecutedTools { get; private set; }
        public int ToolRequestCount { get; private set; }
        public int CompletionReviewRequestCount { get; private set; }
        public List<string> EventOrder { get; } = [];
        private readonly string _userContent;

        public Harness(
            int steps,
            bool prematureStop,
            string firstFinish = "stop",
            string? reviewStatus = null,
            bool reflectFollowUp = false,
            int transientFailures = 0,
            int streamFailures = 0,
            bool ordinaryChat = false,
            bool ordinaryChatReadFile = false,
            bool ordinaryChatForbiddenTool = false,
            string? userContent = null)
        {
            _userContent = userContent ?? "执行所有处理步骤，把最终结果写入 result.txt。";
            var calls = 0;
            var writes = 0;
            var factory = MiraiTestFixture.MockDeepSeekFactory(request =>
            {
                using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                if (body.RootElement.TryGetProperty("response_format", out _))
                {
                    CompletionReviewRequestCount++;
                    return MiraiTestFixture.DeepSeekContentResponse(JsonSerializer.Serialize(new
                    {
                        status = reviewStatus ?? (writes == steps ? "completed" : "continue"),
                        reason = writes == steps ? "文件已写入，所有步骤完成。" : "仅给出计划，文件还未写入。",
                        next_step = writes == steps ? "" : "调用 write_file 写入 result.txt。"
                    }));
                }

                if (body.RootElement.TryGetProperty("tools", out _)) ToolRequestCount++;

                calls++;
                if (ordinaryChatForbiddenTool)
                {
                    return Response("", "tool_calls", stream: true, new[] { new
                    {
                        index = 0, id = "forbidden_write", type = "function",
                        function = new { name = "write_file", arguments = "{\"path\":\"forbidden.txt\",\"content\":\"must not write\"}" }
                    }});
                }
                if (ordinaryChatReadFile)
                {
                    if (!body.RootElement.TryGetProperty("tools", out var chatTools))
                        return Response("没有可用的文件读取工具。", "stop", stream: true);

                    var toolNames = chatTools.EnumerateArray()
                        .Select(t => t.GetProperty("function").GetProperty("name").GetString())
                        .ToArray();
                    Assert.Contains("read_file", toolNames);
                    Assert.Contains("search_internet", toolNames);
                    Assert.Contains("fetch_web_page", toolNames);
                    Assert.DoesNotContain("write_file", toolNames);

                    var hasToolResult = body.RootElement.GetProperty("messages").EnumerateArray()
                        .Any(m => m.GetProperty("role").GetString() == "tool");
                    if (!hasToolResult)
                        return Response("", "tool_calls", stream: true, new[] { new
                        {
                            index = 0, id = "read_input", type = "function",
                            function = new { name = "read_file", arguments = "{\"path\":\"input.txt\"}" }
                        }});

                    var toolResult = body.RootElement.GetProperty("messages").EnumerateArray()
                        .Last(m => m.GetProperty("role").GetString() == "tool")
                        .GetProperty("content").GetString();
                    return Response("读取结果：" + toolResult, "stop", stream: true);
                }
                if (ordinaryChat) return Response("这是直接回答。", "stop", stream: true);
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
                .Returns(() =>
                {
                    EventOrder.Add("memory");
                    return Task.CompletedTask;
                });
            services.AddSingleton(memories.Object);
            var reflector = new Mock<IAgentReflectorService>();
            reflector.Setup(r => r.ReflectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
                .ReturnsAsync(new ReflectionResult { NeedsFollowUp = true, FollowUpAction = "再次检查结果" });
            services.AddSingleton(reflector.Object);
            services.AddSingleton(factory);
            services.Configure<DeepSeekOptions>(o => o.ApiKey = "test-only");
            services.Configure<FileSystemOptions>(o => o.WorkspaceRoot = _root);
            _provider = services.BuildServiceProvider();
            Directory.CreateDirectory(Path.Combine(_root, "users", "1"));
            File.WriteAllText(Path.Combine(_root, "users", "1", "input.txt"), "workspace evidence");
        }

        public async Task<string> RunAsync(bool agent, bool nonStreaming = false, bool persistentAgent = false)
        {
            var service = _provider.GetRequiredService<IChatService>();
            var result = "";
            Task Callback(string type, string data)
            {
                EventOrder.Add(type);
                if (type == "tool_call") ExecutedTools++;
                if (type == "done") result = JsonDocument.Parse(data).RootElement.GetProperty("content").GetString()!;
                return Task.CompletedTask;
            }
            var request = new TemporaryChatRequest { Content = _userContent, SkipConfirmation = true, EnablePlanner = false, EnableReflector = false };
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
