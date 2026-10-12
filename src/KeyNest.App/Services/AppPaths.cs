using System.IO;

namespace KeyNest.App.Services;

public static class AppPaths
{
    /// <summary>exe と同じフォルダに "portable" ファイルがあればポータブルモード（USB メモリ運用）。</summary>
    public static bool IsPortable => File.Exists(Path.Combine(AppContext.BaseDirectory, "portable"));

    public static string DataDirectory => IsPortable
        ? Path.Combine(AppContext.BaseDirectory, "data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyNest");

    public static string DefaultVaultPath => Path.Combine(DataDirectory, "vault.knv");

    /// <summary>ロック解除前に必要なアプリ設定（暗号化しない）。</summary>
    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
}
