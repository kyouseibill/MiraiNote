using Microsoft.Extensions.Options;
using MiraiNote.Shared.Agent;
using MiraiNote.Shared.Dtos.Agent;

namespace MiraiNote.Core.Services;

/// <summary>
/// 后端 Agent 反思器。基于 Shared 层 DeepSeekReflector 实现，
/// 通过 IOptions 注入配置。
/// </summary>
public interface IAgentReflectorService
{
    Task<ReflectionResult?> ReflectAsync(
        string userMessage,
        string assistantResponse,
        int toolCallsCount,
        CancellationToken ct = default,
        string? evaluationContext = null);
}

public class AgentReflectorService : IAgentReflectorService
{
    private readonly DeepSeekReflector _reflector;

    public AgentReflectorService(IOptions<DeepSeekOptions> options, IHttpClientFactory httpClientFactory)
    {
        var opt = options.Value;
        var client = httpClientFactory.CreateClient("DeepSeek");
        _reflector = new DeepSeekReflector(new DeepSeekConnection
        {
            ApiKey = opt.ApiKey,
            BaseUrl = opt.BaseUrl,
            Model = opt.Model
        }, client);
    }

    public async Task<ReflectionResult?> ReflectAsync(
        string userMessage,
        string assistantResponse,
        int toolCallsCount,
        CancellationToken ct = default,
        string? evaluationContext = null)
    {
        return await _reflector.ReflectAsync(userMessage, assistantResponse, toolCallsCount, ct, evaluationContext);
    }
}
