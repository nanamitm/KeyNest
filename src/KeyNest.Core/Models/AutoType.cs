namespace KeyNest.Core.Models;

/// <summary>
/// 名前付きの貼り付けタイプ（自動入力の手順）。各エントリはこの一覧から選ぶ。
/// 一覧の先頭が、タイプを指定していないエントリの既定になる。
/// </summary>
public sealed class AutoTypeTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";

    /// <summary>コマンドのキー（<see cref="AutoTypeCommands"/>）の並び。</summary>
    public List<string> Commands { get; set; } = [];
}

/// <summary>貼り付けタイプで使えるコマンド。キーはデータファイルに保存されるので変更しないこと。</summary>
public static class AutoTypeCommands
{
    public const string Title = "TITLE";
    public const string Url = "URL";
    public const string Email = "EMAIL";
    public const string File = "FILE";
    public const string Serial = "SERIAL";
    public const string Tab = "TAB";
    public const string Enter = "ENTER";
    public const string ShiftTab = "SHIFT+TAB";
    public const string Space = "SPACE";
    public const string Right = "RIGHT";
    public const string Left = "LEFT";
    public const string Up = "UP";
    public const string Down = "DOWN";
    public const string Home = "HOME";
    public const string End = "END";
    public const string ShiftHome = "SHIFT+HOME";
    public const string ShiftEnd = "SHIFT+END";
    public const string Delete = "DELETE";

    /// <summary>エントリの URL をブラウザで開く。</summary>
    public const string OpenUrl = "OPEN_URL";

    /// <summary><see cref="VaultSettings.AutoTypeDelayMs"/> だけ待つ。</summary>
    public const string Delay = "DELAY";

    /// <summary>個人情報の値（key は <see cref="PersonalInfoFields"/> のキー）。</summary>
    public static string Personal(string key) => $"P:{key}";

    /// <summary>個人情報のコマンドなら、そのキーを返す。</summary>
    public static string? PersonalKeyOf(string command) => command.StartsWith("P:", StringComparison.Ordinal) ? command[2..] : null;

    /// <summary>欄の値（index は 0〜3）。</summary>
    public static string FieldValue(int index) => $"F{index + 1}";

    /// <summary>欄の項目名（index は 0〜3）。</summary>
    public static string FieldLabel(int index) => $"F{index + 1}:LABEL";

    /// <summary>編集画面に並べる順番と表示名。</summary>
    public static IReadOnlyList<(string Key, string Name)> Catalog { get; } =
    [
        (Title, "タイトル"),
        (FieldValue(0), "欄1の値"),
        (FieldLabel(0), "欄1の項目名"),
        (FieldValue(1), "欄2の値"),
        (FieldLabel(1), "欄2の項目名"),
        (FieldValue(2), "欄3の値"),
        (FieldLabel(2), "欄3の項目名"),
        (FieldValue(3), "欄4の値"),
        (FieldLabel(3), "欄4の項目名"),
        (Url, "URL"),
        (Email, "メール"),
        (File, "ファイル"),
        (Serial, "シリアル番号"),
        (Tab, "[TAB]"),
        (Enter, "[ENTER]"),
        (ShiftTab, "[SHIFT+TAB]"),
        (Space, "[SPACE]"),
        (Right, "[→]"),
        (Left, "[←]"),
        (Up, "[↑]"),
        (Down, "[↓]"),
        (Home, "[HOME]"),
        (End, "[END]"),
        (ShiftHome, "[SHIFT+HOME]"),
        (ShiftEnd, "[SHIFT+END]"),
        (Delete, "[DELETE]"),
        (OpenUrl, "ブラウザで URL を開く"),
        (Delay, "待機"),
        .. PersonalInfoFields.All.Select(f => (Personal(f.Key), $"個人情報: {f.Name}")),
    ];

    public static string DisplayName(string key) =>
        Catalog.FirstOrDefault(c => c.Key == key).Name ?? $"（不明: {key}）";
}
