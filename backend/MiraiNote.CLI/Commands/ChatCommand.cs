using System.Text;
using MiraiNote.CLI.Agent;
using MiraiNote.CLI.Agent.Tools;
using MiraiNote.CLI.Services;
using MiraiNote.Shared.Agent;
using MiraiNote.Shared.Dtos.Agent;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace MiraiNote.CLI.Commands;

public class ChatSettings : CommandSettings
{
    [CommandOption("--session")]
    [Description("指定已有会话 ID（不指定则新建对话）")]
    public int? SessionId { get; set; }

    [CommandOption("-m|--message")]
    [Description("发送单条消息并退出（Agent/非交互模式）")]
    public string? Message { get; set; }

    [CommandOption("--json")]
    [Description("输出 JSON（与 --message 配合使用）")]
    public bool Json { get; set; } = false;

    [CommandOption("--deepseek-key")]
    [Description("DeepSeek API Key（本地回退用，当 API 服务器未配置 Key 时使用）")]
    public string? DeepSeekKey { get; set; }

    [CommandOption("--deepseek-url")]
    [Description("DeepSeek API 地址（默认 https://api.deepseek.com）")]
    public string? DeepSeekUrl { get; set; }

    [CommandOption("--deepseek-model")]
    [Description("DeepSeek 模型（默认 deepseek-v4-flash）")]
    public string? DeepSeekModel { get; set; }
}

/// <summary>
/// AI 对话命令。
/// 交互模式（默认）：支持多轮对话，/exit 退出，/new 新对话，/sessions 历史。
/// 非交互模式（--message）：发送一条消息，打印回复后退出，适合 Agent/脚本调用。
/// </summary>
public class ChatCommand : AsyncCommand<ChatSettings>
{
    private readonly ApiClient  _api;
    private readonly TokenStore _store;
    private AgentLoop? _fallbackLoop;
    private string? _deepSeekKeyOverride;
    private string? _deepSeekUrlOverride;
    private string? _deepSeekModelOverride;

    public ChatCommand(ApiClient api, TokenStore store) { _api = api; _store = store; }

