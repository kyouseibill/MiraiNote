using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using MiraiNote.API.Controllers;
using MiraiNote.Core.Services;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Auth;
using Moq;
using Xunit;

namespace MiraiNote.Tests;

public class AuthControllerCookieTests
{
    [Fact]
    public async Task Login_OnHttpRequest_DoesNotMarkRefreshCookieSecure()
    {
        var auth = new Mock<IAuthService>();
        auth.Setup(x => x.LoginAsync(It.IsAny<LoginRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResult
            {
                RefreshToken = "refresh-token",
                RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(7)
            });
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns("Production");

        var controller = new AuthController(
            auth.Object,
            Mock.Of<ICurrentUserService>(),
            environment.Object)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.HttpContext.Request.Scheme = "http";

        await controller.Login(new LoginRequest(), CancellationToken.None);

        var setCookie = controller.HttpContext.Response.Headers["Set-Cookie"].ToString();
        Assert.DoesNotContain("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Logout_ClearsRefreshCookie_WithMatchingPathSecureAndSameSite()
    {
        var auth = new Mock<IAuthService>();
        auth.Setup(x => x.LogoutAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        auth.Setup(x => x.LogoutAllAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);
        currentUser.SetupGet(x => x.UserId).Returns(0);

        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns("Production");

        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Headers.Cookie = "mn_refresh=sticky-refresh";

        var controller = new AuthController(auth.Object, currentUser.Object, environment.Object)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = http
            }
        };

        await controller.Logout(CancellationToken.None);

        auth.Verify(x => x.LogoutAsync("sticky-refresh", It.IsAny<CancellationToken>()), Times.Once);
        auth.Verify(x => x.LogoutAllAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

        var setCookie = http.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains("mn_refresh=", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        // Delete cookie uses an expired date
        Assert.True(
            setCookie.Contains("expires=", StringComparison.OrdinalIgnoreCase)
            || setCookie.Contains("max-age=0", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Logout_WhenAuthenticated_RevokesAllForUser()
    {
        var auth = new Mock<IAuthService>();
        auth.Setup(x => x.LogoutAllAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.UserId).Returns(42);

        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns("Development");

        var http = new DefaultHttpContext();
        http.Request.Scheme = "http";

        var controller = new AuthController(auth.Object, currentUser.Object, environment.Object)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = http
            }
        };

        await controller.Logout(CancellationToken.None);

        auth.Verify(x => x.LogoutAllAsync(42, It.IsAny<CancellationToken>()), Times.Once);
        auth.Verify(x => x.LogoutAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        var setCookie = http.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains("path=/api/v1/auth", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }
}
