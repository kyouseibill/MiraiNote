using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MiraiNote.Shared.Common;

namespace MiraiNote.Core.Services.Household;

public interface IHouseholdSecretProtector
{
    bool IsConfigured { get; }
    string Protect(string plaintext);
    string Unprotect(string protectedPayload);
}

/// <summary>
/// 用配置里的密钥做 AES-GCM。密钥不写死；没配置时不能保存 Bark 地址。
/// 密文和明文都不要写进日志。
/// </summary>
public sealed class HouseholdSecretProtector : IHouseholdSecretProtector
{
    private const string Purpose = "MiraiNote.Household.BarkAddress.v1";
    private readonly byte[]? _key;

    public HouseholdSecretProtector(IOptions<HouseholdOptions> options)
    {
        var material = options.Value.Notifications.ProtectionKey;
        if (string.IsNullOrWhiteSpace(material))
        {
            _key = null;
            return;
        }

        _key = SHA256.HashData(Encoding.UTF8.GetBytes(Purpose + "\n" + material.Trim()));
    }

    public bool IsConfigured => _key != null;

    public string Protect(string plaintext)
    {
        var key = RequireKey();
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16))
        {
            aes.Encrypt(nonce, plain, cipher, tag);
        }

        var payload = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, payload, nonce.Length + tag.Length, cipher.Length);
        return Convert.ToBase64String(payload);
    }

    public string Unprotect(string protectedPayload)
    {
        var key = RequireKey();
        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(protectedPayload);
        }
        catch (FormatException)
        {
            throw new BusinessException("Bark 地址无法读取，请重新填写", 400);
        }

        if (payload.Length < 12 + 16)
            throw new BusinessException("Bark 地址无法读取，请重新填写", 400);

        var nonce = payload.AsSpan(0, 12);
        var tag = payload.AsSpan(12, 16);
        var cipher = payload.AsSpan(28);
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, cipher, tag, plain);
        }
        catch (CryptographicException)
        {
            throw new BusinessException("Bark 地址无法读取，请重新填写", 400);
        }

        return Encoding.UTF8.GetString(plain);
    }

    private byte[] RequireKey()
    {
        if (_key == null)
            throw new BusinessException("服务器未配置通知加密密钥，暂时不能保存 Bark 地址", 400);
        return _key;
    }
}
