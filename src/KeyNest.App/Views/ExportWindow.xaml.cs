using System.Windows;
using KeyNest.App.Services;
using KeyNest.Core.Storage;

namespace KeyNest.App.Views;

/// <summary>
/// 書き出しの確認。書き出したファイルは暗号化されないので、注意を表示し、マスターパスワードで本人確認する
/// （席を外している間に第三者に書き出されないように）。
/// </summary>
public partial class ExportWindow : Window
{
    private readonly VaultSession _session;

    /// <param name="selectedGroupName">選択中のグループ（「すべて」以外）の名前。無ければ範囲はすべてのみ。</param>
    public ExportWindow(VaultSession session, string? selectedGroupName)
    {
        InitializeComponent();
        _session = session;
        GroupRadio.Content = selectedGroupName is null ? "選択中のグループのみ（グループが選ばれていません）" : $"選択中のグループ「{selectedGroupName}」のみ";
        GroupRadio.IsEnabled = selectedGroupName is not null;
        Loaded += (_, _) => PasswordInput.Focus();
    }

    public ExportChoice? Choice { get; private set; }

    /// <summary>個人情報は ID Manager の XML にだけ入れられる。</summary>
    private void Format_Changed(object sender, RoutedEventArgs e)
    {
        if (PersonalBox is null) return; // 初期化中
        PersonalBox.IsEnabled = XmlRadio.IsChecked == true;
        if (!PersonalBox.IsEnabled) PersonalBox.IsChecked = false;
    }

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        var password = PasswordInput.Password;
        if (password.Length == 0)
        {
            ShowError("マスターパスワードを入力してください。");
            return;
        }

        IsEnabled = false;
        var verified = await Task.Run(() => _session.VerifyPassword(password)); // 鍵導出は時間がかかる
        IsEnabled = true;
        if (!verified)
        {
            PasswordInput.Clear();
            ShowError("マスターパスワードが違います。");
            return;
        }

        Choice = new ExportChoice(
            XmlRadio.IsChecked == true ? ExportFormat.IdManagerXml : ExportFormat.Csv,
            GroupRadio.IsChecked == true,
            PersonalBox.IsChecked == true);
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        PasswordInput.Focus();
    }
}
