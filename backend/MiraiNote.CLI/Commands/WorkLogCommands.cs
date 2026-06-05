using MiraiNote.CLI.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text.Json;

namespace MiraiNote.CLI.Commands;

// ===== worklog list =====

public class WorkLogListSettings : CommandSettings
{
    [CommandOption("-k|--keyword")]
    [Description("关键词筛选")]
    public string? Keyword { get; set; }

    [CommandOption("--from")]
    [Description("起始日期 yyyy-MM-dd")]
    public string? DateFrom { get; set; }

    [CommandOption("--to")]
    [Description("结束日期 yyyy-MM-dd")]
    public string? DateTo { get; set; }

    [CommandOption("--category")]
    [Description("项目分类")]
    public string? Category { get; set; }

    [CommandOption("-s|--status")]
    [Description("状态：0=未标记 1=进行中 2=已完成 3=已延期")]
    public byte? Status { get; set; }

    [CommandOption("-n|--page-size")]
    [Description("每页条数（默认20）")]
    public int PageSize { get; set; } = 20;

    [CommandOption("--json")]
    [Description("输出 JSON（Agent 友好模式）")]
    public bool Json { get; set; } = false;
}

public class WorkLogListCommand : AsyncCommand<WorkLogListSettings>
{
    private readonly ApiClient  _api;
    private readonly TokenStore _store;
    public WorkLogListCommand(ApiClient api, TokenStore store) { _api = api; _store = store; }

    public override async Task<int> ExecuteAsync(CommandContext context, WorkLogListSettings s)
    {
        if (!CommandHelpers.EnsureLoggedIn(_store, s.Json)) return 1;
        try
        {
            DateTime? from = s.DateFrom != null && DateTime.TryParse(s.DateFrom, out var df) ? df : null;
            DateTime? to   = s.DateTo   != null && DateTime.TryParse(s.DateTo,   out var dt) ? dt : null;

            PagedResult<WorkLogDto> result = null!;
            await CommandHelpers.RunAsync(s.Json, "加载中...",
                async () => result = await _api.GetWorkLogsAsync(s.Keyword, from, to, s.Category, s.Status, pageSize: s.PageSize));

            if (s.Json)
            {
                CommandHelpers.WriteJson(new { success = true, total = result.Total, items = result.Items });
                return 0;
            }

            if (result.Items.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]没有找到符合条件的工作记录。[/]");
                return 0;
            }

            var statusLabels = new[] { "未标记", "进行中", "已完成", "已延期" };
            var statusColors = new[] { "grey", "blue", "green", "red" };

            var table = new Table().Border(TableBorder.Rounded)
                .AddColumn("[bold]ID[/]").AddColumn("[bold]日期[/]").AddColumn("[bold]标题[/]")
                .AddColumn("[bold]分类[/]").AddColumn("[bold]状态[/]").AddColumn("[bold]标签[/]");

            foreach (var w in result.Items)
            {
                var st    = w.Status > 3 ? 0 : w.Status;
                var color = statusColors[st];
                table.AddRow(
                    w.Id.ToString(),
                    w.LogDate.ToString("MM-dd"),
                    Markup.Escape(w.Title),
                    Markup.Escape(w.Category ?? "-"),
                    $"[{color}]{statusLabels[st]}[/]",
                    Markup.Escape(w.Tags ?? "-"));
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"[grey]共 {result.Total} 条，当前显示 {result.Items.Count} 条[/]");
            return 0;
        }
        catch (ApiException ex) { return CommandHelpers.HandleError(ex.Message, s.Json); }
    }
}

// ===== worklog add =====

public class WorkLogAddSettings : CommandSettings
{
    [CommandOption("-t|--title")]
    [Description("标题（必填）")]
    public string? Title { get; set; }

    [CommandOption("-d|--date")]
    [Description("日期 yyyy-MM-dd（默认今天）")]
    public string? Date { get; set; }

    [CommandOption("--purpose")]
    [Description("目的")]
    public string? Purpose { get; set; }

    [CommandOption("-c|--content")]
    [Description("内容")]
    public string? Content { get; set; }

    [CommandOption("--tags")]
    [Description("标签，逗号分隔")]
    public string? Tags { get; set; }

    [CommandOption("--category")]
    [Description("分类")]
    public string? Category { get; set; }

    [CommandOption("-s|--status")]
    [Description("状态：0=未标记 1=进行中 2=已完成 3=已延期（默认0）")]
    public byte? Status { get; set; }

    [CommandOption("--json")]
    [Description("输出 JSON（Agent 友好模式）")]
    public bool Json { get; set; } = false;
}

public class WorkLogAddCommand : AsyncCommand<WorkLogAddSettings>
{
    private readonly ApiClient  _api;
    private readonly TokenStore _store;
    public WorkLogAddCommand(ApiClient api, TokenStore store) { _api = api; _store = store; }

