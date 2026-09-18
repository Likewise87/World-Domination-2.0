# WD Overview Pages

Local HTML art boards used for design/screenshots (Orbitron / Rajdhani / cyan theme). Not part of the Steam mod package (`Source/` is excluded).

Open any `*-overview.html` / `*-details.html` / `title-page.html` in a browser from this folder. Texture icons resolve via `../../Textures/...`.

## Screenshots

PNGs for Steam / docs live in `screenshots/`. Stable raw URLs (same path after replace):

`https://raw.githubusercontent.com/Likewise87/World-Domination-2.0/main/Source/WD_OverviewPages/screenshots/<name>.png`

Named boards:

| Board | File |
|-------|------|
| Title | `title-page.png` |
| Snapshot | `snapshot-overview.png` |
| Outposts | `outposts-overview.png`, `outposts-details.png` |
| Upgrades (core + ungated Bambaryla) | `upgrades-overview.png`, `upgrades-details.png` |
| Beyond Our Reach upgrades | `upgrades-bor-overview.png`, `upgrades-bor-details.png` |
| Caravans | `caravans-overview.png`, `caravans-details.png` |

BOR boards (new):

- https://raw.githubusercontent.com/Likewise87/World-Domination-2.0/main/Source/WD_OverviewPages/screenshots/upgrades-bor-overview.png
- https://raw.githubusercontent.com/Likewise87/World-Domination-2.0/main/Source/WD_OverviewPages/screenshots/upgrades-bor-details.png

Regenerate (needs Playwright Chromium once: `py -3.11 -m playwright install chromium`):

`py -3.11 Source/WD_OverviewPages/capture-screenshots.py`
