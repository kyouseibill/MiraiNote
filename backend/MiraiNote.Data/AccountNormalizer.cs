namespace MiraiNote.Data;

/// <summary>
/// 账号比较口径。用户名页面显示保留 Trim 后的原样；比较和唯一约束只用小写列。
/// 邮箱直接 Trim + 小写后写入 Email，不再另建 NormalizedEmail。
/// 这里只做归一化，不合并、不删除重复账号。
/// </summary>
public static class AccountNormalizer
{
    public static string DisplayUsername(string? username) => (username ?? string.Empty).Trim();

    public static string NormalizeUsername(string? username) =>
        DisplayUsername(username).ToLowerInvariant();

    public static string NormalizeEmail(string? email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();
}
