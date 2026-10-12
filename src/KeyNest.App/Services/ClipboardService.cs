using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace KeyNest.App.Services;

/// <summary>コピーした値を一定時間後に自動消去するクリップボード操作。</summary>
public sealed class ClipboardService
{
    private readonly DispatcherTimer _timer = new();
    private string? _copied;

    public ClipboardService()
    {
        _timer.Tick += (_, _) => ClearIfUnchanged();
    }

    public bool Copy(string text, int clearAfterSeconds)
    {
        if (string.IsNullOrEmpty(text)) return false;
        try
        {
            Clipboard.SetDataObject(text, copy: true);
        }
        catch (COMException)
        {
            return false; // 他のアプリがクリップボードを開いている
        }

        _timer.Stop();
        _copied = text;
        if (clearAfterSeconds > 0)
        {
            _timer.Interval = TimeSpan.FromSeconds(clearAfterSeconds);
            _timer.Start();
        }
        return true;
    }

    /// <summary>ユーザーが別のものをコピーしていたら消さない。</summary>
    public void ClearIfUnchanged()
    {
        _timer.Stop();
        if (_copied is null) return;
        try
        {
            if (Clipboard.ContainsText() && Clipboard.GetText() == _copied)
                Clipboard.Clear();
        }
        catch (COMException)
        {
        }
        _copied = null;
    }
}
