using System.Security.Cryptography;

namespace KeyNest.Core.Security;

/// <summary>
/// データファイルの鍵を別の秘密（Windows Hello の署名など）で包む。
/// 形式: nonce (12) | tag (16) | 暗号文。
/// </summary>
public static class KeyWrap
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int WrappingKeySize = 32;
    private static ReadOnlySpan<byte> Info => "KeyNest key wrap v1"u8;

    /// <summary>秘密（任意長）から包むための鍵を HKDF で作る。</summary>
    public static byte[] DeriveWrappingKey(ReadOnlySpan<byte> secret)
    {
        var key = new byte[WrappingKeySize];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, key, salt: [], info: Info);
        return key;
    }

    public static byte[] Wrap(ReadOnlySpan<byte> key, ReadOnlySpan<byte> wrappingKey)
    {
        var output = new byte[NonceSize + TagSize + key.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(wrappingKey, TagSize);
        aes.Encrypt(nonce, key, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize), Info);
        return output;
    }

    /// <summary>取り出す。包むための鍵が違う・データが壊れている場合は <see cref="InvalidPasswordException"/>。</summary>
    public static byte[] Unwrap(ReadOnlySpan<byte> wrapped, ReadOnlySpan<byte> wrappingKey)
    {
        if (wrapped.Length <= NonceSize + TagSize)
            throw new InvalidPasswordException();
        var key = new byte[wrapped.Length - NonceSize - TagSize];
        using var aes = new AesGcm(wrappingKey, TagSize);
        try
        {
            aes.Decrypt(wrapped[..NonceSize], wrapped[(NonceSize + TagSize)..], wrapped.Slice(NonceSize, TagSize), key, Info);
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new InvalidPasswordException();
        }
        return key;
    }
}
