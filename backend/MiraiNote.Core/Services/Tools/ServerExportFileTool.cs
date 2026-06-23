using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MiraiNote.Shared.Agent;

namespace MiraiNote.Core.Services.Tools;

/// <summary>
/// 文件导出工具。将内容保存到 uploads 目录，返回可访问的文件 URL。
/// </summary>
public class ServerExportFileTool : IServerAgentTool
{
    private readonly UploadOptions _uploadOptions;
    private readonly IHostEnvironment _hostEnv;

    public string Name => "export_file";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Write;
    public string Description =>
        "将内容导出为文件并保存。支持 Markdown、JSON、TXT、CSV 等格式。" +
        "文件保存后返回下载路径，用户可通过链接访问。" +
        "适用于导出周报、备忘列表、数据备份等场景。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["filename"] = ToolParameterProperty.String("文件名（必填，如 weekly_report_2026-06-17.md），含扩展名"),
            ["content"] = ToolParameterProperty.String("文件内容（必填）"),
            ["format"] = ToolParameterProperty.Enum("文件格式（仅标识，不影响实际写入）", new() { "markdown", "json", "txt", "csv" })
        },
        Required = new() { "filename", "content" }
    };

    public ServerExportFileTool(IOptions<UploadOptions> uploadOptions, IHostEnvironment hostEnv)
    {
        _uploadOptions = uploadOptions.Value;
        _hostEnv = hostEnv;
    }

    // IAgentTool 兼容
    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        var args = JsonDocument.Parse(argumentsJson).RootElement;
        if (!ToolArgHelper.TryGetString(args, "filename", out var filename))
            return Task.FromResult("导出失败：未提供 filename。");
        if (!ToolArgHelper.TryGetString(args, "content", out var content))
            return Task.FromResult("导出失败：未提供 content。");

        // 路径遍历防护：移除危险字符
        var safeName = filename
            .Replace("/", "_")
            .Replace("\\", "_")
            .Replace("..", "_")
            .Replace(":", "_");

        try
        {
            // 确定物理存储路径
            string physicalDir;
            if (!string.IsNullOrWhiteSpace(_uploadOptions.PhysicalPath))
            {
                physicalDir = _uploadOptions.PhysicalPath;
            }
            else
            {
                var webRoot = string.IsNullOrWhiteSpace(_hostEnv.ContentRootPath)
                    ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
                    : Path.Combine(_hostEnv.ContentRootPath, "wwwroot");
                physicalDir = Path.Combine(webRoot, _uploadOptions.BasePath);
            }

            if (!Directory.Exists(physicalDir))
                Directory.CreateDirectory(physicalDir);

            var filePath = Path.Combine(physicalDir, safeName);
            File.WriteAllText(filePath, content, System.Text.Encoding.UTF8);

            var relativeUrl = $"/{_uploadOptions.BasePath.TrimStart('/')}/{safeName}";
            return Task.FromResult($"文件已成功导出：{safeName}（{content.Length} 字符）。\n可通过相对路径访问：{relativeUrl}");
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult($"导出失败：没有写入权限。");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"导出失败：{ex.Message}");
        }
    }
}
