namespace MiraiNote.Shared.Dtos.Auth;

// ===== 请求 DTO =====

public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>所在地区，例如「中国 · 上海」。注册必填。</summary>
    public string? Place { get; set; }
}

public class LoginRequest
{
    /// <summary>用户名或邮箱。</summary>
    public string UsernameOrEmail { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; } = false;
}

public class VerifyEmailRequest
{
    public string Token { get; set; } = string.Empty;
}

public class ResendVerifyEmailRequest
{
    public string Email { get; set; } = string.Empty;
}

public class ResendVerifyTokenRequest
{
    public string Token { get; set; } = string.Empty;
}

public static class RegisterOutcomes
{
    public const string VerificationEmailSent = "verification_email_sent";
    public const string VerificationEmailFailed = "verification_email_failed";
    public const string VerificationDisabled = "verification_disabled";
}

public static class VerifyEmailStatuses
{
    public const string Verified = "verified";
    public const string AlreadyVerified = "already_verified";
    public const string Expired = "expired";
    public const string Invalid = "invalid";
}

public static class AuthMessages
{
    public const string ResendVerify =
        "如果这个邮箱已注册但还没验证，几分钟内会收到验证邮件。没收到的话，请看一下垃圾邮件箱。";

    public const string RegisterEmailSent = "注册成功，请查收验证邮件。没收到的话，请看一下垃圾邮件箱。";
    public const string RegisterEmailFailed = "验证邮件发送失败，请稍后重发";
    public const string RegisterVerificationDisabled = "注册成功";

    public const string VerifySuccess = "验证成功";
    public const string VerifyAlready = "邮箱已验证，请直接登录";
    public const string VerifyExpired = "链接已过期";
    public const string VerifyInvalid = "链接无效";

    public static string RegisterMessage(string outcome) => outcome switch
    {
        RegisterOutcomes.VerificationEmailSent => RegisterEmailSent,
        RegisterOutcomes.VerificationEmailFailed => RegisterEmailFailed,
        _ => RegisterVerificationDisabled
    };

    public static string VerifyMessage(string status) => status switch
    {
        VerifyEmailStatuses.Verified => VerifySuccess,
        VerifyEmailStatuses.AlreadyVerified => VerifyAlready,
        VerifyEmailStatuses.Expired => VerifyExpired,
        _ => VerifyInvalid
    };
}

public class RegisterResult
{
    public string Outcome { get; set; } = string.Empty;
}

public class VerifyEmailResult
{
    public string Status { get; set; } = string.Empty;
}

public class ForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}

// ===== 响应 DTO =====

/// <summary>
/// 登录/刷新成功后的响应。
/// RefreshToken 不放在响应体里，由 Controller 写入 HttpOnly Cookie。
/// </summary>
public class AuthTokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAt { get; set; }
    public UserInfoDto User { get; set; } = new();
}

public class UserInfoDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public bool IsEmailVerified { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

/// <summary>用于 Service 内部返回 AccessToken + RefreshToken 原始值，Controller 负责种 Cookie。</summary>
public class LoginResult
{
    public AuthTokenResponse Tokens { get; set; } = new();
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime RefreshTokenExpiresAt { get; set; }
}
