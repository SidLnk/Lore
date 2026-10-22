---
name: Lore
description: A gentle, personal home archive for one family's photos and videos.
colors:
  archive-green: "#2B4C3F"
  archive-green-deep: "#1B3329"
  sage-mist: "#8FC1A9"
  sage-text: "#6FA88C"
  pale-celadon: "#DCEBE3"
  linen-mint: "#EEF3F0"
  night-moss: "#1E2A25"
  paper-white: "#FFFFFF"
  off-black: "#1F1F1F"
  ink: "#000000"
  ink-soft: "#212121"
  ink-on-sage: "#242424"
  danger: "#B3261E"
  danger-dark: "#FFB4AB"
  tile-placeholder: "#E6E6E6"
  tile-placeholder-dark: "#2A2A2A"
  gray-200: "#C8C8C8"
  gray-300: "#ACACAC"
  gray-500: "#6E6E6E"
  gray-600: "#404040"
  gray-950: "#141414"
typography:
  display:
    fontFamily: "OpenSans-Semibold, Open Sans, sans-serif"
    fontSize: "28px"
    fontWeight: 600
  headline:
    fontFamily: "OpenSans-Semibold, Open Sans, sans-serif"
    fontSize: "20px"
    fontWeight: 600
  title:
    fontFamily: "OpenSans-Semibold, Open Sans, sans-serif"
    fontSize: "18px"
    fontWeight: 600
  body-large:
    fontFamily: "OpenSans-Regular, Open Sans, sans-serif"
    fontSize: "16px"
    fontWeight: 400
  body:
    fontFamily: "OpenSans-Regular, Open Sans, sans-serif"
    fontSize: "14px"
    fontWeight: 400
  label:
    fontFamily: "OpenSans-Regular, Open Sans, sans-serif"
    fontSize: "12px"
    fontWeight: 400
rounded:
  none: "0px"
  md: "8px"
  lg: "12px"
spacing:
  hairline: "2px"
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "24px"
  touch: "48px"
components:
  button-primary:
    backgroundColor: "{colors.archive-green}"
    textColor: "{colors.paper-white}"
    typography: "{typography.body}"
    rounded: "{rounded.md}"
    padding: "10px 14px"
    height: "48px"
  button-primary-dark:
    backgroundColor: "{colors.sage-mist}"
    textColor: "{colors.ink-on-sage}"
    rounded: "{rounded.md}"
  button-primary-disabled:
    backgroundColor: "{colors.gray-200}"
    textColor: "{colors.gray-950}"
    rounded: "{rounded.md}"
  button-secondary:
    backgroundColor: "transparent"
    textColor: "{colors.archive-green}"
    rounded: "{rounded.md}"
    padding: "10px 14px"
    height: "48px"
  button-text:
    backgroundColor: "transparent"
    textColor: "{colors.archive-green}"
    padding: "0px 12px"
    height: "48px"
  button-danger-text:
    backgroundColor: "transparent"
    textColor: "{colors.danger}"
    padding: "0px 12px"
    height: "48px"
  filter-chip:
    backgroundColor: "transparent"
    textColor: "{colors.ink-soft}"
    rounded: "{rounded.md}"
    padding: "0px 12px"
    height: "36px"
  filter-chip-active:
    backgroundColor: "{colors.pale-celadon}"
    textColor: "{colors.archive-green}"
    rounded: "{rounded.md}"
    padding: "0px 12px"
    height: "36px"
  photo-tile:
    backgroundColor: "{colors.tile-placeholder}"
    rounded: "{rounded.none}"
  selection-check:
    backgroundColor: "{colors.archive-green}"
    textColor: "{colors.paper-white}"
    size: "28px"
  plot-panel:
    backgroundColor: "{colors.linen-mint}"
    rounded: "{rounded.lg}"
    height: "240px"
  tab-bar:
    backgroundColor: "{colors.paper-white}"
    textColor: "{colors.archive-green}"
---

# Design System: Lore

## Overview

**Creative North Star: "The Home Archive"**

Lore is a family's own archive, kept at home: calm, orderly and built to last, but gentle and personal rather than institutional. The interface is the shelving and the labels. The photos are the collection, and they carry nearly all of the colour on screen. Chrome stays white (or off-black at night), type is a single humanist sans, and one deep Archive Green marks what you can act on.

Density follows the task. Browsing screens are edge-to-edge photo grids with hairline 2px gutters, so the family's pictures fill the phone. Management screens (Upload, Organize, Info, Family) are plain vertical lists with comfortable padding and full-width green buttons. The system is flat. Depth comes from tonal panels and the photos themselves; the only allowed lift is a soft ambient shadow on floating sheets and dialogs.

