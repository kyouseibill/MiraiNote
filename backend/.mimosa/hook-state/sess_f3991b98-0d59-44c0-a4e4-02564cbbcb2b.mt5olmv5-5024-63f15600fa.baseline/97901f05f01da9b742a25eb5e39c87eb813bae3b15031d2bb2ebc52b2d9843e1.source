using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MiraiNote.Core.Services.Mirai;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Mirai;
using Xunit;

namespace MiraiNote.Tests;

/// <summary>
/// 晨报单测（契约 §2.7/2.8）：生成与溯源、占位行防并发（过滤唯一索引）、regenerate 每日限额 429。
/// </summary>
public class BriefingServiceTests : IDisposable
{
    private const int UserId = 1;
    private readonly MiraiTestFixture _fx;

    public BriefingServiceTests() => _fx = new MiraiTestFixture();

    public void Dispose() => _fx.Dispose();

    private const string BriefingMarkdown = """
        ## 晨报
        今天有 **1 件到期事项**——推动安全评审排期【来源: 推动安全评审排期 #101】。
        昨日完成迁移方案修订。
        """;

    private async Task<int> SeedDueMemoAsync()
    {
        await using var db = _fx.CreateContext();
        var memo = new Memo
        {
            UserId = UserId, Section = "work", Content = "推动安全评审排期",
            RemindAt = DateTime.UtcNow.AddHours(2), Priority = 3
        };
        db.Memos.Add(memo);
        await db.SaveChangesAsync();
        return memo.Id;
    }

    [Fact]
    public async Task GetOrGenerate_GeneratesStoresAndExtractsSources()
    {
        var memoId = await SeedDueMemoAsync();
        var (factory, captured) = MiraiTestFixture.MockDeepSeek(
            _ => Task.FromResult(BriefingMarkdown));
        var svc = _fx.CreateBriefingService(factory);

        var outcome = await svc.GetOrGenerateAsync(UserId, DateOnly.FromDateTime(DateTime.UtcNow), 480);

        Assert.NotNull(outcome.Briefing);
        Assert.Null(outcome.Error);
        Assert.Contains("晨报", outcome.Briefing!.Content);
        Assert.Equal("deepseek-test", outcome.Briefing.Model);
        // 溯源：来源清单含被引用的 memo
        var source = Assert.Single(outcome.Briefing.Sources);
        Assert.Equal("memo", source.Type);
        Assert.Equal(memoId, source.Id);

        // prompt 事实区注入了到期任务（纯 SQL 聚合，不含推断）；请求体非 ASCII 已转义，先解码
        var promptText = MiraiTestFixture.DecodeMessageText(captured[0]);
        Assert.Contains("推动安全评审排期", promptText);
        Assert.Contains("给定事实", promptText);

        await using var db = _fx.CreateContext();
        Assert.Equal(1, await db.DailyBriefings.CountAsync());
    }

    /// <summary>验收 4：并发两次 GET 只生成一条（过滤唯一索引 + 占位行防并发生效）。</summary>
    [Fact]
    public async Task GetOrGenerate_ConcurrentCalls_ProduceSingleRow()
    {
        await SeedDueMemoAsync();
        var gate = new SemaphoreSlim(0, 1);
        var (factory, _) = MiraiTestFixture.MockDeepSeek(_ =>
        {
            gate.Wait(TimeSpan.FromSeconds(10)); // 放慢生成，放大并发窗口
            return Task.FromResult(BriefingMarkdown);
        });
        var svc1 = _fx.CreateBriefingService(factory);
        var svc2 = _fx.CreateBriefingService(factory);

        var t1 = Task.Run(() => svc1.GetOrGenerateAsync(UserId, DateOnly.FromDateTime(DateTime.UtcNow), 480));
        var t2 = Task.Run(() => svc2.GetOrGenerateAsync(UserId, DateOnly.FromDateTime(DateTime.UtcNow), 480));
        await Task.Delay(300);
        gate.Release();
        var outcomes = await Task.WhenAll(t1, t2);

        await using var db = _fx.CreateContext();
        Assert.Equal(1, await db.DailyBriefings.CountAsync()); // 唯一索引兜底：仅一条
        Assert.All(outcomes, o => Assert.NotNull(o.Briefing)); // 两个请求均拿到内容
        Assert.All(outcomes, o => Assert.Contains("晨报", o.Briefing!.Content));
    }

