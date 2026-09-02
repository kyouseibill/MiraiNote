using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Auth;

namespace MiraiNote.Core.Services;

public interface IUserAdminService
{
    Task<PagedResult<UserListItemDto>> GetUsersAsync(UserListQuery query, CancellationToken ct = default);
    Task<UserListItemDto> CreateUserAsync(AdminCreateUserRequest request, CancellationToken ct = default);
    Task UpdateStatusAsync(int userId, bool isActive, CancellationToken ct = default);
}

/// <summary>
/// 管理员后台 — 用户管理服务。
/// 注意：禁用账户时会吊销其所有 RefreshToken（立即踢下线）。
/// 不允许操作 Id = 1 的超级管理员。
/// </summary>
public class UserAdminService : IUserAdminService
{
    private const int SuperAdminId = 1;

    private readonly MiraiNoteDbContext _db;
    private readonly IEmailService _email;
    private readonly AppOptions _appOptions;

    public UserAdminService(MiraiNoteDbContext db, IEmailService email, IOptions<AppOptions> appOptions)
    {
        _db = db;
        _email = email;
        _appOptions = appOptions.Value;
    }

    public async Task<PagedResult<UserListItemDto>> GetUsersAsync(UserListQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var q = _db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim();
            q = q.Where(u => u.Username.Contains(kw) || u.Email.Contains(kw));
        }

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserListItemDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                IsAdmin = u.IsAdmin,
                IsActive = u.IsActive,
                IsEmailVerified = u.IsEmailVerified,
                LastLoginAt = u.LastLoginAt,
                CreatedAt = u.CreatedAt
            })
            .ToListAsync(ct);

        return new PagedResult<UserListItemDto>
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            Items = items
        };
    }

    public async Task<UserListItemDto> CreateUserAsync(AdminCreateUserRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Email))
        {
            throw new BusinessException("用户名和邮箱为必填");
        }
        if (await _db.Users.AnyAsync(u => u.Username == request.Username, ct))
        {
            throw new BusinessException("用户名已被使用");
        }
        if (await _db.Users.AnyAsync(u => u.Email == request.Email, ct))
        {
            throw new BusinessException("邮箱已被注册");
        }

        var initialPassword = string.IsNullOrWhiteSpace(request.InitialPassword)
            ? GenerateRandomPassword(10)
            : request.InitialPassword;

        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(initialPassword),
            IsAdmin = request.IsAdmin,
            IsEmailVerified = true, // 管理员创建默认已验证
            IsActive = true
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        var loginLink = $"{_appOptions.FrontendBaseUrl.TrimEnd('/')}/login";
        await _email.SendAccountCreatedAsync(user.Email, user.Username, initialPassword, loginLink, ct);

        return new UserListItemDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            IsAdmin = user.IsAdmin,
            IsActive = user.IsActive,
            IsEmailVerified = user.IsEmailVerified,
            LastLoginAt = user.LastLoginAt,
            CreatedAt = user.CreatedAt
        };
    }

    public async Task UpdateStatusAsync(int userId, bool isActive, CancellationToken ct = default)
    {
        if (userId == SuperAdminId)
        {
            throw new BusinessException("不可操作超级管理员账户");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new BusinessException("用户不存在", 404);

        user.IsActive = isActive;

        // 禁用时吊销所有 RefreshToken（立即踢下线）
        if (!isActive)
        {
            var tokens = await _db.RefreshTokens.Where(t => t.UserId == userId && !t.IsRevoked).ToListAsync(ct);
            foreach (var t in tokens) t.IsRevoked = true;
        }

        await _db.SaveChangesAsync(ct);
    }

    private static string GenerateRandomPassword(int length)
    {
        // 保证至少 1 字母 + 1 数字，避开易混字符（0/O/1/l/I）
        const string letters = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ";
        const string digits = "23456789";
        const string symbols = "!@#$%^&*";
        var pool = letters + digits + symbols;

        Span<byte> buf = stackalloc byte[length];
        RandomNumberGenerator.Fill(buf);

        var sb = new StringBuilder(length);
        // 保底前两位：1 字母 + 1 数字
        sb.Append(letters[buf[0] % letters.Length]);
        sb.Append(digits[buf[1] % digits.Length]);
        for (var i = 2; i < length; i++)
        {
            sb.Append(pool[buf[i] % pool.Length]);
        }
        return sb.ToString();
    }
}
