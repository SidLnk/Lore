<picture>
  <source media="(prefers-color-scheme: dark)" srcset="design/logo/final/horizontal/lore-horizontal-white.svg">
  <img alt="Lore" src="design/logo/final/lore-horizontal.svg" height="64">
</picture>

A private family photo and video library that lives on your own PC. Phones on the home Wi-Fi upload full-quality originals to it, and any paired phone can browse the whole family's media. No cloud, no account, no subscription: nothing leaves the home network.

- **Originals kept exactly.** Byte-identical files, content-addressed on your own disk, with duplicates detected by SHA-256.
- **Reliable uploads.** Chunked and resumable; they continue in the background, pause off home Wi-Fi and pick up after drops or restarts.
- **Browse by person, place and time.** A month-grouped timeline with filters for owner, type, dates, favorites, folders, albums, tags and location, plus a Places view built from photo GPS (no map tiles, no internet).
- **Organize without moving files.** Virtual folders, albums, tags, captions, favorites, bulk edits and a recycle bin.
- **Phones and tablets.** Android app with a navigation rail on tablets and landscape, dark mode and large-text support. Photos, videos and HEIC.

## Download

Get the Android app (Android 8.0+) from [Releases](https://github.com/SidLnk/Lore/releases/latest): open the `lore-*.apk` file on the phone and allow **Install unknown apps**. The app needs a Lore server on your network; set one up below, then [pair the phone](#pair-a-phone).

## How it fits together

The server is ASP.NET Core in Docker on a Windows PC, with media on a host disk (`G:\lore`) and SQLite for the catalogue. The Android app is .NET MAUI.

```
src/Lore.Api        ASP.NET Core API (SQLite + Dapper), admin page at /admin
src/Lore.Contracts  DTOs shared by API and app
src/Lore.Client     API client, resumable UploadEngine, persistent UploadQueue
src/Lore.App        Android app
tests/Lore.Api.Tests  API + upload engine tests against an in-memory server
```

## Run the server

1. Copy `.env.example` to `.env`. Set `LORE_ADMIN_PASSWORD`, and `LORE_FAMILY` to your family members' names, comma separated (they seed the first start only, and never enter the repo).
2. Run `docker compose up -d --build`.
3. In an elevated PowerShell, mark the home network as Private and let LAN devices reach port 80:
   ```powershell
   Set-NetConnectionProfile -InterfaceAlias Ethernet -NetworkCategory Private
   New-NetFirewallRule -DisplayName "Lore (HTTP, LAN only)" -Direction Inbound -Protocol TCP -LocalPort 80 -RemoteAddress LocalSubnet -Action Allow -Profile Private
   ```
4. On the router, reserve the PC's IP (DHCP reservation for its MAC address) so it never changes, and add a DNS record `lore.home.arpa` pointing to it.
5. Keep the server reachable: in Docker Desktop turn on **Start Docker Desktop when you sign in** (the container restarts with it), and set the PC to never sleep while plugged in.
6. Check from a phone on home Wi-Fi: `http://lore.home.arpa/api/health` (or `http://<PC IP>/api/health`).

Storage layout under `G:\lore`:

| Path | Contents |
|---|---|
| `media/originals/ab/cd/<sha256>.<ext>` | Originals. Content-addressed and never overwritten. |
| `derived/` | Thumbnails and previews. Safe to delete; they regenerate on demand. |
| `uploads/` | In-progress chunked uploads. |
| `db-snapshots/` | Daily `VACUUM INTO` copies of the database. The newest 7 are kept. |

The live SQLite DB is on the `lore-db` Docker volume, not on `G:`. SQLite locking is unreliable over Windows bind mounts. To restore, copy a snapshot into the volume.

## Build the release APK

Release builds are signed with a key kept outside the repo, in `%USERPROFILE%\.lore\`:
`lore-release.keystore` (alias `lore`) and `keystore-password.txt` (the password on two lines: store, then key).
Back both up somewhere safe. Every update must be signed with this key, or Android refuses to install it over the old version.

```
dotnet publish src/Lore.App -f net10.0-android -c Release
```

The APK is `src/Lore.App/bin/Release/net10.0-android/publish/arpa.home.lore-Signed.apk`. Copy it to the phone (USB file transfer, Drive, chat), open it and allow **Install unknown apps** for the app you opened it from.

## Pair a phone

1. Install the app: the release APK above, or `dotnet build src/Lore.App -f net10.0-android -t:Install` with the phone connected over USB debugging.
2. On the PC, open `http://lore.home.arpa/admin` (any username, admin password) and tap **New pairing code**.
3. In the app, enter the server (`lore.home.arpa`, or the PC's IP if Private DNS is on), the code, and a phone name. Then pick whose phone it is.

## Develop

- `dotnet test` runs API and upload-engine tests, including a 150-file upload-and-browse milestone test.
- `dotnet run --project src/Lore.Api` uses `appsettings.Development.json`: data in `.dev-data/`, admin password `dev`. An emulator reaches it at `10.0.2.2:<port>`.
- Video metadata and poster frames need `ffmpeg`/`ffprobe` on PATH. Without them, the server skips both. The Docker image includes them.
