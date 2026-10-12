using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using KeyNest.App.ViewModels;
using KeyNest.Core.Models;

namespace KeyNest.App.Views;

/// <summary>
/// 貼り付けタイプの編集。編集用のコピーに対して操作し、OK のときだけ設定へ書き戻す。
/// </summary>
public partial class AutoTypeEditorWindow : Window
{
    private const int MaxDelayMs = 10_000;

    private readonly VaultSettings _settings;
    private readonly ObservableCollection<TemplateItem> _templates;

    public AutoTypeEditorWindow(VaultSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _templates = new(settings.AutoTypes.Select(t => new TemplateItem(t.Id, t.Name, t.Commands.Select(CommandItem.From))));
        RefreshDefaults();

        TemplateList.ItemsSource = _templates;
        CatalogList.ItemsSource = AutoTypeCommands.Catalog.Select(c => new CommandItem(c.Key, c.Name)).ToList();
        DelayBox.Text = settings.AutoTypeDelayMs.ToString();
        TemplateList.SelectedIndex = 0;
        UpdateButtons();
    }

    private TemplateItem? SelectedTemplate => TemplateList.SelectedItem as TemplateItem;

    // ---- 貼り付けタイプ ----

    private void TemplateList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        CommandList.ItemsSource = SelectedTemplate?.Commands;
        UpdateButtons();
    }

    private void AddTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (PromptName("新しい貼り付けタイプ", "") is not { } name) return;
        var item = new TemplateItem(Guid.NewGuid(), name, []);
        _templates.Add(item);
        RefreshDefaults();
        TemplateList.SelectedItem = item;
    }

    private void RenameTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTemplate is { } template && PromptName("名前の変更", template.Name) is { } name)
            template.Name = name;
    }

    private void DeleteTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTemplate is not { } template || _templates.Count <= 1) return;
        var result = MessageDialog.Show(this,
            $"「{template.Name}」を削除しますか？\nこのタイプを使っているエントリは、既定（一覧の先頭）のタイプになります。",
            "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;
        var index = _templates.IndexOf(template);
        _templates.Remove(template);
        RefreshDefaults();
        TemplateList.SelectedIndex = Math.Min(index, _templates.Count - 1);
    }

    private void MoveTemplateUp_Click(object sender, RoutedEventArgs e) => MoveTemplate(-1);
    private void MoveTemplateDown_Click(object sender, RoutedEventArgs e) => MoveTemplate(1);

    private void MoveTemplate(int offset)
    {
        if (SelectedTemplate is not { } template) return;
        var index = _templates.IndexOf(template);
        var target = index + offset;
        if (target < 0 || target >= _templates.Count) return;
        _templates.Move(index, target);
        RefreshDefaults();
        UpdateButtons();
    }

    private void RefreshDefaults()
    {
        for (var i = 0; i < _templates.Count; i++)
            _templates[i].IsDefault = i == 0;
    }

    private string? PromptName(string title, string initial)
    {
        while (true)
        {
            var dialog = new InputDialog(title, "名前:", initial) { Owner = this };
            if (dialog.ShowDialog() != true) return null;
            var name = dialog.Value.Trim();
            if (name.Length > 0) return name;
            initial = name;
        }
    }

    // ---- コマンド ----

    private void CommandList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateButtons();

    private void CatalogList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateButtons();

    private void CatalogList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AddCommand();

    private void AddCommand_Click(object sender, RoutedEventArgs e) => AddCommand();

    /// <summary>選択中のコマンドの下（未選択なら末尾）に追加する。</summary>
    private void AddCommand()
    {
        if (SelectedTemplate is not { } template || CatalogList.SelectedItem is not CommandItem command) return;
        var index = CommandList.SelectedIndex >= 0 ? CommandList.SelectedIndex + 1 : template.Commands.Count;
        var item = new CommandItem(command.Key, command.Name); // 同じコマンドを複数回使えるよう別インスタンスにする
        template.Commands.Insert(index, item);
        CommandList.SelectedIndex = index;
    }

    private void RemoveCommand_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTemplate is not { } template || CommandList.SelectedIndex < 0) return;
        var index = CommandList.SelectedIndex;
        template.Commands.RemoveAt(index);
        CommandList.SelectedIndex = Math.Min(index, template.Commands.Count - 1);
    }

    private void MoveCommandUp_Click(object sender, RoutedEventArgs e) => MoveCommand(-1);
    private void MoveCommandDown_Click(object sender, RoutedEventArgs e) => MoveCommand(1);

    private void MoveCommand(int offset)
    {
        if (SelectedTemplate is not { } template || CommandList.SelectedIndex < 0) return;
        var index = CommandList.SelectedIndex;
        var target = index + offset;
        if (target < 0 || target >= template.Commands.Count) return;
        template.Commands.Move(index, target);
        CommandList.SelectedIndex = target;
    }

    private void UpdateButtons()
    {
        var templateIndex = TemplateList.SelectedIndex;
        RenameButton.IsEnabled = templateIndex >= 0;
        DeleteButton.IsEnabled = templateIndex >= 0 && _templates.Count > 1;
        TemplateUpButton.IsEnabled = templateIndex > 0;
        TemplateDownButton.IsEnabled = templateIndex >= 0 && templateIndex < _templates.Count - 1;

        var commandIndex = CommandList.SelectedIndex;
        var commandCount = SelectedTemplate?.Commands.Count ?? 0;
        RemoveCommandButton.IsEnabled = commandIndex >= 0;
        CommandUpButton.IsEnabled = commandIndex > 0;
        CommandDownButton.IsEnabled = commandIndex >= 0 && commandIndex < commandCount - 1;
        AddCommandButton.IsEnabled = templateIndex >= 0 && CatalogList.SelectedItem is not null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(DelayBox.Text, out var delay) || delay < 0 || delay > MaxDelayMs)
        {
            ErrorText.Text = $"待機時間は 0〜{MaxDelayMs} の整数で入力してください。";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        _settings.AutoTypes = _templates
            .Select(t => new AutoTypeTemplate { Id = t.Id, Name = t.Name, Commands = t.Commands.Select(c => c.Key).ToList() })
            .ToList();
        _settings.AutoTypeDelayMs = delay;
        DialogResult = true;
    }

    /// <summary>
    /// 同じコマンドが一覧に複数あっても選択を区別できるよう、値ではなく参照で比較するクラスにしている。
    /// </summary>
    private sealed class CommandItem(string key, string name)
    {
        public string Key { get; } = key;
        public string Name { get; } = name;

        public static CommandItem From(string key) => new(key, AutoTypeCommands.DisplayName(key));
    }

    private sealed class TemplateItem(Guid id, string name, IEnumerable<CommandItem> commands) : ObservableObject
    {
        private string _name = name;
        private bool _isDefault;

        public Guid Id { get; } = id;
        public ObservableCollection<CommandItem> Commands { get; } = new(commands);

        public string Name { get => _name; set => SetProperty(ref _name, value); }
        public bool IsDefault { get => _isDefault; set => SetProperty(ref _isDefault, value); }
    }
}
