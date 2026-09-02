using MiraiNote.CLI.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace MiraiNote.CLI.Commands;

// ===== lifelog list =====

public class LifeLogListSettings : CommandSettings
{
    [CommandOption("-k|--keyword")]
    [Description("关键词筛选")]
    public string? Keyword { get; set; }

    [CommandOption("--mood")]
    [Description("心情筛选（如：开心/平静/疲惫）")]
    public string? Mood { get; set; }

    [CommandOption("--month")]
    [Description("按月筛选，格式 yyyy-MM")]
    public string? Month { get; set; }

    [CommandOption("-n|--page-size")]
    public int PageSize { get; set; } = 20;

    [CommandOption("--json")]
    [Description("输出 JSON（Agent 友好模式）")]
    public bool Json { get; set; } = false;
}

public class LifeLogListCommand : AsyncCommand<LifeLogListSettings>
{
    private readonly ApiClient  _api;
    private readonly TokenStore _store;
    public LifeLogListCommand(ApiClient api, TokenStore store) { _api = api; _store = store; }

    public override async Task<int> ExecuteAsync(CommandContext context, LifeLogListSettings s)
    {
        if (!CommandHelpers.EnsureLoggedIn(_store, s.Json)) return 1;
        try
        {
            PagedResult<LifeLogDto> result = null!;
            await CommandHelpers.RunAsync(s.Json, "加载中...",
                async () => result = await _api.GetLifeLogsAsync(s.Keyword, s.Mood, s.Month, pageSize: s.PageSize));

            if (s.Json)
            {
                CommandHelpers.WriteJson(new { success = true, total = result.Total, items = result.Items });
                return 0;
            }

            if (result.Items.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]没有找到生活记录。[/]");
                return 0;
            }

            var table = new Table().Border(TableBorder.Rounded)
                .AddColumn("[bold]ID[/]").AddColumn("[bold]日期[/]")
                .AddColumn("[bold]心情[/]").AddColumn("[bold]内容[/]");

            foreach (var l in result.Items)
            {
                table.AddRow(
                    l.Id.ToString(),
                    l.LogDate.ToString("yyyy-MM-dd"),
                    Markup.Escape(l.Mood ?? "-"),
                    Markup.Escape(l.Content.Length > 80 ? l.Content[..80] + "…" : l.Content));
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"[grey]共 {result.Total} 条，当前显示 {result.Items.Count} 条[/]");
            return 0;
        }
        catch (ApiException ex) { return CommandHelpers.HandleError(ex.Message, s.Json); }
    }
}

// ===== lifelog add =====

public class LifeLogAddSettings : CommandSettings
{
    [CommandOption("-c|--content")]
    [Description("内容（必填）")]
    public string? Content { get; set; }

    [CommandOption("-d|--date")]
    [Description("日期 yyyy-MM-dd（默认今天）")]
    public string? Date { get; set; }

    [CommandOption("--mood")]
    [Description("心情（如：开心/平静/疲惫）")]
    public string? Mood { get; set; }

    [CommandOption("--json")]
    [Description("输出 JSON（Agent 友好模式）")]
    public bool Json { get; set; } = false;
}

public class LifeLogAddCommand : AsyncCommand<LifeLogAddSettings>
{
    private readonly ApiClient  _api;
    private readonly TokenStore _store;
    public LifeLogAddCommand(ApiClient api, TokenStore store) { _api = api; _store = store; }

    public override async Task<int> ExecuteAsync(CommandContext context, LifeLogAddSettings s)
    {
        if (!CommandHelpers.EnsureLoggedIn(_store, s.Json)) return 1;
        try
        {
            string content;
            DateTime date;
            string? mood;

            if (s.Content != null)
            {
                // Agent 模式
                content = s.Content;
                date    = DateTime.TryParse(s.Date, out var pd) ? pd : DateTime.Today;
                mood    = CommandHelpers.OrNull(s.Mood);
            }
            else
            {
                // 交互模式
                AnsiConsole.MarkupLine("[bold cyan]新建生活记录[/]");
                content = AnsiConsole.Ask<string>("内容（必填）：");
                var dStr = AnsiConsole.Ask<string>("日期（yyyy-MM-dd，默认今天）：", DateTime.Today.ToString("yyyy-MM-dd"));
                date = DateTime.TryParse(dStr, out var d) ? d : DateTime.Today;
                mood = CommandHelpers.OrNull(AnsiConsole.Ask<string>("心情（可留空）：", ""));
            }

            LifeLogDto created = null!;
            await CommandHelpers.RunAsync(s.Json, "保存中...",
                async () => created = await _api.CreateLifeLogAsync(new { content, logDate = date, mood }));

            if (s.Json) { CommandHelpers.WriteJson(new { success = true, data = created }); return 0; }
            AnsiConsole.MarkupLine($"[green]✓ 生活记录已创建（ID={created.Id}，日期：{created.LogDate:yyyy-MM-dd}）[/]");
            return 0;
        }
        catch (ApiException ex) { return CommandHelpers.HandleError(ex.Message, s.Json); }
    }
}

// ===== lifelog delete =====

public class LifeLogDeleteSettings : CommandSettings
{
    [CommandArgument(0, "<id>")]
    public int Id { get; set; }

    [CommandOption("-y|--yes")]
    [Description("跳过确认（Agent 友好模式）")]
    public bool Yes { get; set; } = false;

    [CommandOption("--json")]
    [Description("输出 JSON（Agent 友好模式）")]
    public bool Json { get; set; } = false;
}

public class LifeLogDeleteCommand : AsyncCommand<LifeLogDeleteSettings>
{
    private readonly ApiClient  _api;
    private readonly TokenStore _store;
    public LifeLogDeleteCommand(ApiClient api, TokenStore store) { _api = api; _store = store; }

    public override async Task<int> ExecuteAsync(CommandContext context, LifeLogDeleteSettings s)
    {
        if (!CommandHelpers.EnsureLoggedIn(_store, s.Json)) return 1;
        try
        {
            if (!s.Yes && !AnsiConsole.Confirm($"确认删除生活记录 ID={s.Id}？", false)) return 0;
            await CommandHelpers.RunAsync(s.Json, "删除中...", async () => await _api.DeleteLifeLogAsync(s.Id));
            if (s.Json) { CommandHelpers.WriteJson(new { success = true, id = s.Id }); return 0; }
            AnsiConsole.MarkupLine("[green]✓ 已删除。[/]");
            return 0;
        }
        catch (ApiException ex) { return CommandHelpers.HandleError(ex.Message, s.Json); }
    }
}
