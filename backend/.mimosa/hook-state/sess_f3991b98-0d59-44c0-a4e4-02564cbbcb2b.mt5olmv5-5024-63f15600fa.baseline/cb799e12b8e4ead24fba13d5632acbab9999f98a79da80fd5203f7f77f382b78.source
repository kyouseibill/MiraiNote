namespace MiraiNote.Shared.Common;

/// <summary>
/// 当前登录用户上下文服务。
/// 放在 Shared 层是为了避免 Data → Core 反向依赖（项目引用链：API → Core → Data → Shared）。
/// 由 API 层基于 HttpContext + JWT 实现，供 DbContext 等下层组件读取当前用户信息。
/// </summary>
public interface ICurrentUserService
{
    /// <summary>当前用户 Id；未登录时返回 0。</summary>
    int UserId { get; }

    /// <summary>是否已认证（已登录）。</summary>
    bool IsAuthenticated { get; }
}
