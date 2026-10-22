-- Folders are virtual (database only); originals never move on disk.
CREATE TABLE Folder(
    Id       INTEGER PRIMARY KEY,
    ParentId INTEGER REFERENCES Folder(Id),
    Name     TEXT NOT NULL,
    UNIQUE (ParentId, Name)
);
-- UNIQUE treats NULL ParentIds as distinct; enforce unique names at the root separately.
CREATE UNIQUE INDEX UX_Folder_RootName ON Folder(Name) WHERE ParentId IS NULL;

ALTER TABLE MediaAsset ADD COLUMN FolderId INTEGER REFERENCES Folder(Id) ON DELETE SET NULL;
CREATE INDEX IX_MediaAsset_Folder ON MediaAsset(FolderId);

CREATE TABLE Album(
    Id        INTEGER PRIMARY KEY,
    Name      TEXT NOT NULL UNIQUE COLLATE NOCASE,
    CreatedAt TEXT NOT NULL
);

CREATE TABLE AlbumMedia(
    AlbumId INTEGER NOT NULL REFERENCES Album(Id) ON DELETE CASCADE,
    MediaId TEXT NOT NULL REFERENCES MediaAsset(Id) ON DELETE CASCADE,
    PRIMARY KEY (AlbumId, MediaId)
);
CREATE INDEX IX_AlbumMedia_Media ON AlbumMedia(MediaId);

CREATE TABLE Tag(
    Id   INTEGER PRIMARY KEY,
    Name TEXT NOT NULL UNIQUE COLLATE NOCASE
);

CREATE TABLE MediaTag(
    MediaId TEXT NOT NULL REFERENCES MediaAsset(Id) ON DELETE CASCADE,
    TagId   INTEGER NOT NULL REFERENCES Tag(Id) ON DELETE CASCADE,
    PRIMARY KEY (MediaId, TagId)
);
CREATE INDEX IX_MediaTag_Tag ON MediaTag(TagId);
