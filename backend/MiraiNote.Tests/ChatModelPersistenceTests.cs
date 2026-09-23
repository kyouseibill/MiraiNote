using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MiraiNote.Core;
using MiraiNote.Core.Services;
using MiraiNote.Core.Services.AgentRuns;
using MiraiNote.Core.Services.ChatModels;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Dtos.Chat;
using Moq;
using Xunit;

namespace MiraiNote.Tests;

public class ChatModelPersistenceTests
{
    [Fact]
    public async Task Work_run_accepts_image_only_input_for_a_vision_model()
    {
        using var fixture = new MiraiTestFixture();
        await using var db = fixture.CreateContext();
        var session = new ChatSession { UserId = 1, Title = "看图", AiProvider = "deepseek", AiModel = "deepseek-v4-flash" };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();
        var service = new AgentRunService(db, new AgentRunDispatcher(), Mock.Of<IChatService>(),
            CreateRegistry("deepseek", "deepseek-v4-flash"), NullLogger<AgentRunService>.Instance);

        await service.CreateAsync(1, session.Id, ImageOnlyRequest(), default);

        Assert.Equal(1, await db.AgentRuns.CountAsync());
    }

    [Fact]
    public async Task Work_run_rejects_images_for_a_text_only_model_before_queuing()
    {
        using var fixture = new MiraiTestFixture();
        await using var db = fixture.CreateContext();
        var session = new ChatSession { UserId = 1, Title = "看图", AiProvider = "deepseek", AiModel = "deepseek-v4-pro" };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();
        var service = new AgentRunService(db, new AgentRunDispatcher(), Mock.Of<IChatService>(),
            CreateRegistry("deepseek", "deepseek-v4-pro"), NullLogger<AgentRunService>.Instance);

        var request = ImageOnlyRequest();
        request.Content = "看图";
        var error = await Assert.ThrowsAsync<MiraiNote.Shared.Common.BusinessException>(() =>
            service.CreateAsync(1, session.Id, request, default));

        Assert.Contains("不支持图片", error.Message);
        Assert.Equal(0, await db.AgentRuns.CountAsync());
    }

    private static SendMessageRequest ImageOnlyRequest() => new()
    {
        Attachments = [new ChatAttachmentContent
        {
            FileName = "image.png", FileType = "图片", MimeType = "image/png", IsImage = true,
            DataUrl = "data:image/png;base64,iVBORw=="
        }]
    };

    [Fact]
    public void Session_and_work_run_keep_the_provider_and_model_snapshot()
    {
        var session = new ChatSession { AiProvider = "minimax", AiModel = "MiniMax-M2.7" };
        var run = new AgentRun { AiProvider = session.AiProvider, AiModel = session.AiModel };
        var dto = new ChatSessionDto { ModelKey = "minimax:MiniMax-M2.7" };

        Assert.Equal("minimax", run.AiProvider);
        Assert.Equal("MiniMax-M2.7", run.AiModel);
        Assert.Equal("minimax:MiniMax-M2.7", dto.ModelKey);
    }

    [Fact]
    public async Task Creating_a_work_run_copies_the_session_model_snapshot()
    {
        using var fixture = new MiraiTestFixture();
        await using var db = fixture.CreateContext();
        var session = new ChatSession
        {
            UserId = 1,
            Title = "模型快照",
            AiProvider = "minimax",
            AiModel = "MiniMax-M2.7",
        };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();
        var service = new AgentRunService(
            db,
            new AgentRunDispatcher(),
            Mock.Of<IChatService>(),
            CreateRegistry("minimax", "MiniMax-M2.7"),
            NullLogger<AgentRunService>.Instance);

        await service.CreateAsync(1, session.Id, new SendMessageRequest { Content = "执行调研" }, default);

        var run = await db.AgentRuns.SingleAsync();
        Assert.Equal("minimax", run.AiProvider);
        Assert.Equal("MiniMax-M2.7", run.AiModel);
    }

    [Fact]
    public async Task Creating_a_work_run_locks_the_default_model_for_a_legacy_session()
    {
        using var fixture = new MiraiTestFixture();
        await using var db = fixture.CreateContext();
        var session = new ChatSession { UserId = 1, Title = "旧会话" };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();
        var service = new AgentRunService(
            db,
            new AgentRunDispatcher(),
            Mock.Of<IChatService>(),
            CreateRegistry("deepseek", "deepseek-v4-flash"),
            NullLogger<AgentRunService>.Instance);

        await service.CreateAsync(1, session.Id, new SendMessageRequest { Content = "继续执行" }, default);

        var run = await db.AgentRuns.SingleAsync();
        Assert.Equal("deepseek", run.AiProvider);
        Assert.Equal("deepseek-v4-flash", run.AiModel);
        Assert.Equal("deepseek", session.AiProvider);
        Assert.Equal("deepseek-v4-flash", session.AiModel);
    }

    [Fact]
    public async Task Creating_a_session_validates_and_persists_its_selected_model()
    {
        using var fixture = new MiraiTestFixture();
        await using var db = fixture.CreateContext();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHostEnvironment>(Mock.Of<IHostEnvironment>(environment => environment.ContentRootPath == AppContext.BaseDirectory));
        services.AddSingleton(db);
        services.AddCoreLayer();
        services.Configure<DeepSeekOptions>(options =>
        {
            options.ApiKey = "legacy-deepseek-key";
            options.Model = "deepseek-v4-flash";
        });
        services.Configure<AiOptions>(options =>
        {
            options.DefaultModelKey = "deepseek:deepseek-v4-flash";
            options.Providers =
            [
                new AiProviderOptions
                {
                    Key = "deepseek",
                    DisplayName = "DeepSeek",
                    Models =
                    [
                        new AiModelOptions { Key = "deepseek:deepseek-v4-flash", ModelId = "deepseek-v4-flash", DisplayName = "DeepSeek V4 Flash" },
                    ],
                },
            ];
        });
        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IChatService>();

        var dto = await service.CreateSessionAsync(1, new CreateSessionRequest { ModelKey = "deepseek:deepseek-v4-flash" });

        Assert.Equal("deepseek:deepseek-v4-flash", dto.ModelKey);
        var persisted = await db.ChatSessions.SingleAsync(session => session.Id == dto.Id);
        Assert.Equal("deepseek", persisted.AiProvider);
        Assert.Equal("deepseek-v4-flash", persisted.AiModel);
    }

    private static IChatModelRegistry CreateRegistry(string provider, string model) =>
        Mock.Of<IChatModelRegistry>(registry => registry.ResolveForExistingSession(It.IsAny<string?>(), It.IsAny<string?>()) ==
            new ChatModelDescriptor($"{provider}:{model}", provider, provider, model, model, true, true, true, true));
}