    /// <summary>全空事实前置短路：不调 LLM，返回固定文案（tools/eval/REPORT.md b06）。</summary>
    [Fact]
    public async Task GetOrGenerate_EmptyFacts_ShortCircuitsWithoutLlm()
    {
        var calls = 0;
        var (factory, _) = MiraiTestFixture.MockDeepSeek(_ =>
        {
            calls++;
            return Task.FromResult(BriefingMarkdown);
        });
        var svc = _fx.CreateBriefingService(factory);

        var outcome = await svc.GetOrGenerateAsync(UserId, DateOnly.FromDateTime(DateTime.UtcNow), 480);

        Assert.Equal(0, calls); // 未调用模型
        Assert.NotNull(outcome.Briefing);
        Assert.Contains("没有到期任务", outcome.Briefing.Content);
        Assert.Empty(outcome.Briefing.Sources);
    }

    [Fact]
    public async Task GetOrGenerate_DeepSeekFails_DegradesToErrorWithoutThrowing()
    {
        await SeedDueMemoAsync(); // 有事实才会走 LLM 路径（全空事实会短路）
        var svc = _fx.CreateBriefingService(
            MiraiTestFixture.MockDeepSeekFactory(_ => MiraiTestFixture.DeepSeekError()));

        var outcome = await svc.GetOrGenerateAsync(UserId, DateOnly.FromDateTime(DateTime.UtcNow), 480);

        Assert.Null(outcome.Briefing);
        Assert.NotNull(outcome.Error);

        // 占位行被软删，失败后可重试
        await using var db = _fx.CreateContext();
        Assert.Equal(0, await db.DailyBriefings.CountAsync());
    }

    [Fact]
    public async Task GetOrGenerate_ExistingRow_ServedFromCache()
    {
        await SeedDueMemoAsync(); // 首次生成需走 LLM 路径（全空事实会短路）
        var calls = 0;
        var (factory, _) = MiraiTestFixture.MockDeepSeek(_ =>
        {
            calls++;
            return Task.FromResult(BriefingMarkdown);
        });
        var svc = _fx.CreateBriefingService(factory);
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        await svc.GetOrGenerateAsync(UserId, date, 480);
        await svc.GetOrGenerateAsync(UserId, date, 480);

        Assert.Equal(1, calls); // 第二次命中缓存，不再调模型
    }

