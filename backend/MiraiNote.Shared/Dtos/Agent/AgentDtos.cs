namespace MiraiNote.Shared.Dtos.Agent;

// ===== Planner Models =====

/// <summary>
/// 执行计划。
/// </summary>
public class ExecutionPlan
{
    public string Goal { get; set; } = "";
    public List<PlanStep> Steps { get; set; } = new();
    public List<string> Risks { get; set; } = new();
    public bool IsTrivial { get; set; }

    public override string ToString()
    {
        if (Steps.Count == 0) return "（无计划）";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"目标：{Goal}");
        for (int i = 0; i < Steps.Count; i++)
            sb.AppendLine($"  {i + 1}. {Steps[i].Action}");
        if (Risks.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("风险：");
            foreach (var r in Risks) sb.AppendLine($"  ⚠ {r}");
        }
        return sb.ToString();
    }
}

public class PlanStep
{
    public int Order { get; set; }
    public string Action { get; set; } = "";
    public string[] Tools { get; set; } = Array.Empty<string>();
    public string ExpectedOutput { get; set; } = "";
}

// ===== Reflector Models =====

/// <summary>
/// 反思结果。
/// </summary>
public class ReflectionResult
{
    public bool IsComplete { get; set; }
    public int Score { get; set; }
    public string[] Strengths { get; set; } = Array.Empty<string>();
    public string[] Issues { get; set; } = Array.Empty<string>();
    public string[] Suggestions { get; set; } = Array.Empty<string>();
    public bool NeedsFollowUp { get; set; }
    public string? FollowUpAction { get; set; }

    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        var icon = IsComplete ? "✓" : "✗";
        sb.AppendLine($"{icon} 目标达成：{(IsComplete ? "是" : "否")}  自评：{Score}/10");

        if (Strengths.Length > 0)
        {
            sb.Append("  优点：");
            sb.AppendLine(string.Join("、", Strengths));
        }
        if (Issues.Length > 0)
        {
            sb.Append("  ⚠ 问题：");
            sb.AppendLine(string.Join("、", Issues));
        }
        if (Suggestions.Length > 0)
        {
            sb.Append("  💡 建议：");
            sb.AppendLine(string.Join("、", Suggestions));
        }
        if (NeedsFollowUp && !string.IsNullOrWhiteSpace(FollowUpAction))
            sb.AppendLine($"  → 将自动补充：{FollowUpAction}");

        return sb.ToString();
    }
}

// ===== Context Models =====

/// <summary>
/// 上下文用量信息。
/// </summary>
public class ContextUsage
{
    public int EstimatedTokens { get; set; }
    public int MaxTokens { get; set; }
    public double PercentUsed { get; set; }
    public int MessageCount { get; set; }
    public bool NeedsCompaction { get; set; }

    public override string ToString()
        => $"📊 上下文用量：{EstimatedTokens:N0}/{MaxTokens:N0} tokens ({PercentUsed}%)，{MessageCount} 条消息{(NeedsCompaction ? " ⚠ 需要压缩" : "")}";
}

// ===== Memory DTOs =====

public class AgentMemoryDto
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string Category { get; set; } = "context";
    public string? Tags { get; set; }
    public byte Importance { get; set; } = 3;
    public int AccessedCount { get; set; }
    public string? Source { get; set; }
    public DateTime LastAccessedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateMemoryRequest
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string Category { get; set; } = "context";
    public string? Tags { get; set; }
    public byte Importance { get; set; } = 3;
    public string? Source { get; set; }
}

public class UpdateMemoryRequest
{
    public string? Value { get; set; }
    public string? Category { get; set; }
    public string? Tags { get; set; }
    public byte? Importance { get; set; }
}

/// <summary>
/// 带相关性说明的记忆 DTO。用于语义匹配返回结果。
/// </summary>
public class RelevantMemoryDto : AgentMemoryDto
{
    /// <summary>为什么这条记忆与当前查询相关</summary>
    public string? Relevance { get; set; }
}

// ===== Agent SSE Event Models =====

/// <summary>
/// SSE plan 事件数据。
/// </summary>
public class AgentPlanEvent
{
    public string Goal { get; set; } = "";
    public List<PlanStep> Steps { get; set; } = new();
    public List<string> Risks { get; set; } = new();
}

/// <summary>
/// SSE reflection 事件数据。
/// </summary>
public class AgentReflectionEvent
{
    public bool IsComplete { get; set; }
    public int Score { get; set; }
    public string[] Strengths { get; set; } = Array.Empty<string>();
    public string[] Issues { get; set; } = Array.Empty<string>();
    public string[] Suggestions { get; set; } = Array.Empty<string>();
}

/// <summary>
/// SSE confirm 事件数据。
/// </summary>
public class AgentConfirmEvent
{
    public string ToolName { get; set; } = "";
    public string RiskLevel { get; set; } = "write";
    public string Arguments { get; set; } = "";
}

/// <summary>
/// 前端确认响应。
/// </summary>
public class AgentConfirmResponse
{
    public bool Confirmed { get; set; }
}

// ===== Tool Call Models =====

/// <summary>
/// 流式解析中的 tool_call delta 信息。
/// </summary>
public class ToolCallDelta
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "function";
    public string FunctionName { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
}

/// <summary>
/// 工具调用信息（SSE 解析用）。
/// </summary>
public class ToolCallInfo
{
    public string Id { get; set; } = string.Empty;
    public string FunctionName { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
}

// ===== Message Models =====

/// <summary>
/// Agent 通用消息模型。
/// </summary>
public class AgentMessage
{
    public string Role { get; init; } = "user";
    public string Content { get; init; } = "";

    public AgentMessage() { }
    public AgentMessage(string role, string content) { Role = role; Content = content; }
}
