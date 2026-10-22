namespace Lore.Contracts;

public static class MediaTypes
{
    public const string Photo = "photo";
    public const string Video = "video";
}

public record PairRequest(string Code, string DeviceName);
public record PairResponse(Guid DeviceId, string Token);

public record FamilyMemberDto(int Id, string Name);
public record DeviceDto(Guid Id, string Name, int? DefaultOwnerId, DateTimeOffset PairedAt, DateTimeOffset? LastSeenAt, DateTimeOffset? RevokedAt);
public record UpdateDeviceRequest(int DefaultOwnerId);
public record PairingCodeDto(string Code, DateTimeOffset ExpiresAt);

public record CreateUploadRequest(string Sha256, long Size, string Filename, string MimeType, int OwnerId, DateTime? ClientDateTaken);
/// <summary>Duplicate=true means the server already has this content; MediaId is set and no upload is needed.</summary>
public record CreateUploadResponse(bool Duplicate, Guid? MediaId, Guid? UploadId, long ReceivedBytes, int ChunkSize);
public record UploadStatusDto(Guid UploadId, long ReceivedBytes, long Size);
public record CompleteUploadResponse(Guid MediaId, bool Duplicate);

public record MediaDto(
    Guid Id,
    string OriginalFilename,
    string MediaType,
    string MimeType,
    long FileSize,
    string Sha256,
    int OwnerId,
    int UploadedById,
    Guid DeviceId,
    DateTime DateTaken,
    DateTimeOffset DateUploaded,
    double? Latitude,
    double? Longitude,
    double? Altitude,
    int? Width,
    int? Height,
    double? Duration,
    string? CameraMake,
    string? CameraModel,
    bool Favorite,
    string? Caption,
    DateTimeOffset? DeletedAt,
    int? FolderId);

public record MediaPage(IReadOnlyList<MediaDto> Items, string? NextCursor);

public record SetFavoriteRequest(bool Favorite);

/// <summary>Which of these SHA-256s Lore holds outside the recycle bin (lets a phone free space safely).</summary>
public record ExistsRequest(IReadOnlyList<string> Sha256s);
public record ExistsResponse(IReadOnlyList<string> Present);
public record EmptyBinResponse(int Removed, long Bytes);

/// <summary>Library totals exclude the recycle bin; Bin* fields cover it.</summary>
public record StorageStatsDto(long VolumeTotalBytes, long VolumeFreeBytes, IReadOnlyList<StorageBreakdownDto> Library, int BinCount, long BinBytes);
public record StorageBreakdownDto(int OwnerId, string MediaType, int Count, long Bytes);

public record FolderDto(int Id, int? ParentId, string Name, int Count);
public record SaveFolderRequest(string Name, int? ParentId);
public record AlbumDto(int Id, string Name, int Count);
public record SaveAlbumRequest(string Name);
public record TagDto(int Id, string Name, int Count);
public record SetCaptionRequest(string? Caption);
public record MediaDetailsDto(MediaDto Media, IReadOnlyList<string> Tags, IReadOnlyList<int> AlbumIds, string? DeviceName);

/// <summary>
/// Applies every non-null change to all Ids in one transaction.
/// SetFolder distinguishes "move to root" (FolderId null) from "leave folder alone".
/// </summary>
public record BulkEditRequest(
    IReadOnlyList<Guid> Ids,
    int? OwnerId = null,
    bool SetFolder = false,
    int? FolderId = null,
    IReadOnlyList<string>? AddTags = null,
    IReadOnlyList<string>? RemoveTags = null,
    IReadOnlyList<int>? AddAlbumIds = null,
    IReadOnlyList<int>? RemoveAlbumIds = null,
    bool? Favorite = null,
    bool Delete = false);
public record BulkEditResponse(int Updated);

/// <summary>
/// GPS media grouped into a grid cell of CellSize degrees. Min/Max bound the cell (use them as the library's area filter);
/// Lat/Lon is the average position of the media inside it.
/// </summary>
public record PlaceDto(double Lat, double Lon, double MinLat, double MaxLat, double MinLon, double MaxLon,
    int Count, Guid SampleMediaId, DateTime FirstTaken, DateTime LastTaken);
