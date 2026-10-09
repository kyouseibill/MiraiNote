using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;

namespace MiraiNote.Data.Context;

/// <summary>
/// MiraiNote 主数据库上下文（程序运行时使用 DefaultConnection，应用账户：仅读写权限）。
/// 统一负责：① 全局软删除过滤器；② SaveChanges 时自动填充审计字段。
/// </summary>
public class MiraiNoteDbContext : DbContext
{
    private const string NotDeletedFilter = "\"IsDeleted\" = false";

    private static readonly ValueConverter<DateTime, DateOnly> CalendarDate = new(
        v => DateOnly.FromDateTime(v),
        v => v.ToDateTime(TimeOnly.MinValue));

    private static readonly ValueConverter<DateTime?, DateOnly?> NullableCalendarDate = new(
        v => v.HasValue ? DateOnly.FromDateTime(v.Value) : null,
        v => v.HasValue ? v.Value.ToDateTime(TimeOnly.MinValue) : null);

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
    public DbSet<ChatProject> ChatProjects => Set<ChatProject>();
    public DbSet<AgentMemory> AgentMemories => Set<AgentMemory>();
    public DbSet<ScheduledTask> ScheduledTasks => Set<ScheduledTask>();
    public DbSet<InboxItem> InboxItems => Set<InboxItem>();
    public DbSet<DailyBriefing> DailyBriefings => Set<DailyBriefing>();
    public DbSet<AIActionLog> AIActionLogs => Set<AIActionLog>();
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<AgentRunEvent> AgentRunEvents => Set<AgentRunEvent>();
    public DbSet<WelcomeNewsSeen> WelcomeNewsSeens => Set<WelcomeNewsSeen>();
    public DbSet<WelcomePhrase> WelcomePhrases => Set<WelcomePhrase>();
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

        // ===== 唯一索引：NormalizedUserName / Email =====
        // 用户名原样留在 Username；比较和唯一约束只用小写列。邮箱列本身已是小写。
        // HasFilter 与软删除过滤器配合，只对未删除记录强制唯一。
        modelBuilder.Entity<User>()
            .HasIndex(u => u.NormalizedUserName)
            .IsUnique()
            .HasFilter(NotDeletedFilter);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique()
            .HasFilter(NotDeletedFilter);

        // 纯日期列：PostgreSQL 用 date，避免把日历日当成 UTC 瞬时再偏移 8 小时。
        modelBuilder.Entity<WorkLog>().Property(w => w.LogDate).HasConversion(CalendarDate);
        modelBuilder.Entity<LifeLog>().Property(l => l.LogDate).HasConversion(CalendarDate);
        modelBuilder.Entity<WeeklyReport>().Property(r => r.WeekStart).HasConversion(CalendarDate);
        modelBuilder.Entity<WeeklyReport>().Property(r => r.WeekEnd).HasConversion(CalendarDate);
        modelBuilder.Entity<WeeklyReportReference>().Property(r => r.WeekStart).HasConversion(NullableCalendarDate);
        modelBuilder.Entity<WeeklyReportReference>().Property(r => r.WeekEnd).HasConversion(NullableCalendarDate);

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
            .HasIndex(s => new { s.UserId, s.UpdatedAt });

        modelBuilder.Entity<ChatSession>()
            .HasIndex(s => new { s.UserId, s.IsArchived, s.UpdatedAt });

        modelBuilder.Entity<ChatSession>()
            .HasIndex(s => new { s.UserId, s.ProjectId, s.IsPinned, s.UpdatedAt });

        modelBuilder.Entity<ChatSession>()
            .Property(s => s.AiProvider)
            .HasMaxLength(50);

        modelBuilder.Entity<ChatSession>()
            .Property(s => s.AiModel)
            .HasMaxLength(120);

