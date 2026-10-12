using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using KeyNest.App.Services;
using KeyNest.App.Views;
using KeyNest.Core.Models;
using KeyNest.Core.Storage;
using Forms = System.Windows.Forms;

namespace KeyNest.App;

/// <summary>
/// アプリ全体の状態（ロック中／表示中／トレイに格納中）と、タスクトレイ・ホットキーを管理する。
/// ShutdownMode は OnExplicitShutdown なので、ウィンドウが無くてもトレイに常駐し続けられる。
/// </summary>
public partial class App : Application
{
    private const int MenuTextMaxLength = 40;

    private string _vaultPath = "";
    private AppSettingsStore _settings = null!;
    private TrayIconService _tray = null!;
    private HotkeyService _hotkey = null!;
    private ClipboardService _clipboard = null!;
    private readonly WindowsHelloService _hello = new();
    private readonly AutoTypeService _autoType = new();
    private ForegroundTracker _foreground = null!;

    private VaultSession? _session;
    private MainWindow? _main;
    private UnlockWindow? _unlock;
    private bool _showUnlockAfterLock;
    private bool _exiting;
    private IntPtr _autoTypeTarget;

    /// <summary>KeyNest を最後に操作した時刻（自動ロックの「KeyNest を操作していない時間」の基準）。</summary>
    private DateTime _lastActivity = DateTime.UtcNow;
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(10) };

    private bool KeepRunningInTray => _settings.Current.TrayEnabled;

    /// <summary>トレイに格納中で、表示にマスターパスワードが必要な状態。</summary>
    private bool IsProtected => _main is { IsVisible: false } && KeepRunningInTray && _settings.Current.RequirePasswordOnRestore;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // WPF の既定の表示言語は en-US なので、DatePicker などの日付表示を OS の言語に合わせる
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(
                System.Globalization.CultureInfo.CurrentCulture.IetfLanguageTag)));
        _vaultPath = e.Args.Length > 0 ? e.Args[0] : AppPaths.DefaultVaultPath;
        _settings = AppSettingsStore.Load(AppPaths.SettingsPath);
        _clipboard = new ClipboardService();

        Forms.Application.EnableVisualStyles();
        // WPF のメッセージループでも WinForms のメニューがキーボード操作（矢印・Enter・Esc）を受け取れるようにする
        System.Windows.Forms.Integration.WindowsFormsHost.EnableWindowsFormsInterop();
        _tray = new TrayIconService { BuildMenu = BuildTrayMenu };
        _tray.OpenRequested += (_, _) => ShowMainWindow();
        _hotkey = new HotkeyService();
        _foreground = new ForegroundTracker();
        _hotkey.Pressed += (_, _) => _tray.ShowMenuAtCursor();

        // キー入力・クリック・ホイールを KeyNest の操作とみなす（マウスを乗せただけでは数えない）
        InputManager.Current.PreProcessInput += (_, args) =>
        {
            if (args.StagingItem.Input is KeyboardEventArgs or MouseButtonEventArgs or MouseWheelEventArgs)
                _lastActivity = DateTime.UtcNow;
        };
        _idleTimer.Tick += (_, _) => CheckIdleLock();
        _idleTimer.Start();

        _settings.Changed += (_, _) => ApplySettings();
        ApplySettings();
        UpdateTrayText();

        if (!(KeepRunningInTray && _settings.Current.StartInTray))
            ShowUnlock();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _idleTimer.Stop();
        _hotkey.Dispose();
        _foreground.Dispose();
        _tray.Dispose();
        _clipboard.ClearIfUnchanged();
        _session?.Dispose();
        base.OnExit(e);
    }

    /// <summary>トレイ表示・ホットキー登録を現在の設定に合わせる。</summary>
    private void ApplySettings()
    {
        var s = _settings.Current;
        _tray.Visible = s.TrayEnabled;
        if (_main is not null)
        {
            _main.HideToTray = s.TrayEnabled;
            if (!s.TrayEnabled && !_main.IsVisible)
                RestoreMain(); // トレイを無効にしたら隠れたままにしない
        }

        _hotkey.Unregister();
        if (s.HotkeyEnabled && !_hotkey.Register(s.HotkeyModifiers, s.HotkeyKey))
        {
            MessageDialog.Show($"ホットキー {s.HotkeyText} を登録できませんでした。\n他のアプリで使用されている可能性があります。設定で別のキーを選んでください。",
                "KeyNest", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateTrayText() =>
        _tray.Text = _session is null ? "KeyNest（ロック中）" : "KeyNest";

    // ---- 画面の遷移 ----

    /// <summary>トレイのダブルクリック・「表示」。状態に応じてロック解除・本人確認・復元を行う。</summary>
    private void ShowMainWindow()
    {
        if (_unlock is not null)
        {
            _unlock.Activate();
            return;
        }
        if (_main is null)
        {
            ShowUnlock();
            return;
        }
        if (IsProtected && !VerifyPassword())
        {
            if (_exiting) ExitApp(); // 確認画面の表示中に「終了」が選ばれた
            return;
        }
        RestoreMain();
    }

    private void RestoreMain()
    {
        if (_main is null) return;
        _main.Show();
        if (_main.WindowState == WindowState.Minimized)
            _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    private bool VerifyPassword()
    {
        var dialog = new UnlockWindow(_vaultPath, cancelExitsApp: false, verifySession: _session, hello: _hello);
        _unlock = dialog;
        try
        {
            return dialog.ShowDialog() == true;
        }
        finally
        {
            _unlock = null;
        }
    }

    private void ShowUnlock()
    {
        if (_unlock is not null)
        {
            _unlock.Activate();
            return;
        }

        var dialog = new UnlockWindow(_vaultPath, cancelExitsApp: !KeepRunningInTray, hello: _hello);
        _unlock = dialog;
        bool unlocked;
        try
        {
            unlocked = dialog.ShowDialog() == true;
        }
        finally
        {
            _unlock = null;
        }

        if (_exiting)
        {
            dialog.Session?.Dispose();
            Shutdown();
            return;
        }
        if (!unlocked || dialog.Session is not { } session)
        {
            if (!KeepRunningInTray) Shutdown(); // トレイ常駐中はロックしたまま待機
            return;
        }
        OpenMain(session);
    }

    private void OpenMain(VaultSession session)
    {
        _session = session;
        var main = new MainWindow(session, _clipboard, _settings, new VaultHelloController(_hello, _vaultPath, session), _autoType, _foreground)
        {
            HideToTray = KeepRunningInTray,
        };
        _main = main;
        MainWindow = main;
        main.LockRequested += (_, _) => Lock(showUnlock: true);
        _lastActivity = DateTime.UtcNow; // ロック解除の直後にすぐ自動ロックしないように
        main.Closed += (_, _) => OnMainClosed(main);
        main.Show();
        UpdateTrayText();
    }

    /// <param name="showUnlock">ロック後すぐにロック解除画面を出すか（トレイからのロックでは出さない）。</param>
    private void Lock(bool showUnlock)
    {
        if (_main is null) return;
        _showUnlockAfterLock = showUnlock;
        _main.RequestClose(MainCloseReason.Lock);
    }

    private void OnMainClosed(MainWindow main)
    {
        _session?.Dispose();
        _session = null;
        _main = null;
        _clipboard.ClearIfUnchanged();
        UpdateTrayText();

        if (main.CloseReason is MainCloseReason.Lock or MainCloseReason.AutoLock)
        {
            if (_showUnlockAfterLock || !KeepRunningInTray)
                ShowUnlock();
            return;
        }
        Shutdown();
    }

    /// <summary>
    /// 一定時間操作が無ければロックする。設定画面などが開いていれば、先に閉じて次の確認でロックする
    /// （閉じられない画面＝パスワード変更の処理中などは、閉じるまで待つ）。
    /// ロック解除・本人確認の画面が出ている間は、それ自体がロックされた状態なので何もしない。
    /// </summary>
    private void CheckIdleLock()
    {
        if (_session is not { } session || _main is not { } main || _unlock is not null) return;
        var settings = session.Vault.Settings;
        if (!settings.AutoLockEnabled || settings.AutoLockMinutes <= 0) return;

        var idle = settings.AutoLockMode == AutoLockMode.System
            ? NativeMethods.GetSystemIdleTime()
            : DateTime.UtcNow - _lastActivity;
        if (idle < TimeSpan.FromMinutes(settings.AutoLockMinutes)) return;

        var dialogs = main.OwnedWindows.Cast<Window>().ToList();
        if (dialogs.Count > 0)
        {
            foreach (var dialog in dialogs)
                dialog.Close();
            return;
        }
        _showUnlockAfterLock = false;
        main.RequestClose(MainCloseReason.AutoLock);
    }

    private void ExitApp()
    {
        _exiting = true;
        if (_unlock is not null)
        {
            _unlock.Close(); // ShowDialog から戻った先で終了処理を続ける
            return;
        }
        if (_main is not null)
        {
            if (!_main.RequestClose(MainCloseReason.Exit))
                _exiting = false; // 未保存の確認でキャンセルされた
            return;
        }
        Shutdown();
    }

    // ---- トレイ・ホットキーのメニュー ----

    private void BuildTrayMenu(Forms.ToolStripItemCollection items, IntPtr pasteTarget)
    {
        _lastActivity = DateTime.UtcNow; // トレイ・ホットキーのメニューを開くのも KeyNest の操作
        items.Clear();
        if (_session is null)
        {
            items.Add(Bold(new Forms.ToolStripMenuItem("ロック解除...", null, (_, _) => ShowMainWindow())));
        }
        else if (IsProtected && _settings.Current.BlockMenuWhileProtected)
        {
            items.Add(new Forms.ToolStripMenuItem("表示するにはマスターパスワードが必要です") { Enabled = false });
            items.Add(Bold(new Forms.ToolStripMenuItem("KeyNest を表示...", null, (_, _) => ShowMainWindow())));
        }
        else
        {
            var paste = pasteTarget != IntPtr.Zero && _settings.Current.PasteFromHotkeyMenu ? pasteTarget : IntPtr.Zero;
            _autoTypeTarget = pasteTarget; // 自動入力は貼り付けの設定に関係なく、ホットキーで開いたときだけ使える
            AddGroupItems(items, _session.Vault.Root, paste);
            if (items.Count == 0)
                items.Add(new Forms.ToolStripMenuItem("（エントリがありません）") { Enabled = false });
            AddPersonalInfoItems(items, _session.Vault.Personal, paste);
            items.Add(new Forms.ToolStripSeparator());
            items.Add(Bold(new Forms.ToolStripMenuItem("KeyNest を表示", null, (_, _) => ShowMainWindow())));
        }

        if (_session is not null)
            items.Add(new Forms.ToolStripMenuItem("ロック", null, (_, _) => Lock(showUnlock: false)));
        items.Add(new Forms.ToolStripSeparator());
        items.Add(new Forms.ToolStripMenuItem("終了", null, (_, _) => ExitApp()));
    }

    /// <summary>ツリーと同じ並び（子グループ → エントリ）。エントリの無いグループは出さない。</summary>
    private void AddGroupItems(Forms.ToolStripItemCollection items, Group group, IntPtr pasteTarget)
    {
        foreach (var child in group.Groups.Where(g => g.AllEntries().Any()))
        {
            var item = new Forms.ToolStripMenuItem(MenuText(child.Name));
            AddGroupItems(item.DropDownItems, child, pasteTarget);
            items.Add(item);
        }
        foreach (var entry in group.Entries)
            items.Add(CreateEntryItem(entry, pasteTarget));
    }

    /// <summary>「メニューに表示」にした、値のある個人情報をサブメニューにまとめる。</summary>
    private void AddPersonalInfoItems(Forms.ToolStripItemCollection items, PersonalInfo personal, IntPtr pasteTarget)
    {
        var shown = personal.Items.Where(i => i.ShowInMenu && i.Value.Length > 0).ToList();
        if (shown.Count == 0) return;

        var menu = new Forms.ToolStripMenuItem("個人情報");
        foreach (var item in shown)
        {
            var value = item.Value;
            menu.DropDownItems.Add(new Forms.ToolStripMenuItem(
                MenuText($"{PersonalInfoFields.NameOf(item.Key)}: {value}"), null, (_, _) => Deliver(value, pasteTarget)));
        }
        items.Add(new Forms.ToolStripSeparator());
        items.Add(menu);
    }

    private Forms.ToolStripMenuItem CreateEntryItem(Entry entry, IntPtr pasteTarget)
    {
        var item = new Forms.ToolStripMenuItem(MenuText(entry.Title.Length > 0 ? entry.Title : "（無題）"));
        var verb = pasteTarget != IntPtr.Zero ? "貼り付け" : "コピー";

        // ホットキーで開いたときは、直前のウィンドウへの自動入力を先頭に置く
        if (_autoTypeTarget != IntPtr.Zero && _session is { } session)
        {
            var target = _autoTypeTarget;
            var template = session.Vault.Settings.ResolveAutoType(entry.AutoTypeId);
            item.DropDownItems.Add(Bold(new Forms.ToolStripMenuItem(MenuText($"自動入力（{template.Name}）"), null,
                async (_, _) => await AutoTypeFromMenuAsync(entry, target))));
            item.DropDownItems.Add(new Forms.ToolStripSeparator());
        }

        foreach (var field in entry.Fields.Where(f => f.Value.Length > 0))
        {
            // 伏せ字の欄は値をメニューに出さない
            var text = field.IsMasked ? $"{field.Label}（{verb}）" : $"{field.Label}: {field.Value}";
            var value = field.Value;
            item.DropDownItems.Add(new Forms.ToolStripMenuItem(MenuText(text), null, (_, _) => Deliver(value, pasteTarget)));
        }
        if (entry.Email.Length > 0)
        {
            var email = entry.Email;
            item.DropDownItems.Add(new Forms.ToolStripMenuItem(MenuText($"メール: {email}"), null, (_, _) => Deliver(email, pasteTarget)));
        }
        if (entry.SerialNumber.Length > 0)
        {
            var serial = entry.SerialNumber;
            item.DropDownItems.Add(new Forms.ToolStripMenuItem(MenuText($"シリアル番号: {serial}"), null, (_, _) => Deliver(serial, pasteTarget)));
        }
        if (entry.Url.Length > 0)
        {
            var url = entry.Url;
            if (item.DropDownItems.Count > 0) item.DropDownItems.Add(new Forms.ToolStripSeparator());
            item.DropDownItems.Add(new Forms.ToolStripMenuItem("URL を開く", null, (_, _) =>
            {
                if (ShellLauncher.OpenUrl(url) is { } error)
                    MessageDialog.Show(error, "KeyNest", MessageBoxButton.OK, MessageBoxImage.Warning);
            }));
        }
        if (item.DropDownItems.Count == 0)
            item.DropDownItems.Add(new Forms.ToolStripMenuItem("（値がありません）") { Enabled = false });
        return item;
    }

    private async Task AutoTypeFromMenuAsync(Entry entry, IntPtr target)
    {
        if (_session is not { } session) return;
        var settings = session.Vault.Settings;
        var steps = KeyNest.Core.AutoTyping.AutoTypePlanner.Plan(
            settings.ResolveAutoType(entry.AutoTypeId), entry, session.Vault.Personal, settings.AutoTypeDelayMs);
        if (steps.Count == 0) return;
        try
        {
            await _autoType.TypeAsync(steps, target);
        }
        catch (AutoTypeException ex)
        {
            MessageDialog.Show(ex.Message, "KeyNest", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>クリップボードにコピーし、貼り付け先があればそのウィンドウに Ctrl+V を送る。</summary>
    private async void Deliver(string value, IntPtr pasteTarget)
    {
        var seconds = _session?.Vault.Settings.ClipboardClearSeconds ?? 15;
        if (!_clipboard.Copy(value, seconds))
        {
            MessageDialog.Show("クリップボードにアクセスできませんでした。", "KeyNest", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (pasteTarget == IntPtr.Zero || !NativeMethods.IsWindow(pasteTarget)) return;

        NativeMethods.SetForegroundWindow(pasteTarget);
        await Task.Delay(150); // メニューが閉じて貼り付け先に入力が戻るのを待つ
        NativeMethods.SendCtrlV();
    }

    private static Forms.ToolStripMenuItem Bold(Forms.ToolStripMenuItem item)
    {
        item.Font = new System.Drawing.Font(item.Font, System.Drawing.FontStyle.Bold);
        return item;
    }

    /// <summary>長すぎる文字列を省略し、&amp; がアクセスキー扱いされないようにする。</summary>
    private static string MenuText(string text)
    {
        if (text.Length > MenuTextMaxLength) text = text[..MenuTextMaxLength] + "…";
        return text.Replace("&", "&&");
    }
}