    [Fact]
    public async Task Regenerate_ReplacesRowAndCountsLimit()
    {
        await SeedDueMemoAsync(); // 走 LLM 路径（全空事实会短路）
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var (factory, _) = MiraiTestFixture.MockDeepSeek(_ => Task.FromResult(BriefingMarkdown));

        // 预置一条已有晨报
        await using (var db = _fx.CreateContext())
        {
            db.DailyBriefings.Add(new DailyBriefing
            {
                UserId = UserId, BriefDate = date, Content = "旧晨报", GeneratedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var svc = _fx.CreateBriefingService(factory);
        var first = await svc.RegenerateAsync(UserId, date, 480);
        Assert.Contains("晨报", first.Content);

        await using (var db = _fx.CreateContext())
        {
            Assert.Equal(1, await db.DailyBriefings.CountAsync()); // 未删除（全局过滤器口径）
            Assert.Equal(1, await db.DailyBriefings.IgnoreQueryFilters()
                .CountAsync(b => b.IsDeleted)); // 旧行软删
        }

        // 限额：当日剩余 2 次，第 4 次抛 429
        await svc.RegenerateAsync(UserId, date, 480);
        await svc.RegenerateAsync(UserId, date, 480);
        var ex = await Assert.ThrowsAsync<BusinessException>(() => svc.RegenerateAsync(UserId, date, 480));
        Assert.Equal(429, ex.StatusCode);
    }
}

/// <summary>今日流聚合（契约 §2.7）与 AI 统计（契约 §2.11）。</summary>
public class DayOverviewAndStatsTests : IDisposable
{
    private const int UserId = 1;
    private readonly MiraiTestFixture _fx;

    public DayOverviewAndStatsTests() => _fx = new MiraiTestFixture();

    public void Dispose() => _fx.Dispose();

    [Fact]
    public async Task Overview_AggregatesFeedDueAndCounts()
    {
        var today = DateTime.UtcNow.Date;
        await using (var db = _fx.CreateContext())
        {
            db.Memos.Add(new Memo
            {
                UserId = UserId, Section = "work", Content = "今日到期任务",
                RemindAt = DateTime.UtcNow, Priority = 3
            });
            db.Memos.Add(new Memo
            {
                UserId = UserId, Section = "life", Content = "无提醒备忘", Priority = 1
            });
            db.WorkLogs.Add(new WorkLog { UserId = UserId, Title = "今日工作", LogDate = today });
            db.LifeLogs.Add(new LifeLog { UserId = UserId, Content = "今日生活", LogDate = today });
            db.InboxItems.Add(new InboxItem { UserId = UserId, Raw = "今日捕获", Source = 1, Status = (byte)InboxStatus.Triaged });
            await db.SaveChangesAsync();
        }

        var svc = _fx.CreateDayOverviewService(
            MiraiTestFixture.MockDeepSeekFactory(_ => MiraiTestFixture.DeepSeekContentResponse("## 晨报\n测试")));

        var overview = await svc.GetOverviewAsync(UserId, DateOnly.FromDateTime(DateTime.UtcNow), 0);

        Assert.NotNull(overview.Briefing);
        Assert.Null(overview.BriefingError);
        Assert.Single(overview.DueTasks);
        Assert.Empty(overview.OverdueTasks);

        var kinds = overview.TodayFeed.Select(f => f.Kind).ToList();
        Assert.Contains("capture", kinds);
        Assert.Contains("worklog", kinds);
        Assert.Contains("lifelog", kinds);
        Assert.Contains("task", kinds);   // 带 RemindAt 的 Memo
        Assert.Contains("memo", kinds);   // 无 RemindAt 的 Memo
        Assert.Contains("briefing", kinds);
        // 按时间升序
        Assert.Equal(overview.TodayFeed, overview.TodayFeed.OrderBy(f => f.Time).ToList());
        // 全部 aiSummary 为 null（M1 恒空，M2 预留）
        Assert.All(overview.TodayFeed, f => Assert.Null(f.AiSummary));

        Assert.Equal(1, overview.InboxPendingCount);
        Assert.True(overview.WeekEntryCount >= 2);
    }

    [Fact]
    public async Task Overview_BriefingFailure_DegradesWithoutAffectingData()
    {
        await using (var db = _fx.CreateContext())
        {
            db.Memos.Add(new Memo
            {
                UserId = UserId, Section = "work", Content = "到期任务",
                RemindAt = DateTime.UtcNow, Priority = 2
            });
            await db.SaveChangesAsync();
        }

        var svc = _fx.CreateDayOverviewService(
            MiraiTestFixture.MockDeepSeekFactory(_ => MiraiTestFixture.DeepSeekError()));

        var overview = await svc.GetOverviewAsync(UserId, DateOnly.FromDateTime(DateTime.UtcNow), 0);

        // 生成失败不抛错：briefingError 有值，纯数据区正常返回
        Assert.Null(overview.Briefing);
        Assert.NotNull(overview.BriefingError);
        Assert.NotNull(overview.DueTasks);
        Assert.Equal(0, overview.InboxPendingCount);
    }

    [Fact]
    public async Task Stats_GroupsByActionTypeAndFillsLast7Days()
    {
        var otherUserId = _fx.SeedAnotherUser();
        await using (var db = _fx.CreateContext())
        {
            var now = DateTime.UtcNow;
            for (var i = 0; i < 3; i++)
                db.AIActionLogs.Add(new AIActionLog
                {
                    UserId = UserId, ActionType = AIActionLog.ActionTypeInboxDispatch,
                    Decision = "applied", CreatedAt = now.AddDays(-i)
                });
            db.AIActionLogs.Add(new AIActionLog
            {
                UserId = UserId, ActionType = AIActionLog.ActionTypeBriefingRegenerate,
                Decision = "applied", CreatedAt = now
            });
            db.AIActionLogs.Add(new AIActionLog
            {
                UserId = otherUserId, ActionType = AIActionLog.ActionTypeInboxDispatch,
                Decision = "applied", CreatedAt = now
            });
            await db.SaveChangesAsync();
        }

        var svc = new MiraiStatsService(_fx.CreateContext());
        var stats = await svc.GetAiActionStatsAsync(UserId);

        Assert.Equal(4, stats.Total);
        Assert.Equal(2, stats.ByActionType.Count);
        Assert.Equal(3, stats.ByActionType.Single(x => x.ActionType == "inbox_dispatch").Count);
        Assert.Equal(7, stats.Last7Days.Count); // 零填充 7 天
        Assert.Equal(4, stats.Last7Days.Sum(x => x.Count));
    }
}
