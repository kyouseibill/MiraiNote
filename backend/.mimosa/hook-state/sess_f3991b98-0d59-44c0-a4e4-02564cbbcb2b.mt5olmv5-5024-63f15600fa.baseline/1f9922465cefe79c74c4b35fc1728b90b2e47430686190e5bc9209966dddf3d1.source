using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MiraiNote.Data.Context;

/// <summary>
/// 仅供 EF Core Migration（dotnet ef）使用的设计期 DbContext 工厂。
/// 使用 MigrationConnection（迁移账户：Bill.Gong / db_owner），不会注册到运行时 DI 容器。
/// 应用程序运行时始终使用 <see cref="MiraiNoteDbContext"/> + DefaultConnection（应用账户）。
/// </summary>
public class MigrationDbContextFactory : IDesignTimeDbContextFactory<MiraiNoteDbContext>
{
    public MiraiNoteDbContext CreateDbContext(string[] args)
    {
        // 设计期：从 API 项目目录的 appsettings 读取 MigrationConnection
        var basePath = Directory.GetCurrentDirectory();

        var config = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("MigrationConnection")
            ?? throw new InvalidOperationException(
                "未找到 ConnectionStrings:MigrationConnection。请在 appsettings.Development.json 中配置迁移账户连接字符串。");

        var optionsBuilder = new DbContextOptionsBuilder<MiraiNoteDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        // 设计期不需要 ICurrentUserService，使用单参构造
        return new MiraiNoteDbContext(optionsBuilder.Options);
    }
}
