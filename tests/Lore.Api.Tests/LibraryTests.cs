using System.Net;
using Lore.Client;
using Lore.Contracts;

namespace Lore.Api.Tests;

public sealed class LibraryTests : IDisposable
{
    readonly LoreFactory _f = new();
    public void Dispose() => _f.Dispose();

    async Task<Guid> UploadAsync(LoreApiClient api, byte[] data, string name, string mime, int owner)
    {
        var up = await api.CreateUploadAsync(new(Fixtures.Sha256(data), data.Length, name, mime, owner, null));
        if (up.Duplicate) return up.MediaId!.Value;
        await api.PutChunkAsync(up.UploadId!.Value, 0, data);
        return (await api.CompleteUploadAsync(up.UploadId!.Value)).MediaId;
    }

    [Fact]
    public async Task Favorites_filter()
    {
        var api = await _f.PairAsync();
        var a = await UploadAsync(api, Fixtures.Jpeg(1, new DateTime(2024, 1, 1)), "a.jpg", "image/jpeg", 1);
        await UploadAsync(api, Fixtures.Jpeg(2, new DateTime(2024, 1, 2)), "b.jpg", "image/jpeg", 1);

        await api.SetFavoriteAsync(a, true);
        var favs = (await api.GetMediaAsync(new MediaQuery(Favorite: true))).Items;
        Assert.Equal([a], favs.Select(m => m.Id));
        Assert.True(favs[0].Favorite);

        await api.SetFavoriteAsync(a, false);
        Assert.Empty((await api.GetMediaAsync(new MediaQuery(Favorite: true))).Items);
    }

    [Fact]
    public async Task Delete_moves_to_bin_restore_brings_back_and_original_survives()
    {
        var api = await _f.PairAsync();
        var data = Fixtures.Jpeg(3, new DateTime(2024, 2, 1));
        var id = await UploadAsync(api, data, "c.jpg", "image/jpeg", 1);

        await api.DeleteAsync(id);
        Assert.Empty((await api.GetMediaAsync(new MediaQuery())).Items);
        var bin = (await api.GetMediaAsync(new MediaQuery(Deleted: true))).Items;
        Assert.Equal(id, Assert.Single(bin).Id);
        Assert.NotNull(bin[0].DeletedAt);
        Assert.Equal(data, await _f.CreateClient().GetByteArrayAsync(api.OriginalUrl(id))); // still on disk

        await api.RestoreAsync(id);
        Assert.Single((await api.GetMediaAsync(new MediaQuery())).Items);
        Assert.Empty((await api.GetMediaAsync(new MediaQuery(Deleted: true))).Items);
    }

    [Fact]
    public async Task Reuploading_binned_content_restores_it()
    {
        var api = await _f.PairAsync();
        var data = Fixtures.Jpeg(4, new DateTime(2024, 3, 1));
        var id = await UploadAsync(api, data, "d.jpg", "image/jpeg", 1);
        await api.DeleteAsync(id);

        Assert.Equal(id, await UploadAsync(api, data, "d.jpg", "image/jpeg", 1));
        Assert.Single((await api.GetMediaAsync(new MediaQuery())).Items);
    }

    [Fact]
    public async Task Emptying_bin_erases_only_binned_originals_and_thumbs()
    {
        var api = await _f.PairAsync();
        var keep = await UploadAsync(api, Fixtures.Jpeg(5, new DateTime(2024, 4, 1)), "keep.jpg", "image/jpeg", 1);
        var gone = await UploadAsync(api, Fixtures.Jpeg(6, new DateTime(2024, 4, 2)), "gone.jpg", "image/jpeg", 1);
        (await _f.CreateClient().GetAsync(api.ThumbUrl(gone))).EnsureSuccessStatusCode();
        await api.DeleteAsync(gone);

        var result = await api.EmptyBinAsync();
        Assert.Equal(1, result.Removed);
        Assert.Equal(HttpStatusCode.NotFound, (await _f.CreateClient().GetAsync(api.OriginalUrl(gone))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _f.CreateClient().GetAsync(api.OriginalUrl(keep))).StatusCode);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_f.MediaRoot, "originals"), "*", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_f.Root, "derived"), gone.ToString("N") + "*", SearchOption.AllDirectories));
        Assert.Equal(HttpStatusCode.NotFound, (await Assert.ThrowsAsync<LoreApiException>(() => api.RestoreAsync(gone))).Status);
    }

    [Fact]
    public async Task Storage_stats_split_by_owner_type_and_bin()
    {
        var api = await _f.PairAsync();
        var photo = Fixtures.Jpeg(7, new DateTime(2024, 5, 1));
        var video = Fixtures.RandomBytes(5000);
        var binned = Fixtures.Jpeg(8, new DateTime(2024, 5, 2));
        await UploadAsync(api, photo, "p.jpg", "image/jpeg", 1);
        await UploadAsync(api, video, "v.mp4", "video/mp4", 2);
        await api.DeleteAsync(await UploadAsync(api, binned, "x.jpg", "image/jpeg", 1));

        var s = await api.GetStorageAsync();
        Assert.Equal(photo.Length, s.Library.Single(b => b is { OwnerId: 1, MediaType: MediaTypes.Photo }).Bytes);
        Assert.Equal(1, s.Library.Single(b => b is { OwnerId: 2, MediaType: MediaTypes.Video }).Count);
        Assert.Equal((1, (long)binned.Length), (s.BinCount, s.BinBytes));
        Assert.True(s.VolumeTotalBytes > 0 && s.VolumeFreeBytes > 0);
    }
}
