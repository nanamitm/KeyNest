using System.Collections.ObjectModel;
using KeyNest.Core.Models;

namespace KeyNest.App.ViewModels;

/// <summary>
/// <see cref="Group"/> のラッパー。モデルのリストと ObservableCollection を常に同期させる。
/// ツリーには <see cref="Nodes"/>（子グループ → エントリの順）を表示する。
/// </summary>
public sealed class GroupViewModel : TreeNodeViewModel
{
    private readonly Action _onChanged;
    private bool _isExpanded = true;

    public Group Model { get; }
    public GroupViewModel? Parent { get; }
    public ObservableCollection<GroupViewModel> Children { get; }
    public ObservableCollection<EntryViewModel> Entries { get; }
    public ObservableCollection<TreeNodeViewModel> Nodes { get; }

    public GroupViewModel(Group model, GroupViewModel? parent, Action onChanged)
    {
        Model = model;
        Parent = parent;
        _onChanged = onChanged;
        Children = new(model.Groups.Select(g => new GroupViewModel(g, this, onChanged)));
        Entries = new(model.Entries.Select(e => new EntryViewModel(e, this, onChanged)));
        Nodes = new(Children.Concat<TreeNodeViewModel>(Entries));
    }

    public string Name
    {
        get => Model.Name;
        set
        {
            if (Model.Name == value) return;
            Model.Name = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
    public bool IsRoot => Parent is null;

    /// <summary>配下のグループを含めたエントリ数（ツリーの件数表示用）。</summary>
    public int TotalCount => AllEntries().Count();

    private void NotifyCountChanged()
    {
        for (var g = this; g is not null; g = g.Parent)
            g.OnPropertyChanged(nameof(TotalCount));
    }

    public IEnumerable<EntryViewModel> AllEntries() =>
        Entries.Concat(Children.SelectMany(c => c.AllEntries()));

    /// <summary>このグループと祖先をすべて展開する（選択した節を見えるようにするため）。</summary>
    public void ExpandToRoot()
    {
        for (var g = this; g is not null; g = g.Parent)
            g.IsExpanded = true;
    }

    /// <summary>
    /// 検索語で配下を絞り込み、一致するものが残ったかを返す。空文字なら全表示に戻す。
    /// </summary>
    public bool ApplyFilter(string query)
    {
        var any = false;
        foreach (var child in Children)
            any |= child.ApplyFilter(query);
        foreach (var entry in Entries)
        {
            entry.IsVisible = query.Length == 0 || entry.Matches(query);
            any |= entry.IsVisible;
        }

        IsVisible = query.Length == 0 || any || IsRoot;
        if (query.Length > 0 && any)
            IsExpanded = true;
        return any;
    }

    /// <summary>このグループが <paramref name="group"/> 自身か、その配下にあるか。</summary>
    public bool IsSelfOrDescendantOf(GroupViewModel group)
    {
        for (var g = this; g is not null; g = g.Parent)
            if (g == group) return true;
        return false;
    }

    /// <summary>表示順（子グループ → エントリ）で最初に見えているエントリ。</summary>
    public EntryViewModel? FirstVisibleEntry() =>
        Children.Where(c => c.IsVisible).Select(c => c.FirstVisibleEntry()).FirstOrDefault(e => e is not null)
        ?? Entries.FirstOrDefault(e => e.IsVisible);

    public GroupViewModel AddGroup(string name) => AddGroup(new Group { Name = name });

    /// <summary>既存のグループ（移動元から外したもの）を子として追加する。</summary>
    public GroupViewModel AddGroup(Group group)
    {
        Model.Groups.Add(group);
        var vm = new GroupViewModel(group, this, _onChanged);
        Children.Add(vm);
        Nodes.Insert(Children.Count - 1, vm); // グループはエントリより前に並べる
        NotifyCountChanged();
        IsExpanded = true;
        _onChanged();
        return vm;
    }

    public void RemoveGroup(GroupViewModel child)
    {
        Model.Groups.Remove(child.Model);
        Children.Remove(child);
        Nodes.Remove(child);
        NotifyCountChanged();
        _onChanged();
    }

    public EntryViewModel AddEntry(Entry entry)
    {
        Model.Entries.Add(entry);
        var vm = new EntryViewModel(entry, this, _onChanged);
        Entries.Add(vm);
        Nodes.Add(vm);
        NotifyCountChanged();
        IsExpanded = true;
        _onChanged();
        return vm;
    }

    public void RemoveEntry(EntryViewModel entry)
    {
        Model.Entries.Remove(entry.Model);
        Entries.Remove(entry);
        Nodes.Remove(entry);
        NotifyCountChanged();
        _onChanged();
    }
}
