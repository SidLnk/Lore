# Lore

Private, LAN-only family photo and video library. The server runs in Docker on the home PC. Media is stored on the host disk (`G:\lore`). The Android app is .NET MAUI.

```
src/Lore.Api        ASP.NET Core API (SQLite + Dapper), admin page at /admin
src/Lore.Contracts  DTOs shared by API and app
src/Lore.Client     API client, resumable UploadEngine, persistent UploadQueue
src/Lore.App        Android app
tests/Lore.Api.Tests  API + upload engine tests against an in-memory server
```

## Run the server

1. Copy `.env.example` to `.env` and set `LORE_ADMIN_PASSWORD`.
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

## Pair a phone

1. Install the app: `dotnet build src/Lore.App -f net10.0-android -t:Install` with the phone connected over USB debugging.
2. On the PC, open `http://lore.home.arpa/admin` (any username, admin password) and tap **New pairing code**.
3. In the app, enter the server (`lore.home.arpa`, or the PC's IP if Private DNS is on), the code, and a phone name. Then pick whose phone it is.

## Develop

- `dotnet test` runs API and upload-engine tests, including a 150-file upload-and-browse milestone test.
- `dotnet run --project src/Lore.Api` uses `appsettings.Development.json`: data in `.dev-data/`, admin password `dev`. An emulator reaches it at `10.0.2.2:<port>`.
- Video metadata and poster frames need `ffmpeg`/`ffprobe` on PATH. Without them, the server skips both. The Docker image includes them.
