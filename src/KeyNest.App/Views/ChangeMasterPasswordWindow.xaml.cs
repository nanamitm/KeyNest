using System.IO;
using System.Windows;
using System.Windows.Controls;
using KeyNest.App.Services;
using KeyNest.Core.Storage;

namespace KeyNest.App.Views;

/// <summary>
/// マスターパスワードの変更。変更はその場でデータファイルに保存する（設定画面の OK を待たない）。
/// Windows Hello の登録は古い鍵に結び付いているので、変更後に消して、登録し直すかを確認する。
/// </summary>
public partial class ChangeMasterPasswordWindow : Window
{
    /// <summary>これより短いと確認を出す。</summary>
    private const int RecommendedMinLength = 12;

    private readonly VaultSession _session;
    private readonly VaultHelloController _hello;
    private readonly bool _helloEnrolled;

    public ChangeMasterPasswordWindow(VaultSession session, VaultHelloController hello)
    {
        InitializeComponent();
        _session = session;
        _hello = hello;
        _helloEnrolled = hello.IsEnrolled;
        HelloNoteText.Visibility = _helloEnrolled ? Visibility.Visible : Visibility.Collapsed;
        UpdateStrength();
        Loaded += (_, _) => CurrentInput.Focus();
    }

    /// <summary>変更の処理中（画面を無効にしている間）は閉じない。自動ロックはこの画面が閉じるまで待つ。</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!IsEnabled) e.Cancel = true;
        base.OnClosing(e);
    }

    private void NewInput_PasswordChanged(object sender, RoutedEventArgs e) => UpdateStrength();

    /// <summary>長さと文字種の数による簡単な目安（厳密な強度評価ではない）。</summary>
    private void UpdateStrength()
    {
        var password = NewInput.Password;
        if (password.Length == 0)
        {
            StrengthText.Text = $"{RecommendedMinLength} 文字以上で、英大文字・小文字・数字・記号を混ぜるのがおすすめです。";
            return;
        }
        var kinds = new[] { password.Any(char.IsUpper), password.Any(char.IsLower), password.Any(char.IsDigit), password.Any(c => !char.IsLetterOrDigit(c)) }
            .Count(b => b);
        var level = password.Length >= 16 && kinds >= 3 ? "強い"
            : password.Length >= RecommendedMinLength && kinds >= 2 ? "普通"
            : "弱い";
        StrengthText.Text = $"強さの目安: {level}（{password.Length} 文字・{kinds} 種類の文字）";
    }

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        var current = CurrentInput.Password;
        var password = NewInput.Password;
        if (current.Length == 0 || password.Length == 0)
        {
            ShowError("今のパスワードと新しいパスワードを入力してください。", CurrentInput.Password.Length == 0 ? CurrentInput : NewInput);
            return;
        }
        if (password != ConfirmInput.Password)
        {
            ShowError("確認用のパスワードが一致しません。", ConfirmInput);
            return;
        }
        if (password == current)
        {
            ShowError("新しいパスワードが今のパスワードと同じです。", NewInput);
            return;
        }
        if (password.Length < RecommendedMinLength
            && MessageDialog.Show(this, $"新しいパスワードが {RecommendedMinLength} 文字未満です。このまま変更しますか？", "確認",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;
        bool verified;
        try
        {
            // 鍵導出は数百ミリ秒かかるので UI スレッドを止めない
            verified = await Task.Run(() =>
            {
                if (!_session.VerifyPassword(current)) return false;
                _session.ChangeMasterPassword(password);
                return true;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            IsEnabled = true;
            ShowError($"保存できなかったため、変更しませんでした。\n{ex.Message}", NewInput);
            return;
        }
        IsEnabled = true;
        if (!verified)
        {
            CurrentInput.Clear();
            ShowError("今のマスターパスワードが違います。", CurrentInput);
            return;
        }

        if (_helloEnrolled)
            await ReenrollHelloAsync();
        DialogResult = true;
    }

    private async Task ReenrollHelloAsync()
    {
        await _hello.UnenrollAsync();
        if (MessageDialog.Show(this, "マスターパスワードを変更したため、Windows Hello の登録を解除しました。\n今の PC で Windows Hello に登録し直しますか？",
                "Windows Hello", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await _hello.EnrollAsync();
        }
        catch (HelloCancelledException)
        {
            // 登録しないまま閉じる。設定画面からいつでも登録できる
        }
        catch (Exception ex) when (ex is HelloException or IOException or UnauthorizedAccessException)
        {
            MessageDialog.Show(this, $"Windows Hello に登録できませんでした。設定画面から登録し直してください。\n{ex.Message}",
                "Windows Hello", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowError(string message, PasswordBox focus)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        focus.Focus();
    }
}
