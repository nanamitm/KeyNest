namespace KeyNest.Core.Models;

/// <summary>エントリをまとめる階層グループ。</summary>
public sealed class Group
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public List<Group> Groups { get; set; } = [];
    public List<Entry> Entries { get; set; } = [];

    public IEnumerable<Entry> AllEntries() =>
        Entries.Concat(Groups.SelectMany(g => g.AllEntries()));
}
