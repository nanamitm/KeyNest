using KeyNest.Core.AutoTyping;
using KeyNest.Core.Models;

namespace KeyNest.Core.Tests;

public sealed class AutoTypePlannerTests
{
    private static Entry SampleEntry()
    {
        var entry = new Entry { Title = "Example", Url = "https://example.com", Email = "me@example.com", SerialNumber = "SN-1" };
        entry.Fields[0].Value = "user01";
        entry.Fields[1].Value = "p@ss 日本語";
        entry.Fields[2].Label = "暗証番号";
        return entry;
    }

    private static IReadOnlyList<AutoTypeStep> Plan(params string[] commands) =>
        AutoTypePlanner.Plan(new AutoTypeTemplate { Commands = [.. commands] }, SampleEntry(), PersonalWithAddress(), delayMs: 250);

    private static PersonalInfo PersonalWithAddress()
    {
        var personal = new PersonalInfo();
        personal.Items.First(i => i.Key == "ADDRESS1").Value = "東京都千代田区";
        return personal;
    }

    [Fact]
    public void Default_template_types_id_tab_password()
    {
        var settings = new VaultSettings();
        var steps = AutoTypePlanner.Plan(settings.AutoTypes[0], SampleEntry(), new PersonalInfo(), settings.AutoTypeDelayMs);

        Assert.Equal<AutoTypeStep>(
            [new TypeTextStep("user01"), new KeyStep(AutoTypeKey.Tab), new TypeTextStep("p@ss 日本語")],
            steps);
    }

    [Fact]
    public void Commands_are_mapped_to_values_keys_and_actions()
    {
        var steps = Plan("TITLE", "F3:LABEL", "URL", "EMAIL", "SERIAL", "SHIFT+TAB", "SHIFT+END", "DELETE", "ENTER", "OPEN_URL", "DELAY", "P:ADDRESS1");

        Assert.Equal<AutoTypeStep>(
        [
            new TypeTextStep("Example"),
            new TypeTextStep("暗証番号"),
            new TypeTextStep("https://example.com"),
            new TypeTextStep("me@example.com"),
            new TypeTextStep("SN-1"),
            new KeyStep(AutoTypeKey.Tab, Shift: true),
            new KeyStep(AutoTypeKey.End, Shift: true),
            new KeyStep(AutoTypeKey.Delete),
            new KeyStep(AutoTypeKey.Enter),
            new OpenUrlStep("https://example.com"),
            new DelayStep(250),
            new TypeTextStep("東京都千代田区"),
        ], steps);
    }

    [Fact]
    public void Empty_values_and_unknown_commands_are_skipped_but_keys_remain()
    {
        // 欄4・ファイル・個人情報の電話番号は空、"???" は不明なコマンド
        var steps = Plan("F4", "TAB", "FILE", "P:TEL1", "???", "ENTER");

        Assert.Equal<AutoTypeStep>([new KeyStep(AutoTypeKey.Tab), new KeyStep(AutoTypeKey.Enter)], steps);
    }

    [Fact]
    public void Open_url_without_url_does_nothing()
    {
        var entry = new Entry();
        var steps = AutoTypePlanner.Plan(new AutoTypeTemplate { Commands = ["OPEN_URL"] }, entry, new PersonalInfo(), 100);

        Assert.Empty(steps);
    }
}
