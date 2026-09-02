using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;

namespace MiraiNote.Core.Services;

/// <summary>
/// 数据库初始化 Seeder。
/// 幂等设计：仅在 User 表为空时执行，可安全重复调用。
/// </summary>
public class DatabaseSeeder
{
    private readonly MiraiNoteDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(MiraiNoteDbContext db, IConfiguration config, ILogger<DatabaseSeeder> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    /// <summary>执行所有 Seed 任务。</summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedSuperAdminAsync(cancellationToken);
    }

    /// <summary>
    /// 创建超级管理员（Id=1，Username=admin）。
    /// 仅在 User 表完全为空时执行，确保 IDENTITY 自增的第一条记录 Id = 1。
    /// </summary>
    private async Task SeedSuperAdminAsync(CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters：避免软删除过滤干扰判断
        if (await _db.Users.IgnoreQueryFilters().AnyAsync(cancellationToken))
        {
            _logger.LogInformation("Seeder: User 表已有数据，跳过超级管理员初始化。");
            return;
        }

        var adminPassword = _config["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            throw new InvalidOperationException(
                "Seed:AdminPassword 未配置。请在 appsettings.Development.json 中设置管理员初始密码。");
        }

        var admin = new User
        {
            Username = "admin",
            Email = "admin@mirainote.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
            IsAdmin = true,
            IsEmailVerified = true,
            IsActive = true
            // CreatedAt/By、UpdatedAt/By 由 MiraiNoteDbContext.SaveChangesAsync 自动填充（未登录时默认 1）
        };

        _db.Users.Add(admin);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seeder: 超级管理员账户已创建（Id={Id}, Username=admin）。", admin.Id);
    }
}
