using System.Text;
using System.Xml;
using KeyNest.Core.Import;
using KeyNest.Core.Models;

namespace KeyNest.Core.Tests;

public sealed class IdManagerImportTests
{
    static IdManagerImportTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    // ID Manager の XML 出力と同じ構造（値は架空）。Group2 は最上位（通信サービスと同じ階層）で、CSV では区別できない配置
    private const string SampleXml = """
        <?xml version="1.0" encoding="shift-jis" standalone="yes"?><idmData>
        <familyName>山田</familyName>
        <address1>東京都</address1>
        <defaultAccountName>Account ID</defaultAccountName>
        <defaultPasswordName>Password</defaultPasswordName>
        <defaultItem1Name></defaultItem1Name>
        <defaultItem2Name></defaultItem2Name>
        <pasteTypeList>test,住所入力,空</pasteTypeList>
        <pasteTypeItem>0,12,9</pasteTypeItem>
        <pasteTypeItem>2,12,31,13,40,21,99</pasteTypeItem>
        <pasteTypeItem></pasteTypeItem>
        <item name="ルート直下" selected="false">
        <account name="ID"></account>
        <password name="PW">top-secret</password>
        <item1 name=""></item1>
        <item2 name=""></item2>
        <pasteType>7</pasteType>
        </item>
        <folder name="通信サービス" open="true" selected="false">
        <folder name="NTT西日本" open="true" selected="false">
        <item name="Fサービス &amp; 光" selected="true">
        <account name="お客様ID">user01</account>
        <password name="Password">p@ss&lt;1&gt;</password>
        <item1 name="暗証番号">1234</item1>
        <item2 name=""></item2>
        <serialNumber>SN-001</serialNumber>
        <comment>1行目
        2行目</comment>
        <url>https://example.com</url>
        <e-mail>user@example.com</e-mail>
        <file>C:\docs\contract.pdf</file>
        <issueDate>4/1/2024</issueDate>
        <expirationDate>3/31/2027</expirationDate>
        <pasteType>2</pasteType>
        </item>
        </folder>
        </folder>
        <folder name="Group2" open="false" selected="false">
        </folder>
        </idmData>
        """;

    private static IdManagerXmlDocument Load(string xml) => IdManagerXmlDocument.Load(Encoding.GetEncoding(932).GetBytes(xml));

    private static (Group Root, VaultSettings Settings, IdManagerImportResult Result) Import(string xml, bool importDates = true)
    {
        var settings = new VaultSettings();
        var result = Load(xml).ImportInto(settings, "取り込み", importDates);
        return (result.Group, settings, result);
    }

    [Fact]
    public void Folder_hierarchy_is_restored_from_shift_jis_xml()
    {
        var doc = Load(SampleXml);
        var (root, _, _) = Import(SampleXml);

        Assert.Equal((3, 2), (doc.FolderCount, doc.ItemCount));
        Assert.Equal("取り込み", root.Name);
        Assert.Equal("ルート直下", Assert.Single(root.Entries).Title);
        Assert.Equal(["通信サービス", "Group2"], root.Groups.Select(g => g.Name));
        var ntt = Assert.Single(root.Groups[0].Groups);
        Assert.Equal("NTT西日本", ntt.Name);
        Assert.Empty(root.Groups[1].Groups);
        Assert.Equal("Fサービス & 光", Assert.Single(ntt.Entries).Title);
    }

    [Fact]
    public void Item_values_labels_and_dates_are_mapped()
    {
        var entry = Import(SampleXml).Root.Groups[0].Groups[0].Entries[0];

        Assert.Equal(["お客様ID", "Password", "暗証番号", "追加項目2"], entry.Fields.Select(f => f.Label));
        Assert.Equal(["user01", "p@ss<1>", "1234", ""], entry.Fields.Select(f => f.Value));
        Assert.Equal([false, true, false, false], entry.Fields.Select(f => f.IsMasked));
        Assert.Equal("SN-001", entry.SerialNumber);
        Assert.Contains("2行目", entry.Notes);
        Assert.Equal("https://example.com", entry.Url);
        Assert.Equal("user@example.com", entry.Email);
        Assert.Equal(@"C:\docs\contract.pdf", entry.FilePath);
        Assert.Equal(new DateOnly(2024, 4, 1), entry.IssueDate);
        Assert.Equal(new DateOnly(2027, 3, 31), entry.ExpirationDate);
    }

