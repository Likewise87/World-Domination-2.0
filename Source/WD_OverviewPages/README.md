# WD Overview Pages

Local HTML art boards used for design/screenshots (Orbitron / Rajdhani / cyan theme). Not part of the Steam mod package (`Source/` is excluded).

Open any `*-overview.html` / `*-details.html` / `title-page.html` in a browser from this folder. Texture icons resolve via `../../Textures/...`.

## Screenshots

PNGs for Steam / docs live in `screenshots/`. Stable raw URLs (same path after replace):

`https://raw.githubusercontent.com/Likewise87/World-Domination-2.0/main/Source/WD_OverviewPages/screenshots/<name>.png`

Regenerate (needs Playwright Chromium once: `py -3.11 -m playwright install chromium`):

`py -3.11 Source/WD_OverviewPages/capture-screenshots.py`
