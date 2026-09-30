using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>
/// Chat 里待确认的家务完成草稿。工具只创建草稿，确认接口过期前才写入完成记录。
/// </summary>
[Table("HouseholdChatDraft")]
public class HouseholdChatDraft : BaseEntity
{
    [Key]
    public int Id { get; set; }

    public int UserId { get; set; }

    public int HouseholdId { get; set; }

    /// <summary>产生这条草稿的对话。临时聊天不记。为空的旧草稿无法在刷新后恢复。</summary>
    public int? ChatSessionId { get; set; }

    public DateOnly CompletedOn { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? Cost { get; set; }

    /// <summary>这次允许确认的事项 Id，JSON 数组。</summary>
    [MaxLength(2000)]
    public string CandidateItemIds { get; set; } = "[]";

    public DateTime ExpiresAt { get; set; }

    [MaxLength(HouseholdIdempotency.KeyMaxLength)]
    public string? IdempotencyKey { get; set; }

    public int? StoredItemId { get; set; }

    public DateOnly? StoredCompletedOn { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? StoredCost { get; set; }

    public bool StoredSkipDeduction { get; set; }
}
