using System.Security.Cryptography;

namespace KeyNest.Core.Security;

/// <summary>
/// マスターパスワードから導出した暗号鍵。
/// 鍵導出は重いので、ロック解除中はこのインスタンスを保持して保存のたびに再利用する。
/// </summary>
public sealed class VaultKey : IDisposable
{
    public const int SaltSize = 16;
    public const int KeySize = 32;
    public const int DefaultIterations = 600_000;

    private readonly byte[] _key;
    private bool _disposed;

    public byte[] Salt { get; }
    public int Iterations { get; }

    private VaultKey(byte[] key, byte[] salt, int iterations)
    {
        _key = key;
        Salt = salt;
        Iterations = iterations;
    }

    /// <summary>新しいソルトで鍵を作る（新規作成・マスターパスワード変更時）。</summary>
    public static VaultKey CreateNew(string password, int iterations = DefaultIterations) =>
        Derive(password, RandomNumberGenerator.GetBytes(SaltSize), iterations);

    /// <summary>
    /// 既存ファイルのソルト・反復回数から鍵を再導出する。
    /// TODO: Argon2id への移行時はファイルヘッダーの KDF ID で分岐する。
    /// </summary>
    public static VaultKey Derive(string password, byte[] salt, int iterations)
    {
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeySize);
        return new VaultKey(key, salt, iterations);
    }

    /// <summary>
    /// 保存しておいた生の鍵から作る（Windows Hello で取り出した鍵など）。
    /// ソルト・反復回数はファイルヘッダーの値を渡す。
    /// </summary>
    public static VaultKey FromRawKey(ReadOnlySpan<byte> key, byte[] salt, int iterations)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"鍵の長さは {KeySize} バイトです。", nameof(key));
        return new VaultKey(key.ToArray(), salt, iterations);
    }

    /// <summary>生の鍵のコピー。呼び出し側は使い終わったら <see cref="CryptographicOperations.ZeroMemory"/> で消すこと。</summary>
    public byte[] ExportKeyBytes() => KeyBytes.ToArray();

    internal ReadOnlySpan<byte> KeyBytes
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _key;
        }
    }

    /// <summary>同じ鍵か（タイミング攻撃を避けるため固定時間で比較）。</summary>
    public bool Matches(VaultKey other) => CryptographicOperations.FixedTimeEquals(KeyBytes, other.KeyBytes);

    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(_key);
        _disposed = true;
    }
}
