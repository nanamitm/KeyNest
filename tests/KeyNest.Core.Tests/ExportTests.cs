using System.Text;
using KeyNest.Core.Export;
using KeyNest.Core.Import;
using KeyNest.Core.Models;

namespace KeyNest.Core.Tests;

public sealed class ExportTests
{
    static ExportTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>すべて → Web（エントリ1件）→ ショッピング（エントリ1件）、すべて直下にもエントリ1件。</summary>
    private static Vault SampleVault()
    {
        var vault = new Vault();
        var custom = new AutoTypeTemplate { Name = "ログイン,Enter", Commands = ["F1", "TAB", "F2", "ENTER", "P:FAMILY_NAME_KATAKANA"] };
        vault.Settings.AutoTypes.Add(custom);

        var top = Entry.Create(vault.Settings, "直下のエントリ");
        top.Fields[0].Value = "user";
        vault.Root.Entries.Add(top);

        var web = new Group { Name = "Web" };
        var site = Entry.Create(vault.Settings, "Example \"Site\" 😀");
        site.Fields[0].Value = "taro";
        site.Fields[1].Value = "p@ss,\"word\"";
        site.Fields[2].Label = "暗証番号";
        site.Fields[2].Value = "1234";
        site.Url = "https://example.com";
        site.Email = "taro@example.com";
        site.FilePath = @"C:\docs\a.pdf";
        site.SerialNumber = "SN-1";
        site.Notes = "1行目\r\n2行目";
        site.IssueDate = new DateOnly(2024, 4, 1);
        site.ExpirationDate = new DateOnly(2027, 3, 31);
        site.AutoTypeId = custom.Id;
        web.Entries.Add(site);

        var shop = new Group { Name = "ショッピング" };
        shop.Entries.Add(Entry.Create(vault.Settings, "Store"));
        web.Groups.Add(shop);
        vault.Root.Groups.Add(web);

        vault.Personal.Items.First(i => i.Key == "FAMILY_NAME").Value = "山田";
        vault.Personal.Items.First(i => i.Key == "ADDRESS1").Value = "東京都";
        return vault;
    }

    [Fact]
    public void Csv_has_bom_header_group_paths_and_quoted_values()
    {
        var vault = SampleVault();
        var result = VaultExporter.ToCsv(vault.Root, vault.Settings);

        Assert.Equal(3, result.EntryCount);
        Assert.Equal(Encoding.UTF8.Preamble.ToArray(), result.Data[..3]);
        var text = Encoding.UTF8.GetString(result.Data, 3, result.Data.Length - 3);
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("\"グループ\",\"タイトル\",\"欄1の項目名\"", lines[0]);
        Assert.StartsWith("\"\",\"直下のエントリ\",\"アカウント\",\"user\"", lines[1]);
        Assert.StartsWith("\"Web\",\"Example \"\"Site\"\" 😀\",\"アカウント\",\"taro\",\"パスワード\",\"p@ss,\"\"word\"\"\",\"暗証番号\",\"1234\"", lines[2]);
        Assert.Contains("\"2024-04-01\",\"2027-03-31\",\"1行目", lines[2]);
        Assert.Contains("2行目\",\"ログイン,Enter\"", text); // 改行を含む備考も1つの項目として囲まれる
        Assert.StartsWith("\"Web/ショッピング\",\"Store\"", lines[^1]);
    }

    [Fact]
    public void Xml_round_trips_through_the_id_manager_importer()
    {
        var vault = SampleVault();
        var result = VaultExporter.ToIdManagerXml(vault.Root, vault.Settings, vault.Personal);
        Assert.Equal(3, result.EntryCount);
        Assert.Equal(1, result.DroppedCommandCount); // 姓（フリガナ）は ID Manager のキー一覧に無い
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"shift-jis\" standalone=\"yes\"?><idmData>", Encoding.ASCII.GetString(result.Data, 0, 80));

        var document = IdManagerXmlDocument.Load(result.Data);
        var imported = new Vault();
        var root = document.ImportInto(imported, "取り込み", importDates: true, importPersonalInfo: true).Group;

        Assert.Equal("直下のエントリ", Assert.Single(root.Entries).Title);
        var web = Assert.Single(root.Groups);
        Assert.Equal("Web", web.Name);
        Assert.Equal("ショッピング", Assert.Single(web.Groups).Name);
        Assert.Equal("Store", Assert.Single(web.Groups[0].Entries).Title);

        var site = Assert.Single(web.Entries);
        Assert.Equal("Example \"Site\" 😀", site.Title); // Shift_JIS に無い文字も文字参照で残る
        Assert.Equal(["アカウント", "パスワード", "暗証番号", "追加項目2"], site.Fields.Select(f => f.Label));
        Assert.Equal(["taro", "p@ss,\"word\"", "1234", ""], site.Fields.Select(f => f.Value));
        Assert.Equal(("https://example.com", "taro@example.com", @"C:\docs\a.pdf", "SN-1"), (site.Url, site.Email, site.FilePath, site.SerialNumber));
        Assert.Contains("2行目", site.Notes);
        Assert.Equal(new DateOnly(2024, 4, 1), site.IssueDate);
        Assert.Equal(new DateOnly(2027, 3, 31), site.ExpirationDate);

        var autoType = imported.Settings.ResolveAutoType(site.AutoTypeId);
        Assert.Equal("ログイン、Enter", autoType.Name); // 名前のカンマは読点に置き換わる
        Assert.Equal(["F1", "TAB", "F2", "ENTER"], autoType.Commands);
        Assert.Null(root.Entries[0].AutoTypeId); // 既定のタイプ

        Assert.Equal("山田", imported.Personal.Get("FAMILY_NAME"));
        Assert.Equal("東京都", imported.Personal.Get("ADDRESS1"));
        Assert.Equal(["アカウント", "パスワード", "追加項目1", "追加項目2"], document.DefaultFieldNames);
    }

    [Fact]
    public void Xml_without_personal_info_leaves_it_empty_and_subgroup_can_be_exported()
    {
        var vault = SampleVault();
        var web = vault.Root.Groups[0];
        var result = VaultExporter.ToIdManagerXml(web, vault.Settings, personal: null);

        Assert.Equal(2, result.EntryCount);
        var document = IdManagerXmlDocument.Load(result.Data);
        Assert.Empty(document.PersonalValues);
        Assert.Equal((1, 2), (document.FolderCount, document.ItemCount)); // Web の中身（ショッピング＋2件）
    }
}
