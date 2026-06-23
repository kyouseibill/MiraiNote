using System.Text;
using Spectre.Console;
using MiraiNote.Shared.Agent;
using MiraiNote.Shared.Dtos.Agent;

namespace MiraiNote.CLI.Agent;

/// <summary>
/// Agent 过程的 Spectre.Console 可视化渲染。
/// </summary>
public class AgentDisplay
{
    private readonly bool _verbose;
    private int _toolCallSeq;

    public AgentDisplay(bool verbose = false)
    {
        _verbose = verbose;
    }

    /// <summary>Agent 开始思考</summary>
    public void BeginThinking()
    {
        if (!_verbose) return;
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]╭─ Agent 思考中 ──────────────────────────────[/]");
    }

    /// <summary>显示 LLM 的推理文本（如果有的话）</summary>
    public void ShowThought(string thought)
    {
        if (!_verbose || string.IsNullOrWhiteSpace(thought)) return;
        AnsiConsole.MarkupLine($"[grey]│ {Markup.Escape(thought.Trim())}[/]");
    }

    /// <summary>显示工具调用</summary>
    public void ShowToolCall(string toolName, string arguments, int attempt = 1)
    {
        _toolCallSeq++;
        var label = GetToolLabel(toolName);
        var attemptSuffix = attempt > 1 ? $" [grey](重试 {attempt})[/]" : "";
        AnsiConsole.MarkupLine($"[cyan]│[/] [bold yellow]⚙ {label}[/]{attemptSuffix}");
        if (_verbose)
        {
            var compactArgs = arguments.Length > 300 ? arguments[..300] + "..." : arguments;
            AnsiConsole.MarkupLine($"[grey]│   入参: {Markup.Escape(compactArgs)}[/]");
        }
    }

    private static string GetToolLabel(string name) => name switch
    {
        "search_work_logs" => "查询工作记录",
        "search_memos" => "查询备忘",
        "search_life_logs" => "查询生活记录",
        "get_weekly_reports" => "获取周报",
        "generate_weekly_report" => "生成周报",
        "search_internet" => "搜索互联网",
        "create_work_log" => "创建工作记录",
        "update_work_log" => "更新工作记录",
        "delete_work_log" => "删除工作记录",
        "create_memo" => "创建备忘",
        "update_memo" => "更新备忘",
        "patch_memo_status" => "更新备忘状态",
        "delete_memo" => "删除备忘",
        "create_life_log" => "创建生活记录",
        "update_life_log" => "更新生活记录",
        "delete_life_log" => "删除生活记录",
        "remember" => "存储记忆",
        "recall" => "检索记忆",
        "forget" => "删除记忆",
        "read_file" => "读取文件",
        "write_file" => "写入文件",
        "list_files" => "浏览目录",
        "run_shell" => "执行命令",
        "system_info" => "系统信息",
        "send_email" => "发送邮件",
        _ => name,
    };

    /// <summary>显示工具返回结果</summary>
    public void ShowToolResult(string result)
    {
        if (!_verbose) return;
        var compact = result.Length > 400 ? result[..400] + "..." : result;
        AnsiConsole.MarkupLine($"[grey]│   结果: {Markup.Escape(compact)}[/]");
    }

    /// <summary>显示思考阶段结束</summary>
    public void EndThinking()
    {
        if (!_verbose) return;
        AnsiConsole.MarkupLine("[dim]╰──────────────────────────────────────────────[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>输出 AI 最终回复</summary>
    public void ShowResponse(string content)
    {
        // 使用 Console.Out.Write 确保 UTF-8 正确输出，避免 Spectre markup 解析
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine();
        AnsiConsole.Write(new Rule("[bold blue]Mirai[/]"));
        Console.WriteLine(content);
        Console.Out.Flush();
        AnsiConsole.Write(new Rule("[grey]─[/]"));
    }

    /// <summary>显示错误</summary>
    public void ShowError(string message)
    {
        AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(message)}[/]");
    }

    /// <summary>显示欢迎横幅</summary>
    public void ShowWelcome()
    {
        AnsiConsole.MarkupLine("[bold cyan]MiraiNote Agent[/]  [grey]（/exit 退出，/new 新任务，/verbose 切换详细输出）[/]");
        AnsiConsole.Write(new Rule());
    }

    /// <summary>显示非交互模式的进度提示</summary>
    public void ShowProgress(string message)
    {
        if (_verbose)
            AnsiConsole.MarkupLine($"[grey]⏳ {Markup.Escape(message)}[/]");
    }

    /// <summary>显示执行计划</summary>
    public void ShowPlan(ExecutionPlan plan)
    {
        AnsiConsole.WriteLine();
        var panel = new Panel(
            string.Join("\n", plan.Steps.Select((s, i) =>
                $"  [bold]{i + 1}.[/] {Markup.Escape(s.Action)}  [grey]({string.Join(", ", s.Tools)})[/]")))
        {
            Header = new PanelHeader($"[bold cyan]📋 执行计划：{Markup.Escape(plan.Goal)}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Cyan1)
        };
        AnsiConsole.Write(panel);

        if (plan.Risks.Count > 0)
        {
            AnsiConsole.MarkupLine("[yellow]⚠ 风险提示：[/]");
            foreach (var r in plan.Risks)
                AnsiConsole.MarkupLine($"  [yellow]• {Markup.Escape(r)}[/]");
        }

        AnsiConsole.WriteLine();
    }

    /// <summary>显示反思结果</summary>
    public void ShowReflection(ReflectionResult reflection)
    {
        var icon = reflection.IsComplete ? "[green]✓[/]" : "[red]✗[/]";
        var scoreColor = reflection.Score >= 8 ? "green" : reflection.Score >= 5 ? "yellow" : "red";

        var lines = new List<string>
        {
            $"{icon} 目标达成：{(reflection.IsComplete ? "是" : "否")}  自评：[{scoreColor}]{reflection.Score}/10[/]"
        };

        if (reflection.Strengths.Length > 0)
            lines.Add($"[green]✓[/] 优点：{Markup.Escape(string.Join("、", reflection.Strengths))}");

        if (reflection.Issues.Length > 0)
            lines.Add($"[yellow]⚠[/] 问题：{Markup.Escape(string.Join("、", reflection.Issues))}");

        if (reflection.Suggestions.Length > 0)
            lines.Add($"[blue]💡[/] 建议：{Markup.Escape(string.Join("、", reflection.Suggestions))}");

        if (reflection.NeedsFollowUp && !string.IsNullOrWhiteSpace(reflection.FollowUpAction))
            lines.Add($"[cyan]→[/] 自动补充：{Markup.Escape(reflection.FollowUpAction)}");

        var panel = new Panel(string.Join("\n", lines))
        {
            Header = new PanelHeader("[bold magenta]🔍 自我反思[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Magenta1)
        };
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>请求用户确认危险操作</summary>
    public bool RequestConfirmation(ToolRiskLevel riskLevel, string toolName, string arguments)
    {
        if (riskLevel == ToolRiskLevel.Safe) return true;

        var color = riskLevel == ToolRiskLevel.Dangerous ? "red" : "yellow";
        var label = riskLevel == ToolRiskLevel.Dangerous ? "⚠ 危险操作" : "📝 写入操作";

        AnsiConsole.MarkupLine($"[{color}]{label}：{Markup.Escape(toolName)}[/]");
        if (_verbose)
        {
            var compact = arguments.Length > 200 ? arguments[..200] + "..." : arguments;
            AnsiConsole.MarkupLine($"[grey]   参数: {Markup.Escape(compact)}[/]");
        }

        return AnsiConsole.Confirm($"[{color}]确认执行此操作？[/]", false);
    }
}
