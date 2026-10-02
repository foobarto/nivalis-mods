using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace NivalisFarmSupply;

public sealed class FarmSupplyLedger
{
    public sealed record Entry(string Venue, string Ingredient, string Name, int Day, int Count);
    public sealed record Row(string Ingredient, string Name, long Count);
    private readonly Dictionary<(string Venue, string Ingredient, int Day), Entry> entries = new();

    public void Clear() => entries.Clear();

    public void Record(string venue, string ingredient, string name, int day, int count)
    {
        Validate(new Entry(venue, ingredient, name, day, count));
        var key = (venue, ingredient, day);
        entries.TryGetValue(key, out var prior);
        entries[key] = new Entry(venue, ingredient, name, day, checked((prior?.Count ?? 0) + count));
    }

    public IReadOnlyList<Row> Rows(string venue, int fromDay, int toDayExclusive) =>
        entries.Values.Where(e => e.Venue == venue && e.Day >= fromDay && e.Day < toDayExclusive)
            .GroupBy(e => e.Ingredient)
            .Select(g => new Row(g.Key, g.Last().Name, g.Sum(e => (long)e.Count)))
            .OrderBy(e => e.Name, StringComparer.Ordinal).ToArray();

    public string Snapshot() => JsonSerializer.Serialize(entries.Values.ToArray());

    public void Restore(string json)
    {
        var values = JsonSerializer.Deserialize<Entry[]>(json) ?? throw new FormatException("Missing receipt entries.");
        var staged = new FarmSupplyLedger();
        foreach (var entry in values)
        {
            Validate(entry);
            if (!staged.entries.TryAdd((entry.Venue, entry.Ingredient, entry.Day), entry))
                throw new FormatException("Duplicate receipt entry.");
        }
        entries.Clear();
        foreach (var pair in staged.entries) entries.Add(pair.Key, pair.Value);
    }

    private static void Validate(Entry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.Venue) || string.IsNullOrWhiteSpace(entry.Ingredient)
            || string.IsNullOrWhiteSpace(entry.Name) || entry.Day < 0 || entry.Count <= 0)
            throw new FormatException("Invalid farm receipt entry.");
    }
}
