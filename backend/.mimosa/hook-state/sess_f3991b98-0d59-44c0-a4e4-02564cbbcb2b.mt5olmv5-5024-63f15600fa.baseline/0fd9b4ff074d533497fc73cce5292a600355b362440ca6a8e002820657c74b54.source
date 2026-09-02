using MiraiNote.CLI.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace MiraiNote.CLI.Commands;

// ===== weekly list =====

public class WeeklyListSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("输出 JSON（Agent 友好模式）")]
    public bool Json { get; set; } = false;
}

public class WeeklyListCommand : AsyncCommand<WeeklyListSettings>
{
    private readonly ApiClient  _api;
    private readonly TokenStore _store;
    public WeeklyListCommand(ApiClient api, TokenStore store) { _api = api; _store = store; }

    public override async Task<int> ExecuteAsync(CommandContext context, WeeklyListSettings s)
    {
        if (!CommandHelpers.EnsureLoggedIn(_store, s.Json)) return 1;
        try
        {
            List<WeeklyReportDto> reports = null!;
            await CommandHelpers.RunAsync(s.Json, "加载中...",
                async () => reports = await _api.GetWeeklyReportsAsync());

            if (s.Json) { CommandHelpers.WriteJson(new { success = true, items = reports }); return 0; }

            if (reports.Count == 0) { AnsiConsole.MarkupLine("[yellow]暂无周报记录。[/]"); return 0; }

            var table = new Table().Border(TableBorder.Rounded)
                .AddColumn("[bold]ID[/]").AddColumn("[bold]周期[/]").AddColumn("[bold]生成时间[/]");
            foreach (var r in reports)
                table.AddRow(
                    r.Id.ToString(),
                    $"{r.WeekStart:yyyy-MM-dd} ~ {r.WeekEnd:yyyy-MM-dd}",
                    r.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            AnsiConsole.Write(table);
            return 0;
        }
        catch (ApiException ex) { return CommandHelpers.HandleError(ex.Message, s.Json); }
    }
}

// ===== weekly generate =====

public class WeeklyGenerateSettings : CommandSettings
{
    [CommandOption("--week-start")]
    [Description("周报起始日期（本周一，格式 yyyy-MM-dd，默认本周）")]
    public string? WeekStart { get; set; }

    [CommandOption("--json")]
    [Description("输出 JSON（Agent 友好模式）")]
    public bool Json { get; set; } = false;
}

public class WeeklyGenerateCommand : AsyncCommand<WeeklyGenerateSettings>
{
    private readonly ApiClient  _api;
    private readonly TokenStore _store;
    public WeeklyGenerateCommand(ApiClient api, TokenStore store) { _api = api; _store = store; }

    public override async Task<int> ExecuteAsync(CommandContext context, WeeklyGenerateSettings s)
    {
        if (!CommandHelpers.EnsureLoggedIn(_store, s.Json)) return 1;
        try
        {
            DateTime weekStart;
            if (!string.IsNullOrWhiteSpace(s.WeekStart) && DateTime.TryParse(s.WeekStart, out var parsed))
                weekStart = parsed;
            else
            {
                var today = DateTime.Today;
                weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            }

            if (!s.Json)
                AnsiConsole.MarkupLine($"[grey]正在为 {weekStart:yyyy-MM-dd} ~ {weekStart.AddDays(6):yyyy-MM-dd} 生成周报...[/]");

            WeeklyReportDto report = null!;
            if (s.Json)
                report = await _api.GenerateWeeklyReportAsync(weekStart);
            else
                await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                    .StartAsync("AI 生成中（可能需要10-30秒）...", async _ =>
                        report = await _api.GenerateWeeklyReportAsync(weekStart));

            if (s.Json) { CommandHelpers.WriteJson(new { success = true, data = report }); return 0; }

            AnsiConsole.MarkupLine($"[bold cyan]── 周报（{report.WeekStart:yyyy-MM-dd} ~ {report.WeekEnd:yyyy-MM-dd}）──[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine(report.Content ?? "（内容为空）");
            return 0;
        }
        catch (ApiException ex) { return CommandHelpers.HandleError(ex.Message, s.Json); }
    }
}

// ===== weekly view =====

public class WeeklyViewSettings : CommandSettings
{
    [CommandArgument(0, "<id>")]
    [Description("周报 ID")]
    public int Id { get; set; }

    [CommandOption("--json")]
    [Description("输出 JSON（Agent 友好模式）")]
    public bool Json { get; set; } = false;
}

public class WeeklyViewCommand : AsyncCommand<WeeklyViewSettings>
{
    private readonly ApiClient  _api;
    private readonly TokenStore _store;
    public WeeklyViewCommand(ApiClient api, TokenStore store) { _api = api; _store = store; }

    public override async Task<int> ExecuteAsync(CommandContext context, WeeklyViewSettings s)
    {
        if (!CommandHelpers.EnsureLoggedIn(_store, s.Json)) return 1;
        try
        {
            List<WeeklyReportDto> reports = null!;
            await CommandHelpers.RunAsync(s.Json, "加载中...",
                async () => reports = await _api.GetWeeklyReportsAsync());

            var report = reports.FirstOrDefault(r => r.Id == s.Id);
            if (report == null)
                return CommandHelpers.HandleError($"未找到周报 ID={s.Id}", s.Json);

            if (s.Json) { CommandHelpers.WriteJson(new { success = true, data = report }); return 0; }

            AnsiConsole.MarkupLine($"[bold cyan]── 周报（{report.WeekStart:yyyy-MM-dd} ~ {report.WeekEnd:yyyy-MM-dd}）──[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine(report.Content ?? "（内容为空）");
            return 0;
        }
        catch (ApiException ex) { return CommandHelpers.HandleError(ex.Message, s.Json); }
    }
}