    public override async Task<int> ExecuteAsync(CommandContext context, ChatSettings s)
    {
        // 缓存 CLI 提供的 DeepSeek 配置，供回退时使用
        _deepSeekKeyOverride = s.DeepSeekKey;
        _deepSeekUrlOverride = s.DeepSeekUrl;
        _deepSeekModelOverride = s.DeepSeekModel;

        bool hasLocalKey = HasAnyDeepSeekKey();

        // ── 非交互模式：--message ─────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(s.Message))
        {
            // 有 token 优先用 API（可回退到本地 Key）；无 token 但有本地 Key 直接用本地
            if (_store.HasToken)
                return await RunSingleMessageAsync(s);
            if (hasLocalKey)
                return await RunSingleMessageLocalAsync(s);
            return CommandHelpers.HandleError("未登录且未配置 DeepSeek API Key。请先执行 mirainote login 或 mirainote config --deepseek-key <key>", s.Json);
        }

        // ── 交互模式 ──────────────────────────────────────────────────
        if (!_store.HasToken)
        {
            if (hasLocalKey)
            {
                // 未登录但有本地 DeepSeek Key → 使用 Agent 模式（无会话持久化）
                AnsiConsole.MarkupLine("[bold cyan]MiraiNote AI 助理（本地模式）[/]  [grey]（未登录，不保存会话。登录后可持久化历史）[/]");
                AnsiConsole.MarkupLine("[grey]/exit 退出  /new 新对话  /auto 全自动  /verbose 详情[/]");
                AnsiConsole.Write(new Rule());
                return await RunInteractiveLocalAsync(s);
            }

            AnsiConsole.MarkupLine("[red]✗ 请先执行 [bold]mirainote login[/] 登录，或配置本地 DeepSeek Key：[bold]mirainote config --deepseek-key <key>[/][/]");
            AnsiConsole.MarkupLine("[grey]（也可直接使用 mirainote agent 命令，无需登录）[/]");
            return 1;
        }

        if (!CommandHelpers.EnsureLoggedIn(_store, s.Json)) return 1;

        // ── 交互模式 ──────────────────────────────────────────────────
        AnsiConsole.MarkupLine("[bold cyan]MiraiNote AI 助理[/]  [grey]（/exit 退出，/new 新对话，/sessions 历史会话）[/]");
        AnsiConsole.Write(new Rule());

        int sessionId = 0;
        try
        {
            if (s.SessionId.HasValue)
            {
                var detail = await _api.GetChatSessionAsync(s.SessionId.Value);
                sessionId = detail.Id;
                AnsiConsole.MarkupLine($"[grey]已切换到会话：[bold]{Markup.Escape(detail.Title)}[/] (ID={sessionId})[/]");
                foreach (var m in detail.Messages)
                    PrintMessage(m.Role, m.Content);
            }
            else
            {
                ChatSessionDto session = null!;
                await AnsiConsole.Status().StartAsync("创建对话...", async _ =>
                    session = await _api.CreateChatSessionAsync());
                sessionId = session.Id;
                AnsiConsole.MarkupLine($"[grey]新对话已创建（ID={sessionId}）[/]");
            }
        }
        catch (ApiException ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ 初始化对话失败：{ex.Message}[/]");
            return 1;
        }

        AnsiConsole.WriteLine();

        while (true)
        {
            AnsiConsole.Markup("[bold green]你：[/] ");
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(input)) continue;

            if (input is "/exit" or "/quit") { AnsiConsole.MarkupLine("[grey]对话已结束。[/]"); break; }

            if (input == "/new")
            {
                try
                {
                    ChatSessionDto session = null!;
                    await AnsiConsole.Status().StartAsync("创建新对话...", async _ =>
                        session = await _api.CreateChatSessionAsync());
                    sessionId = session.Id;
                    AnsiConsole.MarkupLine($"[grey]已开启新对话（ID={sessionId}）[/]");
                }
                catch (ApiException ex) { AnsiConsole.MarkupLine($"[red]✗ {ex.Message}[/]"); }
                continue;
            }

            if (input == "/sessions")
            {
                try
                {
                    List<ChatSessionDto> sessions = null!;
                    await AnsiConsole.Status().StartAsync("加载会话列表...", async _ =>
                        sessions = await _api.GetChatSessionsAsync());
                    var table = new Table().Border(TableBorder.Simple)
                        .AddColumn("ID").AddColumn("标题").AddColumn("最近更新");
                    foreach (var ss in sessions.Take(10))
                        table.AddRow(ss.Id.ToString(), Markup.Escape(ss.Title), ss.UpdatedAt.ToLocalTime().ToString("MM-dd HH:mm"));
                    AnsiConsole.Write(table);
                    AnsiConsole.MarkupLine("[grey]使用 /switch <ID> 切换会话[/]");
                }
                catch (ApiException ex) { AnsiConsole.MarkupLine($"[red]✗ {ex.Message}[/]"); }
                continue;
            }

            if (input.StartsWith("/switch "))
            {
                if (int.TryParse(input[8..].Trim(), out var newId))
                {
                    try
                    {
                        var detail = await _api.GetChatSessionAsync(newId);
                        sessionId = detail.Id;
                        AnsiConsole.MarkupLine($"[grey]已切换到：[bold]{Markup.Escape(detail.Title)}[/] (ID={sessionId})[/]");
                    }
                    catch (ApiException ex) { AnsiConsole.MarkupLine($"[red]✗ {ex.Message}[/]"); }
                }
                else AnsiConsole.MarkupLine("[yellow]用法：/switch <会话ID>[/]");
                continue;
            }

            try
            {
                ChatMessageDto reply = null!;
                await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                    .StartAsync("[grey]AI 思考中...[/]", async _ =>
                        reply = await _api.SendChatMessageAsync(sessionId, input));
                PrintMessage("assistant", reply.Content);
            }
            catch (ApiException ex) when (ex.Message.Contains("DeepSeek API Key") && TryGetLocalDeepSeekConfig(out var config))
            {
                AnsiConsole.MarkupLine("[grey]API 服务器 DeepSeek 未配置，使用本地 Key...[/]");
                var loop = GetOrCreateFallbackLoop(config);
                var result = await loop.RunWithPlanAsync(input, new List<AgentMessage>(), new AgentRunOptions
                {
                    EnablePlanner = false,
                    EnableReflector = false,
                    SkipConfirmation = true
                });
                PrintMessage("assistant", result.Content);
            }
            catch (ApiException ex) { AnsiConsole.MarkupLine($"[red]✗ {ex.Message}[/]"); }

            AnsiConsole.WriteLine();
        }

