namespace MiraiNote.Data.Entities;

/// <summary>
/// 实体基类：所有数据库实体必须继承，统一携带软删除与审计字段。
/// 审计字段由 <see cref="Context.MiraiNoteDbContext"/> 在 SaveChanges 时自动填充，业务代码无需手动赋值。
/// </summary>
public abstract class BaseEntity
{
    /// <summary>软删除标记（true=已删除），全局查询过滤器会自动过滤掉已删除记录。</summary>
    public bool IsDeleted { get; set; } = false;

    /// <summary>创建时间（UTC）。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>创建人用户 Id；未登录场景默认 1（超级管理员）。</summary>
    public int CreatedBy { get; set; } = 1;

    /// <summary>最后更新时间（UTC）。</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>最后更新人用户 Id；未登录场景默认 1（超级管理员）。</summary>
    public int UpdatedBy { get; set; } = 1;
}
