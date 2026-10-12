using System.Collections.ObjectModel;
using System.IO;
using System.Xml;
using KeyNest.App.Services;
using KeyNest.Core.AutoTyping;
using KeyNest.Core.Import;
using KeyNest.Core.Models;
using KeyNest.Core.Security;
using KeyNest.Core.Storage;

namespace KeyNest.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly VaultSession _session;
    private readonly IDialogService _dialogs;
    private readonly ClipboardService _clipboard;

    private GroupViewModel? _selectedGroup;
    private EntryViewModel? _selectedEntry;
    private string _searchText = "";
    private bool _isDirty;
    private string _status = "";
    private bool _refreshingAutoTypes;

    public MainViewModel(VaultSession session, IDialogService dialogs, ClipboardService clipboard)
    {
        _session = session;
        _dialogs = dialogs;
        _clipboard = clipboard;

        Root = new GroupViewModel(session.Vault.Root, null, MarkDirty);
        RootGroups = [Root];
        AutoTypes = new(session.Vault.Settings.AutoTypes);

        AddGroupCommand = new RelayCommand(AddGroup, () => SelectedGroup is not null);
        RenameGroupCommand = new RelayCommand(RenameGroup, () => SelectedEntry is null && SelectedGroup is { IsRoot: false });
        DeleteGroupCommand = new RelayCommand(DeleteGroup, () => SelectedEntry is null && SelectedGroup is { IsRoot: false });
        AddEntryCommand = new RelayCommand(AddEntry, () => SelectedGroup is not null);
        DeleteEntryCommand = new RelayCommand(DeleteEntry, () => SelectedEntry is not null);
        SaveCommand = new RelayCommand(() => Save(), () => IsDirty);
        OpenUrlCommand = new RelayCommand(OpenUrl, () => !string.IsNullOrWhiteSpace(SelectedEntry?.Url));
        OpenMailCommand = new RelayCommand(OpenMail, () => !string.IsNullOrWhiteSpace(SelectedEntry?.Email));
        BrowseFileCommand = new RelayCommand(BrowseFile, () => SelectedEntry is not null);
        OpenFileCommand = new RelayCommand(OpenFile, () => !string.IsNullOrWhiteSpace(SelectedEntry?.FilePath));
        CopySerialCommand = new RelayCommand(() => CopyText(SelectedEntry!.SerialNumber, "シリアル番号"),
            () => !string.IsNullOrEmpty(SelectedEntry?.SerialNumber));
        LockCommand = new RelayCommand(() => LockRequested?.Invoke(this, EventArgs.Empty));
        SettingsCommand = new RelayCommand(EditSettings);
        ImportIdManagerCommand = new RelayCommand(ImportIdManager);
        EditAutoTypesCommand = new RelayCommand(EditAutoTypes);
        PersonalInfoCommand = new RelayCommand(EditPersonalInfo);
        AutoTypeCommand = new RelayCommand(RequestAutoType, () => SelectedEntry is not null);

        // 欄の操作。パラメーターは FieldViewModel、またはショートカット用の欄番号（"0"〜"3"）
        CopyFieldCommand = new RelayCommand<object>(p => CopyField(ResolveField(p)!), p => ResolveField(p) is { Value.Length: > 0 });
        GenerateFieldCommand = new RelayCommand<object>(p => GenerateField(ResolveField(p)!), p => ResolveField(p) is not null);

        Select(Root);
    }

    /// <summary>ロック要求。ウィンドウ側で保存確認とセッション破棄を行う。</summary>
    public event EventHandler? LockRequested;

    public GroupViewModel Root { get; }
    public ObservableCollection<GroupViewModel> RootGroups { get; }

    public RelayCommand AddGroupCommand { get; }
    public RelayCommand RenameGroupCommand { get; }
    public RelayCommand DeleteGroupCommand { get; }
    public RelayCommand AddEntryCommand { get; }
    public RelayCommand DeleteEntryCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand OpenUrlCommand { get; }
    public RelayCommand OpenMailCommand { get; }
    public RelayCommand BrowseFileCommand { get; }
    public RelayCommand OpenFileCommand { get; }
    public RelayCommand CopySerialCommand { get; }
    public RelayCommand LockCommand { get; }
    public RelayCommand SettingsCommand { get; }
    public RelayCommand ImportIdManagerCommand { get; }
    public RelayCommand EditAutoTypesCommand { get; }
    public RelayCommand PersonalInfoCommand { get; }
    public RelayCommand AutoTypeCommand { get; }

    /// <summary>自動入力の要求。ウィンドウ側で最小化してから、直前のウィンドウへ送る。</summary>
    public event EventHandler<IReadOnlyList<AutoTypeStep>>? AutoTypeRequested;

    /// <summary>詳細欄の「貼り付け方法」の選択肢（設定の一覧と同じ順。先頭が既定）。</summary>
    public ObservableCollection<AutoTypeTemplate> AutoTypes { get; }

    /// <summary>選択中のエントリの貼り付けタイプ。先頭（既定）を選ぶと「指定なし」として保存する。</summary>
    public AutoTypeTemplate? SelectedAutoType
    {
        get => SelectedEntry is { } entry ? _session.Vault.Settings.ResolveAutoType(entry.AutoTypeId) : null;
        set
        {
            // 選択肢の作り直し中に ComboBox が null を書き戻してくるのは無視する
            if (_refreshingAutoTypes || value is null || SelectedEntry is not { } entry) return;
            entry.AutoTypeId = value == AutoTypes.FirstOrDefault() ? null : value.Id;
            OnPropertyChanged();
        }
    }
    public RelayCommand<object> CopyFieldCommand { get; }
    public RelayCommand<object> GenerateFieldCommand { get; }

    public string WindowTitle => $"KeyNest - {Path.GetFileName(_session.FilePath)}{(IsDirty ? " *" : "")}";
    public string FilePath => _session.FilePath;

    /// <summary>ツリーで選択された節。エントリ選択中は、その親グループが追加先になる。</summary>
    public TreeNodeViewModel? SelectedNode
    {
        set
        {
            SelectedEntry = value as EntryViewModel;
            SelectedGroup = value switch
            {
                GroupViewModel g => g,
                EntryViewModel e => e.Parent,
                _ => null,
            };
        }
    }

    public GroupViewModel? SelectedGroup { get => _selectedGroup; private set => SetProperty(ref _selectedGroup, value); }

    public EntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        private set
        {
            var previous = _selectedEntry;
            if (!SetProperty(ref _selectedEntry, value)) return;
            previous?.HideRevealedFields();
            OnPropertyChanged(nameof(SelectedAutoType));
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            Root.ApplyFilter(value.Trim());

            // 選択中の節が絞り込みで隠れたら、最初に一致したエントリへ移す
            TreeNodeViewModel? selected = SelectedEntry ?? (TreeNodeViewModel?)SelectedGroup;
            if (selected is { IsVisible: false })
                Select(Root.FirstVisibleEntry() ?? (TreeNodeViewModel)Root);
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
                OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public bool Save()
    {
        try
        {
            _session.Save();
            IsDirty = false;
            Status = $"保存しました ({DateTime.Now:HH:mm:ss})";
            return true;
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"保存に失敗しました。\n{ex.Message}");
            return false;
        }
    }

    private void MarkDirty() => IsDirty = true;

    /// <summary>ドラッグ＆ドロップで <paramref name="node"/> を <paramref name="target"/> へ移動できるか。</summary>
    public static bool CanMove(TreeNodeViewModel node, GroupViewModel target) => node switch
    {
        EntryViewModel e => e.Parent != target,
        GroupViewModel g => !g.IsRoot && g.Parent != target && !target.IsSelfOrDescendantOf(g),
        _ => false,
    };

    /// <summary>エントリまたはグループを別のグループへ移動し、移動後の節を選択する。</summary>
    public void Move(TreeNodeViewModel node, GroupViewModel target)
    {
        if (!CanMove(node, target)) return;
        TreeNodeViewModel moved;
        switch (node)
        {
            case EntryViewModel e:
                e.Parent.RemoveEntry(e);
                moved = target.AddEntry(e.Model);
                Status = $"「{e.Title}」を「{target.Name}」へ移動しました";
                break;
            case GroupViewModel g:
                g.Parent!.RemoveGroup(g);
                moved = target.AddGroup(g.Model);
                Status = $"グループ「{g.Name}」を「{target.Name}」へ移動しました";
                break;
            default:
                return;
        }
        Root.ApplyFilter(SearchText.Trim());
        Select(moved);
    }

    /// <summary>ツリー上で節を選択する。TreeView 側の SelectedItemChanged で SelectedNode に反映される。</summary>
    private static void Select(TreeNodeViewModel node)
    {
        var parent = node switch
        {
            GroupViewModel g => g.Parent,
            EntryViewModel e => e.Parent,
            _ => null,
        };
        parent?.ExpandToRoot();
        node.IsSelected = true;
    }

    private void AddGroup()
    {
        var name = _dialogs.Prompt("グループの追加", "グループ名:", "新しいグループ");
        if (string.IsNullOrWhiteSpace(name)) return;
        Select(SelectedGroup!.AddGroup(name.Trim()));
    }

    private void RenameGroup()
    {
        var name = _dialogs.Prompt("グループ名の変更", "グループ名:", SelectedGroup!.Name);
        if (!string.IsNullOrWhiteSpace(name))
            SelectedGroup.Name = name.Trim();
    }

    private void DeleteGroup()
    {
        var group = SelectedGroup!;
        var count = group.AllEntries().Count();
        if (!_dialogs.Confirm($"グループ「{group.Name}」と、含まれる {count} 件のエントリを削除しますか？")) return;
        group.Parent!.RemoveGroup(group);
        Select(group.Parent);
    }

    private void AddEntry()
    {
        var group = SelectedGroup!;
        SearchText = "";
        Select(group.AddEntry(Entry.Create(_session.Vault.Settings, "新しいエントリ")));
    }

    private void DeleteEntry()
    {
        var entry = SelectedEntry!;
        if (!_dialogs.Confirm($"「{entry.Title}」を削除しますか？")) return;
        entry.Parent.RemoveEntry(entry);
        Select(entry.Parent);
    }

    private FieldViewModel? ResolveField(object? parameter) => parameter switch
    {
        FieldViewModel f => f,
        string s when int.TryParse(s, out var i) && i >= 0 && i < Entry.FieldCount => SelectedEntry?.Fields[i],
        _ => null,
    };

    /// <summary>ツリーのダブルクリック用。最初の伏せ字の欄（無ければ欄1）をコピーする。</summary>
    public void CopyDefaultField()
    {
        if (SelectedEntry is not { } entry) return;
        var field = entry.Fields.FirstOrDefault(f => f.IsMasked && f.Value.Length > 0) ?? entry.Fields[0];
        if (field.Value.Length > 0)
            CopyField(field);
    }

    private void CopyField(FieldViewModel field) => CopyText(field.Value, field.Label);

    private void CopyText(string value, string label)
    {
        var seconds = _session.Vault.Settings.ClipboardClearSeconds;
        if (_clipboard.Copy(value, seconds))
            Status = seconds > 0 ? $"「{label}」をコピーしました（{seconds} 秒後に消去）" : $"「{label}」をコピーしました";
        else
            Status = "クリップボードにアクセスできませんでした";
    }

    private void BrowseFile()
    {
        if (_dialogs.PickFile(SelectedEntry!.FilePath) is { } path)
            SelectedEntry.FilePath = path;
    }

    private void OpenFile()
    {
        var path = SelectedEntry!.FilePath.Trim().Trim('"');
        // 取り込んだデータに他人が作ったパスが入っている可能性があるので、実行形式は確認してから開く
        if (ShellLauncher.IsExecutable(path)
            && !_dialogs.Confirm($"実行形式のファイルです。開く（実行する）と、プログラムが起動します。\n\n{path}\n\n開きますか？"))
            return;
        if (ShellLauncher.OpenFile(path) is { } error)
            _dialogs.ShowError(error);
    }

    private void GenerateField(FieldViewModel field)
    {
        if (_dialogs.GeneratePassword(field.Label, value => CopyText(value, "生成したパスワード")) is not { } value) return;
        if (field.Value.Length > 0 && field.Value != value
            && !_dialogs.Confirm($"「{field.Label}」の今の値を、生成したパスワードに置き換えますか？"))
            return;
        field.Value = value;
        field.IsRevealed = true;
        Status = $"「{field.Label}」に生成したパスワードを設定しました";
    }

    private void RequestAutoType()
    {
        var entry = SelectedEntry!.Model;
        var settings = _session.Vault.Settings;
        var template = settings.ResolveAutoType(entry.AutoTypeId);
        var steps = AutoTypePlanner.Plan(template, entry, _session.Vault.Personal, settings.AutoTypeDelayMs);
        if (steps.Count == 0)
        {
            Status = $"貼り付けタイプ「{template.Name}」で送る内容がありません（値が空です）";
            return;
        }
        Status = $"「{template.Name}」で自動入力しています…";
        AutoTypeRequested?.Invoke(this, steps);
    }

    /// <summary>ウィンドウ側から自動入力の結果を伝える。</summary>
    public void ReportAutoType(string message) => Status = message;

    private void EditPersonalInfo()
    {
        if (!_dialogs.EditPersonalInfo(_session.Vault.Personal)) return;
        MarkDirty();
        Status = "個人情報を変更しました";
    }

    private void EditAutoTypes()
    {
        if (!_dialogs.EditAutoTypes(_session.Vault.Settings)) return;
        RefreshAutoTypes();
        MarkDirty();
        Status = "貼り付けタイプを変更しました";
    }

    private void RefreshAutoTypes()
    {
        _refreshingAutoTypes = true;
        try
        {
            AutoTypes.Clear();
            foreach (var template in _session.Vault.Settings.AutoTypes)
                AutoTypes.Add(template);
        }
        finally
        {
            _refreshingAutoTypes = false;
        }
        OnPropertyChanged(nameof(SelectedAutoType));
    }

    /// <summary>設定画面から呼ばれる。変更時にデータファイルを保存し直すので、未保存の状態も解消される。</summary>
    private bool ChangeMasterPassword()
    {
        if (!_dialogs.ChangeMasterPassword()) return false;
        IsDirty = false;
        Status = $"マスターパスワードを変更しました（{DateTime.Now:HH:mm:ss} に保存）";
        return true;
    }

    private void EditSettings()
    {
        // 項目の初期値は新規エントリにだけ使うので、既存エントリの表示は変わらない
        var vaultChanged = _dialogs.EditSettings(_session.Vault.Settings, ChangeMasterPassword);
        if (vaultChanged is null) return;
        if (vaultChanged == true) MarkDirty();
        Status = "設定を変更しました";
    }

    /// <summary>元の ID Manager の XML 出力を、新しいグループとして取り込む（既存のデータは変更しない）。</summary>
    private void ImportIdManager()
    {
        if (_dialogs.PickFile("", "ID Manager の XML 出力 (*.xml)|*.xml|すべてのファイル (*.*)|*.*") is not { } path) return;

        IdManagerXmlDocument document;
        try
        {
            document = IdManagerXmlDocument.Load(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or InvalidDataException)
        {
            _dialogs.ShowError($"ファイルを読み込めませんでした。\n{ex.Message}");
            return;
        }
        var hasPersonalInfo = _session.Vault.Personal.Items.Any(i => i.Value.Length > 0);
        if (_dialogs.ConfirmImport(document, Path.GetFileName(path), hasPersonalInfo) is not { } choice) return;

        var settings = _session.Vault.Settings;
        if (choice.ApplyFieldDefaults)
        {
            // ID Manager 側で名前が設定されている欄だけ合わせる（伏せ字の設定は今のまま）
            settings.FieldDefaults = settings.FieldDefaults
                .Select((d, i) => document.DefaultFieldNames[i] is { Length: > 0 } name ? new FieldDefault { Label = name, IsMasked = d.IsMasked } : d)
                .ToList();
        }

        var result = document.ImportInto(_session.Vault, choice.GroupName, choice.ImportDates, choice.ImportPersonalInfo);
        RefreshAutoTypes();
        SearchText = "";
        Select(Root.AddGroup(result.Group));
        var dropped = result.DroppedCommandCount > 0
            ? $"。対応していないコマンド {result.DroppedCommandCount} 件は貼り付けタイプから外しました"
            : "";
        Status = $"ID Manager からフォルダ {document.FolderCount} 件・エントリ {document.ItemCount} 件・貼り付けタイプ {result.AutoTypeCount} 件を取り込みました（未保存）{dropped}";
    }

    private void OpenMail()
    {
        if (ShellLauncher.OpenMail(SelectedEntry!.Email) is { } error)
            _dialogs.ShowError(error);
    }

    private void OpenUrl()
    {
        if (ShellLauncher.OpenUrl(SelectedEntry!.Url) is { } error)
            _dialogs.ShowError(error);
    }
}
