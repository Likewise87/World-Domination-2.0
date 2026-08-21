# TSA World Domination — copy and UI text style

How to use this file: open when changing keyed XML, tooltips, labels, or tone. Do not use it for globe icons (`Core/WORLD_MAP_ICONS.md`), tiles (`Core/PLANET_LAYERS.md`), sim owners (`ARCHITECTURE.md`), hub layout / IMGUI rects (`UI_WINDOWS.md`), or Def structure/naming (`dev/DEFS_GUIDE.md`). Index: `dev/GUIDANCE.md`. After a copy-rule change, edit this file in the same pass.

Rules for player-facing strings (translations, tooltips, dialog labels). Apply to EN first; ES/ZH should match meaning and placeholder slots.

World-model **ownership** (one colony, player-only outposts, NPC holdings = Settlements) lives in the always-on rule `dev/.cursor/rules/wd-product-docs.mdc`. This file only owns wording.

## Traveler vs caravan wording

- NPC mobile forces are WD travelers (`WorldObject_Traveler` with abstract strength). There are **no** normal NPC caravans with real pawns.
- **Only the player** uses vanilla `Caravan` objects with real pawns on the world map.
- Do not write copy, UI, or design notes that imply “NPC caravans” as pawn-bearing world objects. Say **NPC traveler / WD caravan / raid caravan** (strength pool) vs **player caravan** (real pawns).

Globe icons and static `Texture2D` / `Material`: `Core/WORLD_MAP_ICONS.md`.
Label heights and hub layout: `UI_WINDOWS.md`.

## Punctuation and tone

- **Do not use em dashes (`—`) ever.** Use parentheses, commas, or a short second sentence instead.
  - Bad: `Settlement Name — Tier 2`
  - Good: `Settlement Name (Tier 2)` or `Settlement Name, Tier 2`
- Avoid decorative middle dots (`·`) in tooltips unless RimWorld vanilla uses them in the same context.
- Prefer short labels in lists; put detail in tooltips or formula lines under the label.
- Write like a game manual, not a design doc. No internal terms (`weight`, `tier weight sum`, `floor()`, `divisor`).

## Recruiting outpost (player model)

Layout for the recruiting dialog (two-column production picker): `UI_WINDOWS.md`.

- **Recruit count:** Social (1 per 10 average Social this cycle) **plus** extra recruits from nearby settlement tiers.
- **Neighbor bonus:** Up to the **top 3** nearby **NPC settlements** **per faction** (by tier, then distance) add **neighbor points** (Tier 1=1, Tier 2=2, Tier 3=3.5, Tier 4=5). Sum those contributors. **Every 3 combined points = +1 extra recruit**. Dialog: contributing rows highlighted; other in-range rows greyed. Footer rule line (tier breakdown on hover); total and extra pawns below the list. (WD outposts are player-only and never count as partners.)
- **Social rule:** Explain on Current/Average Social tooltips (1 pawn per 10 Social at cycle end). Do not repeat in expected-outcome tooltip.
- **Expected outcome tooltip:** Compact only: Social => pawns, Neighbor points => pawns, optional skill penalty %, resulting pawns with math.
- **Xenotypes:** Rolled from nearby factions; list settlements with faction icons where helpful.
- **Skill training:** Picking a specific skill costs **30% fewer recruits** that cycle and guarantees a minimum level in that skill only. **Any** = full count, random skills.
- **Inspect line:** `Producing: {count} pawns capable in {skill} ({days})` when a skill is selected; `Producing: {count} pawns ({days})` when training any pawn. Gizmo button keeps `Recruiting: {skill}` / `Recruiting any Pawn`.
- **Cycle length** is never affected by skill choice.

## Settings tooltip defaults (mandatory)

Settings UI already appends the default for you. **Never put default values in keyed tooltip strings.**

Applies when the control uses:
- `SettingsUI.LabeledSlider(..., tooltip, ..., defaultValue)` (e.g. `Settings_Window_Outposts.cs`)
- `SettingsUI.TooltipWithDefault(tooltip, defaultValue)` on checkboxes / labels

**Wrong:** `… Research outposts are not affected. Default +15%.`  
**Right:** `… Research outposts are not affected.`

The helper appends something like `Default: …` from the `Def*` / `defaultValue` argument. Hardcoding numbers in EN/ES/ZH causes double defaults and drifts when constants change (e.g. aura radius 20 → 16).

Write the mechanic only. Pass the `Def*` constant into the slider / `TooltipWithDefault` call site.

## Translation keys

- Keep `{0}` / `{1}` placeholder order identical across EN, ES, and ZH for the same key.
- When retiring a key, grep the codebase and remove dead references.
- **Never bulk-replace translation / keyed XML text** (PowerShell `-replace`, sed across files, mass find-replace scripts, encoding-blind rewrite of whole language folders). That regularly corrupts UTF-8 (mojibake, `????`, broken Spanish/Chinese). Edit one string or one file at a time with a precise, encoding-safe edit (targeted `StrReplace` / editor replace of a single known line). After any translation edit, spot-check non-ASCII characters in ES/ZH.
