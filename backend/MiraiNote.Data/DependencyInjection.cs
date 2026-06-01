using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiraiNote.Data.Context;

namespace MiraiNote.Data;

/// <summary>
/// Data 层 DI 注册扩展。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册数据访问层服务。运行时使用 DefaultConnection（应用账户）。
    /// </summary>
    public static IServiceCollection AddDataLayer(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("未配置 ConnectionStrings:DefaultConnection。");

        services.AddDbContext<MiraiNoteDbContext>(options =>
            options.UseSqlServer(connectionString, sqlOptions =>
                sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorNumbersToAdd: null)));

        return services;
    }
}
