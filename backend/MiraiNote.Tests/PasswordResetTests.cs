using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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

public class PasswordResetTests
{
    [Fact]
    public async Task Reset_RequiresConfirm_ThenOldPasswordCannotLogin()
    {
        using var h = new Harness();
        var token = await h.IssueResetTokenAsync();

        var mismatch = await Assert.ThrowsAsync<BusinessException>(() => h.Auth.ResetPasswordAsync(new ResetPasswordRequest
        {
            Token = token,
            NewPassword = "Newpass1",
            ConfirmPassword = ""
        }));
        Assert.Equal("两次输入的密码不一致", mismatch.Message);
        await h.Auth.LoginAsync(Login("Password1"));

        await h.Auth.ResetPasswordAsync(new ResetPasswordRequest
        {
            Token = token,
            NewPassword = "Newpass1",
            ConfirmPassword = "Newpass1"
        });

        var oldPassword = await Assert.ThrowsAsync<BusinessException>(() => h.Auth.LoginAsync(Login("Password1")));
        Assert.Equal("用户名或密码错误", oldPassword.Message);

        var fresh = await h.Auth.LoginAsync(Login("Newpass1"));
        Assert.Equal("BillUser", fresh.Tokens.User.Username);

        var used = await Assert.ThrowsAsync<BusinessException>(() => h.Auth.ResetPasswordAsync(new ResetPasswordRequest
        {
            Token = token,
            NewPassword = "Newpass2a",
            ConfirmPassword = "Newpass2a"
        }));
        Assert.Equal("链接已使用", used.Message);
    }

    [Fact]
    public async Task Reset_ExpiredToken_SaysSoAndKeepsOldPassword()
    {
        using var h = new Harness();
        var token = await h.IssueResetTokenAsync();
        var row = await h.Db.EmailVerifyTokens.SingleAsync(t => t.Token == token);
        row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await h.Db.SaveChangesAsync();

        var expired = await Assert.ThrowsAsync<BusinessException>(() => h.Auth.ResetPasswordAsync(new ResetPasswordRequest
        {
            Token = token,
            NewPassword = "Newpass1",
            ConfirmPassword = "Newpass1"
        }));
        Assert.Equal("链接已过期", expired.Message);

        var stillOld = await h.Auth.LoginAsync(Login("Password1"));
        Assert.Equal("BillUser", stillOld.Tokens.User.Username);
    }

    [Fact]
    public async Task ForgotPassword_ShortIntervalKeepsOneValidSend()
    {
        using var h = new Harness();
        await h.RegisterVerifiedAsync();

        await h.Auth.ForgotPasswordAsync("bill@example.com");
        await h.Auth.ForgotPasswordAsync("bill@example.com");
        var first = Assert.Single(h.ResetLinks);

        await h.BackdateResetTokensAsync(TimeSpan.FromSeconds(61));
        await h.Auth.ForgotPasswordAsync("BILL@example.com");
        Assert.Equal(2, h.ResetLinks.Count);

        var superseded = await Assert.ThrowsAsync<BusinessException>(() => h.Auth.ResetPasswordAsync(new ResetPasswordRequest
        {
            Token = TokenFromLink(first),
            NewPassword = "Newpass1",
            ConfirmPassword = "Newpass1"
        }));
        Assert.Equal("链接已过期", superseded.Message);

        await h.Auth.ResetPasswordAsync(new ResetPasswordRequest
        {
            Token = TokenFromLink(h.ResetLinks[1]),
            NewPassword = "Newpass1",
            ConfirmPassword = "Newpass1"
        });
        Assert.Equal("BillUser", (await h.Auth.LoginAsync(Login("Newpass1"))).Tokens.User.Username);
    }

    [Fact]
    public async Task ForgotPassword_HourlyCapStillDropsExtraSends()
    {
        using var h = new Harness();
        await h.RegisterVerifiedAsync();

        for (var i = 0; i < 3; i++)
        {
            await h.BackdateResetTokensAsync(TimeSpan.FromSeconds(61));
            await h.Auth.ForgotPasswordAsync("bill@example.com");
        }
        Assert.Equal(3, h.ResetLinks.Count);

        await h.BackdateResetTokensAsync(TimeSpan.FromSeconds(61));
        await h.Auth.ForgotPasswordAsync("bill@example.com");
        Assert.Equal(3, h.ResetLinks.Count);
    }

    private static LoginRequest Login(string password) => new()
    {
        UsernameOrEmail = "billuser",
        Password = password
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
        public List<string> VerifyLinks { get; } = [];
        public List<string> ResetLinks { get; } = [];
        public AuthService Auth { get; }

        public Harness()
        {
            var email = new Mock<IEmailService>();
            email.Setup(e => e.SendVerifyEmailAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, string, CancellationToken>((_, _, link, _) => VerifyLinks.Add(link))
                .Returns(Task.CompletedTask);
            email.Setup(e => e.SendResetPasswordAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, string, CancellationToken>((_, _, link, _) => ResetLinks.Add(link))
                .Returns(Task.CompletedTask);
            email.Setup(e => e.SendPasswordChangedAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
                email.Object,
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
                    RequireEmailVerification = true
                }),
                new ListLogger<AuthService>(),
                new InlineBackgroundWork());
        }

        public async Task RegisterVerifiedAsync()
        {
            await Auth.RegisterAsync(new RegisterRequest
            {
                Username = "BillUser",
                Email = "Bill@Example.COM",
                Password = "Password1",
                ConfirmPassword = "Password1"
            });
            await Auth.VerifyEmailAsync(TokenFromLink(VerifyLinks[0]));
        }

        public async Task<string> IssueResetTokenAsync()
        {
            await RegisterVerifiedAsync();
            await Auth.ForgotPasswordAsync("bill@example.com");
            return TokenFromLink(Assert.Single(ResetLinks));
        }

        public async Task BackdateResetTokensAsync(TimeSpan age)
        {
            var rows = await Db.EmailVerifyTokens
                .Where(t => t.Type == EmailVerifyTokenType.ResetPassword)
                .ToListAsync();
            var createdAt = DateTime.UtcNow - age;
            foreach (var row in rows)
                row.CreatedAt = createdAt;
            await Db.SaveChangesAsync();
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

    private sealed class ListLogger<T> : ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
