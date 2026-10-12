using System.Windows.Input;
using System.Windows.Interop;

namespace KeyNest.App.Services;

/// <summary>
/// システム全体のホットキー。メイン画面の有無（ロック中など）に関係なく受け取れるよう、
/// 専用のメッセージ専用ウィンドウで登録する。
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x4B4E;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private bool _registered;

    public HotkeyService()
    {
        _source = new HwndSource(new HwndSourceParameters("KeyNestHotkey") { ParentWindow = HwndMessage, WindowStyle = 0 });
        _source.AddHook(WndProc);
    }

    public event EventHandler? Pressed;

    /// <summary>登録する。他のアプリが使用中などで失敗したら false。</summary>
    public bool Register(ModifierKeys modifiers, Key key)
    {
        Unregister();
        // WPF の ModifierKeys（Alt=1, Control=2, Shift=4, Windows=8）は Win32 の MOD_* と同じ値
        var mods = (uint)modifiers | NativeMethods.MOD_NOREPEAT;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        _registered = NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, mods, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
