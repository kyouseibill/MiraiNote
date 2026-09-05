using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using MiraiNote.Shared.Agent;

namespace MiraiNote.CLI.Agent.Tools;

// ===== 互联网搜索 =====

public class InternetSearchTool : IAgentTool
{
    private readonly string? _apiKey;
    private readonly HttpClient _http;

    public InternetSearchTool(string? tavilyApiKey)
    {
        _apiKey = tavilyApiKey;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public string Name => "search_internet";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Safe;
    public string Description =>
        "搜索互联网公开信息。适用于：天气预报、新闻、知识问答、技术资料、政策法规等。" +
        "用户个人数据（工作记录/备忘/日记）不通过此工具查询。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["query"] = ToolParameterProperty.String("搜索查询词（必填）")
        },
        Required = new() { "query" }
    };

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return "互联网搜索功能未配置（Tavily API Key 为空）。";

        using var doc = JsonDocument.Parse(argumentsJson);
        var args = doc.RootElement;

        if (!ToolArgHelper.TryGetString(args, "query", out var query))
            return "搜索失败：未提供 query。";

        try
        {
            var body = new { api_key = _apiKey, query, max_results = 5, search_depth = "basic" };
            var resp = await _http.PostAsJsonAsync("https://api.tavily.com/search", body, ct);

            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                return $"搜索失败 ({(int)resp.StatusCode})：{err[..Math.Min(200, err.Length)]}";
            }

            using var resultDoc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (!resultDoc.RootElement.TryGetProperty("results", out var resultsEl))
                return "搜索无结果。";

            var results = resultsEl.EnumerateArray().Take(5).Select(r => new
            {
                title = r.TryGetProperty("title", out var t) ? t.GetString() : null,
                url = r.TryGetProperty("url", out var u) ? u.GetString() : null,
                content = r.TryGetProperty("content", out var c) ? c.GetString() : null
            }).ToList();

            if (results.Count == 0) return "搜索无结果。";
            return JsonSerializer.Serialize(results);
        }
        catch (TaskCanceledException)
        {
            return "搜索超时，请稍后重试。";
        }
    }
}

// ===== 邮件发送 =====

public class SendEmailTool : IAgentTool
{
    private readonly string? _smtpHost;
    private readonly int _smtpPort;
    private readonly string? _smtpUser;
    private readonly string? _smtpPassword;
    private readonly string? _fromAddress;
    private readonly string? _fromName;

    public string Name => "send_email";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Dangerous;
    public string Description =>
        string.IsNullOrWhiteSpace(_smtpHost)
            ? "发送邮件（SMTP 未配置，设置 SMTP_HOST 等环境变量后可用）"
            : "使用系统 SMTP 配置发送邮件。需用户确认后执行。适用于发送周报、提醒、通知等。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["to"] = ToolParameterProperty.String("收件人邮箱地址（必填）"),
            ["subject"] = ToolParameterProperty.String("邮件主题（必填）"),
            ["body"] = ToolParameterProperty.String("邮件正文（必填）")
        },
        Required = new() { "to", "subject", "body" }
    };

    public SendEmailTool(string? smtpHost, int smtpPort, string? smtpUser, string? smtpPassword, string? fromAddress = null, string? fromName = null)
    {
        _smtpHost = smtpHost;
        _smtpPort = smtpPort;
        _smtpUser = smtpUser;
        _smtpPassword = smtpPassword;
        _fromAddress = fromAddress;
        _fromName = fromName;
    }

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_smtpHost) || string.IsNullOrWhiteSpace(_smtpUser))
            return "邮件发送未配置（SMTP_HOST / SMTP_USER 等环境变量未设置）。";

        var args = JsonDocument.Parse(argumentsJson).RootElement;
        if (!ToolArgHelper.TryGetString(args, "to", out var to))
            return "发送失败：未提供 to（收件人）。";
        if (!ToolArgHelper.TryGetString(args, "subject", out var subject))
            return "发送失败：未提供 subject（主题）。";
        if (!ToolArgHelper.TryGetString(args, "body", out var body))
            return "发送失败：未提供 body（正文）。";

        if (!to.Contains('@') || !to.Contains('.'))
            return $"发送失败：收件人邮箱「{to}」格式不正确。";

        try
        {
            var message = new MimeKit.MimeMessage();
            var fromEmail = !string.IsNullOrWhiteSpace(_fromAddress) ? _fromAddress : _smtpUser;
            message.From.Add(new MimeKit.MailboxAddress(_fromName ?? "MiraiNote", fromEmail));
            message.To.Add(MimeKit.MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new MimeKit.BodyBuilder
            {
                HtmlBody = WrapHtml(body)
            }.ToMessageBody();

            using var client = new MailKit.Net.Smtp.SmtpClient();
            await client.ConnectAsync(_smtpHost, _smtpPort, MailKit.Security.SecureSocketOptions.StartTls, ct);
            await client.AuthenticateAsync(_smtpUser, _smtpPassword, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            return $"邮件已成功发送至 {to}，主题：「{subject}」。";
        }
        catch (Exception ex)
        {
            return $"邮件发送失败：{ex.Message}";
        }
    }

    private static string WrapHtml(string text)
    {
        var escaped = System.Net.WebUtility.HtmlEncode(text)
            .Replace("\r\n", "<br/>").Replace("\n", "<br/>");
        return $"<!DOCTYPE html><html lang='zh-CN'><head><meta charset='UTF-8'/></head>" +
               $"<body style='font-family:sans-serif;padding:20px;color:#333;'>" +
               $"<div>{escaped}</div>" +
               $"<hr style='margin-top:24px;border:none;border-top:1px solid #e5e7eb;'/>" +
               $"<p style='font-size:12px;color:#9ca3af;'>此邮件由 MiraiNote CLI Agent 代用户发送。</p>" +
               $"</body></html>";
    }
}

