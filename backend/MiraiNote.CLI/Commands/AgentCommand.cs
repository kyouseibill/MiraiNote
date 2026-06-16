using System.ComponentModel;
using System.Text;
using System.Text.Json;
using MiraiNote.CLI.Agent;
using MiraiNote.CLI.Agent.Tools;
using MiraiNote.CLI.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MiraiNote.CLI.Commands;

public class AgentSettings : CommandSettings
{
    [CommandOption("-m|--message")]
    [Description("发送一条任务消息并退出（非交互/Agent 模式）")]
    public string? Message { get; set; }

    [CommandOption("--json")]
    [Description("以 JSON 格式输出结果（与 --message 配合使用）")]
    public bool Json { get; set; } = false;

    [CommandOption("--verbose")]
    [Description("显示 Agent 思考过程和工具调用详情")]
    public bool Verbose { get; set; } = false;

    [CommandOption("--plan")]
    [Description("先展示执行计划，用户确认后再执行（交互模式默认启用）")]
    public bool? ShowPlan { get; set; }

    [CommandOption("--no-reflect")]
    [Description("禁用任务完成后的自我反思")]
    public bool NoReflect { get; set; } = false;

    [CommandOption("--auto")]
    [Description("跳过规划和确认，直接执行（全自动模式）")]
    public bool Auto { get; set; } = false;

    [CommandOption("--model")]
    [Description("指定 DeepSeek 模型（默认 deepseek-chat）")]
    public string? Model { get; set; }

    [CommandOption("--max-rounds")]
    [Description("最大工具调用轮次（默认 12）")]
    public int? MaxRounds { get; set; }

    [CommandOption("--deepseek-key")]
    [Description("DeepSeek API Key（优先级：此参数 > DEEPSEEK_API_KEY 环境变量）")]
    public string? DeepSeekKey { get; set; }

    [CommandOption("--deepseek-url")]
    [Description("DeepSeek API 地址（默认 https://api.deepseek.com）")]
    public string? DeepSeekUrl { get; set; }

    [CommandOption("--tavily-key")]
    [Description("Tavily API Key（互联网搜索用）")]
    public string? TavilyKey { get; set; }
}

/// <summary>
/// MiraiNote Agent 命令 v2 —— 功能强大的 AI Agent。
/// 集成 Planner、Reflector、Guard 确认、上下文管理。
///
/// 交互模式（默认）：多轮对话，/exit 退出，/new 新任务，/verbose 切换详细输出，
///                  /plan 查看计划, /auto 全自动模式, /context 查看上下文用量。
/// 非交互模式（--message）：执行单次任务后退出。
/// </summary>
public class AgentCommand : AsyncCommand<AgentSettings>
{
    private readonly ApiClient _api;
    private readonly TokenStore _store;