        modelBuilder.Entity<ChatSession>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ChatSession>()
            .HasOne(s => s.Project)
            .WithMany(p => p.Sessions)
            .HasForeignKey(s => s.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ChatProject>()
            .HasIndex(p => new { p.UserId, p.Name });

        modelBuilder.Entity<ChatProject>()
            .HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ChatMessage>()
            .HasIndex(m => m.SessionId);

        modelBuilder.Entity<ChatMessage>()
            .HasIndex(m => new { m.SessionId, m.CreatedAt });

        modelBuilder.Entity<ChatMessage>()
            .HasOne(m => m.Session)
            .WithMany(s => s.Messages)
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // ===== AgentRun：执行与 SSE 观察者解耦的持久化账本 =====
        modelBuilder.Entity<AgentRun>()
            .HasIndex(r => new { r.UserId, r.SessionId, r.Status });

        modelBuilder.Entity<AgentRun>()
            .HasIndex(r => new { r.Status, r.CreatedAt });

        modelBuilder.Entity<AgentRun>()
            .Property(r => r.AiProvider)
            .HasMaxLength(50);

        modelBuilder.Entity<AgentRun>()
            .Property(r => r.AiModel)
            .HasMaxLength(120);

        modelBuilder.Entity<AgentRun>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AgentRun>()
            .HasOne(r => r.Session)
            .WithMany()
            .HasForeignKey(r => r.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AgentRunEvent>()
            .HasIndex(e => new { e.RunId, e.Sequence })
            .IsUnique();

        modelBuilder.Entity<AgentRunEvent>()
            .HasOne(e => e.Run)
            .WithMany(r => r.Events)
            .HasForeignKey(e => e.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        // ===== AgentMemory：UserId + Key 唯一索引 =====
        modelBuilder.Entity<AgentMemory>()
            .HasIndex(m => new { m.UserId, m.Key })
            .IsUnique()
            .HasFilter(NotDeletedFilter);

        modelBuilder.Entity<AgentMemory>()
            .HasIndex(m => m.UserId);

        modelBuilder.Entity<AgentMemory>()
            .HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ===== ScheduledTask 索引：UserId + Status（用于按用户 + 状态查询）=====
        modelBuilder.Entity<ScheduledTask>()
            .HasIndex(t => new { t.UserId, t.Status });

        modelBuilder.Entity<ScheduledTask>()
            .HasIndex(t => t.ExecuteAt);

        modelBuilder.Entity<ScheduledTask>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ===== ChatSession：Mirai M1 会话类型检索索引 =====
        modelBuilder.Entity<ChatSession>()
            .HasIndex(s => new { s.UserId, s.SessionType });

        // ===== InboxItems（Mirai M1 捕获收件箱）=====
        modelBuilder.Entity<InboxItem>()
            .HasIndex(i => new { i.UserId, i.Status, i.CreatedAt });

        modelBuilder.Entity<InboxItem>()
            .HasOne(i => i.User)
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ===== DailyBriefings（Mirai M1 晨报缓存）=====
        // 过滤唯一索引：同一用户同日仅一条未删除晨报（并发占位防重）。
        modelBuilder.Entity<DailyBriefing>()
            .HasIndex(b => new { b.UserId, b.BriefDate })
            .IsUnique()
            .HasFilter(NotDeletedFilter);

        modelBuilder.Entity<DailyBriefing>()
            .HasOne(b => b.User)
            .WithMany()
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ===== AIActionLogs（Mirai M1 AI 写操作审计）=====
        modelBuilder.Entity<AIActionLog>()
            .HasIndex(a => new { a.UserId, a.CreatedAt });

        modelBuilder.Entity<AIActionLog>()
            .HasIndex(a => new { a.TargetType, a.TargetId });

        modelBuilder.Entity<AIActionLog>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ===== 欢迎语已展示新闻：同一用户同一链接只留一行，并按展示时间清理 7 天前的记录 =====
        modelBuilder.Entity<WelcomeNewsSeen>()
            .Property(row => row.Url)
            .HasMaxLength(WelcomeNewsSeen.MaxUrlLength);

        modelBuilder.Entity<WelcomeNewsSeen>()
            .HasIndex(row => new { row.UserId, row.Url })
            .IsUnique()
            .HasFilter(NotDeletedFilter);

        modelBuilder.Entity<WelcomeNewsSeen>()
            .HasIndex(row => new { row.UserId, row.ShownAt });

        modelBuilder.Entity<WelcomeNewsSeen>()
            .HasOne(row => row.User)
            .WithMany()
            .HasForeignKey(row => row.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // 欢迎文案库。停用和软删都立刻排除，挑选按 Id，不按 SortOrder。
        modelBuilder.Entity<WelcomePhrase>()
            .Property(row => row.Kind)
            .HasMaxLength(WelcomePhrase.MaxKindLength)
            .IsRequired();

        modelBuilder.Entity<WelcomePhrase>()
            .Property(row => row.Text)
            .HasMaxLength(WelcomePhrase.MaxTextLength)
            .IsRequired();

        modelBuilder.Entity<WelcomePhrase>()
            .Property(row => row.Author)
            .HasMaxLength(WelcomePhrase.MaxAuthorLength);

        modelBuilder.Entity<WelcomePhrase>()
            .Property(row => row.Source)
            .HasMaxLength(WelcomePhrase.MaxSourceLength);

        modelBuilder.Entity<WelcomePhrase>()
            .Property(row => row.Period)
            .HasMaxLength(WelcomePhrase.MaxPeriodLength);

        modelBuilder.Entity<WelcomePhrase>()
            .Property(row => row.Special)
            .HasMaxLength(WelcomePhrase.MaxSpecialLength);

        modelBuilder.Entity<WelcomePhrase>()
            .Property(row => row.Season)
            .HasMaxLength(WelcomePhrase.MaxSeasonLength);

        modelBuilder.Entity<WelcomePhrase>()
            .Property(row => row.IsEnabled)
            .HasDefaultValue(true);

        modelBuilder.Entity<WelcomePhrase>()
            .HasIndex(row => row.Kind);

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
    /// - Added：CreatedAt/By 与 UpdatedAt/By 全部赋值。若调用方已写入非默认 CreatedAt，则保留该值。
    /// - Modified：仅更新 UpdatedAt/By
    /// 未登录场景 UserId=0，统一回退为 1（超级管理员）。
    /// </summary>
    public override int SaveChanges()
    {
        ApplyAudit();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAudit();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyAudit()
    {
        var now = DateTime.UtcNow;
        var userId = _currentUserService?.UserId ?? 0;
        var effectiveUserId = userId > 0 ? userId : 1;

        foreach (var entry in ChangeTracker.Entries<User>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            entry.Entity.Username = AccountNormalizer.DisplayUsername(entry.Entity.Username);
            entry.Entity.NormalizedUserName = AccountNormalizer.NormalizeUsername(entry.Entity.Username);
            entry.Entity.Email = AccountNormalizer.NormalizeEmail(entry.Entity.Email);
        }

        NormalizeInstantKinds();

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default)
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
    }

    /// <summary>
    /// timestamptz 列只接受 UTC。未标注 Kind 的瞬时值按既有约定视为 UTC，不加减 8 小时。
    /// 日历日列（LogDate / WeekStart / WeekEnd）保持日期部分，不在这里改 Kind。
    /// </summary>
    private void NormalizeInstantKinds()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            foreach (var prop in entry.Properties)
            {
                if (prop.Metadata.ClrType != typeof(DateTime) && prop.Metadata.ClrType != typeof(DateTime?))
                    continue;
                if (prop.CurrentValue is not DateTime value)
                    continue;
                if (IsCalendarDate(prop.Metadata.Name))
                    continue;

                if (value.Kind == DateTimeKind.Unspecified)
                    prop.CurrentValue = DateTime.SpecifyKind(value, DateTimeKind.Utc);
                else if (value.Kind == DateTimeKind.Local)
                    prop.CurrentValue = value.ToUniversalTime();
            }
        }
    }

    private static bool IsCalendarDate(string propertyName) =>
        propertyName is "LogDate" or "WeekStart" or "WeekEnd";
}
