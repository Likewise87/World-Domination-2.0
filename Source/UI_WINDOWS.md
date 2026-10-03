# Hub windows and IMGUI layout

How to use this file: open when adding or copying a hub window, table header, roster, restore-view control, or IMGUI label rect. Do not use it for keyed tone (`COPY_STYLE.md`), sim owners (`ARCHITECTURE.md`), globe icons (`Core/WORLD_MAP_ICONS.md`), or Def XML (`Guardrails/DEFS_GUIDE.md`). Index: `Guardrails/GUIDANCE.md` (file vs type naming: Settings vs Dialog vs Window vs WITab). After a code change that moves a shared helper, edit this file in the same pass. If a do-not-copy line is fixed, delete it.

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

- Recruiting dialog follows **Outpost Production** two-column layout: stats and context on the left, selectable rows on the right. The travel mode icon plus fallback checkbox sit right-aligned on the left column's selected-training row (not the full-width header row, which would float above the right-hand picker).
- Right-column rows match production: icon, name, gray formula line, Select button.
- Do not stack long explanatory paragraphs above a picker table; use one selected summary line on the left (icon + name) and formula text on each row.

## Settings sliders with interdependent values

When several sliders must stay ordered (e.g. skill-band ends must increase, efficiency weights must decrease), **do not change each slider's min/max based on neighbors**. Shrinking the range feels broken and causes click/drag quirks.

Keep a fixed min/max on every slider, then after the player edits a value, clamp/normalize the stored settings so constraints hold (push later band ends up, clamp later weights to ≤ previous, hard cap ≥ last band end).

See `Dialog_OutpostSkillScalingSettings` + `OutpostSkillScaling.NormalizeBands`.

## Hubs and exclusivity

`WdNavWindows` (`UI/WdNavWindows.cs`): `OpenExclusive` closes all nav windows then opens one. `ToggleExclusive` closes if already open. `CloseAllNavWindows` also closes faction/raid-detail overlays, a few pawn dialogs, and `Dialog_OutpostArmory`.

| Class | File |
|-------|------|
| `Window_DiplomacyMatrix` | `UI/Window_DiplomacyMatrix.cs` |
| `Window_OutpostOverview` | `UI/Window_OutpostOverview.cs` |
| `Window_WorldStats` | `UI/Window_WorldStats.cs` |
| `Window_ActionLog` | `UI/Window_ActionLog.cs` (dashboard only) |
| `Window_ActiveTravelers` | `UI/Window_ActiveTravelers.cs` |
| `Window_AllPlayerPawns` | `UI/Window_AllPlayerPawns.cs` |
| `Window_AllPlayerGear` | `UI/Window_AllPlayerGear.cs` |
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

- `UI/Window_AllPlayerPawns.cs` — viewport-culls rows with `lastScrollViewportHeight` (only draw Y in the scroll band). Prefer the same for any new long hub table; see ARCHITECTURE “GUI perf” hub-list bullet.
- `UI/Window_Prisoners.cs`
- `UI/Window_RemoteEstablishPawns.cs` — tile-first founding picker; uses `PawnRosterColumnWindow.RemoteEstablish` + `DrawRosterViewControls` (same chrome as All Player Pawns; Confirm + ad-hoc travel strip instead of Transfer / Establish).
- `Outposts/WITab_Outpost_Pawns.cs`

Extend `DrawRosterViewControls` / `DrawFilterableHeader`. Do not start a fifth copy.

## Pawn travel mode controls

All drawing lives in `Outposts/PlayerPawnDropPodUtility.cs`. Icons are the warehouse `LandIcon` / `DropPodIcon` tinted `WorldOverlayLineMaterials.DarkCyanColor`, with a float menu on click. No visible labels on the mode icon; everything is explained on mouseover. Ad hoc strips and confirms use one line: mode icon, then if drop pod `Total Cost: {N}x` plus the industrial-component `ThingIcon` (`DrawModeAndTotalCostLine` / `DrawAdHocModeAndCostStrip` / `DrawModeReadoutWithTotalCost`). Stock and short-stock notes live on the cost-segment tip.

| Scope | Helper | Where |
|-------|--------|--------|
| Ad hoc, one global choice, session only | `DrawAdHocModeIcon` (28f) and `DrawAdHocModeAndCostStrip` (single-line Total Cost) | `Window_AllPlayerPawns` toolbar sets it; Transfer, Remote establish, and the tile-first picker read it |

Remote founding (All Player Pawns Establish / tile-first picker): founders must share one origin (one colony map or one WD outpost of any type). Mixed origins disable Establish/Confirm with the fail tip. Materials and drop-pod components still come from the player colony map plus warehouses (`Outpost_PowerPlant.GetPlayerColonyMap`), never from the outpost origin map.
| Confirm / goods dest | `DrawModeReadoutWithTotalCost` | `Dialog_SmartSendConfirm`, `Dialog_AdHocShipmentDestination` (Gear + Warehouse Ship Now) |
| Automated sends, persisted per origin | `DrawOriginModeAndFallback(Single)` (58f: icon, 6f gap, 24f checkbox) | `Window_Prisoners`, `WITab_Outpost_Pawns`, `Dialog_OutpostRecruiting` left column |

The fallback checkbox only appears next to a per-origin mode icon, because it governs automated sends only. Manual actions pass `ShortStockFallback.AlwaysLand` and let the player back out of the "Still launch?" confirm instead. Do not add a fallback checkbox to an ad-hoc toolbar. Ad hoc pod launches (Gear, Warehouse Ship Now, Transfer / Smart Send, remote establish, Rapid Response) call `PlayerPawnDropPodUtility.ConfirmHostileAaThen` before commit; multi-origin warns if any remaining pod flight is threatened.

## Armory windows