        return 0;
    }

    /// <summary>
    /// 非交互单次模式：复用上次会话（续聊）或新建会话，
    /// 发送一条消息，保存 sessionId 供下次复用，输出回复后退出。
    /// </summary>
    private async Task<int> RunSingleMessageAsync(ChatSettings s)
    {
        int sessionId;
        try
        {
            if (s.SessionId.HasValue)
            {
                // 用户显式指定了会话 ID
                var detail = await _api.GetChatSessionAsync(s.SessionId.Value);
                sessionId = detail.Id;
            }
            else if (_store.LastChatSessionId.HasValue)
            {
                // 复用上次的会话（续聊，除非用户要求新开）
                sessionId = _store.LastChatSessionId.Value;
            }
            else
            {
                // 首次对话，创建新会话
                var session = await _api.CreateChatSessionAsync();
                sessionId = session.Id;
            }
        }
        catch (ApiException ex)
        {
            // 上次的会话可能已被删除，回退到创建新会话
            if (!s.SessionId.HasValue && _store.LastChatSessionId.HasValue)
            {
                try
                {
                    var session = await _api.CreateChatSessionAsync();
                    sessionId = session.Id;
                }
                catch (ApiException ex2)
                {
                    return CommandHelpers.HandleError($"初始化对话失败：{ex2.Message}", s.Json);
                }
            }
            else
            {
                return CommandHelpers.HandleError($"初始化对话失败：{ex.Message}", s.Json);
            }
        }

        try
        {
            var reply = await _api.SendChatMessageAsync(sessionId, s.Message!);

            // 保存会话 ID 到本地配置，下次非交互模式自动复用
            _store.SaveChatSessionId(sessionId);

            if (s.Json)
            {
                CommandHelpers.WriteJson(new
                {
                    success   = true,
                    sessionId,
                    messageId = reply.Id,
                    content   = reply.Content
                });
            }
            else
            {
                Console.OutputEncoding = Encoding.UTF8;
                Console.WriteLine(reply.Content);
                Console.Out.Flush();
            }
            return 0;
        }
        catch (ApiException ex) when (ex.Message.Contains("DeepSeek API Key") && TryGetLocalDeepSeekConfig(out var config))
        {
            var loop = GetOrCreateFallbackLoop(config);
            var result = await loop.RunWithPlanAsync(s.Message!, new List<AgentMessage>(), new AgentRunOptions
            {
                EnablePlanner = false,
                EnableReflector = false,
                SkipConfirmation = true
            });

            _store.SaveChatSessionId(sessionId);

            if (s.Json)
            {
                CommandHelpers.WriteJson(new
                {
                    success = true,
                    sessionId,
                    content = result.Content,
                    fallback = true
                });
            }
            else
            {
                Console.OutputEncoding = Encoding.UTF8;
                Console.WriteLine(result.Content);
                Console.Out.Flush();
            }
            return 0;
        }
        catch (ApiException ex)
        {
            return CommandHelpers.HandleError(ex.Message, s.Json);
        }
    }

    private static void PrintMessage(string role, string content)
    {
        if (role == "assistant")
        {
            AnsiConsole.MarkupLine("[bold blue]AI：[/]");
            // 使用 Console.Out.Write 而非 AnsiConsole.WriteLine 避免 Spectre markup 解析
            // 并且确保 UTF-8 编码输出（PTY 和管道都能正确处理中文）
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine(content);
            Console.Out.Flush();
            AnsiConsole.Write(new Rule("[grey]─[/]"));
        }
        else if (role == "user")
        {
            AnsiConsole.MarkupLine($"[bold green]你：[/] {Markup.Escape(content)}");
        }
    }

    /// <summary>
    /// 检查是否有任何来源的 DeepSeek Key 可用（CLI 参数 > 环境变量 > TokenStore）。
    /// </summary>
    private bool HasAnyDeepSeekKey()
    {
        if (!string.IsNullOrWhiteSpace(_deepSeekKeyOverride)) return true;
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY"))) return true;
        if (!string.IsNullOrWhiteSpace(_store.DeepSeekApiKey)) return true;
        return false;
    }

    /// <summary>
    /// 非交互单次模式（本地 AgentLoop，无需登录）。
    /// </summary>
    private async Task<int> RunSingleMessageLocalAsync(ChatSettings s)
    {
        if (!TryGetLocalDeepSeekConfig(out var config))
            return CommandHelpers.HandleError("DeepSeek API Key 未配置", s.Json);

        var loop = GetOrCreateFallbackLoop(config);
        try
        {
            var result = await loop.RunWithPlanAsync(s.Message!, new List<AgentMessage>(), new AgentRunOptions
            {
                EnablePlanner = false,
                EnableReflector = false,
                SkipConfirmation = true
            });

            if (s.Json)
            {
                CommandHelpers.WriteJson(new
                {
                    success = true,
                    content = result.Content,
                    local = true
                });
            }
            else
            {
                Console.OutputEncoding = Encoding.UTF8;
                Console.WriteLine(result.Content);
                Console.Out.Flush();
            }
            return 0;
        }
        catch (Exception ex)
        {
            return CommandHelpers.HandleError(ex.Message, s.Json);
        }
    }

    /// <summary>
    /// 交互模式（本地 AgentLoop，无需登录，无会话持久化）。
    /// </summary>
    private async Task<int> RunInteractiveLocalAsync(ChatSettings s)
    {
        if (!TryGetLocalDeepSeekConfig(out var config))
            return CommandHelpers.HandleError("DeepSeek API Key 未配置", s.Json);

        var loop = GetOrCreateFallbackLoop(config);
        var history = new List<AgentMessage>();
        bool verbose = false;
        bool autoMode = false;

        AnsiConsole.WriteLine();

        while (true)
        {
            AnsiConsole.Markup("[bold green]你：[/] ");
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(input)) continue;

            if (input is "/exit" or "/quit") { AnsiConsole.MarkupLine("[grey]对话已结束。[/]"); break; }

            if (input == "/new") { history.Clear(); AnsiConsole.MarkupLine("[grey]已开启新对话。[/]"); continue; }

            if (input == "/verbose") { verbose = !verbose; AnsiConsole.MarkupLine($"[grey]详细输出：{(verbose ? "开启" : "关闭")}[/]"); continue; }

            if (input == "/auto") { autoMode = !autoMode; AnsiConsole.MarkupLine($"[grey]全自动模式：{(autoMode ? "开启" : "关闭")}[/]"); continue; }

            try
            {
                AnsiConsole.MarkupLine("[bold blue]AI：[/]");

                var result = await loop.RunWithPlanAsync(input, history, new AgentRunOptions
                {
                    EnablePlanner = false,
                    EnableReflector = false,
                    SkipConfirmation = autoMode
                }, onToken: verbose ? async token =>
                {
                    Console.OutputEncoding = Encoding.UTF8;
                    Console.Write(token);
                    await Console.Out.FlushAsync();
                } : null);

                history.Add(new AgentMessage("user", input));
                history.Add(new AgentMessage("assistant", result.Content));

                Console.OutputEncoding = Encoding.UTF8;
                if (!verbose) Console.WriteLine(result.Content);
                Console.Out.Flush();
                AnsiConsole.Write(new Rule("[grey]─[/]"));
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(ex.Message)}[/]");
            }

            AnsiConsole.WriteLine();
        }

        return 0;
    }

    /// <summary>
    /// 尝试获取本地 DeepSeek 配置（优先级：CLI 参数 > 环境变量 > TokenStore）。
    /// 同时读取 Tavily 和 SMTP 配置（仅从环境变量）。
    /// </summary>
    private bool TryGetLocalDeepSeekConfig(out AgentConfig config)
    {
        var apiKey = _deepSeekKeyOverride
            ?? Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")
            ?? (_store.DeepSeekApiKey is { Length: > 0 } k ? k : null);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            config = new AgentConfig();
            return false;
        }

        config = new AgentConfig
        {
            DeepSeekApiKey = apiKey,
            DeepSeekBaseUrl = _deepSeekUrlOverride
                ?? Environment.GetEnvironmentVariable("DEEPSEEK_BASE_URL")
                ?? (_store.DeepSeekBaseUrl is { Length: > 0 } u ? u : null)
                ?? "https://api.deepseek.com",
            DeepSeekModel = _deepSeekModelOverride
                ?? Environment.GetEnvironmentVariable("DEEPSEEK_MODEL")
                ?? (_store.DeepSeekModel is { Length: > 0 } m ? m : null)
                ?? "deepseek-v4-flash",

            // Tavily 互联网搜索 Key（环境变量 > TokenStore）
            TavilyApiKey = Environment.GetEnvironmentVariable("TAVILY_API_KEY")
                ?? _store.TavilyApiKey,

            // SMTP 邮件配置（环境变量 > TokenStore）
            SmtpHost = Environment.GetEnvironmentVariable("SMTP_HOST")
                ?? _store.SmtpHost,
            SmtpPort = int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT"), out var port) ? port
                : _store.SmtpPort,
            SmtpUser = Environment.GetEnvironmentVariable("SMTP_USER")
                ?? _store.SmtpUser,
            SmtpPassword = Environment.GetEnvironmentVariable("SMTP_PASSWORD")
                ?? _store.SmtpPassword,
            SmtpFromAddress = Environment.GetEnvironmentVariable("SMTP_FROM_ADDRESS")
                ?? _store.SmtpFromAddress,
            SmtpFromName = Environment.GetEnvironmentVariable("SMTP_FROM_NAME")
                ?? _store.SmtpFromName
                ?? "MiraiNote"
        };
        return true;
    }

    /// <summary>
    /// 获取或创建本地 AgentLoop（用于 API 服务器 DeepSeek 未配置时的回退）。
    /// 缓存以避免重复创建工具注册表。
    /// </summary>
    private AgentLoop GetOrCreateFallbackLoop(AgentConfig config)
    {
        if (_fallbackLoop != null) return _fallbackLoop;

        var registry = new MiraiNote.Shared.Agent.AgentToolRegistry();

        // API 代理工具（如果有 token）
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
            registry.Register(new RememberTool(_api));
            registry.Register(new RecallTool(_api));
            registry.Register(new ForgetTool(_api));
        }

        // 本地工具
        registry.Register(new InternetSearchTool(config.TavilyApiKey));
        registry.Register(new FileReadTool());
        registry.Register(new FileWriteTool());
        registry.Register(new FileListTool());
        registry.Register(new ShellTool());
        registry.Register(new SystemInfoTool());
        registry.Register(new SendEmailTool(
            config.SmtpHost, config.SmtpPort,
            config.SmtpUser, config.SmtpPassword,
            config.SmtpFromAddress, config.SmtpFromName));

        var display = new AgentDisplay(verbose: false);
        _fallbackLoop = new AgentLoop(config, registry, display, _store);
        return _fallbackLoop;
    }
}
