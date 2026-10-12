using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using KeyNest.Core.Models;
using Group = KeyNest.Core.Models.Group; // System.Text.RegularExpressions.Group と区別

namespace KeyNest.Core.Import;

/// <summary>
/// 元の ID Manager の「XML 出力」を読む。CSV 出力はフォルダの終わりが記録されず階層を復元できないため、XML を使う。
///
/// 構造: &lt;idmData&gt; 直下に既定の項目名（defaultAccountName など）と、入れ子の &lt;folder name&gt; / &lt;item name&gt;。
/// item の中に account / password / item1 / item2（name 属性が項目名）、serialNumber、comment、url、e-mail、file、
/// issueDate / expirationDate（M/d/yyyy）、pasteType。
/// 貼り付けタイプ: pasteTypeList に自作タイプの名前がカンマ区切りで並び、同じ順番で pasteTypeItem（コマンド番号の
/// カンマ区切り）が1つずつ続く。item の pasteType は 0 が組み込みの「ID+TAB+PASSWORD」、1 以降が自作タイプの1つ目〜。
/// 個人情報は idmData 直下の familyName、address1 などの要素（<see cref="PersonalInfoFields"/> の XmlName）。
/// </summary>
public sealed record IdManagerImportResult(Group Group, int AutoTypeCount, int DroppedCommandCount);

public sealed partial class IdManagerXmlDocument
{
    private static readonly string[] DateFormats = ["M/d/yyyy", "MM/dd/yyyy"];

    /// <summary>
    /// ID Manager の「設定できるキー一覧」の番号 → KeyNest のコマンド。一覧に無い番号は取り込み時に手順から外す。
    /// （ID Manager のキー一覧には 姓・名（フリガナ）は無い）
    /// </summary>
    internal static readonly string?[] CommandMap =
    [
        AutoTypeCommands.Title,                                          // 0  Title
        AutoTypeCommands.FieldLabel(0), AutoTypeCommands.FieldValue(0),  // 1  Account項目名, 2 Account ID
        AutoTypeCommands.FieldLabel(1), AutoTypeCommands.FieldValue(1),  // 3  Password項目名, 4 Password
        AutoTypeCommands.FieldLabel(2), AutoTypeCommands.FieldValue(2),  // 5  追加項目1項目名, 6 追加項目1
        AutoTypeCommands.FieldLabel(3), AutoTypeCommands.FieldValue(3),  // 7  追加項目2項目名, 8 追加項目2
        AutoTypeCommands.Url, AutoTypeCommands.Email, AutoTypeCommands.File, // 9 URL, 10 e-mail, 11 File
        AutoTypeCommands.Tab, AutoTypeCommands.Enter, AutoTypeCommands.ShiftTab, AutoTypeCommands.Space, // 12-15
        AutoTypeCommands.Right, AutoTypeCommands.Left, AutoTypeCommands.Up, AutoTypeCommands.Down,       // 16-19
        AutoTypeCommands.OpenUrl,                                        // 20 ブラウザを開く
        AutoTypeCommands.Delay,                                          // 21 Delay Time
        P("FAMILY_NAME"), P("GIVEN_NAME"), P("FAMILY_NAME_KANA"), P("GIVEN_NAME_KANA"), // 22-25 姓, 名, 姓(ふりがな), 名(ふりがな)
        P("EMAIL1"), P("EMAIL2"), P("EMAIL3"),                           // 26-28 e-mail1〜3
        P("ZIP1"), P("PREFECTURE1"), P("ADDRESS1"),                      // 29-31 郵便番号1, 都道府県1, 住所1
        P("ZIP2"), P("PREFECTURE2"), P("ADDRESS2"),                      // 32-34 郵便番号2, 都道府県2, 住所2
        P("TEL1"), P("TEL2"), P("HANDLE1"), P("HANDLE2"), P("COMPANY"),  // 35-39 電話番号1/2, ハンドルネーム1/2, 勤務先
        AutoTypeCommands.Serial,                                         // 40 シリアルナンバー
        AutoTypeCommands.ShiftEnd, AutoTypeCommands.Delete, AutoTypeCommands.ShiftHome, // 41-43
        AutoTypeCommands.Home, AutoTypeCommands.End,                     // 44-45
    ];

    private readonly XElement _root;

    private static string P(string personalKey) => AutoTypeCommands.Personal(personalKey);

