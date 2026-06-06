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
    }
}

