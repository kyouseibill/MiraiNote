using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiraiNote.Data;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Auth;

namespace MiraiNote.Core.Services;

/// <summary>
/// 认证业务实现。
/// 设计要点：
/// 1. 输入校验：在 Service 内完成（用户名/邮箱/密码格式），保证业务规则集中可测。
/// 2. 错误处理：通过抛 <see cref="BusinessException"/>，由全局异常中间件统一转 API 响应。
/// 3. 安全：
///    - 密码 BCrypt 哈希
///    - 登录连续失败 5 次，账户锁定 15 分钟（IMemoryCache，进程级，重启重置 —— 可接受）
///    - 重置邮件：同一账户加锁后才检查 60 秒冷却。行锁、冷却检查和写入新 token 放在执行策略里的用户事务中，兼容 Npgsql EnableRetryOnFailure。邮件发送成功后才作废旧链接；1 小时内最多 3 封（IMemoryCache）。超限时不发信也不单独报错
///    - 验证邮件重发次数记在 EmailVerifyToken 表，超限时不发信也不单独报错
///    - 忘记密码无论邮箱是否存在均返回成功，防止账户枚举
///    - RefreshToken 入库只存 SHA-256 哈希，原文随响应返回，由 Controller 写入 HttpOnly Cookie
/// </summary>
public class AuthService : IAuthService
{
    private static readonly Regex UsernameRegex = new("^[A-Za-z0-9]([A-Za-z0-9]|[._][A-Za-z0-9])*$", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    // 登录锁定参数
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    // 重置邮件：60 秒内同一账户只发一封（数据库，与设置页按钮冷却一致）；1 小时内最多 3 封（内存）。
    // 验证邮件重发见数据库计数。
    private const int MaxEmailsPerHour = 3;
    private static readonly TimeSpan EmailRateWindow = TimeSpan.FromHours(1);
    private const int MaxVerifyEmailsPerDay = 5;
    private static readonly TimeSpan VerifyEmailMinInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ResetPasswordMinInterval = VerifyEmailMinInterval;

    // Token 有效期
    private static readonly TimeSpan VerifyEmailTokenLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan ResetPasswordTokenLifetime = TimeSpan.FromHours(1);

    private readonly MiraiNoteDbContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly IEmailService _email;
    private readonly IMemoryCache _cache;
    private readonly JwtOptions _jwtOptions;
    private readonly AppOptions _appOptions;
    private readonly ILogger<AuthService> _logger;
    private readonly IBackgroundWork _background;

    /// <summary>同一账户的重置邮件串行发送，避免并发请求都通过 60 秒检查。</summary>
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> ResetPasswordSendLocks = new();

    /// <summary>测试可替换，生产使用 UTC 现在。</summary>
    internal Func<DateTime> UtcNowProvider { get; set; } = static () => DateTime.UtcNow;

    public AuthService(
        MiraiNoteDbContext db,
        IJwtTokenService jwt,
        IEmailService email,
        IMemoryCache cache,
        IOptions<JwtOptions> jwtOptions,
        IOptions<AppOptions> appOptions,
        ILogger<AuthService> logger,
        IBackgroundWork background)
    {
        _db = db;
        _jwt = jwt;
        _email = email;
        _cache = cache;
        _jwtOptions = jwtOptions.Value;
        _appOptions = appOptions.Value;
        _logger = logger;
        _background = background;
    }

    // ============================================================
    // 注册
    // ============================================================
    public async Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var username = AccountNormalizer.DisplayUsername(request.Username);
        var normalizedUsername = AccountNormalizer.NormalizeUsername(request.Username);
        var email = AccountNormalizer.NormalizeEmail(request.Email);

        ValidateUsername(username);
        ValidateEmail(email);
        ValidatePassword(request.Password);
        if (request.Password != request.ConfirmPassword)
        {
            throw new BusinessException("两次输入的密码不一致");
        }

        if (await _db.Users.AnyAsync(u => u.NormalizedUserName == normalizedUsername, ct))
        {
            throw new BusinessException("用户名已被使用");
        }
        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
        {
            throw new BusinessException("邮箱已被注册");
        }

        var user = new User
        {
            Username = username,
            NormalizedUserName = normalizedUsername,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            IsAdmin = false,
            // 关闭验证开关时也不自动标成已验证，与现有注册行为一致
            IsEmailVerified = false,
            IsActive = true
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        if (!_appOptions.RequireEmailVerification)
        {
            return new RegisterResult { Outcome = RegisterOutcomes.VerificationDisabled };
        }

        try
        {
            var link = await IssueVerifyLinkAsync(user, ct);
            await _email.SendVerifyEmailAsync(user.Email, user.Username, link, ct);
            return new RegisterResult { Outcome = RegisterOutcomes.VerificationEmailSent };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "注册后发送验证邮件失败，用户 {UserId}，类型 {ExceptionType}",
                user.Id,
                ex.GetType().Name);
            return new RegisterResult { Outcome = RegisterOutcomes.VerificationEmailFailed };
        }
    }

