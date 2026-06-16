using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;

namespace MiraiNote.Data.Context;

/// <summary>
/// MiraiNote 主数据库上下文（程序运行时使用 DefaultConnection，应用账户：仅读写权限）。
/// 统一负责：① 全局软删除过滤器；② SaveChanges 时自动填充审计字段。
/// </summary>
public class MiraiNoteDbContext : DbContext
{
    private readonly ICurrentUserService? _currentUserService;

    public DbSet<User> Users => Set<User>();
    public DbSet<EmailVerifyToken> EmailVerifyTokens => Set<EmailVerifyToken>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<WorkLog> WorkLogs => Set<WorkLog>();
    public DbSet<Memo> Memos => Set<Memo>();
    public DbSet<LifeLog> LifeLogs => Set<LifeLog>();
    public DbSet<WeeklyReport> WeeklyReports => Set<WeeklyReport>();
    public DbSet<WeeklyReportReference> WeeklyReportReferences => Set<WeeklyReportReference>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<AgentMemory> AgentMemories => Set<AgentMemory>();

    /// <summary>运行时构造：注入当前用户服务，用于自动填充审计字段。</summary>
    public MiraiNoteDbContext(DbContextOptions<MiraiNoteDbContext> options, ICurrentUserService currentUserService)
        : base(options)
    {
        _currentUserService = currentUserService;
    }

    /// <summary>设计期/迁移期构造：不依赖 HttpContext，审计字段使用默认值。</summary>
    public MiraiNoteDbContext(DbContextOptions<MiraiNoteDbContext> options)
        : base(options)
    {
        _currentUserService = null;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ===== 唯一索引：Username / Email =====
        // 注意：HasFilter 与软删除过滤器配合，只对未删除记录强制唯一
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        modelBuilder.Entity<EmailVerifyToken>()
            .HasIndex(t => t.Token);

        modelBuilder.Entity<EmailVerifyToken>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<RefreshToken>()
            .HasIndex(t => t.TokenHash);

        modelBuilder.Entity<RefreshToken>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ===== WorkLog 索引：UserId + LogDate（用于按用户按日期范围查询周报）=====
        modelBuilder.Entity<WorkLog>()
            .HasIndex(w => new { w.UserId, w.LogDate });

        modelBuilder.Entity<WorkLog>()
            .HasOne(w => w.User)
            .WithMany()
            .HasForeignKey(w => w.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ===== Memo 索引：UserId + Section（按板块查询）=====
        modelBuilder.Entity<Memo>()
            .HasIndex(m => new { m.UserId, m.Section });

        modelBuilder.Entity<Memo>()
            .HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ===== LifeLog 索引：UserId + LogDate =====
        modelBuilder.Entity<LifeLog>()
            .HasIndex(l => new { l.UserId, l.LogDate });

        modelBuilder.Entity<LifeLog>()
            .HasOne(l => l.User)
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ===== WeeklyReport 索引：UserId + WeekStart =====
        modelBuilder.Entity<WeeklyReport>()
            .HasIndex(r => new { r.UserId, r.WeekStart });

        modelBuilder.Entity<WeeklyReport>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ===== WeeklyReportReference 索引：UserId =====
        modelBuilder.Entity<WeeklyReportReference>()
            .HasIndex(r => r.UserId);

        modelBuilder.Entity<WeeklyReportReference>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ===== ChatSession / ChatMessage =====
        modelBuilder.Entity<ChatSession>()
            .HasIndex(s => s.UserId);

        modelBuilder.Entity<ChatSession>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ChatMessage>()
            .HasIndex(m => m.SessionId);

        modelBuilder.Entity<ChatMessage>()
            .HasOne(m => m.Session)
            .WithMany(s => s.Messages)
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // ===== AgentMemory：UserId + Key 唯一索引 =====
        modelBuilder.Entity<AgentMemory>()
            .HasIndex(m => new { m.UserId, m.Key })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        modelBuilder.Entity<AgentMemory>()
            .HasIndex(m => m.UserId);

        modelBuilder.Entity<AgentMemory>()
            .HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // 自动为所有继承 BaseEntity 的实体注册软删除全局查询过滤器
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var property = Expression.Property(parameter, nameof(BaseEntity.IsDeleted));
                var condition = Expression.Equal(property, Expression.Constant(false));
                var lambda = Expression.Lambda(condition, parameter);
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
            }
        }
    }

    /// <summary>
    /// 自动填充 BaseEntity 的审计字段：
    /// - Added：CreatedAt/By 与 UpdatedAt/By 全部赋值
    /// - Modified：仅更新 UpdatedAt/By
    /// 未登录场景 UserId=0，统一回退为 1（超级管理员）。
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var userId = _currentUserService?.UserId ?? 0;
        var effectiveUserId = userId > 0 ? userId : 1;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy = effectiveUserId;
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = effectiveUserId;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = effectiveUserId;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