- `Outposts/Armory/Dialog_OutpostArmory.cs` — two-column gear dialog, opened from the Armory button on `WITab_Outpost_Pawns`. Occupants on the left read **live** `pawn.equipment` / `pawn.apparel` (`VirtualPawnSummary` never snapshots those); the store on the right renders grouped `ArmoryRows()` (on a warehouse, the gear slice of the shared warehouse stock) and ungrouped uniques. Its Convert to virtual food button converts all stored food via `OutpostFoodConversion`. Chrome matches All Player Pawns: `PawnRosterHeaderFilter` name search on occupants, Item search + Type weapon filter (Guns / Bows / Melee / Grenades / Other) + Count sort on the store, static session filters, zebra rows, shared dropdown. Occupant rows also show Shooting / Melee / Health% (sortable; Health on by default here) and icon-only Apparel / Weapons / Food & Drugs sections. Armory / All Gear buttons use the vanilla recon armor icon (`Apparel_ReconArmor`).
- `UI/Window_AllPlayerGear.cs` — every outpost Armory, uncommitted colony-map stacks, and pawn-equipped gear, with multi-select send from stores/maps. Outposts send through `OutpostStorageShipping.TryLaunch`, colonies through `ColonyArmoryLaunchUtility.TryLaunch`; the targeter validates each source with `IsValidShipmentDestination`. Rows at an outpost under manual defense, gear in world caravans, and worn/carried gear on a map under an active hostile assault/siege lord (or an active WD defense/clash encounter) show a disabled checkbox with the reason on hover. Multi-origin send to a destination that is also one of the selected origins opens `Dialog_AllInventorySkipSameDest` (scrollable skipped list) and ships only the other origins on confirm. Part of the exclusive nav set above. Draw path: viewport-cull rows via `lastScrollViewportHeight` (same band as All Player Pawns); cache JumpTip / on-pawn / blocked / quantity tips at rebuild — no `Translate(args)` in `DrawRow`. `MaxRows` caps collect/sort only.
- `Outposts/Warehouse/WITab_Outpost_Warehouse.cs` Storage tab lists the whole warehouse stock (armory rows included) plus armory uniques (cyan), with All Player Gear chrome: checkboxes, Type / Quality / Count columns, filterable Item / Type / Quality headers, clean labels (quality not in the name), and send amounts defaulting to max. Ship Now and Convert to virtual food only use checked rows. The daily auto delivery button ends under Ship Now. The auto-delivery menu carries the per-warehouse Include Apparel / Weapons and ammo / Food / Drugs / Medicine checkboxes; picking one toggles it and reopens the menu through `WdFixedPosFloatMenu` at the old menu's position, so the rows do not jump to the cursor. Any self-rebuilding float menu must do the same; `UI/WdCascadingFloatMenu.cs` is only for the parent/child build cascade, since its statics treat every instance as part of one cascade.
- `UI/WdItemDragDrop.cs` — the only drag-and-drop helper. Built on `Widgets.ButtonInvisibleDraggable`, which also gives click-to-move from the same call. It owns the payload and mouse-up itself, because the lists cull rows while scrolling and that would otherwise drop Widgets' active control mid-drag. Draw the ghost after the last `EndScrollView`/`EndGroup` or it is clipped.
- `UI/Dialog_WdCountPicker.cs` — shared count prompt (shift-drop to split a stack), using the established `Widgets.TextFieldNumeric` convention.

**Join Stamp** (prefs id still `New`): days since first player-faction join via `WorldComponent_PlayerPawnJoinTimes`. Shared filter enum `PawnRosterJoinedFilter` and helpers in `PlayerPawnRosterUtility` / `PawnRosterHeaderFilter.JoinedFilterChoices`. Used by All Player Pawns and Outpost Pawns.

## Table headers

Call `PawnRosterHeaderFilter.DrawFilterableHeader`. Filter icon is optional (`onFilterClick == null` skips it). Diplomacy, World Stats, Outpost Overview, and Buy / Bribe / Gift / Negotiate deals already use it. Do not add another local sort-arrow `DrawHeader`.

Leftover local headers (leave unless touching that window): `Window_FactionDetails` (separate filter rows), roster `DrawHeader`s, `Window_ActiveTravelers` (no sort).

## Session state trap

- Diplomacy: `searchTerm`, `sortColumn`, `sortAscending` are **static** (survive close).
- All Player Pawns: filters and sort are **static**.
- Outpost Armory: `pawnSearchTerm`, `itemSearchTerm`, `typeFilter`, `sortColumn`, `sortAscending` are **static**.
- World Stats: `nameFilter`, `sortColumn`, and `sortAscending` are **static**.

Match the window you are editing. Do not assume all hubs share one pattern.

## Do not copy (windows)

- Do not clone roster chrome. Extend the shared roster helpers.
- Do not add a local `DrawHeader` / `HeaderButton`. Call `DrawFilterableHeader` (`onFilterClick: null` when there is no filter).

Raid-range, raid interpolators, and buff stacks: `ARCHITECTURE.md`.

## Appendix (memory, not a backlog)

- God files: `Settings/Settings.cs` (~4600), `Outposts/WorldObject_WD_Outpost.cs` (~3700), `Travelers/WorldActions_Traveler.cs` (~3000), `Core/CompViralSpread.cs` (~2200), `WorldActions/WorldActions_Orchestrator.cs` (~1550). `UI/Window_Utils.cs` holds two unrelated static classes (`RaidUIUtils` and `WorldDomination_UIUtils`).
- Settings clamp: `caravanRaidPointsMin/MaxStorytellerFraction` is the legacy pair when escalation-scaled clamp is off; Early/Mid/Late bands win otherwise (`RaidPointsHelper.GetActiveStorytellerClampFractions`).
