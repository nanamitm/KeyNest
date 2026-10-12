using System.Security.Cryptography;
using KeyNest.Core.Models;
using KeyNest.Core.Security;
using KeyNest.Core.Storage;

namespace KeyNest.Core.Tests;

public sealed class VaultTests : IDisposable
{
    // テストを速くするため反復回数を下げる
    private const int FastIterations = 1_000;

    private readonly string _dir = Directory.CreateTempSubdirectory("keynest-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Encrypt_then_decrypt_round_trips()
    {
        using var key = VaultKey.CreateNew("pass", FastIterations);
        var data = VaultCrypto.Encrypt("hello"u8, key);

        Assert.Equal("hello"u8.ToArray(), VaultCrypto.Decrypt(data, key));
    }

    [Fact]
    public void Wrong_password_is_rejected()
    {
        using var key = VaultKey.CreateNew("right", FastIterations);
        var data = VaultCrypto.Encrypt("secret"u8, key);
        var header = VaultCrypto.ReadHeader(data);
        using var wrong = VaultKey.Derive("wrong", header.Salt, header.Iterations);

        Assert.Throws<InvalidPasswordException>(() => VaultCrypto.Decrypt(data, wrong));
    }

    [Fact]
    public void Tampered_data_is_rejected()
    {
        using var key = VaultKey.CreateNew("pass", FastIterations);
        var data = VaultCrypto.Encrypt("secret"u8, key);
        data[^1] ^= 0xFF;

        Assert.Throws<InvalidPasswordException>(() => VaultCrypto.Decrypt(data, key));
    }

    [Fact]
    public void Non_vault_file_is_rejected()
    {
        Assert.Throws<InvalidVaultFileException>(() => VaultCrypto.ReadHeader(new byte[100]));
    }

    [Fact]
    public void Session_save_and_reopen_preserves_entries()
    {
        var path = Path.Combine(_dir, "test.knv");
        using (var session = VaultSession.Create(path, "master"))
        {
            var entry = new Entry { Title = "Example" };
            entry.Fields[0].Value = "me";
            entry.Fields[1].Value = "p@ss";
            entry.Fields[2].Label = "暗証番号";
            entry.FilePath = @"C:\Licenses\app.lic";
            entry.SerialNumber = "ABCD-EFGH-1234";
            entry.ExpirationDate = new DateOnly(2027, 10, 12);
            session.Vault.Root.Groups[0].Entries.Add(entry);
            session.Save();
        }

        using var reopened = VaultSession.Open(path, "master");
        var reloaded = Assert.Single(reopened.Vault.Root.AllEntries());
        Assert.Equal("Example", reloaded.Title);
        Assert.Equal("p@ss", reloaded.Fields[1].Value);
        Assert.Equal("暗証番号", reloaded.Fields[2].Label);
        Assert.Equal("アカウント", reloaded.Fields[0].Label);
        Assert.True(reloaded.Fields[1].IsMasked);
        Assert.Equal(@"C:\Licenses\app.lic", reloaded.FilePath);
        Assert.Equal("ABCD-EFGH-1234", reloaded.SerialNumber);
        Assert.Null(reloaded.IssueDate);
        Assert.Equal(new DateOnly(2027, 10, 12), reloaded.ExpirationDate);
        Assert.True(File.Exists(path + ".bak"));
    }

    [Fact]
    public void Verify_password_checks_against_unlocked_session()
    {
        using var session = VaultSession.Create(Path.Combine(_dir, "test.knv"), "master");

        Assert.True(session.VerifyPassword("master"));
        Assert.False(session.VerifyPassword("Master"));
        Assert.False(session.VerifyPassword(""));
    }

    [Fact]
    public void Exported_key_reopens_vault_until_master_password_changes()
    {
        var path = Path.Combine(_dir, "test.knv");
        byte[] key;
        using (var session = VaultSession.Create(path, "master"))
            key = session.ExportKey();

        using (var reopened = VaultSession.OpenWithKey(path, key))
            reopened.ChangeMasterPassword("new");

        // マスターパスワード変更でソルトが変わるため、古い鍵では開けない（Windows Hello 登録の失効）
        Assert.Throws<InvalidPasswordException>(() => VaultSession.OpenWithKey(path, key));
    }

    [Fact]
    public void Wrapped_key_round_trips_and_rejects_other_secrets()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var wrappingKey = KeyWrap.DeriveWrappingKey("signature-bytes"u8);
        var wrapped = KeyWrap.Wrap(key, wrappingKey);

        Assert.Equal(key, KeyWrap.Unwrap(wrapped, KeyWrap.DeriveWrappingKey("signature-bytes"u8)));
        Assert.Throws<InvalidPasswordException>(() => KeyWrap.Unwrap(wrapped, KeyWrap.DeriveWrappingKey("other"u8)));
        Assert.Throws<InvalidPasswordException>(() => KeyWrap.Unwrap(wrapped[..10], wrappingKey));
    }