// ===== 文件读取 =====

public class FileReadTool : IAgentTool
{
    public string Name => "read_file";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Safe;
    public string Description =>
        "读取本地文件内容。支持文本文件（.txt .md .json .cs .csproj .sln .py .js .ts .html .css .yaml .yml .env .gitignore 等）。" +
        "当用户要求查看文件内容、阅读代码、检查配置时调用。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["path"] = ToolParameterProperty.String("文件路径，支持绝对路径和相对路径（必填）"),
            ["max_lines"] = ToolParameterProperty.Integer("最大读取行数，默认 500")
        },
        Required = new() { "path" }
    };

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var args = doc.RootElement;

        if (!ToolArgHelper.TryGetString(args, "path", out var path))
            return Task.FromResult("读取失败：path 为必填项。");

        // 解析相对路径
        if (!Path.IsPathRooted(path))
            path = Path.Combine(Directory.GetCurrentDirectory(), path);

        if (!File.Exists(path))
            return Task.FromResult($"文件不存在：{path}");

        ToolArgHelper.TryGetInt(args, "max_lines", out var maxLines);
        if (maxLines <= 0) maxLines = 500;

        try
        {
            var lines = File.ReadAllLines(path);
            var total = lines.Length;

            if (total <= maxLines)
                return Task.FromResult(string.Join("\n", lines));

            // 超过限制时给出摘要
            var preview = string.Join("\n", lines.Take(maxLines));
            return Task.FromResult($"{preview}\n\n... (共 {total} 行，以上为前 {maxLines} 行)");
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult($"没有读取权限：{path}");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"读取文件失败：{ex.Message}");
        }
    }
}

// ===== 文件写入 =====

public class FileWriteTool : IAgentTool
{
    public string Name => "write_file";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Write;
    public string Description =>
        "将内容写入本地文件（覆盖写入）。用于保存代码、配置、文档等。" +
        "仅在用户明确要求写入文件时调用，操作前应告知用户目标路径。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["path"] = ToolParameterProperty.String("文件路径（必填）"),
            ["content"] = ToolParameterProperty.String("要写入的内容（必填）")
        },
        Required = new() { "path", "content" }
    };

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var args = doc.RootElement;

        if (!ToolArgHelper.TryGetString(args, "path", out var path))
            return Task.FromResult("写入失败：path 为必填项。");
        if (!ToolArgHelper.TryGetString(args, "content", out var content))
            return Task.FromResult("写入失败：content 为必填项。");

        if (!Path.IsPathRooted(path))
            path = Path.Combine(Directory.GetCurrentDirectory(), path);

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, content, System.Text.Encoding.UTF8);
            return Task.FromResult($"已成功写入文件：{path} ({content.Length} 字符)");
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult($"没有写入权限：{path}");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"写入文件失败：{ex.Message}");
        }
    }
}

// ===== 文件列表 =====

public class FileListTool : IAgentTool
{
    public string Name => "list_files";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Safe;
    public string Description =>
        "列出目录中的文件和子目录。当用户要求查看目录内容、浏览项目结构时调用。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["path"] = ToolParameterProperty.String("目录路径，不填默认当前目录"),
            ["pattern"] = ToolParameterProperty.String("文件匹配模式，如 *.cs，不填列出所有")
        }
    };

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var args = doc.RootElement;

        ToolArgHelper.TryGetString(args, "path", out var path);
        if (string.IsNullOrWhiteSpace(path))
            path = Directory.GetCurrentDirectory();
        if (!Path.IsPathRooted(path))
            path = Path.Combine(Directory.GetCurrentDirectory(), path);

        if (!Directory.Exists(path))
            return Task.FromResult($"目录不存在：{path}");

        ToolArgHelper.TryGetString(args, "pattern", out var pattern);
        if (string.IsNullOrWhiteSpace(pattern)) pattern = "*";

        try
        {
            var entries = new List<object>();

            // 目录
            foreach (var dir in Directory.GetDirectories(path))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.StartsWith(".") || dirName == "node_modules" || dirName == "bin" || dirName == "obj") continue;
                entries.Add(new { type = "dir", name = dirName, path = dir });
            }

            // 文件（限制数量）
            var files = Directory.GetFiles(path, pattern).Take(100).ToList();
            entries.AddRange(files.Select(f => new
            {
                type = "file",
                name = Path.GetFileName(f),
                size = new FileInfo(f).Length,
                path = f
            }));

            return Task.FromResult(JsonSerializer.Serialize(new
            {
                path,
                count = entries.Count,
                entries = entries.Take(100)
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult($"列出目录失败：{ex.Message}");
        }
    }
}

