using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MiraiNote.API.Health;

public static class HealthEndpoints
{
    public const string LivePath = "/health";
    public const string ReadyPath = "/health/ready";

    public static IServiceCollection AddMiraiHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<PostgresReadyHealthCheck>("database", tags: ["ready"]);
        return services;
    }

    public static WebApplication MapMiraiHealthChecks(this WebApplication app)
    {
        var live = PlainOptions(_ => false);
        var ready = PlainOptions(check => check.Tags.Contains("ready"));
        ready.ResultStatusCodes[HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable;

        // 不走登录，也不进全局限流。路径不带 /api。
        app.MapHealthChecks(LivePath, live).AllowAnonymous().DisableRateLimiting();
        app.MapHealthChecks(ReadyPath, ready).AllowAnonymous().DisableRateLimiting();
        return app;
    }

    private static HealthCheckOptions PlainOptions(Func<HealthCheckRegistration, bool> predicate) => new()
    {
        Predicate = predicate,
        ResponseWriter = WritePlain,
        ResultStatusCodes =
        {
            [HealthStatus.Healthy] = StatusCodes.Status200OK,
            [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
            [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
        },
    };

    private static Task WritePlain(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "text/plain; charset=utf-8";
        var body = report.Status == HealthStatus.Healthy ? "Healthy" : "Unhealthy";
        return context.Response.WriteAsync(body);
    }
}
