using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Xunit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using MiraiNote.API.Infrastructure;
using MiraiNote.Shared.Common;

namespace MiraiNote.Tests;

public class ChineseModelStateResponseTests
{
    [Fact]
    public void InvalidDate_ReturnsChinese400_WithoutClrTypeName()
    {
        var context = new ActionContext(
            new DefaultHttpContext(),
            new RouteData(),
            new ActionDescriptor(),
            new ModelStateDictionary());
        context.ModelState.AddModelError(
            "$.expiryDate",
            "The JSON value could not be converted to System.DateOnly. Path: $.expiryDate | LineNumber: 0 | BytePositionInLine: 28.");

        var result = Assert.IsType<BadRequestObjectResult>(ChineseModelStateResponses.Create(context));
        var body = Assert.IsType<ApiResponse>(result.Value);
        Assert.False(body.Success);
        Assert.Equal(ChineseModelStateResponses.InvalidDateMessage, body.Message);

        var json = JsonSerializer.Serialize(body);
        Assert.DoesNotContain("System.DateOnly", json, StringComparison.Ordinal);
        Assert.DoesNotContain("DateOnly", json, StringComparison.Ordinal);
    }

    [Fact]
    public void OtherBindingFailure_DoesNotEchoFrameworkText()
    {
        var context = new ActionContext(
            new DefaultHttpContext(),
            new RouteData(),
            new ActionDescriptor(),
            new ModelStateDictionary());
        context.ModelState.AddModelError("request", "The request field is required.");

        var result = Assert.IsType<BadRequestObjectResult>(ChineseModelStateResponses.Create(context));
        var body = Assert.IsType<ApiResponse>(result.Value);
        Assert.Equal(ChineseModelStateResponses.InvalidRequestMessage, body.Message);
        Assert.DoesNotContain("System.", JsonSerializer.Serialize(body), StringComparison.Ordinal);
    }
}
