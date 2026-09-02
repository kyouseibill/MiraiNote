using System.ComponentModel;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MiraiNote.CLI.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MiraiNote.CLI.Commands;

public class WorkLogListSettings : CommandSettings
{
    [CommandOption("-k|--keyword")]
    [Description("在标题、目的、内容和标签中搜索")]
    public string? Keyword { get; set; }

    [CommandOption("--from")]
    [Description("开始日期，格式 yyyy-MM-dd")]
    public string? DateFrom { get; set; }

    [CommandOption("--to")]
    [Description("结束日期，格式 yyyy-MM-dd")]
    public string? DateTo { get; set; }

    [CommandOption("--category")]
    [Description("按分类筛选")]
    public string? Category { get; set; }

    [CommandOption("--tag")]
    [Description("按单个标签筛选")]
    public string? Tag { get; set; }

    [CommandOption("-s|--status")]
    [Description("状态：unmarked、in-progress、completed、delayed 或 0-3")]
    public string? Status { get; set; }

    [CommandOption("--page")]
    [Description("页码，默认 1")]
    public int Page { get; set; } = 1;

    [CommandOption("-n|--page-size")]
    [Description("每页条数，范围 1-100，默认 20")]
    public int PageSize { get; set; } = 20;

    [CommandOption("--json")]
    [Description("只输出机器可读 JSON")]
    public bool Json { get; set; }
}

public sealed class WorkLogListCommand(ApiClient api, TokenStore store) : AsyncCommand<WorkLogListSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, WorkLogListSettings settings)
        => WorkLogCommandSupport.GuardAsync(settings.Json, async () =>
        {
            WorkLogCommandSupport.RequireLogin(store, settings.Json);
            if (settings.Page < 1) throw new WorkLogInputException("page 必须大于等于 1");
            if (settings.PageSize is < 1 or > 100) throw new WorkLogInputException("page-size 必须在 1 到 100 之间");

            var from = WorkLogCommandSupport.ParseOptionalDate(settings.DateFrom, "from");
            var to = WorkLogCommandSupport.ParseOptionalDate(settings.DateTo, "to");
            if (from.HasValue && to.HasValue && from > to)
                throw new WorkLogInputException("from 不能晚于 to");

            var status = WorkLogCommandSupport.ParseOptionalStatus(settings.Status);
            PagedResult<WorkLogDto> result = null!;
            await CommandHelpers.RunAsync(settings.Json, "正在加载工作记录...", async () =>
                result = await api.GetWorkLogsAsync(
                    keyword: CommandHelpers.OrNull(settings.Keyword),
                    dateFrom: from,
                    dateTo: to,
                    category: CommandHelpers.OrNull(settings.Category),
                    tag: CommandHelpers.OrNull(settings.Tag),
                    status: status,
                    page: settings.Page,
                    pageSize: settings.PageSize));

            if (settings.Json)
            {
                CommandHelpers.WriteJson(new
                {
                    success = true,
                    page = result.Page,
                    pageSize = result.PageSize,
                    total = result.Total,
                    items = result.Items
                });
                return 0;
            }

            if (result.Items.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]没有找到符合条件的工作记录。[/]");
                return 0;
            }

            var table = new Table().Border(TableBorder.Rounded)
                .AddColumn("[bold]ID[/]")
                .AddColumn("[bold]日期[/]")
                .AddColumn("[bold]标题[/]")
                .AddColumn("[bold]分类[/]")
                .AddColumn("[bold]状态[/]")
                .AddColumn("[bold]标签[/]");

            foreach (var item in result.Items)
            {
                var (label, color) = WorkLogCommandSupport.StatusDisplay(item.Status);
                table.AddRow(
                    item.Id.ToString(CultureInfo.InvariantCulture),
                    item.LogDate.ToString("yyyy-MM-dd"),
                    Markup.Escape(item.Title),
                    Markup.Escape(item.Category ?? "-"),
                    $"[{color}]{label}[/]",
                    Markup.Escape(item.Tags ?? "-"));
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"[grey]共 {result.Total} 条，当前显示 {result.Items.Count} 条。[/]");
            return 0;
        });
}

