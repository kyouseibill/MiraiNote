using Microsoft.Extensions.DependencyInjection;

namespace MiraiNote.Core;

/// <summary>
/// Core 层 DI 注册扩展。
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCoreLayer(this IServiceCollection services)
    {
        services.AddScoped<Services.DatabaseSeeder>();
        services.AddSingleton<Services.IJwtTokenService, Services.JwtTokenService>();
        services.AddScoped<Services.IAuthService, Services.AuthService>();
        services.AddScoped<Services.IUserAdminService, Services.UserAdminService>();
        services.AddScoped<Services.IWorkLogService, Services.WorkLogService>();
        services.AddScoped<Services.IMemoService, Services.MemoService>();
        services.AddScoped<Services.ILifeLogService, Services.LifeLogService>();
        services.AddScoped<Services.IWeeklyReportService, Services.WeeklyReportService>();
        services.AddScoped<Services.IChatService, Services.ChatService>();
        services.AddSingleton<Services.IEmailService, Services.SmtpEmailService>();
        services.AddHostedService<Services.MemoReminderBackgroundService>();
        services.AddHttpClient("DeepSeek");
        return services;
    }
}
