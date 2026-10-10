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
        services.AddSingleton<Services.IBackgroundWork, Services.BackgroundWork>();
        services.AddScoped<Services.IAuthService, Services.AuthService>();
        services.AddScoped<Services.IUserAdminService, Services.UserAdminService>();
        services.AddScoped<Services.IWorkLogService, Services.WorkLogService>();
        services.AddScoped<Services.IMemoService, Services.MemoService>();
        services.AddScoped<Services.ILifeLogService, Services.LifeLogService>();
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
        services.AddHttpClient(Services.BarkNotifier.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddSingleton<Services.IBarkNotifier, Services.BarkNotifier>();
        services.AddScoped<Services.IMemoReminderSettingsService, Services.MemoReminderSettingsService>();
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
        services.AddMemoryCache();
        services.AddDistributedMemoryCache();
        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient(Services.QWeatherWarningClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(4);
        }).ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(2),
        });
        services.AddHttpClient(Services.WelcomeNewsClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(4);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MiraiNote/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/rss+xml");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/atom+xml");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/xml");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/xml");
        }).ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(2),
        });
        services.AddSingleton<Services.QWeatherWarningClient>();
        services.AddSingleton<Services.ISevereWeatherWarningSource>(sp => sp.GetRequiredService<Services.QWeatherWarningClient>());
        services.AddSingleton<Services.IWelcomeWeatherSource>(sp => sp.GetRequiredService<Services.QWeatherWarningClient>());
        services.AddSingleton<Services.IWelcomeTitleTranslator, Services.DeepSeekWelcomeTitleTranslator>();
        services.AddSingleton<Services.IWelcomeNewsSeenStore, Services.WelcomeNewsSeenStore>();
        services.AddSingleton<Services.IWelcomeNewsSource, Services.WelcomeNewsClient>();
        services.AddScoped<Services.IWelcomePlaceSettingsService, Services.WelcomePlaceSettingsService>();
        services.AddSingleton<Services.IWelcomeInspirationSource, Services.DeepSeekWelcomeInspiration>();
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
