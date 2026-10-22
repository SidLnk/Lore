using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Lore.Contracts;

namespace Lore.Client;

public sealed class LoreApiException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>Typed wrapper over the Lore REST API. One instance per server + device token.</summary>
public sealed class LoreApiClient(HttpClient http)
{
    public Uri BaseAddress => http.BaseAddress!;
    string? _token;

    public void SetToken(string? token)
    {
        _token = token;
        http.DefaultRequestHeaders.Authorization = token == null ? null : new AuthenticationHeaderValue("Bearer", token);
    }

    public Task<PairResponse> PairAsync(string code, string deviceName, CancellationToken ct = default) =>
        SendAsync<PairResponse>(HttpMethod.Post, "api/pair", JsonContent.Create(new PairRequest(code, deviceName)), ct);

    public Task<List<FamilyMemberDto>> GetFamilyAsync(CancellationToken ct = default) =>
        SendAsync<List<FamilyMemberDto>>(HttpMethod.Get, "api/family", null, ct);

    public Task<DeviceDto> GetDeviceAsync(CancellationToken ct = default) =>
        SendAsync<DeviceDto>(HttpMethod.Get, "api/device", null, ct);

    public Task SetDefaultOwnerAsync(int ownerId, CancellationToken ct = default) =>
        SendAsync<object>(HttpMethod.Put, "api/device", JsonContent.Create(new UpdateDeviceRequest(ownerId)), ct);

    public Task<CreateUploadResponse> CreateUploadAsync(CreateUploadRequest r, CancellationToken ct = default) =>
        SendAsync<CreateUploadResponse>(HttpMethod.Post, "api/uploads", JsonContent.Create(r), ct);

    public Task<UploadStatusDto> GetUploadAsync(Guid id, CancellationToken ct = default) =>
        SendAsync<UploadStatusDto>(HttpMethod.Get, $"api/uploads/{id}", null, ct);

