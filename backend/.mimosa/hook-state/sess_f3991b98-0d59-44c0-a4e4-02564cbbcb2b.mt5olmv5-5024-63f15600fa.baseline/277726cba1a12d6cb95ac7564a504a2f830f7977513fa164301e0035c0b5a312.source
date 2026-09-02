namespace MiraiNote.Shared.Common;

/// <summary>
/// 业务异常：由 Service 抛出，全局异常中间件捕获后映射为统一响应。
/// 使用 <see cref="StatusCode"/> 指示期望的 HTTP 状态码（默认 400）。
/// </summary>
public class BusinessException : Exception
{
    public int StatusCode { get; }

    public BusinessException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }
}
