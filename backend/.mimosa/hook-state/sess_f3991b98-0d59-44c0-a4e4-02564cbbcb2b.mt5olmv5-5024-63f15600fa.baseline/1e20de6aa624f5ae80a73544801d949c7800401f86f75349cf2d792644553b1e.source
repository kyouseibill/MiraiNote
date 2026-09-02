using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiraiNote.API.Infrastructure;

/// <summary>
/// 将 DateTime（包括 EF Core 读取的 Unspecified Kind）序列化时统一加 Z 后缀，
/// 确保前端 new Date(...) 正确解析为 UTC 时间而非本地时间。
/// </summary>
public class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => DateTime.SpecifyKind(reader.GetDateTime(), DateTimeKind.Utc);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

/// <summary>
/// 可空 DateTime 的 UTC 转换器。
/// </summary>
public class UtcNullableDateTimeJsonConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        return DateTime.SpecifyKind(reader.GetDateTime(), DateTimeKind.Utc);
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
        else
            writer.WriteNullValue();
    }
}
