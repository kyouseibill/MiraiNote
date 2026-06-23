using Microsoft.Extensions.Options;
using MiraiNote.Shared.Agent;
using MiraiNote.Shared.Dtos.Agent;

namespace MiraiNote.Core.Services;

/// <summary>
/// 后端 Agent 规划器。基于 Shared 层 DeepSeekPlanner 实现，
/// 通过 IOptions 注入配置。
/// </summary>
public interface IAgentPlannerService
{
    Task<ExecutionPlan?> GeneratePlanAsync(string userMessage, List<string> availableTools, CancellationToken ct = default);
}

public class AgentPlannerService : IAgentPlannerService
{
    private readonly DeepSeekPlanner _planner;

    public AgentPlannerService(IOptions<DeepSeekOptions> options, IHttpClientFactory httpClientFactory)
    {
        var opt = options.Value;
        var client = httpClientFactory.CreateClient("DeepSeek");
        _planner = new DeepSeekPlanner(new DeepSeekConnection
        {
            ApiKey = opt.ApiKey,
            BaseUrl = opt.BaseUrl,
            Model = opt.Model
        }, client);
    }

    public async Task<ExecutionPlan?> GeneratePlanAsync(
        string userMessage, List<string> availableTools, CancellationToken ct = default)
    {
        return await _planner.GeneratePlanAsync(userMessage, availableTools, ct: ct);
    }
}
