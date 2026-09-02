using MiraiNote.CLI.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace MiraiNote.CLI.Commands;

// ===== memo list =====

public class MemoListSettings : CommandSettings
{
    [CommandOption("--section")]
    [Description("板块：work（默认）或 life")]
    public string Section { get; set; } = "work";

    [CommandOption("-k|--keyword")]
    [Description("关键词筛选")]
    public string? Keyword { get; set; }

    [CommandOption("--all")]
    [Description("包含已完成和已归档")]
    public bool All { get; set; } = false;

    [CommandOption("--json")]
    [Description("输出 JSON（Agent 友好模式）")]
    public bool Json { get; set; } = false;
}

public class MemoListCommand : AsyncCommand<MemoListSettings>
{
    private readonly ApiClient  _api;
    private readonly TokenStore _store;
    public MemoListCommand(ApiClient api, TokenStore store) { _api = api; _store = store; }

    public override async Task<int> ExecuteAsync(CommandContext context, MemoListSettings s)
    {
        if (!CommandHelpers.EnsureLoggedIn(_store, s.Json)) return 1;
        try
        {
            PagedResult<MemoDto> result = null!;
            await CommandHelpers.RunAsync(s.Json, "加载中...", async () =>
                result = await _api.GetMemosAsync(s.Section, s.Keyword,
                    includeDone: s.All, includeArchived: s.All));

            if (s.Json)
            {
                CommandHelpers.WriteJson(new { success = true, total = result.Total, items = result.Items });
                return 0;
            }

            if (result.Items.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]没有找到备忘事项。[/]");
                return 0;
            }

            var sectionLabel = s.Section == "life" ? "生活备忘" : "工作备忘";
            AnsiConsole.MarkupLine($"[bold cyan]── {sectionLabel} ──[/]");

            var table = new Table().Border(TableBorder.Rounded)
                .AddColumn("[bold]ID[/]").AddColumn("[bold]内容[/]")
                .AddColumn("[bold]优先级[/]").AddColumn("[bold]置顶[/]")
                .AddColumn("[bold]完成[/]").AddColumn("[bold]提醒时间[/]");

            var prioLabels = new[] { "", "低", "中", "高" };
            var cstZone    = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

            foreach (var m in result.Items)
            {
                var prioStr = m.Priority > 0 && m.Priority <= 3 ? prioLabels[m.Priority] : "-";
                var prioClr = m.Priority == 3 ? "red" : m.Priority == 2 ? "yellow" : "grey";
                var remindStr = m.RemindAt.HasValue
                    ? TimeZoneInfo.ConvertTimeFromUtc(m.RemindAt.Value, cstZone).ToString("MM-dd HH:mm")
                    : "-";

                table.AddRow(
                    m.Id.ToString(),
                    Markup.Escape(m.Content.Length > 60 ? m.Content[..60] + "…" : m.Content),
                    $"[{prioClr}]{prioStr}[/]",
                    m.IsPinned ? "[yellow]★[/]" : "",
                    m.IsDone   ? "[green]✓[/]" : "",
                    remindStr);
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"[grey]共 {result.Total} 条[/]");
            return 0;
        }
        catch (ApiException ex) { return CommandHelpers.HandleError(ex.Message, s.Json); }
    }
}

// ===== memo add =====

public class MemoAddSettings : CommandSettings
{
    [CommandOption("-c|--content")]
    [Description("备忘内容（必填）")]
    public string? Content { get; set; }

    [CommandOption("--section")]
    [Description("板块：work（默认）或 life")]
    public string? Section { get; set; }

    [CommandOption("-p|--priority")]
    [Description("优先级：1=低 2=中 3=高（默认2）")]
    public byte? Priority { get; set; }

    [CommandOption("--pin")]
    [Description("置顶")]
    public bool IsPinned { get; set; } = false;

    [CommandOption("--remind-at")]
    [Description("提醒时间 yyyy-MM-dd HH:mm（本地时间）")]
    public string? RemindAt { get; set; }

    [CommandOption("--remind-methods")]
    [Description("提醒方式：0=不提醒 1=弹窗 2=邮件 3=弹窗+邮件（默认0）")]
    public byte? RemindMethods { get; set; }

    [CommandOption("--json")]
    [Description("输出 JSON（Agent 友好模式）")]
    public bool Json { get; set; } = false;
}

public class MemoAddCommand : AsyncCommand<MemoAddSettings>
{
    private readonly ApiClient  _api;
    private readonly TokenStore _store;
    public MemoAddCommand(ApiClient api, TokenStore store) { _api = api; _store = store; }

