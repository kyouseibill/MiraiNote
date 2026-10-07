using System.Net;
using MiraiNote.API;
using MiraiNote.API.Health;
using MiraiNote.API.Infrastructure;
using MiraiNote.API.Middleware;
using MiraiNote.Core;
using MiraiNote.Core.Services;
using MiraiNote.Data;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using Serilog;
using Serilog.Events;

// 日志目录：相对于程序运行目录下的 logs/
var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "log-.txt");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,       // 每天一个文件：log-20260601.txt
        retainedFileCountLimit: 90,                 // 保留最近 90 天
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// ===== Services =====
// 注册 UTC DateTime JSON 转换器：确保 EF Core 返回的 DateTimeKind.Unspecified 序列化时带 Z 后缀
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = ChineseModelStateResponses.Create;
    })
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
        opts.JsonSerializerOptions.Converters.Add(new UtcNullableDateTimeJsonConverter());
    });

builder.Services.AddDataLayer(builder.Configuration);
builder.Services.AddCoreLayer();
builder.Services.AddApiLayer(builder.Configuration, builder.Environment);
builder.Services.AddMiraiHealthChecks();

// 只信任本机 Nginx。不要再设 ASPNETCORE_FORWARDEDHEADERS_ENABLED，那个开关会信任任意来源，也不能和这里叠加。
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    // net10 里 KnownNetworks 就是 KnownIPNetworks，默认是 127.0.0.0/8。清掉后只留两个回环地址。
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

var app = builder.Build();

// ===== Database Seed（幂等）=====
// 集成测试用 Test 环境，避免启动时连库。生产和其他环境仍会播种。
if (!app.Environment.IsEnvironment("Test"))
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await seeder.SeedAsync();
}

// ===== Pipeline =====
// 转发头必须在 HSTS、HTTPS 重定向、限流和认证之前。登录锁定按账号，验证邮件冷却按用户，都不读套接字地址。
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "MiraiNote API v1"));
}

app.UseMiddleware<GlobalExceptionMiddleware>();
if (app.Configuration.GetValue<bool>("Hosting:RequireHttps"))
{
    app.UseHttpsRedirection();
}
app.UseCors(ApiDependencyInjection.CorsPolicyName);

// 服务上传文件（图片等静态资源）；PhysicalPath 配置时指向外部目录，否则使用 wwwroot
var uploadCfg = app.Configuration.GetSection("Upload").Get<UploadOptions>() ?? new UploadOptions();
if (!string.IsNullOrEmpty(uploadCfg.PhysicalPath))
{
    Directory.CreateDirectory(uploadCfg.PhysicalPath);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(uploadCfg.PhysicalPath),
        RequestPath = "/" + uploadCfg.BasePath.Trim('/')
    });
}
else
{
    app.UseStaticFiles();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapMiraiHealthChecks();

if (app.Environment.IsEnvironment("Test"))
{
    app.MapGet("/__test/client-ip", (HttpContext http) =>
        Results.Text(http.Connection.RemoteIpAddress?.ToString() ?? "")).AllowAnonymous();
}

app.Run();

public partial class Program { }
