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
    private readonly IntPtr _iconHandle;
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
            Icon = CreateIcon(out _iconHandle),
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

    /// <summary>実行時に鍵アイコンを描いて作る（アイコンファイルを持たないため）。</summary>
    private static Drawing.Icon CreateIcon(out IntPtr handle)
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var background = new Drawing.SolidBrush(Drawing.Color.FromArgb(0x1F, 0x8A, 0xD2));
            g.FillEllipse(background, 0, 0, 31, 31);

            using var font = CreateIconFont(18);
            using var foreground = new Drawing.SolidBrush(Drawing.Color.White);
            using var format = new Drawing.StringFormat
            {
                Alignment = Drawing.StringAlignment.Center,
                LineAlignment = Drawing.StringAlignment.Center,
            };
            g.DrawString("", font, foreground, new Drawing.RectangleF(0, 1, 32, 32), format);
        }
        handle = bitmap.GetHicon();
        return Drawing.Icon.FromHandle(handle);
    }

    private static Drawing.Font CreateIconFont(float size)
    {
        foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            var font = new Drawing.Font(name, size, Drawing.GraphicsUnit.Pixel);
            if (font.Name == name) return font; // 見つからないと別フォントに置き換わる
            font.Dispose();
        }
        return new Drawing.Font(Drawing.SystemFonts.DefaultFont.FontFamily, size, Drawing.GraphicsUnit.Pixel);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        NativeMethods.DestroyIcon(_iconHandle);
    }
}
