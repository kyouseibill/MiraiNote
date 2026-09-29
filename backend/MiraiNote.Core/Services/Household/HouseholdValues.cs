using System.Text.Json;
using MiraiNote.Shared.Common;

namespace MiraiNote.Core.Services.Household;

internal static class HouseholdFieldLimits
{
    public const int Name = 100;
    public const int Location = 200;
    public const int ModelSpec = 200;
    public const int Note = 2000;
    public const int PurchaseLink = 500;
    public const int Unit = 20;
    public const int Alias = 50;
    public const int AliasCount = 20;
}

internal static class HouseholdText
{
    public static string Require(string? value, int max, string fieldName)
    {
        var cleaned = Clean(value, max, fieldName);
        if (cleaned == null)
            throw new BusinessException($"{fieldName}不能为空", 400);
        return cleaned;
    }

    public static string? Clean(string? value, int max, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length > max)
            throw new BusinessException($"{fieldName}不能超过 {max} 个字符", 400);
        return trimmed;
    }
}

/// <summary>
/// 完成记录上的照片引用。只接受上传接口返回的站内相对路径，不接收文件本体。
/// </summary>
public static class HouseholdPhotoRefs
{
    public const int MaxCount = 9;
    public const int MaxPathLength = 500;

    public static List<string> Normalize(IEnumerable<string>? paths)
    {
        var list = new List<string>();
        foreach (var raw in paths ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var path = raw.Trim();
            if (path.Length > MaxPathLength)
                throw new BusinessException("图片路径过长", 400);
            if (!path.StartsWith('/')
                || path.Contains("..", StringComparison.Ordinal)
                || path.Contains('\\')
                || path.Contains("://", StringComparison.Ordinal))
            {
                throw new BusinessException("图片需先通过上传接口上传，并使用返回的相对路径", 400);
            }

            if (!list.Contains(path, StringComparer.Ordinal))
                list.Add(path);
        }

        if (list.Count > MaxCount)
            throw new BusinessException($"最多关联 {MaxCount} 张图片", 400);
        return list;
    }

    public static string? Serialize(IEnumerable<string>? paths)
    {
        var list = Normalize(paths);
        return list.Count == 0 ? null : JsonSerializer.Serialize(list);
    }

    public static List<string> Deserialize(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(stored)?
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

public static class HouseholdAliases
{
    public static List<string> Normalize(IEnumerable<string>? aliases)
    {
        var list = new List<string>();
        foreach (var raw in aliases ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var alias = raw.Trim();
            if (alias.Length > HouseholdFieldLimits.Alias)
                throw new BusinessException($"别名不能超过 {HouseholdFieldLimits.Alias} 个字符", 400);
            if (list.Contains(alias, StringComparer.OrdinalIgnoreCase))
                continue;
            list.Add(alias);
        }

        if (list.Count > HouseholdFieldLimits.AliasCount)
            throw new BusinessException($"别名最多 {HouseholdFieldLimits.AliasCount} 个", 400);
        return list;
    }

    public static string? Serialize(IEnumerable<string>? aliases)
    {
        var list = Normalize(aliases);
        return list.Count == 0 ? null : JsonSerializer.Serialize(list);
    }

    public static List<string> Deserialize(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(stored)?
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
