using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiraiNote.Core.Services;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Auth;
using Moq;
using Xunit;

namespace MiraiNote.Tests;

public class EmailVerificationAcceptanceTests
{
    [Fact]
    public async Task Register_WithoutRegion_IsRejectedAndCreatesNoUser()
    {
        using var h = new Harness(requireVerification: false);
        var request = Bill();
        request.Place = "  ";

        var ex = await Assert.ThrowsAsync<BusinessException>(() => h.Auth.RegisterAsync(request));

        Assert.Equal("请选择所在地区", ex.Message);
        Assert.False(await h.Db.Users.AnyAsync(u => u.Email == "bill@example.com"));
    }

    [Fact]
    public async Task Register_StoresCanonicalRegion_AndRejectsDistrict()
    {
        using var h = new Harness(requireVerification: false);
        var request = Bill();
        request.Place = "中国-上海";
        await h.Auth.RegisterAsync(request);

        var place = await h.Db.Users.Where(u => u.Email == "bill@example.com").Select(u => u.WeatherPlace).SingleAsync();
        Assert.Equal("中国 · 上海", place);

        request.Username = "OtherUser";
        request.Email = "other@example.com";
        request.Place = "中国 · 徐汇区";
        var ex = await Assert.ThrowsAsync<BusinessException>(() => h.Auth.RegisterAsync(request));
        Assert.Equal("请选择城市", ex.Message);
        Assert.DoesNotContain("徐汇", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_LinkIsAbsoluteAndUsernameKeepsDisplayCase()
    {
        using var h = new Harness();
        var result = await h.Auth.RegisterAsync(Bill());

        Assert.Equal(RegisterOutcomes.VerificationEmailSent, result.Outcome);
        Assert.Equal("注册成功，请查收验证邮件。没收到的话，请看一下垃圾邮件箱。", AuthMessages.RegisterMessage(result.Outcome));
        var link = Assert.Single(h.Links);
        Assert.StartsWith("https://notes.example.com/verify-email?token=", link);

        var user = await h.Db.Users.SingleAsync(u => u.NormalizedUserName == "billuser");
        Assert.Equal("BillUser", user.Username);
        Assert.Equal("billuser", user.NormalizedUserName);
        Assert.Equal("bill@example.com", user.Email);
        Assert.False(user.IsEmailVerified);
    }

    [Fact]
    public async Task Login_Unverified_IsBlockedWithExactMessage()
    {
        using var h = new Harness();
        await h.Auth.RegisterAsync(Bill());

        var ex = await Assert.ThrowsAsync<BusinessException>(() => h.Auth.LoginAsync(new LoginRequest
        {
            UsernameOrEmail = "billuser",
            Password = "Password1"
        }));
        Assert.Equal("请先验证邮箱", ex.Message);
    }

    [Fact]
    public async Task Verify_ExpiredOrUsedLink_ExplainsWhy()
    {
        using var h = new Harness();
        await h.Auth.RegisterAsync(Bill());
        var token = TokenFromLink(h.Links[0]);

        var expiredRow = await h.Db.EmailVerifyTokens.SingleAsync(t => t.Token == token);
        expiredRow.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await h.Db.SaveChangesAsync();

        var expired = await h.Auth.VerifyEmailAsync(token);
        Assert.Equal(VerifyEmailStatuses.Expired, expired.Status);
        Assert.Equal("链接已过期", AuthMessages.VerifyMessage(expired.Status));

        var usedRow = await h.Db.EmailVerifyTokens.SingleAsync(t => t.Token == token);
        usedRow.ExpiresAt = DateTime.UtcNow.AddHours(1);
        usedRow.IsUsed = true;
        await h.Db.SaveChangesAsync();

        var used = await h.Auth.VerifyEmailAsync(token);
        Assert.Equal(VerifyEmailStatuses.AlreadyVerified, used.Status);
        Assert.Equal("邮箱已验证，请直接登录", AuthMessages.VerifyMessage(used.Status));

        var invalid = await h.Auth.VerifyEmailAsync("not-a-token");
        Assert.Equal(VerifyEmailStatuses.Invalid, invalid.Status);
        Assert.Equal("链接无效", AuthMessages.VerifyMessage(invalid.Status));
        Assert.Equal(VerifyEmailStatuses.Invalid, (await h.Auth.VerifyEmailAsync("")).Status);
    }

    [Fact]
    public async Task Verify_OneTimeLink_ThenLoginIgnoresCase()
    {
        using var h = new Harness();
        await h.Auth.RegisterAsync(Bill());
        var verified = await h.Auth.VerifyEmailAsync(TokenFromLink(h.Links[0]));
        Assert.Equal(VerifyEmailStatuses.Verified, verified.Status);

        var again = await h.Auth.VerifyEmailAsync(TokenFromLink(h.Links[0]));
        Assert.Equal(VerifyEmailStatuses.AlreadyVerified, again.Status);
        Assert.Equal("邮箱已验证，请直接登录", AuthMessages.VerifyMessage(again.Status));

        var byName = await h.Auth.LoginAsync(new LoginRequest
        {
            UsernameOrEmail = "BILLUSER",
            Password = "Password1"
        });
        Assert.Equal("BillUser", byName.Tokens.User.Username);

        var byEmail = await h.Auth.LoginAsync(new LoginRequest
        {
            UsernameOrEmail = "BILL@example.com",
            Password = "Password1"
        });
        Assert.Equal("bill@example.com", byEmail.Tokens.User.Email);
    }

    [Fact]
    public async Task Resend_RateLimitIsStoredAndUnknownEmailLooksTheSame()
    {
        const string expected =
            "如果这个邮箱已注册但还没验证，几分钟内会收到验证邮件。没收到的话，请看一下垃圾邮件箱。";
        Assert.Equal(expected, AuthMessages.ResendVerify);

        using var h = new Harness();
        await h.Auth.ResendVerifyEmailAsync("missing@example.com");
        await h.Auth.ResendVerifyByTokenAsync("missing-token");
        h.Email.Verify(e => e.SendVerifyEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        await h.Auth.RegisterAsync(Bill());
        var sentAtRegister = h.Links.Count;
        await h.Auth.ResendVerifyEmailAsync("bill@example.com");
        Assert.Equal(sentAtRegister, h.Links.Count);

        var shanghaiMorning = new DateTime(2026, 10, 6, 23, 59, 0, DateTimeKind.Utc);
        var shanghaiLater = new DateTime(2026, 10, 7, 0, 1, 0, DateTimeKind.Utc);
        var limitedUser = await h.Db.Users.SingleAsync(u => u.Email == "bill@example.com");
        var existing = await h.Db.EmailVerifyTokens.Where(t => t.UserId == limitedUser.Id).ToListAsync();
        foreach (var row in existing)
            row.CreatedAt = new DateTime(2026, 10, 6, 16, 0, 0, DateTimeKind.Utc);
        for (var i = existing.Count; i < 5; i++)
        {
            h.Db.EmailVerifyTokens.Add(new EmailVerifyToken
            {
                UserId = limitedUser.Id,
                Token = Guid.NewGuid().ToString("N"),
                Type = EmailVerifyTokenType.VerifyEmail,
                ExpiresAt = shanghaiLater.AddHours(24),
                IsUsed = false,
                CreatedAt = new DateTime(2026, 10, 6, 16, 0, 0, DateTimeKind.Utc)
            });
        }
        await h.Db.SaveChangesAsync();

        h.Auth.UtcNowProvider = () => shanghaiMorning;
        await h.Auth.ResendVerifyEmailAsync("BILL@example.com");
        h.Auth.UtcNowProvider = () => shanghaiLater;
        await h.Auth.ResendVerifyEmailAsync("bill@example.com");
        Assert.Equal(sentAtRegister, h.Links.Count);

        var counted = await h.Db.EmailVerifyTokens.Where(t => t.UserId == limitedUser.Id).ToListAsync();
        foreach (var row in counted)
            row.CreatedAt = new DateTime(2026, 10, 6, 15, 59, 0, DateTimeKind.Utc);
        await h.Db.SaveChangesAsync();

        await h.Auth.ResendVerifyEmailAsync("bill@example.com");
        Assert.Equal(sentAtRegister + 1, h.Links.Count);
    }

    [Fact]
    public async Task ResendByToken_ExpiredUnverifiedSends_VerifiedDoesNot()
    {
        const string expected =
            "如果这个邮箱已注册但还没验证，几分钟内会收到验证邮件。没收到的话，请看一下垃圾邮件箱。";
        Assert.Equal(expected, AuthMessages.ResendVerify);

        using var h = new Harness();
        await h.Auth.RegisterAsync(Bill());
        var token = TokenFromLink(h.Links[0]);
        var row = await h.Db.EmailVerifyTokens.SingleAsync(t => t.Token == token);
        row.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);
        row.CreatedAt = DateTime.UtcNow.AddMinutes(-5);
        await h.Db.SaveChangesAsync();

        Assert.Equal(VerifyEmailStatuses.Expired, (await h.Auth.VerifyEmailAsync(token)).Status);
        await h.Auth.ResendVerifyByTokenAsync(token);
        Assert.Equal(2, h.Links.Count);

        await h.Auth.VerifyEmailAsync(TokenFromLink(h.Links[1]));
        var before = h.Links.Count;
        await h.Auth.ResendVerifyByTokenAsync(token);
        await h.Auth.ResendVerifyEmailAsync("bill@example.com");
        Assert.Equal(before, h.Links.Count);
    }

    [Fact]
    public async Task Resend_ExpiresPreviousToken_NewTokenStillVerifies()
    {
        using var h = new Harness();
        await h.Auth.RegisterAsync(Bill());
        var oldToken = TokenFromLink(h.Links[0]);
        var row = await h.Db.EmailVerifyTokens.SingleAsync(t => t.Token == oldToken);
        row.CreatedAt = DateTime.UtcNow.AddMinutes(-2);
        await h.Db.SaveChangesAsync();

        await h.Auth.ResendVerifyEmailAsync("bill@example.com");
        Assert.Equal(2, h.Links.Count);
        var newToken = TokenFromLink(h.Links[1]);

        Assert.Equal(VerifyEmailStatuses.Expired, (await h.Auth.VerifyEmailAsync(oldToken)).Status);
        Assert.Equal(VerifyEmailStatuses.Verified, (await h.Auth.VerifyEmailAsync(newToken)).Status);
    }

    [Fact]
    public async Task ResendBySupersededToken_StillSendsAndKeepsSameCopy()
    {
        using var h = new Harness();
        await h.Auth.RegisterAsync(Bill());
        var oldToken = TokenFromLink(h.Links[0]);
        var row = await h.Db.EmailVerifyTokens.SingleAsync(t => t.Token == oldToken);
        row.CreatedAt = DateTime.UtcNow.AddMinutes(-2);
        await h.Db.SaveChangesAsync();

        await h.Auth.ResendVerifyEmailAsync("bill@example.com");
        var issued = await h.Db.EmailVerifyTokens.Where(t => t.UserId == row.UserId).ToListAsync();
        foreach (var tokenRow in issued)
            tokenRow.CreatedAt = DateTime.UtcNow.AddMinutes(-2);
        await h.Db.SaveChangesAsync();

        var before = h.Links.Count;
        await h.Auth.ResendVerifyByTokenAsync(oldToken);
        Assert.Equal(before + 1, h.Links.Count);
        Assert.Equal(
            "如果这个邮箱已注册但还没验证，几分钟内会收到验证邮件。没收到的话，请看一下垃圾邮件箱。",
            AuthMessages.ResendVerify);
        Assert.Equal(VerifyEmailStatuses.Expired, (await h.Auth.VerifyEmailAsync(oldToken)).Status);
        Assert.Equal(VerifyEmailStatuses.Verified, (await h.Auth.VerifyEmailAsync(TokenFromLink(h.Links[^1]))).Status);
    }

    [Fact]
    public async Task Register_SendFailure_KeepsAccountAndHidesSmtpSecret()
    {
        using var h = new Harness();
        h.Email.Setup(e => e.SendVerifyEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp password=SuperSecretAuthCode"));

        var result = await h.Auth.RegisterAsync(new RegisterRequest
        {
            Username = "MailFail",
            Email = "fail@example.com",
            Password = "Password1",
            ConfirmPassword = "Password1",
            Place = "中国 · 上海"
        });

        Assert.Equal(RegisterOutcomes.VerificationEmailFailed, result.Outcome);
        Assert.Equal("验证邮件发送失败，请稍后重发", AuthMessages.RegisterMessage(result.Outcome));
        Assert.True(await h.Db.Users.AnyAsync(u => u.Email == "fail@example.com"));
        Assert.DoesNotContain(h.Logs, line => line.Contains("SuperSecretAuthCode", StringComparison.Ordinal));

        var row = await h.Db.EmailVerifyTokens.SingleAsync();
        row.CreatedAt = DateTime.UtcNow.AddMinutes(-2);
        await h.Db.SaveChangesAsync();
        h.Logs.Clear();
        await h.Auth.ResendVerifyEmailAsync("fail@example.com");
        Assert.Equal(AuthMessages.ResendVerify, "如果这个邮箱已注册但还没验证，几分钟内会收到验证邮件。没收到的话，请看一下垃圾邮件箱。");
        Assert.DoesNotContain(h.Logs, line => line.Contains("SuperSecretAuthCode", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RequireEmailVerification_Off_DoesNotSendOrBlock()
    {
        using var h = new Harness(requireVerification: false);
        var result = await h.Auth.RegisterAsync(new RegisterRequest
        {
            Username = "OpenUser",
            Email = "open@example.com",
            Password = "Password1",
            ConfirmPassword = "Password1",
            Place = "日本-东京"
        });

        Assert.Equal(RegisterOutcomes.VerificationDisabled, result.Outcome);
        Assert.Equal("注册成功", AuthMessages.RegisterMessage(result.Outcome));
        Assert.DoesNotContain("查收验证邮件", AuthMessages.RegisterMessage(result.Outcome));
        h.Email.Verify(e => e.SendVerifyEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        var openUser = await h.Db.Users.SingleAsync(u => u.Email == "open@example.com");
        Assert.False(openUser.IsEmailVerified);

        var login = await h.Auth.LoginAsync(new LoginRequest
        {
            UsernameOrEmail = "open@example.com",
            Password = "Password1"
        });
        Assert.Equal("OpenUser", login.Tokens.User.Username);
    }

    [Fact]
    public async Task SeedAdmin_IsEmailVerified()
    {
        using var h = new Harness();
        var db = h.Db;
        var users = await db.Users.IgnoreQueryFilters().ToListAsync();
        db.Users.RemoveRange(users);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Seed:AdminPassword"] = "Admin1234" })
            .Build();
        var seeder = new DatabaseSeeder(db, config, new ListLogger<DatabaseSeeder>(h.Logs));
        await seeder.SeedAsync();

        var admin = await db.Users.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("admin", admin.Username);
        Assert.True(admin.IsEmailVerified);
    }

    private static RegisterRequest Bill() => new()
    {
        Username = "BillUser",
        Email = "Bill@Example.COM",
        Password = "Password1",
        ConfirmPassword = "Password1",
        Place = "中国 · 上海"
    };

    private static string TokenFromLink(string link)
    {
        var query = new Uri(link).Query.TrimStart('?');
        return query.Split('&').Select(p => p.Split('=')).First(p => p[0] == "token")[1];
    }

    private sealed class Harness : IDisposable
    {
        public MiraiTestFixture Fx { get; } = new();
        public MiraiNoteDbContext Db { get; }
        public List<string> Links { get; } = [];
        public List<string> Logs { get; } = [];
        public Mock<IEmailService> Email { get; } = new();
        public AuthService Auth { get; }

        public Harness(bool requireVerification = true)
        {
            Email.Setup(e => e.SendVerifyEmailAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, string, CancellationToken>((_, _, link, _) => Links.Add(link))
                .Returns(Task.CompletedTask);

            var jwt = new Mock<IJwtTokenService>();
            jwt.Setup(j => j.GenerateAccessToken(It.IsAny<User>()))
                .Returns(("access-token", DateTime.UtcNow.AddHours(1)));
            jwt.Setup(j => j.GenerateRefreshToken()).Returns("refresh-token");
            jwt.Setup(j => j.HashRefreshToken(It.IsAny<string>())).Returns("refresh-hash");

            Db = Fx.CreateContext();
            Auth = new AuthService(
                Db,
                jwt.Object,
                Email.Object,
                new MemoryCache(new MemoryCacheOptions()),
                Options.Create(new JwtOptions
                {
                    Secret = new string('k', 32),
                    RefreshTokenExpiryDays = 1,
                    RefreshTokenExpiryDaysRememberMe = 30
                }),
                Options.Create(new AppOptions
                {
                    PublicBaseUrl = "https://notes.example.com",
                    FrontendBaseUrl = "http://localhost:5173",
                    RequireEmailVerification = requireVerification
                }),
                new ListLogger<AuthService>(Logs),
                new InlineBackgroundWork());
        }

        public void Dispose()
        {
            Db.Dispose();
            Fx.Dispose();
        }
    }

    private sealed class InlineBackgroundWork : IBackgroundWork
    {
        public void Run(Func<Task> work) => work().GetAwaiter().GetResult();
    }

    private sealed class ListLogger<T>(List<string> sink) : ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            sink.Add(formatter(state, exception) + exception);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
