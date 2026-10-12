namespace KeyNest.Core.Models;

/// <summary>1件の登録情報。用途を決めない汎用欄を <see cref="FieldCount"/> 個持つ。</summary>
public sealed class Entry
{
    public const int FieldCount = 4;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public List<EntryField> Fields { get; set; } = CreateFields(VaultSettings.CreateFieldDefaults());
    public string Url { get; set; } = "";
    public string Email { get; set; } = "";

    /// <summary>関連ファイルのパス（ライセンスファイルなど）。</summary>
    public string FilePath { get; set; } = "";

    public string SerialNumber { get; set; } = "";
    public DateOnly? IssueDate { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public string Notes { get; set; } = "";

    /// <summary>
    /// 貼り付けタイプ（<see cref="VaultSettings.AutoTypes"/>）の ID。null または見つからない場合は一覧の先頭（既定）を使う。
    /// </summary>
    public Guid? AutoTypeId { get; set; }

    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.Now;

    /// <summary>設定の初期値から項目名・伏せ字をコピーした新しいエントリ。作成後は設定と連動しない。</summary>
    public static Entry Create(VaultSettings settings, string title = "") =>
        new() { Title = title, Fields = CreateFields(settings.FieldDefaults) };

    public static List<EntryField> CreateFields(IReadOnlyList<FieldDefault> defaults) =>
        Enumerable.Range(0, FieldCount).Select(i => EntryField.FromDefault(defaults[i])).ToList();

    /// <summary>欄の数を常に <see cref="FieldCount"/> にそろえる（読み込んだデータの補正用）。</summary>
    public void NormalizeFields(IReadOnlyList<FieldDefault> defaults)
    {
        Fields ??= [];
        while (Fields.Count < FieldCount) Fields.Add(EntryField.FromDefault(defaults[Fields.Count]));
        if (Fields.Count > FieldCount) Fields.RemoveRange(FieldCount, Fields.Count - FieldCount);
        foreach (var field in Fields)
            field.Label ??= "";
    }
}

/// <summary>汎用欄。項目名と伏せ字はエントリごとに自由に設定できる。</summary>
public sealed class EntryField
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public bool IsMasked { get; set; }

    public static EntryField FromDefault(FieldDefault d) => new() { Label = d.Label, IsMasked = d.IsMasked };
}
