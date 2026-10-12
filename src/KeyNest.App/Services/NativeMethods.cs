using System.Runtime.InteropServices;
using KeyNest.Core.AutoTyping;

namespace KeyNest.App.Services;

/// <summary>ホットキー登録・ウィンドウ切り替え・キー送信に使う Win32 API。</summary>
internal static class NativeMethods
{
    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassNameNative(IntPtr hWnd, System.Text.StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    private static extern int GetWindowTextNative(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);

    public static string GetClassName(IntPtr hWnd)
    {
        var sb = new System.Text.StringBuilder(256);
        GetClassNameNative(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetWindowTitle(IntPtr hWnd)
    {
        var sb = new System.Text.StringBuilder(512);
        GetWindowTextNative(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    /// <summary>パソコン全体でキーボード・マウスを最後に操作してからの時間。</summary>
    public static TimeSpan GetSystemIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        // TickCount は約49日で一周するので符号なしの差で計算する
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_TAB = 0x09;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_SPACE = 0x20;
    private const ushort VK_END = 0x23;
    private const ushort VK_HOME = 0x24;
    private const ushort VK_LEFT = 0x25;
    private const ushort VK_UP = 0x26;
    private const ushort VK_RIGHT = 0x27;
    private const ushort VK_DOWN = 0x28;
    private const ushort VK_DELETE = 0x2E;
    private const ushort VK_V = 0x56;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;

    public static int GetProcessIdOfWindow(IntPtr hWnd)
    {
        GetWindowThreadProcessId(hWnd, out var processId);
        return (int)processId;
    }

    /// <summary>Ctrl・Alt・Shift・Windows キーのどれかが押されているか。</summary>
    public static bool IsAnyModifierPressed() =>
        new[] { VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN }.Any(vk => (GetAsyncKeyState(vk) & 0x8000) != 0);

    /// <summary>
    /// 1文字を入力する。キーボード配列や日本語入力の状態に左右されないよう Unicode として送る。
    /// 改行は Enter、タブは Tab キーとして送る。
    /// </summary>
    public static void SendChar(char c)
    {
        switch (c)
        {
            case '\r':
                return; // \r\n の \r は無視して \n で Enter を送る
            case '\n':
                SendKey(AutoTypeKey.Enter, shift: false);
                return;
            case '\t':
                SendKey(AutoTypeKey.Tab, shift: false);
                return;
        }
        INPUT[] inputs = [Unicode(c, down: true), Unicode(c, down: false)];
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void SendKey(AutoTypeKey key, bool shift)
    {
        var (vk, extended) = key switch
        {
            AutoTypeKey.Tab => (VK_TAB, false),
            AutoTypeKey.Enter => (VK_RETURN, false),
            AutoTypeKey.Space => (VK_SPACE, false),
            // 矢印・Home・End・Delete はテンキー側と区別するため拡張キーとして送る
            AutoTypeKey.Left => (VK_LEFT, true),
            AutoTypeKey.Right => (VK_RIGHT, true),
            AutoTypeKey.Up => (VK_UP, true),
            AutoTypeKey.Down => (VK_DOWN, true),
            AutoTypeKey.Home => (VK_HOME, true),
            AutoTypeKey.End => (VK_END, true),
            AutoTypeKey.Delete => (VK_DELETE, true),
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
        var inputs = new List<INPUT>();
        if (shift) inputs.Add(Key(VK_SHIFT, down: true));
        inputs.Add(Key(vk, down: true, extended));
        inputs.Add(Key(vk, down: false, extended));
        if (shift) inputs.Add(Key(VK_SHIFT, down: false));
        SendInput((uint)inputs.Count, [.. inputs], Marshal.SizeOf<INPUT>());
    }

    /// <summary>前面のウィンドウに Ctrl+V を送る。</summary>
    public static void SendCtrlV()
    {
        INPUT[] inputs =
        [
            Key(VK_CONTROL, down: true),
            Key(VK_V, down: true),
            Key(VK_V, down: false),
            Key(VK_CONTROL, down: false),
        ];
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT Key(ushort vk, bool down, bool extended = false) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT { wVk = vk, dwFlags = (down ? 0 : KEYEVENTF_KEYUP) | (extended ? KEYEVENTF_EXTENDEDKEY : 0) },
        },
    };

    private static INPUT Unicode(char c, bool down) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | (down ? 0 : KEYEVENTF_KEYUP) } },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    // MOUSEINPUT を含めて共用体のサイズを Win32 の INPUT と一致させる
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
