using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
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
///    - 频率限制（注册/重置邮件 1 小时 3 封）同样基于 IMemoryCache
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

    // 邮件频率限制：1 小时内最多 3 封
    private const int MaxEmailsPerHour = 3;
    private static readonly TimeSpan EmailRateWindow = TimeSpan.FromHours(1);

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

    public AuthService(
        MiraiNoteDbContext db,
        IJwtTokenService jwt,
        IEmailService email,
        IMemoryCache cache,
        IOptions<JwtOptions> jwtOptions,
        IOptions<AppOptions> appOptions,
        ILogger<AuthService> logger)
    {
        _db = db;
        _jwt = jwt;
        _email = email;
        _cache = cache;
        _jwtOptions = jwtOptions.Value;
        _appOptions = appOptions.Value;
        _logger = logger;
    }

    // ============================================================
    // 注册
    // ============================================================
    public async Task RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        ValidateUsername(request.Username);
        ValidateEmail(request.Email);
        ValidatePassword(request.Password);
        if (request.Password != request.ConfirmPassword)
        {
            throw new BusinessException("两次输入的密码不一致");
        }

        // 唯一性检查 —— 全局过滤器已自动排除软删除
        if (await _db.Users.AnyAsync(u => u.Username == request.Username, ct))
        {
            throw new BusinessException("用户名已被使用");
        }
        if (await _db.Users.AnyAsync(u => u.Email == request.Email, ct))
        {
            throw new BusinessException("邮箱已被注册");
        }

        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            IsAdmin = false,
            // 邮箱验证状态始终从「未验证」开始，由用户主动完成验证后才置 true
            IsEmailVerified = false,
            IsActive = true
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        // 仅在启用邮件验证功能时发送验证邮件
        if (_appOptions.RequireEmailVerification)
        {
            try
            {
                await IssueAndSendVerifyEmailAsync(user, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注册用户 {Username} 后发送验证邮件失败，请检查 SMTP 配置", user.Username);
            }
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

        var lockoutKey = $"login:lockout:{request.UsernameOrEmail.ToLowerInvariant()}";
        if (_cache.TryGetValue<DateTime>(lockoutKey, out var lockedUntil) && lockedUntil > DateTime.UtcNow)
        {
            var remain = (int)Math.Ceiling((lockedUntil - DateTime.UtcNow).TotalMinutes);
            throw new BusinessException($"账户已暂时锁定，请 {remain} 分钟后重试");
        }

        var user = await _db.Users.FirstOrDefaultAsync(
            u => u.Username == request.UsernameOrEmail || u.Email == request.UsernameOrEmail, ct);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            RecordFailedLogin(request.UsernameOrEmail, lockoutKey);
            throw new BusinessException("用户名或密码错误");
        }

        if (!user.IsActive)
        {
            throw new BusinessException("账户已被禁用，请联系管理员");
        }

        // 仅在启用邮件验证功能时拦截未验证邮箱（关闭时允许直接登录）
        if (_appOptions.RequireEmailVerification && !user.IsEmailVerified)
        {
            throw new BusinessException("邮箱尚未验证，请前往邮箱完成验证后再登录");
        }

        // 登录成功：清除失败计数 + 更新 LastLoginAt
        _cache.Remove($"login:fails:{request.UsernameOrEmail.ToLowerInvariant()}");
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
            _logger.LogWarning("账户 {Key} 因连续登录失败已锁定至 {Until}", usernameOrEmail, until);
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
            record.IsRevoked = true;
            await _db.SaveChangesAsync(ct);
        }
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
    public async Task VerifyEmailAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new BusinessException("链接无效");
        }
        var record = await _db.EmailVerifyTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == token && t.Type == EmailVerifyTokenType.VerifyEmail, ct);

        if (record == null || record.User == null)
        {
            throw new BusinessException("链接无效");
        }
        if (record.IsUsed)
        {
            throw new BusinessException("链接已使用");
        }
        if (record.ExpiresAt < DateTime.UtcNow)
        {
            throw new BusinessException("链接已过期");
        }

        record.IsUsed = true;
        record.User.IsEmailVerified = true;
        await _db.SaveChangesAsync(ct);
    }

    public async Task ResendVerifyEmailAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new BusinessException("用户不存在", 404);

        if (user.IsEmailVerified)
        {
            throw new BusinessException("邮箱已验证，无需重复发送");
        }
        await IssueAndSendVerifyEmailAsync(user, ct);
    }

    private async Task IssueAndSendVerifyEmailAsync(User user, CancellationToken ct)
    {
        EnforceEmailRateLimit($"email:verify:{user.Email.ToLowerInvariant()}");

        var token = Guid.NewGuid().ToString("N");
        _db.EmailVerifyTokens.Add(new EmailVerifyToken
        {
            UserId = user.Id,
            Token = token,
            Type = EmailVerifyTokenType.VerifyEmail,
            ExpiresAt = DateTime.UtcNow.Add(VerifyEmailTokenLifetime),
            IsUsed = false
        });
        await _db.SaveChangesAsync(ct);

        var link = $"{_appOptions.FrontendBaseUrl.TrimEnd('/')}/verify-email?token={token}";
        await _email.SendVerifyEmailAsync(user.Email, user.Username, link, ct);
    }

    // ============================================================
    // 忘记密码 / 重置密码
    // ============================================================
    public async Task ForgotPasswordAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return; // 统一成功响应，不暴露信息

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user == null || !user.IsActive)
        {
            return; // 防止枚举：邮箱不存在也返回成功
        }

        // 频率限制
        try
        {
            EnforceEmailRateLimit($"email:reset:{email.ToLowerInvariant()}");
        }
        catch (BusinessException)
        {
            return; // 静默：仍然对外返回成功
        }

        var token = Guid.NewGuid().ToString("N");
        _db.EmailVerifyTokens.Add(new EmailVerifyToken
        {
            UserId = user.Id,
            Token = token,
            Type = EmailVerifyTokenType.ResetPassword,
            ExpiresAt = DateTime.UtcNow.Add(ResetPasswordTokenLifetime),
            IsUsed = false
        });
        await _db.SaveChangesAsync(ct);

        var link = $"{_appOptions.FrontendBaseUrl.TrimEnd('/')}/reset-password?token={token}";
        await _email.SendResetPasswordAsync(user.Email, user.Username, link, ct);
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
        if (record.ExpiresAt < DateTime.UtcNow)
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

    private void EnforceEmailRateLimit(string key)
    {
        var count = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = EmailRateWindow;
            return 0;
        });
        if (count >= MaxEmailsPerHour)
        {
            throw new BusinessException("发送过于频繁，请稍后再试");
        }
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