    public override async Task<int> ExecuteAsync(CommandContext context, MemoAddSettings s)
    {
        if (!CommandHelpers.EnsureLoggedIn(_store, s.Json)) return 1;
        try
        {
            string section, content;
            byte priority, remindMethods;
            bool isPinned;
            DateTime? remindAt;

            if (s.Content != null)
            {
                // Agent 模式：从 flags 取
                content       = s.Content;
                section       = CommandHelpers.OrNull(s.Section) ?? "work";
                priority      = s.Priority ?? 2;
                isPinned      = s.IsPinned;
                remindMethods = s.RemindMethods ?? 0;
                remindAt      = !string.IsNullOrWhiteSpace(s.RemindAt) && DateTime.TryParse(s.RemindAt, out var rt)
                                ? rt : null;
            }
            else
            {
                // 交互模式
                AnsiConsole.MarkupLine("[bold cyan]新建备忘[/]");
                section = AnsiConsole.Prompt(
                    new SelectionPrompt<string>().Title("板块：")
                        .AddChoices("work 工作", "life 生活"))
                    .Split(' ')[0];
                content = AnsiConsole.Ask<string>("内容（必填）：");
                var prioRaw = AnsiConsole.Prompt(
                    new SelectionPrompt<string>().Title("优先级：")
                        .AddChoices("1 低", "2 中（默认）", "3 高"));
                priority = byte.Parse(prioRaw[0].ToString());
                isPinned = AnsiConsole.Confirm("置顶？", false);
                var remindStr = AnsiConsole.Ask<string>("提醒时间（yyyy-MM-dd HH:mm，可留空）：", "");
                remindAt      = null;
                remindMethods = 0;
                if (!string.IsNullOrWhiteSpace(remindStr) && DateTime.TryParse(remindStr, out var rd))
                {
                    remindAt = rd;
                    var rmRaw = AnsiConsole.Prompt(
                        new SelectionPrompt<string>().Title("提醒方式：")
                            .AddChoices("0 不提醒", "1 弹窗", "2 邮件", "3 弹窗+邮件"));
                    remindMethods = byte.Parse(rmRaw[0].ToString());
                }
            }

            MemoDto created = null!;
            await CommandHelpers.RunAsync(s.Json, "保存中...", async () =>
                created = await _api.CreateMemoAsync(new
                {
                    section, content, priority, isPinned,
                    remindAt = remindAt?.ToUniversalTime().ToString("o"),
                    remindMethods
                }));

            if (s.Json) { CommandHelpers.WriteJson(new { success = true, data = created }); return 0; }
            AnsiConsole.MarkupLine($"[green]✓ 备忘已创建（ID={created.Id}）[/]");
            return 0;
        }
        catch (ApiException ex) { return CommandHelpers.HandleError(ex.Message, s.Json); }
    }
}

// ===== memo done =====

public class MemoDoneSettings : CommandSettings
{
    [CommandArgument(0, "<id>")]
    [Description("备忘 ID")]
    public int Id { get; set; }

    [CommandOption("--undo")]
    [Description("取消完成标记")]
    public bool Undo { get; set; } = false;

    [CommandOption("--json")]
    [Description("输出 JSON（Agent 友好模式）")]
    public bool Json { get; set; } = false;
}

public class MemoDoneCommand : AsyncCommand<MemoDoneSettings>
{
    private readonly ApiClient  _api;
    private readonly TokenStore _store;
    public MemoDoneCommand(ApiClient api, TokenStore store) { _api = api; _store = store; }

    public override async Task<int> ExecuteAsync(CommandContext context, MemoDoneSettings s)
    {
        if (!CommandHelpers.EnsureLoggedIn(_store, s.Json)) return 1;
        try
        {
            await CommandHelpers.RunAsync(s.Json, "更新中...",
                async () => await _api.PatchMemoStatusAsync(s.Id, new { isDone = !s.Undo }));
            if (s.Json) { CommandHelpers.WriteJson(new { success = true, id = s.Id, isDone = !s.Undo }); return 0; }
            AnsiConsole.MarkupLine($"[green]✓ 备忘 ID={s.Id} 已{(s.Undo ? "取消完成" : "标记为完成")}。[/]");
            return 0;
        }
        catch (ApiException ex) { return CommandHelpers.HandleError(ex.Message, s.Json); }
    }
}

// ===== memo delete =====

public class MemoDeleteSettings : CommandSettings
{
    [CommandArgument(0, "<id>")]
    [Description("要删除的备忘 ID")]
    public int Id { get; set; }

    [CommandOption("-y|--yes")]
    [Description("跳过确认（Agent 友好模式）")]
    public bool Yes { get; set; } = false;

    [CommandOption("--json")]
    [Description("输出 JSON（Agent 友好模式）")]
    public bool Json { get; set; } = false;
}

public class MemoDeleteCommand : AsyncCommand<MemoDeleteSettings>
{
    private readonly ApiClient  _api;
    private readonly TokenStore _store;
    public MemoDeleteCommand(ApiClient api, TokenStore store) { _api = api; _store = store; }

    public override async Task<int> ExecuteAsync(CommandContext context, MemoDeleteSettings s)
    {
        if (!CommandHelpers.EnsureLoggedIn(_store, s.Json)) return 1;
        try
        {
            if (!s.Yes && !AnsiConsole.Confirm($"确认删除备忘 ID={s.Id}？", false)) return 0;
            await CommandHelpers.RunAsync(s.Json, "删除中...", async () => await _api.DeleteMemoAsync(s.Id));
            if (s.Json) { CommandHelpers.WriteJson(new { success = true, id = s.Id }); return 0; }
            AnsiConsole.MarkupLine("[green]✓ 已删除。[/]");
            return 0;
        }
        catch (ApiException ex) { return CommandHelpers.HandleError(ex.Message, s.Json); }
    }
}
