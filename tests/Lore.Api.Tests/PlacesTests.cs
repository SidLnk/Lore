using System.Net;
using Lore.Client;
using Lore.Contracts;

namespace Lore.Api.Tests;

public sealed class PlacesTests : IDisposable
{
    readonly LoreFactory _f = new();
    public void Dispose() => _f.Dispose();

    async Task<Guid> UploadAsync(LoreApiClient api, int seed, DateTime taken, (double, double)? gps)
    {
        var data = Fixtures.Jpeg(seed, taken, gps);
        var up = await api.CreateUploadAsync(new(Fixtures.Sha256(data), data.Length, $"{seed}.jpg", "image/jpeg", 1, null));
        await api.PutChunkAsync(up.UploadId!.Value, 0, data);
        return (await api.CompleteUploadAsync(up.UploadId!.Value)).MediaId;
    }

    [Fact]
    public async Task Places_group_by_cell_and_area_filter_returns_exactly_that_cell()
    {
        var api = await _f.PairAsync();
        // Two shots in Singapore ~200 m apart, one in Sydney (southern/western hemisphere checks floor on negatives), one without GPS.
        var sg1 = await UploadAsync(api, 1, new DateTime(2024, 1, 1), (1.2903, 103.8519));
        var sg2 = await UploadAsync(api, 2, new DateTime(2024, 3, 1), (1.2921, 103.8530));
        var syd = await UploadAsync(api, 3, new DateTime(2024, 2, 1), (-33.8568, 151.2153));
        await UploadAsync(api, 4, new DateTime(2024, 4, 1), null);

        var places = await api.GetPlacesAsync(0.1);
        Assert.Equal(2, places.Count);
        var sg = places[0]; // most items first
        Assert.Equal(2, sg.Count);
        Assert.Equal(sg2, sg.SampleMediaId); // newest
        Assert.Equal((new DateTime(2024, 1, 1), new DateTime(2024, 3, 1)), (sg.FirstTaken, sg.LastTaken));
        Assert.Equal(1.29, sg.Lat, 2);
        Assert.InRange(1.2903, sg.MinLat, sg.MaxLat);

        var sydney = places[1];
        Assert.True(sydney.MinLat < -33.8568 && -33.8568 < sydney.MaxLat, "negative latitude must floor down");

        var inSg = (await api.GetMediaAsync(new MediaQuery(Area: GeoArea.Of(sg)))).Items.Select(m => m.Id).ToHashSet();
        Assert.Equal([sg1, sg2], inSg.Order());
        Assert.Equal([syd], (await api.GetMediaAsync(new MediaQuery(Area: GeoArea.Of(sydney)))).Items.Select(m => m.Id));

        // Finer cells split the two Singapore shots apart.
        Assert.Equal(3, (await api.GetPlacesAsync(0.001)).Count);
    }

    [Fact]
    public async Task Binned_media_leave_places_and_bad_inputs_are_rejected()
    {
        var api = await _f.PairAsync();
        var id = await UploadAsync(api, 5, new DateTime(2024, 1, 1), (48.8584, 2.2945));
        await api.DeleteAsync(id);
        Assert.Empty(await api.GetPlacesAsync(0.1));

        Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAsync<LoreApiException>(() => api.GetPlacesAsync(0))).Status);
        var reversed = new GeoArea(10, 5, 0, 1);
        Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAsync<LoreApiException>(() => api.GetMediaAsync(new MediaQuery(Area: reversed)))).Status);
    }
}
