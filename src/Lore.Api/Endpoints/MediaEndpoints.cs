using System.Globalization;
using System.Text;
using Dapper;
using Lore.Api.Data;
using Lore.Api.Media;
using Lore.Api.Storage;
using Lore.Contracts;

namespace Lore.Api.Endpoints;

public static class MediaEndpoints
{
    const string Immutable = "private, max-age=31536000, immutable";

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/media", async (Db db, [AsParameters] MediaFilter q) =>
        {
            var take = Math.Clamp(q.Limit ?? 60, 1, 200);
            var where = new List<string> { q.Deleted == true ? "DeletedAt IS NOT NULL" : "DeletedAt IS NULL" };
            var p = new DynamicParameters();
            if (q.Favorite == true) where.Add("Favorite = 1");
            if (q.From != null) { where.Add("DateTaken >= @from"); p.Add("from", q.From.Value); }
            if (q.To != null) { where.Add("DateTaken < @to"); p.Add("to", q.To.Value); }
            if (q.OwnerId != null) { where.Add("OwnerId = @ownerId"); p.Add("ownerId", q.OwnerId); }
            if (q.Type != null)
            {
                if (q.Type is not (MediaTypes.Photo or MediaTypes.Video)) return Results.BadRequest("type must be photo or video");
                where.Add("MediaType = @type"); p.Add("type", q.Type);
            }
            if (q.DeviceId != null) { where.Add("DeviceId = @deviceId"); p.Add("deviceId", q.DeviceId.Value); }
            if (q.FolderId != null)
            {
                // A folder includes everything in its subfolders.
                where.Add($"FolderId IN ({OrganizeEndpoints.SubtreeCte} SELECT Id FROM sub)");
                p.Add("root", q.FolderId);
            }
            if (q.AlbumId != null) { where.Add("Id IN (SELECT MediaId FROM AlbumMedia WHERE AlbumId = @albumId)"); p.Add("albumId", q.AlbumId); }
            if (q.Tag != null) { where.Add("Id IN (SELECT mt.MediaId FROM MediaTag mt JOIN Tag t ON t.Id = mt.TagId WHERE t.Name = @tag)"); p.Add("tag", q.Tag); }
            if (q.HasGps != null) where.Add(q.HasGps.Value ? "Latitude IS NOT NULL" : "Latitude IS NULL");
            if (q.MinLat != null || q.MaxLat != null || q.MinLon != null || q.MaxLon != null)
            {
                if (q is not { MinLat: { } minLat, MaxLat: { } maxLat, MinLon: { } minLon, MaxLon: { } maxLon } || minLat > maxLat || minLon > maxLon)
                    return Results.BadRequest("Area needs minLat <= maxLat and minLon <= maxLon");
                // Half-open box so adjacent place cells never share an item.
                where.Add("Latitude >= @minLat AND Latitude < @maxLat AND Longitude >= @minLon AND Longitude < @maxLon");
                p.AddDynamicParams(new { minLat, maxLat, minLon, maxLon });
            }
            if (q.Cursor != null)
            {
                if (DecodeCursor(q.Cursor) is not { } cur) return Results.BadRequest("bad cursor");
                where.Add("(DateTaken < @cDate OR (DateTaken = @cDate AND Id < @cId))");
                p.Add("cDate", cur.Date);
                p.Add("cId", cur.Id);
            }
            p.Add("take", take + 1);

            using var c = db.Open();
            var rows = (await c.QueryAsync<MediaAssetRow>(
                $"SELECT {MediaAssetRow.Columns} FROM MediaAsset WHERE {string.Join(" AND ", where)} ORDER BY DateTaken DESC, Id DESC LIMIT @take", p)).ToList();
            var next = rows.Count > take ? EncodeCursor(rows[take - 1]) : null;
            return Results.Ok(new MediaPage(rows.Take(take).Select(r => r.ToDto()).ToList(), next));
        });

        api.MapGet("/media/{id:guid}", async (Guid id, Db db) =>
            await FindAsync(db, id) is { } m ? Results.Ok(m.ToDto()) : Results.NotFound());

        foreach (var kind in Thumbnails.Sizes.Keys)
        {
            api.MapGet($"/media/{{id:guid}}/{kind}", async (Guid id, Db db, Thumbnails thumbs, HttpResponse res, CancellationToken ct) =>
            {
                if (await FindAsync(db, id) is not { } m) return Results.NotFound();
                if (await thumbs.GetAsync(m, kind, ct) is not { } path) return Results.NotFound();
                res.Headers.CacheControl = Immutable;
                return Results.File(path, "image/jpeg");
            });
        }

        api.MapPut("/media/{id:guid}/favorite", async (Guid id, SetFavoriteRequest r, Db db) =>
            await ExecAsync(db, "UPDATE MediaAsset SET Favorite = @Favorite WHERE Id = @id", new { id, r.Favorite }));

        // Delete only moves to the recycle bin; originals are removed solely by emptying the bin.
        api.MapDelete("/media/{id:guid}", async (Guid id, Db db) =>
            await ExecAsync(db, "UPDATE MediaAsset SET DeletedAt = @now WHERE Id = @id AND DeletedAt IS NULL", new { id, now = DateTimeOffset.UtcNow }));

        api.MapPost("/media/{id:guid}/restore", async (Guid id, Db db) =>
            await ExecAsync(db, "UPDATE MediaAsset SET DeletedAt = NULL WHERE Id = @id AND DeletedAt IS NOT NULL", new { id }));

        api.MapPost("/bin/empty", async (Db db, StorageRegistry storage, Thumbnails thumbs, ILoggerFactory logs) =>
        {
            using var c = db.Open();
            var rows = (await c.QueryAsync<MediaAssetRow>($"SELECT {MediaAssetRow.Columns} FROM MediaAsset WHERE DeletedAt IS NOT NULL")).ToList();
            foreach (var m in rows)
            {
                // Row first: if file removal fails, an orphan file is harmless; a row pointing at nothing is not.
                await c.ExecuteAsync("DELETE FROM MediaAsset WHERE Id = @Id", m);
                try
                {
                    storage.Get(m.StorageVolumeId).Delete(m.ObjectKey);
                    thumbs.Delete(m.Id);
                }
                catch (IOException e)
                {
                    logs.CreateLogger("Bin").LogWarning(e, "Could not delete files for {Id}", m.Id);
                }
            }
            await c.ExecuteAsync("DELETE FROM Tag WHERE Id NOT IN (SELECT TagId FROM MediaTag)"); // album/tag links cascaded away
            return Results.Ok(new EmptyBinResponse(rows.Count, rows.Sum(r => r.FileSize)));
        });

        api.MapGet("/stats/storage", async (Db db, StorageRegistry storage) =>
        {
            using var c = db.Open();
            var groups = (await c.QueryAsync<StorageGroupRow>("""
                SELECT OwnerId, MediaType, DeletedAt IS NOT NULL AS InBin, COUNT(*) AS Count, SUM(FileSize) AS Bytes
                FROM MediaAsset GROUP BY OwnerId, MediaType, InBin
                """)).ToList();
            var (total, free) = storage.Primary.GetUsage();
            return new StorageStatsDto(
                total, free,
                groups.Where(g => !g.InBin).Select(g => new StorageBreakdownDto(g.OwnerId, g.MediaType, g.Count, g.Bytes)).ToList(),
                groups.Where(g => g.InBin).Sum(g => g.Count),
                groups.Where(g => g.InBin).Sum(g => g.Bytes));
        });

        // cell: grid size in degrees (0.01 is about 1 km, 0.1 about 11 km).
        api.MapGet("/places", async (Db db, double? cell) =>
        {
            var size = cell ?? 0.1;
            if (size is < 0.001 or > 10) return Results.BadRequest("cell must be between 0.001 and 10 degrees");
            using var c = db.Open();
            // shortcut: groups in memory; fine for a family library (100k GPS rows is a few MB). Move to SQL if it gets slow.
            var rows = await c.QueryAsync<GpsRow>(
                "SELECT Id, Latitude, Longitude, DateTaken FROM MediaAsset WHERE Latitude IS NOT NULL AND Longitude IS NOT NULL AND DeletedAt IS NULL");
            var places = rows
                .GroupBy(r => (Lat: (long)Math.Floor(r.Latitude / size), Lon: (long)Math.Floor(r.Longitude / size)))
                .Select(g => new PlaceDto(
                    g.Average(r => r.Latitude), g.Average(r => r.Longitude),
                    g.Key.Lat * size, (g.Key.Lat + 1) * size, g.Key.Lon * size, (g.Key.Lon + 1) * size,
                    g.Count(), g.MaxBy(r => r.DateTaken)!.Id, g.Min(r => r.DateTaken), g.Max(r => r.DateTaken)))
                .OrderByDescending(pl => pl.Count)
                .ToList();
            return Results.Ok(places);
        });

        api.MapGet("/media/{id:guid}/original", async (Guid id, Db db, StorageRegistry storage, HttpResponse res) =>
        {
            if (await FindAsync(db, id) is not { } m) return Results.NotFound();
            res.Headers.CacheControl = Immutable;
            return Results.Stream(storage.Get(m.StorageVolumeId).OpenRead(m.ObjectKey), m.MimeType, enableRangeProcessing: true);
        });
    }

    /// <summary>Library query. from: inclusive, to: exclusive (local wall-clock, same as DateTaken). deleted=true lists the recycle bin.</summary>
    public sealed record MediaFilter(
        DateTime? From, DateTime? To, int? OwnerId, string? Type, bool? Favorite, bool? Deleted,
        Guid? DeviceId, int? FolderId, int? AlbumId, string? Tag, bool? HasGps,
        double? MinLat, double? MaxLat, double? MinLon, double? MaxLon, string? Cursor, int? Limit);

    static async Task<IResult> ExecAsync(Db db, string sql, object args)
    {
        using var c = db.Open();
        return await c.ExecuteAsync(sql, args) == 0 ? Results.NotFound() : Results.NoContent();
    }

    sealed class GpsRow
    {
        public Guid Id { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public DateTime DateTaken { get; set; }
    }

    sealed class StorageGroupRow
    {
        public int OwnerId { get; set; }
        public string MediaType { get; set; } = "";
        public bool InBin { get; set; }
        public int Count { get; set; }
        public long Bytes { get; set; }
    }

    static async Task<MediaAssetRow?> FindAsync(Db db, Guid id)
    {
        using var c = db.Open();
        return await c.QuerySingleOrDefaultAsync<MediaAssetRow>($"SELECT {MediaAssetRow.Columns} FROM MediaAsset WHERE Id = @id", new { id });
    }

    static string EncodeCursor(MediaAssetRow r) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{r.DateTaken.ToString("O", CultureInfo.InvariantCulture)}|{r.Id}"));

    static (DateTime Date, Guid Id)? DecodeCursor(string cursor)
    {
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            return (DateTime.Parse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), Guid.Parse(parts[1]));
        }
        catch (Exception e) when (e is FormatException or IndexOutOfRangeException)
        {
            return null;
        }
    }
}
