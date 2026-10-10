using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MiraiNote.API.Controllers;
using MiraiNote.Core.Services;
using MiraiNote.Core.Services.AgentRuns;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Chat;
using Moq;
using Xunit;

namespace MiraiNote.Tests;

public class AgentRunEventStreamTests
{
    [Fact]
    public async Task Stream_closes_after_replaying_a_terminal_event()
    {
        var runId = Guid.NewGuid();
        var runs = new ScriptedAgentRunService
        {
            Events =
            [
                new AgentRunEventDto(1, "token", "{\"content\":\"hi\"}", DateTime.UtcNow),
                new AgentRunEventDto(2, "done", "{\"content\":\"hi\"}", DateTime.UtcNow)
            ],
            Snapshot = Snapshot(runId, AgentRunStatus.Completed)
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var body = await Subscribe(runs, runId, afterSequence: 0, cts.Token);

        Assert.Contains("event: done", body);
        Assert.Contains("event: token", body);
        Assert.True(runs.WaitCalls == 0);
    }

    [Fact]
    public async Task Stream_closes_when_the_client_attaches_after_completion()
    {
        var runId = Guid.NewGuid();
        var runs = new ScriptedAgentRunService
        {
            Events = [],
            Snapshot = Snapshot(runId, AgentRunStatus.Completed)
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var body = await Subscribe(runs, runId, afterSequence: 4, cts.Token);

        Assert.Equal(string.Empty, body);
        Assert.Equal(0, runs.WaitCalls);
    }

    [Fact]
    public async Task Queued_run_announces_queued_before_waiting()
    {
        var runId = Guid.NewGuid();
        var runs = new ScriptedAgentRunService
        {
            Events = [],
            Snapshot = Snapshot(runId, AgentRunStatus.Queued, queuePosition: 2),
            Wait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var bodyTask = Subscribe(runs, runId, afterSequence: 0, cts.Token);
        var sawQueued = false;
        var deadline = DateTime.UtcNow.AddSeconds(1);
        while (DateTime.UtcNow < deadline && !sawQueued)
        {
            await Task.Delay(20);
            sawQueued = runs.LastBody?.Contains("event: queued") == true && runs.LastBody.Contains("\"position\":2");
        }

        Assert.True(sawQueued);
        cts.Cancel();
        try { await bodyTask; }
        catch (OperationCanceledException) { }
    }

    private static async Task<string> Subscribe(ScriptedAgentRunService runs, Guid runId, long afterSequence, CancellationToken ct = default)
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(item => item.UserId).Returns(1);
        var controller = new ChatController(
            Mock.Of<IChatService>(),
            user.Object,
            new ChatFileParserService(Microsoft.Extensions.Options.Options.Create(new DeepSeekOptions())),
            new ChatSessionRunGate(),
            runs,
            NullLogger<ChatController>.Instance);
        var body = new MemoryStream();
        var http = new DefaultHttpContext();
        http.Response.Body = body;
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        runs.Body = body;

        await controller.SubscribeAgentRunEvents(runId, afterSequence, ct);
        body.Position = 0;
        return Encoding.UTF8.GetString(body.ToArray());
    }

    private static AgentRunSnapshot Snapshot(Guid runId, string status, int? queuePosition = null) =>
        new(runId, 1, status, 0, null, DateTime.UtcNow, DateTime.UtcNow, null, queuePosition);

    private sealed class ScriptedAgentRunService : IAgentRunService
    {
        public List<AgentRunEventDto> Events { get; init; } = [];
        public AgentRunSnapshot Snapshot { get; init; } = null!;
        public TaskCompletionSource? Wait { get; init; }
        public int WaitCalls { get; private set; }
        public MemoryStream? Body { get; set; }
        public string? LastBody => Body == null ? null : Encoding.UTF8.GetString(Body.ToArray());

        public Task<IReadOnlyList<AgentRunEventDto>> GetEventsAsync(int userId, Guid runId, long afterSequence, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AgentRunEventDto>>(Events.Where(item => item.Sequence > afterSequence).ToList());

        public Task<AgentRunSnapshot> GetAsync(int userId, Guid runId, CancellationToken ct) => Task.FromResult(Snapshot);

        public Task WaitForUpdateAsync(Guid runId, int observedSignalVersion, TimeSpan timeout, CancellationToken ct)
        {
            WaitCalls++;
            return Wait?.Task.WaitAsync(ct) ?? Task.Delay(timeout, ct);
        }

        public int CurrentSignalVersion(Guid runId) => 0;
        public Task RecoverInterruptedRunsAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<AgentRunSnapshot> CreateAsync(int userId, int sessionId, SendMessageRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<bool> StopAsync(int userId, Guid runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ConfirmAsync(int userId, Guid runId, bool confirmed, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ResumeAsync(int userId, Guid runId, CancellationToken ct) => throw new NotSupportedException();
        public Task ExecuteAsync(Guid runId, CancellationToken stoppingToken) => throw new NotSupportedException();
        public Task<bool> TryClaimQueuedAsync(Guid runId, CancellationToken stoppingToken) => throw new NotSupportedException();
        public Task ExecuteClaimedAsync(Guid runId, CancellationToken stoppingToken) => throw new NotSupportedException();
    }
}
