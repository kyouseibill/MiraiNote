using System.Text.Json;
using MiraiNote.Shared.Agent;
using MiraiNote.Shared.Common;

namespace MiraiNote.Core.Services.Tools;

/// <summary>Progressively loads a user-owned workflow after the model matches its metadata.</summary>
public sealed class ServerLoadSkillTool : IServerAgentTool
{
    private readonly IFileSkillService _skills;

    public ServerLoadSkillTool(IFileSkillService skills) => _skills = skills;

    public string Name => "load_skill";
    public string Description => "读取已启用且允许自动调用的用户 Skill 完整工作流程。先根据系统提示中的名称和描述选择，不要猜测名称。";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Safe;
    public ToolParameterSchema Parameters => new()
    {
        Properties = new() { ["name"] = ToolParameterProperty.String("Skill 名称，例如 yahoo-transit-jp") },
        Required = new() { "name" }
    };

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default) =>
        ExecuteAsync(0, argumentsJson, ct);

    public Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            if (!ToolArgHelper.TryGetString(document.RootElement, "name", out var name))
                return Task.FromResult("Skill 加载失败：name 为必填项。");
            return Task.FromResult(_skills.Load(userId, name));
        }
        catch (JsonException)
        {
            return Task.FromResult("Skill 加载失败：参数不是有效 JSON。");
        }
        catch (BusinessException ex)
        {
            return Task.FromResult($"Skill 不可用：{ex.Message}");
        }
    }
}
