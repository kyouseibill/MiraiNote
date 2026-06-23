using System.Text;
using System.Text.Json;
using System.ComponentModel;
using MiraiNote.CLI.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MiraiNote.CLI.Commands;

public class TaskSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("输出 JSON（Agent 友好模式）")]
    public bool Json { get; set; } = false;
}

public class TaskDeleteSettings : CommandSettings
{
    [CommandArgument(0, "<id>")]
    [Description("要取消的任务 ID")]
    public int Id { get; set; }

    [CommandOption("--json")]
    public bool Json { get; set; } = false;
}

/// <summary>
/// 列出所有定时任务。
/// </summary>
public class TaskListCommand : AsyncCommand<TaskSettings>
{
    private readonly ApiClient _api;
    private readonly TokenStore _store;

    public TaskListCommand(ApiClient api, TokenStore store) { _api = api; _store = store; }

    public override async Task<int> ExecuteAsync(CommandContext context, TaskSettings s)
    {
        if (!CommandHelpers.EnsureLoggedIn(_store, s.Json)) return 1;

        try
        {
            var tasks = await _api.GetScheduledTasksAsync();

            if (s.Json)
            {
                CommandHelpers.WriteJson(new { success = true, total = tasks.Count, items = tasks });
                return 0;
            }

            if (tasks.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]没有定时任务。[/]");
                return 0;
            }

            var table = new Table()
                .Border(TableBorder.Simple)
                .AddColumn("ID")
                .AddColumn("状态")
                .AddColumn("执行时间")
                .AddColumn("描述");

            foreach (var t in tasks)
            {
                var status = t.Status switch
                {
                    "Pending" => "[yellow]待执行[/]",
                    "Running" => "[blue]执行中[/]",
                    "Completed" => "[green]已完成[/]",
                    "Failed" => "[red]失败[/]",
                    "Cancelled" => "[grey]已取消[/]",
                    _ => t.Status
                };
                table.AddRow(
                    t.Id.ToString(),
                    Markup.Escape(status),
                    t.ExecuteAt.ToLocalTime().ToString("MM-dd HH:mm"),
                    Markup.Escape(t.Description.Length > 50 ? t.Description[..50] + "..." : t.Description));
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine("[grey]使用 task delete <ID> 取消任务[/]");
            return 0;
        }
        catch (ApiException ex) { return CommandHelpers.HandleError(ex.Message, s.Json); }
    }
}

/// <summary>
/// 取消一个定时任务。
/// </summary>
public class TaskDeleteCommand : AsyncCommand<TaskDeleteSettings>
{
    private readonly ApiClient _api;
    private readonly TokenStore _store;

    public TaskDeleteCommand(ApiClient api, TokenStore store) { _api = api; _store = store; }

    public override async Task<int> ExecuteAsync(CommandContext context, TaskDeleteSettings s)
    {
        if (!CommandHelpers.EnsureLoggedIn(_store, s.Json)) return 1;

        try
        {
            await _api.CancelScheduledTaskAsync(s.Id);

            if (s.Json) { CommandHelpers.WriteJson(new { success = true, id = s.Id }); return 0; }
            AnsiConsole.MarkupLine($"[green]✓ 任务 ID={s.Id} 已取消。[/]");
            return 0;
        }
        catch (ApiException ex) { return CommandHelpers.HandleError(ex.Message, s.Json); }
    }
}