    public override async Task<int> ExecuteAsync(CommandContext context, WorkLogAddSettings s)
    {
        if (!CommandHelpers.EnsureLoggedIn(_store, s.Json)) return 1;
        try
        {
            string title;
            DateTime date;
            string? purpose, content, tags, category;
            byte status;

            if (s.Title != null)
            {
                // Agent 模式：全部从 flag 取
                title    = s.Title;
                date     = DateTime.TryParse(s.Date, out var pd) ? pd : DateTime.Today;
                purpose  = CommandHelpers.OrNull(s.Purpose);
                content  = CommandHelpers.OrNull(s.Content);
                tags     = CommandHelpers.OrNull(s.Tags);
                category = CommandHelpers.OrNull(s.Category);
                status   = s.Status ?? 0;
            }
            else
            {
                // 交互模式
                AnsiConsole.MarkupLine("[bold cyan]新建工作记录[/]");
                title    = AnsiConsole.Ask<string>("标题（必填）：");
                var dStr = AnsiConsole.Ask<string>("日期（yyyy-MM-dd，默认今天）：", DateTime.Today.ToString("yyyy-MM-dd"));
                date     = DateTime.TryParse(dStr, out var d) ? d : DateTime.Today;
                purpose  = CommandHelpers.OrNull(AnsiConsole.Ask<string>("目的（可留空）：", ""));
                content  = CommandHelpers.OrNull(AnsiConsole.Ask<string>("内容（可留空）：", ""));
                tags     = CommandHelpers.OrNull(AnsiConsole.Ask<string>("标签（逗号分隔，可留空）：", ""));
                category = CommandHelpers.OrNull(AnsiConsole.Ask<string>("分类（可留空）：", ""));
                var stRaw = AnsiConsole.Prompt(
                    new SelectionPrompt<string>().Title("状态：")
                        .AddChoices("0 未标记", "1 进行中", "2 已完成", "3 已延期"));
                status = byte.Parse(stRaw[0].ToString());
            }

            WorkLogDto created = null!;
            await CommandHelpers.RunAsync(s.Json, "保存中...", async () =>
                created = await _api.CreateWorkLogAsync(new { title, logDate = date, purpose, content, tags, category, status }));

            if (s.Json)
            {
                CommandHelpers.WriteJson(new { success = true, data = created });
                return 0;
            }
            AnsiConsole.MarkupLine($"[green]✓ 工作记录已创建（ID={created.Id}）：{Markup.Escape(created.Title)}[/]");
            return 0;
        }
        catch (ApiException ex) { return CommandHelpers.HandleError(ex.Message, s.Json); }
    }
}

// ===== worklog delete =====

public class WorkLogDeleteSettings : CommandSettings
{
    [CommandArgument(0, "<id>")]
    [Description("要删除的工作记录 ID")]
    public int Id { get; set; }

    [CommandOption("-y|--yes")]
    [Description("跳过确认（Agent 友好模式）")]
    public bool Yes { get; set; } = false;

    [CommandOption("--json")]
    [Description("输出 JSON（Agent 友好模式）")]
    public bool Json { get; set; } = false;
}

public class WorkLogDeleteCommand : AsyncCommand<WorkLogDeleteSettings>
{
    private readonly ApiClient  _api;
    private readonly TokenStore _store;
    public WorkLogDeleteCommand(ApiClient api, TokenStore store) { _api = api; _store = store; }

    public override async Task<int> ExecuteAsync(CommandContext context, WorkLogDeleteSettings s)
    {
        if (!CommandHelpers.EnsureLoggedIn(_store, s.Json)) return 1;
        try
        {
            if (!s.Yes && !AnsiConsole.Confirm($"确认删除工作记录 ID={s.Id}？此操作不可恢复。", false))
                return 0;
            await CommandHelpers.RunAsync(s.Json, "删除中...", async () => await _api.DeleteWorkLogAsync(s.Id));
            if (s.Json) { CommandHelpers.WriteJson(new { success = true, id = s.Id }); return 0; }
            AnsiConsole.MarkupLine("[green]✓ 已删除。[/]");
            return 0;
        }
        catch (ApiException ex) { return CommandHelpers.HandleError(ex.Message, s.Json); }
    }
}

// ===== 共用辅助 =====

internal static partial class CommandHelpers
{
    private static readonly System.Text.Json.JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented               = false,
        Encoder                     = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static bool EnsureLoggedIn(TokenStore store, bool json = false)
    {
        if (store.HasToken) return true;
        if (json) WriteJson(new { success = false, error = "未登录，请先执行 mirainote login" });
        else AnsiConsole.MarkupLine("[red]✗ 请先执行 [bold]mirainote login[/] 登录。[/]");
        return false;
    }

    public static string? OrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    public static void WriteJson(object obj)
        => Console.WriteLine(JsonSerializer.Serialize(obj, _jsonOpts));

    public static int HandleError(string msg, bool json)
    {
        if (json) WriteJson(new { success = false, error = msg });
        else AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(msg)}[/]");
        return 1;
    }

    /// <summary>有 --json 时静默执行，否则显示 spinner。</summary>
    public static async Task RunAsync(bool json, string statusMsg, Func<Task> action)
    {
        if (json) { await action(); return; }
        await AnsiConsole.Status().StartAsync(statusMsg, async _ => await action());
    }
}

