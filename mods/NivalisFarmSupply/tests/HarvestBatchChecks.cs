using System;
using System.Collections.Generic;
using Cheeze.Managed;

public static class HarvestBatchChecks
{
    public static int Run()
    {
        int checks = 0;
        void Equal<T>(T expected, T actual, string name)
        {
            checks++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"{name}: expected {expected}, actual {actual}");
        }
        void Refuses(Action action, string name)
        {
            checks++;
            try { action(); }
            catch (InvalidOperationException) { return; }
            throw new Exception(name + ": expected ownership refusal");
        }
        HashSet<int> Set(params int[] values) => new(values);
        HashSet<int> Added(HashSet<int> original, int count)
        {
            var result = new HashSet<int>(original);
            for (int i = 0; i < count; i++) result.Add(100 + i);
            return result;
        }

        var original = Set(1, 2);
        foreach (int returns in new[] { 1, 2 })
        {
            var harvested = Added(original, 14 + returns);
            var counts = HarvestBatch.Observe(14, true, original, harvested, original, harvested);
            Equal(14 + returns, counts.AddedItems, "shared count includes planting returns");
            Equal<int?>(returns, counts.SeedReturns, "shared planting returns inferred once");
            Equal(true, counts.MatchesExpected, "lemon total matches yield and returns");

            var removeNew = new HashSet<int>(harvested); removeNew.Remove(100);
            var available = HarvestBatch.AfterReplant(true, true, original, harvested, removeNew);
            Equal(13 + returns, available.Count, "new seed removal leaves 14 or 15 new lemons");
            Equal(false, available.Contains(100), "consumed new lemon cannot be delivered");
            Equal(false, available.Overlaps(original), "previous player lemons never delivered");
            Equal(true, available.IsSubsetOf(removeNew), "delivery candidates actually owned");

            var removeOriginal = new HashSet<int>(harvested); removeOriginal.Remove(1);
            available = HarvestBatch.AfterReplant(true, true, original, harvested, removeOriginal);
            Equal(14 + returns, available.Count, "old seed removal leaves 15 or 16 new lemons");
            Equal(false, available.Overlaps(original), "surviving original lemon never delivered");
            Equal(true, available.IsSubsetOf(removeOriginal), "old seed consumption manufactures nothing");

            available = HarvestBatch.AfterReplant(true, false, original, harvested, harvested);
            Equal(14 + returns, available.Count, "rejected replant retains new batch");
            Equal(true, harvested.SetEquals(Added(original, 14 + returns)), "helper leaves input snapshot intact");
            available.Clear();
            Equal(16 + returns, harvested.Count, "candidate result does not alias inventory snapshot");
        }

        foreach (int actual in new[] { 0, 13, 14, 17 })
        {
            var harvested = Added(original, actual);
            var counts = HarvestBatch.Observe(14, true, original, harvested, original, harvested);
            Equal(actual, counts.AddedItems, "mismatched shared total remains observable");
            Equal(false, counts.MatchesExpected, "shared mismatch is recoverable flag");
            Equal<int?>(null, counts.SeedReturns, "shared mismatched seed return is unknown");
        }

        var cropOriginal = Set(1, 2);
        var crops = Added(cropOriginal, 14);
        var seedOriginal = Set(50);
        var seeds = Set(50, 51, 52);
        var distinct = HarvestBatch.Observe(14, false, cropOriginal, crops, seedOriginal, seeds);
        Equal(14, distinct.AddedItems, "distinct crop count excludes seeds");
        Equal<int?>(2, distinct.SeedReturns, "distinct seed count observed directly");
        Equal(true, distinct.MatchesExpected, "distinct native batch matches");
        foreach (bool replanted in new[] { false, true })
        {
            var available = HarvestBatch.AfterReplant(false, replanted, cropOriginal, crops, crops);
            Equal(14, available.Count, "distinct crop unchanged by either replant result");
            Equal(false, available.Overlaps(cropOriginal), "distinct prior crop never transferred");
        }
        distinct = HarvestBatch.Observe(14, false, cropOriginal, Added(cropOriginal, 13), seedOriginal, seeds);
        Equal(false, distinct.MatchesExpected, "distinct yield mismatch is local flag");
        Equal<int?>(2, distinct.SeedReturns, "distinct mismatch retains exact seed count");
        distinct = HarvestBatch.Observe(14, false, cropOriginal, crops, seedOriginal, seedOriginal);
        Equal(false, distinct.MatchesExpected, "zero returned seeds is local flag");
        Equal<int?>(0, distinct.SeedReturns, "zero seeds is observed without invention");
        distinct = HarvestBatch.Observe(14, false, cropOriginal, crops, seedOriginal, Set(50, 51, 52, 53));
        Equal(false, distinct.MatchesExpected, "three returned seeds is local flag");

