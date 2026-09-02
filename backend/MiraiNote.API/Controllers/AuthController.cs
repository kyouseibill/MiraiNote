using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Auth;

namespace MiraiNote.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    private const string RefreshCookieName = "mn_refresh";

    private readonly IAuthService _auth;
    private readonly ICurrentUserService _currentUser;
    private readonly IWebHostEnvironment _env;

    public AuthController(IAuthService auth, ICurrentUserService currentUser, IWebHostEnvironment env)
    {
        _auth = auth;
        _currentUser = currentUser;
        _env = env;
    }

    [HttpPost("register")]
    public async Task<ActionResult<ApiResponse>> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        await _auth.RegisterAsync(request, ct);
        return Ok(ApiResponse.Ok("注册成功，请查收验证邮件"));
    }

    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<AuthTokenResponse>>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _auth.LoginAsync(request, ct);
        SetRefreshCookie(result.RefreshToken, result.RefreshTokenExpiresAt);
        return Ok(ApiResponse<AuthTokenResponse>.Ok(result.Tokens, "登录成功"));
    }

    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpPost("logout")]
    public async Task<ActionResult<ApiResponse>> Logout(CancellationToken ct)
    {
        var refreshToken = Request.Cookies[RefreshCookieName];
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            await _auth.LogoutAsync(refreshToken, ct);
        }
        ClearRefreshCookie();
        return Ok(ApiResponse.Ok("已登出"));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<ApiResponse<AuthTokenResponse>>> Refresh(CancellationToken ct)
    {
        var refreshToken = Request.Cookies[RefreshCookieName]
            ?? throw new BusinessException("缺少刷新凭证", 401);

        var result = await _auth.RefreshTokenAsync(refreshToken, ct);
        SetRefreshCookie(result.RefreshToken, result.RefreshTokenExpiresAt);
        return Ok(ApiResponse<AuthTokenResponse>.Ok(result.Tokens));
    }

    [HttpPost("verify-email")]
    public async Task<ActionResult<ApiResponse>> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken ct)
    {
        await _auth.VerifyEmailAsync(request.Token, ct);
        return Ok(ApiResponse.Ok("邮箱验证成功"));
    }

    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpPost("resend-verify")]
    public async Task<ActionResult<ApiResponse>> ResendVerify(CancellationToken ct)
    {
        await _auth.ResendVerifyEmailAsync(_currentUser.UserId, ct);
        return Ok(ApiResponse.Ok("验证邮件已重新发送"));
    }

    [HttpPost("forgot-password")]
    public async Task<ActionResult<ApiResponse>> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        await _auth.ForgotPasswordAsync(request.Email, ct);
        // 统一成功响应，避免账户枚举
        return Ok(ApiResponse.Ok("若该邮箱已注册，您将收到重置邮件"));
    }

    [HttpPost("reset-password")]
    public async Task<ActionResult<ApiResponse>> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        await _auth.ResetPasswordAsync(request, ct);
        ClearRefreshCookie();
        return Ok(ApiResponse.Ok("密码重置成功，请重新登录"));
    }

    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpPut("change-password")]
    public async Task<ActionResult<ApiResponse>> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        await _auth.ChangePasswordAsync(_currentUser.UserId, request, ct);
        ClearRefreshCookie();
        return Ok(ApiResponse.Ok("密码修改成功，请重新登录"));
    }

    // ===== Cookie 工具 =====

    private void SetRefreshCookie(string token, DateTime expiresAt)
    {
        Response.Cookies.Append(RefreshCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            // Cookie 必须与实际请求协议匹配；当前生产环境通过 HTTP IP 访问时不能标记为 Secure。
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = expiresAt,
            Path = "/api/v1/auth" // 仅 auth 路径下随请求带出
        });
    }

    private void ClearRefreshCookie()
    {
        Response.Cookies.Delete(RefreshCookieName, new CookieOptions
        {
            Path = "/api/v1/auth"
        });
    }
}
