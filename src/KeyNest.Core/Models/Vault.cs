namespace KeyNest.Core.Models;

/// <summary>保存ファイル1つ分の中身（暗号化前の平文）。</summary>
public sealed class Vault
{
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Group Root { get; set; } = new() { Name = "すべて" };
    public VaultSettings Settings { get; set; } = new();
    public PersonalInfo Personal { get; set; } = new();

    public static Vault CreateDefault()
    {
        var vault = new Vault();
        vault.Root.Groups.Add(new Group { Name = "Web サイト" });
        vault.Root.Groups.Add(new Group { Name = "メール" });
        vault.Root.Groups.Add(new Group { Name = "ソフトウェア" });
        return vault;
    }

    /// <summary>読み込んだデータの欠けや過不足を補正する。</summary>
    public void Normalize()
    {
        Settings ??= new VaultSettings();
        Settings.NormalizeFieldDefaults();
        Settings.NormalizeAutoTypes();
        Personal ??= new PersonalInfo();
        Personal.Normalize();
        foreach (var entry in Root.AllEntries())
            entry.NormalizeFields(Settings.FieldDefaults);
    }
}

public sealed class VaultSettings
{
    /// <summary>新しく作成するエントリの項目名と伏せ字の初期値。既存のエントリには影響しない。</summary>
    public List<FieldDefault> FieldDefaults { get; set; } = CreateFieldDefaults();

    /// <summary>貼り付けタイプの一覧。先頭が既定。</summary>
    public List<AutoTypeTemplate> AutoTypes { get; set; } = CreateDefaultAutoTypes();

    /// <summary>貼り付けタイプの「待機」コマンドで待つミリ秒（ID Manager の DelayTime に相当）。</summary>
    public int AutoTypeDelayMs { get; set; } = 400;

    /// <summary>クリップボードを自動消去するまでの秒数（0 で無効）。</summary>
    public int ClipboardClearSeconds { get; set; } = 15;

    /// <summary>一定時間操作しないと自動でロックする（既定は無効）。</summary>
    public bool AutoLockEnabled { get; set; }

    /// <summary>自動ロックまでの分数。</summary>
    public int AutoLockMinutes { get; set; } = 10;

    /// <summary>何を「操作していない」とみなすか。</summary>
    public AutoLockMode AutoLockMode { get; set; } = AutoLockMode.Application;

    public static List<FieldDefault> CreateFieldDefaults() =>
    [
        new() { Label = "アカウント" },
        new() { Label = "パスワード", IsMasked = true },
        new() { Label = "追加項目1" },
        new() { Label = "追加項目2" },
    ];

    public static List<AutoTypeTemplate> CreateDefaultAutoTypes() =>
    [
        new()
        {
            Name = "ID+TAB+PASSWORD",
            Commands = [AutoTypeCommands.FieldValue(0), AutoTypeCommands.Tab, AutoTypeCommands.FieldValue(1)],
        },
    ];

    /// <summary>一覧が空にならないようにする（既定のタイプが必ずある）。</summary>
    public void NormalizeAutoTypes()
    {
        AutoTypes ??= [];
        AutoTypes.RemoveAll(t => t is null);
        foreach (var template in AutoTypes)
            template.Commands ??= [];
        if (AutoTypes.Count == 0)
            AutoTypes.AddRange(CreateDefaultAutoTypes());
    }

    /// <summary>エントリが使う貼り付けタイプ（指定なし・削除済みなら先頭）。</summary>
    public AutoTypeTemplate ResolveAutoType(Guid? id) =>
        AutoTypes.FirstOrDefault(t => t.Id == id) ?? AutoTypes[0];

    public void NormalizeFieldDefaults()
    {
        FieldDefaults ??= [];
        var defaults = CreateFieldDefaults();
        while (FieldDefaults.Count < Entry.FieldCount) FieldDefaults.Add(defaults[FieldDefaults.Count]);
        if (FieldDefaults.Count > Entry.FieldCount)
            FieldDefaults.RemoveRange(Entry.FieldCount, FieldDefaults.Count - Entry.FieldCount);
    }
}

public enum AutoLockMode
{
    /// <summary>KeyNest を操作していない時間（ほかのアプリで作業中でもロックする）。</summary>
    Application,

    /// <summary>パソコンを操作していない時間（席を外したときだけロックする）。</summary>
    System,
}

public sealed class FieldDefault
{
    public string Label { get; set; } = "";
    public bool IsMasked { get; set; }
}