public class WorkLogGetSettings : CommandSettings
{
    [CommandArgument(0, "<id>")]
    [Description("工作记录 ID")]
    public int Id { get; set; }

    [CommandOption("--json")]
    [Description("只输出机器可读 JSON")]
    public bool Json { get; set; }
}

public sealed class WorkLogGetCommand(ApiClient api, TokenStore store) : AsyncCommand<WorkLogGetSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, WorkLogGetSettings settings)
        => WorkLogCommandSupport.GuardAsync(settings.Json, async () =>
        {
            WorkLogCommandSupport.RequireLogin(store, settings.Json);
            WorkLogCommandSupport.ValidateId(settings.Id);
            WorkLogDto item = null!;
            await CommandHelpers.RunAsync(settings.Json, "正在读取工作记录...",
                async () => item = await api.GetWorkLogAsync(settings.Id));

            if (settings.Json)
                CommandHelpers.WriteJson(new { success = true, data = item });
            else
                WorkLogCommandSupport.RenderDetails(item);
            return 0;
        });
}

public class WorkLogWriteSettings : CommandSettings
{
    [CommandOption("-t|--title")]
    [Description("标题；创建时必填")]
    public string? Title { get; set; }

    [CommandOption("-d|--date")]
    [Description("记录日期，格式 yyyy-MM-dd；创建时默认今天")]
    public string? Date { get; set; }

    [CommandOption("--purpose")]
    [Description("工作目的")]
    public string? Purpose { get; set; }

    [CommandOption("-c|--content")]
    [Description("工作内容")]
    public string? Content { get; set; }

    [CommandOption("--tags")]
    [Description("标签，使用逗号分隔")]
    public string? Tags { get; set; }

    [CommandOption("--category")]
    [Description("分类")]
    public string? Category { get; set; }

    [CommandOption("-s|--status")]
    [Description("状态：unmarked、in-progress、completed、delayed 或 0-3")]
    public string? Status { get; set; }

    [CommandOption("--status-remark")]
    [Description("状态备注")]
    public string? StatusRemark { get; set; }

    [CommandOption("--input")]
    [Description("内联 JSON 对象或 @JSON文件路径")]
    public string? Input { get; set; }

    [CommandOption("--stdin")]
    [Description("从标准输入读取 JSON 对象")]
    public bool Stdin { get; set; }

    [CommandOption("--json")]
    [Description("只输出机器可读 JSON；此模式永不进入交互提示")]
    public bool Json { get; set; }
}

public sealed class WorkLogCreateCommand(ApiClient api, TokenStore store) : AsyncCommand<WorkLogWriteSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, WorkLogWriteSettings settings)
        => WorkLogCommandSupport.GuardAsync(settings.Json, async () =>
        {
            WorkLogCommandSupport.RequireLogin(store, settings.Json);
            var input = await WorkLogInput.LoadAsync(settings.Input, settings.Stdin);
            input.Apply(settings);
            input.ValidateKnownFields();

            var title = input.RequiredString("title").Trim();
            if (title.Length > 200) throw new WorkLogInputException("title 不能超过 200 个字符");

            var logDate = input.OptionalDate("logDate") ?? DateTime.Today;
            var status = input.OptionalStatus("status") ?? 0;
            var payload = new
            {
                title,
                logDate,
                purpose = input.OptionalString("purpose"),
                content = input.OptionalString("content"),
                tags = input.OptionalString("tags"),
                category = input.OptionalString("category"),
                status,
                statusRemark = input.OptionalString("statusRemark")
            };

            WorkLogDto created = null!;
            await CommandHelpers.RunAsync(settings.Json, "正在创建工作记录...",
                async () => created = await api.CreateWorkLogAsync(payload));

            if (settings.Json)
                CommandHelpers.WriteJson(new { success = true, data = created });
            else
                AnsiConsole.MarkupLine($"[green]✓ 已创建工作记录 ID={created.Id}：{Markup.Escape(created.Title)}[/]");
            return 0;
        });
}

public class WorkLogUpdateSettings : WorkLogWriteSettings
{
    [CommandArgument(0, "<id>")]
    [Description("工作记录 ID")]
    public int Id { get; set; }
}

