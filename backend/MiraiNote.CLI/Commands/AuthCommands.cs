using MiraiNote.CLI.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace MiraiNote.CLI.Commands;

// ===== login =====

public class LoginSettings : CommandSettings
{
    [CommandOption("-u|--username")]
    [Description("用户名")]
    public string? Username { get; set; }

    [CommandOption("-p|--password")]
    [Description("密码（Agent 模式用，避免在终端记录密码时请使用交互输入）")]
    public string? Password { get; set; }

    [CommandOption("--json")]
    [Description("输出 JSON（Agent 友好模式）")]
    public bool Json { get; set; } = false;
}

public class LoginCommand : AsyncCommand<LoginSettings>
{
    private readonly ApiClient   _api;
    private readonly TokenStore  _store;

    public LoginCommand(ApiClient api, TokenStore store)
    {
        _api   = api;
        _store = store;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, LoginSettings settings)
    {
        // Agent 模式：username 和 password 都通过 flag 提供
        bool agentMode = settings.Username != null && settings.Password != null;

        if (!agentMode)
        {
            AnsiConsole.Write(new FigletText("MiraiNote").Color(Color.Cyan1));
            AnsiConsole.MarkupLine("[bold cyan]个人助理系统 CLI[/]");
            AnsiConsole.WriteLine();

            if (_store.HasToken)
            {
                AnsiConsole.MarkupLine($"[yellow]当前已以 [bold]{_store.Username}[/] 身份登录。[/]");
                if (!AnsiConsole.Confirm("重新登录？", false))
                    return 0;
            }
        }

        var username = settings.Username ?? AnsiConsole.Ask<string>("用户名：");
        var password = settings.Password ?? AnsiConsole.Prompt(new TextPrompt<string>("密码：").Secret());

        try
        {
            AuthTokens tokens = null!;
            await CommandHelpers.RunAsync(settings.Json, "登录中...",
                async () => tokens = await _api.LoginAsync(username, password));

            _store.SaveToken(tokens.AccessToken, username);

            if (settings.Json)
            {
                CommandHelpers.WriteJson(new { success = true, username, message = "登录成功" });
                return 0;
            }
            AnsiConsole.MarkupLine($"[green]✓ 登录成功！欢迎，[bold]{username}[/][/]");
            return 0;
        }
        catch (ApiException ex)
        {
            return CommandHelpers.HandleError($"登录失败：{ex.Message}", settings.Json);
        }
    }
}

// ===== logout =====

public class LogoutSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("输出 JSON（Agent 友好模式）")]
    public bool Json { get; set; } = false;
}

public class LogoutCommand : AsyncCommand<LogoutSettings>
{
    private readonly ApiClient  _api;
    private readonly TokenStore _store;

    public LogoutCommand(ApiClient api, TokenStore store)
    {
        _api   = api;
        _store = store;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, LogoutSettings settings)
    {
        if (!_store.HasToken)
        {
            if (settings.Json) CommandHelpers.WriteJson(new { success = false, error = "当前未登录" });
            else AnsiConsole.MarkupLine("[yellow]当前未登录。[/]");
            return 0;
        }
        await _api.LogoutAsync();
        _store.ClearToken();
        if (settings.Json) { CommandHelpers.WriteJson(new { success = true, message = "已成功注销" }); return 0; }
        AnsiConsole.MarkupLine("[green]✓ 已成功注销。[/]");
        return 0;
    }
}

// ===== config =====

public class ConfigSettings : CommandSettings
{
    [CommandOption("--api-url")]
    [Description("设置 API 服务地址（如 http://localhost:5273）")]
    public string? ApiUrl { get; set; }

    [CommandOption("--json")]
    [Description("输出 JSON（Agent 友好模式）")]
    public bool Json { get; set; } = false;
}

public class ConfigCommand : Command<ConfigSettings>
{
    private readonly TokenStore _store;
    public ConfigCommand(TokenStore store) { _store = store; }

    public override int Execute(CommandContext context, ConfigSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ApiUrl))
        {
            _store.SaveApiBase(settings.ApiUrl);
            if (settings.Json) { CommandHelpers.WriteJson(new { success = true, apiBase = settings.ApiUrl }); return 0; }
            AnsiConsole.MarkupLine($"[green]✓ API 地址已设置为：{settings.ApiUrl}[/]");
        }
        else
        {
            if (settings.Json)
            {
                CommandHelpers.WriteJson(new { apiBase = _store.ApiBase, username = _store.Username, loggedIn = _store.HasToken });
                return 0;
            }
            AnsiConsole.MarkupLine($"当前 API 地址：[cyan]{_store.ApiBase}[/]");
            AnsiConsole.MarkupLine($"当前登录用户：[cyan]{(_store.Username ?? "(未登录)")}[/]");
        }
        return 0;
    }
}
