namespace KeyNest.App.ViewModels;

/// <summary>ツリーの節（グループまたはエントリ）。TreeViewItem の選択・表示状態とバインドする。</summary>
public abstract class TreeNodeViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _isVisible = true;
    private bool _isDropTarget;

    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    /// <summary>検索で絞り込まれたときに false になる。</summary>
    public bool IsVisible { get => _isVisible; set => SetProperty(ref _isVisible, value); }

    /// <summary>ドラッグ中にドロップ先としてハイライトする。</summary>
    public bool IsDropTarget { get => _isDropTarget; set => SetProperty(ref _isDropTarget, value); }
}
