using System.Text.Json;

namespace MiraiNote.CLI.Services;

/// <summary>
/// 将 JWT Token 和 API 地址持久化到本地 ~/.mirainote/session.json
/// </summary>
public class TokenStore
{
    private static readonly string ConfigDir  = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mirainote");
    private static readonly string ConfigFile = Path.Combine(ConfigDir, "session.json");

    private SessionData _data = new();

    public string? Token    => _data.Token;
    public string  ApiBase  => _data.ApiBase ?? "http://localhost:5273";
    public string? Username => _data.Username;
    public int?    LastChatSessionId => _data.LastChatSessionId;
    public string  DeepSeekApiKey    => _data.DeepSeekApiKey ?? string.Empty;
    public string  DeepSeekBaseUrl   => _data.DeepSeekBaseUrl ?? "https://api.deepseek.com";
    public string  DeepSeekModel     => _data.DeepSeekModel ?? "deepseek-v4-flash";
    public string? TavilyApiKey      => _data.TavilyApiKey;
    public string? SmtpHost          => _data.SmtpHost;
    public int     SmtpPort          => _data.SmtpPort > 0 ? _data.SmtpPort : 587;
    public string? SmtpUser          => _data.SmtpUser;
    public string? SmtpPassword      => _data.SmtpPassword;
    public string? SmtpFromAddress   => _data.SmtpFromAddress;
    public string? SmtpFromName      => _data.SmtpFromName;

    public void Load()
    {
        if (!File.Exists(ConfigFile)) return;
        try
        {
            var json = File.ReadAllText(ConfigFile);
            _data = JsonSerializer.Deserialize<SessionData>(json) ?? new SessionData();
        }
        catch { /* 损坏的配置文件直接忽略 */ }
    }

    public void SaveToken(string token, string username)
    {
        _data.Token    = token;
        _data.Username = username;
        Persist();
    }

    public void SaveApiBase(string apiBase)
    {
        _data.ApiBase = apiBase.TrimEnd('/');
        Persist();
    }

    public void SaveDeepSeekConfig(string? apiKey = null, string? baseUrl = null, string? model = null)
    {
        // null = 未提供（不修改）；非null = 提供（设置或清空）
        if (apiKey != null)
            _data.DeepSeekApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
        if (baseUrl != null)
            _data.DeepSeekBaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.TrimEnd('/');
        if (model != null)
            _data.DeepSeekModel = string.IsNullOrWhiteSpace(model) ? null : model;
        Persist();
    }

    public void ClearToken()
    {
        _data.Token    = null;
        _data.Username = null;
        Persist();
    }

    public void SaveChatSessionId(int sessionId)
    {
        _data.LastChatSessionId = sessionId;
        Persist();
    }

    public void SaveTavilyConfig(string? apiKey)
    {
        _data.TavilyApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
        Persist();
    }

    public void SaveSmtpConfig(string? host = null, int? port = null, string? user = null,
        string? password = null, string? fromAddress = null, string? fromName = null)
    {
        if (host != null)        _data.SmtpHost = string.IsNullOrWhiteSpace(host) ? null : host;
        if (port != null)        _data.SmtpPort = port.Value;
        if (user != null)        _data.SmtpUser = string.IsNullOrWhiteSpace(user) ? null : user;
        if (password != null)    _data.SmtpPassword = string.IsNullOrWhiteSpace(password) ? null : password;
        if (fromAddress != null) _data.SmtpFromAddress = string.IsNullOrWhiteSpace(fromAddress) ? null : fromAddress;
        if (fromName != null)    _data.SmtpFromName = string.IsNullOrWhiteSpace(fromName) ? null : fromName;
        Persist();
    }

    public bool HasToken => !string.IsNullOrWhiteSpace(_data.Token);

    private void Persist()
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigFile, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class SessionData
    {
        public string? Token    { get; set; }
        public string? ApiBase  { get; set; }
        public string? Username { get; set; }
        public int?    LastChatSessionId { get; set; }
        public string? DeepSeekApiKey  { get; set; }
        public string? DeepSeekBaseUrl { get; set; }
        public string? DeepSeekModel   { get; set; }
        public string? TavilyApiKey    { get; set; }
        public string? SmtpHost        { get; set; }
        public int     SmtpPort        { get; set; }
        public string? SmtpUser        { get; set; }
        public string? SmtpPassword    { get; set; }
        public string? SmtpFromAddress { get; set; }
        public string? SmtpFromName    { get; set; }
    }
}

