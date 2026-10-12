using KeyNest.Core.Models;

namespace KeyNest.App.ViewModels;

/// <summary>汎用欄1つ分。項目名・値・伏せ字をエントリごとに編集できる。</summary>
public sealed class FieldViewModel : ObservableObject
{
    private readonly EntryField _model;
    private readonly Action _onChanged;
    private bool _isRevealed;

    public FieldViewModel(int index, EntryField model, Action onChanged)
    {
        Index = index;
        _model = model;
        _onChanged = onChanged;
    }

    public int Index { get; }
    public string Shortcut => $"Ctrl+{Index + 1}";

    public string Label
    {
        get => _model.Label;
        set
        {
            if (_model.Label == value) return;
            _model.Label = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public string Value
    {
        get => _model.Value;
        set
        {
            if (_model.Value == value) return;
            _model.Value = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MaskedText));
            _onChanged();
        }
    }

    public bool IsMasked
    {
        get => _model.IsMasked;
        set
        {
            if (_model.IsMasked == value) return;
            _model.IsMasked = value;
            _isRevealed = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRevealed));
            OnPropertyChanged(nameof(ShowValue));
            _onChanged();
        }
    }

    public string MaskedText => Value.Length == 0 ? "" : "●●●●●●●●";

    /// <summary>伏せ字の欄を一時的に表示している（保存しない画面上の状態）。</summary>
    public bool IsRevealed
    {
        get => _isRevealed;
        set
        {
            if (SetProperty(ref _isRevealed, value))
                OnPropertyChanged(nameof(ShowValue));
        }
    }

    public bool ShowValue => !IsMasked || IsRevealed;
}
