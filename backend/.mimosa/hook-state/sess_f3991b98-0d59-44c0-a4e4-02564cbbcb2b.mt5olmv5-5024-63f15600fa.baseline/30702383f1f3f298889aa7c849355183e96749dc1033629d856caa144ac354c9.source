using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MiraiNote.Shared.Agent;

namespace MiraiNote.Core.Services.Tools;

/// <summary>
/// 天气查询工具（基于 Open-Meteo 免费 API，无需 API Key）。
/// 支持城市名查询，返回未来几天的天气预报。
/// </summary>
public class ServerWeatherTool : IServerAgentTool
{
    private readonly IHttpClientFactory _httpClientFactory;

    public string Name => "get_weather";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Safe;
    public string Description =>
        "查询指定城市未来几天的天气预报。基于 Open-Meteo 免费 API。" +
        "返回每日最高/最低温度、降水概率和天气状况。" +
        "适用于用户询问天气相关问题。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["city"] = ToolParameterProperty.String("城市名称，中文或英文均可（必填，如 北京、上海、tokyo）"),
            ["days"] = ToolParameterProperty.Integer("预报天数，1-7，默认 3")
        },
        Required = new() { "city" }
    };

    public ServerWeatherTool(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    // IAgentTool 兼容
    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public async Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        var args = JsonDocument.Parse(argumentsJson).RootElement;
        if (!ToolArgHelper.TryGetString(args, "city", out var city))
            return "查询失败：未提供 city 参数。";

        ToolArgHelper.TryGetInt(args, "days", out var days);
        if (days <= 0 || days > 7) days = 3;

        try
        {
            var geoClient = _httpClientFactory.CreateClient("OpenMeteo");

            // Step 1: 地理编码（城市名 → 经纬度）
            var geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=zh";
            var geoResp = await geoClient.GetAsync(geoUrl, ct);
            if (!geoResp.IsSuccessStatusCode)
                return $"地理编码失败（{(int)geoResp.StatusCode}）：无法找到城市「{city}」。";

            var geoJson = await geoResp.Content.ReadAsStringAsync(ct);
            using var geoDoc = JsonDocument.Parse(geoJson);
            var results = geoDoc.RootElement.GetProperty("results");

            if (results.GetArrayLength() == 0)
                return $"未找到城市「{city}」，请尝试更具体的中文城市名（如 北京市、浦东新区）。";

            var location = results[0];
            var lat = location.GetProperty("latitude").GetDouble();
            var lon = location.GetProperty("longitude").GetDouble();
            var resolvedName = location.TryGetProperty("name", out var n) ? n.GetString() : city;
            var country = location.TryGetProperty("country", out var c) ? c.GetString() : "";

            // Step 2: 获取天气预报
            var weatherUrl = $"https://api.open-meteo.com/v1/forecast" +
                $"?latitude={lat}&longitude={lon}" +
                $"&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_mean,weathercode" +
                $"&timezone=Asia%2FShanghai&forecast_days={days}";

            var weatherResp = await geoClient.GetAsync(weatherUrl, ct);
            if (!weatherResp.IsSuccessStatusCode)
                return $"天气查询失败（{(int)weatherResp.StatusCode}）。";

            var weatherJson = await weatherResp.Content.ReadAsStringAsync(ct);
            using var weatherDoc = JsonDocument.Parse(weatherJson);
            var daily = weatherDoc.RootElement.GetProperty("daily");

            var dates = daily.GetProperty("time").EnumerateArray().Select(d => d.GetString()).ToArray();
            var maxTemps = daily.GetProperty("temperature_2m_max").EnumerateArray().Select(t => t.GetDouble()).ToArray();
            var minTemps = daily.GetProperty("temperature_2m_min").EnumerateArray().Select(t => t.GetDouble()).ToArray();
            var precipProbs = daily.GetProperty("precipitation_probability_mean").EnumerateArray().Select(p => p.GetInt32()).ToArray();
            var weatherCodes = daily.GetProperty("weathercode").EnumerateArray().Select(w => w.GetInt32()).ToArray();

            var forecast = new List<object>();
            for (int i = 0; i < dates.Length; i++)
            {
                forecast.Add(new
                {
                    date = dates[i],
                    temp_max = maxTemps[i],
                    temp_min = minTemps[i],
                    precipitation_probability = precipProbs[i],
                    weather = WmoCodeToDescription(weatherCodes[i])
                });
            }

            return JsonSerializer.Serialize(new
            {
                city = resolvedName,
                country,
                latitude = lat,
                longitude = lon,
                unit = "°C",
                days = forecast.Count,
                forecast
            });
        }
        catch (TaskCanceledException)
        {
            return "天气查询超时，请稍后重试。";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"天气查询失败：{ex.Message}";
        }
    }

    private static string WmoCodeToDescription(int code) => code switch
    {
        0 => "晴天",
        1 => "大部晴朗",
        2 => "多云",
        3 => "阴天",
        45 or 48 => "雾",
        51 => "小毛毛雨",
        53 => "毛毛雨",
        55 => "大毛毛雨",
        61 => "小雨",
        63 => "中雨",
        65 => "大雨",
        71 => "小雪",
        73 => "中雪",
        75 => "大雪",
        80 => "阵雨",
        81 => "中阵雨",
        82 => "大阵雨",
        85 => "小阵雪",
        86 => "大阵雪",
        95 => "雷暴",
        96 or 99 => "雷暴+冰雹",
        _ => $"未知({code})"
    };
}
