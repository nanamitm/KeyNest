using System.IO;
using System.Security.Cryptography;
using System.Windows;
using KeyNest.App.Services;
using KeyNest.Core.Security;
using KeyNest.Core.Storage;

namespace KeyNest.App.Views;

/// <summary>
/// 保存ファイルが無ければ新規作成、あればロック解除する。
/// <c>verifySession</c> を渡すと、ロック解除中のセッションに対する本人確認（トレイからの表示時）になる。
/// Windows Hello が登録済みなら、表示直後に自動で Hello の認証を始める。
/// </summary>
public partial class UnlockWindow : Window
{
    private readonly string _vaultPath;
    private readonly bool _isNew;
    private readonly VaultSession? _verifySession;
    private readonly WindowsHelloService? _hello;
    private readonly HelloEnrollment? _enrollment;

    /// <param name="cancelExitsApp">キャンセルでアプリが終了するか（ボタン名を「終了」にする）。</param>
    public UnlockWindow(string vaultPath, bool cancelExitsApp = true, VaultSession? verifySession = null,
        WindowsHelloService? hello = null)
    {
        InitializeComponent();
        _vaultPath = vaultPath;
        _verifySession = verifySession;
        _isNew = verifySession is null && !File.Exists(vaultPath);
        _hello = hello;
        _enrollment = hello is not null && !_isNew ? HelloEnrollmentStore.Load(vaultPath) : null;

        HeadingText.Text = verifySession is not null ? "マスターパスワードの確認"
            : _isNew ? "新しいデータファイルを作成" : "ロック解除";
        PathText.Text = vaultPath;
        PathText.ToolTip = vaultPath;
        OkButton.Content = verifySession is not null ? "表示" : _isNew ? "作成" : "ロック解除";
        CancelButton.Content = cancelExitsApp ? "終了" : "キャンセル";
        ConfirmPanel.Visibility = _isNew ? Visibility.Visible : Visibility.Collapsed;
        HelloPanel.Visibility = _enrollment is not null ? Visibility.Visible : Visibility.Collapsed;
        HelloButtonText.Text = verifySession is not null ? "Windows Hello で確認" : "Windows Hello でロック解除";

        Loaded += (_, _) =>
        {
            Activate();
            PasswordInput.Focus();
        };
        // 描画が終わってから Hello を始める（ウィンドウが出る前に Hello の画面が出ないように）
        ContentRendered += async (_, _) =>
        {
            if (_enrollment is not null)
                await UnlockWithHelloAsync();
        };
    }

    public VaultSession? Session { get; private set; }

    private async void Hello_Click(object sender, RoutedEventArgs e) => await UnlockWithHelloAsync();

    private async Task UnlockWithHelloAsync()
    {
        if (_hello is null || _enrollment is null) return;

        IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;
        try
        {
            if (_verifySession is not null)
            {
                // ロック解除中なので鍵は不要。本人確認だけ行う
                await _hello.VerifyUserAsync("KeyNest を表示するには本人確認が必要です。");
                Session = _verifySession;
            }
            else
            {
                var rawKey = await _hello.UnlockAsync(_enrollment);
                try
                {
                    Session = await Task.Run(() => VaultSession.OpenWithKey(_vaultPath, rawKey));
                }
                catch (InvalidPasswordException)
                {
                    throw new HelloEnrollmentInvalidException(); // マスターパスワードが変更されている
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(rawKey);
                }
            }
            DialogResult = true;
        }
        catch (HelloCancelledException)
        {
            // マスターパスワードでの解除に切り替える
        }
        catch (HelloEnrollmentInvalidException ex)
        {
            HelloEnrollmentStore.Delete(_vaultPath);
            HelloPanel.Visibility = Visibility.Collapsed;
            ShowError(ex.Message);
        }
        catch (Exception ex) when (ex is HelloException or IOException or InvalidVaultFileException)
        {
            ShowError(ex.Message);
        }
        finally
        {
            IsEnabled = true;
        }
        if (DialogResult != true)
            PasswordInput.Focus();
    }

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        var password = PasswordInput.Password;
        if (password.Length == 0)
        {
            ShowError("マスターパスワードを入力してください。");
            return;
        }
        if (_isNew && password != ConfirmInput.Password)
        {
            ShowError("確認用のパスワードが一致しません。");
            return;
        }

        IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;
        var retry = false;
        try
        {
            // 鍵導出は数百ミリ秒かかるので UI スレッドを止めない
            Session = await Task.Run(() => Unlock(password));
            DialogResult = true;
        }
        catch (InvalidPasswordException ex)
        {
            ShowError(_verifySession is not null ? "マスターパスワードが違います。" : ex.Message);
            PasswordInput.Clear();
            retry = true;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            IsEnabled = true;
        }
        // 無効化中のコントロールにはフォーカスを移せないので、有効に戻してから移す
        if (retry)
            PasswordInput.Focus();
    }

    private VaultSession Unlock(string password)
    {
        if (_verifySession is not null)
            return _verifySession.VerifyPassword(password) ? _verifySession : throw new InvalidPasswordException();
        return _isNew ? VaultSession.Create(_vaultPath, password) : VaultSession.Open(_vaultPath, password);
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
