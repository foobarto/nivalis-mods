using System;
using System.Collections.Generic;

namespace Cheeze.Managed;

public enum ReplantReason
{
    Replanted,
    ReplantedDespiteFalseResult,
    NoSeedAvailable,
    RejectedWithoutMutation,
    ReportedSuccessWithoutMutation
}

public readonly record struct ReplantResult(bool Replanted, ReplantReason Reason);

/// <summary>Attempt one native replant and classify its observed mutation.</summary>
public static class HarvestRecovery
{
    /// <summary>
    /// The adapter supplies validated unique identities of seeds it actually owns.
    /// This helper neither creates seeds nor restores or retries a native mutation.
    /// API and snapshot exceptions propagate, including exceptions after a commit.
    /// It is not atomic against concurrent game mutations. The caller must prevent
    /// repeated processing of the same harvested unit after an uncertain outcome.
    /// </summary>
    public static ReplantResult Replant<T>(Func<HashSet<T>> seeds, Func<bool> planted,
        Func<bool> sameCrop, Func<bool> ready, Func<bool> plant) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(planted);
        ArgumentNullException.ThrowIfNull(sameCrop);
        ArgumentNullException.ThrowIfNull(ready);
        ArgumentNullException.ThrowIfNull(plant);

        if (planted() || ready())
            throw new InvalidOperationException("Replant requires a known empty, unready module.");

        var observed = seeds()
            ?? throw new InvalidOperationException("Seed ownership snapshot is unavailable.");
        // An adapter may reuse its mutable backing set. Keep our own before image.
        var before = new HashSet<T>(observed, observed.Comparer);
        if (before.Count == 0)
            return new ReplantResult(false, ReplantReason.NoSeedAvailable);

        bool reportedSuccess = plant();
        var afterObserved = seeds()
            ?? throw new InvalidOperationException("Seed ownership snapshot is unavailable after replant.");
        var after = new HashSet<T>(afterObserved, before.Comparer);
        bool isPlanted = planted();
        bool isReady = ready();

        // Set subtraction, rather than a count delta, rules out replacing an old
        // seed with a new identity while claiming that exactly one was consumed.
        bool consumedOne = after.Count == before.Count - 1 && after.IsSubsetOf(before);
        if (consumedOne && isPlanted && !isReady && sameCrop())
            return new ReplantResult(true, reportedSuccess ? ReplantReason.Replanted
                : ReplantReason.ReplantedDespiteFalseResult);

        if (!isPlanted && !isReady && after.SetEquals(before))
            return new ReplantResult(false, reportedSuccess
                ? ReplantReason.ReportedSuccessWithoutMutation : ReplantReason.RejectedWithoutMutation);

        throw new InvalidOperationException("Replant changed seed ownership or module state ambiguously.");
    }
}
