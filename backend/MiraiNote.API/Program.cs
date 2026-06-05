using MiraiNote.API;
using MiraiNote.API.Infrastructure;
using MiraiNote.API.Middleware;
using MiraiNote.Core;
using MiraiNote.Core.Services;
using MiraiNote.Data;
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
builder.Services.AddControllers().AddJsonOptions(opts =>
{
    opts.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
    opts.JsonSerializerOptions.Converters.Add(new UtcNullableDateTimeJsonConverter());
});

builder.Services.AddDataLayer(builder.Configuration);
builder.Services.AddCoreLayer();
builder.Services.AddApiLayer(builder.Configuration, builder.Environment);

var app = builder.Build();

// ===== Database Seed（幂等）=====
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await seeder.SeedAsync();
}

// ===== Pipeline =====
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "MiraiNote API v1"));
}

app.UseMiddleware<GlobalExceptionMiddleware>();
if (!app.Environment.IsDevelopment())
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

app.Run();
