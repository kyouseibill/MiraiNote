using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using MiraiNote.Core.Services;
using MiraiNote.Core.Services.AgentRuns;
using MiraiNote.Core.Services.ChatModels;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Dtos.Chat;
using Moq;
using Xunit;

namespace MiraiNote.Tests;

public class AgentRunEventSequenceTests
{
    [Fact]
    public async Task Failed_event_save_does_not_reuse_sequence_on_the_failure_marker()
    {
        using var fixture = new MiraiTestFixture();
        var interceptor = new ThrowOnFirstTokenEventInterceptor();
        await using var db = fixture.CreateContextWithInterceptor(interceptor);
        var runId = await SeedQueuedRunAsync(db);
        var chat = new Mock<IChatService>();
        chat.Setup(service => service.SendMessageAgentStreamAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<SendMessageRequest>(),
                It.IsAny<ChatStreamCallback>(),
                It.IsAny<Func<Task<bool>>?>(),
                It.IsAny<CancellationToken>()))
            .Returns<int, int, SendMessageRequest, ChatStreamCallback, Func<Task<bool>>?, CancellationToken>(
                async (_, _, _, callback, _, _) =>
                {
                    await callback("token", "{\"content\":\"hello\"}");
                    await callback("done", "{\"messageId\":1,\"content\":\"hello\"}");
                });

        var service = CreateService(db, chat.Object);
        await service.ExecuteAsync(runId, CancellationToken.None);

        Assert.Equal(1, interceptor.TokenSaveAttempts);
        Assert.DoesNotContain(db.ChangeTracker.Entries<AgentRunEvent>(), entry => entry.State == EntityState.Added);

