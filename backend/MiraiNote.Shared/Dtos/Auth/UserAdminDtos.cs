using MiraiNote.Shared.Dtos.Auth;

namespace MiraiNote.Shared.Dtos.Auth;

/// <summary>管理员创建用户的请求。</summary>
public class AdminCreateUserRequest
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    /// <summary>初始密码；若为空，则系统随机生成（8 位字母+数字）。</summary>
    public string? InitialPassword { get; set; }
    public bool IsAdmin { get; set; } = false;
}

/// <summary>管理员设置账户启用/禁用状态。</summary>
public class UpdateUserStatusRequest
{
    public bool IsActive { get; set; }
}

/// <summary>用户列表查询参数。</summary>
public class UserListQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    /// <summary>关键词（按用户名或邮箱模糊匹配，可选）。</summary>
    public string? Keyword { get; set; }
}

/// <summary>分页结果。</summary>
public class PagedResult<T>
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
}

/// <summary>用户管理列表项。</summary>
public class UserListItemDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public bool IsActive { get; set; }
    public bool IsEmailVerified { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