// ===== Shell 命令执行 =====

public class ShellTool : IAgentTool
{
    // 安全黑名单：禁止危险命令
    private static readonly HashSet<string> DangerousCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "rm -rf /", "del /f /s", "format", "shutdown", "reboot",
        "dd if=", "mkfs", ":(){ :|:& };:", "chmod 777 /"
    };

    public string Name => "run_shell";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Dangerous;
    public string Description =>
        "执行 Shell 命令并返回输出。支持常用的开发命令：dotnet、git、npm、dir/ls、type/cat 等。" +
        "禁止执行破坏性命令（rm -rf、format 等）。命令会持续运行直到完成，也可由用户主动停止。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["command"] = ToolParameterProperty.String("要执行的命令（必填）"),
            ["working_dir"] = ToolParameterProperty.String("工作目录，不填默认当前目录")
        },
        Required = new() { "command" }
    };

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var args = doc.RootElement;

        if (!ToolArgHelper.TryGetString(args, "command", out var command))
            return "执行失败：command 为必填项。";

        // 安全检查
        if (DangerousCommands.Any(dc => command.Contains(dc, StringComparison.OrdinalIgnoreCase)))
            return $"安全限制：禁止执行危险命令。";

        ToolArgHelper.TryGetString(args, "working_dir", out var workingDir);
        if (string.IsNullOrWhiteSpace(workingDir))
            workingDir = Directory.GetCurrentDirectory();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;
            // 不设置固定时限：长时间下载/构建命令持续运行，用户取消时由 ct 终止等待。
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                throw;
            }
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            var output = stdout.TrimEnd();
            if (!string.IsNullOrEmpty(stderr))
                output += (output.Length > 0 ? "\n" : "") + $"[stderr]\n{stderr.TrimEnd()}";

            if (string.IsNullOrEmpty(output))
                output = $"(exit code: {process.ExitCode})";

            return output.Length > 5000 ? output[..5000] + "\n... (输出已截断)" : output;
        }
        catch (Exception ex)
        {
            return $"执行命令失败：{ex.Message}";
        }
    }
}

// ===== 系统信息 =====

public class SystemInfoTool : IAgentTool
{
    public string Name => "system_info";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Safe;
    public string Description =>
        "获取当前系统信息：操作系统、当前目录、环境变量等。" +
        "当用户询问系统状态、当前环境时调用。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["info_type"] = ToolParameterProperty.String("信息类型：os / cwd / env / all，默认 all")
        }
    };

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var args = doc.RootElement;

        ToolArgHelper.TryGetString(args, "info_type", out var infoType);
        if (string.IsNullOrWhiteSpace(infoType)) infoType = "all";

        var result = new Dictionary<string, object>();

        if (infoType is "os" or "all")
        {
            result["os"] = new
            {
                platform = Environment.OSVersion.Platform.ToString(),
                version = Environment.OSVersion.VersionString,
                is64bit = Environment.Is64BitOperatingSystem,
                machineName = Environment.MachineName,
                processorCount = Environment.ProcessorCount,
                clrVersion = Environment.Version?.ToString()
            };
        }

        if (infoType is "cwd" or "all")
        {
            result["currentDirectory"] = Directory.GetCurrentDirectory();
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => new { name = d.Name, type = d.DriveType.ToString(),
                    totalGB = d.TotalSize / 1_073_741_824.0, freeGB = d.AvailableFreeSpace / 1_073_741_824.0 });
            result["drives"] = drives;
        }

        if (infoType is "env" or "all")
        {
            var envVars = Environment.GetEnvironmentVariables();
            var safeEnv = new Dictionary<string, string>();
            foreach (var key in envVars.Keys)
            {
                var keyStr = key.ToString()!;
                // 过滤敏感信息
                if (keyStr.Contains("KEY", StringComparison.OrdinalIgnoreCase) ||
                    keyStr.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) ||
                    keyStr.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
                    keyStr.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase))
                    continue;
                var valStr = envVars[key]?.ToString() ?? "";
                if (valStr.Length > 200) valStr = valStr[..200] + "...";
                safeEnv[keyStr] = valStr;
            }
            result["environmentVariables"] = safeEnv;
        }

        return Task.FromResult(JsonSerializer.Serialize(result));
    }
}
