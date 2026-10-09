using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MiraiNote.Core.Services;
using MiraiNote.Core.Services.AgentRuns;
using MiraiNote.Shared.Dtos.Chat;
using Xunit;

namespace MiraiNote.Tests;

public class AgentRunConcurrencyTests
{
    [Fact]
    public void Production_defaults_are_two_global_and_one_per_user()
    {
        var options = new AgentRunOptions();
        Assert.Equal(2, options.MaxConcurrent);
        Assert.Equal(1, options.MaxPerUser);
    }

    [Fact]
    public void Appsettings_can_raise_the_caps()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AgentRuns:MaxConcurrent"] = "4",
            ["AgentRuns:MaxPerUser"] = "2",
        }).Build();
        var services = new ServiceCollection();
        services.AddOptions<AgentRunOptions>().Bind(config.GetSection(AgentRunOptions.SectionName));
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<AgentRunOptions>>().Value;
        Assert.Equal(4, options.MaxConcurrent);
        Assert.Equal(2, options.MaxPerUser);
    }

    [Fact]
    public async Task Defaults_let_a_second_user_start_while_the_first_user_is_capped()
    {
        var options = new AgentRunOptions();
        await using var harness = await Harness.Start(options);
        var userA = 11;
        var userB = 12;
        var first = Guid.NewGuid();
        var secondForA = Guid.NewGuid();
        var otherUser = Guid.NewGuid();

        harness.Enqueue(first, userA);
        harness.Enqueue(secondForA, userA);
        harness.Enqueue(otherUser, userB);

        var started = await harness.Runs.TakeStarted(2, TimeSpan.FromSeconds(3));
        Assert.Contains(first, started);
        Assert.Contains(otherUser, started);
        Assert.DoesNotContain(secondForA, started);
        await Task.Delay(100);
        Assert.DoesNotContain(secondForA, harness.Runs.StartedIds);
    }

    [Fact]
    public async Task Configured_four_by_two_queues_the_third_run_of_the_same_user()
    {
        await using var harness = await Harness.Start(new AgentRunOptions { MaxConcurrent = 4, MaxPerUser = 2 });
        var user = 21;
        var runs = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        foreach (var run in runs) harness.Enqueue(run, user);

        var started = await harness.Runs.TakeStarted(2, TimeSpan.FromSeconds(3));
        Assert.Equal(runs.Take(2).OrderBy(id => id), started.OrderBy(id => id));
        Assert.Equal(1, harness.Queue.GetPosition(runs[2]));
        await Task.Delay(100);
        Assert.DoesNotContain(runs[2], harness.Runs.StartedIds);

        harness.Runs.Complete(runs[0]);
        var third = await harness.Runs.TakeStarted(1, TimeSpan.FromSeconds(3));
        Assert.Equal(runs[2], Assert.Single(third));
    }

    [Fact]
    public async Task Global_cap_of_four_holds_the_fifth_run()
    {
        await using var harness = await Harness.Start(new AgentRunOptions { MaxConcurrent = 4, MaxPerUser = 4 });
        var runs = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        for (var i = 0; i < runs.Length; i++) harness.Enqueue(runs[i], 100 + i);

        var started = await harness.Runs.TakeStarted(4, TimeSpan.FromSeconds(3));
        Assert.Equal(4, started.Count);
        Assert.DoesNotContain(runs[4], started);
        await Task.Delay(100);
        Assert.DoesNotContain(runs[4], harness.Runs.StartedIds);

        harness.Runs.Complete(started[0]);
        var fifth = await harness.Runs.TakeStarted(1, TimeSpan.FromSeconds(3));
        Assert.Equal(runs[4], Assert.Single(fifth));
    }

    [Fact]
    public async Task Saturated_user_does_not_head_of_line_block_another_user()
    {
        await using var harness = await Harness.Start(new AgentRunOptions { MaxConcurrent = 4, MaxPerUser = 1 });
        var userA = 31;
        var userB = 32;
        var running = Guid.NewGuid();
        var queuedBehind = Guid.NewGuid();
        var otherUser = Guid.NewGuid();

        harness.Enqueue(running, userA);
        harness.Enqueue(queuedBehind, userA);
        harness.Enqueue(otherUser, userB);

        var started = await harness.Runs.TakeStarted(2, TimeSpan.FromSeconds(3));
        Assert.Equal(new[] { running, otherUser }.OrderBy(id => id), started.OrderBy(id => id));
        Assert.DoesNotContain(queuedBehind, harness.Runs.StartedIds);
        Assert.Equal(1, harness.Queue.GetPosition(queuedBehind));
    }

    [Fact]
    public async Task Failure_frees_the_slot_for_the_next_run()
    {
        await using var harness = await Harness.Start(new AgentRunOptions { MaxConcurrent = 1, MaxPerUser = 1 });
        var failing = Guid.NewGuid();
        var next = Guid.NewGuid();
        harness.Runs.Fail(failing);

        harness.Enqueue(failing, 41);
        harness.Enqueue(next, 42);

        var started = await harness.Runs.TakeStarted(2, TimeSpan.FromSeconds(3));
        Assert.Equal(new[] { failing, next }, started);
    }

    [Fact]
    public async Task Cancellation_frees_the_slot_for_the_next_run()
    {
        await using var harness = await Harness.Start(new AgentRunOptions { MaxConcurrent = 1, MaxPerUser = 1 });
        var cancelled = Guid.NewGuid();
        var next = Guid.NewGuid();
        harness.Enqueue(cancelled, 51);
        var started = await harness.Runs.TakeStarted(1, TimeSpan.FromSeconds(3));
        Assert.Equal(cancelled, Assert.Single(started));

        harness.Enqueue(next, 52);
        await Task.Delay(100);
        Assert.DoesNotContain(next, harness.Runs.StartedIds);

        harness.Runs.Cancel(cancelled);
        var followed = await harness.Runs.TakeStarted(1, TimeSpan.FromSeconds(3));
        Assert.Equal(next, Assert.Single(followed));
    }

    [Fact]
    public async Task Work_mode_runs_do_not_block_normal_chat_sessions()
    {
        await using var harness = await Harness.Start(new AgentRunOptions { MaxConcurrent = 4, MaxPerUser = 2 });
        harness.Enqueue(Guid.NewGuid(), 61);
        harness.Enqueue(Guid.NewGuid(), 62);
        await harness.Runs.TakeStarted(2, TimeSpan.FromSeconds(3));

        var gate = new ChatSessionRunGate();
        using var first = gate.Enter(ChatSessionRunGate.SessionKey(61, 1), CancellationToken.None, out var firstToken);
        using var second = gate.Enter(ChatSessionRunGate.SessionKey(62, 2), CancellationToken.None, out var secondToken);

        Assert.False(firstToken.IsCancellationRequested);
        Assert.False(secondToken.IsCancellationRequested);
        Assert.Equal(2, harness.Runs.StartedIds.Count);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required BlockingAgentRunService Runs { get; init; }
        public required AgentRunAdmissionQueue Queue { get; init; }
        public required AgentRunBackgroundService Background { get; init; }
        public required ServiceProvider Provider { get; init; }

        public static async Task<Harness> Start(AgentRunOptions options)
        {
            var runs = new BlockingAgentRunService();
            var queue = new AgentRunAdmissionQueue(options.MaxConcurrent, options.MaxPerUser);
            var services = new ServiceCollection();
            services.AddSingleton(runs);
            services.AddScoped<IAgentRunService>(sp => sp.GetRequiredService<BlockingAgentRunService>());
            var provider = services.BuildServiceProvider();
            var background = new AgentRunBackgroundService(provider, queue, NullLogger<AgentRunBackgroundService>.Instance);
            await background.StartAsync(CancellationToken.None);
            return new Harness { Runs = runs, Queue = queue, Background = background, Provider = provider };
        }

        public void Enqueue(Guid runId, int userId) => Queue.Enqueue(runId, userId);

        public async ValueTask DisposeAsync()
        {
            Runs.CompleteAll();
            await Background.StopAsync(CancellationToken.None);
            await Provider.DisposeAsync();
        }
    }

    private sealed class BlockingAgentRunService : IAgentRunService
    {
        private readonly Channel<Guid> _started = Channel.CreateUnbounded<Guid>();
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _gates = new();
        private readonly ConcurrentDictionary<Guid, byte> _fail = new();
        private readonly ConcurrentBag<Guid> _startedIds = new();

        public IReadOnlyCollection<Guid> StartedIds => _startedIds.ToArray();

        public void Fail(Guid runId) => _fail[runId] = 1;

        public void Complete(Guid runId) =>
            _gates.GetOrAdd(runId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

        public void Cancel(Guid runId) =>
            _gates.GetOrAdd(runId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetCanceled();

        public void CompleteAll()
        {
            foreach (var gate in _gates.Values) gate.TrySetResult();
        }

        public async Task<IReadOnlyList<Guid>> TakeStarted(int count, TimeSpan timeout)
        {
            var list = new List<Guid>(count);
            using var cts = new CancellationTokenSource(timeout);
            for (var i = 0; i < count; i++)
                list.Add(await _started.Reader.ReadAsync(cts.Token));
            return list;
        }

        public async Task ExecuteAsync(Guid runId, CancellationToken stoppingToken)
        {
            _startedIds.Add(runId);
            _started.Writer.TryWrite(runId);
            if (_fail.ContainsKey(runId))
                throw new InvalidOperationException("simulated run failure");

            var gate = _gates.GetOrAdd(runId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            await gate.Task.WaitAsync(stoppingToken);
        }

        public Task RecoverInterruptedRunsAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<AgentRunSnapshot> CreateAsync(int userId, int sessionId, SendMessageRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<AgentRunSnapshot> GetAsync(int userId, Guid runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgentRunEventDto>> GetEventsAsync(int userId, Guid runId, long afterSequence, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<bool> StopAsync(int userId, Guid runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ConfirmAsync(int userId, Guid runId, bool confirmed, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ResumeAsync(int userId, Guid runId, CancellationToken ct) => throw new NotSupportedException();
        public Task WaitForUpdateAsync(Guid runId, int observedSignalVersion, TimeSpan timeout, CancellationToken ct) =>
            throw new NotSupportedException();
        public int CurrentSignalVersion(Guid runId) => 0;
    }
}
