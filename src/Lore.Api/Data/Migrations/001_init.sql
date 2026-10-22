CREATE TABLE FamilyMember(
    Id   INTEGER PRIMARY KEY,
    Name TEXT NOT NULL UNIQUE
);
-- Members are seeded from the Lore:Family setting on first start (Db.Migrate), so names stay out of the code.

CREATE TABLE Device(
    Id             TEXT PRIMARY KEY,
    Name           TEXT NOT NULL,
    DefaultOwnerId INTEGER REFERENCES FamilyMember(Id),
    TokenHash      TEXT NOT NULL UNIQUE,
    PairedAt       TEXT NOT NULL,
    LastSeenAt     TEXT,
    RevokedAt      TEXT
);

CREATE TABLE PairingCode(
    Code      TEXT PRIMARY KEY,
    ExpiresAt TEXT NOT NULL,
    UsedAt    TEXT
);

CREATE TABLE StorageVolume(
    Id   TEXT PRIMARY KEY,
    Name TEXT NOT NULL
);

CREATE TABLE MediaAsset(
    Id               TEXT PRIMARY KEY,
    StorageVolumeId  TEXT NOT NULL REFERENCES StorageVolume(Id),
    ObjectKey        TEXT NOT NULL,
    OriginalFilename TEXT NOT NULL,
    MediaType        TEXT NOT NULL CHECK (MediaType IN ('photo', 'video')),
    MimeType         TEXT NOT NULL,
    FileSize         INTEGER NOT NULL,
    SHA256           TEXT NOT NULL UNIQUE,
    OwnerId          INTEGER NOT NULL REFERENCES FamilyMember(Id),
    UploadedById     INTEGER NOT NULL REFERENCES FamilyMember(Id),
    DeviceId         TEXT NOT NULL REFERENCES Device(Id),
    DateTaken        TEXT NOT NULL,
    DateUploaded     TEXT NOT NULL,
    Latitude         REAL,
    Longitude        REAL,
    Altitude         REAL,
    LocationSource   TEXT CHECK (LocationSource IN ('exif', 'manual')),
    Width            INTEGER,
    Height           INTEGER,
    Duration         REAL,
    CameraMake       TEXT,
    CameraModel      TEXT,
    Caption          TEXT,
    Favorite         INTEGER NOT NULL DEFAULT 0,
    DeletedAt        TEXT,
    UNIQUE (StorageVolumeId, ObjectKey)
);
CREATE INDEX IX_MediaAsset_Timeline ON MediaAsset(DateTaken DESC, Id DESC);
CREATE INDEX IX_MediaAsset_Owner ON MediaAsset(OwnerId, DateTaken DESC);
CREATE INDEX IX_MediaAsset_Type ON MediaAsset(MediaType, DateTaken DESC);
CREATE INDEX IX_MediaAsset_Device ON MediaAsset(DeviceId);

CREATE TABLE Upload(
    Id               TEXT PRIMARY KEY,
    DeviceId         TEXT NOT NULL REFERENCES Device(Id),
    Sha256           TEXT NOT NULL,
    Size             INTEGER NOT NULL,
    OriginalFilename TEXT NOT NULL,
    MimeType         TEXT NOT NULL,
    OwnerId          INTEGER NOT NULL REFERENCES FamilyMember(Id),
    ClientDateTaken  TEXT,
    CreatedAt        TEXT NOT NULL,
    UNIQUE (DeviceId, Sha256)
);
