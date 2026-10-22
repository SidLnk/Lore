using Lore.Contracts;

namespace Lore.Api.Data;

/// <summary>One MediaAsset table row. Mutable so ingest steps can fill fields in turn.</summary>
public sealed class MediaAssetRow
{
    public Guid Id { get; set; }
    public string StorageVolumeId { get; set; } = "";
    public string ObjectKey { get; set; } = "";
    public string OriginalFilename { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string MimeType { get; set; } = "";
    public long FileSize { get; set; }
    public string SHA256 { get; set; } = "";
    public int OwnerId { get; set; }
    public int UploadedById { get; set; }
    public Guid DeviceId { get; set; }
    public DateTime DateTaken { get; set; }
    public DateTimeOffset DateUploaded { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Altitude { get; set; }
    public string? LocationSource { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? Duration { get; set; }
    public string? CameraMake { get; set; }
    public string? CameraModel { get; set; }
    public string? Caption { get; set; }
    public bool Favorite { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int? FolderId { get; set; }

    public const string Columns =
        "Id, StorageVolumeId, ObjectKey, OriginalFilename, MediaType, MimeType, FileSize, SHA256, OwnerId, UploadedById, DeviceId, " +
        "DateTaken, DateUploaded, Latitude, Longitude, Altitude, LocationSource, Width, Height, Duration, CameraMake, CameraModel, Caption, Favorite, DeletedAt, FolderId";

    public const string InsertSql =
        $"INSERT INTO MediaAsset ({Columns}) VALUES (@Id, @StorageVolumeId, @ObjectKey, @OriginalFilename, @MediaType, @MimeType, @FileSize, @SHA256, " +
        "@OwnerId, @UploadedById, @DeviceId, @DateTaken, @DateUploaded, @Latitude, @Longitude, @Altitude, @LocationSource, @Width, @Height, @Duration, " +
        "@CameraMake, @CameraModel, @Caption, @Favorite, @DeletedAt, @FolderId)";

    public MediaDto ToDto() => new(Id, OriginalFilename, MediaType, MimeType, FileSize, SHA256, OwnerId, UploadedById, DeviceId,
        DateTaken, DateUploaded, Latitude, Longitude, Altitude, Width, Height, Duration, CameraMake, CameraModel, Favorite, Caption, DeletedAt, FolderId);
}
