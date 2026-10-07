using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiraiNote.Shared.Json;

/// <summary>
/// 纯日期字段序列化为 yyyy-MM-dd。读取时只取前 10 位，避免带 Z 的午夜被时区换日。
/// </summary>
public sealed class CalendarDateJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => CalendarDateJson.ReadRequired(ref reader);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => CalendarDateJson.Write(writer, value);
}

public sealed class NullableCalendarDateJsonConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        var text = reader.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return CalendarDateJson.ReadRequired(text);
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            CalendarDateJson.Write(writer, value.Value);
        else
            writer.WriteNullValue();
    }
}

internal static class CalendarDateJson
{
    public static DateTime ReadRequired(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("日期格式应为 yyyy-MM-dd");
        return ReadRequired(reader.GetString());
    }

    public static DateTime ReadRequired(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 10
            || !DateTime.TryParseExact(text.AsSpan(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new JsonException("日期格式应为 yyyy-MM-dd");
        }

        return DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
    }

    public static void Write(Utf8JsonWriter writer, DateTime value)
        => writer.WriteStringValue(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
}
