using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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
        services.AddSingleton(Services.Household.HouseholdAccessPolicy.Default);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(sp => new Services.Household.AdjustableHouseholdTimeProvider(TimeProvider.System));
        services.AddSingleton<Services.Household.IHouseholdClock>(sp =>
        {
            var env = sp.GetService<IHostEnvironment>();
            var options = sp.GetService<IOptions<Services.Household.HouseholdOptions>>()?.Value;
            if (env != null && options != null && Services.Household.HouseholdTestClockPolicy.IsEnabled(options, env))
                return sp.GetRequiredService<Services.Household.AdjustableHouseholdTimeProvider>();
            return new Services.Household.SystemHouseholdClock();
        });
        services.AddHostedService<Services.Household.HouseholdTestClockStartupLogger>();
        services.AddSingleton<Services.Household.HouseholdLinkBuilder>();
        services.AddSingleton<Services.Household.HouseholdCycleRules>();
        services.AddScoped<Services.Household.IHouseholdAccessService, Services.Household.HouseholdAccessService>();
        services.AddScoped<Services.Household.IHouseholdService, Services.Household.HouseholdService>();
        services.AddScoped<Services.Household.IHouseholdItemService, Services.Household.HouseholdItemService>();
        services.AddScoped<Services.Household.IHouseholdConsumableService, Services.Household.HouseholdConsumableService>();
        services.AddScoped<Services.Household.IHouseholdTestClockService, Services.Household.HouseholdTestClockService>();
        services.AddSingleton<Services.Household.IHouseholdSecretProtector, Services.Household.HouseholdSecretProtector>();
        services.AddScoped<Services.Household.BarkNotificationChannel>();
        services.AddScoped<Services.Household.EmailNotificationChannel>();
        services.AddSingleton<Services.Household.HouseholdNotificationRateLimiter>();
        services.AddScoped<Services.Household.IHouseholdNotificationSettingsService, Services.Household.HouseholdNotificationSettingsService>();
        services.AddScoped<Services.Household.IHouseholdNotificationDispatcher, Services.Household.HouseholdNotificationDispatcher>();
        services.AddHostedService<Services.Household.HouseholdNotificationBackgroundService>();
        services.AddHttpClient(Services.Household.BarkNotificationChannel.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(Services.Household.BarkNotificationChannel.TimeoutSeconds);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(Services.Household.BarkNotificationChannel.TimeoutSeconds),
            ConnectCallback = Services.Household.HouseholdBarkConnector.ConnectCallback
        })
        .RemoveAllLoggers();
        services.AddScoped<Services.IWeeklyReportService, Services.WeeklyReportService>();
        services.AddSingleton<Services.ChatSessionRunGate>();
        services.AddSingleton<Services.AgentRuns.AgentRunDispatcher>();
        services.AddScoped<Services.AgentRuns.IAgentRunService, Services.AgentRuns.AgentRunService>();
        services.AddHostedService<Services.AgentRuns.AgentRunBackgroundService>();
        services.AddScoped<Services.IChatService, Services.ChatService>();
        services.AddSingleton<Services.ChatModels.IChatModelRegistry, Services.ChatModels.ChatModelRegistry>();
        services.AddSingleton<Services.ChatModels.IChatModelProvider, Services.ChatModels.DeepSeekChatModelProvider>();
        services.AddSingleton<Services.ChatModels.IChatModelProvider, Services.ChatModels.MiniMaxChatModelProvider>();
        services.AddSingleton<Services.ChatModels.IChatModelProviderResolver, Services.ChatModels.ChatModelProviderResolver>();
        services.AddScoped<Services.ChatFileParserService>();
        services.AddSingleton<Services.IEmailService, Services.SmtpEmailService>();
        services.AddScoped<Services.IScheduledTaskService, Services.ScheduledTaskService>();
        services.AddHostedService<Services.MemoReminderBackgroundService>();
        services.AddHostedService<Services.MemoryDecayBackgroundService>();
        services.AddHostedService<Services.ScheduledTaskExecutionService>();
        services.AddScoped<Services.IAgentMemoryService, Services.AgentMemoryService>();
        services.AddScoped<Services.IFileSkillService, Services.FileSkillService>();
        services.AddScoped<Services.IAgentPlannerService, Services.AgentPlannerService>();
        services.AddScoped<Services.IAgentReflectorService, Services.AgentReflectorService>();
        // Mirai M1：收件箱分拣 / 晨报 / 今日流 / AI 统计 / context 会话快照
        services.AddScoped<Services.Mirai.IInboxTriageService, Services.Mirai.InboxTriageService>();
        services.AddScoped<Services.Mirai.IBriefingService, Services.Mirai.BriefingService>();
        services.AddScoped<Services.Mirai.IDayOverviewService, Services.Mirai.DayOverviewService>();
        services.AddScoped<Services.Mirai.IMiraiStatsService, Services.Mirai.MiraiStatsService>();
        services.AddScoped<Services.IWelcomeGreetingService, Services.WelcomeGreetingService>();
        services.AddScoped<Services.Mirai.IMiraiContextProvider, Services.Mirai.MiraiContextProvider>();
        services.AddHostedService<Services.TempCleanupBackgroundService>();
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
        services.AddScoped<Services.Tools.ServerLoadSkillTool>();
        // 混合推理模型"思考+正文"可能远超 HttpClient 默认 100s 超时导致流被掐断，
        // 改为无限超时；由 ChatService 读取循环里的空闲超时兜底（长时间收不到新行才中断）。
        services.AddHttpClient("DeepSeek").ConfigureHttpClient(c =>
        {
            c.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        });
        services.AddHttpClient("ChatModel").ConfigureHttpClient(c =>
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
