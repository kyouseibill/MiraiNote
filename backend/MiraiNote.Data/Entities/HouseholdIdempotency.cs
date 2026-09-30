namespace MiraiNote.Data.Entities;

/// <summary>
/// 家务完成幂等键。草稿和完成记录共用这一列长度，超长要在写入前拒绝。
/// </summary>
public static class HouseholdIdempotency
{
    public const int KeyMaxLength = 100;

    public static readonly string KeyTooLongMessage = "Idempotency-Key 最长 " + KeyMaxLength + " 个字符";
}
