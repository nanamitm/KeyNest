using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using KeyNest.Core.Security;

namespace KeyNest.App.Services;

/// <summary>
/// アプリ全体の設定。ロック解除前（起動時のトレイ格納・ホットキー）にも必要なので、
/// データファイルとは別に暗号化せず保存する。パスワード等の秘密情報は置かないこと。
/// </summary>
public sealed class AppSettings
{
    /// <summary>タスクトレイに常駐し、閉じる・最小化でトレイに格納する。</summary>
    public bool TrayEnabled { get; set; }

    /// <summary>起動時にウィンドウを出さずトレイに格納する。</summary>
    public bool StartInTray { get; set; }

    /// <summary>トレイから復元するときにマスターパスワードを求める。</summary>
    public bool RequirePasswordOnRestore { get; set; }

    /// <summary>上記でパスワードが必要な間は、トレイ・ホットキーのメニューから値を取り出せない。</summary>
    public bool BlockMenuWhileProtected { get; set; } = true;

    public bool HotkeyEnabled { get; set; }
    public ModifierKeys HotkeyModifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Alt;
    public Key HotkeyKey { get; set; } = Key.I;

    /// <summary>ホットキーのメニューで選んだ値を、直前のウィンドウに貼り付ける（false ならコピーのみ）。</summary>
    public bool PasteFromHotkeyMenu { get; set; } = true;

    /// <summary>メイン画面から自動入力するときに KeyNest を最小化する（false なら表示したまま入力先へ切り替える）。</summary>
    public bool MinimizeOnAutoType { get; set; } = true;

    /// <summary>パスワード生成の条件（前回使った条件で開く）。</summary>
    public PasswordOptions PasswordGenerator { get; set; } = new();

    /// <summary>メイン画面のサイズ・位置を保存して次回復元する。</summary>
    public bool SaveWindowPlacement { get; set; } = true;

    /// <summary>前回終了時のメイン画面の配置（未保存なら null）。</summary>
    public WindowPlacement? MainWindowPlacement { get; set; }

    [JsonIgnore]
    public string HotkeyText => FormatHotkey(HotkeyModifiers, HotkeyKey);

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public static string FormatHotkey(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyText(key));
        return string.Join("+", parts);
    }

    public static string KeyText(Key key) => key is >= Key.D0 and <= Key.D9 ? ((char)('0' + (key - Key.D0))).ToString() : key.ToString();
}

/// <summary>ウィンドウの配置。座標・サイズは WPF の論理単位（DIP）。</summary>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized, double TreeWidth);

/// <summary>settings.json の読み書き。変更時に <see cref="Changed"/> を通知する。</summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    private AppSettingsStore(string path, AppSettings current)
    {
        _path = path;
        Current = current;
    }

    public AppSettings Current { get; private set; }

    public event EventHandler? Changed;

    /// <summary>読めない・壊れている場合は既定値で起動する（設定ファイルは秘密情報を含まないため）。</summary>
    public static AppSettingsStore Load(string path)
    {
        AppSettings? settings = null;
        try
        {
            if (File.Exists(path))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return new AppSettingsStore(path, settings ?? new AppSettings());
    }

    public void Update(AppSettings settings)
    {
        Current = settings;
        try
        {
            Save();
        }
        finally
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// ウィンドウ配置のように頻繁に変わる値を保存する。<see cref="Changed"/> は通知しない
    /// （ホットキーの再登録などを起こさないため）。保存に失敗しても動作に支障はないので無視する。
    /// </summary>
    public void SaveQuietly(Action<AppSettings> update)
    {
        update(Current);
        try
        {
            Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Current, Options));
        File.Move(temp, _path, overwrite: true);
    }
}
