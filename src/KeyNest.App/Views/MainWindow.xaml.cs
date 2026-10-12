using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using KeyNest.App.Services;
using KeyNest.App.ViewModels;
using KeyNest.Core.Storage;

namespace KeyNest.App.Views;

/// <summary>メイン画面が閉じられた理由。App が後処理（ロック解除画面に戻る・終了）を決める。</summary>
public enum MainCloseReason
{
    /// <summary>ユーザーが閉じた（トレイ常駐でないとき）。</summary>
    None,
    Lock,
    Exit,

    /// <summary>無操作による自動ロック。未保存の変更は確認せずに保存する。</summary>
    AutoLock,
}

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly AppSettingsStore _appSettings;
    private readonly AutoTypeService _autoType;
    private readonly ForegroundTracker _foreground;
    private bool _minimizingForAutoType;
    private bool _closed;
    private bool _wasMaximized;

    private Point _dragStart;
    private TreeNodeViewModel? _dragCandidate;
    private GroupViewModel? _dropTarget;

    public MainWindow(VaultSession session, ClipboardService clipboard, AppSettingsStore appSettings, VaultHelloController hello,
        AutoTypeService autoType, ForegroundTracker foreground)
    {
        InitializeComponent();
        _appSettings = appSettings;
        _autoType = autoType;
        _foreground = foreground;
        RestorePlacement();
        _viewModel = new MainViewModel(session, new DialogService(this, appSettings, hello, session), clipboard);
        _viewModel.LockRequested += (_, _) => LockRequested?.Invoke(this, EventArgs.Empty);
        _viewModel.AutoTypeRequested += async (_, steps) => await RunAutoTypeAsync(steps);
        DataContext = _viewModel;

        InputBindings.Add(new KeyBinding(new RelayCommand(() => SearchBox.Focus()), Key.F, ModifierKeys.Control));
    }

    /// <summary>ツールバーの「ロック」。App が <see cref="RequestClose"/> で閉じる。</summary>
    public event EventHandler? LockRequested;

    /// <summary>true なら閉じる・最小化でウィンドウを隠す（タスクトレイ常駐）。</summary>
    public bool HideToTray { get; set; }

    public MainCloseReason CloseReason { get; private set; }

    /// <summary>
    /// 理由を指定して閉じる。未保存の確認でキャンセルされたら false。
    /// トレイ常駐中でも、ロック・終了のときは隠さずに閉じる。
    /// </summary>
    public bool RequestClose(MainCloseReason reason)
    {
        CloseReason = reason;
        Close();
        if (!_closed) CloseReason = MainCloseReason.None;
        return _closed;
    }

    private void NodeTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
        _viewModel.SelectedNode = e.NewValue as TreeNodeViewModel;

    /// <summary>エントリのダブルクリックで最初の伏せ字の欄をコピー（グループは通常どおり開閉）。</summary>
    private void NodeTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not EntryViewModel) return;
        _viewModel.CopyDefaultField();
        e.Handled = true;
    }

    // ---- ドラッグ＆ドロップでエントリ・グループを別グループへ移動 ----

    private void NodeTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragCandidate = null;
        var source = e.OriginalSource as DependencyObject;
        if (FindAncestor<ToggleButton>(source) is not null || FindAncestor<ScrollBar>(source) is not null) return;

        if (FindAncestor<TreeViewItem>(source)?.DataContext is TreeNodeViewModel node and not GroupViewModel { IsRoot: true })
        {
            _dragCandidate = node;
            _dragStart = e.GetPosition(NodeTree);
        }
    }

    private void NodeTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is null || e.LeftButton != MouseButtonState.Pressed) return;

        var delta = e.GetPosition(NodeTree) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var node = _dragCandidate;
        _dragCandidate = null;
        DragDrop.DoDragDrop(NodeTree, new DataObject(typeof(TreeNodeViewModel), node), DragDropEffects.Move);
        SetDropTarget(null);
    }

    private void NodeTree_DragOver(object sender, DragEventArgs e)
    {
        var target = GetDropGroup(e);
        var canDrop = e.Data.GetData(typeof(TreeNodeViewModel)) is TreeNodeViewModel node
                      && target is not null && MainViewModel.CanMove(node, target);
        e.Effects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
        SetDropTarget(canDrop ? target : null);
        AutoScroll(e.GetPosition(NodeTree));
        e.Handled = true;
    }

    private void NodeTree_DragLeave(object sender, DragEventArgs e) => SetDropTarget(null);

    private void NodeTree_Drop(object sender, DragEventArgs e)
    {
        SetDropTarget(null);
        if (e.Data.GetData(typeof(TreeNodeViewModel)) is TreeNodeViewModel node && GetDropGroup(e) is { } target)
            _viewModel.Move(node, target);
        e.Handled = true;
    }

    /// <summary>ドロップ先のグループ。エントリの上に落とした場合はそのエントリのグループ。</summary>
    private static GroupViewModel? GetDropGroup(DragEventArgs e) =>
        FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)?.DataContext switch
        {
            GroupViewModel g => g,
            EntryViewModel entry => entry.Parent,
            _ => null,
        };

    private void SetDropTarget(GroupViewModel? target)
    {
        if (_dropTarget == target) return;
        if (_dropTarget is not null) _dropTarget.IsDropTarget = false;
        _dropTarget = target;
        if (target is not null) target.IsDropTarget = true;
    }

    /// <summary>ドラッグ中にツリーの上端・下端へ近づいたらスクロールする。</summary>
    private void AutoScroll(Point position)
    {
        const double edge = 24;
        if (FindDescendant<ScrollViewer>(NodeTree) is not { } scroll) return;
        if (position.Y < edge) scroll.LineUp();
        else if (position.Y > NodeTree.ActualHeight - edge) scroll.LineDown();
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null and not T)
            d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }

    /// <summary>
    /// KeyNest に切り替える直前に使っていたウィンドウ（<see cref="ForegroundTracker"/> が記録）へ送る。
    /// 設定で最小化する場合、トレイ常駐中でも復元時のパスワード確認が挟まらないよう、トレイには格納せず最小化にとどめる。
    /// </summary>
    private async Task RunAutoTypeAsync(IReadOnlyList<KeyNest.Core.AutoTyping.AutoTypeStep> steps)
    {
        var target = _foreground.LastExternalWindow;
        if (_appSettings.Current.MinimizeOnAutoType)
        {
            _minimizingForAutoType = true;
            try
            {
                WindowState = WindowState.Minimized;
            }
            finally
            {
                _minimizingForAutoType = false;
            }
            // 記録が無ければ、最小化で前面に戻ったウィンドウへ送る（IntPtr.Zero）
        }
        else if (target == IntPtr.Zero)
        {
            const string message = "入力先のウィンドウが見つかりません。入力したいウィンドウを一度選んでから、KeyNest に戻って自動入力してください。";
            _viewModel.ReportAutoType(message);
            MessageDialog.Show(this, message, "KeyNest", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var title = await _autoType.TypeAsync(steps, target);
            _viewModel.ReportAutoType(title.Length > 0 ? $"「{title}」へ自動入力しました" : "自動入力しました");
        }
        catch (AutoTypeException ex)
        {
            _viewModel.ReportAutoType(ex.Message);
            MessageDialog.Show(ex.Message, "KeyNest", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---- ウィンドウサイズ・位置の保存 ----

    /// <summary>前回の配置を復元する。画面外になる配置（モニター構成の変更など）は使わない。</summary>
    private void RestorePlacement()
    {
        var settings = _appSettings.Current;
        if (!settings.SaveWindowPlacement || settings.MainWindowPlacement is not { } p) return;

        if (p.TreeWidth >= TreeColumn.MinWidth)
            TreeColumn.Width = new GridLength(p.TreeWidth);
        if (!IsOnScreen(p)) return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = p.Left;
        Top = p.Top;
        Width = Math.Max(p.Width, MinWidth);
        Height = Math.Max(p.Height, MinHeight);
        if (p.Maximized)
            WindowState = WindowState.Maximized; // 上の位置にあるモニターで最大化される
    }

    /// <summary>タイトルバー付近が仮想画面（全モニターを含む範囲）内に十分見えているか。</summary>
    private static bool IsOnScreen(WindowPlacement p)
    {
        const double minVisible = 100;
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var titleBar = new Rect(p.Left, p.Top, p.Width, 40);
        var visible = Rect.Intersect(screen, titleBar);
        return p.Width > 0 && p.Height > 0 && !visible.IsEmpty && visible.Width >= minVisible;
    }

    private void SavePlacement()
    {
        if (!_appSettings.Current.SaveWindowPlacement) return;
        // 最大化・最小化中は RestoreBounds が通常時のサイズ
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (bounds.IsEmpty) return;
        var maximized = WindowState == WindowState.Maximized || (WindowState == WindowState.Minimized && _wasMaximized);
        var placement = new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height, maximized, TreeColumn.ActualWidth);
        _appSettings.SaveQuietly(s => s.MainWindowPlacement = placement);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState != WindowState.Minimized)
            _wasMaximized = WindowState == WindowState.Maximized;
        else if (HideToTray && !_minimizingForAutoType)
        {
            SavePlacement();
            Hide();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (CloseReason == MainCloseReason.None && HideToTray)
        {
            // 閉じるボタンではトレイに格納するだけ（データは開いたまま）
            e.Cancel = true;
            SavePlacement();
            Hide();
            return;
        }
        if (!_viewModel.IsDirty) return;

        if (CloseReason == MainCloseReason.AutoLock)
        {
            // 席を外している間に確認画面で止まると、データを開いたままになるので確認せず保存する。
            // 保存できなければロックしない（エラーは Save が表示する）
            if (!_viewModel.Save())
                e.Cancel = true;
            return;
        }

        var result = MessageDialog.Show(this, "変更を保存しますか？", "KeyNest",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel || (result == MessageBoxResult.Yes && !_viewModel.Save()))
            e.Cancel = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        SavePlacement(); // 未保存の確認でキャンセルされた場合は保存しない
        base.OnClosed(e);
    }
}
