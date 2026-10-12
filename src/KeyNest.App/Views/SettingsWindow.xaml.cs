using System.Windows;
using System.Windows.Input;
using KeyNest.App.Services;
using KeyNest.Core.Models;

namespace KeyNest.App.Views;

/// <summary>
/// 設定画面。左列はデータファイル内の設定（<see cref="VaultSettings"/>）、右列はアプリ設定（<see cref="Services.AppSettings"/>）。
/// 編集用のコピーに対して操作し、OK のときだけ書き戻す。
/// </summary>
public partial class SettingsWindow : Window
{
    private const int MaxClipboardSeconds = 600;
    private const int MaxAutoLockMinutes = 1440;

    private static readonly Choice<AutoLockMode>[] AutoLockModeChoices =
    [
        new(AutoLockMode.Application, "KeyNest を操作していない時間"),
        new(AutoLockMode.System, "パソコンを操作していない時間"),
    ];

    private static readonly Choice<ModifierKeys>[] ModifierChoices =
        new[]
        {
            ModifierKeys.Control | ModifierKeys.Alt,
            ModifierKeys.Control | ModifierKeys.Shift,
            ModifierKeys.Alt | ModifierKeys.Shift,
            ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift,
            ModifierKeys.Windows | ModifierKeys.Alt,
            ModifierKeys.Windows | ModifierKeys.Shift,
            ModifierKeys.Control,
            ModifierKeys.Alt,
        }
        .Select(m => new Choice<ModifierKeys>(m, AppSettings.FormatHotkey(m, Key.None).Replace("+None", "")))
        .ToArray();

    private static readonly Choice<Key>[] KeyChoices =
        Enumerable.Range(0, 26).Select(i => Key.A + i)
            .Concat(Enumerable.Range(0, 10).Select(i => Key.D0 + i))
            .Concat(Enumerable.Range(0, 12).Select(i => Key.F1 + i))
            .Select(k => new Choice<Key>(k, AppSettings.KeyText(k)))
            .ToArray();

    private readonly VaultSettings _vaultSettings;
    private readonly VaultHelloController _hello;
    private readonly Func<bool> _changeMasterPassword;
    private List<FieldRow> _rows;

    public SettingsWindow(VaultSettings vaultSettings, AppSettings appSettings, VaultHelloController hello, Func<bool> changeMasterPassword)
    {
        InitializeComponent();
        _vaultSettings = vaultSettings;
        _hello = hello;
        _changeMasterPassword = changeMasterPassword;
        AppSettings = appSettings;
        Loaded += async (_, _) => await RefreshHelloAsync();

        _rows = ToRows(vaultSettings.FieldDefaults);
        FieldRows.ItemsSource = _rows;
        ClipboardSecondsBox.Text = vaultSettings.ClipboardClearSeconds.ToString();
        AutoLockBox.IsChecked = vaultSettings.AutoLockEnabled;
        AutoLockMinutesBox.Text = vaultSettings.AutoLockMinutes.ToString();
        AutoLockModeBox.ItemsSource = AutoLockModeChoices;
        AutoLockModeBox.SelectedItem = AutoLockModeChoices.FirstOrDefault(c => c.Value == vaultSettings.AutoLockMode) ?? AutoLockModeChoices[0];

        TrayEnabledBox.IsChecked = appSettings.TrayEnabled;
        StartInTrayBox.IsChecked = appSettings.StartInTray;
        RequirePasswordBox.IsChecked = appSettings.RequirePasswordOnRestore;
        BlockMenuBox.IsChecked = appSettings.BlockMenuWhileProtected;
        HotkeyEnabledBox.IsChecked = appSettings.HotkeyEnabled;
        PasteBox.IsChecked = appSettings.PasteFromHotkeyMenu;
        SavePlacementBox.IsChecked = appSettings.SaveWindowPlacement;
        MinimizeOnAutoTypeBox.IsChecked = appSettings.MinimizeOnAutoType;
        ModifierBox.ItemsSource = ModifierChoices;
        ModifierBox.SelectedItem = ModifierChoices.FirstOrDefault(c => c.Value == appSettings.HotkeyModifiers) ?? ModifierChoices[0];
        KeyBox.ItemsSource = KeyChoices;
        KeyBox.SelectedItem = KeyChoices.FirstOrDefault(c => c.Value == appSettings.HotkeyKey) ?? KeyChoices[0];
    }

    /// <summary>OK 後のアプリ設定（呼び出し側で保存する）。</summary>
    public AppSettings AppSettings { get; }

    /// <summary>データファイル側の設定が変わったか（未保存マークを付けるかの判断用）。</summary>
    public bool VaultSettingsChanged { get; private set; }

    private static List<FieldRow> ToRows(IEnumerable<FieldDefault> defaults) =>
        defaults.Select((d, i) => new FieldRow { Number = i + 1, Label = d.Label, IsMasked = d.IsMasked }).ToList();

