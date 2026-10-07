using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MiraiNote.Data.Context;

namespace MiraiNote.API.Health;

/// <summary>
/// 就绪检查只回答能不能连上数据库。失败时不把异常、连接串或主机名放进结果。
/// </summary>
public sealed class PostgresReadyHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopes;

    public PostgresReadyHealthCheck(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MiraiNoteDbContext>();
            var connected = await db.Database.CanConnectAsync(cancellationToken);
            return connected ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
        }
        catch
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}