public sealed class WorkLogUpdateCommand(ApiClient api, TokenStore store) : AsyncCommand<WorkLogUpdateSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, WorkLogUpdateSettings settings)
        => WorkLogCommandSupport.GuardAsync(settings.Json, async () =>
        {
            WorkLogCommandSupport.RequireLogin(store, settings.Json);
            WorkLogCommandSupport.ValidateId(settings.Id);

            var input = await WorkLogInput.LoadAsync(settings.Input, settings.Stdin);
            input.Apply(settings);
            input.ValidateKnownFields();
            if (!input.HasAnyField)
                throw new WorkLogInputException("没有提供要更新的字段");

            var current = await api.GetWorkLogAsync(settings.Id);
            var title = input.StringOrExisting("title", current.Title)?.Trim();
            if (string.IsNullOrWhiteSpace(title)) throw new WorkLogInputException("title 不能为空");
            if (title.Length > 200) throw new WorkLogInputException("title 不能超过 200 个字符");

            var payload = new
            {
                title,
                logDate = input.DateOrExisting("logDate", current.LogDate),
                purpose = input.StringOrExisting("purpose", current.Purpose),
                content = input.StringOrExisting("content", current.Content),
                tags = input.StringOrExisting("tags", current.Tags),
                category = input.StringOrExisting("category", current.Category),
                status = input.StatusOrExisting("status", current.Status),
                statusRemark = input.StringOrExisting("statusRemark", current.StatusRemark)
            };

            WorkLogDto updated = null!;
            await CommandHelpers.RunAsync(settings.Json, "正在更新工作记录...",
                async () => updated = await api.UpdateWorkLogAsync(settings.Id, payload));

            if (settings.Json)
                CommandHelpers.WriteJson(new { success = true, data = updated });
            else
                AnsiConsole.MarkupLine($"[green]✓ 已更新工作记录 ID={updated.Id}：{Markup.Escape(updated.Title)}[/]");
            return 0;
        });
}

public class WorkLogDeleteSettings : CommandSettings
{
    [CommandArgument(0, "<id>")]
    [Description("工作记录 ID")]
    public int Id { get; set; }

    [CommandOption("-y|--yes")]
    [Description("跳过确认；自动化调用必须提供此参数")]
    public bool Yes { get; set; }

    [CommandOption("--json")]
    [Description("只输出机器可读 JSON；此模式永不进入交互提示")]
    public bool Json { get; set; }
}

public sealed class WorkLogDeleteCommand(ApiClient api, TokenStore store) : AsyncCommand<WorkLogDeleteSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, WorkLogDeleteSettings settings)
        => WorkLogCommandSupport.GuardAsync(settings.Json, async () =>
        {
            WorkLogCommandSupport.RequireLogin(store, settings.Json);
            WorkLogCommandSupport.ValidateId(settings.Id);
            if (settings.Json && !settings.Yes)
                throw new WorkLogInputException("JSON 模式下删除记录必须提供 --yes");
            if (!settings.Yes && !AnsiConsole.Confirm($"确认删除工作记录 ID={settings.Id}？此操作不可恢复。", false))
                return 0;

            await CommandHelpers.RunAsync(settings.Json, "正在删除工作记录...",
                async () => await api.DeleteWorkLogAsync(settings.Id));
            if (settings.Json)
                CommandHelpers.WriteJson(new { success = true, id = settings.Id });
            else
                AnsiConsole.MarkupLine("[green]✓ 已删除。[/]");
            return 0;
        });
}

public class WorkLogCategoriesSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("只输出机器可读 JSON")]
    public bool Json { get; set; }
}