    [Fact]
    public void Changed_master_password_takes_effect()
    {
        var path = Path.Combine(_dir, "test.knv");
        using (var session = VaultSession.Create(path, "old"))
            session.ChangeMasterPassword("new");

        Assert.Throws<InvalidPasswordException>(() => VaultSession.Open(path, "old"));
        using var _ = VaultSession.Open(path, "new");

        // バックアップも古いパスワードでは開けない
        Assert.Throws<InvalidPasswordException>(() => VaultSession.Open(path + ".bak", "old"));
        using var backup = VaultSession.Open(path + ".bak", "new");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(32)]
    public void Generated_password_contains_every_selected_class(int length)
    {
        var pw = PasswordGenerator.Generate(new PasswordOptions { Length = length });

        Assert.Equal(length, pw.Length);
        Assert.Contains(pw, char.IsUpper);
        Assert.Contains(pw, char.IsLower);
        Assert.Contains(pw, char.IsDigit);
        Assert.Contains(pw, c => !char.IsLetterOrDigit(c));
    }

    [Fact]
    public void Short_or_single_class_passwords_can_be_generated()
    {
        Assert.Single(PasswordGenerator.Generate(new PasswordOptions { Length = 1 }));
        var digits = PasswordGenerator.Generate(new PasswordOptions { Length = 12, Uppercase = false, Lowercase = false, Symbols = false });
        Assert.Equal(12, digits.Length);
        Assert.All(digits, c => Assert.True(char.IsDigit(c)));
        Assert.Equal(128, PasswordGenerator.Generate(new PasswordOptions { Length = 128 }).Length);
        Assert.Throws<ArgumentException>(() => PasswordGenerator.Generate(new PasswordOptions { Length = 129 }));
        Assert.Throws<ArgumentException>(() => PasswordGenerator.Generate(new PasswordOptions
            { Uppercase = false, Lowercase = false, Digits = false, Symbols = false }));
    }

    [Fact]
    public void Ambiguous_characters_can_be_excluded()
    {
        for (var i = 0; i < 50; i++)
        {
            var pw = PasswordGenerator.Generate(new PasswordOptions { Length = 64, ExcludeAmbiguous = true });
            Assert.DoesNotContain(pw, c => "Il1O0o|".Contains(c));
        }
    }