        await using var read = fixture.CreateContext();
        var events = await read.AgentRunEvents.AsNoTracking()
            .Where(item => item.RunId == runId)
            .OrderBy(item => item.Sequence)
            .ToListAsync();
        Assert.Equal(new long[] { 1, 3 }, events.Select(item => item.Sequence).ToArray());
        Assert.Equal("context", events[0].Type);
        Assert.Equal("error", events[1].Type);
        Assert.Equal(AgentRunStatus.Failed, (await read.AgentRuns.AsNoTracking().SingleAsync(item => item.Id == runId)).Status);
    }

    [Fact]
    public async Task Token_chunks_are_coalesced_into_one_event()
    {
        using var fixture = new MiraiTestFixture();
        await using var db = fixture.CreateContext();
        var runId = await SeedQueuedRunAsync(db);
        var chat = new Mock<IChatService>();
        chat.Setup(service => service.SendMessageAgentStreamAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<SendMessageRequest>(),
                It.IsAny<ChatStreamCallback>(),
                It.IsAny<Func<Task<bool>>?>(),
                It.IsAny<CancellationToken>()))
            .Returns<int, int, SendMessageRequest, ChatStreamCallback, Func<Task<bool>>?, CancellationToken>(
                async (_, _, _, callback, _, _) =>
                {
                    for (var i = 0; i < 30; i++)
                        await callback("token", "{\"content\":\"x\"}");
                    await callback("done", "{\"messageId\":9,\"content\":\"done\"}");
                });

        var service = CreateService(db, chat.Object);
        await service.ExecuteAsync(runId, CancellationToken.None);

        await using var read = fixture.CreateContext();
        var tokens = await read.AgentRunEvents.AsNoTracking()
            .Where(item => item.RunId == runId && item.Type == "token")
            .ToListAsync();
        Assert.InRange(tokens.Count, 1, 3);
        var combined = string.Concat(tokens.Select(item =>
        {
            using var json = JsonDocument.Parse(item.DataJson);
            return json.RootElement.GetProperty("content").GetString();
        }));
        Assert.Equal(new string('x', 30), combined);
        Assert.Empty(db.ChangeTracker.Entries<AgentRunEvent>());
    }

    [Fact]
    public async Task Same_run_cannot_be_executed_twice()
    {
        using var fixture = new MiraiTestFixture();
        await using var seed = fixture.CreateContext();
        var runId = await SeedQueuedRunAsync(seed);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var chat = new Mock<IChatService>();
        chat.Setup(service => service.SendMessageAgentStreamAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<SendMessageRequest>(),
                It.IsAny<ChatStreamCallback>(),
                It.IsAny<Func<Task<bool>>?>(),
                It.IsAny<CancellationToken>()))
            .Returns<int, int, SendMessageRequest, ChatStreamCallback, Func<Task<bool>>?, CancellationToken>(
                async (_, _, _, callback, _, _) =>
                {
                    Interlocked.Increment(ref calls);
                    entered.TrySetResult();
                    await release.Task;
                    await callback("done", "{\"messageId\":1,\"content\":\"ok\"}");
                });

        var dispatcher = new AgentRunDispatcher();
        var admission = new AgentRunAdmissionQueue(4, 2);
        await using var firstDb = fixture.CreateContext();
        await using var secondDb = fixture.CreateContext();
        var first = CreateService(firstDb, chat.Object, dispatcher, admission);
        var second = CreateService(secondDb, chat.Object, dispatcher, admission);

        var firstTask = first.ExecuteAsync(runId, CancellationToken.None);
        var secondTask = second.ExecuteAsync(runId, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(100);
        Assert.Equal(1, Volatile.Read(ref calls));

        release.TrySetResult();
        await firstTask.WaitAsync(TimeSpan.FromSeconds(3));
        await secondTask.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, Volatile.Read(ref calls));

        await using var read = fixture.CreateContext();
        Assert.Equal(AgentRunStatus.Completed, (await read.AgentRuns.AsNoTracking().SingleAsync(item => item.Id == runId)).Status);
    }

    [Fact]
    public async Task Status_reads_do_not_stick_to_the_first_tracked_value()
    {
        using var fixture = new MiraiTestFixture();
        await using var writer = fixture.CreateContext();
        var runId = await SeedQueuedRunAsync(writer);
        await using var reader = fixture.CreateContext();
        var service = CreateService(reader, Mock.Of<IChatService>());

        var before = await service.GetAsync(1, runId, CancellationToken.None);
        Assert.Equal(AgentRunStatus.Queued, before.Status);

        var stored = await writer.AgentRuns.SingleAsync(item => item.Id == runId);
        stored.Status = AgentRunStatus.Completed;
        stored.CompletedAt = DateTime.UtcNow;
        await writer.SaveChangesAsync();

        var after = await service.GetAsync(1, runId, CancellationToken.None);
        Assert.Equal(AgentRunStatus.Completed, after.Status);
    }

    [Fact]
    public void Cumulative_stream_deltas_do_not_copy_the_accumulated_buffer_for_a_short_chunk()
    {
        var previous = new System.Text.StringBuilder("hello world, this is already a long reasoning trace");
        Assert.Equal("!", ChatService.NormalizeStreamDelta(previous, "!", cumulative: true));
        Assert.Equal(" again", ChatService.NormalizeStreamDelta(previous, previous.ToString() + " again", cumulative: true));
        Assert.Equal("delta", ChatService.NormalizeStreamDelta(previous, "delta", cumulative: false));
    }

    private static AgentRunService CreateService(
        MiraiNoteDbContext db,
        IChatService chat,
        AgentRunDispatcher? dispatcher = null,
        AgentRunAdmissionQueue? admission = null) =>
        new(db,
            dispatcher ?? new AgentRunDispatcher(),
            admission ?? new AgentRunAdmissionQueue(4, 2),
            chat,
            Mock.Of<IChatModelRegistry>(),
            NullLogger<AgentRunService>.Instance);

    private static async Task<Guid> SeedQueuedRunAsync(MiraiNoteDbContext db)
    {
        var session = new ChatSession { UserId = 1, Title = "工作" };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();
        var run = new AgentRun
        {
            UserId = 1,
            SessionId = session.Id,
            Status = AgentRunStatus.Queued,
            RequestJson = JsonSerializer.Serialize(new SendMessageRequest { Content = "执行" }),
            LastActivityAt = DateTime.UtcNow
        };
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        return run.Id;
    }

    private sealed class ThrowOnFirstTokenEventInterceptor : SaveChangesInterceptor
    {
        public int TokenSaveAttempts { get; private set; }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            ThrowIfToken(eventData);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfToken(eventData);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void ThrowIfToken(DbContextEventData eventData)
        {
            if (eventData.Context == null) return;
            var hasToken = eventData.Context.ChangeTracker.Entries<AgentRunEvent>()
                .Any(entry => entry.State == EntityState.Added && entry.Entity.Type == "token");
            if (!hasToken) return;
            TokenSaveAttempts++;
            if (TokenSaveAttempts == 1)
                throw new DbUpdateException("simulated event save failure");
        }
    }
}