public sealed class WorkLogCategoriesCommand(ApiClient api, TokenStore store) : AsyncCommand<WorkLogCategoriesSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, WorkLogCategoriesSettings settings)
        => WorkLogCommandSupport.GuardAsync(settings.Json, async () =>
        {
            WorkLogCommandSupport.RequireLogin(store, settings.Json);
            List<string> categories = null!;
            await CommandHelpers.RunAsync(settings.Json, "正在读取工作分类...",
                async () => categories = await api.GetWorkLogCategoriesAsync());
            if (settings.Json)
                CommandHelpers.WriteJson(new { success = true, items = categories });
            else if (categories.Count == 0)
                AnsiConsole.MarkupLine("[yellow]还没有工作分类。[/]");
            else
                AnsiConsole.Write(new Rows(categories.Select(x => new Markup($"• {Markup.Escape(x)}"))));
            return 0;
        });
}

public class WorkLogSchemaSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("输出紧凑 JSON")]
    public bool Json { get; set; }
}

public sealed class WorkLogSchemaCommand : Command<WorkLogSchemaSettings>
{
    public override int Execute(CommandContext context, WorkLogSchemaSettings settings)
    {
        var schema = new
        {
            success = true,
            version = 1,
            resource = "worklog",
            invocation = "mirainote worklog <command> [options] --json",
            commands = new
            {
                list = "list [--keyword TEXT] [--from DATE] [--to DATE] [--category TEXT] [--tag TEXT] [--status STATUS] [--page N] [--page-size N] --json",
                get = "get <id> --json",
                create = "create (--title TEXT ... | --input JSON|@FILE | --stdin) --json",
                update = "update <id> (--field VALUE ... | --input JSON|@FILE | --stdin) --json",
                delete = "delete <id> --yes --json",
                categories = "categories --json"
            },
            inputFields = new
            {
                title = new { type = "string", requiredOnCreate = true },
                logDate = new { type = "string", format = "yyyy-MM-dd", defaultOnCreate = "today" },
                purpose = new { type = "string|null" },
                content = new { type = "string|null" },
                tags = new { type = "string|null", description = "comma-separated" },
                category = new { type = "string|null" },
                status = new { type = "string|integer", values = new object[] { "unmarked", "in-progress", "completed", "delayed", 0, 1, 2, 3 } },
                statusRemark = new { type = "string|null" }
            },
            inputRules = new[]
            {
                "Command-line field options override the same fields in --input.",
                "For update, omitted fields are preserved; JSON null clears nullable fields.",
                "--input accepts an inline JSON object or @path/to/file.json; --stdin reads JSON from standard input.",
                "--json never prompts for input or confirmation."
            },
            exitCodes = new { success = 0, apiOrNetworkError = 1, invalidInput = 2, notAuthenticated = 3 },
            examples = new[]
            {
                "mirainote worklog create --title \"修复登录问题\" --date 2026-07-26 --status completed --json",
                "'{\"title\":\"实现 CLI\",\"content\":\"支持多行内容\",\"status\":\"in-progress\"}' | mirainote worklog create --stdin --json",
                "mirainote worklog update 42 --input '{\"status\":\"completed\",\"statusRemark\":null}' --json"
            }
        };

        if (settings.Json)
            CommandHelpers.WriteJson(schema);
        else
            Console.WriteLine(JsonSerializer.Serialize(schema, WorkLogCommandSupport.PrettyJsonOptions));
        return 0;
    }
}

internal sealed class WorkLogInputException(string message) : Exception(message);
internal sealed class WorkLogAuthenticationException(string message) : Exception(message);