    [Fact]
    public void V1_fixed_fields_are_migrated_to_generic_fields()
    {
        var v1 = """
            {"SchemaVersion":1,"Root":{"Name":"すべて","Groups":[{"Name":"Web","Groups":[],"Entries":[
              {"Title":"Site","UserId":"me","Password":"pw","Email":"me@example.com","Url":"https://example.com",
               "AutoTypeSequence":"{USERID}{TAB}{PASSWORD}{ENTER}"}]}],"Entries":[]},
             "Settings":{"ClipboardClearSeconds":20}}
            """u8;

        var vault = VaultSerializer.Deserialize(v1);

        Assert.Equal(Vault.CurrentSchemaVersion, vault.SchemaVersion);
        var entry = Assert.Single(vault.Root.AllEntries());
        Assert.Equal(Entry.FieldCount, entry.Fields.Count);
        Assert.Equal(["me", "pw", "", ""], entry.Fields.Select(f => f.Value));
        Assert.Equal(["アカウント", "パスワード", "追加項目1", "追加項目2"], entry.Fields.Select(f => f.Label));
        Assert.Equal("me@example.com", entry.Email);
        Assert.Equal([false, true, false, false], entry.Fields.Select(f => f.IsMasked));
        Assert.Null(entry.AutoTypeId);
        var autoType = vault.Settings.ResolveAutoType(entry.AutoTypeId);
        Assert.Equal("ID+TAB+PASSWORD", autoType.Name);
        Assert.Equal(["F1", "TAB", "F2"], autoType.Commands);
        Assert.Equal("https://example.com", entry.Url);
        Assert.Equal(20, vault.Settings.ClipboardClearSeconds);
        Assert.Equal(Entry.FieldCount, vault.Settings.FieldDefaults.Count);
        Assert.True(vault.Settings.FieldDefaults[1].IsMasked);
    }

    [Fact]
    public void V1_vault_file_opens_with_current_app()
    {
        var path = Path.Combine(_dir, "old.knv");
        using (var key = VaultKey.CreateNew("master", FastIterations))
        {
            var v1 = """{"SchemaVersion":1,"Root":{"Name":"すべて","Groups":[],"Entries":[{"Title":"A","UserId":"u","Password":"p"}]}}"""u8;
            File.WriteAllBytes(path, VaultCrypto.Encrypt(v1, key));
        }

        using var session = VaultSession.Open(path, "master");
        Assert.Equal("p", Assert.Single(session.Vault.Root.AllEntries()).Fields[1].Value);
    }

    [Fact]
    public void V2_default_linked_labels_are_fixed_from_settings()
    {
        var v2 = """
            {"SchemaVersion":2,"Root":{"Name":"r","Groups":[],"Entries":[{"Title":"A","Fields":[
              {"Value":"u"},{"Label":"PIN","Value":"1234","IsMasked":true},{"Value":""},{"Value":"","IsMasked":true}]}]},
             "Settings":{"FieldDefaults":[{"Label":"ID"},{"Label":"PW","IsMasked":true},{"Label":"X"},{"Label":"Y"}]}}
            """u8;

        var entry = Assert.Single(VaultSerializer.Deserialize(v2).Root.AllEntries());

        Assert.Equal(["ID", "PIN", "X", "Y"], entry.Fields.Select(f => f.Label));
        Assert.Equal([false, true, false, true], entry.Fields.Select(f => f.IsMasked));
    }

    [Fact]
    public void New_entry_copies_defaults_and_is_independent_of_later_changes()
    {
        var settings = new VaultSettings();
        settings.FieldDefaults[0].Label = "ユーザー名";
        var entry = Entry.Create(settings, "新規");

        settings.FieldDefaults[0].Label = "変更後";
        settings.FieldDefaults[1].IsMasked = false;

        Assert.Equal("ユーザー名", entry.Fields[0].Label);
        Assert.True(entry.Fields[1].IsMasked);
    }

    [Fact]
    public void V3_auto_type_sequences_are_replaced_by_default_template()
    {
        var v3 = """
            {"SchemaVersion":3,"Root":{"Name":"r","Groups":[],"Entries":[
              {"Title":"A","AutoTypeSequence":"{F1}{TAB}{F2}{ENTER}","Fields":[]}]}}
            """u8;

        var vault = VaultSerializer.Deserialize(v3);

        Assert.Equal(4, vault.SchemaVersion);
        Assert.Null(Assert.Single(vault.Root.AllEntries()).AutoTypeId);
        Assert.Single(vault.Settings.AutoTypes);
        Assert.Equal(400, vault.Settings.AutoTypeDelayMs);
    }

