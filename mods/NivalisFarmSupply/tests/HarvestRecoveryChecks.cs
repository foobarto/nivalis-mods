using System;
using System.Collections.Generic;
using Cheeze.Managed;

public static class HarvestRecoveryChecks
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
        void Throws<T>(Action action, string name) where T : Exception
        {
            checks++;
            try { action(); }
            catch (T) { return; }
            throw new Exception(name + ": expected refusal");
        }

        var unit = new Unit();
        var result = unit.Replant();
        Equal(ReplantReason.NoSeedAvailable, result.Reason, "no real seed");
        Equal(false, result.Replanted, "missing seed is local failure");
        Equal(0, unit.Attempts, "missing seed never calls native planting");

        unit = new Unit(1, 2) { ReportedSuccess = false, Commit = false };
        result = unit.Replant();
        Equal(ReplantReason.RejectedWithoutMutation, result.Reason, "false and no mutation");
        Equal(false, result.Replanted, "rejected replant is local failure");
        Equal(1, unit.Attempts, "rejection is attempted once");
        Equal(true, unit.Seeds.SetEquals(new[] { 1, 2 }), "rejection retains all owned seeds");

        unit = new Unit(1, 2) { Commit = false };
        result = unit.Replant();
        Equal(ReplantReason.ReportedSuccessWithoutMutation, result.Reason, "true result cannot establish planting");
        Equal(false, result.Replanted, "no observed mutation remains local failure");
        Equal(1, unit.Attempts, "false success is not retried");

        unit = new Unit(1, 2);
        result = unit.Replant();
        Equal(ReplantReason.Replanted, result.Reason, "normal native commit");
        Equal(true, result.Replanted, "normal native state verifies");
        Equal(true, unit.Seeds.SetEquals(new[] { 2 }), "exact original seed removed");
        Equal(1, unit.Attempts, "normal commit attempts once");
        Throws<InvalidOperationException>(() => unit.Replant(), "same planted module refuses another attempt");
        Equal(1, unit.Attempts, "already planted module never calls native again");

        unit = new Unit(1, 2) { ReportedSuccess = false };
        result = unit.Replant();
        Equal(ReplantReason.ReplantedDespiteFalseResult, result.Reason, "false after commit uses actual state");
        Equal(true, result.Replanted, "false after commit is complete");
        Equal(1, unit.Attempts, "false after commit never retries");

        unit = new Unit(1, 2) { WrongCrop = true };
        Throws<InvalidOperationException>(() => unit.Replant(), "wrong crop is uncertain mutation");
        Equal(1, unit.Attempts, "wrong crop never triggers corrective planting");
        unit = new Unit(1, 2) { RemoveCount = 0 };
        Throws<InvalidOperationException>(() => unit.Replant(), "planting without a real seed consumption");
        unit = new Unit(1, 2) { RemoveCount = 2 };
        Throws<InvalidOperationException>(() => unit.Replant(), "two seed consumption is refused");
        unit = new Unit(1, 2, 3) { RemoveCount = 2, AddedSeed = 99 };
        Throws<InvalidOperationException>(() => unit.Replant(), "matching count delta cannot conceal seed replacement");
        unit = new Unit(1, 2) { LeaveEmpty = true };
        Throws<InvalidOperationException>(() => unit.Replant(), "seed removed without planted crop");
        unit = new Unit(1, 2) { ReadyAfterCommit = true };
        Throws<InvalidOperationException>(() => unit.Replant(), "ready module is not verified growing replant");
        unit = new Unit(1) { Ready = true };
        Throws<InvalidOperationException>(() => unit.Replant(), "unplanted but ready initial state");
        Equal(0, unit.Attempts, "uncertain initial state never calls native planting");

        unit = new Unit(1) { SnapshotError = new InvalidOperationException("unknown ownership") };
        Throws<InvalidOperationException>(() => unit.Replant(), "ownership exception remains fatal");
        Equal(0, unit.Attempts, "failed before snapshot prevents mutation");
        unit = new Unit(1) { NullSnapshot = true };
        Throws<InvalidOperationException>(() => unit.Replant(), "null snapshot does not mean no seeds");
        Equal(0, unit.Attempts, "null snapshot prevents mutation");
        unit = new Unit(1) { FailPostSnapshot = true };
        Throws<InvalidOperationException>(() => unit.Replant(), "postcommit ownership cannot be guessed");
        Equal(1, unit.Attempts, "failed post snapshot never retries");

        unit = new Unit(1) { Commit = false, PlantError = new InvalidOperationException("native rejection") };
        Throws<InvalidOperationException>(() => unit.Replant(), "ordinary native exception propagates");
        Equal(1, unit.Attempts, "native exception has one attempt");
        unit = new Unit(1) { PlantError = new InvalidOperationException("native error after commit") };
        Throws<InvalidOperationException>(() => unit.Replant(), "native exception after mutation remains visible");
        Equal(true, unit.Planted, "propagation does not undo committed crop");
        Equal(0, unit.Seeds.Count, "propagation does not manufacture lost seed");
        Equal(1, unit.Attempts, "exception after commit never retries");

        foreach (Exception error in new Exception[] { new MissingMemberException(), new TypeLoadException(),
            new BadImageFormatException(), new InvalidProgramException() })
        {
            unit = new Unit(1) { Commit = false, PlantError = error };
            checks++;
            try { unit.Replant(); throw new Exception("compatibility error was suppressed"); }
            catch (Exception caught) when (ReferenceEquals(caught, error)) { }
            Equal(1, unit.Attempts, "compatibility exception never retries");
        }
        return checks;
    }

    private sealed class Unit(params int[] seeds)
    {
        public readonly HashSet<int> Seeds = new(seeds);
        public int Attempts, RemoveCount = 1;
        public bool Planted, Ready, ReportedSuccess = true, Commit = true;
        public bool WrongCrop, LeaveEmpty, ReadyAfterCommit, NullSnapshot, FailPostSnapshot;
        public int? AddedSeed;
        public Exception? SnapshotError, PlantError;

        // Deliberately return our backing set: the helper must clone its before image.
        private HashSet<int> Snapshot()
        {
            if (SnapshotError != null) throw SnapshotError;
            if (FailPostSnapshot && Attempts != 0) throw new InvalidOperationException("postcommit ownership unavailable");
            return NullSnapshot ? null! : Seeds;
        }
        private bool Plant()
        {
            Attempts++;
            if (Commit)
            {
                var owned = new List<int>(Seeds);
                owned.Sort();
                for (int i = 0; i < RemoveCount && i < owned.Count; i++) Seeds.Remove(owned[i]);
                if (AddedSeed.HasValue) Seeds.Add(AddedSeed.Value);
                Planted = !LeaveEmpty;
                Ready = ReadyAfterCommit;
            }
            if (PlantError != null) throw PlantError;
            return ReportedSuccess;
        }
        public ReplantResult Replant() => HarvestRecovery.Replant(Snapshot, () => Planted,
            () => !WrongCrop, () => Ready, Plant);
    }
}
