using MiraiNote.Core.Services.Tools;

namespace MiraiNote.Core.Services.Mirai;

/// <summary>
/// Mirai M1 文件存储布局（docs/m1-detailed-design.md §3.5）：
/// fileservice 根下 workspace / exports / temp 分工——workspace 是 Agent 草稿纸，
/// exports 是交付用户的成品（按 yyyy\MM 子目录），temp 是即弃文件（每日清理）。
/// </summary>
public static class MiraiFileStorage
{
    /// <summary>
    /// 成品导出根目录：显式配置 ExportsRoot 优先；未配置时回落
    /// workspace 根的同级 exports 目录（生产布局 fileservice\exports）。
    /// </summary>
    public static string ExportsRoot(FileSystemOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ExportsRoot))
            return options.ExportsRoot;
        var workspaceRoot = WorkspacePaths.Root(options);
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workspaceRoot)) ?? ".", "exports");
    }

    /// <summary>
    /// 即弃文件根目录：显式配置 TempRoot 优先；未配置时回落 workspace 根的同级 temp 目录。
    /// </summary>
    public static string TempRoot(FileSystemOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.TempRoot))
            return options.TempRoot;
        var workspaceRoot = WorkspacePaths.Root(options);
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workspaceRoot)) ?? ".", "temp");
    }
}
