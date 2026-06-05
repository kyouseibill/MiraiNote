using MiraiNote.CLI.Services;
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
    public ChatCommand(ApiClient api, TokenStore store) { _api = api; _store = store; }

    public override async Task<int> ExecuteAsync(CommandContext context, ChatSettings s)
    {
        if (!CommandHelpers.EnsureLoggedIn(_store, s.Json)) return 1;

        // ── 非交互模式：--message ─────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(s.Message))
            return await RunSingleMessageAsync(s);

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
            catch (ApiException ex) { AnsiConsole.MarkupLine($"[red]✗ {ex.Message}[/]"); }

            AnsiConsole.WriteLine();
        }

        return 0;
    }

    /// <summary>非交互单次模式：建立（或复用）会话，发送一条消息，输出回复后退出。</summary>
    private async Task<int> RunSingleMessageAsync(ChatSettings s)
    {
        int sessionId;
        try
        {
            if (s.SessionId.HasValue)
            {
                var detail = await _api.GetChatSessionAsync(s.SessionId.Value);
                sessionId = detail.Id;
            }
            else
            {
                var session = await _api.CreateChatSessionAsync();
                sessionId = session.Id;
            }
        }
        catch (ApiException ex)
        {
            return CommandHelpers.HandleError($"初始化对话失败：{ex.Message}", s.Json);
        }

        try
        {
            var reply = await _api.SendChatMessageAsync(sessionId, s.Message!);

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
                Console.WriteLine(reply.Content);
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
            AnsiConsole.WriteLine(content);
            AnsiConsole.Write(new Rule("[grey]─[/]"));
        }
        else if (role == "user")
        {
            AnsiConsole.MarkupLine($"[bold green]你：[/] {Markup.Escape(content)}");
        }
    }
}

