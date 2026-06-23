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

    [CommandOption("--deepseek-key")]
    [Description("设置 DeepSeek API Key（持久化到本地）")]
    public string? DeepSeekKey { get; set; }

    [CommandOption("--deepseek-url")]
    [Description("设置 DeepSeek API 地址（默认 https://api.deepseek.com）")]
    public string? DeepSeekUrl { get; set; }

    [CommandOption("--deepseek-model")]
    [Description("设置 DeepSeek 模型（默认 deepseek-chat）")]
    public string? DeepSeekModel { get; set; }

    [CommandOption("--tavily-key")]
    [Description("设置 Tavily API Key（互联网搜索）")]
    public string? TavilyKey { get; set; }

    [CommandOption("--smtp-host")]
    [Description("SMTP 服务器地址")]
    public string? SmtpHost { get; set; }

    [CommandOption("--smtp-port")]
    [Description("SMTP 端口（默认 587）")]
    public int? SmtpPort { get; set; }

    [CommandOption("--smtp-user")]
    [Description("SMTP 用户名")]
    public string? SmtpUser { get; set; }

    [CommandOption("--smtp-password")]
    [Description("SMTP 密码")]
    public string? SmtpPassword { get; set; }

    [CommandOption("--smtp-from")]
    [Description("发件人邮箱地址")]
    public string? SmtpFromAddress { get; set; }

    [CommandOption("--smtp-name")]
    [Description("发件人名称（默认 MiraiNote）")]
    public string? SmtpFromName { get; set; }

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
        bool anySet = false;

        if (!string.IsNullOrWhiteSpace(settings.ApiUrl))
        {
            _store.SaveApiBase(settings.ApiUrl);
            if (!settings.Json) AnsiConsole.MarkupLine($"[green]✓ API 地址已设置为：{settings.ApiUrl}[/]");
            anySet = true;
        }

        // DeepSeek 配置
        bool dsChanged = settings.DeepSeekKey != null
                      || settings.DeepSeekUrl != null
                      || settings.DeepSeekModel != null;

        if (dsChanged)
        {
            _store.SaveDeepSeekConfig(settings.DeepSeekKey, settings.DeepSeekUrl, settings.DeepSeekModel);

            if (!settings.Json)
            {
                if (settings.DeepSeekKey != null)
                {
                    if (string.IsNullOrWhiteSpace(settings.DeepSeekKey))
                        AnsiConsole.MarkupLine("[green]✓ DeepSeek API Key 已清空[/]");
                    else
                    {
                        var masked = settings.DeepSeekKey.Length > 8
                            ? settings.DeepSeekKey[..5] + "***" + settings.DeepSeekKey[^4..]
                            : "***";
                        AnsiConsole.MarkupLine($"[green]✓ DeepSeek API Key 已设置：{masked}[/]");
                    }
                }
                if (settings.DeepSeekUrl != null)
                {
                    if (string.IsNullOrWhiteSpace(settings.DeepSeekUrl))
                        AnsiConsole.MarkupLine("[green]✓ DeepSeek API 地址已重置为默认[/]");
                    else
                        AnsiConsole.MarkupLine($"[green]✓ DeepSeek API 地址：{settings.DeepSeekUrl}[/]");
                }
                if (settings.DeepSeekModel != null)
                {
                    if (string.IsNullOrWhiteSpace(settings.DeepSeekModel))
                        AnsiConsole.MarkupLine("[green]✓ DeepSeek 模型已重置为默认[/]");
                    else
                        AnsiConsole.MarkupLine($"[green]✓ DeepSeek 模型：{settings.DeepSeekModel}[/]");
                }
            }
            anySet = true;
        }

        // Tavily 配置
        if (settings.TavilyKey != null)
        {
            _store.SaveTavilyConfig(settings.TavilyKey);
            if (!settings.Json)
            {
                if (string.IsNullOrWhiteSpace(settings.TavilyKey))
                    AnsiConsole.MarkupLine("[green]✓ Tavily API Key 已清空[/]");
                else
                {
                    var masked = settings.TavilyKey.Length > 8
                        ? settings.TavilyKey[..5] + "***" + settings.TavilyKey[^4..]
                        : "***";
                    AnsiConsole.MarkupLine($"[green]✓ Tavily API Key 已设置：{masked}[/]");
                }
            }
            anySet = true;
        }

        // SMTP 配置
        bool smtpChanged = settings.SmtpHost != null || settings.SmtpPort != null
                        || settings.SmtpUser != null || settings.SmtpPassword != null
                        || settings.SmtpFromAddress != null || settings.SmtpFromName != null;

        if (smtpChanged)
        {
            _store.SaveSmtpConfig(settings.SmtpHost, settings.SmtpPort,
                settings.SmtpUser, settings.SmtpPassword,
                settings.SmtpFromAddress, settings.SmtpFromName);

            if (!settings.Json)
            {
                if (settings.SmtpHost != null)
                    AnsiConsole.MarkupLine(string.IsNullOrWhiteSpace(settings.SmtpHost)
                        ? "[green]✓ SMTP 主机已清空[/]"
                        : $"[green]✓ SMTP 主机：{settings.SmtpHost}[/]");
                if (settings.SmtpPort != null)
                    AnsiConsole.MarkupLine($"[green]✓ SMTP 端口：{settings.SmtpPort}[/]");
                if (settings.SmtpUser != null)
                    AnsiConsole.MarkupLine(string.IsNullOrWhiteSpace(settings.SmtpUser)
                        ? "[green]✓ SMTP 用户已清空[/]"
                        : $"[green]✓ SMTP 用户：{settings.SmtpUser}[/]");
                if (settings.SmtpPassword != null)
                    AnsiConsole.MarkupLine(string.IsNullOrWhiteSpace(settings.SmtpPassword)
                        ? "[green]✓ SMTP 密码已清空[/]"
                        : "[green]✓ SMTP 密码已设置（***）[/]");
                if (settings.SmtpFromAddress != null)
                    AnsiConsole.MarkupLine(string.IsNullOrWhiteSpace(settings.SmtpFromAddress)
                        ? "[green]✓ 发件人地址已清空[/]"
                        : $"[green]✓ 发件人地址：{settings.SmtpFromAddress}[/]");
                if (settings.SmtpFromName != null)
                    AnsiConsole.MarkupLine(string.IsNullOrWhiteSpace(settings.SmtpFromName)
                        ? "[green]✓ 发件人名称已清空[/]"
                        : $"[green]✓ 发件人名称：{settings.SmtpFromName}[/]");
            }
            anySet = true;
        }

        if (anySet)
        {
            if (settings.Json)
                CommandHelpers.WriteJson(new { success = true });
            return 0;
        }

        // 显示当前配置
        if (settings.Json)
        {
            CommandHelpers.WriteJson(new
            {
                apiBase = _store.ApiBase,
                username = _store.Username,
                loggedIn = _store.HasToken,
                deepSeekConfigured = !string.IsNullOrWhiteSpace(_store.DeepSeekApiKey),
                deepSeekBaseUrl = _store.DeepSeekBaseUrl,
                deepSeekModel = _store.DeepSeekModel,
                tavilyConfigured = !string.IsNullOrWhiteSpace(_store.TavilyApiKey),
                smtpConfigured = !string.IsNullOrWhiteSpace(_store.SmtpHost) && !string.IsNullOrWhiteSpace(_store.SmtpUser),
                smtpHost = _store.SmtpHost,
                smtpPort = _store.SmtpPort,
                smtpUser = _store.SmtpUser,
                smtpFromAddress = _store.SmtpFromAddress,
                smtpFromName = _store.SmtpFromName
            });
            return 0;
        }

        AnsiConsole.MarkupLine($"API 地址：[cyan]{_store.ApiBase}[/]");
        AnsiConsole.MarkupLine($"登录用户：[cyan]{(_store.Username ?? "(未登录)")}[/]");
        AnsiConsole.WriteLine();

        // DeepSeek
        AnsiConsole.MarkupLine("[bold]DeepSeek[/]");
        if (!string.IsNullOrWhiteSpace(_store.DeepSeekApiKey))
        {
            var masked = _store.DeepSeekApiKey.Length > 8
                ? _store.DeepSeekApiKey[..5] + "***" + _store.DeepSeekApiKey[^4..]
                : "***";
            AnsiConsole.MarkupLine($"  Key：[cyan]{masked}[/]");
        }
        else
            AnsiConsole.MarkupLine($"  Key：[grey](未配置)[/]");
        AnsiConsole.MarkupLine($"  URL：[cyan]{_store.DeepSeekBaseUrl}[/]");
        AnsiConsole.MarkupLine($"  模型：[cyan]{_store.DeepSeekModel}[/]");
        AnsiConsole.WriteLine();

        // Tavily
        AnsiConsole.MarkupLine("[bold]Tavily（互联网搜索）[/]");
        if (!string.IsNullOrWhiteSpace(_store.TavilyApiKey))
        {
            var masked = _store.TavilyApiKey.Length > 8
                ? _store.TavilyApiKey[..5] + "***" + _store.TavilyApiKey[^4..]
                : "***";
            AnsiConsole.MarkupLine($"  Key：[cyan]{masked}[/]");
        }
        else
            AnsiConsole.MarkupLine($"  Key：[grey](未配置)[/]");
        AnsiConsole.WriteLine();

        // SMTP
        AnsiConsole.MarkupLine("[bold]SMTP（邮件发送）[/]");
        if (!string.IsNullOrWhiteSpace(_store.SmtpHost))
        {
            AnsiConsole.MarkupLine($"  主机：[cyan]{_store.SmtpHost}[/]");
            AnsiConsole.MarkupLine($"  端口：[cyan]{_store.SmtpPort}[/]");
            AnsiConsole.MarkupLine($"  用户：[cyan]{_store.SmtpUser}[/]");
            AnsiConsole.MarkupLine($"  密码：[cyan]{(string.IsNullOrWhiteSpace(_store.SmtpPassword) ? "(未设置)" : "***")}[/]");
            if (!string.IsNullOrWhiteSpace(_store.SmtpFromAddress))
                AnsiConsole.MarkupLine($"  发件地址：[cyan]{_store.SmtpFromAddress}[/]");
            if (!string.IsNullOrWhiteSpace(_store.SmtpFromName))
                AnsiConsole.MarkupLine($"  发件名称：[cyan]{_store.SmtpFromName}[/]");
        }
        else
            AnsiConsole.MarkupLine($"  [grey](未配置)[/]");

        return 0;
    }
}
