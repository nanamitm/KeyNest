using System.Windows;
using System.Windows.Controls;

namespace KeyNest.App.Views;

/// <summary>
/// Fluent テーマ（ダークモード）に合わせたメッセージボックス。Windows 標準の MessageBox はテーマに追従しないため、
/// アプリ内ではこちらを使う。呼び出し方と戻り値は <see cref="MessageBox.Show(Window, string, string, MessageBoxButton, MessageBoxImage)"/> と同じ。
/// </summary>
public partial class MessageDialog : Window
{
    private MessageBoxResult _result;

    private MessageDialog(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        SetIcon(image);
        AddButtons(buttons);
    }

    public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image) =>
        Show(null, message, title, buttons, image);

    /// <summary>
    /// <paramref name="owner"/> が無い・表示されていない場合（トレイから出すときなど）は、画面中央に最前面で表示する。
    /// </summary>
    public static MessageBoxResult Show(Window? owner, string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        var dialog = new MessageDialog(message, title, buttons, image);
        if (owner is { IsVisible: true } && owner.WindowState != WindowState.Minimized)
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.Topmost = true;
        }
        dialog.ShowDialog();
        return dialog._result;
    }

    private void SetIcon(MessageBoxImage image)
    {
        // MessageBoxImage は同じ値に複数の名前がある（Error = Hand = Stop など）
        var (glyph, brushKey) = image switch
        {
            MessageBoxImage.Error => ("", "SystemFillColorCriticalBrush"),
            MessageBoxImage.Warning => ("", "SystemFillColorCautionBrush"),
            MessageBoxImage.Question => ("", "AccentTextFillColorPrimaryBrush"),
            MessageBoxImage.Information => ("", "AccentTextFillColorPrimaryBrush"),
            _ => ((string?)null, (string?)null),
        };
        if (glyph is null) return;
        IconText.Text = glyph;
        IconText.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        IconText.Visibility = Visibility.Visible;
    }

    private void AddButtons(MessageBoxButton buttons)
    {
        // 既定（Enter）は先頭のボタン、Esc はキャンセル（はい／いいえのときは「いいえ」）
        var specs = buttons switch
        {
            MessageBoxButton.OKCancel => new[] { ("OK", MessageBoxResult.OK), ("キャンセル", MessageBoxResult.Cancel) },
            MessageBoxButton.YesNo => new[] { ("はい(_Y)", MessageBoxResult.Yes), ("いいえ(_N)", MessageBoxResult.No) },
            MessageBoxButton.YesNoCancel => new[]
            {
                ("はい(_Y)", MessageBoxResult.Yes), ("いいえ(_N)", MessageBoxResult.No), ("キャンセル", MessageBoxResult.Cancel),
            },
            _ => new[] { ("OK", MessageBoxResult.OK) },
        };
        var escapeResult = specs[^1].Item2;
        _result = buttons == MessageBoxButton.OK ? MessageBoxResult.OK : escapeResult; // ×ボタンで閉じたとき

        for (var i = 0; i < specs.Length; i++)
        {
            var (label, result) = specs[i];
            var button = new Button
            {
                // アクセントボタンのスタイルは "_" をアクセスキーとして解釈しないので、AccessText で明示する
                Content = new AccessText { Text = label },
                MinWidth = 96,
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                IsDefault = i == 0,
                IsCancel = result == escapeResult,
            };
            if (i == 0) button.Style = (Style)FindResource("AccentButtonStyle");
            button.Click += (_, _) =>
            {
                _result = result;
                Close();
            };
            ButtonPanel.Children.Add(button);
        }
        Loaded += (_, _) => ((Button)ButtonPanel.Children[0]).Focus();
    }
}
