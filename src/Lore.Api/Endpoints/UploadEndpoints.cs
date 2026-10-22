using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Dapper;
using Lore.Api.Data;
using Lore.Api.Media;
using Lore.Api.Storage;
using Lore.Contracts;
using Microsoft.Data.Sqlite;

namespace Lore.Api.Endpoints;

/// <summary>
/// Resumable upload: create (or dedupe) -> PUT chunks at the server's offset -> complete.
/// The .part file length is the only source of truth for received bytes, so a crash mid-chunk resumes cleanly.
/// </summary>
public static partial class UploadEndpoints
{
    public const int ChunkSize = 8 * 1024 * 1024;

    [GeneratedRegex("^[0-9a-f]{64}$")] private static partial Regex Sha256Hex();
    [GeneratedRegex(@"^\.[a-z0-9]{1,10}$")] private static partial Regex SafeExtension();

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/uploads", async (CreateUploadRequest r, ClaimsPrincipal user, Db db, LorePaths paths) =>
        {
            var sha = r.Sha256?.ToLowerInvariant() ?? "";
            var filename = Path.GetFileName(r.Filename ?? "");
            if (!Sha256Hex().IsMatch(sha)) return Results.BadRequest("sha256 must be 64 hex chars");
            if (r.Size <= 0) return Results.BadRequest("size must be positive");
            if (filename.Length is 0 or > 255) return Results.BadRequest("filename required");
            if (r.MimeType is null || !(r.MimeType.StartsWith("image/") || r.MimeType.StartsWith("video/")))
                return Results.BadRequest("only image/* and video/* are accepted");

            using var c = db.Open();
            var existing = await c.QuerySingleOrDefaultAsync<Guid?>("SELECT Id FROM MediaAsset WHERE SHA256 = @sha", new { sha });
            if (existing != null)
            {
                // Uploading something that sits in the recycle bin brings it back.
                await c.ExecuteAsync("UPDATE MediaAsset SET DeletedAt = NULL WHERE Id = @existing", new { existing });
                return Results.Ok(new CreateUploadResponse(true, existing, null, 0, ChunkSize));
            }
            if (!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM FamilyMember WHERE Id = @OwnerId)", r))
                return Results.BadRequest("Unknown owner");

            // Same device + same content = the same upload: re-POST is how a client resumes after losing its upload id.
            var id = await c.ExecuteScalarAsync<Guid>("""
                INSERT INTO Upload(Id, DeviceId, Sha256, Size, OriginalFilename, MimeType, OwnerId, ClientDateTaken, CreatedAt)
                VALUES (@newId, @deviceId, @sha, @Size, @filename, @MimeType, @OwnerId, @ClientDateTaken, @now)
                ON CONFLICT(DeviceId, Sha256) DO UPDATE SET
                    OriginalFilename = excluded.OriginalFilename, MimeType = excluded.MimeType,
                    OwnerId = excluded.OwnerId, ClientDateTaken = excluded.ClientDateTaken
                RETURNING Id
                """,
                new { newId = Guid.CreateVersion7(), deviceId = user.DeviceId(), sha, r.Size, filename, r.MimeType, r.OwnerId, r.ClientDateTaken, now = DateTimeOffset.UtcNow });

            return Results.Ok(new CreateUploadResponse(false, null, id, paths.PartLength(id), ChunkSize));
        });

        api.MapGet("/uploads/{id:guid}", async (Guid id, ClaimsPrincipal user, Db db, LorePaths paths) =>
            await FindAsync(db, id, user) is { } up
                ? Results.Ok(new UploadStatusDto(id, paths.PartLength(id), up.Size))
                : Results.NotFound());

        api.MapPut("/uploads/{id:guid}", async (Guid id, long offset, HttpRequest req, ClaimsPrincipal user, Db db, LorePaths paths, CancellationToken ct) =>
        {
            if (await FindAsync(db, id, user) is not { } up) return Results.NotFound();

            FileStream part;
            try
            {
                // FileShare.None: a second concurrent PUT for the same upload gets 409 instead of interleaving bytes.
                part = new FileStream(paths.Part(id), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                return Results.Conflict(new UploadStatusDto(id, paths.PartLength(id), up.Size));
            }

            await using (part)
            {
                if (part.Length != offset) return Results.Conflict(new UploadStatusDto(id, part.Length, up.Size));
                part.Seek(0, SeekOrigin.End);

                var buffer = new byte[81920];
                long written = 0;
                int n;
                while ((n = await req.Body.ReadAsync(buffer, ct)) > 0)
                {
                    if (written + n > ChunkSize || offset + written + n > up.Size)
                    {
                        part.SetLength(offset);
                        return Results.BadRequest("Chunk exceeds chunk size or declared file size");
                    }
                    await part.WriteAsync(buffer.AsMemory(0, n), ct);
                    written += n;
                }
                await part.FlushAsync(ct);
                return Results.Ok(new UploadStatusDto(id, part.Length, up.Size));
            }
        });

        api.MapPost("/uploads/{id:guid}/complete", async (Guid id, ClaimsPrincipal user, Db db, LorePaths paths, StorageRegistry storage,
            IEnumerable<IIngestStep> steps, CancellationToken ct) =>
        {
            if (await FindAsync(db, id, user) is not { } up) return Results.NotFound();
            var partPath = paths.Part(id);
            var received = paths.PartLength(id);
            if (received != up.Size) return Results.Conflict(new UploadStatusDto(id, received, up.Size));

            string actualSha;
            await using (var f = File.OpenRead(partPath))
                actualSha = Convert.ToHexStringLower(await SHA256.HashDataAsync(f, ct));

            using var c = db.Open();
            if (actualSha != up.Sha256)
            {
                await c.ExecuteAsync("DELETE FROM Upload WHERE Id = @id", new { id });
                File.Delete(partPath);
                return Results.Problem("Content does not match declared SHA-256; upload discarded", statusCode: 422);
            }

            var ext = Path.GetExtension(up.OriginalFilename).ToLowerInvariant();
            var volume = storage.Primary;
            var media = new MediaAssetRow
            {
                Id = Guid.CreateVersion7(),
                StorageVolumeId = volume.Id,
                ObjectKey = $"originals/{up.Sha256[..2]}/{up.Sha256[2..4]}/{up.Sha256}{(SafeExtension().IsMatch(ext) ? ext : "")}",
                OriginalFilename = up.OriginalFilename,
                MediaType = up.MimeType.StartsWith("video/") ? MediaTypes.Video : MediaTypes.Photo,
                MimeType = up.MimeType,
                FileSize = up.Size,
                SHA256 = up.Sha256,
                OwnerId = up.OwnerId,
                UploadedById = await c.ExecuteScalarAsync<int?>("SELECT DefaultOwnerId FROM Device WHERE Id = @DeviceId", up) ?? up.OwnerId,
                DeviceId = up.DeviceId,
                DateUploaded = DateTimeOffset.UtcNow,
            };
            foreach (var step in steps) await step.RunAsync(partPath, media, ct);
            if (media.DateTaken == default) media.DateTaken = up.ClientDateTaken ?? DateTime.Now;

            await volume.PutAsync(partPath, media.ObjectKey, ct);
            var duplicate = false;
            try
            {
                await c.ExecuteAsync(MediaAssetRow.InsertSql, media);
            }
            catch (SqliteException e) when (e.SqliteErrorCode == 19) // UNIQUE(SHA256): another device finished the same file first
            {
                media.Id = await c.ExecuteScalarAsync<Guid>("SELECT Id FROM MediaAsset WHERE SHA256 = @SHA256", media);
                duplicate = true;
            }
            await c.ExecuteAsync("DELETE FROM Upload WHERE Id = @id", new { id });
            File.Delete(partPath);
            return Results.Ok(new CompleteUploadResponse(media.Id, duplicate));
        });
    }

    static async Task<UploadRow?> FindAsync(Db db, Guid id, ClaimsPrincipal user)
    {
        using var c = db.Open();
        return await c.QuerySingleOrDefaultAsync<UploadRow>("SELECT * FROM Upload WHERE Id = @id AND DeviceId = @d", new { id, d = user.DeviceId() });
    }
}

public sealed class LorePaths(string dataRoot)
{
    public string Uploads { get; } = Directory.CreateDirectory(Path.Combine(dataRoot, "uploads")).FullName;
    public string Derived { get; } = Directory.CreateDirectory(Path.Combine(dataRoot, "derived")).FullName;
    public string Snapshots { get; } = Directory.CreateDirectory(Path.Combine(dataRoot, "db-snapshots")).FullName;

    public string Part(Guid uploadId) => Path.Combine(Uploads, uploadId.ToString("N") + ".part");
    public long PartLength(Guid uploadId) => File.Exists(Part(uploadId)) ? new FileInfo(Part(uploadId)).Length : 0;
}
