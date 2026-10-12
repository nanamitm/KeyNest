using System.Windows;
using KeyNest.App.Services;
using KeyNest.Core.Import;

namespace KeyNest.App.Views;

/// <summary>ID Manager の XML を取り込む前の確認（件数の表示と取り込み方の選択）。</summary>
public partial class ImportWindow : Window
{
    public ImportWindow(IdManagerXmlDocument document, string fileName, bool hasPersonalInfo)
    {
        InitializeComponent();
        FileText.Text = fileName;
        SummaryText.Text = $"フォルダ {document.FolderCount} 件・エントリ {document.ItemCount} 件"
            + (document.PasteTypeNames.Count > 0 ? $"・貼り付けタイプ {document.PasteTypeNames.Count} 件（{string.Join("、", document.PasteTypeNames)}）" : "");
        GroupNameBox.Text = "ID Manager から取り込み";

        ImportDatesBox.IsEnabled = document.ItemsWithDates > 0;
        ImportDatesBox.IsChecked = document.ItemsWithDates > 0 && !document.DatesLookAutoFilled;
        DatesWarningText.Visibility = document.DatesLookAutoFilled ? Visibility.Visible : Visibility.Collapsed;

        // 既に個人情報を入力済みなら、上書きしないよう既定はオフ
        var personalCount = document.PersonalValues.Count;
        ImportPersonalBox.IsEnabled = personalCount > 0;
        ImportPersonalBox.IsChecked = personalCount > 0 && !hasPersonalInfo;
        ImportPersonalText.Text = personalCount > 0
            ? $"個人情報（氏名・住所など {personalCount} 項目）も取り込む。値がある項目は今の内容を上書きします"
            : "個人情報も取り込む（ID Manager 側に入力がありません）";

        var names = document.DefaultFieldNames.Where(n => n.Length > 0).ToList();
        ApplyDefaultsBox.IsEnabled = names.Count > 0;
        ApplyDefaultsText.Text = names.Count > 0
            ? $"新規エントリの項目名の初期値を ID Manager の設定（{string.Join(" / ", names)}）に合わせる"
            : "新規エントリの項目名の初期値を ID Manager の設定に合わせる（ID Manager 側に設定がありません）";

        Loaded += (_, _) =>
        {
            GroupNameBox.Focus();
            GroupNameBox.SelectAll();
        };
    }

    public ImportChoice? Choice { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = GroupNameBox.Text.Trim();
        Choice = new ImportChoice(name.Length > 0 ? name : "ID Manager から取り込み",
            ImportDatesBox.IsChecked == true, ApplyDefaultsBox.IsChecked == true, ImportPersonalBox.IsChecked == true);
        DialogResult = true;
    }
}
