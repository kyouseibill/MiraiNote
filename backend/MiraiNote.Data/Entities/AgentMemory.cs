namespace MiraiNote.Data.Entities;

/// <summary>
/// Agent 持久记忆。键值对存储，用于记录用户偏好、上下文和常用操作。
/// </summary>
public class AgentMemory : BaseEntity
{
    public int Id { get; set; }

    /// <summary>所属用户</summary>
    public int UserId { get; set; }

    /// <summary>记忆键（同一用户下唯一）</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>记忆内容</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>分类：preference（偏好）/ context（上下文）/ fact（事实）/ command（常用命令）</summary>
    public string Category { get; set; } = "context";

    /// <summary>标签，逗号分隔，用于检索</summary>
    public string? Tags { get; set; }

    /// <summary>重要性 1-5，默认 3。访问时自动 +1（上限 5），定期衰减</summary>
    public byte Importance { get; set; } = 3;

    /// <summary>最后访问时间</summary>
    public DateTime LastAccessedAt { get; set; }

    // 导航属性
    public User? User { get; set; }
}
