namespace MiraiNote.Shared.Common;

/// <summary>
/// 统一 API 响应包装。所有 Controller 接口的返回体均使用此格式。
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string Message { get; set; } = string.Empty;

    public static ApiResponse<T> Ok(T data, string message = "") =>
        new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> Fail(string message) =>
        new() { Success = false, Data = default, Message = message };
}

/// <summary>无数据的统一响应。</summary>
public class ApiResponse : ApiResponse<object?>
{
    public static ApiResponse Ok(string message = "") =>
        new() { Success = true, Data = null, Message = message };

    public static new ApiResponse Fail(string message) =>
        new() { Success = false, Data = null, Message = message };
}