    /// <summary>Returns the server's offset after the call. A 409 is not an error: it returns the offset the server actually has.</summary>
    public async Task<long> PutChunkAsync(Guid id, long offset, ReadOnlyMemory<byte> chunk, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, $"api/uploads/{id}?offset={offset}") { Content = new ReadOnlyMemoryContent(chunk) };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var res = await http.SendAsync(req, ct);
        if (res.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict)
            return (await res.Content.ReadFromJsonAsync<UploadStatusDto>(ct))!.ReceivedBytes;
        throw await ErrorAsync(res, ct);
    }

    public Task<CompleteUploadResponse> CompleteUploadAsync(Guid id, CancellationToken ct = default) =>
        SendAsync<CompleteUploadResponse>(HttpMethod.Post, $"api/uploads/{id}/complete", null, ct);

    public Task<MediaPage> GetMediaAsync(MediaQuery q, string? cursor = null, int limit = 60, CancellationToken ct = default)
    {
        var qs = new List<string> { $"limit={limit}" };
        if (q.From != null) qs.Add($"from={Uri.EscapeDataString(q.From.Value.ToString("yyyy-MM-ddTHH:mm:ss"))}");
        if (q.To != null) qs.Add($"to={Uri.EscapeDataString(q.To.Value.ToString("yyyy-MM-ddTHH:mm:ss"))}");
        if (q.OwnerId != null) qs.Add($"ownerId={q.OwnerId}");
        if (q.Type != null) qs.Add($"type={q.Type}");
        if (q.Favorite) qs.Add("favorite=true");
        if (q.DeviceId != null) qs.Add($"deviceId={q.DeviceId}");
        if (q.FolderId != null) qs.Add($"folderId={q.FolderId}");
        if (q.AlbumId != null) qs.Add($"albumId={q.AlbumId}");
        if (q.Tag != null) qs.Add($"tag={Uri.EscapeDataString(q.Tag)}");
        if (q.Area is { } a)
            qs.Add(FormattableString.Invariant($"minLat={a.MinLat}&maxLat={a.MaxLat}&minLon={a.MinLon}&maxLon={a.MaxLon}"));
        if (q.HasGps != null) qs.Add($"hasGps={(q.HasGps.Value ? "true" : "false")}");
        if (q.Deleted) qs.Add("deleted=true");
        if (cursor != null) qs.Add($"cursor={Uri.EscapeDataString(cursor)}");
        return SendAsync<MediaPage>(HttpMethod.Get, "api/media?" + string.Join('&', qs), null, ct);
    }

    public Task<ExistsResponse> ExistsAsync(IReadOnlyList<string> sha256s, CancellationToken ct = default) =>
        SendAsync<ExistsResponse>(HttpMethod.Post, "api/media/exists", JsonContent.Create(new ExistsRequest(sha256s)), ct);

    public Task SetFavoriteAsync(Guid id, bool favorite, CancellationToken ct = default) =>
        SendAsync<object>(HttpMethod.Put, $"api/media/{id}/favorite", JsonContent.Create(new SetFavoriteRequest(favorite)), ct);

    /// <summary>Moves to the recycle bin. Nothing is erased until <see cref="EmptyBinAsync"/>.</summary>
    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        SendAsync<object>(HttpMethod.Delete, $"api/media/{id}", null, ct);

    public Task RestoreAsync(Guid id, CancellationToken ct = default) =>
        SendAsync<object>(HttpMethod.Post, $"api/media/{id}/restore", null, ct);

    public Task<EmptyBinResponse> EmptyBinAsync(CancellationToken ct = default) =>
        SendAsync<EmptyBinResponse>(HttpMethod.Post, "api/bin/empty", null, ct);

    public Task<StorageStatsDto> GetStorageAsync(CancellationToken ct = default) =>
        SendAsync<StorageStatsDto>(HttpMethod.Get, "api/stats/storage", null, ct);

    public Task<List<DeviceDto>> GetDevicesAsync(CancellationToken ct = default) =>
        SendAsync<List<DeviceDto>>(HttpMethod.Get, "api/devices", null, ct);

    public Task<List<FolderDto>> GetFoldersAsync(CancellationToken ct = default) =>
        SendAsync<List<FolderDto>>(HttpMethod.Get, "api/folders", null, ct);

    public Task<FolderDto> CreateFolderAsync(string name, int? parentId, CancellationToken ct = default) =>
        SendAsync<FolderDto>(HttpMethod.Post, "api/folders", JsonContent.Create(new SaveFolderRequest(name, parentId)), ct);

    public Task UpdateFolderAsync(int id, string name, int? parentId, CancellationToken ct = default) =>
        SendAsync<object>(HttpMethod.Put, $"api/folders/{id}", JsonContent.Create(new SaveFolderRequest(name, parentId)), ct);

    public Task DeleteFolderAsync(int id, CancellationToken ct = default) =>
        SendAsync<object>(HttpMethod.Delete, $"api/folders/{id}", null, ct);

    public Task<List<AlbumDto>> GetAlbumsAsync(CancellationToken ct = default) =>
        SendAsync<List<AlbumDto>>(HttpMethod.Get, "api/albums", null, ct);

    public Task<AlbumDto> CreateAlbumAsync(string name, CancellationToken ct = default) =>
        SendAsync<AlbumDto>(HttpMethod.Post, "api/albums", JsonContent.Create(new SaveAlbumRequest(name)), ct);

    public Task RenameAlbumAsync(int id, string name, CancellationToken ct = default) =>
        SendAsync<object>(HttpMethod.Put, $"api/albums/{id}", JsonContent.Create(new SaveAlbumRequest(name)), ct);

    public Task DeleteAlbumAsync(int id, CancellationToken ct = default) =>
        SendAsync<object>(HttpMethod.Delete, $"api/albums/{id}", null, ct);

    public Task<List<TagDto>> GetTagsAsync(CancellationToken ct = default) =>
        SendAsync<List<TagDto>>(HttpMethod.Get, "api/tags", null, ct);

    public Task SetCaptionAsync(Guid id, string? caption, CancellationToken ct = default) =>
        SendAsync<object>(HttpMethod.Put, $"api/media/{id}/caption", JsonContent.Create(new SetCaptionRequest(caption)), ct);

    public Task<MediaDetailsDto> GetDetailsAsync(Guid id, CancellationToken ct = default) =>
        SendAsync<MediaDetailsDto>(HttpMethod.Get, $"api/media/{id}/details", null, ct);

    public Task<BulkEditResponse> BulkEditAsync(BulkEditRequest r, CancellationToken ct = default) =>
        SendAsync<BulkEditResponse>(HttpMethod.Post, "api/media/bulk", JsonContent.Create(r), ct);

    /// <param name="cellDegrees">0.01 is about 1 km cells, 0.1 about 11 km.</param>
    public Task<List<PlaceDto>> GetPlacesAsync(double cellDegrees, CancellationToken ct = default) =>
        SendAsync<List<PlaceDto>>(HttpMethod.Get, FormattableString.Invariant($"api/places?cell={cellDegrees}"), null, ct);

    /// <summary>The original's bytes as a stream (not buffered: videos can be large). Dispose it to free the connection.</summary>
    public async Task<Stream> OpenOriginalAsync(Guid id, CancellationToken ct = default)
    {
        var res = await http.GetAsync($"api/media/{id}/original", HttpCompletionOption.ResponseHeadersRead, ct);
        if (res.IsSuccessStatusCode) return await res.Content.ReadAsStreamAsync(ct);
        using (res) throw await ErrorAsync(res, ct);
    }

    /// <summary>The app version the home server offers, or null when it has none.</summary>
    public async Task<AppVersionDto?> GetAppVersionAsync(CancellationToken ct = default)
    {
        using var res = await http.GetAsync("api/app", ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        if (!res.IsSuccessStatusCode) throw await ErrorAsync(res, ct);
        return await res.Content.ReadFromJsonAsync<AppVersionDto>(ct);
    }

    /// <summary>The offered APK as a stream (not buffered). Dispose it to free the connection.</summary>
    public async Task<Stream> OpenAppApkAsync(CancellationToken ct = default)
    {
        var res = await http.GetAsync("api/app/apk", HttpCompletionOption.ResponseHeadersRead, ct);
        if (res.IsSuccessStatusCode) return await res.Content.ReadAsStreamAsync(ct);
        using (res) throw await ErrorAsync(res, ct);
    }

    // Image and video controls can't send headers, so media URLs carry the token.
    public Uri ThumbUrl(Guid id) => MediaUrl(id, "thumb");
    public Uri PreviewUrl(Guid id) => MediaUrl(id, "preview");
    public Uri OriginalUrl(Guid id) => MediaUrl(id, "original");
    Uri MediaUrl(Guid id, string kind) => new(http.BaseAddress!, $"api/media/{id}/{kind}?access_token={Uri.EscapeDataString(_token ?? "")}");

    async Task<T> SendAsync<T>(HttpMethod method, string path, HttpContent? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path) { Content = body };
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) throw await ErrorAsync(res, ct);
        return res.StatusCode == HttpStatusCode.NoContent ? default! : (await res.Content.ReadFromJsonAsync<T>(ct))!;
    }

    static async Task<LoreApiException> ErrorAsync(HttpResponseMessage res, CancellationToken ct) =>
        new(res.StatusCode, $"{(int)res.StatusCode} {res.ReasonPhrase}: {await res.Content.ReadAsStringAsync(ct)}");
}

/// <summary>Deleted=true browses the recycle bin.</summary>
public sealed record MediaQuery(
    DateTime? From = null, DateTime? To = null, int? OwnerId = null, string? Type = null, bool Favorite = false, bool Deleted = false,
    Guid? DeviceId = null, int? FolderId = null, int? AlbumId = null, string? Tag = null, bool? HasGps = null, GeoArea? Area = null);

/// <summary>Half-open box: min inclusive, max exclusive.</summary>
public sealed record GeoArea(double MinLat, double MaxLat, double MinLon, double MaxLon)
{
    public static GeoArea Of(PlaceDto p) => new(p.MinLat, p.MaxLat, p.MinLon, p.MaxLon);
}
