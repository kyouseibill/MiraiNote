using Microsoft.AspNetCore.Mvc;
using MiraiNote.Shared.Common;

namespace MiraiNote.API.Infrastructure;

/// <summary>
/// 模型绑定失败时返回统一中文 400，响应正文不带 CLR 类型名。
/// </summary>
public static class ChineseModelStateResponses
{
    public const string InvalidDateMessage = "日期格式不正确";

    public const string InvalidRequestMessage = "请求格式不正确";

    public static IActionResult Create(ActionContext context)
    {
        var texts = context.ModelState.Values
            .SelectMany(entry => entry.Errors)
            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                ? error.Exception?.Message ?? ""
                : error.ErrorMessage);

        var message = texts.Any(IsDateFormatError) ? InvalidDateMessage : InvalidRequestMessage;
        return new BadRequestObjectResult(ApiResponse.Fail(message));
    }

    private static bool IsDateFormatError(string text) =>
        text.Contains("DateOnly", StringComparison.Ordinal)
        || text.Contains("DateTime", StringComparison.Ordinal)
        || text.Contains("could not be converted", StringComparison.OrdinalIgnoreCase);
}
