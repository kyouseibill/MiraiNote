using System.Text.Json;
using System.Text.Json.Serialization;
using MiraiNote.Core.Services.Household;
using MiraiNote.Shared.Agent;

namespace MiraiNote.Core.Services.Tools;

/// <summary>
/// 只把家务一句话解析成待确认草稿或只读查询。确认写入走单独接口，这个工具不落完成记录。
/// </summary>
public sealed class ServerHouseholdChatTool : ServerQueryTool
{
    public const string ToolName = "household_chat";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHouseholdChatService _chat;

    public ServerHouseholdChatTool(IHouseholdChatService chat) => _chat = chat;

    public override string Name => ToolName;

    public override string Description =>
        "识别家务一句话。用户说换了、做了保养、花了多少钱，或问什么时候换过、最近要到期时调用。" +
        "把用户原话放进 utterance。这个工具只返回待确认草稿或只读结果，不会写入完成记录。" +
        "识别不出就照 message 说明，不要猜测已经记上。";

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["utterance"] = ToolParameterProperty.String("用户的原话，不要改写")
        },
        Required = ["utterance"]
    };

    public override async Task<string> ExecuteAsync(int userId, string argsJson, CancellationToken ct)
    {
        var utterance = ReadUtterance(argsJson);
        var result = await _chat.InterpretAsync(userId, utterance, ct);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    private static string ReadUtterance(string argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson))
            return "";
        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            if (doc.RootElement.TryGetProperty("utterance", out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? "";
        }
        catch (JsonException)
        {
            return "";
        }
        return "";
    }
}
