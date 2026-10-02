using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Cheeze.Managed;

public sealed class SaveSnapshotLoad
{
    public bool Active { get; private set; }
    public bool Quarantined { get; private set; }
    private string? hash;
    private string? payload;

    public void Begin()
    {
        if (Active) Quarantined = true;
        Active = true;
        hash = payload = null;
    }

    public void Stage(string digest, string? snapshot)
    {
        if (!Active || Quarantined) return;
        hash = digest;
        payload = snapshot;
    }

    public string? Complete(string? completedHash)
    {
        string? result = Active && !Quarantined && hash != null && hash == completedHash ? payload : null;
        Active = false;
        hash = payload = null;
        return result;
    }
}

// Optional reporting state stays outside the game's own save format. Exact byte
// identity prevents histories from being attached to a different save branch.
public sealed class SaveSnapshotStore(string directory)
{
    public sealed record Envelope(int Schema, string SaveHash, string Payload);
    public static string Hash(string savePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(savePath);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    public string? Read(string hash)
    {
        string path = SnapshotPath(hash);
        if (File.Exists(path + ".invalid")) return null;
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new FormatException("Receipt snapshot is too large.");
        var data = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path));
        if (data == null || data.Schema != 1 || data.SaveHash != hash || string.IsNullOrEmpty(data.Payload))
            throw new FormatException("Receipt snapshot does not match this save.");
        return data.Payload;
    }

    public void Write(string savePath, string payload)
    {
        string hash = Hash(savePath);
        string path = SnapshotPath(hash);
        Directory.CreateDirectory(directory);
        // Conflicting histories for byte-identical saves are ambiguous. Retain
        // neither attribution rather than replacing one branch's receipts.
        if (File.Exists(path))
        {
            string? existing = Read(hash);
            if (existing == payload) return;
            File.WriteAllText(path + ".invalid", "Conflicting receipt histories for identical save bytes.");
            throw new InvalidOperationException("Different receipts already exist for identical save bytes.");
        }
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new Envelope(1, hash, payload));
                stream.Flush(true);
            }
            if (Hash(savePath) != hash) throw new IOException("Save changed while recording receipts.");
            File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private string SnapshotPath(string hash)
    {
        if (hash.Length != 64) throw new ArgumentException("Invalid save digest.");
        foreach (char c in hash)
            if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F'))) throw new ArgumentException("Invalid save digest.");
        return Path.Combine(directory, hash + ".json");
    }
}
