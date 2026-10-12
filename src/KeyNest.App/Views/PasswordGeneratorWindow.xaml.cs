using System.Windows;
using KeyNest.Core.Security;

namespace KeyNest.App.Views;

/// <summary>
/// パスワードの生成。条件（文字種・文字数）を変えるとすぐに作り直す。
/// 「〇〇に設定」で呼び出し元の欄へ入れ、「コピー」はクリップボードへ（一定時間後に自動消去）。
/// </summary>
public partial class PasswordGeneratorWindow : Window
{
    private readonly Action<string> _copy;
    private bool _loading = true;

    public PasswordGeneratorWindow(string targetLabel, PasswordOptions options, Action<string> copy)
    {
        InitializeComponent();
        _copy = copy;
        ApplyButton.Content = $"「{targetLabel}」に設定";

        UpperBox.IsChecked = options.Uppercase;
        LowerBox.IsChecked = options.Lowercase;
        DigitsBox.IsChecked = options.Digits;
        SymbolsBox.IsChecked = options.Symbols;
        ExcludeAmbiguousBox.IsChecked = options.ExcludeAmbiguous;
        var length = Math.Clamp(options.Length, PasswordOptions.MinLength, PasswordOptions.MaxLength);
        LengthSlider.Value = length;
        LengthBox.Text = length.ToString();
        _loading = false;

        Generate();
    }

    /// <summary>閉じたときの条件（次回も同じ条件で開くために保存する）。</summary>
    public PasswordOptions Options { get; private set; } = new();

    /// <summary>「〇〇に設定」で閉じたときの値。</summary>
    public string? Result { get; private set; }

    private void Option_Changed(object sender, RoutedEventArgs e) => Generate();

    private void LengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        var text = ((int)LengthSlider.Value).ToString();
        if (LengthBox.Text != text) LengthBox.Text = text;
        Generate();
    }

    /// <summary>数値の入力はスライダーに反映する（範囲外・数字以外はエラー表示のまま）。</summary>
    private void LengthBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_loading) return;
        if (int.TryParse(LengthBox.Text, out var length) && length is >= PasswordOptions.MinLength and <= PasswordOptions.MaxLength)
        {
            if ((int)LengthSlider.Value != length)
                LengthSlider.Value = length; // ValueChanged で作り直す
            else
                Generate();
        }
        else
        {
            ShowError($"文字数は {PasswordOptions.MinLength}〜{PasswordOptions.MaxLength} の整数で入力してください。");
        }
    }

    private void Generate_Click(object sender, RoutedEventArgs e) => Generate();

    private void Generate()
    {
        if (_loading) return;
        Options = new PasswordOptions
        {
            Uppercase = UpperBox.IsChecked == true,
            Lowercase = LowerBox.IsChecked == true,
            Digits = DigitsBox.IsChecked == true,
            Symbols = SymbolsBox.IsChecked == true,
            ExcludeAmbiguous = ExcludeAmbiguousBox.IsChecked == true,
            Length = (int)LengthSlider.Value,
        };
        if (!(Options.Uppercase || Options.Lowercase || Options.Digits || Options.Symbols))
        {
            ShowError("使う文字を1種類以上選んでください。");
            return;
        }
        ResultBox.Text = PasswordGenerator.Generate(Options);
        ErrorText.Visibility = Visibility.Collapsed;
        ApplyButton.IsEnabled = CopyButton.IsEnabled = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        ApplyButton.IsEnabled = CopyButton.IsEnabled = false;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (ResultBox.Text.Length == 0) return;
        Result = ResultBox.Text;
        DialogResult = true;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (ResultBox.Text.Length > 0)
            _copy(ResultBox.Text);
    }
}
