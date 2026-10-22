# Product

<!-- impeccable:product-schema 1 -->

## Platform

android

## Users

- **Alex and Sam** (parents) run Lore day to day. They pair their phones, upload photos and videos, and organize and browse the family library. Alex also runs the home PC server and its admin page (pairing codes, devices, server status).
- **Robin** is a child. Robin mostly appears in the library as an owner of media and may browse it later on a shared or personal device.
- Ownership ("whose photo this is") is separate from the uploader ("whose phone sent it"). A parent often uploads media that belongs to Robin or to the other parent.

## Product Purpose

Lore is a private family photo and video library that lives entirely on the home PC. Phones on the home Wi-Fi upload originals to it. Any paired phone can browse the whole family's media by date, person, type, place, folder, album and tag.

V1 succeeds when Alex or Sam can pair an Android phone, reliably upload 100+ photos and videos over the LAN, and browse them from another phone by date, owner and media type.

## Positioning

Lore is the family's own archive, not a cloud service:

- **Privacy:** nothing leaves the home network. There is no cloud, no tunnel and no internet dependency in V1.
- **Ownership of originals:** full-quality, byte-identical, immutable originals on the family's own disk. A storage abstraction lets them move from the HDD to RAID or NAS later.
- **No subscription:** storage cost is the family's own hardware.
- **Heritage:** a long-lived family record (the "lore"), organized by person, place and time rather than by device.

## Operating Context

- Server: Docker on the Windows home PC with a static LAN IP, reachable as `lore.home.arpa` (raw IP as a fallback). Media lives on `G:\lore`; the live database is on a Docker volume and gets daily snapshots onto `G:`.
- Clients: Android phones (.NET MAUI) on home Wi-Fi only. Uploads run as a background foreground-service and pause when the phone is off home Wi-Fi.
- Pairing: Alex opens `http://lore.home.arpa/admin` on the PC, generates a 6-digit code (10-minute, single use), and enters it on the phone. Each phone has a default family member.
- Typical sessions:
  - after an outing, select many photos and videos and upload them in the background;
  - later, browse the timeline or a person, album or place, often on a different phone than the one that uploaded.

## Capabilities and Constraints

- Upload is chunked, resumable and retried, with SHA-256 dedupe. Originals are read from the phone's MediaStore so GPS EXIF is kept; the system photo picker strips it and remains only as a labelled fallback.
- Metadata comes from EXIF (images) and ffprobe (videos): date taken, GPS, dimensions, duration, camera.
- Browsing: a timeline grouped by month, with lazy loading and filters for date range, owner, type, favorites, device, folder (including subfolders), album, tag, with/without location, and place area. The viewer is fullscreen; video streams the original with seeking.
- Organizing: virtual nested folders (files never move on disk), albums, tags, captions, favorites, bulk edits, and a recycle bin. Emptying the bin is the only way an original is erased.
- Places: GPS media grouped into grid cells shown as a dot plot plus a list, with no map tiles because V1 is offline. Places are labelled by coordinates; there are no place names without online or offline geocoding.
- Excluded from V1: internet access or tunnels, cloud sync, backup automation (beyond the DB snapshot), RAID/NAS management, face recognition, OCR, AI tagging, video transcoding, and near-duplicate detection. Interfaces should allow these later without restructuring.
- Security posture: HTTP on the LAN with per-device bearer tokens; the admin page uses an admin password. Every paired phone has full family rights, including bulk delete and emptying the bin.

## Brand Commitments

- Name: **Lore**.
- Voice: warm, plain and family-friendly. Short sentences, no jargon. Destructive actions say exactly what happens and whether it can be undone.
- The family members' names appear in the UI as owners. Alex, Sam and Robin are placeholders: the real names live only in the server's `.env` (`LORE_FAMILY`), never in the repo.

## Evidence on Hand

- A working V1 implementation in this repo: `src/Lore.Api` (server plus admin page `admin.html`), `src/Lore.App` (Android app), and tests in `tests/Lore.Api.Tests`.
- No real family photos are in the repo. Screenshots and tests use generated test images. Do not fabricate family content, testimonials or usage numbers.

## Product Principles

1. **Originals are sacred.** Never alter, recompress or silently drop an original or its metadata. Deletion is two-step and explicit.
2. **Home stays home.** No feature may require the internet or a cloud account. Hand-offs to online tools (e.g., opening the phone's maps app) are opt-in and happen on the phone, not in Lore.
3. **Reliable before clever.** Uploads must survive Wi-Fi drops, app restarts and screen-off; the family should never wonder whether a photo made it.
4. **People, places and time.** Organize around whose memory it is, where it happened and when, not around devices or files.
5. **Built for the household.** Two busy parents managing a family archive, with a child in it. Keep flows short, obvious and forgiving.
