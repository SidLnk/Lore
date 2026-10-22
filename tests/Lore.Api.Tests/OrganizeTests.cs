using System.Net;
using Lore.Client;
using Lore.Contracts;

namespace Lore.Api.Tests;

public sealed class OrganizeTests : IDisposable
{
    readonly LoreFactory _f = new();
    public void Dispose() => _f.Dispose();

    async Task<List<Guid>> SeedAsync(LoreApiClient api, int count)
    {
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var data = Fixtures.Jpeg(100 + i, new DateTime(2024, 6, 1).AddDays(i), gps: i == 0 ? (1.29, 103.85) : null);
            var up = await api.CreateUploadAsync(new(Fixtures.Sha256(data), data.Length, $"{i}.jpg", "image/jpeg", 1, null));
            await api.PutChunkAsync(up.UploadId!.Value, 0, data);
            ids.Add((await api.CompleteUploadAsync(up.UploadId!.Value)).MediaId);
        }
        return ids;
    }

    static async Task<HashSet<Guid>> IdsAsync(LoreApiClient api, MediaQuery q) =>
        (await api.GetMediaAsync(q, limit: 200)).Items.Select(m => m.Id).ToHashSet();

    [Fact]
    public async Task Nested_folder_filter_includes_subfolders()
    {
        var api = await _f.PairAsync();
        var ids = await SeedAsync(api, 3);
        var trips = await api.CreateFolderAsync("Trips", null);
        var bali = await api.CreateFolderAsync("Bali", trips.Id);

        await api.BulkEditAsync(new([ids[0]], SetFolder: true, FolderId: trips.Id));
        await api.BulkEditAsync(new([ids[1]], SetFolder: true, FolderId: bali.Id));

        Assert.Equal([ids[0], ids[1]], (await IdsAsync(api, new(FolderId: trips.Id))).Order());
        Assert.Equal([ids[1]], await IdsAsync(api, new(FolderId: bali.Id)));
        Assert.Contains(await api.GetFoldersAsync(), f => f is { Name: "Bali", Count: 1 } && f.ParentId == trips.Id);
    }

    [Fact]
    public async Task Folder_rules_names_cycles_and_delete()
    {
        var api = await _f.PairAsync();
        var ids = await SeedAsync(api, 1);
        var a = await api.CreateFolderAsync("A", null);
        var b = await api.CreateFolderAsync("B", a.Id);

        Assert.Equal(HttpStatusCode.Conflict, (await Assert.ThrowsAsync<LoreApiException>(() => api.CreateFolderAsync("A", null))).Status);
        await api.CreateFolderAsync("A", b.Id); // same name, different parent is fine
        Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAsync<LoreApiException>(() => api.UpdateFolderAsync(a.Id, "A", b.Id))).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Assert.ThrowsAsync<LoreApiException>(() => api.DeleteFolderAsync(a.Id))).Status);

        // Deleting a folder leaves its media in the library, just unfiled.
        var c = await api.CreateFolderAsync("C", null);
        await api.BulkEditAsync(new(ids, SetFolder: true, FolderId: c.Id));
        await api.DeleteFolderAsync(c.Id);
        var m = Assert.Single((await api.GetMediaAsync(new MediaQuery())).Items);
        Assert.Null(m.FolderId);
    }

    [Fact]
    public async Task Bulk_tags_albums_owner_favorite_and_filters()
    {
        var api = await _f.PairAsync();
        var ids = await SeedAsync(api, 4);
        var album = await api.CreateAlbumAsync("Birthday");

        var r = await api.BulkEditAsync(new(ids[..3], OwnerId: 3, AddTags: ["Beach", "  sun   set "], AddAlbumIds: [album.Id], Favorite: true));
        Assert.Equal(3, r.Updated);

        Assert.Equal(ids[..3].ToHashSet(), await IdsAsync(api, new(OwnerId: 3)));
        Assert.Equal(ids[..3].ToHashSet(), await IdsAsync(api, new(Tag: "beach"))); // tags match case-insensitively
        Assert.Equal(ids[..3].ToHashSet(), await IdsAsync(api, new(AlbumId: album.Id)));
        Assert.Equal(ids[..3].ToHashSet(), await IdsAsync(api, new(Favorite: true)));
        Assert.Contains(await api.GetTagsAsync(), t => t is { Name: "sun set", Count: 3 });

        await api.BulkEditAsync(new([ids[0]], RemoveTags: ["Beach"], RemoveAlbumIds: [album.Id]));
        var details = await api.GetDetailsAsync(ids[0]);
        Assert.Equal(["sun set"], details.Tags);
        Assert.Empty(details.AlbumIds);
        Assert.Equal("Test phone", details.DeviceName);

        // Removing a tag from everything deletes the tag.
        await api.BulkEditAsync(new(ids, RemoveTags: ["sun set", "beach"]));
        Assert.Empty(await api.GetTagsAsync());
    }

    [Fact]
    public async Task Bulk_delete_and_unknown_targets()
    {
        var api = await _f.PairAsync();
        var ids = await SeedAsync(api, 2);
        Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAsync<LoreApiException>(() => api.BulkEditAsync(new(ids, OwnerId: 99)))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAsync<LoreApiException>(() => api.BulkEditAsync(new(ids, AddAlbumIds: [42])))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAsync<LoreApiException>(() => api.BulkEditAsync(new([])))).Status);

        await api.BulkEditAsync(new(ids, Delete: true));
        Assert.Empty((await api.GetMediaAsync(new MediaQuery())).Items);
        Assert.Equal(2, (await api.GetMediaAsync(new MediaQuery(Deleted: true))).Items.Count);
    }

    [Fact]
    public async Task Album_survives_media_and_bin_empty_cleans_links()
    {
        var api = await _f.PairAsync();
        var ids = await SeedAsync(api, 2);
        var album = await api.CreateAlbumAsync("Trip");
        await api.BulkEditAsync(new(ids, AddAlbumIds: [album.Id], AddTags: ["x"]));

        await api.BulkEditAsync(new([ids[0]], Delete: true));
        Assert.Equal(1, (await api.GetAlbumsAsync()).Single().Count); // binned media don't count
        await api.EmptyBinAsync();
        Assert.Equal([ids[1]], await IdsAsync(api, new(AlbumId: album.Id)));

        await api.DeleteAlbumAsync(album.Id);
        Assert.Single((await api.GetMediaAsync(new MediaQuery())).Items); // media stay
    }

    [Fact]
    public async Task Caption_device_and_gps_filters()
    {
        var a = await _f.PairAsync("Phone A");
        var ids = await SeedAsync(a, 2);
        await a.SetCaptionAsync(ids[0], "  Sunset at the pier ");
        Assert.Equal("Sunset at the pier", (await a.GetDetailsAsync(ids[0])).Media.Caption);
        await a.SetCaptionAsync(ids[0], "");
        Assert.Null((await a.GetDetailsAsync(ids[0])).Media.Caption);

        Assert.Equal([ids[0]], await IdsAsync(a, new(HasGps: true)));
        Assert.Equal([ids[1]], await IdsAsync(a, new(HasGps: false)));

        var b = await _f.PairAsync("Phone B");
        var devices = await b.GetDevicesAsync();
        var phoneA = devices.Single(d => d.Name == "Phone A").Id;
        Assert.Equal(ids.ToHashSet(), await IdsAsync(b, new(DeviceId: phoneA)));
        Assert.Empty(await IdsAsync(b, new(DeviceId: devices.Single(d => d.Name == "Phone B").Id)));
    }
}
