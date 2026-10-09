namespace MiraiNote.Shared.Dtos.Welcome;

public class WelcomePhraseDto
{
    public int Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? Source { get; set; }
    public string? Period { get; set; }
    public string? Special { get; set; }
    public string? Season { get; set; }
    public bool IsEnabled { get; set; }
    public int SortOrder { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class WelcomePhraseWriteRequest
{
    public string Kind { get; set; } = "greeting";
    public string Text { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? Source { get; set; }
    public string? Period { get; set; }
    public string? Special { get; set; }
    public string? Season { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
}

public class WelcomePhraseEnabledRequest
{
    public bool IsEnabled { get; set; }
}
