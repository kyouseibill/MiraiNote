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
    public async Task Register_LinkIsAbsoluteAndUsernameKeepsDisplayCase()
    {
        using var h = new Harness();
        var message = await h.Auth.RegisterAsync(Bill());

        Assert.Equal("注册成功，请查收验证邮件", message);
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

        var expired = await Assert.ThrowsAsync<BusinessException>(() => h.Auth.VerifyEmailAsync(token));
        Assert.Equal("链接已过期", expired.Message);

        var usedRow = await h.Db.EmailVerifyTokens.SingleAsync(t => t.Token == token);
        usedRow.ExpiresAt = DateTime.UtcNow.AddHours(1);
        usedRow.IsUsed = true;
        await h.Db.SaveChangesAsync();

        var used = await Assert.ThrowsAsync<BusinessException>(() => h.Auth.VerifyEmailAsync(token));
        Assert.Equal("链接已使用", used.Message);

        var invalid = await Assert.ThrowsAsync<BusinessException>(() => h.Auth.VerifyEmailAsync("not-a-token"));
        Assert.Equal("链接无效", invalid.Message);
    }

    [Fact]
    public async Task Verify_OneTimeLink_ThenLoginIgnoresCase()
    {
        using var h = new Harness();
        await h.Auth.RegisterAsync(Bill());
        await h.Auth.VerifyEmailAsync(TokenFromLink(h.Links[0]));

        var again = await Assert.ThrowsAsync<BusinessException>(() => h.Auth.VerifyEmailAsync(TokenFromLink(h.Links[0])));
        Assert.Equal("链接已使用", again.Message);

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
        using var h = new Harness();
        await h.Auth.ResendVerifyEmailAsync("missing@example.com");
        h.Email.Verify(e => e.SendVerifyEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        await h.Auth.RegisterAsync(Bill());
        var tooSoon = await Assert.ThrowsAsync<BusinessException>(() => h.Auth.ResendVerifyEmailAsync("bill@example.com"));
        Assert.Equal("发送过于频繁，请 60 秒后再试", tooSoon.Message);

        var limitedUser = await h.Db.Users.SingleAsync(u => u.Email == "bill@example.com");
        var existing = await h.Db.EmailVerifyTokens.Where(t => t.UserId == limitedUser.Id).ToListAsync();
        foreach (var row in existing)
            row.CreatedAt = DateTime.UtcNow.AddMinutes(-2);
        for (var i = existing.Count; i < 5; i++)
        {
            h.Db.EmailVerifyTokens.Add(new EmailVerifyToken
            {
                UserId = limitedUser.Id,
                Token = Guid.NewGuid().ToString("N"),
                Type = EmailVerifyTokenType.VerifyEmail,
                ExpiresAt = DateTime.UtcNow.AddHours(24),
                IsUsed = false,
                CreatedAt = DateTime.UtcNow.AddMinutes(-2)
            });
        }
        await h.Db.SaveChangesAsync();

        var daily = await Assert.ThrowsAsync<BusinessException>(() => h.Auth.ResendVerifyEmailAsync("BILL@example.com"));
        Assert.Equal("今日验证邮件已达 5 次上限，请明天再试", daily.Message);
    }

    [Fact]
    public async Task Register_SendFailure_KeepsAccountAndHidesSmtpSecret()
    {
        using var h = new Harness();
        h.Email.Setup(e => e.SendVerifyEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp password=SuperSecretAuthCode"));

        var message = await h.Auth.RegisterAsync(new RegisterRequest
        {
            Username = "MailFail",
            Email = "fail@example.com",
            Password = "Password1",
            ConfirmPassword = "Password1"
        });

        Assert.Equal("验证邮件发送失败，请稍后重发", message);
        Assert.True(await h.Db.Users.AnyAsync(u => u.Email == "fail@example.com"));
        Assert.DoesNotContain(h.Logs, line => line.Contains("SuperSecretAuthCode", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RequireEmailVerification_Off_DoesNotSendOrBlock()
    {
        using var h = new Harness(requireVerification: false);
        var message = await h.Auth.RegisterAsync(new RegisterRequest
        {
            Username = "OpenUser",
            Email = "open@example.com",
            Password = "Password1",
            ConfirmPassword = "Password1"
        });

        Assert.Equal("注册成功，请查收验证邮件", message);
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
        ConfirmPassword = "Password1"
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
                new ListLogger<AuthService>(Logs));
        }

        public void Dispose()
        {
            Db.Dispose();
            Fx.Dispose();
        }
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
