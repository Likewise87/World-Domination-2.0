"""Capture WD overview HTML boards to screenshots/ as PNGs."""
from __future__ import annotations

from pathlib import Path

from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "screenshots"
# file:// avoids needing a local HTTP server; textures load via relative paths.

PAGES = [
    "title-page.html",
    "snapshot-overview.html",
    "outposts-overview.html",
    "outposts-details.html",
    "upgrades-overview.html",
    "upgrades-details.html",
    "upgrades-bor-overview.html",
    "upgrades-bor-details.html",
    "caravans-overview.html",
    "caravans-details.html",
]


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    # Remove debug crops from earlier attempts
    for junk in OUT.glob("_*.png"):
        junk.unlink()

    with sync_playwright() as p:
        browser = p.chromium.launch()
        # Title is 1920x1080; overview boards are ~860 wide / tall content
        context = browser.new_context(
            viewport={"width": 1920, "height": 1080},
            device_scale_factor=1,
        )
        page = context.new_page()

        for name in PAGES:
            url = (ROOT / name).as_uri()
            out_path = OUT / (Path(name).stem + ".png")
            print(f"capturing {name} -> {out_path.name}")
            page.goto(url, wait_until="load")
            page.evaluate("() => document.fonts.ready")
            page.wait_for_timeout(300)
            locator = page.locator(".page")
            if locator.count() > 0:
                locator.first.screenshot(path=str(out_path), type="png")
            else:
                page.screenshot(path=str(out_path), type="png", full_page=True)
            print(f"  wrote {out_path.stat().st_size} bytes")

        browser.close()
    print("done")


if __name__ == "__main__":
    main()
