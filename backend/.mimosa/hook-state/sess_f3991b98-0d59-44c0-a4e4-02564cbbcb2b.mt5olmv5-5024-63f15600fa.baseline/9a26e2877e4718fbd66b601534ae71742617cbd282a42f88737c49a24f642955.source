using System.Text.Json;
using MiraiNote.Shared.Common;

namespace MiraiNote.API.Middleware;

/// <summary>
/// 全局异常中间件：
/// - <see cref="BusinessException"/> → 对应 HTTP 状态码 + 统一响应（含业务消息）
/// - 其他未处理异常 → 500；开发环境返回异常类型与堆栈，生产环境只返回"服务器内部错误"
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IWebHostEnvironment _env;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IWebHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (BusinessException ex)
        {
            _logger.LogWarning("业务异常 ({Status}): {Message}", ex.StatusCode, ex.Message);
            await WriteAsync(context, ex.StatusCode, ex.Message);
        }
        catch (OperationCanceledException)
        {
            // 客户端主动断开连接（浏览器切换页面等），属正常情况，不记录为错误
            _logger.LogInformation("请求已被客户端取消 {Path}", context.Request.Path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "未处理异常 {Path}", context.Request.Path);
            var message = _env.IsDevelopment()
                ? $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"
                : "服务器内部错误";
            await WriteAsync(context, 500, message);
        }
    }

    private static Task WriteAsync(HttpContext context, int statusCode, string message)
    {
        if (context.Response.HasStarted) return Task.CompletedTask;

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        var body = JsonSerializer.Serialize(ApiResponse.Fail(message), new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        return context.Response.WriteAsync(body);
    }
}