    [Fact]
    public void Missing_or_deleted_auto_type_falls_back_to_first()
    {
        var settings = new VaultSettings();
        var custom = new AutoTypeTemplate { Name = "custom", Commands = ["TITLE"] };
        settings.AutoTypes.Add(custom);

        Assert.Same(custom, settings.ResolveAutoType(custom.Id));
        Assert.Same(settings.AutoTypes[0], settings.ResolveAutoType(Guid.NewGuid()));
        Assert.Same(settings.AutoTypes[0], settings.ResolveAutoType(null));
    }

    [Fact]
    public void Personal_info_is_saved_and_normalized()
    {
        var path = Path.Combine(_dir, "personal.knv");
        using (var session = VaultSession.Create(path, "master"))
        {
            session.Vault.Personal.Items.First(i => i.Key == "ADDRESS1").Value = "東京都千代田区";
            session.Vault.Personal.Items.First(i => i.Key == "COMPANY").ShowInMenu = true;
            session.Save();
        }

        using var reopened = VaultSession.Open(path, "master");
        var personal = reopened.Vault.Personal;
        Assert.Equal(PersonalInfoFields.All.Select(f => f.Key), personal.Items.Select(i => i.Key));
        Assert.Equal("東京都千代田区", personal.Get("ADDRESS1"));
        Assert.True(personal.Items.First(i => i.Key == "COMPANY").ShowInMenu);

        // 古いファイル（個人情報なし）や項目の欠け・不明な項目は補正される
        var vault = VaultSerializer.Deserialize("""
            {"SchemaVersion":4,"Root":{"Name":"r"},"Personal":{"Items":[{"Key":"UNKNOWN","Value":"x"},{"Key":"TEL1","Value":"1"}]}}
            """u8);
        Assert.Equal(PersonalInfoFields.All.Count, vault.Personal.Items.Count);
        Assert.Equal("1", vault.Personal.Get("TEL1"));
        Assert.Equal("", VaultSerializer.Deserialize("""{"SchemaVersion":4,"Root":{"Name":"r"}}"""u8).Personal.Get("TEL1"));
    }

    [Fact]
    public void Auto_lock_is_disabled_by_default_including_older_files()
    {
        var settings = new VaultSettings();
        Assert.False(settings.AutoLockEnabled);
        Assert.Equal(10, settings.AutoLockMinutes);
        Assert.Equal(AutoLockMode.Application, settings.AutoLockMode);

        // 以前の版は AutoLockMinutes だけを保存していた（機能は未実装だった）。有効にはならない
        var vault = VaultSerializer.Deserialize("""{"SchemaVersion":4,"Root":{"Name":"r"},"Settings":{"AutoLockMinutes":10}}"""u8);
        Assert.False(vault.Settings.AutoLockEnabled);
    }

    [Fact]
    public void Future_schema_is_rejected()
    {
        Assert.Throws<InvalidVaultFileException>(() => VaultSerializer.Deserialize("""{"SchemaVersion":99}"""u8));
    }

    [Fact]
    public void Field_counts_are_normalized()
    {
        var vault = VaultSerializer.Deserialize("""
            {"SchemaVersion":3,"Root":{"Name":"r","Groups":[],"Entries":[{"Title":"A","Fields":[{"Label":"L","Value":"x"}]}]},
             "Settings":{"FieldDefaults":[{"Label":"a"},{"Label":"b"},{"Label":"c"},{"Label":"d"},{"Label":"e"}]}}
            """u8);

        var entry = Assert.Single(vault.Root.AllEntries());
        Assert.Equal(["L", "b", "c", "d"], entry.Fields.Select(f => f.Label));
        Assert.Equal(["a", "b", "c", "d"], vault.Settings.FieldDefaults.Select(f => f.Label));
    }
}
