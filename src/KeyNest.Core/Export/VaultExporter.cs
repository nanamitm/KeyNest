using System.Globalization;
using System.Text;
using System.Xml;
using KeyNest.Core.Import;
using KeyNest.Core.Models;

namespace KeyNest.Core.Export;

/// <summary>書き出しの結果。DroppedCommandCount は ID Manager に対応するコマンドが無く外したもの。</summary>
public sealed record ExportResult(byte[] Data, int EntryCount, int DroppedCommandCount = 0);

/// <summary>
/// データの書き出し。書き出したファイルは暗号化されないので、呼び出し側で本人確認と注意喚起を行うこと。
/// </summary>
public static class VaultExporter
{
    static VaultExporter()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // Shift_JIS
    }

    // ---- CSV（KeyNest 形式）----

    private static readonly string[] CsvHeader =
    [
        "グループ", "タイトル",
        "欄1の項目名", "欄1の値", "欄2の項目名", "欄2の値", "欄3の項目名", "欄3の値", "欄4の項目名", "欄4の値",
        "URL", "メール", "ファイル", "シリアル番号", "発行日", "有効期限", "備考", "貼り付け方法",
    ];

    /// <summary>
    /// 1行1エントリの CSV（UTF-8・BOM 付き・CRLF。Excel でそのまま開ける）。
    /// グループの列は <paramref name="root"/> からの階層を "/" でつないだもの（<paramref name="root"/> 自身は含めない）。
    /// </summary>
    public static ExportResult ToCsv(Group root, VaultSettings settings)
    {
        var builder = new StringBuilder();
        AppendCsvLine(builder, CsvHeader);
        var count = 0;
        foreach (var (path, entry) in EnumerateEntries(root, ""))
        {
            var fields = Enumerable.Range(0, Entry.FieldCount).SelectMany(i => new[] { entry.Fields[i].Label, entry.Fields[i].Value });
            AppendCsvLine(builder,
            [
                path, entry.Title, .. fields,
                entry.Url, entry.Email, entry.FilePath, entry.SerialNumber,
                FormatDate(entry.IssueDate, "yyyy-MM-dd"), FormatDate(entry.ExpirationDate, "yyyy-MM-dd"),
                entry.Notes, settings.ResolveAutoType(entry.AutoTypeId).Name,
            ]);
            count++;
        }
        return new ExportResult([.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(builder.ToString())], count);
    }

    private static IEnumerable<(string Path, Entry Entry)> EnumerateEntries(Group group, string path)
    {
        foreach (var entry in group.Entries)
            yield return (path, entry);
        foreach (var child in group.Groups)
        {
            var childPath = path.Length == 0 ? child.Name : $"{path}/{child.Name}";
            foreach (var item in EnumerateEntries(child, childPath))
                yield return item;
        }
    }

    /// <summary>全項目をダブルクォートで囲み、中のダブルクォートは2つ重ねる（RFC 4180）。改行もそのまま入れられる。</summary>
    private static void AppendCsvLine(StringBuilder builder, IEnumerable<string> values)
    {
        builder.AppendJoin(',', values.Select(v => "\"" + (v ?? "").Replace("\"", "\"\"") + "\""));
        builder.Append("\r\n");
    }

    // ---- XML（ID Manager 互換）----

    /// <summary>
    /// 元の ID Manager の「XML 出力」と同じ形式（Shift_JIS）。KeyNest にも ID Manager にも取り込める。
    /// 貼り付けタイプは、KeyNest の先頭（既定）を ID Manager 組み込みの 0 番、それ以降を自作タイプとして書く。
    /// Shift_JIS で表せない文字は文字参照（&amp;#x...;）になるので失われない。伏せ字の設定など ID Manager に無い情報は書かない。
    /// </summary>
    public static ExportResult ToIdManagerXml(Group root, VaultSettings settings, PersonalInfo? personal)
    {
        var commandNumbers = BuildCommandNumbers();
        var dropped = 0;
        using var stream = new MemoryStream();
        // ID Manager と同じ宣言（encoding="shift-jis"）にするため、宣言は自分で書く
        stream.Write("<?xml version=\"1.0\" encoding=\"shift-jis\" standalone=\"yes\"?>"u8);
        var xmlSettings = new XmlWriterSettings
        {
            Encoding = Encoding.GetEncoding(932),
            OmitXmlDeclaration = true,
            Indent = true,
            IndentChars = "",
            NewLineChars = "\r\n",
        };
        using (var writer = XmlWriter.Create(stream, xmlSettings))
        {
            writer.WriteStartElement("idmData");
            foreach (var field in PersonalInfoFields.All)
                WriteText(writer, field.XmlName, personal?.Get(field.Key) ?? "");
            WriteText(writer, "defaultAccountName", settings.FieldDefaults[0].Label);
            WriteText(writer, "defaultPasswordName", settings.FieldDefaults[1].Label);
            WriteText(writer, "defaultItem1Name", settings.FieldDefaults[2].Label);
            WriteText(writer, "defaultItem2Name", settings.FieldDefaults[3].Label);

            var customTypes = settings.AutoTypes.Skip(1).ToList();
            // 名前の一覧はカンマ区切りなので、名前の中のカンマは読点に置き換える
            WriteText(writer, "pasteTypeList", string.Join(",", customTypes.Select(t => t.Name.Replace(",", "、"))));
            foreach (var template in customTypes)
            {
                var numbers = new List<int>();
                foreach (var command in template.Commands)
                {
                    if (commandNumbers.TryGetValue(command, out var number)) numbers.Add(number);
                    else dropped++;
                }
                WriteText(writer, "pasteTypeItem", string.Join(",", numbers));
            }

            var count = WriteGroupContents(writer, root, settings);
            writer.WriteEndElement();
            writer.Flush();
            return new ExportResult(stream.ToArray(), count, dropped);
        }
    }

    /// <summary>KeyNest のコマンド → ID Manager のキー一覧の番号（取り込みの対応表の逆）。</summary>
    private static Dictionary<string, int> BuildCommandNumbers()
    {
        var numbers = new Dictionary<string, int>();
        for (var i = 0; i < IdManagerXmlDocument.CommandMap.Length; i++)
        {
            if (IdManagerXmlDocument.CommandMap[i] is { } command)
                numbers.TryAdd(command, i);
        }
        return numbers;
    }

    private static int WriteGroupContents(XmlWriter writer, Group group, VaultSettings settings)
    {
        var count = 0;
        foreach (var child in group.Groups)
        {
            writer.WriteStartElement("folder");
            writer.WriteAttributeString("name", child.Name);
            writer.WriteAttributeString("open", "true");
            writer.WriteAttributeString("selected", "false");
            count += WriteGroupContents(writer, child, settings);
            writer.WriteFullEndElement();
        }
        foreach (var entry in group.Entries)
        {
            WriteItem(writer, entry, settings);
            count++;
        }
        return count;
    }

    private static void WriteItem(XmlWriter writer, Entry entry, VaultSettings settings)
    {
        writer.WriteStartElement("item");
        writer.WriteAttributeString("name", entry.Title);
        writer.WriteAttributeString("selected", "false");
        string[] fieldElements = ["account", "password", "item1", "item2"];
        for (var i = 0; i < fieldElements.Length; i++)
        {
            writer.WriteStartElement(fieldElements[i]);
            writer.WriteAttributeString("name", entry.Fields[i].Label);
            writer.WriteString(entry.Fields[i].Value);
            writer.WriteFullEndElement();
        }
        WriteText(writer, "serialNumber", entry.SerialNumber);
        WriteText(writer, "comment", entry.Notes);
        WriteText(writer, "url", entry.Url);
        WriteText(writer, "e-mail", entry.Email);
        WriteText(writer, "file", entry.FilePath);
        WriteText(writer, "issueDate", FormatDate(entry.IssueDate, "M/d/yyyy"));
        WriteText(writer, "expirationDate", FormatDate(entry.ExpirationDate, "M/d/yyyy"));
        // 0 = 先頭（既定）のタイプ、1 以降 = 自作タイプ
        var index = settings.AutoTypes.IndexOf(settings.ResolveAutoType(entry.AutoTypeId));
        WriteText(writer, "pasteType", Math.Max(index, 0).ToString(CultureInfo.InvariantCulture));
        writer.WriteFullEndElement();
    }

    private static void WriteText(XmlWriter writer, string name, string value)
    {
        writer.WriteStartElement(name);
        writer.WriteString(value);
        writer.WriteFullEndElement(); // ID Manager と同じく空でも <x></x> と書く
    }

    private static string FormatDate(DateOnly? date, string format) =>
        date?.ToString(format, CultureInfo.InvariantCulture) ?? "";
}
