using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MiraiNote.Core.Services;
using MiraiNote.Core.Services.Mirai;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Mirai;
using Moq;

namespace MiraiNote.Tests;

/// <summary>
/// SQLite 共享内存库测试基座：提供真实关系语义（过滤唯一索引 / 事务 / 回滚），
/// DeepSeek 一律 mock（HttpMessageHandler 桩），不做真实调用。
/// </summary>
public sealed class MiraiTestFixture : IDisposable
{
    private readonly SqliteConnection _anchor;

    public string ConnectionString { get; }

    public MiraiTestFixture()
    {
        ConnectionString = $"Data Source=mirai-test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        // 锚定连接：保持共享内存库在全部上下文关闭后仍然存活
        _anchor = new SqliteConnection(ConnectionString);
        _anchor.Open();
        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();
        SeedUser(ctx);
    }

    public MiraiNoteDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<MiraiNoteDbContext>()
            .UseSqlite(ConnectionString)
            .Options);

    /// <summary>可注入 SaveChanges 拦截器的上下文（事务性回滚测试用）。</summary>
    public MiraiNoteDbContext CreateContextWithInterceptor(IInterceptor interceptor) =>
        new(new DbContextOptionsBuilder<MiraiNoteDbContext>()
            .UseSqlite(ConnectionString)
            .AddInterceptors(interceptor)
            .Options);

    public InboxTriageService CreateInboxService(IHttpClientFactory? factory = null) =>
        new(CreateContext(),
            Options.Create(new DeepSeekOptions { ApiKey = "test-key", Model = "deepseek-test" }),
            factory ?? MockDeepSeekFactory(_ => DeepSeekContentResponse("")),
            NullLogger<InboxTriageService>.Instance);

    public BriefingService CreateBriefingService(IHttpClientFactory? factory = null) =>
        new(CreateContext(),
            Options.Create(new DeepSeekOptions { ApiKey = "test-key", Model = "deepseek-test" }),
            factory ?? MockDeepSeekFactory(_ => DeepSeekContentResponse("")),
            NullLogger<BriefingService>.Instance);

    public DayOverviewService CreateDayOverviewService(IHttpClientFactory? factory) =>
        new(CreateContext(), CreateBriefingService(factory));

    private static void SeedUser(MiraiNoteDbContext ctx)
    {
        ctx.Users.Add(new User
        {
            Username = "tester",
            Email = "tester@example.com",
            PasswordHash = "hash"
        });
        ctx.SaveChanges();
    }

    // ===== DeepSeek mock =====

    /// <summary>
    /// 构造 IHttpClientFactory 桩：capture 收到请求体（可断言 prompt 装配），
    /// respond 决定每次响应内容；返回 (factory, 请求体列表)。
    /// </summary>
    public static (IHttpClientFactory Factory, List<string> CapturedRequests) MockDeepSeek(
        Func<string, Task<string>> respond)
    {
        var captured = new List<string>();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(new StubHandler(async req =>
            {
                var body = req.Content == null ? "" : await req.Content.ReadAsStringAsync();
                captured.Add(body);
                return DeepSeekContentResponse(await respond(body));
            })));
        return (factory.Object, captured);
    }

    public static IHttpClientFactory MockDeepSeekFactory(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(new StubHandler(_ => Task.FromResult(respond(_)))));
        return factory.Object;
    }

    /// <summary>DeepSeek chat/completions 响应包装：content 为模型输出文本。</summary>
    public static HttpResponseMessage DeepSeekContentResponse(string content) =>
        new()
        {
            StatusCode = System.Net.HttpStatusCode.OK,
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { content } } }
                }))
        };

    /// <summary>
    /// 从捕获的请求体中还原全部消息文本。
    /// 请求体 JSON 默认转义非 ASCII 字符，中文断言必须先解码。
    /// </summary>
    public static string DecodeMessageText(string requestBody)
    {
        using var doc = JsonDocument.Parse(requestBody);
        var sb = new System.Text.StringBuilder();
        foreach (var message in doc.RootElement.GetProperty("messages").EnumerateArray())
            if (message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                sb.AppendLine(c.GetString());
        return sb.ToString();
    }

    /// <summary>再种一个用户并返回其 Id（跨用户隔离断言用）。</summary>
    public int SeedAnotherUser()
    {
        using var ctx = CreateContext();
        var user = new User
        {
            Username = "other-" + Guid.NewGuid().ToString("N")[..8],
            Email = $"other-{Guid.NewGuid():N}@example.com",
            PasswordHash = "hash"
        };
        ctx.Users.Add(user);
        ctx.SaveChanges();
        return user.Id;
    }

    public static HttpResponseMessage DeepSeekError() =>
        new() { StatusCode = System.Net.HttpStatusCode.InternalServerError, Content = new StringContent("boom") };

    /// <summary>双意图分拣样本（task + worklog），模拟模型 json_object 输出。</summary>
    public const string DoubleIntentTriageJson = """
        {
          "items": [
            { "suggestionId": "s1", "type": "task", "confidence": 0.92,
              "rationale": "含行动+期限",
              "fields": { "content": "推动安全评审排期（老王）", "remindAtLocal": "2026-08-26T09:00", "priority": 2, "section": "work" } },
            { "suggestionId": "s2", "type": "worklog", "confidence": 0.81,
              "rationale": "工作事实记录",
              "fields": { "title": "安全评审排期待推进", "content": "重构方案需过安全评审。", "tags": ["重构方案"], "category": null } },
            { "suggestionId": "s3", "type": "knowledge", "confidence": 0.5,
              "rationale": "感想", "fields": null },
            { "suggestionId": "s4", "type": "ignore", "confidence": 0.3,
              "rationale": "测试", "fields": null }
          ],
          "uncertain": ["「周三」按当前时间推算为 2026-08-26"]
        }
        """;

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _respond;

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => _respond(request);
    }

    public void Dispose() => _anchor.Dispose();
}

/// <summary>在第二个目标实体写入时抛 DbUpdateException，验证 dispatch 单事务回滚。</summary>
public sealed class ThrowOnSecondWorkLogInsertInterceptor : SaveChangesInterceptor
{
    private int _saveCallsWithAdds;

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        CountAndThrow(eventData);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        CountAndThrow(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void CountAndThrow(DbContextEventData eventData)
    {
        if (eventData.Context == null) return;
        var addsWorkLog = eventData.Context.ChangeTracker.Entries<WorkLog>()
            .Any(e => e.State == EntityState.Added);
        var addsAnything = eventData.Context.ChangeTracker.Entries()
            .Any(e => e.State == EntityState.Added);

        // 首个目标实体（Memo）已落库后，再写 WorkLog 即失败 → 整个事务应回滚
        if (addsWorkLog && _saveCallsWithAdds >= 1)
            throw new DbUpdateException("forced failure for transaction test");
        if (addsAnything) _saveCallsWithAdds++;
    }
}
