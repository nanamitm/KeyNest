using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace KeyNest.App.Services;

/// <summary>
/// タスクトレイのアイコンとメニュー。メニューの中身は開くたびに <see cref="BuildMenu"/> で作り直す。
/// ホットキーからも同じメニューをカーソル位置に出す。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ContextMenuStrip _menu;
    private IntPtr _pendingPasteTarget;

    public TrayIconService()
    {
        _menu = new Forms.ContextMenuStrip();
        _menu.Opening += (_, e) =>
        {
            var target = _pendingPasteTarget;
            _pendingPasteTarget = IntPtr.Zero;
            BuildMenu?.Invoke(_menu.Items, target);
            e.Cancel = _menu.Items.Count == 0;
        };

        _icon = new Forms.NotifyIcon
        {
            Text = "KeyNest",
            Icon = LoadIcon(),
            ContextMenuStrip = _menu,
        };
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                OpenRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>アイコンのダブルクリック。</summary>
    public event EventHandler? OpenRequested;

    /// <summary>
    /// メニューを開く直前に呼ばれる。第2引数は貼り付け先のウィンドウ（ホットキーで開いたとき）、
    /// トレイアイコンから開いたときは <see cref="IntPtr.Zero"/>（コピーのみ）。
    /// </summary>
    public Action<Forms.ToolStripItemCollection, IntPtr>? BuildMenu { get; set; }

    public bool Visible
    {
        get => _icon.Visible;
        set => _icon.Visible = value;
    }

    public string Text
    {
        set => _icon.Text = value.Length > 63 ? value[..63] : value; // NotifyIcon の上限
    }

    /// <summary>ホットキー用: 直前の前面ウィンドウを貼り付け先として記憶し、カーソル位置にメニューを出す。</summary>
    public void ShowMenuAtCursor()
    {
        _pendingPasteTarget = NativeMethods.GetForegroundWindow();
        _menu.Show(Forms.Cursor.Position);
        // 前面にしておかないと、メニュー外をクリックしても閉じない
        NativeMethods.SetForegroundWindow(_menu.Handle);
    }

    /// <summary>アプリのアイコン（Assets/KeyNest.ico）から、通知領域に合ったサイズを読み込む。</summary>
    private static Drawing.Icon LoadIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/KeyNest.ico"))
            ?? throw new InvalidOperationException("アイコンのリソースが見つかりません。");
        using var stream = resource.Stream;
        return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.Dispose();
        _menu.Dispose();
    }
}
