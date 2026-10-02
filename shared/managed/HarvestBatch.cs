using System;
using System.Collections.Generic;

namespace Cheeze.Managed;

public readonly record struct HarvestCounts(int AddedItems, int? SeedReturns, bool MatchesExpected);

/// <summary>Validate actual item identities when a crop may also be its planting material.</summary>
public static class HarvestBatch
{
    /// <summary>
    /// Snapshots must contain validated unique identities with the same equality
    /// semantics. Count differences are recoverable; loss or conflicting ownership
    /// is not. Shared planting returns can only be inferred from a total consistent
    /// with the expected yield; otherwise SeedReturns is unknown (null).
    /// </summary>
    public static HarvestCounts Observe<T>(int expectedYield, bool sharedPlantingType,
        HashSet<T> originalProduce, HashSet<T> harvestedProduce,
        HashSet<T> originalSeeds, HashSet<T> harvestedSeeds) where T : notnull
    {
        if (expectedYield <= 0) throw new ArgumentOutOfRangeException(nameof(expectedYield));
        RequireSnapshot(originalProduce);
        RequireSnapshot(harvestedProduce);
        RequireSnapshot(originalSeeds);
        RequireSnapshot(harvestedSeeds);
        if (!originalProduce.IsSubsetOf(harvestedProduce) || !originalSeeds.IsSubsetOf(harvestedSeeds))
            throw new InvalidOperationException("Harvest removed previously owned produce or seeds.");

        int added = harvestedProduce.Count - originalProduce.Count;
        if (sharedPlantingType)
        {
            if (!originalProduce.SetEquals(originalSeeds) || !harvestedProduce.SetEquals(harvestedSeeds))
                throw new InvalidOperationException("Shared crop and planting snapshots disagree on ownership.");
            long inferred = (long)added - expectedYield;
            bool matches = inferred >= 1 && inferred <= 2;
            return new HarvestCounts(added, matches ? (int)inferred : null, matches);
        }

        if (originalProduce.Overlaps(originalSeeds) || harvestedProduce.Overlaps(harvestedSeeds))
            throw new InvalidOperationException("Distinct crop and planting types share an item identity.");
        int returnedSeeds = harvestedSeeds.Count - originalSeeds.Count;
        return new HarvestCounts(added, returnedSeeds,
            added == expectedYield && returnedSeeds >= 1 && returnedSeeds <= 2);
    }

    /// <summary>
    /// Replant validation separately establishes the seed and module transaction.
    /// Distinct produce must stay untouched. A shared crop permits exactly one
    /// known item removal on successful replant, including an original player item.
    /// The returned set excludes all original produce and never creates item IDs.
    /// </summary>
    public static HashSet<T> AfterReplant<T>(bool shared, bool replanted,
        HashSet<T> originalProduce, HashSet<T> harvestedProduce,
        HashSet<T> afterProduce) where T : notnull
    {
        RequireSnapshot(originalProduce);
        RequireSnapshot(harvestedProduce);
        RequireSnapshot(afterProduce);
        if (!originalProduce.IsSubsetOf(harvestedProduce))
            throw new InvalidOperationException("Harvest snapshot lost previously owned produce.");

        bool preserved = shared && replanted
            ? afterProduce.Count == harvestedProduce.Count - 1 && afterProduce.IsSubsetOf(harvestedProduce)
            : afterProduce.SetEquals(harvestedProduce);
        if (!preserved)
            throw new InvalidOperationException("Produce ownership changed unexpectedly during replant.");

        var available = new HashSet<T>(afterProduce, afterProduce.Comparer);
        available.ExceptWith(originalProduce);
        return available;
    }

    private static void RequireSnapshot<T>(HashSet<T> snapshot) where T : notnull
    {
        if (snapshot == null) throw new InvalidOperationException("Item ownership snapshot is unavailable.");
    }
}
