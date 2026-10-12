using System.Text.Json;
using System.Text.Json.Nodes;
using KeyNest.Core.Models;
using KeyNest.Core.Security;

namespace KeyNest.Core.Storage;

/// <summary>Vault と JSON の相互変換。古いスキーマは読み込み時に最新へ移行する。</summary>
public static class VaultSerializer
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static byte[] Serialize(Vault vault) => JsonSerializer.SerializeToUtf8Bytes(vault, Options);

    public static Vault Deserialize(ReadOnlySpan<byte> json)
    {
        var root = JsonNode.Parse(json) as JsonObject
                   ?? throw new InvalidVaultFileException("データが空です。");

        var version = root["SchemaVersion"]?.GetValue<int>() ?? 1;
        if (version > Vault.CurrentSchemaVersion)
            throw new InvalidVaultFileException($"新しい形式のデータです (schema v{version})。アプリを更新してください。");
        if (version < 2)
            MigrateV1ToV2(root);
        if (version < 3)
            MigrateV2ToV3(root);
        if (version < 4)
            MigrateV3ToV4(root);

        var vault = root.Deserialize<Vault>(Options) ?? throw new InvalidVaultFileException("データが空です。");
        vault.Normalize();
        return vault;
    }

    /// <summary>
    /// v1: UserId / Password / Email の固定項目 → v2: 汎用欄4つ。
    /// ID → 欄1、パスワード → 欄2。Email は専用欄としてそのまま残す。
    /// </summary>
    private static void MigrateV1ToV2(JsonObject root)
    {
        foreach (var entry in EnumerateEntries(root["Root"] as JsonObject))
        {
            entry["Fields"] = new JsonArray(
                new JsonObject { ["Value"] = TakeString(entry, "UserId") },
                new JsonObject { ["Value"] = TakeString(entry, "Password") },
                new JsonObject { ["Value"] = "" },
                new JsonObject { ["Value"] = "" });

        }
        root["SchemaVersion"] = 2;
    }

    /// <summary>
    /// v2: 項目名・伏せ字が null なら設定の既定値に連動 → v3: 各エントリが値を持つ（設定は新規作成時の初期値のみ）。
    /// null だった欄に、その時点の設定値を書き込んで固定する。
    /// </summary>
    private static void MigrateV2ToV3(JsonObject root)
    {
        var defaults = ReadFieldDefaults(root);
        foreach (var entry in EnumerateEntries(root["Root"] as JsonObject))
        {
            if (entry["Fields"] is not JsonArray fields) continue;
            for (var i = 0; i < fields.Count && i < defaults.Count; i++)
            {
                if (fields[i] is not JsonObject field) continue;
                if (string.IsNullOrEmpty(field["Label"]?.GetValue<string>()))
                    field["Label"] = defaults[i].Label;
                if (field["IsMasked"] is null) // 未設定と JSON の null のどちらも null になる
                    field["IsMasked"] = defaults[i].IsMasked;
            }
        }
        root["SchemaVersion"] = 3;
    }

    /// <summary>
    /// v3: エントリごとのキー操作の文字列 (AutoTypeSequence) → v4: 名前付きの貼り付けタイプの一覧から選ぶ (AutoTypeId)。
    /// v3 までは文字列を編集する画面が無く、既定値か取り込み時の値しか入っていないので、全エントリを既定のタイプにする。
    /// </summary>
    private static void MigrateV3ToV4(JsonObject root)
    {
        foreach (var entry in EnumerateEntries(root["Root"] as JsonObject))
            entry.Remove("AutoTypeSequence");
        root["SchemaVersion"] = 4;
    }

    private static List<FieldDefault> ReadFieldDefaults(JsonObject root)
    {
        var defaults = root["Settings"]?["FieldDefaults"]?.Deserialize<List<FieldDefault>>(Options) ?? [];
        var builtIn = VaultSettings.CreateFieldDefaults();
        while (defaults.Count < builtIn.Count) defaults.Add(builtIn[defaults.Count]);
        return defaults;
    }

    private static IEnumerable<JsonObject> EnumerateEntries(JsonObject? group)
    {
        if (group is null) yield break;
        foreach (var entry in (group["Entries"] as JsonArray ?? []).OfType<JsonObject>())
            yield return entry;
        foreach (var child in (group["Groups"] as JsonArray ?? []).OfType<JsonObject>())
            foreach (var entry in EnumerateEntries(child))
                yield return entry;
    }

    private static string TakeString(JsonObject obj, string key)
    {
        var value = obj[key]?.GetValue<string>() ?? "";
        obj.Remove(key);
        return value;
    }
}