        Refuses(() => HarvestBatch.Observe(14, true, original, Added(original, 15), Set(1), Added(original, 15)),
            "shared original snapshots must agree");
        Refuses(() => HarvestBatch.Observe(14, true, original, Added(original, 15), original, Added(original, 16)),
            "shared harvested snapshots must agree");
        Refuses(() => HarvestBatch.Observe(14, false, cropOriginal, Set(2, 100), seedOriginal, seeds),
            "pre-existing crop loss is fatal");
        Refuses(() => HarvestBatch.Observe(14, false, cropOriginal, crops, seedOriginal, Set(51, 52)),
            "pre-existing seed loss is fatal");
        Refuses(() => HarvestBatch.Observe(14, false, cropOriginal, crops, Set(1), Set(1, 50)),
            "distinct types cannot claim an original identity twice");
        Refuses(() => HarvestBatch.Observe(14, false, cropOriginal, crops, seedOriginal, Set(50, 100)),
            "distinct types cannot claim new identity twice");
        Refuses(() => HarvestBatch.Observe(14, false, cropOriginal, null!, seedOriginal, seeds),
            "missing snapshot is not zero yield");

        var sharedHarvest = Added(original, 15);
        var liveItems = new HashSet<int>(sharedHarvest);
        bool planted = false;
        int attempts = 0;
        var recovery = HarvestRecovery.Replant(() => liveItems, () => planted,
            () => true, () => false, () =>
            {
                attempts++;
                liveItems.Remove(1); // Native planting may spend a previously owned lemon.
                planted = true;
                return false; // Real endpoint state still establishes the successful commit.
            });
        var integrated = HarvestBatch.AfterReplant(true, recovery.Replanted,
            original, sharedHarvest, liveItems);
        Equal(true, recovery.Replanted, "real recovery helper accepts shared original seed commit");
        Equal(1, attempts, "integrated replant uses exactly one attempt");
        Equal(15, integrated.Count, "integrated shared seed consumption retains all fifteen new lemons");
        Equal(false, integrated.Overlaps(original), "integrated batch excludes original player items");
        Equal(true, integrated.IsSubsetOf(liveItems), "integrated batch never manufactures item identities");
        var emptyBatch = HarvestBatch.AfterReplant(true, true, original, original, Set(2));
        Equal(0, emptyBatch.Count, "spending an old seed without new produce cannot create delivery stock");
        emptyBatch = HarvestBatch.AfterReplant(false, false, cropOriginal, cropOriginal, cropOriginal);
        Equal(0, emptyBatch.Count, "unchanged old crop stock cannot become a harvest batch");

        var twoRemoved = new HashSet<int>(sharedHarvest); twoRemoved.Remove(100); twoRemoved.Remove(101);
        Refuses(() => HarvestBatch.AfterReplant(true, true, original, sharedHarvest, twoRemoved),
            "shared replant cannot remove two identities");
        Refuses(() => HarvestBatch.AfterReplant(true, true, original, sharedHarvest, sharedHarvest),
            "successful shared replant must consume one real item");
        var replaced = new HashSet<int>(sharedHarvest); replaced.Remove(100); replaced.Remove(101); replaced.Add(999);
        Refuses(() => HarvestBatch.AfterReplant(true, true, original, sharedHarvest, replaced),
            "matching count delta cannot conceal identity replacement");
        var oneRemoved = new HashSet<int>(sharedHarvest); oneRemoved.Remove(100);
        Refuses(() => HarvestBatch.AfterReplant(true, false, original, sharedHarvest, oneRemoved),
            "rejected replant must retain exact produce identities");
        var distinctRemoved = new HashSet<int>(crops); distinctRemoved.Remove(100);
        Refuses(() => HarvestBatch.AfterReplant(false, true, cropOriginal, crops, distinctRemoved),
            "distinct replant cannot consume produce");
        var distinctReplacement = new HashSet<int>(crops); distinctReplacement.Remove(100); distinctReplacement.Add(999);
        Refuses(() => HarvestBatch.AfterReplant(false, false, cropOriginal, crops, distinctReplacement),
            "distinct equal count replacement remains fatal");
        Refuses(() => HarvestBatch.AfterReplant(true, true, original, Set(2, 100), Set(2)),
            "earlier ownership loss cannot be legitimized by planting");
        Refuses(() => HarvestBatch.AfterReplant(true, true, original, sharedHarvest, null!),
            "unknown final ownership refuses delivery");
        return checks;
    }
}
