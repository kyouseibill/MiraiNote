using MiraiNote.Shared.Dtos.Auth;

namespace MiraiNote.Core.Services;

public interface IAuthService
{
    Task RegisterAsync(RegisterRequest request, CancellationToken ct = default);

    Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken ct = default);

    Task LogoutAsync(string refreshToken, CancellationToken ct = default);

    Task<LoginResult> RefreshTokenAsync(string refreshToken, CancellationToken ct = default);

    Task VerifyEmailAsync(string token, CancellationToken ct = default);

    Task ResendVerifyEmailAsync(int userId, CancellationToken ct = default);

    Task ForgotPasswordAsync(string email, CancellationToken ct = default);

    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);

    Task ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken ct = default);
}
