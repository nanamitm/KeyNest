using System.Runtime.InteropServices;

namespace KeyNest.App.Services;

/// <summary>
/// KeyNest 以外で最後に前面になったウィンドウを記録する（自動入力の入力先）。
/// タスクバーやスタートメニューなど、KeyNest へ切り替える途中で一瞬前面になるだけのシェルのウィンドウは除く。
/// </summary>
public sealed class ForegroundTracker : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    private static readonly HashSet<string> ShellWindowClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
        "Progman", "WorkerW", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "MultitaskingViewFrame",
        "ForegroundStaging", "TaskListThumbnailWnd",
    };

    private readonly WinEventProc _callback; // GC に回収されないよう保持する
    private readonly IntPtr _hook;
    private IntPtr _last;

    public ForegroundTracker()
    {
        _callback = OnForegroundChanged;
        // OUTOFCONTEXT の通知は、登録したスレッド（UI スレッド）のメッセージループで届く
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _callback, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        Remember(NativeMethods.GetForegroundWindow());
    }

    /// <summary>記録しているウィンドウ。閉じられた・非表示になった場合は IntPtr.Zero。</summary>
    public IntPtr LastExternalWindow =>
        _last != IntPtr.Zero && NativeMethods.IsWindow(_last) && NativeMethods.IsWindowVisible(_last) ? _last : IntPtr.Zero;

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time) =>
        Remember(hwnd);

    private void Remember(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || NativeMethods.GetProcessIdOfWindow(hwnd) == Environment.ProcessId) return;
        if (ShellWindowClasses.Contains(NativeMethods.GetClassName(hwnd))) return;
        _last = hwnd;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
            UnhookWinEvent(_hook);
    }

    private delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
}
