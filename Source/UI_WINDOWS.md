# Hub windows and IMGUI layout

How to use this file: open when adding or copying a hub window, table header, roster, restore-view control, or IMGUI label rect. Do not use it for keyed tone (`COPY_STYLE.md`), sim owners (`ARCHITECTURE.md`), globe icons (`Core/WORLD_MAP_ICONS.md`), or Def XML (`dev/DEFS_GUIDE.md`). Index: `dev/GUIDANCE.md` (file vs type naming: Settings vs Dialog vs Window vs WITab). After a code change that moves a shared helper, edit this file in the same pass. If a do-not-copy line is fixed, delete it.

## Label and row heights (IMGUI)

`Widgets.Label` crops glyphs when the rect is shorter than the current font’s line box. This has bitten outpost tabs repeatedly.

| Font | Minimum rect height (single line) | Prefer |
|------|-----------------------------------|--------|
| `GameFont.Tiny` | **15** (never 12) | `15f`+; two-line headers: `2 × 15` and `HeaderHeight ≥ 30` |
| `GameFont.Small` | **24** (never 18–20) | `24f` or `Text.LineHeight` |
| `GameFont.Medium` | **30** | `30f` |

- **Never** put `GameFont.Small` (or larger) text in a rect under ~24px tall.
- **Never** put `GameFont.Tiny` text in a rect under ~15px tall (tops of letters crop first).
- When the rect is taller than the glyph, **vertically center** with `Text.Anchor = MiddleLeft` / `MiddleCenter` (or a shared `LabelAnchored` helper). `UpperLeft` in a short rect is what clips.
- For intentional two-line headers (`"Daily\nfood needed"`), keep each line rect ≥15px. Prefer `HeaderHeight` of 30–32 over squeezing glyphs.
- After adding a subtitle under a headline + rule, bump the content start Y so the next block clears the full label rect (not only the separator line).

Em dashes and keyed tone: `COPY_STYLE.md`.

## Layout conventions

- Recruiting dialog follows **Outpost Production** two-column layout: stats and context on the left, selectable rows on the right.
- Right-column rows match production: icon, name, gray formula line, Select button.
- Do not stack long explanatory paragraphs above a picker table; use one selected summary line on the left (icon + name) and formula text on each row.

## Settings sliders with interdependent values

When several sliders must stay ordered (e.g. skill-band ends must increase, efficiency weights must decrease), **do not change each slider's min/max based on neighbors**. Shrinking the range feels broken and causes click/drag quirks.

Keep a fixed min/max on every slider, then after the player edits a value, clamp/normalize the stored settings so constraints hold (push later band ends up, clamp later weights to ≤ previous, hard cap ≥ last band end).

See `Dialog_OutpostSkillScalingSettings` + `OutpostSkillScaling.NormalizeBands`.

## Hubs and exclusivity

`WdNavWindows` (`UI/WdNavWindows.cs`): `OpenExclusive` closes all nav windows then opens one. `ToggleExclusive` closes if already open. `CloseAllNavWindows` also closes faction/raid-detail overlays and a few pawn dialogs.

| Class | File |
|-------|------|
| `Window_DiplomacyMatrix` | `UI/Window_DiplomacyMatrix.cs` |
| `Window_OutpostOverview` | `UI/Window_OutpostOverview.cs` |
| `Window_WorldStats` | `UI/Window_WorldStats.cs` |
| `Window_ActionLog` | `UI/Window_ActionLog.cs` (dashboard only) |
| `Window_ActiveTravelers` | `UI/Window_ActiveTravelers.cs` |
| `Window_AllPlayerPawns` | `UI/Window_AllPlayerPawns.cs` |
| `Window_Prisoners` | `UI/Window_Prisoners.cs` |

Main tab: `MainTabWindow_WorldDomination` (`UI/Window_MainDashboard.cs`). `Window_RemoteEstablishPawns` is not in this exclusive set.

## Reuse these

| Helper | File | Use for |
|--------|------|---------|
| `PawnRosterHeaderFilter.DrawFilterableHeader` | `UI/PawnRosterHeaderFilter.cs` | Column headers. Pass `onFilterClick` for a filter icon; pass null to sort (or label-only) without the glyph. |
| `PlayerPawnRosterUtility.DrawRosterViewControls` | `Outposts/PlayerPawnRosterUtility.cs` | Restore + columns + highlight icons |
| `WorldDomination_UIUtils` | `UI/Window_Utils.cs` | Restore-view icon, slate `ButtonTextWithIcon`, `JumpToWorldObjectOnMap` |
| `RaidUIUtils` | `UI/Window_Utils.cs` | Raid power boxes, win-chance bar, forecast |
| `SettlementCaravanDealUi` | `Outposts/Actions/SettlementCaravanLootUtility.cs` | Buy/gift/bribe tables |
| `WdWindowEsc.TryCloseOnCancel` | `UI/WdWindowEsc.cs` | Two-step Escape (defocus TextField, then close) |

Restore default view: `WorldDomination_UIUtils.DrawTitleRestoreDefaultView`. Tooltip key `TSA_WD_AllPlayerPawns_RestoreDefault` until a generic key exists. Diplomacy and World Stats call it directly; rosters go through `DrawRosterViewControls`.

## Roster family

Column sets differ. Chrome must not be copied again.

- `UI/Window_AllPlayerPawns.cs`
- `UI/Window_Prisoners.cs`
- `UI/Window_RemoteEstablishPawns.cs`
- `Outposts/WITab_Outpost_Pawns.cs`

Extend `DrawRosterViewControls` / `DrawFilterableHeader`. Do not start a fifth copy.

## Table headers

Call `PawnRosterHeaderFilter.DrawFilterableHeader`. Filter icon is optional (`onFilterClick == null` skips it). Diplomacy, World Stats, Outpost Overview, and Buy / Bribe / Gift / Negotiate deals already use it. Do not add another local sort-arrow `DrawHeader`.

Leftover local headers (leave unless touching that window): `Window_FactionDetails` (separate filter rows), roster `DrawHeader`s, `Window_ActiveTravelers` (no sort).

## Session state trap

- Diplomacy: `searchTerm`, `sortColumn`, `sortAscending` are **static** (survive close).
- All Player Pawns: filters and sort are **static**.
- World Stats: `nameFilter`, `sortColumn`, and `sortAscending` are **static**.

Match the window you are editing. Do not assume all hubs share one pattern.

## Do not copy (windows)

- Do not clone roster chrome. Extend the shared roster helpers.
- Do not add a local `DrawHeader` / `HeaderButton`. Call `DrawFilterableHeader` (`onFilterClick: null` when there is no filter).

Raid-range, raid interpolators, and buff stacks: `ARCHITECTURE.md`.

## Appendix (memory, not a backlog)

- God files: `Settings/Settings.cs` (~4600), `Outposts/WorldObject_WD_Outpost.cs` (~3700), `Travelers/WorldActions_Traveler.cs` (~3000), `Core/CompViralSpread.cs` (~2200), `WorldActions/WorldActions_Orchestrator.cs` (~1550). `UI/Window_Utils.cs` holds two unrelated static classes (`RaidUIUtils` and `WorldDomination_UIUtils`).
- Settings clamp: `caravanRaidPointsMin/MaxStorytellerFraction` is the legacy pair when escalation-scaled clamp is off; Early/Mid/Late bands win otherwise (`RaidPointsHelper.GetActiveStorytellerClampFractions`).
