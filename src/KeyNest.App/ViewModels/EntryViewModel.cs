using System.Runtime.CompilerServices;
using KeyNest.Core.Models;

namespace KeyNest.App.ViewModels;

public enum ExpirationState
{
    None,
    /// <summary>有効期限まで <see cref="EntryViewModel.ExpirationWarningDays"/> 日以内。</summary>
    Soon,
    Expired,
}

/// <summary><see cref="Entry"/> を直接書き換えるラッパー。変更は onChanged で親に通知する。</summary>
public sealed class EntryViewModel : TreeNodeViewModel
{
    private readonly Action _onChanged;

    public const int ExpirationWarningDays = 30;

    public EntryViewModel(Entry model, GroupViewModel parent, Action onChanged)
    {
        Model = model;
        Parent = parent;
        _onChanged = onChanged;
        Fields = model.Fields
            .Select((f, i) => new FieldViewModel(i, f, OnFieldChanged))
            .ToList();
    }

    public Entry Model { get; }
    public GroupViewModel Parent { get; }
    public IReadOnlyList<FieldViewModel> Fields { get; }

    public string Title { get => Model.Title; set => Update(Model.Title, value, v => Model.Title = v); }
    public string Url { get => Model.Url; set => Update(Model.Url, value, v => Model.Url = v); }
    public string Email { get => Model.Email; set => Update(Model.Email, value, v => Model.Email = v); }
    public string FilePath { get => Model.FilePath; set => Update(Model.FilePath, value, v => Model.FilePath = v); }
    public string SerialNumber { get => Model.SerialNumber; set => Update(Model.SerialNumber, value, v => Model.SerialNumber = v); }
    public string Notes { get => Model.Notes; set => Update(Model.Notes, value, v => Model.Notes = v); }

    public DateTimeOffset Modified => Model.Modified;

    /// <summary>貼り付けタイプの ID（null は既定のタイプ）。</summary>
    public Guid? AutoTypeId
    {
        get => Model.AutoTypeId;
        set
        {
            if (Model.AutoTypeId == value) return;
            Model.AutoTypeId = value;
            Touch();
            OnPropertyChanged();
            _onChanged();
        }
    }

    // DatePicker が DateTime? を扱うので、モデルの DateOnly? と相互変換する
    public DateTime? IssueDate
    {
        get => Model.IssueDate?.ToDateTime(TimeOnly.MinValue);
        set => UpdateDate(Model.IssueDate, value, d => Model.IssueDate = d);
    }

    public DateTime? ExpirationDate
    {
        get => Model.ExpirationDate?.ToDateTime(TimeOnly.MinValue);
        set
        {
            if (UpdateDate(Model.ExpirationDate, value, d => Model.ExpirationDate = d))
            {
                OnPropertyChanged(nameof(ExpirationState));
                OnPropertyChanged(nameof(ExpirationText));
            }
        }
    }

    public ExpirationState ExpirationState => DaysUntilExpiration switch
    {
        null => ExpirationState.None,
        < 0 => ExpirationState.Expired,
        <= ExpirationWarningDays => ExpirationState.Soon,
        _ => ExpirationState.None,
    };

    public string ExpirationText => DaysUntilExpiration switch
    {
        null => "",
        < 0 => "期限切れ",
        0 => "本日まで",
        <= ExpirationWarningDays and var days => $"あと {days} 日",
        _ => "",
    };

    private int? DaysUntilExpiration =>
        Model.ExpirationDate is { } d ? d.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber : null;

    /// <summary>ツリーでタイトルの横に出す補足（欄1。伏せ字なら出さない）。</summary>
    public string Subtitle => Fields[0].IsMasked ? "" : Fields[0].Value;

    /// <summary>伏せ字の欄は検索対象にしない（検索結果から中身を推測されないように）。</summary>
    public bool Matches(string query) =>
        Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || Url.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || Email.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || FilePath.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || SerialNumber.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || Notes.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || Fields.Any(f => !f.IsMasked && f.Value.Contains(query, StringComparison.CurrentCultureIgnoreCase));

    public void HideRevealedFields()
    {
        foreach (var field in Fields)
            field.IsRevealed = false;
    }

    private void OnFieldChanged()
    {
        Touch();
        OnPropertyChanged(nameof(Subtitle));
        _onChanged();
    }

    private void Update(string current, string value, Action<string> set, [CallerMemberName] string? name = null)
    {
        if (current == value) return;
        set(value);
        Touch();
        OnPropertyChanged(name);
        _onChanged();
    }

    private bool UpdateDate(DateOnly? current, DateTime? value, Action<DateOnly?> set, [CallerMemberName] string? name = null)
    {
        var date = value is { } v ? DateOnly.FromDateTime(v) : (DateOnly?)null;
        if (current == date) return false;
        set(date);
        Touch();
        OnPropertyChanged(name);
        _onChanged();
        return true;
    }

    private void Touch()
    {
        Model.Modified = DateTimeOffset.Now;
        OnPropertyChanged(nameof(Modified));
    }
}
