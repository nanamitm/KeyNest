using System.Security.Cryptography;

namespace KeyNest.Core.Security;

public sealed record PasswordOptions
{
    public const int MinLength = 1;
    public const int MaxLength = 128;

    public int Length { get; init; } = 16;
    public bool Uppercase { get; init; } = true;
    public bool Lowercase { get; init; } = true;
    public bool Digits { get; init; } = true;
    public bool Symbols { get; init; } = true;

    /// <summary>I, l, 1, O, 0 など見間違えやすい文字を除外する。</summary>
    public bool ExcludeAmbiguous { get; init; }
}

public static class PasswordGenerator
{
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Digit = "0123456789";
    private const string Symbol = "!#$%&()*+,-./:;<=>?@[]^_{|}~";
    private const string Ambiguous = "Il1O0o|";

    /// <summary>
    /// 選択した文字種を最低1文字ずつ含むパスワードを生成する。
    /// 長さが文字種の数より短いときは、この保証はせず全文字種から選ぶ。
    /// </summary>
    public static string Generate(PasswordOptions options)
    {
        var sets = new List<string>();
        if (options.Uppercase) sets.Add(Upper);
        if (options.Lowercase) sets.Add(Lower);
        if (options.Digits) sets.Add(Digit);
        if (options.Symbols) sets.Add(Symbol);

        if (options.ExcludeAmbiguous)
            sets = sets.Select(s => new string(s.Where(c => !Ambiguous.Contains(c)).ToArray())).ToList();

        if (sets.Count == 0)
            throw new ArgumentException("文字種を1つ以上選択してください。", nameof(options));
        if (options.Length is < PasswordOptions.MinLength or > PasswordOptions.MaxLength)
            throw new ArgumentException($"長さは {PasswordOptions.MinLength}〜{PasswordOptions.MaxLength} にしてください。", nameof(options));

        var all = string.Concat(sets);
        var chars = new char[options.Length];
        var guaranteed = options.Length >= sets.Count ? sets.Count : 0;
        for (var i = 0; i < guaranteed; i++)
            chars[i] = sets[i][RandomNumberGenerator.GetInt32(sets[i].Length)];
        for (var i = guaranteed; i < chars.Length; i++)
            chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];

        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }
}
