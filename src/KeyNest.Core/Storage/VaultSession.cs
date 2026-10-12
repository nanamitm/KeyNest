using KeyNest.Core.Models;
using KeyNest.Core.Security;

namespace KeyNest.Core.Storage;

/// <summary>
/// ロック解除中の保存ファイル。平文の <see cref="Vault"/> と鍵を保持し、保存を担当する。
/// </summary>
public sealed class VaultSession : IDisposable
{
    private VaultKey _key;

    public string FilePath { get; }
    public Vault Vault { get; }

    private VaultSession(string filePath, Vault vault, VaultKey key)
    {
        FilePath = filePath;
        Vault = vault;
        _key = key;
    }

    public static VaultSession Create(string filePath, string masterPassword, Vault? vault = null)
    {
        if (File.Exists(filePath))
            throw new IOException($"ファイルが既に存在します: {filePath}");

        var session = new VaultSession(filePath, vault ?? Vault.CreateDefault(), VaultKey.CreateNew(masterPassword));
        session.Save();
        return session;
    }

    public static VaultSession Open(string filePath, string masterPassword)
    {
        var data = File.ReadAllBytes(filePath);
        var header = VaultCrypto.ReadHeader(data);
        return Open(filePath, data, VaultKey.Derive(masterPassword, header.Salt, header.Iterations));
    }

    /// <summary>
    /// 生の鍵で開く（Windows Hello でのロック解除）。マスターパスワードが変更されていると
    /// ソルトが変わっているため <see cref="InvalidPasswordException"/> になる。
    /// </summary>
    public static VaultSession OpenWithKey(string filePath, ReadOnlySpan<byte> rawKey)
    {
        var data = File.ReadAllBytes(filePath);
        var header = VaultCrypto.ReadHeader(data);
        return Open(filePath, data, VaultKey.FromRawKey(rawKey, header.Salt, header.Iterations));
    }

    private static VaultSession Open(string filePath, byte[] data, VaultKey key)
    {
        try
        {
            var plaintext = VaultCrypto.Decrypt(data, key);
            return new VaultSession(filePath, VaultSerializer.Deserialize(plaintext), key);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 一時ファイルに書いてから置き換える。既存ファイルは .bak として1世代残す。
    /// </summary>
    public void Save()
    {
        var plaintext = VaultSerializer.Serialize(Vault);
        var encrypted = VaultCrypto.Encrypt(plaintext, _key);

        var dir = Path.GetDirectoryName(Path.GetFullPath(FilePath))!;
        Directory.CreateDirectory(dir);
        var tempPath = FilePath + ".tmp";

        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(encrypted);
            fs.Flush(flushToDisk: true);
        }

        if (File.Exists(FilePath))
            File.Replace(tempPath, FilePath, FilePath + ".bak");
        else
            File.Move(tempPath, FilePath);
    }

    /// <summary>
    /// ロック解除中のまま本人確認する（トレイからの復元時など）。ファイルは読み直さず、保持している鍵と比較する。
    /// </summary>
    public bool VerifyPassword(string password)
    {
        using var candidate = VaultKey.Derive(password, _key.Salt, _key.Iterations);
        return candidate.Matches(_key);
    }

    /// <summary>Windows Hello 登録用に、いまの鍵のコピーを返す。使い終わったら消去すること。</summary>
    public byte[] ExportKey() => _key.ExportKeyBytes();

    /// <summary>
    /// 新しいマスターパスワードで保存し直す（未保存の変更も含む）。保存に失敗したら元の鍵に戻す。
    /// </summary>
    public void ChangeMasterPassword(string newPassword)
    {
        var oldKey = _key;
        _key = VaultKey.CreateNew(newPassword);
        try
        {
            Save();
        }
        catch
        {
            _key.Dispose();
            _key = oldKey;
            throw;
        }
        oldKey.Dispose();
        // .bak は直前のファイル（古いパスワードで開ける）になるので、もう一度保存して新しいパスワードのものに置き換える。
        // パスワードが漏れた可能性があって変更する場合に、古いパスワードでデータを開けるファイルを残さないため。
        Save();
    }

    public void Dispose() => _key.Dispose();
}
