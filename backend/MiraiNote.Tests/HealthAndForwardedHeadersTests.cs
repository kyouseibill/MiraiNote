using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MiraiNote.Data.Context;
using Xunit;

namespace MiraiNote.Tests;

public class HealthAndForwardedHeadersTests : IClassFixture<HealthApiFactory>
{
    private const string DbPassword = "SuperSecretDbPassword";
    private readonly HealthApiFactory _factory;

    public HealthAndForwardedHeadersTests(HealthApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Live_IsAnonymousPlainHealthy_EvenWhenDatabaseIsDown()
    {
        var client = _factory.CreateClient();

        var first = await client.GetAsync("/health");
        var second = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("text/plain", first.Content.Headers.ContentType?.MediaType);
        var body = await first.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", body);
        Assert.DoesNotContain(DbPassword, body);
        Assert.DoesNotContain(Environment.MachineName, body);
    }

    [Fact]
    public async Task Ready_WhenDatabaseIsDown_Is503WithoutSecrets()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Unhealthy", body);
        Assert.DoesNotContain(DbPassword, body);
        Assert.DoesNotContain("Host=", body);
        Assert.DoesNotContain(Environment.MachineName, body);
    }

    [Fact]
    public async Task Ready_WhenDatabaseConnects_IsHealthy()
    {
        await using var factory = new HealthApiFactory().WithSqlite();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body);
    }

    [Fact]
    public async Task ForwardedFor_FromLoopback_IsApplied()
    {
        var body = await ReadClientIp(IPAddress.Loopback, "203.0.113.10");
        Assert.Equal("203.0.113.10", body);
    }

    [Fact]
    public async Task ForwardedFor_FromIpv6Loopback_IsApplied()
    {
        var body = await ReadClientIp(IPAddress.IPv6Loopback, "203.0.113.11");
        Assert.Equal("203.0.113.11", body);
    }

    [Fact]
    public async Task ForwardedFor_FromOtherAddress_IsIgnored()
    {
        var body = await ReadClientIp(IPAddress.Parse("198.51.100.8"), "203.0.113.10");
        Assert.Equal("198.51.100.8", body);
    }

    private async Task<string> ReadClientIp(IPAddress remote, string forwardedFor)
    {
        var context = await _factory.Server.SendAsync(http =>
        {
            http.Request.Method = "GET";
            http.Request.Path = "/__test/client-ip";
            http.Request.Headers["X-Forwarded-For"] = forwardedFor;
            http.Connection.RemoteIpAddress = remote;
        });
        using var reader = new StreamReader(context.Response.Body);
        return (await reader.ReadToEndAsync()).Trim();
    }
}

public class HealthApiFactory : WebApplicationFactory<Program>
{
    private readonly string? _sqlitePath;

    public HealthApiFactory()
    {
    }

    private HealthApiFactory(string sqlitePath)
    {
        _sqlitePath = sqlitePath;
    }

    public HealthApiFactory WithSqlite()
    {
        var path = Path.Combine(Path.GetTempPath(), "mirai-health-" + Guid.NewGuid().ToString("N") + ".db");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            connection.Open();
        return new HealthApiFactory(path);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (_sqlitePath != null && File.Exists(_sqlitePath))
        {
            try { File.Delete(_sqlitePath); } catch { /* 测试库占用时留给系统清理 */ }
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.UseSetting(
            "ConnectionStrings:DefaultConnection",
            "Host=127.0.0.1;Port=1;Database=mirainote;Username=mirai;Password=SuperSecretDbPassword;Timeout=1");
        builder.UseSetting("Jwt:Secret", "test-jwt-secret-0123456789-abcdef");
        builder.UseSetting("Jwt:Issuer", "MiraiNote");
        builder.UseSetting("Jwt:Audience", "MiraiNote");
        builder.UseSetting("App:PublicBaseUrl", "https://notes.example.com");
        builder.ConfigureTestServices(services =>
        {
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                services.Remove(descriptor);

            if (_sqlitePath == null)
                return;

            var npgsql = services.Where(d =>
            {
                var names = new[]
                {
                    d.ServiceType.FullName,
                    d.ImplementationType?.FullName,
                    d.ImplementationInstance?.GetType().FullName,
                };
                return names.Any(name => name != null && (
                    name.Contains("Npgsql", StringComparison.Ordinal)
                    || name.Contains("PostgreSQL", StringComparison.Ordinal)
                    || name.Contains("IDbContextOptionsConfiguration", StringComparison.Ordinal)));
            }).ToList();
            foreach (var descriptor in npgsql)
                services.Remove(descriptor);

            services.RemoveAll<DbContextOptions<MiraiNoteDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<MiraiNoteDbContext>();
            services.AddDbContext<MiraiNoteDbContext>(options => options.UseSqlite($"Data Source={_sqlitePath}"));
        });
    }
}
