using System.Diagnostics;
using KeyNest.Core.AutoTyping;

namespace KeyNest.App.Services;

/// <summary>自動入力を中止した理由（利用者に表示する）。</summary>
public sealed class AutoTypeException(string message) : Exception(message);

/// <summary>
/// 自動入力の操作の並びを、前面のウィンドウへキー入力として送る。
/// 誤入力を防ぐため、修飾キーが離されるまで待ち、途中で入力先のウィンドウが変わったら中止する。
/// </summary>
public sealed class AutoTypeService
{
    /// <summary>1文字・1キーごとの間隔。速すぎると取りこぼすアプリがある。</summary>
    private const int KeyIntervalMs = 8;
    private const int ModifierWaitTimeoutMs = 3000;
    private const int ForegroundWaitTimeoutMs = 2000;

    /// <param name="target">
    /// 入力先のウィンドウ。IntPtr.Zero なら、KeyNest 以外のウィンドウが前面になるのを待ってそこへ送る
    /// （メイン画面を最小化した直後など）。
    /// </param>
    /// <returns>入力したウィンドウのタイトル。</returns>
    public async Task<string> TypeAsync(IReadOnlyList<AutoTypeStep> steps, IntPtr target)
    {
        // ホットキーの Ctrl・Alt などを押したままだと、送ったキーと組み合わさって誤操作になる
        if (!await WaitAsync(() => !NativeMethods.IsAnyModifierPressed(), ModifierWaitTimeoutMs))
            throw new AutoTypeException("Ctrl・Alt・Shift・Windows キーが押されたままのため、自動入力を中止しました。");

        if (target != IntPtr.Zero)
        {
            if (!NativeMethods.IsWindow(target))
                throw new AutoTypeException("入力先のウィンドウが見つかりません。");
            NativeMethods.SetForegroundWindow(target);
        }
        if (!await WaitAsync(() => IsOtherAppWindow(NativeMethods.GetForegroundWindow()), ForegroundWaitTimeoutMs))
            throw new AutoTypeException("入力先のウィンドウを特定できませんでした。入力したいウィンドウを選んでから、もう一度お試しください。");
        await Task.Delay(100); // 前面になった直後は入力を受け付けないことがある

        IntPtr? expected = NativeMethods.GetForegroundWindow();
        var title = NativeMethods.GetWindowTitle(expected.Value);
        foreach (var step in steps)
        {
            switch (step)
            {
                case TypeTextStep text:
                    EnsureForeground(ref expected);
                    foreach (var c in text.Text)
                    {
                        EnsureForeground(ref expected);
                        NativeMethods.SendChar(c);
                        await Task.Delay(KeyIntervalMs);
                    }
                    break;
                case KeyStep key:
                    EnsureForeground(ref expected);
                    NativeMethods.SendKey(key.Key, key.Shift);
                    await Task.Delay(KeyIntervalMs);
                    break;
                case OpenUrlStep open:
                    if (ShellLauncher.OpenUrl(open.Url) is { } error)
                        throw new AutoTypeException(error);
                    expected = null; // ブラウザが前面になるので、次の入力の時点の前面ウィンドウを入力先にする
                    break;
                case DelayStep delay:
                    await Task.Delay(delay.Milliseconds);
                    break;
            }
        }
        return title;
    }

    /// <summary>入力先が途中で変わっていたら中止する（パスワードを別の場所へ打ち込まないため）。</summary>
    private static void EnsureForeground(ref IntPtr? expected)
    {
        var current = NativeMethods.GetForegroundWindow();
        if (expected is null)
        {
            if (!IsOtherAppWindow(current))
                throw new AutoTypeException("入力先のウィンドウを特定できなかったため、自動入力を中止しました。");
            expected = current;
            return;
        }
        if (current != expected)
            throw new AutoTypeException("入力中に別のウィンドウに切り替わったため、自動入力を中止しました。");
    }

    private static bool IsOtherAppWindow(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && NativeMethods.GetProcessIdOfWindow(hwnd) != Environment.ProcessId;

    private static async Task<bool> WaitAsync(Func<bool> condition, int timeoutMs)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > timeoutMs) return false;
            await Task.Delay(50);
        }
        return true;
    }
}
