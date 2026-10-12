using System.ComponentModel;
using System.Diagnostics;
using System.Net.Mail;

namespace KeyNest.App.Services;

/// <summary>URL・メールを既定のアプリで開く。失敗時はエラーメッセージを返す（成功なら null）。</summary>
public static class ShellLauncher
{
    public static string? OpenUrl(string raw)
    {
        var url = raw.Trim();
        if (!url.Contains("://")) url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return "URL の形式が正しくありません。";
        return Start(uri.AbsoluteUri, "ブラウザを起動できませんでした。");
    }

    /// <summary>既定のメールソフトで新規メールを開く（宛先のみ指定）。</summary>
    public static string? OpenMail(string raw)
    {
        // MailAddress で検証し、"?subject=..." などの付加パラメーターが混ざらないアドレス部分だけを使う
        if (!MailAddress.TryCreate(raw.Trim(), out var address)
            || address.Address.IndexOfAny(['?', '&', '#', ' ']) >= 0)
            return "メールアドレスの形式が正しくありません。";
        return Start("mailto:" + address.Address, "メールソフトを起動できませんでした。既定のメールアプリが設定されているか確認してください。");
    }

    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
        ".msi", ".msp", ".scr", ".pif", ".lnk", ".hta", ".cpl", ".jar", ".reg",
    };

    /// <summary>開くとプログラムが実行される種類のファイルか。</summary>
    public static bool IsExecutable(string path)
    {
        try
        {
            return ExecutableExtensions.Contains(System.IO.Path.GetExtension(path));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>関連付けられたアプリでファイル（またはフォルダ）を開く。</summary>
    public static string? OpenFile(string path)
    {
        if (path.Length == 0 || !(System.IO.File.Exists(path) || System.IO.Directory.Exists(path)))
            return $"ファイルが見つかりません。\n{path}";
        return Start(path, "ファイルを開けませんでした。関連付けられたアプリがあるか確認してください。");
    }

    private static string? Start(string target, string failureMessage)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return null;
        }
        catch (Win32Exception)
        {
            return failureMessage;
        }
    }
}
