using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiraiNote.CLI.Services;

// ===== DTO 模型 =====

public record ApiResp<T>(bool Success, T? Data, string Message);
public record ApiResp(bool Success, string Message);
public record PagedResult<T>(int Page, int PageSize, int Total, List<T> Items);

public record AuthTokenUserInfo(int Id, string Username, string Email, bool IsAdmin);
public record AuthTokens(string AccessToken, DateTime AccessTokenExpiresAt, AuthTokenUserInfo? User);

public record WorkLogDto(
    int Id, string Title, string? Purpose, string? Content,
    string? Tags, string? Category, DateTime LogDate, byte Status,
    DateTime CreatedAt, DateTime UpdatedAt);

public record MemoDto(
    int Id, string Section, string Content, byte Priority,
    bool IsPinned, bool IsDone, bool IsArchived,
    DateTime? RemindAt, DateTime CreatedAt, DateTime UpdatedAt);

public record LifeLogDto(
    int Id, string Content, string? Mood, string? ImagePath,
    DateTime LogDate, DateTime CreatedAt, DateTime UpdatedAt);

public record ChatSessionDto(int Id, string Title, DateTime CreatedAt, DateTime UpdatedAt);
public record ChatMessageDto(int Id, string Role, string Content, DateTime CreatedAt);
public record ChatSessionDetailDto(int Id, string Title, List<ChatMessageDto> Messages, DateTime UpdatedAt);

public record WeeklyReportDto(
    int Id, DateTime WeekStart, DateTime WeekEnd,
    string? Content, DateTime GeneratedAt);

/// <summary>
/// 封装所有对 MiraiNote REST API 的 HTTP 调用。
/// </summary>
public class ApiClient
{
    private readonly HttpClient _http;
    private readonly TokenStore _store;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ApiClient(TokenStore store)
    {
        _store = store;
        _http  = new HttpClient();
    }

    // ===== 认证 =====

    public async Task<AuthTokens> LoginAsync(string username, string password)
    {
        var resp = await _http.PostAsJsonAsync(
            Url("/api/v1/auth/login"),
            new { usernameOrEmail = username, password });
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw new ApiException(ExtractMessage(body, (int)resp.StatusCode));

        var result = Deserialize<ApiResp<AuthTokens>>(body);
        if (result?.Data == null) throw new ApiException("登录响应异常");
        return result.Data;
    }

    public async Task LogoutAsync()
    {
        try { await SendAsync(HttpMethod.Post, "/api/v1/auth/logout"); }
        catch { /* 忽略注销错误 */ }
    }

    // ===== 工作记录 =====

    public async Task<PagedResult<WorkLogDto>> GetWorkLogsAsync(
        string? keyword = null, DateTime? dateFrom = null, DateTime? dateTo = null,
        string? category = null, byte? status = null, int page = 1, int pageSize = 20)
    {
        var qs = BuildQs(new Dictionary<string, string?>
        {
            ["keyword"]  = keyword,
            ["dateFrom"] = dateFrom?.ToString("yyyy-MM-dd"),
            ["dateTo"]   = dateTo?.ToString("yyyy-MM-dd"),
            ["category"] = category,
            ["status"]   = status?.ToString(),
            ["page"]     = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        });
        return await GetAsync<PagedResult<WorkLogDto>>($"/api/v1/worklogs{qs}");
    }

    public async Task<WorkLogDto> CreateWorkLogAsync(object payload)
        => await PostAsync<WorkLogDto>("/api/v1/worklogs", payload);

    public async Task<WorkLogDto> UpdateWorkLogAsync(int id, object payload)
        => await PutAsync<WorkLogDto>($"/api/v1/worklogs/{id}", payload);

    public async Task DeleteWorkLogAsync(int id)
        => await DeleteAsync($"/api/v1/worklogs/{id}");

    // ===== 备忘 =====

    public async Task<PagedResult<MemoDto>> GetMemosAsync(
        string section = "work", string? keyword = null,
        bool includeDone = false, bool includeArchived = false, int page = 1, int pageSize = 50)
    {
        var qs = BuildQs(new Dictionary<string, string?>
        {
            ["section"]         = section,
            ["keyword"]         = keyword,
            ["includeDone"]     = includeDone.ToString().ToLower(),
            ["includeArchived"] = includeArchived.ToString().ToLower(),
            ["page"]            = page.ToString(),
            ["pageSize"]        = pageSize.ToString()
        });
        return await GetAsync<PagedResult<MemoDto>>($"/api/v1/memos{qs}");
    }

    public async Task<MemoDto> CreateMemoAsync(object payload)
        => await PostAsync<MemoDto>("/api/v1/memos", payload);

    public async Task<MemoDto> UpdateMemoAsync(int id, object payload)
        => await PutAsync<MemoDto>($"/api/v1/memos/{id}", payload);

    public async Task<MemoDto> PatchMemoStatusAsync(int id, object payload)
    {
        var req = new HttpRequestMessage(HttpMethod.Patch, Url($"/api/v1/memos/{id}/status"))
        {
            Content = JsonContent.Create(payload, options: _json)
        };
        SetAuthHeader(req);
        var resp = await _http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw new ApiException(ExtractMessage(body, (int)resp.StatusCode));
        var result = Deserialize<ApiResp<MemoDto>>(body);
        return result?.Data ?? throw new ApiException("响应数据为空");
    }