internal sealed class WorkLogInput
{
    private static readonly HashSet<string> KnownFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "title", "logDate", "purpose", "content", "tags", "category", "status", "statusRemark"
    };

    private readonly Dictionary<string, JsonNode?> _fields = new(StringComparer.OrdinalIgnoreCase);

    public bool HasAnyField => _fields.Count > 0;

    public static async Task<WorkLogInput> LoadAsync(string? source, bool stdin)
    {
        var result = new WorkLogInput();
        if (stdin && !string.IsNullOrWhiteSpace(source))
            throw new WorkLogInputException("--input 和 --stdin 不能同时使用");
        if (!stdin && string.IsNullOrWhiteSpace(source)) return result;

        string json;
        if (stdin)
            json = await Console.In.ReadToEndAsync();
        else if (source!.StartsWith('@'))
        {
            var path = source[1..];
            if (string.IsNullOrWhiteSpace(path)) throw new WorkLogInputException("@ 后必须提供 JSON 文件路径");
            json = await File.ReadAllTextAsync(path);
        }
        else
            json = source;

        if (string.IsNullOrWhiteSpace(json)) throw new WorkLogInputException("input 不能为空");
        var node = JsonNode.Parse(json) ?? throw new WorkLogInputException("input 必须是 JSON 对象");
        if (node is not JsonObject obj) throw new WorkLogInputException("input 必须是 JSON 对象");
        foreach (var pair in obj)
            result._fields[pair.Key] = pair.Value?.DeepClone();
        return result;
    }

    public void Apply(WorkLogWriteSettings settings)
    {
        SetIfProvided("title", settings.Title);
        SetIfProvided("logDate", settings.Date);
        SetIfProvided("purpose", settings.Purpose);
        SetIfProvided("content", settings.Content);
        SetIfProvided("tags", settings.Tags);
        SetIfProvided("category", settings.Category);
        SetIfProvided("status", settings.Status);
        SetIfProvided("statusRemark", settings.StatusRemark);
    }

    public void ValidateKnownFields()
    {
        var unknown = _fields.Keys.Where(x => !KnownFields.Contains(x)).OrderBy(x => x).ToArray();
        if (unknown.Length > 0)
            throw new WorkLogInputException($"未知字段：{string.Join(", ", unknown)}");
    }

    public string RequiredString(string name)
    {
        var value = OptionalString(name);
        if (string.IsNullOrWhiteSpace(value)) throw new WorkLogInputException($"{name} 为必填字段");
        return value;
    }

    public string? OptionalString(string name)
    {
        if (!_fields.TryGetValue(name, out var node) || node is null) return null;
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
            return CommandHelpers.OrNull(text);
        throw new WorkLogInputException($"{name} 必须是字符串或 null");
    }

    public DateTime? OptionalDate(string name)
    {
        if (!_fields.ContainsKey(name)) return null;
        var text = OptionalString(name);
        if (text is null) throw new WorkLogInputException($"{name} 不能为 null");
        return WorkLogCommandSupport.ParseDate(text, name);
    }

    public byte? OptionalStatus(string name)
    {
        if (!_fields.TryGetValue(name, out var node)) return null;
        if (node is null) throw new WorkLogInputException($"{name} 不能为 null");
        if (node is JsonValue value)
        {
            if (value.TryGetValue<byte>(out var number)) return WorkLogCommandSupport.ValidateStatus(number);
            if (value.TryGetValue<int>(out var integer) && integer is >= 0 and <= 3) return (byte)integer;
            if (value.TryGetValue<string>(out var text)) return WorkLogCommandSupport.ParseStatus(text);
        }
        throw new WorkLogInputException($"{name} 必须是状态名称或 0-3 的整数");
    }

    public string? StringOrExisting(string name, string? existing)
        => _fields.ContainsKey(name) ? OptionalString(name) : existing;

    public DateTime DateOrExisting(string name, DateTime existing)
        => _fields.ContainsKey(name) ? OptionalDate(name)!.Value : existing;

    public byte StatusOrExisting(string name, byte existing)
        => _fields.ContainsKey(name) ? OptionalStatus(name)!.Value : existing;

    private void SetIfProvided(string name, string? value)
    {
        if (value is not null) _fields[name] = JsonValue.Create(value);
    }
}

internal static class WorkLogCommandSupport
{
    public static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static async Task<int> GuardAsync(bool json, Func<Task<int>> action)
    {
        try
        {
            return await action();
        }
        catch (WorkLogAuthenticationException ex)
        {
            WriteError(json, "not_authenticated", ex.Message);
            return 3;
        }
        catch (WorkLogInputException ex)
        {
            WriteError(json, "invalid_input", ex.Message);
            return 2;
        }
        catch (JsonException ex)
        {
            WriteError(json, "invalid_json", ex.Message);
            return 2;
        }
        catch (IOException ex)
        {
            WriteError(json, "input_io_error", ex.Message);
            return 2;
        }
        catch (ApiException ex)
        {
            WriteError(json, "api_error", ex.Message);
            return 1;
        }
        catch (HttpRequestException ex)
        {
            WriteError(json, "network_error", ex.Message);
            return 1;
        }
        catch (TaskCanceledException)
        {
            WriteError(json, "timeout", "请求超时，请检查 API 服务状态");
            return 1;
        }
    }

