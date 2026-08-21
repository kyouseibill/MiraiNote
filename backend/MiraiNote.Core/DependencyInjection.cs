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
        services.AddScoped<Services.ChatFileParserService>();
        services.AddSingleton<Services.IEmailService, Services.SmtpEmailService>();
        services.AddScoped<Services.IScheduledTaskService, Services.ScheduledTaskService>();
        services.AddHostedService<Services.MemoReminderBackgroundService>();
        services.AddHostedService<Services.MemoryDecayBackgroundService>();
        services.AddHostedService<Services.ScheduledTaskExecutionService>();
        services.AddScoped<Services.IAgentMemoryService, Services.AgentMemoryService>();
        services.AddScoped<Services.IAgentPlannerService, Services.AgentPlannerService>();
        services.AddScoped<Services.IAgentReflectorService, Services.AgentReflectorService>();
        services.AddScoped<Services.ServerAgentToolRegistry>();
        services.AddScoped<Services.Tools.ServerSearchWorkLogsTool>();
        services.AddScoped<Services.Tools.ServerSearchMemosTool>();
        services.AddScoped<Services.Tools.ServerSearchLifeLogsTool>();
        services.AddScoped<Services.Tools.ServerGetWeeklyReportsTool>();
        services.AddScoped<Services.Tools.ServerSearchInternetTool>();
        services.AddScoped<Services.Tools.ServerFetchWebPageTool>();
        services.AddScoped<Services.Tools.ServerHttpApiTool>();
        services.AddScoped<Services.Tools.ServerLoginAndFetchWebTool>();
        services.AddScoped<Services.Tools.ServerCreateWorkLogTool>();
        services.AddScoped<Services.Tools.ServerUpdateWorkLogTool>();
        services.AddScoped<Services.Tools.ServerDeleteWorkLogTool>();
        services.AddScoped<Services.Tools.ServerCreateMemoTool>();
        services.AddScoped<Services.Tools.ServerUpdateMemoTool>();
        services.AddScoped<Services.Tools.ServerPatchMemoStatusTool>();
        services.AddScoped<Services.Tools.ServerDeleteMemoTool>();
        services.AddScoped<Services.Tools.ServerCreateLifeLogTool>();
        services.AddScoped<Services.Tools.ServerUpdateLifeLogTool>();
        services.AddScoped<Services.Tools.ServerDeleteLifeLogTool>();
        services.AddScoped<Services.Tools.ServerRememberTool>();
        services.AddScoped<Services.Tools.ServerRecallTool>();
        services.AddScoped<Services.Tools.ServerForgetTool>();
        services.AddScoped<Services.Tools.ServerWeatherTool>();
        services.AddScoped<Services.Tools.ServerSendEmailTool>();
        services.AddScoped<Services.Tools.ServerExportFileTool>();
        services.AddScoped<Services.Tools.ServerCalendarTool>();
        services.AddScoped<Services.Tools.ServerCurrentTimeTool>();
        services.AddScoped<Services.Tools.ServerCalculatorTool>();
        services.AddScoped<Services.Tools.ServerRecordOverviewTool>();
        services.AddScoped<Services.Tools.ServerFileReadTool>();
        services.AddScoped<Services.Tools.ServerFileWriteTool>();
        services.AddScoped<Services.Tools.ServerFileDeleteTool>();
        services.AddScoped<Services.Tools.ServerFileMoveOrRenameTool>();
        services.AddScoped<Services.Tools.ServerPublishWorkspaceFileTool>();
        services.AddScoped<Services.Tools.ServerFileListTool>();
        services.AddScoped<Services.Tools.ServerShellTool>();
        services.AddScoped<Services.Tools.ServerScheduleTaskTool>();
        services.AddScoped<Services.Tools.ServerListScheduledTasksTool>();
        // 混合推理模型"思考+正文"可能远超 HttpClient 默认 100s 超时导致流被掐断，
        // 改为无限超时；由 ChatService 读取循环里的空闲超时兜底（长时间收不到新行才中断）。
        services.AddHttpClient("DeepSeek").ConfigureHttpClient(c =>
        {
            c.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        });
        services.AddHttpClient("Tavily");
        services.AddHttpClient("OpenMeteo", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        return services;
    }
}
