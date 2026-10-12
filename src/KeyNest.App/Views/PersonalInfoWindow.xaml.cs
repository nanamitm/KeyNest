using System.Windows;
using KeyNest.Core.Models;

namespace KeyNest.App.Views;

/// <summary>個人情報の編集。編集用のコピーに対して操作し、OK のときだけ書き戻す。</summary>
public partial class PersonalInfoWindow : Window
{
    /// <summary>左列に置く項目数（姓〜e-mail3）。残り（郵便番号1〜勤務先）は右列。</summary>
    private const int LeftColumnCount = 9;

    private readonly PersonalInfo _personal;
    private readonly List<Row> _rows;

    public PersonalInfoWindow(PersonalInfo personal)
    {
        InitializeComponent();
        _personal = personal;
        _rows = personal.Items
            .Select(i => new Row { Key = i.Key, Name = PersonalInfoFields.NameOf(i.Key), Value = i.Value, ShowInMenu = i.ShowInMenu })
            .ToList();
        LeftRows.ItemsSource = _rows.Take(LeftColumnCount).ToList();
        RightRows.ItemsSource = _rows.Skip(LeftColumnCount).ToList();
    }

    /// <summary>OK で内容が変わったか（未保存マークを付けるかの判断用）。</summary>
    public bool Changed { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        foreach (var (item, row) in _personal.Items.Zip(_rows))
        {
            var value = row.Value.Trim();
            if (item.Value == value && item.ShowInMenu == row.ShowInMenu) continue;
            item.Value = value;
            item.ShowInMenu = row.ShowInMenu;
            Changed = true;
        }
        DialogResult = true;
    }

    private sealed class Row
    {
        public string Key { get; init; } = "";
        public string Name { get; init; } = "";
        public string Value { get; set; } = "";
        public bool ShowInMenu { get; set; }
    }
}
