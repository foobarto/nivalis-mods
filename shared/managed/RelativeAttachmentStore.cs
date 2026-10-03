using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Cheeze.Managed;

public sealed record RelativeAttachment(string Id, string PrefabId, string ParentId,
    float X, float Y, float Z, float QX, float QY, float QZ, float QW);

// Metadata only: callers own game-object lifetime and transform synchronization.
public sealed class RelativeAttachmentStore
{
    public const int MaximumEntries = 128;
    public const int MaximumSnapshotCharacters = 512 * 1024;
    private Dictionary<string, RelativeAttachment> entries = new(StringComparer.Ordinal);
    private sealed record Envelope(int Schema, RelativeAttachment[] Entries);

    public int Count => entries.Count;
    public IReadOnlyCollection<RelativeAttachment> Entries => entries.Values.ToArray();
    public bool TryGet(string id, out RelativeAttachment? value) => entries.TryGetValue(id, out value);
    public bool Remove(string id) => entries.Remove(id);
    public void Clear() => entries.Clear();

    public void Put(RelativeAttachment value)
    {
        Validate(value);
        if (entries.TryGetValue(value.Id, out var old))
        {
            if (old.PrefabId != value.PrefabId || old.ParentId != value.ParentId)
                throw new InvalidOperationException("Attachment identity changed; remove the previous entry explicitly.");
        }
        else if (entries.Count >= MaximumEntries)
            throw new InvalidOperationException("Attachment limit reached.");
        entries[value.Id] = value;
    }

    public string Snapshot() => JsonSerializer.Serialize(new Envelope(1,
        entries.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray()));

    public void Restore(string json)
    {
        if (json == null || json.Length > MaximumSnapshotCharacters)
            throw new FormatException("Attachment snapshot is missing or too large.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            RequireFields(root, "Schema", "Entries");
            if (!root.GetProperty("Schema").TryGetInt32(out int schema) || schema != 1)
                throw new FormatException("Unsupported attachment snapshot schema.");
            var array = root.GetProperty("Entries");
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > MaximumEntries)
                throw new FormatException("Invalid attachment count.");
            var restored = new Dictionary<string, RelativeAttachment>(StringComparer.Ordinal);
            foreach (var item in array.EnumerateArray())
            {
                RequireFields(item, "Id", "PrefabId", "ParentId", "X", "Y", "Z", "QX", "QY", "QZ", "QW");
                var entry = item.Deserialize<RelativeAttachment>() ?? throw new FormatException("Missing attachment.");
                Validate(entry);
                if (!restored.TryAdd(entry.Id, entry)) throw new FormatException("Duplicate attachment identity.");
            }
            entries = restored;
        }
        catch (Exception ex) when (ex is JsonException || ex is ArgumentException || ex is InvalidOperationException || ex is OverflowException)
        {
            throw new FormatException("Invalid attachment snapshot.", ex);
        }
    }

    private static void RequireFields(JsonElement item, params string[] expected)
    {
        if (item.ValueKind != JsonValueKind.Object) throw new FormatException("Expected an attachment object.");
        var remaining = new HashSet<string>(expected, StringComparer.Ordinal);
        foreach (var field in item.EnumerateObject())
            if (!remaining.Remove(field.Name)) throw new FormatException("Unknown or duplicate attachment field.");
        if (remaining.Count != 0) throw new FormatException("Missing attachment field.");
    }

    private static void Validate(RelativeAttachment value)
    {
        if (value == null) throw new ArgumentException("Missing attachment.");
        foreach (string id in new[] { value.Id, value.PrefabId, value.ParentId })
            if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || id.Any(char.IsControl))
                throw new ArgumentException("Invalid attachment identity.");
        if (value.Id == value.ParentId) throw new ArgumentException("An attachment cannot parent itself.");
        foreach (float v in new[] { value.X, value.Y, value.Z, value.QX, value.QY, value.QZ, value.QW })
            if (float.IsNaN(v) || float.IsInfinity(v)) throw new ArgumentException("Attachment pose must be finite.");
        double distanceSquared = (double)value.X * value.X + (double)value.Y * value.Y + (double)value.Z * value.Z;
        if (distanceSquared > 900) throw new ArgumentException("Attachment exceeds the 30-unit radius.");
        double norm = (double)value.QX * value.QX + (double)value.QY * value.QY + (double)value.QZ * value.QZ + (double)value.QW * value.QW;
        if (Math.Abs(norm - 1) > 0.001) throw new ArgumentException("Attachment rotation must be normalized.");
    }
}
