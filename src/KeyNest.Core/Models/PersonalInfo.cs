namespace KeyNest.Core.Models;

/// <summary>
/// 個人情報（氏名・住所など）。フォームへの入力支援用で、トレイ・ホットキーのメニューや貼り付けタイプから使う。
/// データファイルに暗号化して保存する。
/// </summary>
public sealed class PersonalInfo
{
    public List<PersonalInfoItem> Items { get; set; } = Create();

    public string Get(string key) => Items.FirstOrDefault(i => i.Key == key)?.Value ?? "";

    public static List<PersonalInfoItem> Create() =>
        PersonalInfoFields.All.Select(f => new PersonalInfoItem { Key = f.Key, ShowInMenu = f.ShowInMenuByDefault }).ToList();

    /// <summary>項目を一覧の順にそろえる（足りない項目を追加し、不明な項目を除く）。</summary>
    public void Normalize()
    {
        var existing = (Items ?? []).Where(i => i is not null).GroupBy(i => i.Key).ToDictionary(g => g.Key, g => g.First());
        Items = PersonalInfoFields.All
            .Select(f => existing.TryGetValue(f.Key, out var item)
                ? new PersonalInfoItem { Key = f.Key, Value = item.Value ?? "", ShowInMenu = item.ShowInMenu }
                : new PersonalInfoItem { Key = f.Key, ShowInMenu = f.ShowInMenuByDefault })
            .ToList();
    }
}

public sealed class PersonalInfoItem
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";

    /// <summary>トレイ・ホットキーのメニューに表示する。</summary>
    public bool ShowInMenu { get; set; }
}

/// <summary>個人情報の項目。キーはデータファイルに保存されるので変更しないこと。</summary>
public static class PersonalInfoFields
{
    public sealed record Field(string Key, string Name, string XmlName, bool ShowInMenuByDefault);

    /// <summary>表示順。XmlName は ID Manager の XML 出力の要素名。</summary>
    public static IReadOnlyList<Field> All { get; } =
    [
        new("FAMILY_NAME", "姓", "familyName", true),
        new("GIVEN_NAME", "名", "givenName", true),
        new("FAMILY_NAME_KANA", "姓（ふりがな）", "familyNamePhonetic", true),
        new("GIVEN_NAME_KANA", "名（ふりがな）", "givenNamePhonetic", true),
        new("FAMILY_NAME_KATAKANA", "姓（フリガナ）", "familyNamePhonetic2", true),
        new("GIVEN_NAME_KATAKANA", "名（フリガナ）", "givenNamePhonetic2", true),
        new("EMAIL1", "e-mail1", "e-mail1", true),
        new("EMAIL2", "e-mail2", "e-mail2", false),
        new("EMAIL3", "e-mail3", "e-mail3", false),
        new("ZIP1", "郵便番号1", "zip1", true),
        new("PREFECTURE1", "都道府県1", "prefecture1", true),
        new("ADDRESS1", "住所1", "address1", true),
        new("ZIP2", "郵便番号2", "zip2", false),
        new("PREFECTURE2", "都道府県2", "prefecture2", false),
        new("ADDRESS2", "住所2", "address2", false),
        new("TEL1", "電話番号1", "tel1", true),
        new("TEL2", "電話番号2", "tel2", false),
        new("HANDLE1", "ハンドルネーム1", "handlename1", false),
        new("HANDLE2", "ハンドルネーム2", "handlename2", false),
        new("COMPANY", "勤務先", "company", false),
    ];

    public static string NameOf(string key) => All.FirstOrDefault(f => f.Key == key)?.Name ?? key;
}
