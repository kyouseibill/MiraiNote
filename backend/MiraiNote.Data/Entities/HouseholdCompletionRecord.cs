using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 事项完成记录。照片只保存生活记录上传接口返回的相对路径，不在这里接收文件。
/// </summary>
[Table("HouseholdCompletionRecord")]
public class HouseholdCompletionRecord : BaseEntity
{
    [Key]
    public int Id { get; set; }

    public int HouseholdItemId { get; set; }

    [ForeignKey(nameof(HouseholdItemId))]
    public HouseholdItem? Item { get; set; }

    public DateOnly CompletedOn { get; set; }

    /// <summary>
    /// 可空外键：成员软删除后，全局过滤器不能把完成记录一起滤掉。
    /// 写入时总会赋值；展示用 <see cref="CompletedByUsername"/> 快照。
    /// </summary>
    public int? CompletedByMemberId { get; set; }

    [ForeignKey(nameof(CompletedByMemberId))]
    public HouseholdMember? CompletedBy { get; set; }

    /// <summary>完成时的用户 Id，成员软删除后仍可追溯。</summary>
    public int CompletedByUserId { get; set; }

    /// <summary>完成时的用户名快照。成员被移出家庭后历史仍显示执行人。</summary>
    [Required]
    [MaxLength(50)]
    public string CompletedByUsername { get; set; } = string.Empty;

    /// <summary>图片相对路径的 JSON 数组，来自 POST /api/v1/upload/image。</summary>
    public string? PhotoRefs { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? Cost { get; set; }

    [MaxLength(500)]
    public string? PurchaseLink { get; set; }

    [MaxLength(2000)]
    public string? Note { get; set; }

    public int? ConsumableId { get; set; }

    public int ConsumableQuantityDeducted { get; set; }

    /// <summary>本次扣减把库存打到 0，或请求的数量没能扣完。</summary>
    public bool NeedsRestock { get; set; }

    /// <summary>一次性到期事项续期时写入的新到期日。补记更早日期时只记在这条历史上。</summary>
    public DateOnly? NewExpiryDate { get; set; }

    /// <summary>发起完成的用户。幂等键按（用户、事项、键）区分，不是全家庭共用。</summary>
    public int? IdempotencyUserId { get; set; }

    /// <summary>可选的 Idempotency-Key。同一用户对同一事项重复使用同一键且请求体一致时返回第一次的结果。</summary>
    [MaxLength(HouseholdIdempotency.KeyMaxLength)]
    public string? IdempotencyKey { get; set; }

    /// <summary>请求体哈希。同一键配上不同内容时拒绝，而不是再执行一次。</summary>
    [MaxLength(64)]
    public string? RequestBodyHash { get; set; }

    /// <summary>完成请求指纹。短时间内相同指纹只接受一次，用来挡住连续点击。</summary>
    [MaxLength(64)]
    public string? SubmissionFingerprint { get; set; }
}
