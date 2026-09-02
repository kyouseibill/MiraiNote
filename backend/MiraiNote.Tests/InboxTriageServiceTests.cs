using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MiraiNote.Core.Services;
using MiraiNote.Core.Services.Mirai;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Mirai;
using Moq;
using Xunit;

namespace MiraiNote.Tests;

/// <summary>
/// 收件箱分拣单测（契约 §2.1–2.6）：
/// 双意图拆分 / 失败降级 / 重试 / recentTags 注入 / dispatch 事务与 Local→UTC 换算 / 丢弃与撤销。
/// 每个测试独立 SQLite 内存库（xUnit 每测试重建实例），互不污染。
/// </summary>
public class InboxTriageServiceTests : IDisposable
{
    private const int UserId = 1;
    private readonly MiraiTestFixture _fx;

    public InboxTriageServiceTests() => _fx = new MiraiTestFixture();

    public void Dispose() => _fx.Dispose();

    // ===== 2.1 创建 + 同步分拣 =====

    [Fact]
    public async Task CreateAndTriage_DoubleIntent_SplitsAndPersists()
    {
        var (factory, captured) = MiraiTestFixture.MockDeepSeek(
            _ => Task.FromResult(MiraiTestFixture.DoubleIntentTriageJson));
        var svc = _fx.CreateInboxService(factory);

        var dto = await svc.CreateAndTriageAsync(UserId, new CreateInboxItemRequest(
            "重构方案要过安全评审，老王周三前要排期，顺便写个工作日志", 1,
            "2026-08-22T09:20:00", 480));

        Assert.Equal((int)InboxStatus.Triaged, dto.Status);
        Assert.NotNull(dto.AiParse);
        Assert.Equal(4, dto.AiParse!.Items.Count);
        Assert.Equal("task", dto.AiParse.Items[0].Type);
        Assert.Equal("worklog", dto.AiParse.Items[1].Type);
        Assert.Single(dto.AiParse.Uncertain);
        Assert.Equal("deepseek-test", dto.AiModel);
        Assert.Null(dto.Error);

        // 信封含换算上下文，对外 DTO 不暴露
        await using var db = _fx.CreateContext();
        var row = await db.InboxItems.SingleAsync(i => i.Id == dto.Id);
        Assert.Equal((byte)InboxStatus.Triaged, row.Status);
        var envelope = JsonSerializer.Deserialize<JsonElement>(row.AiParse!);
        Assert.Equal(480, envelope.GetProperty("tzOffsetMinutes").GetInt32());
        Assert.Equal("2026-08-22T09:20:00", envelope.GetProperty("localTime").GetString());

        // 请求体：json_object + system prompt 含本地时间与时区
        Assert.Single(captured);
        Assert.Contains("json_object", captured[0]);
        Assert.Contains("2026-08-22T09:20:00", captured[0]);
        Assert.Contains("480", captured[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAndTriage_EmptyRaw_Rejected400(string raw)
    {
        var svc = _fx.CreateInboxService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.CreateAndTriageAsync(UserId, new CreateInboxItemRequest(raw, 1, "2026-08-22T09:20:00", 480)));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task CreateAndTriage_RawTooLong_Rejected400()
    {
        var svc = _fx.CreateInboxService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.CreateAndTriageAsync(UserId, new CreateInboxItemRequest(new string('长', 2001), 1,
                "2026-08-22T09:20:00", 480)));
        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(9999)]
    public async Task CreateAndTriage_InvalidSourceOrTz_Rejected400(int source)
    {
        var svc = _fx.CreateInboxService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.CreateAndTriageAsync(UserId, new CreateInboxItemRequest("内容", source,
                "2026-08-22T09:20:00", 480)));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task CreateAndTriage_DeepSeekFails_StatusErrorWithoutThrowing()
    {
        var errorFactory = MiraiTestFixture.MockDeepSeekFactory(_ => MiraiTestFixture.DeepSeekError());
        var svc = _fx.CreateInboxService(errorFactory);

        var dto = await svc.CreateAndTriageAsync(UserId, new CreateInboxItemRequest(
            "内容", 1, "2026-08-22T09:20:00", 480));

        // 分拣失败不报错：仍返回，status=5、error 有值（客户端提供重试）
        Assert.Equal((int)InboxStatus.Error, dto.Status);
        Assert.NotNull(dto.Error);
        Assert.Contains("500", dto.Error);
        Assert.Null(dto.AiParse);
    }

    [Fact]
    public async Task CreateAndTriage_InvalidJsonThenSuccess_RetriedOnce()
    {
        var calls = 0;
        var (factory, captured) = MiraiTestFixture.MockDeepSeek(_ =>
        {
            calls++;
            return Task.FromResult(calls == 1 ? "not a json ###" : MiraiTestFixture.DoubleIntentTriageJson);
        });
        var svc = _fx.CreateInboxService(factory);

        var dto = await svc.CreateAndTriageAsync(UserId, new CreateInboxItemRequest(
            "双意图样本", 1, "2026-08-22T09:20:00", 480));

        Assert.Equal((int)InboxStatus.Triaged, dto.Status);
        Assert.Equal(2, calls); // 失败重试 1 次
        Assert.Equal(2, captured.Count);
    }

    [Fact]
    public async Task CreateAndTriage_PromptIncludesRecentTagsTop10()
    {
        await using (var db = _fx.CreateContext())
        {
            for (var i = 0; i < 50; i++)
                db.WorkLogs.Add(new WorkLog
                {
                    UserId = UserId,
                    Title = $"日志{i}",
                    Tags = i % 2 == 0 ? "安全评审,重构方案" : "重构方案",
                    LogDate = DateTime.UtcNow.Date.AddDays(-i % 10)
                });
            await db.SaveChangesAsync();
        }

        var (factory, captured) = MiraiTestFixture.MockDeepSeek(
            _ => Task.FromResult(MiraiTestFixture.DoubleIntentTriageJson));
        var svc = _fx.CreateInboxService(factory);

        await svc.CreateAndTriageAsync(UserId, new CreateInboxItemRequest(
            "再记一条", 1, "2026-08-22T09:20:00", 480));

        // 近 50 条 WorkLog tag 频次：重构方案(50) > 安全评审(25)，均应注入 prompt 候选
        var promptText = MiraiTestFixture.DecodeMessageText(captured[0]);
        Assert.Contains("重构方案", promptText);
        Assert.Contains("安全评审", promptText);
    }

    // ===== 2.2 列表 =====

    [Fact]
    public async Task GetList_ExcludesDiscardedByDefault_OrdersByCreatedDesc()
    {
        var otherUserId = _fx.SeedAnotherUser();
        await using (var db = _fx.CreateContext())
        {
            db.InboxItems.Add(new InboxItem { UserId = UserId, Raw = "a", Source = 1, Status = (byte)InboxStatus.Triaged });
            db.InboxItems.Add(new InboxItem { UserId = UserId, Raw = "b", Source = 1, Status = (byte)InboxStatus.Discarded });
            db.InboxItems.Add(new InboxItem { UserId = UserId, Raw = "c", Source = 1, Status = (byte)InboxStatus.Error });
            db.InboxItems.Add(new InboxItem { UserId = otherUserId, Raw = "other-user", Source = 1, Status = (byte)InboxStatus.Triaged });
            await db.SaveChangesAsync();
        }

        var svc = _fx.CreateInboxService();
        var page = await svc.GetListAsync(UserId, status: null, page: 1, pageSize: 50);

        Assert.Equal(2, page.Total); // 默认排除 Discarded 与他人数据
        Assert.All(page.Items, i => Assert.NotEqual((int)InboxStatus.Discarded, i.Status));
    }

    [Fact]
    public async Task GetList_ExplicitDiscardedFilter_ReturnsDiscarded()
    {
        await using (var db = _fx.CreateContext())
        {
            db.InboxItems.Add(new InboxItem { UserId = UserId, Raw = "d", Source = 1, Status = (byte)InboxStatus.Discarded });
            await db.SaveChangesAsync();
        }

        var svc = _fx.CreateInboxService();
        var page = await svc.GetListAsync(UserId, status: 4, page: 1, pageSize: 50);
        Assert.Equal(1, page.Total);
    }

    // ===== 2.3 重新分拣 =====

    [Fact]
    public async Task Retriage_Missing_404()
    {
        var svc = _fx.CreateInboxService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.RetriageAsync(UserId, 424242, new RetriageRequest(null)));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Retriage_StoresCorrectionAndReusesTzContext()
    {
        var (factory, captured) = MiraiTestFixture.MockDeepSeek(
            _ => Task.FromResult(MiraiTestFixture.DoubleIntentTriageJson));
        var svc = _fx.CreateInboxService(factory);

        var created = await svc.CreateAndTriageAsync(UserId, new CreateInboxItemRequest(
            "原始内容", 2, "2026-08-22T09:20:00", 480));
        var retriaged = await svc.RetriageAsync(UserId, created.Id, new RetriageRequest("第二条不是任务，是想法"));

        Assert.Equal((int)InboxStatus.Triaged, retriaged.Status);
        Assert.Equal("第二条不是任务，是想法", retriaged.CorrectionNote);
        // 重分拣复用捕获时的本地时间/时区，纠错语注入 prompt（请求体非 ASCII 已转义，先解码）
        var promptText = MiraiTestFixture.DecodeMessageText(captured[^1]);
        Assert.Contains("2026-08-22T09:20:00", promptText);
        Assert.Contains("第二条不是任务，是想法", promptText);

        await using var db = _fx.CreateContext();
        var row = await db.InboxItems.SingleAsync(i => i.Id == created.Id);
        Assert.Equal((byte)InboxSource.Retriage, row.Source);
    }

    // ===== 2.4 dispatch =====

    /// <summary>直接落库一个已分拣捕获项（信封带 tz 上下文），供 dispatch 测试使用。</summary>
    private async Task<InboxItem> SeedTriagedItemAsync(int tzOffsetMinutes, string? remindAtLocal)
    {
        await using var db = _fx.CreateContext();
        var envelopeJson = JsonSerializer.Serialize(new
        {
            items = new List<TriageSuggestionDto>
            {
                new("s1", "task", 0.9, "行动", new FieldsDto(
                    "任务内容", remindAtLocal, 2, "work", null, null, null, null)),
                new("s2", "worklog", 0.8, "事实", new FieldsDto(
                    "补充说明", null, null, null, "工作日志标题", new List<string> { "标签" }, "分类", null))
            },
            uncertain = new List<string>(),
            tzOffsetMinutes,
            localTime = "2026-08-22T09:20:00"
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var item = new InboxItem
        {
            UserId = UserId,
            Raw = "原始捕获",
            Source = 1,
            Status = (byte)InboxStatus.Triaged,
            AiParse = envelopeJson
        };
        db.InboxItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    [Fact]
    public async Task Dispatch_CreatesEntitiesAndAuditLogs_Atomic()
    {
        var item = await SeedTriagedItemAsync(480, "2026-08-26T09:00");
        var svc = _fx.CreateInboxService();

        var result = await svc.DispatchAsync(UserId, item.Id, new DispatchRequest(new List<DispatchItemRequest>
        {
            new("s1", new FieldsDto(null, null, 3, null, null, null, null, null)), // priority 覆盖为 3
            new("s2", null)
        }));

        Assert.Equal(item.Id, result.InboxItemId);
        Assert.Equal(2, result.Created.Count);
        Assert.Equal("task", result.Created[0].Type);
        Assert.Equal("任务内容", result.Created[0].Title);

        await using var db = _fx.CreateContext();
        var memo = await db.Memos.SingleAsync(m => m.Id == result.Created[0].Id);
        Assert.Equal(3, memo.Priority);
        Assert.Equal(1, memo.RemindMethods);
        var workLog = await db.WorkLogs.SingleAsync(w => w.Id == result.Created[1].Id);
        Assert.Equal("工作日志标题", workLog.Title);

        var logs = await db.AIActionLogs
            .Where(l => l.ActionType == AIActionLog.ActionTypeInboxDispatch)
            .ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.All(logs, l => Assert.Equal("applied", l.Decision));
        Assert.All(logs, l => Assert.Equal(item.Id, l.TargetId));

        var row = await db.InboxItems.SingleAsync(i => i.Id == item.Id);
        Assert.Equal((byte)InboxStatus.Dispatched, row.Status);
    }

    /// <summary>验收 2：相对日期按 tzOffsetMinutes 换算零时区错误（覆盖 ±12h 及极端偏移）。</summary>
    [Theory]
    [InlineData(480, "2026-08-26T09:00", "2026-08-26T01:00:00")]   // 东八区
    [InlineData(-720, "2026-08-26T09:00", "2026-08-26T21:00:00")]  // UTC-12
    [InlineData(720, "2026-08-26T09:00", "2026-08-25T21:00:00")]   // UTC+12
    [InlineData(0, "2026-08-26T09:00", "2026-08-26T09:00:00")]     // 零时区
    [InlineData(660, "2026-08-26T09:00", "2026-08-25T22:00:00")]   // UTC+11
    [InlineData(-660, "2026-08-26T09:00", "2026-08-26T20:00:00")]  // UTC-11
    [InlineData(840, "2026-08-26T09:00", "2026-08-25T19:00:00")]   // UTC+14（上限）
    public async Task Dispatch_ConvertsRemindAtLocalToUtc_ByTzOffset(
        int tzOffsetMinutes, string remindAtLocal, string expectedUtc)
    {
        var item = await SeedTriagedItemAsync(tzOffsetMinutes, remindAtLocal);
        var svc = _fx.CreateInboxService();

        var result = await svc.DispatchAsync(UserId, item.Id, new DispatchRequest(new List<DispatchItemRequest>
        {
            new("s1", null)
        }));

        await using var db = _fx.CreateContext();
        var memo = await db.Memos.SingleAsync(m => m.Id == result.Created[0].Id);
        // SQLite 往返后 Kind 为 Unspecified，按壁钟时刻（ticks）比较换算结果
        Assert.Equal(DateTime.Parse(expectedUtc).Ticks, memo.RemindAt!.Value.Ticks);
    }

    /// <summary>验收 3：两条建议其二失败 → 全部回滚（实体、审计、状态均不变）。</summary>
    [Fact]
    public async Task Dispatch_SecondCreateFails_RollsBackAll()
    {
        var item = await SeedTriagedItemAsync(480, null);
        var svc = new InboxTriageService(
            _fx.CreateContextWithInterceptor(new ThrowOnSecondWorkLogInsertInterceptor()),
            Options.Create(new DeepSeekOptions { ApiKey = "k", Model = "m" }),
            MiraiTestFixture.MockDeepSeekFactory(_ => MiraiTestFixture.DeepSeekError()),
            NullLogger<InboxTriageService>.Instance);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.DispatchAsync(UserId, item.Id, new DispatchRequest(new List<DispatchItemRequest>
            {
                new("s1", null), new("s2", null)
            })));
        Assert.Equal(500, ex.StatusCode);

        await using var db = _fx.CreateContext();
        Assert.Equal(0, await db.Memos.CountAsync(m => m.UserId == UserId && m.Content == "任务内容"));
        Assert.Equal(0, await db.WorkLogs.CountAsync(w => w.UserId == UserId && w.Title == "工作日志标题"));
        Assert.Equal(0, await db.AIActionLogs.CountAsync());
        var row = await db.InboxItems.SingleAsync(i => i.Id == item.Id);
        Assert.Equal((byte)InboxStatus.Triaged, row.Status); // 未被置为 Dispatched
    }

    [Fact]
    public async Task Dispatch_NotTriaged_409()
    {
        await using (var db = _fx.CreateContext())
        {
            db.InboxItems.Add(new InboxItem { UserId = UserId, Raw = "x", Source = 1, Status = (byte)InboxStatus.Pending });
            await db.SaveChangesAsync();
        }
        var svc = _fx.CreateInboxService();
        await using var db2 = _fx.CreateContext();
        var id = await db2.InboxItems.Where(i => i.UserId == UserId && i.Status == (byte)InboxStatus.Pending)
            .Select(i => i.Id).FirstAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.DispatchAsync(UserId, id, new DispatchRequest(new List<DispatchItemRequest> { new("s1", null) })));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Dispatch_UnknownSuggestion_422()
    {
        var item = await SeedTriagedItemAsync(480, null);
        var svc = _fx.CreateInboxService();

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.DispatchAsync(UserId, item.Id, new DispatchRequest(new List<DispatchItemRequest>
            {
                new("s-not-exist", null)
            })));
        Assert.Equal(422, ex.StatusCode);
    }

    [Fact]
    public async Task Dispatch_KnowledgeOrIgnoreType_422()
    {
        await using (var db = _fx.CreateContext())
        {
            db.InboxItems.Add(new InboxItem
            {
                UserId = UserId, Raw = "y", Source = 1, Status = (byte)InboxStatus.Triaged,
                AiParse = JsonSerializer.Serialize(new
                {
                    items = new List<TriageSuggestionDto>
                    {
                        new("k1", "knowledge", 0.9, "", null),
                        new("i1", "ignore", 0.9, "", null)
                    },
                    uncertain = new List<string>(), tzOffsetMinutes = 0, localTime = "2026-08-22T09:00"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            });
            await db.SaveChangesAsync();
        }
        var svc = _fx.CreateInboxService();
        await using var db2 = _fx.CreateContext();
        var id = await db2.InboxItems.Where(i => i.Raw == "y").Select(i => i.Id).FirstAsync();

        var ex1 = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.DispatchAsync(UserId, id, new DispatchRequest(new List<DispatchItemRequest> { new("k1", null) })));
        Assert.Equal(422, ex1.StatusCode);
        var ex2 = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.DispatchAsync(UserId, id, new DispatchRequest(new List<DispatchItemRequest> { new("i1", null) })));
        Assert.Equal(422, ex2.StatusCode);
    }

    [Fact]
    public async Task Dispatch_OverrideKeyNotForType_400()
    {
        var item = await SeedTriagedItemAsync(480, null);
        var svc = _fx.CreateInboxService();

        // task 建议不允许覆盖 worklog 专属字段 title
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.DispatchAsync(UserId, item.Id, new DispatchRequest(new List<DispatchItemRequest>
            {
                new("s1", new FieldsDto(null, null, null, null, "越权标题", null, null, null))
            })));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Dispatch_MissingRequiredFieldWithoutOverride_400()
    {
        await using (var db = _fx.CreateContext())
        {
            db.InboxItems.Add(new InboxItem
            {
                UserId = UserId, Raw = "z", Source = 1, Status = (byte)InboxStatus.Triaged,
                AiParse = JsonSerializer.Serialize(new
                {
                    items = new List<TriageSuggestionDto>
                    {
                        new("s1", "task", 0.9, "", new FieldsDto(null, null, null, null, null, null, null, null))
                    },
                    uncertain = new List<string>(), tzOffsetMinutes = 0, localTime = "2026-08-22T09:00"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            });
            await db.SaveChangesAsync();
        }
        var svc = _fx.CreateInboxService();
        await using var db2 = _fx.CreateContext();
        var id = await db2.InboxItems.Where(i => i.Raw == "z").Select(i => i.Id).FirstAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.DispatchAsync(UserId, id, new DispatchRequest(new List<DispatchItemRequest> { new("s1", null) })));
        Assert.Equal(400, ex.StatusCode);
    }

    // ===== 2.5 discard / 2.6 undo =====

    [Fact]
    public async Task Discard_SoftDeletesWithAuditLog()
    {
        var item = await SeedTriagedItemAsync(480, null);
        var svc = _fx.CreateInboxService();

        await svc.DiscardAsync(UserId, item.Id);

        await using var db = _fx.CreateContext();
        var row = await db.InboxItems.SingleAsync(i => i.Id == item.Id);
        Assert.Equal((byte)InboxStatus.Discarded, row.Status);
        var log = await db.AIActionLogs.SingleAsync(l => l.ActionType == AIActionLog.ActionTypeInboxDiscard);
        Assert.Equal("discarded", log.Decision);

        // 显式 status=4 过滤仍可检索（契约 §2.2）；默认列表排除（契约 §2.2，另测）
        var explicitPage = await svc.GetListAsync(UserId, status: 4, page: 1, pageSize: 50);
        Assert.Equal(1, explicitPage.Total);
        var defaultPage = await svc.GetListAsync(UserId, status: null, page: 1, pageSize: 50);
        Assert.Equal(0, defaultPage.Total);
    }

    [Fact]
    public async Task Discard_AlreadyDispatched_409()
    {
        var item = await SeedTriagedItemAsync(480, null);
        var svc = _fx.CreateInboxService();
        await svc.DispatchAsync(UserId, item.Id, new DispatchRequest(new List<DispatchItemRequest> { new("s1", null) }));

        var ex = await Assert.ThrowsAsync<BusinessException>(() => svc.DiscardAsync(UserId, item.Id));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Undo_SoftDeletesCreatedEntitiesAndRestoresTriaged()
    {
        var item = await SeedTriagedItemAsync(480, null);
        var svc = _fx.CreateInboxService();
        var result = await svc.DispatchAsync(UserId, item.Id, new DispatchRequest(new List<DispatchItemRequest>
        {
            new("s1", null), new("s2", null)
        }));

        await svc.UndoAsync(UserId, item.Id);

        await using var db = _fx.CreateContext();
        Assert.Equal(0, await db.Memos.CountAsync(m => m.Id == result.Created[0].Id));
        Assert.Equal(0, await db.WorkLogs.CountAsync(w => w.Id == result.Created[1].Id));
        var row = await db.InboxItems.SingleAsync(i => i.Id == item.Id);
        Assert.Equal((byte)InboxStatus.Triaged, row.Status);
        var undoLog = await db.AIActionLogs.SingleAsync(l => l.ActionType == AIActionLog.ActionTypeInboxUndo);
        Assert.Equal("undone", undoLog.Decision);
    }

    [Fact]
    public async Task Undo_NotDispatched_409()
    {
        var item = await SeedTriagedItemAsync(480, null);
        var svc = _fx.CreateInboxService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() => svc.UndoAsync(UserId, item.Id));
        Assert.Equal(409, ex.StatusCode);
    }
}