    public async Task DeleteMemoAsync(int id)
        => await DeleteAsync($"/api/v1/memos/{id}");

    // ===== 生活记录 =====

    public async Task<PagedResult<LifeLogDto>> GetLifeLogsAsync(
        string? keyword = null, string? mood = null, string? month = null, int page = 1, int pageSize = 20)
    {
        var qs = BuildQs(new Dictionary<string, string?>
        {
            ["keyword"]  = keyword,
            ["mood"]     = mood,
            ["month"]    = month,
            ["page"]     = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        });
        return await GetAsync<PagedResult<LifeLogDto>>($"/api/v1/lifelogs{qs}");
    }

    public async Task<LifeLogDto> CreateLifeLogAsync(object payload)
        => await PostAsync<LifeLogDto>("/api/v1/lifelogs", payload);

    public async Task<LifeLogDto> UpdateLifeLogAsync(int id, object payload)
        => await PutAsync<LifeLogDto>($"/api/v1/lifelogs/{id}", payload);

    public async Task DeleteLifeLogAsync(int id)
        => await DeleteAsync($"/api/v1/lifelogs/{id}");

    // ===== AI 对话 =====

    public async Task<List<ChatSessionDto>> GetChatSessionsAsync()
        => await GetAsync<List<ChatSessionDto>>("/api/v1/chat/sessions");

    public async Task<ChatSessionDto> CreateChatSessionAsync(string title = "新对话")
        => await PostAsync<ChatSessionDto>("/api/v1/chat/sessions", new { title });

    public async Task<ChatSessionDetailDto> GetChatSessionAsync(int sessionId)
        => await GetAsync<ChatSessionDetailDto>($"/api/v1/chat/sessions/{sessionId}");

    public async Task<ChatMessageDto> SendChatMessageAsync(int sessionId, string content)
        => await PostAsync<ChatMessageDto>($"/api/v1/chat/sessions/{sessionId}/messages", new { content });

    // ===== 周报 =====

    public async Task<List<WeeklyReportDto>> GetWeeklyReportsAsync()
        => await GetAsync<List<WeeklyReportDto>>("/api/v1/reports");

    public async Task<WeeklyReportDto> GenerateWeeklyReportAsync(DateTime weekStart)
        => await PostAsync<WeeklyReportDto>("/api/v1/reports/generate",
            new { weekStart = weekStart.ToString("yyyy-MM-dd"), weekEnd = weekStart.AddDays(6).ToString("yyyy-MM-dd") });

    // ===== 通用 HTTP 方法（供 MemoryTools 等自定义工具使用）=====

    public async Task<T> GetAsync<T>(string path) where T : class
    {
        var req = new HttpRequestMessage(HttpMethod.Get, Url(path));
        SetAuthHeader(req);
        var resp = await _http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw new ApiException(ExtractMessage(body, (int)resp.StatusCode));
        var result = Deserialize<ApiResp<T>>(body);
        return result?.Data ?? throw new ApiException("响应数据为空");
    }

    public async Task<T> PostAsync<T>(string path, object payload) where T : class
    {
        var req = new HttpRequestMessage(HttpMethod.Post, Url(path))
        { Content = JsonContent.Create(payload, options: _json) };
        SetAuthHeader(req);
        var resp = await _http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw new ApiException(ExtractMessage(body, (int)resp.StatusCode));
        var result = Deserialize<ApiResp<T>>(body);
        return result?.Data ?? throw new ApiException("响应数据为空");
    }

    public async Task DeleteAsync(string path)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, Url(path));
        SetAuthHeader(req);
        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            throw new ApiException(ExtractMessage(body, (int)resp.StatusCode));
        }
    }

    // ===== 内部辅助 =====

    private async Task<T> PutAsync<T>(string path, object payload) where T : class
    {
        var req = new HttpRequestMessage(HttpMethod.Put, Url(path))
        {
            Content = JsonContent.Create(payload, options: _json)
        };
        SetAuthHeader(req);
        var resp = await _http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw new ApiException(ExtractMessage(body, (int)resp.StatusCode));
        var result = Deserialize<ApiResp<T>>(body);
        return result?.Data ?? throw new ApiException("响应数据为空");
    }

    private async Task SendAsync(HttpMethod method, string path, object? payload = null)
    {
        var req = new HttpRequestMessage(method, Url(path));
        if (payload != null) req.Content = JsonContent.Create(payload, options: _json);
        SetAuthHeader(req);
        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            throw new ApiException(ExtractMessage(body, (int)resp.StatusCode));
        }
    }

    private void SetAuthHeader(HttpRequestMessage req)
    {
        if (!string.IsNullOrWhiteSpace(_store.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _store.Token);
    }

    private string Url(string path) => _store.ApiBase + path;

    private static string BuildQs(Dictionary<string, string?> p)
    {
        var pairs = p.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                     .Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value!)}");
        var qs = string.Join("&", pairs);
        return string.IsNullOrEmpty(qs) ? "" : "?" + qs;
    }

    private static T? Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, _json);

    private static string ExtractMessage(string body, int statusCode)
    {
        try
        {
            var doc  = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                return m.GetString() ?? $"HTTP {statusCode}";
        }
        catch { /* ignore */ }
        return $"HTTP {statusCode}: {body[..Math.Min(200, body.Length)]}";
    }
}

public class ApiException(string message) : Exception(message) { }