    public static void RequireLogin(TokenStore store, bool json)
    {
        if (!store.HasToken)
            throw new WorkLogAuthenticationException("未登录，请先执行 mirainote login");
    }

    public static void ValidateId(int id)
    {
        if (id <= 0) throw new WorkLogInputException("id 必须是正整数");
    }

    public static DateTime? ParseOptionalDate(string? value, string name)
        => string.IsNullOrWhiteSpace(value) ? null : ParseDate(value, name);

    public static DateTime ParseDate(string value, string name)
    {
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            throw new WorkLogInputException($"{name} 必须使用 yyyy-MM-dd 格式");
        return date;
    }

    public static byte? ParseOptionalStatus(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : ParseStatus(value);

    public static byte ParseStatus(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
        return normalized switch
        {
            "0" or "unmarked" or "none" => 0,
            "1" or "in-progress" or "progress" or "doing" => 1,
            "2" or "completed" or "complete" or "done" => 2,
            "3" or "delayed" or "overdue" => 3,
            _ => throw new WorkLogInputException("status 必须是 unmarked、in-progress、completed、delayed 或 0-3")
        };
    }

    public static byte ValidateStatus(byte status)
    {
        if (status > 3) throw new WorkLogInputException("status 必须在 0 到 3 之间");
        return status;
    }

    public static (string Label, string Color) StatusDisplay(byte status) => status switch
    {
        1 => ("进行中", "blue"),
        2 => ("已完成", "green"),
        3 => ("已延期", "red"),
        _ => ("未标记", "grey")
    };

    public static void RenderDetails(WorkLogDto item)
    {
        var (status, _) = StatusDisplay(item.Status);
        var grid = new Grid().AddColumn().AddColumn();
        grid.AddRow("[grey]ID[/]", item.Id.ToString(CultureInfo.InvariantCulture));
        grid.AddRow("[grey]日期[/]", Markup.Escape(item.LogDate.ToString("yyyy-MM-dd")));
        grid.AddRow("[grey]标题[/]", Markup.Escape(item.Title));
        grid.AddRow("[grey]状态[/]", Markup.Escape(status));
        grid.AddRow("[grey]状态备注[/]", Markup.Escape(item.StatusRemark ?? "-"));
        grid.AddRow("[grey]分类[/]", Markup.Escape(item.Category ?? "-"));
        grid.AddRow("[grey]标签[/]", Markup.Escape(item.Tags ?? "-"));
        grid.AddRow("[grey]目的[/]", Markup.Escape(item.Purpose ?? "-"));
        grid.AddRow("[grey]内容[/]", Markup.Escape(item.Content ?? "-"));
        AnsiConsole.Write(new Panel(grid).Header($"工作记录 #{item.Id}").Border(BoxBorder.Rounded));
    }

    private static void WriteError(bool json, string code, string message)
    {
        if (json)
            CommandHelpers.WriteJson(new { success = false, error = new { code, message } });
        else
            AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(message)}[/]");
    }
}

// 其他 CLI 命令共用的输出辅助方法。
internal static partial class CommandHelpers
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static bool EnsureLoggedIn(TokenStore store, bool json = false)
    {
        if (store.HasToken) return true;
        if (json) WriteJson(new { success = false, error = "未登录，请先执行 mirainote login" });
        else AnsiConsole.MarkupLine("[red]✗ 请先执行 [bold]mirainote login[/] 登录。[/]");
        return false;
    }

    public static string? OrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    public static void WriteJson(object value)
        => Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    public static int HandleError(string message, bool json)
    {
        if (json) WriteJson(new { success = false, error = message });
        else AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(message)}[/]");
        return 1;
    }

    public static async Task RunAsync(bool json, string statusMessage, Func<Task> action)
    {
        if (json)
        {
            await action();
            return;
        }

        await AnsiConsole.Status().StartAsync(statusMessage, async _ => await action());
    }
}
