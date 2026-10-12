using System.Windows;
using KeyNest.App.Views;
using KeyNest.Core.Import;
using KeyNest.Core.Models;

namespace KeyNest.App.Services;

public interface IDialogService
{
    string? Prompt(string title, string label, string initialValue);
    bool Confirm(string message);
    void ShowError(string message);

    /// <summary>ファイルを選ぶ。キャンセルなら null。<paramref name="filter"/> は OpenFileDialog の形式。</summary>
    string? PickFile(string initialPath, string filter = "すべてのファイル (*.*)|*.*");

    /// <summary>貼り付けタイプの編集。OK なら <paramref name="settings"/> を書き換えて true。</summary>
    bool EditAutoTypes(VaultSettings settings);

    /// <summary>個人情報の編集。OK で内容が変わったら true。</summary>
    bool EditPersonalInfo(PersonalInfo personal);

    /// <summary>書き出しの確認（形式・範囲の選択と本人確認）。キャンセルなら null。</summary>
    ExportChoice? ConfirmExport(string? selectedGroupName);

    /// <summary>保存先のファイルを選ぶ。キャンセルなら null。</summary>
    string? PickSaveFile(string defaultFileName, string filter);

    /// <summary>ID Manager の XML を取り込む前の確認。キャンセルなら null。</summary>
    ImportChoice? ConfirmImport(IdManagerXmlDocument document, string fileName, bool hasPersonalInfo);

    /// <summary>
    /// 設定画面を開く。キャンセルなら null、OK なら <paramref name="settings"/>（データファイル側）が
    /// 変わったかどうかを返す。アプリ設定（トレイ・ホットキー）はこの中で保存する。
    /// </summary>
    bool? EditSettings(VaultSettings settings, Func<bool> changeMasterPassword);

    /// <summary>
    /// パスワードの生成画面を開く。「設定」で閉じたら生成した値、それ以外は null。
    /// <paramref name="copy"/> は画面の「コピー」ボタンで呼ばれる。
    /// </summary>
    string? GeneratePassword(string targetLabel, Action<string> copy);

    /// <summary>マスターパスワードの変更。変更して保存したら true。</summary>
    bool ChangeMasterPassword();
}

public enum ExportFormat
{
    Csv,
    IdManagerXml,
}

/// <summary>書き出し方の選択。</summary>
public sealed record ExportChoice(ExportFormat Format, bool SelectedGroupOnly, bool IncludePersonalInfo);

/// <summary>取り込み方の選択。</summary>
public sealed record ImportChoice(string GroupName, bool ImportDates, bool ApplyFieldDefaults, bool ImportPersonalInfo);

public sealed class DialogService(Window owner, AppSettingsStore appSettings, VaultHelloController hello,
    KeyNest.Core.Storage.VaultSession session) : IDialogService
{
    public string? Prompt(string title, string label, string initialValue)
    {
        var dialog = new InputDialog(title, label, initialValue) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    public bool Confirm(string message) =>
        MessageDialog.Show(owner, message, "確認", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowError(string message) =>
        MessageDialog.Show(owner, message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);

    public string? PickFile(string initialPath, string filter = "すべてのファイル (*.*)|*.*")
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "ファイルの選択", CheckFileExists = true, Filter = filter };
        var current = initialPath.Trim().Trim('"');
        if (current.Length > 0)
        {
            try
            {
                if (System.IO.Path.GetDirectoryName(current) is { Length: > 0 } dir && System.IO.Directory.Exists(dir))
                {
                    dialog.InitialDirectory = dir;
                    dialog.FileName = System.IO.Path.GetFileName(current);
                }
            }
            catch (ArgumentException)
            {
                // パスとして不正な文字列なら初期位置は指定しない
            }
        }
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public ExportChoice? ConfirmExport(string? selectedGroupName)
    {
        var dialog = new ExportWindow(session, selectedGroupName) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Choice : null;
    }

    public string? PickSaveFile(string defaultFileName, string filter)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "書き出し先の選択",
            FileName = defaultFileName,
            Filter = filter,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public bool EditAutoTypes(VaultSettings settings) =>
        new AutoTypeEditorWindow(settings) { Owner = owner }.ShowDialog() == true;

    public bool EditPersonalInfo(PersonalInfo personal)
    {
        var dialog = new PersonalInfoWindow(personal) { Owner = owner };
        return dialog.ShowDialog() == true && dialog.Changed;
    }

    public ImportChoice? ConfirmImport(IdManagerXmlDocument document, string fileName, bool hasPersonalInfo)
    {
        var dialog = new ImportWindow(document, fileName, hasPersonalInfo) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Choice : null;
    }

    public string? GeneratePassword(string targetLabel, Action<string> copy)
    {
        var dialog = new PasswordGeneratorWindow(targetLabel, appSettings.Current.PasswordGenerator, copy) { Owner = owner };
        var applied = dialog.ShowDialog() == true;
        appSettings.SaveQuietly(s => s.PasswordGenerator = dialog.Options); // 閉じ方に関係なく条件は覚えておく
        return applied ? dialog.Result : null;
    }

    public bool ChangeMasterPassword()
    {
        // 設定画面から開いたときは設定画面の上に出す
        var parent = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? owner;
        return new ChangeMasterPasswordWindow(session, hello) { Owner = parent }.ShowDialog() == true;
    }

    public bool? EditSettings(VaultSettings settings, Func<bool> changeMasterPassword)
    {
        var dialog = new SettingsWindow(settings, appSettings.Current.Clone(), hello, changeMasterPassword) { Owner = owner };
        if (dialog.ShowDialog() != true) return null;
        try
        {
            appSettings.Update(dialog.AppSettings);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            ShowError($"アプリ設定を保存できませんでした。今回の起動中のみ有効です。\n{ex.Message}");
        }
        return dialog.VaultSettingsChanged;
    }
}
