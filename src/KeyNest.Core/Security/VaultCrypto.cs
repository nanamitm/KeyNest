using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KeyNest.Core.Security;

/// <summary>
/// 保存ファイルの暗号化形式（AES-256-GCM）。
///
/// レイアウト（リトルエンディアン）:
///   magic "KNST" (4) | format version u16 | kdf id u8 | iterations u32 | salt (16) | nonce (12)
///   | tag (16) | ciphertext
/// magic〜nonce をヘッダーとして AAD に含め、ヘッダーの改ざんも検出する。
/// </summary>
public static class VaultCrypto
{
    private static ReadOnlySpan<byte> Magic => "KNST"u8;
    public const ushort FormatVersion = 1;
    public const byte KdfPbkdf2Sha256 = 1;

    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 4 + 2 + 1 + 4 + VaultKey.SaltSize + NonceSize;

    public readonly record struct Header(ushort Version, byte KdfId, int Iterations, byte[] Salt);

    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, VaultKey key)
    {
        var output = new byte[HeaderSize + TagSize + plaintext.Length];
        var header = output.AsSpan(0, HeaderSize);

        Magic.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], FormatVersion);
        header[6] = KdfPbkdf2Sha256;
        BinaryPrimitives.WriteInt32LittleEndian(header[7..], key.Iterations);
        key.Salt.CopyTo(header[11..]);
        var nonce = header[(11 + VaultKey.SaltSize)..];
        RandomNumberGenerator.Fill(nonce);

        var tag = output.AsSpan(HeaderSize, TagSize);
        var ciphertext = output.AsSpan(HeaderSize + TagSize);

        using var aes = new AesGcm(key.KeyBytes, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, header);
        return output;
    }

    /// <summary>ファイル先頭のヘッダーだけを読む（鍵導出に必要なソルト等を得るため）。</summary>
    public static Header ReadHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize + TagSize || !data[..4].SequenceEqual(Magic))
            throw new InvalidVaultFileException("KeyNest のデータファイルではありません。");

        var version = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        if (version > FormatVersion)
            throw new InvalidVaultFileException($"新しい形式のファイルです (v{version})。アプリを更新してください。");

        var kdf = data[6];
        if (kdf != KdfPbkdf2Sha256)
            throw new InvalidVaultFileException($"未対応の鍵導出方式です (id={kdf})。");

        var iterations = BinaryPrimitives.ReadInt32LittleEndian(data[7..]);
        var salt = data.Slice(11, VaultKey.SaltSize).ToArray();
        return new Header(version, kdf, iterations, salt);
    }

    public static byte[] Decrypt(ReadOnlySpan<byte> data, VaultKey key)
    {
        ReadHeader(data);
        var header = data[..HeaderSize];
        var nonce = header[(11 + VaultKey.SaltSize)..];
        var tag = data.Slice(HeaderSize, TagSize);
        var ciphertext = data[(HeaderSize + TagSize)..];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key.KeyBytes, TagSize);
        try
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext, header);
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new InvalidPasswordException();
        }
        return plaintext;
    }
}

public class InvalidVaultFileException(string message) : Exception(message);

/// <summary>パスワード違い、またはファイルの破損・改ざん。GCM ではこの2つを区別できない。</summary>
public sealed class InvalidPasswordException()
    : Exception("マスターパスワードが違うか、ファイルが破損しています。");
