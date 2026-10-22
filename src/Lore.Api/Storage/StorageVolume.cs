namespace Lore.Api.Storage;

/// <summary>
/// A place originals live. Media rows store (StorageVolumeId, ObjectKey) only,
/// so moving HDD to RAID/NAS means remapping a volume, not rewriting rows.
/// </summary>
public interface IStorageVolume
{
    string Id { get; }
    bool Exists(string objectKey);
    Stream OpenRead(string objectKey);
    /// <summary>Moves a finished temp file into the volume. No-op if the key already exists (keys are content-addressed).</summary>
    Task PutAsync(string tempFile, string objectKey, CancellationToken ct);
    void Delete(string objectKey);
    (long Total, long Free) GetUsage();
}

public sealed class LocalStorageVolume(string id, string root) : IStorageVolume
{
    public string Id => id;

    string FullPath(string objectKey)
    {
        var full = Path.GetFullPath(Path.Combine(root, objectKey));
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Object key escapes volume root", nameof(objectKey));
        return full;
    }

    public bool Exists(string objectKey) => File.Exists(FullPath(objectKey));

    public Stream OpenRead(string objectKey) =>
        new FileStream(FullPath(objectKey), FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);

    public async Task PutAsync(string tempFile, string objectKey, CancellationToken ct)
    {
        var dest = FullPath(objectKey);
        if (File.Exists(dest)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        // Copy to a sibling temp then rename: a crash never leaves a half-written original under its real key.
        var staging = dest + ".incoming";
        await using (var src = File.OpenRead(tempFile))
        await using (var dst = new FileStream(staging, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous))
        {
            await src.CopyToAsync(dst, ct);
            await dst.FlushAsync(ct);
        }
        File.Move(staging, dest, overwrite: false);
    }

    public void Delete(string objectKey)
    {
        var path = FullPath(objectKey);
        if (!File.Exists(path)) return;
        File.Delete(path);
    }

    public (long Total, long Free) GetUsage()
    {
        Directory.CreateDirectory(root);
        var d = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
        return (d.TotalSize, d.AvailableFreeSpace);
    }
}

public sealed class StorageRegistry(IReadOnlyList<IStorageVolume> volumes)
{
    readonly Dictionary<string, IStorageVolume> _byId = volumes.ToDictionary(v => v.Id);

    /// <summary>Volume new uploads go to. shortcut: always the first configured; add placement rules when a second volume exists.</summary>
    public IStorageVolume Primary => volumes[0];
    public IStorageVolume Get(string id) => _byId[id];
}