    private void ResetFields_Click(object sender, RoutedEventArgs e)
    {
        _rows = ToRows(VaultSettings.CreateFieldDefaults());
        FieldRows.ItemsSource = _rows;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_rows.Any(r => string.IsNullOrWhiteSpace(r.Label)))
        {
            ShowError("項目名を空にすることはできません。");
            return;
        }
        if (!int.TryParse(ClipboardSecondsBox.Text, out var seconds) || seconds < 0 || seconds > MaxClipboardSeconds)
        {
            ShowError($"クリップボードの消去時間は 0〜{MaxClipboardSeconds} の整数で入力してください。");
            return;
        }

        if (!int.TryParse(AutoLockMinutesBox.Text, out var autoLockMinutes) || autoLockMinutes < 1 || autoLockMinutes > MaxAutoLockMinutes)
        {
            ShowError($"自動ロックまでの時間は 1〜{MaxAutoLockMinutes} 分の整数で入力してください。");
            return;
        }
        var autoLockEnabled = AutoLockBox.IsChecked == true;
        var autoLockMode = ((Choice<AutoLockMode>)AutoLockModeBox.SelectedItem).Value;

        var fieldDefaults = _rows.Select(r => new FieldDefault { Label = r.Label.Trim(), IsMasked = r.IsMasked }).ToList();
        VaultSettingsChanged = seconds != _vaultSettings.ClipboardClearSeconds
            || autoLockEnabled != _vaultSettings.AutoLockEnabled
            || autoLockMinutes != _vaultSettings.AutoLockMinutes
            || autoLockMode != _vaultSettings.AutoLockMode
            || !fieldDefaults.Select(f => (f.Label, f.IsMasked)).SequenceEqual(_vaultSettings.FieldDefaults.Select(f => (f.Label, f.IsMasked)));
        _vaultSettings.FieldDefaults = fieldDefaults;
        _vaultSettings.ClipboardClearSeconds = seconds;
        _vaultSettings.AutoLockEnabled = autoLockEnabled;
        _vaultSettings.AutoLockMinutes = autoLockMinutes;
        _vaultSettings.AutoLockMode = autoLockMode;

        AppSettings.TrayEnabled = TrayEnabledBox.IsChecked == true;
        AppSettings.StartInTray = StartInTrayBox.IsChecked == true;
        AppSettings.RequirePasswordOnRestore = RequirePasswordBox.IsChecked == true;
        AppSettings.BlockMenuWhileProtected = BlockMenuBox.IsChecked == true;
        AppSettings.HotkeyEnabled = HotkeyEnabledBox.IsChecked == true;
        AppSettings.HotkeyModifiers = ((Choice<ModifierKeys>)ModifierBox.SelectedItem).Value;
        AppSettings.HotkeyKey = ((Choice<Key>)KeyBox.SelectedItem).Value;
        AppSettings.PasteFromHotkeyMenu = PasteBox.IsChecked == true;
        AppSettings.SaveWindowPlacement = SavePlacementBox.IsChecked == true;
        AppSettings.MinimizeOnAutoType = MinimizeOnAutoTypeBox.IsChecked == true;
        if (!AppSettings.SaveWindowPlacement)
            AppSettings.MainWindowPlacement = null; // 保存しないなら前回の配置も残さない

        DialogResult = true;
    }

    private async void ChangeMasterPassword_Click(object sender, RoutedEventArgs e)
    {
        if (!_changeMasterPassword()) return;
        MasterPasswordText.Text = $"変更しました（{DateTime.Now:HH:mm} に保存）。";
        await RefreshHelloAsync(); // 変更で登録が解除・再登録されている
    }

    // ---- Windows Hello（OK を待たずにすぐ反映する） ----

    private async Task RefreshHelloAsync()
    {
        if (!await _hello.IsAvailableAsync())
        {
            HelloStatusText.Text = "この PC では Windows Hello が使えません。Windows の［設定］→［アカウント］→［サインイン オプション］で PIN などを設定してください。";
            HelloButton.Content = "登録する";
            HelloButton.IsEnabled = false;
            return;
        }
        var enrolled = _hello.IsEnrolled;
        HelloStatusText.Text = enrolled ? "登録済み：起動時やロック後に Windows Hello でロック解除できます。" : "未登録";
        HelloButton.Content = enrolled ? "登録を解除" : "登録する";
        HelloButton.IsEnabled = true;
    }

    private async void Hello_Click(object sender, RoutedEventArgs e)
    {
        HelloButton.IsEnabled = false;
        try
        {
            if (_hello.IsEnrolled)
            {
                await _hello.UnenrollAsync();
            }
            else
            {
                HelloStatusText.Text = "Windows Hello の画面で認証してください…";
                await _hello.EnrollAsync();
            }
            await RefreshHelloAsync();
        }
        catch (HelloCancelledException)
        {
            await RefreshHelloAsync();
        }
        catch (Exception ex) when (ex is HelloException or System.IO.IOException or UnauthorizedAccessException)
        {
            await RefreshHelloAsync();
            HelloStatusText.Text = ex.Message;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private sealed class FieldRow
    {
        public int Number { get; init; }
        public string Label { get; set; } = "";
        public bool IsMasked { get; set; }
    }

    private sealed record Choice<T>(T Value, string Text);
}
