using Lore.Contracts;

namespace Lore.Api.Data;

// Dapper maps settable properties with type conversion; positional records need exact column types, which SQLite can't give.

public sealed class FamilyMemberRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public FamilyMemberDto ToDto() => new(Id, Name);
}

public sealed class DeviceRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int? DefaultOwnerId { get; set; }
    public DateTimeOffset PairedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DeviceDto ToDto() => new(Id, Name, DefaultOwnerId, PairedAt, LastSeenAt, RevokedAt);
}

public sealed class UploadRow
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public string OriginalFilename { get; set; } = "";
    public string MimeType { get; set; } = "";
    public int OwnerId { get; set; }
    public DateTime? ClientDateTaken { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
