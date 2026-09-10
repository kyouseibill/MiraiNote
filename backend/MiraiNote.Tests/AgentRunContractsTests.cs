using MiraiNote.Core.Services.AgentRuns;
using Xunit;

namespace MiraiNote.Tests;

public class AgentRunContractsTests
{
    [Theory]
    [InlineData(AgentRunStatus.Queued, AgentRunStatus.Running, true)]
    [InlineData(AgentRunStatus.Running, AgentRunStatus.Completed, true)]
    [InlineData(AgentRunStatus.Running, AgentRunStatus.Recoverable, true)]
    [InlineData(AgentRunStatus.Completed, AgentRunStatus.Running, false)]
    [InlineData(AgentRunStatus.Stopped, AgentRunStatus.Running, false)]
    public void Transition_rules_protect_terminal_runs(string from, string to, bool expected)
    {
        Assert.Equal(expected, AgentRunState.CanTransition(from, to));
    }

    [Fact]
    public void A_restart_only_recovers_nonterminal_runs()
    {
        Assert.True(AgentRunState.ShouldRecoverOnStartup(AgentRunStatus.Queued));
        Assert.True(AgentRunState.ShouldRecoverOnStartup(AgentRunStatus.Running));
        Assert.True(AgentRunState.ShouldRecoverOnStartup(AgentRunStatus.AwaitingConfirmation));
        Assert.False(AgentRunState.ShouldRecoverOnStartup(AgentRunStatus.Completed));
        Assert.False(AgentRunState.ShouldRecoverOnStartup(AgentRunStatus.Failed));
        Assert.False(AgentRunState.ShouldRecoverOnStartup(AgentRunStatus.Stopped));
    }
}