    [Fact]
    public void Paste_types_are_converted_and_linked_to_entries()
    {
        var (root, settings, result) = Import(SampleXml);

        Assert.Equal(["ID+TAB+PASSWORD", "test", "住所入力", "空"], settings.AutoTypes.Select(t => t.Name));
        Assert.Equal(["TITLE", "TAB", "URL"], settings.AutoTypes[1].Commands);
        // 31 = 住所1 は個人情報のコマンドに変換、99（一覧に無い番号）は外す
        Assert.Equal(["F1", "TAB", "P:ADDRESS1", "ENTER", "SERIAL", "DELAY"], settings.AutoTypes[2].Commands);
        Assert.Empty(settings.AutoTypes[3].Commands);
        Assert.Equal((3, 1), (result.AutoTypeCount, result.DroppedCommandCount));

        // pasteType 2 = 自作の2つ目
        Assert.Equal(settings.AutoTypes[2].Id, root.Groups[0].Groups[0].Entries[0].AutoTypeId);
        // 一覧に無い番号は既定
        Assert.Null(root.Entries[0].AutoTypeId);
    }

    [Fact]
    public void Importing_twice_reuses_identical_paste_types()
    {
        var settings = new VaultSettings();
        var doc = Load(SampleXml);
        doc.ImportInto(settings, "1回目", importDates: true);
        settings.AutoTypes[1].Commands.Add("ENTER"); // 「test」だけ中身を変えておく
        doc.ImportInto(settings, "2回目", importDates: true);

        Assert.Equal(["ID+TAB+PASSWORD", "test", "住所入力", "空", "test（ID Manager）"], settings.AutoTypes.Select(t => t.Name));
    }

    [Fact]
    public void Personal_info_is_imported_only_when_requested_and_keeps_existing_values_for_blanks()
    {
        var doc = Load(SampleXml);
        Assert.Equal(new Dictionary<string, string> { ["FAMILY_NAME"] = "山田", ["ADDRESS1"] = "東京都" }, doc.PersonalValues);

        var vault = new Vault();
        vault.Personal.Items.First(i => i.Key == "TEL1").Value = "03-0000-0000";
        doc.ImportInto(vault, "x", importDates: true, importPersonalInfo: false);
        Assert.Equal("", vault.Personal.Get("FAMILY_NAME"));

        doc.ImportInto(vault, "y", importDates: true, importPersonalInfo: true);
        Assert.Equal("山田", vault.Personal.Get("FAMILY_NAME"));
        Assert.Equal("東京都", vault.Personal.Get("ADDRESS1"));
        Assert.Equal("03-0000-0000", vault.Personal.Get("TEL1")); // ID Manager 側が空なので残る
    }

    [Fact]
    public void Dates_can_be_skipped()
    {
        var (root, _, _) = Import(SampleXml, importDates: false);

        Assert.Null(root.Groups[0].Groups[0].Entries[0].ExpirationDate);
    }

    [Fact]
    public void Default_field_names_are_read()
    {
        Assert.Equal(["Account ID", "Password", "", ""], Load(SampleXml).DefaultFieldNames);
    }

    [Fact]
    public void Same_issue_date_with_one_year_expiration_is_detected_as_auto_filled()
    {
        static string Item(string issue, string expiration) =>
            $"<item name=\"a\"><issueDate>{issue}</issueDate><expirationDate>{expiration}</expirationDate></item>";

        var autoFilled = Load($"<idmData>{Item("10/12/2026", "10/12/2027")}{Item("10/12/2026", "10/12/2027")}</idmData>");
        var real = Load($"<idmData>{Item("4/1/2024", "3/31/2027")}{Item("10/12/2026", "10/12/2027")}</idmData>");

        Assert.True(autoFilled.DatesLookAutoFilled);
        Assert.False(real.DatesLookAutoFilled);
        Assert.Equal(2, real.ItemsWithDates);
    }

    [Fact]
    public void Non_id_manager_xml_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => Load("<other />"));
    }

    [Fact]
    public void Dtd_is_not_processed()
    {
        const string xxe = """<?xml version="1.0"?><!DOCTYPE idmData [<!ENTITY x SYSTEM "file:///c:/windows/win.ini">]><idmData>&x;</idmData>""";
        Assert.Throws<XmlException>(() => Load(xxe));
    }
}