    static IdManagerXmlDocument()
    {
        // ID Manager の XML は Shift_JIS
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private IdManagerXmlDocument(XElement root)
    {
        _root = root;
        var items = root.Descendants("item").ToList();
        ItemCount = items.Count;
        FolderCount = root.Descendants("folder").Count();
        DefaultFieldNames =
        [
            (string?)root.Element("defaultAccountName") ?? "",
            (string?)root.Element("defaultPasswordName") ?? "",
            (string?)root.Element("defaultItem1Name") ?? "",
            (string?)root.Element("defaultItem2Name") ?? "",
        ];

        var names = ((string?)root.Element("pasteTypeList") ?? "").Split(',', StringSplitOptions.TrimEntries);
        var commandLists = root.Elements("pasteTypeItem").Select(e => e.Value).ToList();
        PasteTypes = names.Where(n => n.Length > 0)
            .Select((name, i) => (name, i < commandLists.Count ? commandLists[i] : ""))
            .ToList();

        var dates = items.Select(i => (Issue: ParseDate(i.Element("issueDate")), Expiration: ParseDate(i.Element("expirationDate")))).ToList();
        ItemsWithDates = dates.Count(d => d.Issue is not null || d.Expiration is not null);
        DatesLookAutoFilled = dates.Count > 0
            && dates.All(d => d.Issue is { } issue && d.Expiration == issue.AddYears(1))
            && dates.Select(d => d.Issue).Distinct().Count() == 1;
    }

    public int FolderCount { get; }
    public int ItemCount { get; }
    public int ItemsWithDates { get; }

    /// <summary>
    /// 全項目の発行日が同じで、有効期限がちょうどその1年後。ID Manager が自動で入れた値の可能性が高く、
    /// そのまま取り込むと1年後に全エントリが期限切れになる。
    /// </summary>
    public bool DatesLookAutoFilled { get; }

    /// <summary>自作の貼り付けタイプ（名前と、コマンド番号のカンマ区切り）。</summary>
    private IReadOnlyList<(string Name, string Commands)> PasteTypes { get; }

    public IReadOnlyList<string> PasteTypeNames => PasteTypes.Select(p => p.Name).ToList();

    /// <summary>個人情報（値が入っている項目だけ。キー → 値）。</summary>
    public IReadOnlyDictionary<string, string> PersonalValues =>
        PersonalInfoFields.All
            .Select(f => (f.Key, Value: (string?)_root.Element(f.XmlName) ?? ""))
            .Where(p => p.Value.Length > 0)
            .ToDictionary(p => p.Key, p => p.Value);

    /// <summary>ID Manager 側の項目名の既定値（欄1〜4。未設定は空文字）。</summary>
    public IReadOnlyList<string> DefaultFieldNames { get; }

    public static IdManagerXmlDocument Load(byte[] data)
    {
        var settings = new XmlReaderSettings
        {
            // 外部の DTD・エンティティは読まない（XXE 対策）
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        // 自分で復号した文字列から読むので、XML 宣言の encoding は無視される
        using var reader = XmlReader.Create(new StringReader(Decode(data)), settings);
        var root = XDocument.Load(reader).Root;
        if (root?.Name != "idmData")
            throw new InvalidDataException("ID Manager の XML 出力ではありません（idmData 要素がありません）。");
        return new IdManagerXmlDocument(root);
    }

    /// <summary>
    /// 自作の貼り付けタイプを <paramref name="settings"/> に追加し、フォルダ構成をそのまま指定した名前のグループの下に作る。
    /// 項目名が空の欄と伏せ字の有無は設定の初期値に従う。グループは呼び出し側でツリーに追加する。
    /// </summary>
    public IdManagerImportResult ImportInto(VaultSettings settings, string groupName, bool importDates)
    {
        return ImportInto(new Vault { Settings = settings }, groupName, importDates, importPersonalInfo: false);
    }

    /// <summary>
    /// <see cref="ImportInto(VaultSettings, string, bool)"/> に加えて、個人情報も取り込む。
    /// 値が入っている項目だけ上書きし、ID Manager 側で空の項目は今の値を残す。
    /// </summary>
    public IdManagerImportResult ImportInto(Vault vault, string groupName, bool importDates, bool importPersonalInfo)
    {
        var settings = vault.Settings;
        if (importPersonalInfo)
        {
            foreach (var (key, value) in PersonalValues)
                vault.Personal.Items.First(i => i.Key == key).Value = value;
        }

        var dropped = 0;
        // pasteType の番号 → 貼り付けタイプの ID（0 = 組み込みの ID+TAB+PASSWORD は KeyNest の既定を使う）
        var autoTypeIds = new Dictionary<string, Guid?> { ["0"] = null };
        for (var i = 0; i < PasteTypes.Count; i++)
        {
            var commands = ConvertCommands(PasteTypes[i].Commands, ref dropped);
            autoTypeIds[(i + 1).ToString(CultureInfo.InvariantCulture)] = AddAutoType(settings, PasteTypes[i].Name, commands).Id;
        }

        var group = new Group { Name = groupName };
        Fill(group, _root, new ImportContext(settings.FieldDefaults, importDates, autoTypeIds));
        return new IdManagerImportResult(group, PasteTypes.Count, dropped);
    }

    private static List<string> ConvertCommands(string numbers, ref int dropped)
    {
        var commands = new List<string>();
        foreach (var part in numbers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n < CommandMap.Length && CommandMap[n] is { } command)
                commands.Add(command);
            else
                dropped++;
        }
        return commands;
    }

    /// <summary>同じ名前・同じ手順のタイプがあれば使い回す（同じファイルを2回取り込んでも増えないように）。</summary>
    private static AutoTypeTemplate AddAutoType(VaultSettings settings, string name, List<string> commands)
    {
        if (settings.AutoTypes.FirstOrDefault(t => t.Name == name && t.Commands.SequenceEqual(commands)) is { } existing)
            return existing;
        var uniqueName = settings.AutoTypes.Any(t => t.Name == name) ? $"{name}（ID Manager）" : name;
        var template = new AutoTypeTemplate { Name = uniqueName, Commands = commands };
        settings.AutoTypes.Add(template);
        return template;
    }

    private sealed record ImportContext(IReadOnlyList<FieldDefault> Defaults, bool ImportDates, Dictionary<string, Guid?> AutoTypeIds);

    private static void Fill(Group group, XElement element, ImportContext context)
    {
        foreach (var child in element.Elements())
        {
            if (child.Name == "folder")
            {
                var sub = new Group { Name = Name(child) };
                Fill(sub, child, context);
                group.Groups.Add(sub);
            }
            else if (child.Name == "item")
            {
                group.Entries.Add(ToEntry(child, context));
            }
        }
    }

    private static Entry ToEntry(XElement item, ImportContext context)
    {
        var (defaults, importDates, autoTypeIds) = context;
        var entry = new Entry
        {
            Title = Name(item),
            Fields = Entry.CreateFields(defaults),
            SerialNumber = Text(item, "serialNumber"),
            Notes = Text(item, "comment"),
            Url = Text(item, "url"),
            Email = Text(item, "e-mail"),
            FilePath = Text(item, "file"),
            // 番号が一覧に無ければ既定のタイプ
            AutoTypeId = autoTypeIds.GetValueOrDefault(Text(item, "pasteType").Trim()),
        };
        SetField(entry.Fields[0], item.Element("account"));
        SetField(entry.Fields[1], item.Element("password"));
        SetField(entry.Fields[2], item.Element("item1"));
        SetField(entry.Fields[3], item.Element("item2"));
        if (importDates)
        {
            entry.IssueDate = ParseDate(item.Element("issueDate"));
            entry.ExpirationDate = ParseDate(item.Element("expirationDate"));
        }
        return entry;
    }

    private static void SetField(EntryField field, XElement? element)
    {
        if (element is null) return;
        var label = (string?)element.Attribute("name");
        if (!string.IsNullOrEmpty(label))
            field.Label = label;
        field.Value = element.Value;
    }

    private static string Name(XElement element) => (string?)element.Attribute("name") ?? "";

    private static string Text(XElement element, string name) => (string?)element.Element(name) ?? "";

    private static DateOnly? ParseDate(XElement? element) =>
        DateOnly.TryParseExact(element?.Value.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    /// <summary>BOM・XML 宣言の encoding から文字コードを判断して文字列にする（既定は UTF-8）。</summary>
    private static string Decode(byte[] data)
    {
        if (data.AsSpan().StartsWith(Encoding.UTF8.Preamble))
            return Encoding.UTF8.GetString(data, 3, data.Length - 3);

        var head = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 256));
        var declared = EncodingDeclaration().Match(head) is { Success: true } m ? m.Groups[1].Value.ToLowerInvariant() : "";
        Encoding encoding;
        if (declared.Contains("shift") || declared is "sjis" or "windows-31j" or "ms932" or "cp932")
            encoding = Encoding.GetEncoding(932);
        else
        {
            try
            {
                encoding = declared.Length > 0 ? Encoding.GetEncoding(declared) : Encoding.UTF8;
            }
            catch (ArgumentException)
            {
                encoding = Encoding.UTF8;
            }
        }
        return encoding.GetString(data);
    }

    [GeneratedRegex("""encoding\s*=\s*["']([^"']+)["']""")]
    private static partial Regex EncodingDeclaration();
}
