using System.Text.RegularExpressions;
using Dapper;
using Lore.Api.Data;
using Lore.Contracts;
using Microsoft.Data.Sqlite;

namespace Lore.Api.Endpoints;

/// <summary>Virtual folders, albums, tags, captions and bulk edits. None of these touch files on disk.</summary>
public static partial class OrganizeEndpoints
{
    const int MaxBulk = 2000;

    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();

    /// <summary>Trimmed, single-spaced, 1..max chars; null when invalid.</summary>
    static string? CleanName(string? name, int max = 100)
    {
        var n = Spaces().Replace(name ?? "", " ").Trim();
        return n.Length is 0 || n.Length > max ? null : n;
    }

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/devices", async (Db db) =>
        {
            using var c = db.Open();
            return (await c.QueryAsync<DeviceRow>("SELECT * FROM Device ORDER BY Name")).Select(d => d.ToDto());
        });

        // ---- Folders ----
        api.MapGet("/folders", async (Db db) =>
        {
            using var c = db.Open();
            return (await c.QueryAsync<FolderRow>("""
                SELECT f.Id, f.ParentId, f.Name,
                       (SELECT COUNT(*) FROM MediaAsset m WHERE m.FolderId = f.Id AND m.DeletedAt IS NULL) AS Count
                FROM Folder f ORDER BY f.Name
                """)).Select(f => f.ToDto());
        });

        api.MapPost("/folders", async (SaveFolderRequest r, Db db) =>
        {
            if (CleanName(r.Name) is not { } name) return Results.BadRequest("Folder name required (max 100 chars)");
            using var c = db.Open();
            if (r.ParentId != null && !await ExistsAsync(c, "Folder", r.ParentId.Value)) return Results.BadRequest("Unknown parent folder");
            try
            {
                var id = await c.ExecuteScalarAsync<int>("INSERT INTO Folder(Name, ParentId) VALUES (@name, @ParentId) RETURNING Id", new { name, r.ParentId });
                return Results.Ok(new FolderDto(id, r.ParentId, name, 0));
            }
            catch (SqliteException e) when (e.SqliteErrorCode == 19) { return Results.Conflict("A folder with that name already exists here"); }
        });

        api.MapPut("/folders/{id:int}", async (int id, SaveFolderRequest r, Db db) =>
        {
            if (CleanName(r.Name) is not { } name) return Results.BadRequest("Folder name required (max 100 chars)");
            using var c = db.Open();
            if (!await ExistsAsync(c, "Folder", id)) return Results.NotFound();
            if (r.ParentId != null)
            {
                if (!await ExistsAsync(c, "Folder", r.ParentId.Value)) return Results.BadRequest("Unknown parent folder");
                // A folder can't move under itself or any of its descendants.
                var subtree = await c.QueryAsync<int>($"{SubtreeCte} SELECT Id FROM sub", new { root = id });
                if (subtree.Contains(r.ParentId.Value)) return Results.BadRequest("A folder can't be moved inside itself");
            }
            try
            {
                await c.ExecuteAsync("UPDATE Folder SET Name = @name, ParentId = @ParentId WHERE Id = @id", new { id, name, r.ParentId });
                return Results.NoContent();
            }
            catch (SqliteException e) when (e.SqliteErrorCode == 19) { return Results.Conflict("A folder with that name already exists here"); }
        });

        // Media in the folder fall back to "no folder" (FK ON DELETE SET NULL); files are untouched.
        api.MapDelete("/folders/{id:int}", async (int id, Db db) =>
        {
            using var c = db.Open();
            if (await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM Folder WHERE ParentId = @id)", new { id }))
                return Results.Conflict("Folder has subfolders; delete or move them first");
            return await c.ExecuteAsync("DELETE FROM Folder WHERE Id = @id", new { id }) == 0 ? Results.NotFound() : Results.NoContent();
        });

        // ---- Albums ----
        api.MapGet("/albums", async (Db db) =>
        {
            using var c = db.Open();
            return (await c.QueryAsync<NamedCountRow>("""
                SELECT a.Id, a.Name, (SELECT COUNT(*) FROM AlbumMedia am JOIN MediaAsset m ON m.Id = am.MediaId
                                      WHERE am.AlbumId = a.Id AND m.DeletedAt IS NULL) AS Count
                FROM Album a ORDER BY a.Name
                """)).Select(a => new AlbumDto(a.Id, a.Name, a.Count));
        });

        api.MapPost("/albums", async (SaveAlbumRequest r, Db db) =>
        {
            if (CleanName(r.Name) is not { } name) return Results.BadRequest("Album name required (max 100 chars)");
            using var c = db.Open();
            try
            {
                var id = await c.ExecuteScalarAsync<int>("INSERT INTO Album(Name, CreatedAt) VALUES (@name, @now) RETURNING Id", new { name, now = DateTimeOffset.UtcNow });
                return Results.Ok(new AlbumDto(id, name, 0));
            }
            catch (SqliteException e) when (e.SqliteErrorCode == 19) { return Results.Conflict("An album with that name already exists"); }
        });

        api.MapPut("/albums/{id:int}", async (int id, SaveAlbumRequest r, Db db) =>
        {
            if (CleanName(r.Name) is not { } name) return Results.BadRequest("Album name required (max 100 chars)");
            using var c = db.Open();
            try
            {
                return await c.ExecuteAsync("UPDATE Album SET Name = @name WHERE Id = @id", new { id, name }) == 0 ? Results.NotFound() : Results.NoContent();
            }
            catch (SqliteException e) when (e.SqliteErrorCode == 19) { return Results.Conflict("An album with that name already exists"); }
        });

        // Removes the album only; its media stay in the library.
        api.MapDelete("/albums/{id:int}", async (int id, Db db) =>
        {
            using var c = db.Open();
            return await c.ExecuteAsync("DELETE FROM Album WHERE Id = @id", new { id }) == 0 ? Results.NotFound() : Results.NoContent();
        });

        // ---- Tags (created on first use, removed when unused) ----
        api.MapGet("/tags", async (Db db) =>
        {
            using var c = db.Open();
            return (await c.QueryAsync<NamedCountRow>("""
                SELECT t.Id, t.Name, (SELECT COUNT(*) FROM MediaTag mt JOIN MediaAsset m ON m.Id = mt.MediaId
                                      WHERE mt.TagId = t.Id AND m.DeletedAt IS NULL) AS Count
                FROM Tag t ORDER BY t.Name
                """)).Select(t => new TagDto(t.Id, t.Name, t.Count));
        });

        // ---- Per-item ----
        api.MapPut("/media/{id:guid}/caption", async (Guid id, SetCaptionRequest r, Db db) =>
        {
            var caption = r.Caption?.Trim();
            if (caption?.Length > 2000) return Results.BadRequest("Caption max 2000 chars");
            using var c = db.Open();
            return await c.ExecuteAsync("UPDATE MediaAsset SET Caption = @caption WHERE Id = @id",
                new { id, caption = string.IsNullOrEmpty(caption) ? null : caption }) == 0 ? Results.NotFound() : Results.NoContent();
        });

        api.MapGet("/media/{id:guid}/details", async (Guid id, Db db) =>
        {
            using var c = db.Open();
            var m = await c.QuerySingleOrDefaultAsync<MediaAssetRow>($"SELECT {MediaAssetRow.Columns} FROM MediaAsset WHERE Id = @id", new { id });
            if (m == null) return Results.NotFound();
            var tags = await c.QueryAsync<string>("SELECT t.Name FROM MediaTag mt JOIN Tag t ON t.Id = mt.TagId WHERE mt.MediaId = @id ORDER BY t.Name", new { id });
            var albums = await c.QueryAsync<int>("SELECT AlbumId FROM AlbumMedia WHERE MediaId = @id", new { id });
            var device = await c.ExecuteScalarAsync<string?>("SELECT Name FROM Device WHERE Id = @DeviceId", m);
            return Results.Ok(new MediaDetailsDto(m.ToDto(), tags.ToList(), albums.ToList(), device));
        });

        // ---- Bulk ----
        api.MapPost("/media/bulk", async (BulkEditRequest r, Db db) =>
        {
            // As strings: Dapper's IN-list expansion skips the Guid type handler and would bind blobs.
            var ids = r.Ids?.Distinct().Select(i => i.ToString("D")).ToList() ?? [];
            if (ids.Count is 0 or > MaxBulk) return Results.BadRequest($"Select 1 to {MaxBulk} items");
            var addTags = r.AddTags?.Select(t => CleanName(t, 50)).ToList() ?? [];
            if (addTags.Contains(null)) return Results.BadRequest("Tags must be 1-50 chars");

            using var c = db.Open();
            using var tx = c.BeginTransaction();
            var found = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MediaAsset WHERE Id IN @ids", new { ids }, tx);

            if (r.OwnerId is { } owner)
            {
                if (!await ExistsAsync(c, "FamilyMember", owner, tx)) return Results.BadRequest("Unknown owner");
                await c.ExecuteAsync("UPDATE MediaAsset SET OwnerId = @owner WHERE Id IN @ids", new { ids, owner }, tx);
            }
            if (r.SetFolder)
            {
                if (r.FolderId is { } f && !await ExistsAsync(c, "Folder", f, tx)) return Results.BadRequest("Unknown folder");
                await c.ExecuteAsync("UPDATE MediaAsset SET FolderId = @FolderId WHERE Id IN @ids", new { ids, r.FolderId }, tx);
            }
            foreach (var tag in addTags)
            {
                await c.ExecuteAsync("INSERT OR IGNORE INTO Tag(Name) VALUES (@tag)", new { tag }, tx);
                await c.ExecuteAsync("""
                    INSERT OR IGNORE INTO MediaTag(MediaId, TagId)
                    SELECT m.Id, t.Id FROM MediaAsset m, Tag t WHERE m.Id IN @ids AND t.Name = @tag
                    """, new { ids, tag }, tx);
            }
            if (r.RemoveTags is { Count: > 0 } remove)
            {
                await c.ExecuteAsync("DELETE FROM MediaTag WHERE MediaId IN @ids AND TagId IN (SELECT Id FROM Tag WHERE Name IN @remove)", new { ids, remove }, tx);
                await c.ExecuteAsync("DELETE FROM Tag WHERE Id NOT IN (SELECT TagId FROM MediaTag)", transaction: tx);
            }
            foreach (var album in r.AddAlbumIds ?? [])
            {
                if (!await ExistsAsync(c, "Album", album, tx)) return Results.BadRequest("Unknown album");
                await c.ExecuteAsync("INSERT OR IGNORE INTO AlbumMedia(AlbumId, MediaId) SELECT @album, Id FROM MediaAsset WHERE Id IN @ids", new { ids, album }, tx);
            }
            if (r.RemoveAlbumIds is { Count: > 0 } removeAlbums)
                await c.ExecuteAsync("DELETE FROM AlbumMedia WHERE MediaId IN @ids AND AlbumId IN @removeAlbums", new { ids, removeAlbums }, tx);
            if (r.Favorite is { } fav)
                await c.ExecuteAsync("UPDATE MediaAsset SET Favorite = @fav WHERE Id IN @ids", new { ids, fav }, tx);
            if (r.Delete)
                await c.ExecuteAsync("UPDATE MediaAsset SET DeletedAt = @now WHERE Id IN @ids AND DeletedAt IS NULL", new { ids, now = DateTimeOffset.UtcNow }, tx);

            tx.Commit();
            return Results.Ok(new BulkEditResponse(found));
        });
    }

    /// <summary>Folder @root and all folders below it, as CTE "sub".</summary>
    public const string SubtreeCte = """
        WITH RECURSIVE sub(Id) AS (SELECT @root UNION ALL SELECT f.Id FROM Folder f JOIN sub ON f.ParentId = sub.Id)
        """;

    static Task<bool> ExistsAsync(SqliteConnection c, string table, int id, SqliteTransaction? tx = null) =>
        c.ExecuteScalarAsync<bool>($"SELECT EXISTS(SELECT 1 FROM {table} WHERE Id = @id)", new { id }, tx);

    sealed class FolderRow
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public FolderDto ToDto() => new(Id, ParentId, Name, Count);
    }

    sealed class NamedCountRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }
}
