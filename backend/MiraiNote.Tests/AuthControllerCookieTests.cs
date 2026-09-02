using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Primitives;
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
}
