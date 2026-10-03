using Cheeze.Managed;
using System.Text.Json;

int checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    checks++;
}
void Reject(Action action, string label)
{
    try { action(); }
    catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is InvalidOperationException)
    { checks++; return; }
    throw new Exception("Unexpected acceptance: " + label);
}

var a = new RelativeAttachment("a", "plant", "boat", 1, 2, 3, 0, 0, 0, 1);
var b = a with { Id = "b" };
var store = new RelativeAttachmentStore();
store.Put(b);
store.Put(a);
string snapshot = store.Snapshot();
var other = new RelativeAttachmentStore();
other.Put(a);
other.Put(b);
Check(snapshot == other.Snapshot(), "Snapshots must not depend on insertion order.");
other.Clear();
other.Restore(snapshot);
Check(other.Snapshot() == snapshot && other.Count == 2, "Round trip must preserve exact values.");
Check(other.TryGet("a", out var found) && found == a, "Identity lookup failed.");
Check(!other.TryGet("A", out _), "Identity must be case sensitive.");
store.Put(a with { X = 2 });
Check(store.Count == 2 && store.TryGet("a", out found) && found!.X == 2, "Pose update must replace the same identity.");
Reject(() => store.Put(a with { PrefabId = "chair" }), "prefab mismatch");
Reject(() => store.Put(a with { ParentId = "other-boat" }), "parent mismatch");
foreach (var bad in new[] {
    a with { Id = "" }, a with { Id = new string('x',257) }, a with { Id = "\n" },
    a with { PrefabId = null! }, a with { ParentId = "a" },
    a with { X = float.NaN }, a with { QW = float.PositiveInfinity },
    a with { QW = 0 }, a with { QW = 2 }, a with { X = 30 },
    a with { X = 20, Y = 20, Z = 20 }
}) Reject(() => store.Put(bad), "invalid input");
store.Put(a with { X = 30, Y = 0, Z = 0 });
Check(store.TryGet("a", out found) && found!.X == 30, "Radius boundary rejected.");
string before = store.Snapshot();
string entry = JsonSerializer.Serialize(a);
foreach (string bad in new[] {
    "", "null", "[]", snapshot[..^1], snapshot.Replace("\"Schema\":1", "\"Schema\":2"),
    "{\"Schema\":1,\"Entries\":[" + entry + "," + entry + "]}",
    "{\"Schema\":1,\"Schema\":1,\"Entries\":[]}",
    "{\"Schema\":1,\"Entries\":[" + entry.Replace("\"QW\":1", "\"QW\":0") + "]}",
    "{\"Schema\":1,\"Entries\":[" + entry.Replace("\"X\":1,", "") + "]}",
    "{\"Schema\":1,\"Entries\":[" + entry.Replace("\"X\":1", "\"X\":1,\"X\":2") + "]}",
    new string(' ', RelativeAttachmentStore.MaximumSnapshotCharacters + 1),
    JsonSerializer.Serialize(new { Schema = 1, Entries = Enumerable.Range(0,129).Select(i => a with { Id = "x" + i }).ToArray() })
})
{
    Reject(() => store.Restore(bad), "malformed snapshot");
    Check(store.Snapshot() == before, "Failed restore changed good state.");
}
store.Clear();
for (int i = 0; i < 128; i++) store.Put(a with { Id = "id" + i });
Reject(() => store.Put(a), "maximum record count");
store.Put(a with { Id = "id0", X = 4 });
Check(store.Count == 128, "Updates at capacity failed.");
Check(store.Remove("id0") && !store.Remove("id0") && store.Count == 127, "Remove failed.");
store.Clear();
store.Restore("{\"Schema\":1,\"Entries\":[]}");
Check(store.Count == 0, "Empty restore failed.");

string directory = Path.Combine(Path.GetTempPath(), "nivalis-boat-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    string saveA = Path.Combine(directory, "a.save");
    string saveB = Path.Combine(directory, "b.save");
    File.WriteAllText(saveA, "branch A");
    File.WriteAllText(saveB, "branch B");
    var disk = new SaveSnapshotStore(Path.Combine(directory, "snapshots"));
    disk.Write(saveA, snapshot);
    string hashA = SaveSnapshotStore.Hash(saveA);
    string hashB = SaveSnapshotStore.Hash(saveB);
    Check(disk.Read(hashA) == snapshot, "Exact save snapshot failed.");
    Check(disk.Read(hashB) == null, "Another save branch acquired attachments.");
    var load = new SaveSnapshotLoad();
    load.Begin();
    load.Stage(hashA, disk.Read(hashA));
    Check(load.Complete(hashB) == null, "Changed load identity accepted.");
    disk.Write(saveA, snapshot);
    Reject(() => disk.Write(saveA, "different"), "conflicting identical save snapshot");
    Check(disk.Read(hashA) == null, "Conflicting snapshot was not quarantined.");
}
finally { Directory.Delete(directory, true); }
Console.WriteLine($"PASS {checks} attachment and save identity checks");
