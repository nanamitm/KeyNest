using KeyNest.Core.Models;

namespace KeyNest.Core.AutoTyping;

/// <summary>自動入力で行う操作の1つ。</summary>
public abstract record AutoTypeStep;

/// <summary>文字列を入力する（改行は Enter、タブは Tab として送る）。</summary>
public sealed record TypeTextStep(string Text) : AutoTypeStep;

/// <summary>特殊キーを押す。</summary>
public sealed record KeyStep(AutoTypeKey Key, bool Shift = false) : AutoTypeStep;

/// <summary>URL をブラウザで開く。</summary>
public sealed record OpenUrlStep(string Url) : AutoTypeStep;

/// <summary>指定した時間だけ待つ。</summary>
public sealed record DelayStep(int Milliseconds) : AutoTypeStep;

public enum AutoTypeKey
{
    Tab,
    Enter,
    Space,
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    Delete,
}

/// <summary>
/// 貼り付けタイプのコマンドを、エントリ・個人情報の値を埋め込んだ操作の並びに変換する。
/// 実際のキー送信はアプリ側で行う（ここは画面や OS に依存しない）。
/// </summary>
public static class AutoTypePlanner
{
    public static IReadOnlyList<AutoTypeStep> Plan(AutoTypeTemplate template, Entry entry, PersonalInfo personal, int delayMs)
    {
        var steps = new List<AutoTypeStep>();
        foreach (var command in template.Commands)
        {
            if (ToStep(command, entry, personal, delayMs) is { } step)
                steps.Add(step);
        }
        return steps;
    }

    /// <summary>値が空の文字列・URL の無い「ブラウザで開く」・不明なコマンドは null（何もしない）。</summary>
    private static AutoTypeStep? ToStep(string command, Entry entry, PersonalInfo personal, int delayMs)
    {
        if (AutoTypeCommands.PersonalKeyOf(command) is { } personalKey)
            return Text(personal.Get(personalKey));

        for (var i = 0; i < entry.Fields.Count; i++)
        {
            if (command == AutoTypeCommands.FieldValue(i)) return Text(entry.Fields[i].Value);
            if (command == AutoTypeCommands.FieldLabel(i)) return Text(entry.Fields[i].Label);
        }

        return command switch
        {
            AutoTypeCommands.Title => Text(entry.Title),
            AutoTypeCommands.Url => Text(entry.Url),
            AutoTypeCommands.Email => Text(entry.Email),
            AutoTypeCommands.File => Text(entry.FilePath),
            AutoTypeCommands.Serial => Text(entry.SerialNumber),
            AutoTypeCommands.Tab => new KeyStep(AutoTypeKey.Tab),
            AutoTypeCommands.ShiftTab => new KeyStep(AutoTypeKey.Tab, Shift: true),
            AutoTypeCommands.Enter => new KeyStep(AutoTypeKey.Enter),
            AutoTypeCommands.Space => new KeyStep(AutoTypeKey.Space),
            AutoTypeCommands.Left => new KeyStep(AutoTypeKey.Left),
            AutoTypeCommands.Right => new KeyStep(AutoTypeKey.Right),
            AutoTypeCommands.Up => new KeyStep(AutoTypeKey.Up),
            AutoTypeCommands.Down => new KeyStep(AutoTypeKey.Down),
            AutoTypeCommands.Home => new KeyStep(AutoTypeKey.Home),
            AutoTypeCommands.End => new KeyStep(AutoTypeKey.End),
            AutoTypeCommands.ShiftHome => new KeyStep(AutoTypeKey.Home, Shift: true),
            AutoTypeCommands.ShiftEnd => new KeyStep(AutoTypeKey.End, Shift: true),
            AutoTypeCommands.Delete => new KeyStep(AutoTypeKey.Delete),
            AutoTypeCommands.OpenUrl => entry.Url.Trim().Length > 0 ? new OpenUrlStep(entry.Url.Trim()) : null,
            AutoTypeCommands.Delay => new DelayStep(delayMs),
            _ => null,
        };
    }

    private static TypeTextStep? Text(string value) => value.Length > 0 ? new TypeTextStep(value) : null;
}
