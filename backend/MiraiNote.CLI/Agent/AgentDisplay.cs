using System.Text;
using Spectre.Console;

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
        var attemptSuffix = attempt > 1 ? $" [grey](重试 {attempt})[/]" : "";
        AnsiConsole.MarkupLine($"[cyan]│[/] [bold yellow]⚙ {toolName}[/]{attemptSuffix}");
        if (_verbose)
        {
            var compactArgs = arguments.Length > 300 ? arguments[..300] + "..." : arguments;
            AnsiConsole.MarkupLine($"[grey]│   入参: {Markup.Escape(compactArgs)}[/]");
        }
    }

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
}