    // ============================================================
    // 登录
    // ============================================================
    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.UsernameOrEmail) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new BusinessException("请输入用户名和密码");
        }

        var loginKey = request.UsernameOrEmail.Trim().ToLowerInvariant();
        var lockoutKey = $"login:lockout:{loginKey}";
        if (_cache.TryGetValue<DateTime>(lockoutKey, out var lockedUntil) && lockedUntil > DateTime.UtcNow)
        {
            var remain = (int)Math.Ceiling((lockedUntil - DateTime.UtcNow).TotalMinutes);
            throw new BusinessException($"账户已暂时锁定，请 {remain} 分钟后重试");
        }

        var user = await _db.Users.FirstOrDefaultAsync(
            u => u.NormalizedUserName == loginKey || u.Email == loginKey, ct);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            RecordFailedLogin(loginKey, lockoutKey);
            throw new BusinessException("用户名或密码错误");
        }

        if (!user.IsActive)
        {
            throw new BusinessException("账户已被禁用，请联系管理员");
        }

        // 仅在启用邮件验证功能时拦截未验证邮箱（关闭时允许直接登录）
        if (_appOptions.RequireEmailVerification && !user.IsEmailVerified)
        {
            throw new BusinessException("请先验证邮箱");
        }

        // 登录成功：清除失败计数 + 更新 LastLoginAt
        _cache.Remove($"login:fails:{loginKey}");
        _cache.Remove(lockoutKey);
        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return await IssueTokensAsync(user, request.RememberMe, ct);
    }

    private void RecordFailedLogin(string usernameOrEmail, string lockoutKey)
    {
        var failsKey = $"login:fails:{usernameOrEmail.ToLowerInvariant()}";
        var attempts = _cache.GetOrCreate(failsKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = LockoutDuration;
            return 0;
        });
        attempts++;
        _cache.Set(failsKey, attempts, LockoutDuration);

        if (attempts >= MaxFailedAttempts)
        {
            var until = DateTime.UtcNow.Add(LockoutDuration);
            _cache.Set(lockoutKey, until, LockoutDuration);
            _logger.LogWarning("账户因连续登录失败已锁定至 {Until}", until);
        }
    }

    // ============================================================
    // 登出
    // ============================================================
    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return; // 静默成功，幂等
        }
        var hash = _jwt.HashRefreshToken(refreshToken);
        var record = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (record != null)
        {
            // 吊销该用户全部 refresh，避免同账号其他会话仍可静默续期
            await RevokeAllRefreshTokensAsync(record.UserId, ct);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task LogoutAllAsync(int userId, CancellationToken ct = default)
    {
        if (userId <= 0)
        {
            return;
        }
        await RevokeAllRefreshTokensAsync(userId, ct);
        await _db.SaveChangesAsync(ct);
    }

    // ============================================================
    // 刷新 Token
    // ============================================================
    public async Task<LoginResult> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new BusinessException("缺少刷新凭证", 401);
        }

        var hash = _jwt.HashRefreshToken(refreshToken);
        var record = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (record == null || record.IsRevoked || record.ExpiresAt < DateTime.UtcNow || record.User == null)
        {
            throw new BusinessException("刷新凭证无效或已过期", 401);
        }
        if (!record.User.IsActive)
        {
            throw new BusinessException("账户已被禁用", 401);
        }

        // 旋转 RefreshToken：吊销旧的，签发新的（继承剩余有效期长度的"是否记住我"无从得知，
        // 这里保持与旧 Token 相同的剩余有效期，避免无限续期）
        var remainingDays = Math.Max(1, (int)Math.Ceiling((record.ExpiresAt - DateTime.UtcNow).TotalDays));
        var rememberMe = remainingDays > _jwtOptions.RefreshTokenExpiryDays;

        record.IsRevoked = true;
        await _db.SaveChangesAsync(ct);

        return await IssueTokensAsync(record.User, rememberMe, ct);
    }

    // ============================================================
    // 邮箱验证
    // ============================================================
    public async Task<VerifyEmailResult> VerifyEmailAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new VerifyEmailResult { Status = VerifyEmailStatuses.Invalid };
        }

        var record = await _db.EmailVerifyTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == token && t.Type == EmailVerifyTokenType.VerifyEmail, ct);

        if (record?.User == null)
        {
            return new VerifyEmailResult { Status = VerifyEmailStatuses.Invalid };
        }

        if (record.IsUsed || record.User.IsEmailVerified)
        {
            return new VerifyEmailResult { Status = VerifyEmailStatuses.AlreadyVerified };
        }

        if (record.ExpiresAt < UtcNow())
        {
            return new VerifyEmailResult { Status = VerifyEmailStatuses.Expired };
        }

        record.IsUsed = true;
        record.User.IsEmailVerified = true;
        await _db.SaveChangesAsync(ct);
        return new VerifyEmailResult { Status = VerifyEmailStatuses.Verified };
    }

    public async Task ResendVerifyEmailAsync(string email, CancellationToken ct = default)
    {
        var normalized = AccountNormalizer.NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
        if (user == null || user.IsEmailVerified || !user.IsActive)
        {
            return;
        }

        await TryQueueVerifyEmailAsync(user, ct);
    }

    public async Task ResendVerifyByTokenAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var record = await _db.EmailVerifyTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == token && t.Type == EmailVerifyTokenType.VerifyEmail, ct);
        if (record?.User == null || record.User.IsEmailVerified || !record.User.IsActive)
        {
            return;
        }

        await TryQueueVerifyEmailAsync(record.User, ct);
    }

    private async Task TryQueueVerifyEmailAsync(User user, CancellationToken ct)
    {
        if (!await CanSendVerifyEmailAsync(user.Id, ct))
        {
            return;
        }

        var link = await IssueVerifyLinkAsync(user, ct);
        var email = user.Email;
        var username = user.Username;
        var userId = user.Id;
        _background.Run(async () =>
        {
            try
            {
                await _email.SendVerifyEmailAsync(email, username, link, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "重发验证邮件失败，用户 {UserId}，类型 {ExceptionType}",
                    userId,
                    ex.GetType().Name);
            }
        });
    }

    private async Task<string> IssueVerifyLinkAsync(User user, CancellationToken ct)
    {
        var now = UtcNow();
        var previous = await _db.EmailVerifyTokens
            .Where(t => t.UserId == user.Id
                && t.Type == EmailVerifyTokenType.VerifyEmail
                && !t.IsUsed
                && t.ExpiresAt > now)
            .ToListAsync(ct);
        // 新链接发出后，旧的未使用链接立刻过期。验证页按 expired 给一键重发，不标成已使用。
        foreach (var old in previous)
            old.ExpiresAt = now.AddSeconds(-1);

        var token = Guid.NewGuid().ToString("N");
        _db.EmailVerifyTokens.Add(new EmailVerifyToken
        {
            UserId = user.Id,
            Token = token,
            Type = EmailVerifyTokenType.VerifyEmail,
            ExpiresAt = now.Add(VerifyEmailTokenLifetime),
            IsUsed = false
        });
        await _db.SaveChangesAsync(ct);
        return BuildAbsoluteLink($"/verify-email?token={token}");
    }

    private async Task<bool> CanSendVerifyEmailAsync(int userId, CancellationToken ct)
    {
        var now = UtcNow();
        var sinceMinute = now - VerifyEmailMinInterval;
        var (dayStart, _) = ShanghaiClock.DayRangeUtc(ShanghaiClock.Today(new DateTimeOffset(now, TimeSpan.Zero)));

        var sentRecently = await _db.EmailVerifyTokens.AnyAsync(t =>
            t.UserId == userId
            && t.Type == EmailVerifyTokenType.VerifyEmail
            && t.CreatedAt >= sinceMinute, ct);
        if (sentRecently)
        {
            return false;
        }

        var sentToday = await _db.EmailVerifyTokens.CountAsync(t =>
            t.UserId == userId
            && t.Type == EmailVerifyTokenType.VerifyEmail
            && t.CreatedAt >= dayStart, ct);
        return sentToday < MaxVerifyEmailsPerDay;
    }

    private DateTime UtcNow() => DateTime.SpecifyKind(UtcNowProvider(), DateTimeKind.Utc);

    private string BuildAbsoluteLink(string pathAndQuery) =>
        AppLinks.Absolute(_appOptions, pathAndQuery);

    // ============================================================
    // 忘记密码 / 重置密码
    // ============================================================
    public async Task ForgotPasswordAsync(string email, CancellationToken ct = default)
    {
        var normalizedEmail = AccountNormalizer.NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(normalizedEmail)) return; // 统一成功响应，不暴露信息

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);
        if (user == null || !user.IsActive)
        {
            return; // 防止枚举：邮箱不存在也返回成功
        }

        var gate = ResetPasswordSendLock(user.Id);
        await gate.WaitAsync(ct);
        try
        {
            var now = UtcNow();
            var rateKey = $"email:reset:{normalizedEmail}";
            // EnableRetryOnFailure 下，手动事务必须整段放进执行策略，否则后续 SQL / SaveChanges
            // 会因 NpgsqlRetryingExecutionStrategy 拒绝用户事务而失败。
            // 策略可能重试委托：每次开始前清空跟踪，避免上一次未提交的 token 再被插入。
            var strategy = _db.Database.CreateExecutionStrategy();
            var created = await strategy.ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                await using var tx = await _db.Database.BeginTransactionAsync(ct);

                // 锁住账户行，让其他进程的并发请求排队后再看 60 秒窗口。
                var locked = await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"""UPDATE "User" SET "UpdatedAt" = "UpdatedAt" WHERE "Id" = {user.Id}""", ct);
                if (locked == 0)
                    return null;

                if (await HasRecentResetPasswordAsync(user.Id, now, ct) || !CanSendAnotherResetEmail(rateKey))
                    return null;

                var token = new EmailVerifyToken
                {
                    UserId = user.Id,
                    Token = Guid.NewGuid().ToString("N"),
                    Type = EmailVerifyTokenType.ResetPassword,
                    ExpiresAt = now.Add(ResetPasswordTokenLifetime),
                    IsUsed = false
                };
                _db.EmailVerifyTokens.Add(token);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return token;
            });
            if (created == null)
                return;

            var link = BuildAbsoluteLink($"/reset-password?token={created.Token}");
            try
            {
                await _email.SendResetPasswordAsync(user.Email, user.Username, link, ct);
            }
            catch (Exception ex)
            {
                await DiscardUnsentResetTokenAsync(created, ct);
                _logger.LogError(
                    "重置邮件发送失败，未作废已有链接，用户 {UserId}，类型 {ExceptionType}",
                    user.Id,
                    ex.GetType().Name);
                throw;
            }

            RecordResetEmailSent(rateKey);
            await ExpireOtherUnusedResetTokensAsync(user.Id, created.Id, now, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private static SemaphoreSlim ResetPasswordSendLock(int userId) =>
        ResetPasswordSendLocks.GetOrAdd(userId, static _ => new SemaphoreSlim(1, 1));

    private Task<bool> HasRecentResetPasswordAsync(int userId, DateTime now, CancellationToken ct) =>
        _db.EmailVerifyTokens.AnyAsync(t =>
            t.UserId == userId
            && t.Type == EmailVerifyTokenType.ResetPassword
            && t.CreatedAt >= now - ResetPasswordMinInterval, ct);

    private async Task DiscardUnsentResetTokenAsync(EmailVerifyToken created, CancellationToken ct)
    {
        var tracked = await _db.EmailVerifyTokens.FirstOrDefaultAsync(t => t.Id == created.Id, ct);
        if (tracked == null)
            return;
        _db.EmailVerifyTokens.Remove(tracked);
        await _db.SaveChangesAsync(ct);
    }

    private async Task ExpireOtherUnusedResetTokensAsync(int userId, int keepId, DateTime now, CancellationToken ct)
    {
        var previous = await _db.EmailVerifyTokens
            .Where(t => t.UserId == userId
                && t.Id != keepId
                && t.Type == EmailVerifyTokenType.ResetPassword
                && !t.IsUsed
                && t.ExpiresAt > now)
            .ToListAsync(ct);
        if (previous.Count == 0)
            return;

        foreach (var old in previous)
            old.ExpiresAt = now.AddSeconds(-1);
        await _db.SaveChangesAsync(ct);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        ValidatePassword(request.NewPassword);
        if (request.NewPassword != request.ConfirmPassword)
        {
            throw new BusinessException("两次输入的密码不一致");
        }

        var record = await _db.EmailVerifyTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == request.Token && t.Type == EmailVerifyTokenType.ResetPassword, ct);

        if (record == null || record.User == null)
        {
            throw new BusinessException("链接无效");
        }
        if (record.IsUsed)
        {
            throw new BusinessException("链接已使用");
        }
        if (record.ExpiresAt < UtcNow())
        {
            throw new BusinessException("链接已过期");
        }

        record.IsUsed = true;
        record.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await RevokeAllRefreshTokensAsync(record.User.Id, ct);
        await _db.SaveChangesAsync(ct);

        await _email.SendPasswordChangedAsync(record.User.Email, record.User.Username, ct);
    }

    // ============================================================
    // 修改密码
    // ============================================================
    public async Task ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        ValidatePassword(request.NewPassword);
        if (request.NewPassword != request.ConfirmPassword)
        {
            throw new BusinessException("两次输入的密码不一致");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new BusinessException("用户不存在", 404);

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw new BusinessException("当前密码不正确");
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await RevokeAllRefreshTokensAsync(user.Id, ct);
        await _db.SaveChangesAsync(ct);

        await _email.SendPasswordChangedAsync(user.Email, user.Username, ct);
    }

    // ============================================================
    // 内部工具方法
    // ============================================================

    private async Task<LoginResult> IssueTokensAsync(User user, bool rememberMe, CancellationToken ct)
    {
        var (accessToken, accessExpires) = _jwt.GenerateAccessToken(user);

        var refreshRaw = _jwt.GenerateRefreshToken();
        var refreshDays = rememberMe ? _jwtOptions.RefreshTokenExpiryDaysRememberMe : _jwtOptions.RefreshTokenExpiryDays;
        var refreshExpires = DateTime.UtcNow.AddDays(refreshDays);

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _jwt.HashRefreshToken(refreshRaw),
            ExpiresAt = refreshExpires,
            IsRevoked = false
        });
        await _db.SaveChangesAsync(ct);

        return new LoginResult
        {
            Tokens = new AuthTokenResponse
            {
                AccessToken = accessToken,
                AccessTokenExpiresAt = accessExpires,
                User = new UserInfoDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Email = user.Email,
                    IsAdmin = user.IsAdmin,
                    IsEmailVerified = user.IsEmailVerified,
                    IsActive = user.IsActive,
                    LastLoginAt = user.LastLoginAt
                }
            },
            RefreshToken = refreshRaw,
            RefreshTokenExpiresAt = refreshExpires
        };
    }

    private async Task RevokeAllRefreshTokensAsync(int userId, CancellationToken ct)
    {
        var tokens = await _db.RefreshTokens.Where(t => t.UserId == userId && !t.IsRevoked).ToListAsync(ct);
        foreach (var t in tokens) t.IsRevoked = true;
    }

    private bool CanSendAnotherResetEmail(string key)
    {
        var count = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = EmailRateWindow;
            return 0;
        });
        return count < MaxEmailsPerHour;
    }

    private void RecordResetEmailSent(string key)
    {
        var count = _cache.TryGetValue(key, out int current) ? current : 0;
        _cache.Set(key, count + 1, EmailRateWindow);
    }

    private static void ValidateUsername(string username)
    {
        var len = (username ?? string.Empty).Length;
        if (len < 3 || len > 30)
        {
            throw new BusinessException("用户名长度为 3–30 个字符");
        }
        if (!UsernameRegex.IsMatch(username!))
        {
            throw new BusinessException("用户名只能含字母、数字、下划线或点，不可连续使用特殊字符，且不能以特殊字符结尾");
        }
    }

    private static void ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !EmailRegex.IsMatch(email))
        {
            throw new BusinessException("邮箱格式不正确");
        }
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8 || password.Length > 32)
        {
            throw new BusinessException("密码长度需为 8~32 位");
        }
        var hasLetter = password.Any(char.IsLetter);
        var hasDigit = password.Any(char.IsDigit);
        if (!hasLetter || !hasDigit)
        {
            throw new BusinessException("密码必须同时包含字母和数字");
        }
    }
}