Lore must never look like a cloud-photo app clone (no Google Photos or iCloud chrome and conventions copied for their own sake), never use loud multi-colour UI, and never feel like a corporate dashboard of tables and KPI tiles.

**Key Characteristics:**
- Photos lead; the UI recedes to white space, one sans and one green.
- A single accent, Archive Green, for actions, the selected tab, checks and progress.
- Edge-to-edge square photo tiles with 2px gutters, grouped under month headings.
- Soft, steady controls: 8px corners and 48dp touch targets everywhere (Material minimum).
- One filled action per screen; everything else outlined, text, or a chip.
- Flat surfaces; tonal panels for depth; soft lift only for sheets and dialogs.
- Warm, plain words in every label and message.

## Colors

One deep green on paper, with sage tints for the night theme and for quiet panels; every other colour comes from the family's photos.

### Primary
- **Archive Green** (#2B4C3F): fills primary buttons, the selected tab icon and label, check boxes, progress bars, selection overlays and check badges, and the dots on the Places plot. It is also the Android status bar colour. White text sits on it.
- **Deep Archive Green** (#1B3329): the darker partner for Android's `colorPrimaryDark` and accent. It is reserved for pressed or system-level surfaces, never for body content.

### Secondary
- **Sage Mist** (#8FC1A9): Archive Green's dark-theme counterpart. It fills buttons and tints the selected tab at night, with Ink-on-Sage (#242424) text.
- **Sage Text** (#6FA88C): nav bar title and foreground colour in the dark theme.
- **Pale Celadon** (#DCEBE3): the lightest green tint, available for quiet highlighted surfaces in the light theme.

### Neutral
- **Paper White** (#FFFFFF): page, nav bar and tab bar background in the light theme.
- **Off-Black** (#1F1F1F): page and nav bar background in the dark theme. The dark tab bar is pure black (#000000).
- **Ink** (#000000) / **Ink Soft** (#212121): body text and picker text in the light theme; Ink Soft is the unselected tab colour.
- **Linen Mint** (#EEF3F0) / **Night Moss** (#1E2A25): the tonal panel behind the Places dot plot, light and dark.
- **Tile Placeholder** (#E6E6E6) / **Tile Placeholder Dark** (#2A2A2A): what a photo tile shows before its thumbnail loads, or when no thumbnail exists (missing ffmpeg or libheif, or a format neither can read).
- **Gray ramp** (#C8C8C8, #ACACAC, #6E6E6E, #404040, #141414): disabled controls, placeholders, inactive chip outlines (#ACACAC light, #6E6E6E dark) and the dark-theme unselected tab.

### Danger
- **Danger Red** (#B3261E, #FFB4AB in the dark theme): only for destructive actions and failures, such as "Unpair this phone" and a failed upload's status and reason. Never for emphasis.

### Named Rules
**The Photos Carry Colour Rule.** Chrome uses Archive Green plus neutrals and nothing else. If a screen looks colourful, it should be because of the pictures.

**The One Green Rule.** Archive Green means "you can act here" or "this is selected". Don't use it as decoration, large backgrounds or text colour for body copy.

## Typography

**Body Font:** Open Sans Regular (`OpenSansRegular`, bundled)
**Emphasis Font:** Open Sans Semibold (`OpenSansSemibold`, bundled)

**Character:** One friendly humanist sans in two weights. Semibold carries headings and names; Regular carries everything else. No second family, no italics for emphasis, no all-caps labels.

### Hierarchy
- **Display** (600, 28–32px): the app name on the pairing screen and page-level headings such as "Family" and "Places". Use at most once per screen.
- **Headline** (600, 20px): section headings inside a page, such as Folders, Albums and Tags on Organize.
- **Title** (600, 18px): names and key labels, such as family member names, the "Recent" heading on Home and the filename on Info.
- **Body Large** (400, 16px): list row titles and month group headers (headers in Semibold), where a line must scan quickly.
- **Body** (400, 14px): default for labels, pickers, entries, buttons and messages.
- **Label** (400, 12–13px, often at 0.6–0.7 opacity): secondary detail such as upload status, item counts, date ranges and the server address.

### Named Rules
**The Two Weights Rule.** Hierarchy comes from size and Semibold versus Regular only. Never add a third family or a decorative face.

**The Quiet Detail Rule.** Secondary information is smaller and dimmed (0.6–0.7 opacity), not coloured.

## Layout

Phone portrait is the first canvas; landscape, tablets and split screen restructure from the window width, never the device model. There are two layout modes.

- **Collection grids** run edge to edge. Library and Home keep tiles about 130dp wide (never fewer than 3 columns) and the device-photo chooser about 100dp (never fewer than 4), so a phone shows 3 and 4 columns, a tablet in portrait 5–6 and 8, and landscape more. Tiles are always square, with 2px gutters, under month headings padded 16px top and 6px bottom. Grids load more as you scroll (30 items before the end) and pull to refresh. Home's Recent shows four full rows at any width.
- **Management pages** are a single column with 16px side padding (Pairing keeps 24px). Rhythm comes from contrast: 2–4px inside a group (a name and its detail line), 16px between items, 32px between groups (Family's members, this phone, and unpair). On wide windows the column stops at 640dp (Places 720dp, Pairing 560dp) and stays aligned under the page title, not centred.

Filters are one horizontally scrolling row of chips directly under the nav bar, with 12px side padding and 8px between chips. Photos therefore start about a fifth of the way down the screen. When anything is set, "Clear all" leads the row so it is never scrolled out of reach, followed by the place chip (if one is set), then owner, type, dates, favorites, album, folder, tag, location and device. Picking "Choose dates…" adds one row of from/to date pickers under the chips; nothing else expands.

Text buttons sit with a −12px leading margin, so their label aligns with the text column above them.

Spacing steps are 2, 4, 8, 12, 16, 24 and 32px. Every tappable target is at least 48dp tall (Material minimum); chips draw a 36dp pill inside a 48dp touch area.

## Elevation & Depth

Flat by default. Pages, lists, the nav bar (shadow explicitly off) and the tab bar have no shadows. Depth comes from tonal panels (Linen Mint behind the Places plot), from photos filling the grid, and from translucent overlays. Floating sheets and dialogs are the only surfaces that may lift, with a soft ambient shadow.

### Shadow Vocabulary
- **Sheet lift** (`box-shadow: 0 8px 24px rgba(0, 0, 0, 0.16)`): provisional. Use it only for floating sheets and dialogs that sit above content. No current custom surface uses it; system action sheets and alerts keep Android's own elevation.

### Named Rules
**The Flat Page Rule.** Anything attached to the page is flat. Only things that float above the page may cast a shadow, and only softly.

## Shapes

- Gently rounded controls (8px) for all buttons.
- Softer panels (12px) for tonal containers like the Places plot.
- Photos are square-cornered (0px) and tile edge to edge; rounding thumbnails would break the album grid.
- Small square badges sit in tile corners with a 4–6px inset: the selection check (24–28px) and the video duration label.

## Components

### Buttons
Soft and steady: solid, calm and easy to hit. Each screen gets **one** filled button for its main job; everything else steps down.
- **Shape:** gently rounded (8px).
- **Primary (filled):** Archive Green fill, white Open Sans 14px, 10px × 14px padding, at least 48 × 48dp. In the dark theme, Sage Mist fill with #242424 text. Examples: "Choose photos & videos", "Pair", "Save caption".
- **Secondary (outlined, SecondaryButton style):** transparent with a 1px Archive Green border and green text. Used for repeated or optional actions such as the Info edit actions and the Maps buttons on Places and Info.
- **Text (TextButton style):** Open Sans Semibold 14px in Archive Green (Sage Mist at night), no fill, 48dp tall. Used for navigation and low-stakes actions: Recycle bin, See all, Use system picker, Retry failed, Clear done, Clear all.
- **Danger text (DangerTextButton style):** the same, in Danger Red. Used for Unpair. It always confirms first.
- **Disabled:** Gray 200 fill with Gray 950 text (dark theme: Gray 600 with Gray 200).
- **Toolbar actions:** text-only items in the nav bar (Select, Organize, Edit (n), Empty bin, Info, Delete), in black or the theme foreground. Prefer these for screen-level actions over extra filled buttons.

### Filter Chips (Chip control)
- A 36dp pill with 8px corners and 12px padding inside a 48dp touch area, Open Sans 14px.
- **Inactive:** outlined (1px #ACACAC, #6E6E6E at night), Ink Soft text, and the filter's own name ("Everyone", "Photos & videos", "Any time", "Album", "Folder", "Tag", "Location", "Device").
- **Active:** a tonal fill and Semibold label showing the chosen value ("Sam", "Photos", "Last 3 months", "Trips / Bali"). Light theme: Pale Celadon fill with Archive Green text. Dark theme: Archive Green fill with Pale Celadon text, because a deep tint disappears against off-black.
- **Trailing icon:** a drawn 10px chevron for choices that open a list; a drawn close mark for the removable place chip; none for toggles (Favorites). Icons are drawn paths, never text glyphs.
- Tapping a chip opens a native action sheet; Favorites toggles in place.

### Photo Tile (signature)
- A square image with Aspect Fill, a Tile Placeholder background while loading, and no corner radius.
- **Video:** a drawn white play triangle (7 × 9) in the 50% black badge, followed by the duration when it is known.
- **Selecting:** every tile shows a 24px white ring (2px stroke, 20% black fill) in the top-left corner, 6px inset.
- **Selected:** the photo insets by 12px onto a tonal tile (Pale Celadon; Deep Archive Green at night), and the ring fills with Archive Green (Sage Mist at night) and a drawn check. Never cover the photo with an overlay: it must stay recognisable.
- The device-photo chooser uses the same selection language, with the ring always visible.
- The device-photo chooser opens with three chips under the nav bar (**All**, **Photos**, **Videos**; one active, as on Places) and groups tiles under the same month headers as the Library. Order is newest first by date taken, using the date added when a file has none. Picks survive switching chips; the toolbar's **Upload (n)** counts them all. Each month header carries a right-aligned text button, **Select all**, which reads **Clear** once every item in that month is picked (it loads the rest of the month first if it runs past the loaded page).
- Tap opens the viewer, or toggles selection while selecting.

### Month Header
- Open Sans Semibold 16px, padding 16px top, 8px sides and 6px bottom, with a "MMMM yyyy" label such as "October 2026".

### Inputs / Pickers
- **Style:** Android-native underline fields with a transparent background and 14px text, at least 44px tall.
- **Filter pickers** always show what they filter, even when unset: "Any folder", "Any album", "Any tag", "Any device", "Any location". Never use a bare "Any".
- **Check boxes:** Archive Green (white in the dark theme).

### Save to Phone
- Offered in three places: the viewer's overflow menu, first in the selection **Edit (n)** sheet, and first among the Info page's outlined actions.
- Tapping it shows a toast: "Saving 3 items to your phone. Find them in Pictures/Lore and Movies/Lore." The work runs in the background service after any uploads.
- Progress notification "Saving to phone", with "{file name} · 2 of 5" or "Waiting for home Wi-Fi…".
- On finish, a dismissable summary that stays: "Saved 3 items to Pictures/Lore or Movies/Lore · 2 already on this phone · 1 failed (try again on home Wi-Fi)". Only the parts that apply appear.
- An exact copy already on the phone (same size, then same SHA-256) is never downloaded again.

### In Lore and Free Up Space
- **In Lore badge:** in the device-photo chooser, a phone file whose exact bytes are in Lore gets a bottom-left badge: a drawn check plus "In Lore" (12px, white, on the same 50% black as the video badge). It stays selectable.
- **Upload tab:** a quiet line under the choose buttons, either "926 KB on this phone (18 items) is already safe in Lore." or a hint to run Check this phone. Below it are two text buttons: **Free up space** (only when there's something to free) and **Check this phone**.
- **Check this phone** fingerprints the whole gallery in the background service. It shows a "Checking this phone · 120 of 3,400" notification, then a summary: "Checked 20 photos and videos on this phone: 19 are safe in Lore."
- **Free up space** is for home Wi-Fi only. It re-fingerprints every file and asks Lore to confirm it still holds that exact file outside the recycle bin. Edited files are skipped and forgotten.
  - **Android 11+:** files move to the phone's trash through the system dialog ("Allow Lore to move 18 items to trash?").
  - **Older Android:** Lore asks first, and the copies are deleted for good.
  - **Result toast:** "Moved 18 items (926 KB) to the phone's trash. Android empties it after 30 days; Lore keeps the originals."

### App Updates
- **Family → This phone:** a quiet "Lore 1.3" line.
  - When the home PC offers a newer version, it reads "Lore 1.2. Version 1.3 is ready on the home PC (35.4 MB)." with an outlined **Update Lore to 1.3** button.
  - The button reads "Downloading… 31%" while it works, then hands over to Android's own "Do you want to update this app?" prompt.
  - The first time, Android's "Install unknown apps" page opens instead, and the line says to allow it and tap Update again.
- **Home:** a text button "Update available: Lore 1.3" that leads to Family.
- **Admin page:** an "App update" section showing the offered version, with a file picker and an **Upload APK** button. Older versions and non-Lore APKs are refused with a plain message.

### Queue Row
- **Line 1:** the filename (middle-truncated), with a short status on the right in Quiet style ("Uploaded", "Already in Lore", "62% · 3.4 MB", "Retry 2/5").
- **Line 2:** a full-width Archive Green progress bar.
- Rows have 10px vertical padding and 6px between lines.
- **Failed:** the status turns Danger Red ("Failed"), and the full reason appears as a third line in Danger Red. The screen then shows a "Retry failed" text button.

### Place Row and Plot
- **Row:** a 64px square thumbnail, coordinates in Body Large ("1.291°N, 103.852°E"), a detail line ("2 items · Oct 2026") and an outlined Maps button.
- **Plot:** a 240px Linen Mint panel with 12px corners and 28px inner padding. Each place is an Archive Green dot at 75% opacity whose radius grows with the item count (4 + 3·log₂ count). No map tiles.

### Icons
- 24px outline glyphs at a 2px stroke with round caps and joins, drawn as SVG (tab bar, viewer toolbar) or as MAUI paths (play, check, chevron, close).
- Viewer toolbar: heart (outline, or filled when a favorite), info and trash (restore inside the bin). Each icon has a text name for screen readers and the overflow menu. **Save to phone** lives in the overflow menu (not shown in the bin): a fourth icon would squeeze the two-line title at large text sizes.
- Android doesn't tint toolbar icons, so each ships a light-bar variant (black) and a dark-bar variant (Sage Text #6FA88C), switched by theme.
- No text glyphs (✓ ▶ ♡) as icons.

### Empty States
- Centred body text with 32px side and 48px top margins that says what to do next: "No photos yet. Add some from the Upload tab." / "Nothing matches these filters. Tap Clear all to see everything." / "The recycle bin is empty. Deleted photos and videos wait here until you empty it."
- Actions that can't do anything (Empty bin on an empty bin) are disabled, not hidden.

### Navigation
- Under 600dp wide: a bottom tab bar with five tabs: Home, Library, Upload, Places and Family. Icons are 24px outline glyphs at a 2px stroke. Tab and rail labels are a fixed 13dp (the same selected or not) so they never collide with their icons at large system text sizes; every other text scales with the system setting.
- The selected tab is Archive Green (Sage Mist at night); unselected tabs are Ink Soft (Gray 200 at night). The tab bar background is white (black at night).
- 600dp and wider (tablets, phones in landscape): the same five destinations as an 80dp navigation rail on the start edge, with the Lore symbol (32dp) at the top. Each item is the icon over a 13dp label; the selected one sits in a 56 × 32dp pill (Pale Celadon; Archive Green at night) with a Semibold label. The rail's top carries the status bar's green band and widens by any cutout on its side.
- The viewer and video player are fullscreen at every size: they hide the rail as they hide the tabs.
- The nav bar is white or off-black, flat and title-left, with text toolbar actions on the right.

### Admin Page (web, on the home PC)
- A single 640px column in the system UI font. Light and dark follow the OS (`color-scheme: light dark`).
- Headings are 2.5rem (page) and 1.375rem (sections). The pairing code is shown at 3rem, weight 700, with wide letter spacing.
- Status values are a two-column definition list; warnings are red (#b3261e, or #ffb4ab in the dark theme) and semibold. Buttons are native.

## Do's and Don'ts

### Do:
- **Do** keep Archive Green (#2B4C3F) as the only chrome accent. Use it for actions, the selected state and progress.
- **Do** let photos fill the width: square tiles, 2px gutters, no rounding, no borders.
- **Do** give every tappable thing at least 48dp of height, and buttons 8px corners.
- **Do** keep one filled button per screen; step everything else down to outlined, text or chip.
- **Do** put filters in the chip row. Never stack pickers above a photo grid.
- **Do** show placeholders in Tile Placeholder grey (#E6E6E6 / #2A2A2A) rather than leaving holes.
- **Do** dim secondary text with opacity (0.6–0.7) instead of introducing new colours.
- **Do** keep pages flat; use the soft sheet lift only on floating sheets and dialogs.
- **Do** write labels the way the family talks: "Waiting for home Wi-Fi", "Already in Lore", "Clear place".

### Don't:
- **Don't** imitate Google Photos or iCloud chrome, icons or layouts; Lore is the family's own archive.
- **Don't** add loud or multi-colour UI accents, gradients or coloured section backgrounds. The photos supply the colour.
- **Don't** introduce corporate-dashboard patterns (dense tables, KPI tiles, charts) into the family app. Plain lists and counts are enough.
- **Don't** use the template leftovers Magenta (#D600AA) or Midnight Blue (#190649); they are not part of Lore's palette.
- **Don't** round photo tiles or add shadows to pages, rows or tiles.
- **Don't** add a second typeface or use all-caps labels.