    public AgentCommand(ApiClient api, TokenStore store)
    {
        _api = api;
        _store = store;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, AgentSettings s)
    {
        // ── 单次任务模式（--message）─────────────────────
        if (!string.IsNullOrWhiteSpace(s.Message))
            return await RunSingleTaskAsync(s);

        // ── 交互模式 ──────────────────────────────────
        var config = BuildConfig(s);
        var display = new AgentDisplay(verbose: false);
        var loop = BuildLoop(config, display);

        display.ShowWelcome();
        AnsiConsole.MarkupLine("[grey]  /plan 查看计划  /auto 全自动  /verbose 详情  /context 用量  /exit 退出[/]");
        Console.WriteLine();

        var history = new List<AgentMessage>();
        bool verbose = s.Verbose;
        bool enablePlan = true;
        bool enableReflect = true;
        bool skipConfirm = false;

        while (true)
        {
            AnsiConsole.Markup("[bold green]你：[/] ");
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(input)) continue;

            // 元命令
            if (input is "/exit" or "/quit")
            {
                AnsiConsole.MarkupLine("[grey]Agent 已退出。[/]");
                break;
            }

            if (input == "/new")
            {
                history.Clear();
                AnsiConsole.MarkupLine("[grey]已开启新任务。[/]");
                continue;
            }

            if (input == "/verbose")
            {
                verbose = !verbose;
                display = new AgentDisplay(verbose);
                loop = BuildLoop(config, display);
                AnsiConsole.MarkupLine($"[grey]详细输出：{(verbose ? "开启" : "关闭")}[/]");
                continue;
            }

            if (input == "/plan")
            {
                enablePlan = !enablePlan;
                AnsiConsole.MarkupLine($"[grey]执行计划：{(enablePlan ? "开启" : "关闭")}[/]");
                continue;
            }

            if (input == "/reflect")
            {
                enableReflect = !enableReflect;
                AnsiConsole.MarkupLine($"[grey]自我反思：{(enableReflect ? "开启" : "关闭")}[/]");
                continue;
            }

            if (input == "/auto")
            {
                skipConfirm = !skipConfirm;
                AnsiConsole.MarkupLine($"[grey]全自动模式：{(skipConfirm ? "开启（跳过确认）" : "关闭（需确认）")}[/]");
                continue;
            }

            if (input == "/context")
            {
                var ctxMgr = new AgentContextManager();
                var usage = ctxMgr.GetUsage(history, 15);
                AnsiConsole.MarkupLine($"[grey]{usage}[/]");
                continue;
            }

            if (input == "/history")
            {
                AnsiConsole.MarkupLine($"[grey]历史消息：{history.Count} 条[/]");
                continue;
            }

            try
            {
                var options = new AgentRunOptions
                {
                    EnablePlanner = enablePlan,
                    EnableReflector = enableReflect,
                    SkipConfirmation = skipConfirm,
                    ConfirmCallback = (risk, toolName, args) =>
                        Task.FromResult(display.RequestConfirmation(risk, toolName, args))
                };

                var result = await loop.RunWithPlanAsync(
                    input, history, options, onToken: null, CancellationToken.None);

                display.ShowResponse(result.Content);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]✗ Agent 错误：{Markup.Escape(ex.Message)}[/]");
            }
        }

        return 0;
    }

    /// <summary>
    /// 非交互单次任务模式。
    /// </summary>
    private async Task<int> RunSingleTaskAsync(AgentSettings s)
    {
        var config = BuildConfig(s);
        var display = new AgentDisplay(s.Verbose);
        var loop = BuildLoop(config, display);

        var history = new List<AgentMessage>();
        var options = new AgentRunOptions
        {
            EnablePlanner = s.ShowPlan ?? !s.Auto,
            EnableReflector = !s.NoReflect,
            SkipConfirmation = s.Auto || s.Json,
            ConfirmCallback = s.Json ? null : (risk, toolName, args) =>
                Task.FromResult(display.RequestConfirmation(risk, toolName, args))
        };

        try
        {
            var result = await loop.RunWithPlanAsync(
                s.Message!, history, options,
                onToken: s.Json ? null : async token =>
                {
                    Console.OutputEncoding = Encoding.UTF8;
                    Console.Write(token);
                    await Console.Out.FlushAsync();
                },
                CancellationToken.None);

            if (s.Json)
            {
                CommandHelpers.WriteJson(new
                {
                    success = true,
                    content = result.Content,
                    toolCalls = result.ToolCallsCount,
                    plan = result.Plan?.Goal,
                    reflection = result.Reflection != null ? new
                    {
                        score = result.Reflection.Score,
                        isComplete = result.Reflection.IsComplete,
                        issues = result.Reflection.Issues,
                        suggestions = result.Reflection.Suggestions
                    } : null
                });
            }
            else if (!s.Verbose)
            {
                Console.OutputEncoding = Encoding.UTF8;
                Console.WriteLine(result.Content);
                Console.Out.Flush();
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (s.Json)
                CommandHelpers.WriteJson(new { success = false, error = ex.Message });
            else
                AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    /// <summary>
    /// 组装 AgentLoop（包含工具注册）。
    /// </summary>
    private AgentLoop BuildLoop(AgentConfig config, AgentDisplay display)
    {
        var registry = new AgentToolRegistry();

        // ── API 代理工具 ──
        if (_store.HasToken)
        {
            registry.Register(new SearchWorkLogsTool(_api));
            registry.Register(new SearchMemosTool(_api));
            registry.Register(new SearchLifeLogsTool(_api));
            registry.Register(new GetWeeklyReportsTool(_api));
            registry.Register(new GenerateWeeklyReportTool(_api));
            registry.Register(new CreateWorkLogTool(_api));
            registry.Register(new CreateMemoTool(_api));
            registry.Register(new PatchMemoStatusTool(_api));
        }

        // ── 本地工具 ──
        registry.Register(new InternetSearchTool(config.TavilyApiKey));
        registry.Register(new FileReadTool());
        registry.Register(new FileWriteTool());
        registry.Register(new FileListTool());
        registry.Register(new ShellTool());
        registry.Register(new SystemInfoTool());

        return new AgentLoop(config, registry, display, _store);
    }

    /// <summary>
    /// 构建 AgentConfig（优先级：CLI 参数 > 环境变量 > 默认值）。
    /// </summary>
    private static AgentConfig BuildConfig(AgentSettings s) => new()
    {
        DeepSeekApiKey = s.DeepSeekKey
            ?? Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")
            ?? string.Empty,
        DeepSeekBaseUrl = s.DeepSeekUrl
            ?? Environment.GetEnvironmentVariable("DEEPSEEK_BASE_URL")
            ?? "https://api.deepseek.com",
        DeepSeekModel = s.Model
            ?? Environment.GetEnvironmentVariable("DEEPSEEK_MODEL")
            ?? "deepseek-chat",
        TavilyApiKey = s.TavilyKey
            ?? Environment.GetEnvironmentVariable("TAVILY_API_KEY"),
        MaxToolRounds = s.MaxRounds ?? 12
    };
}
