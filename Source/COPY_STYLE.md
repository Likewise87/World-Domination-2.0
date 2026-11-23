# TSA World Domination — copy and UI text style

How to use this file: open when changing keyed XML, tooltips, labels, or tone. Do not use it for globe icons (`Core/WORLD_MAP_ICONS.md`), tiles (`Core/PLANET_LAYERS.md`), sim owners (`ARCHITECTURE.md`), hub layout / IMGUI rects (`UI_WINDOWS.md`), Def structure/naming (`Guardrails/DEFS_GUIDE.md`), or release / Steam Workshop publish (`Guardrails/VERSIONING.md`). Index: `Guardrails/GUIDANCE.md`. After a copy-rule change, edit this file in the same pass.

Rules for player-facing strings (translations, tooltips, dialog labels). Apply to EN first; ES/ZH should match meaning and placeholder slots.

World-model **ownership** (one colony, player-only outposts, NPC holdings = Settlements) lives in the always-on rule `Guardrails/.cursor/rules/wd-product-docs.mdc`. This file only owns wording.

## Traveler vs caravan wording

- NPC mobile forces are WD travelers (`WorldObject_Traveler` with abstract strength). There are **no** normal NPC caravans with real pawns.
- **Only the player** uses vanilla `Caravan` objects with real pawns on the world map.
- Do not write copy, UI, or design notes that imply “NPC caravans” as pawn-bearing world objects. Say **NPC traveler / WD caravan / raid caravan** (strength pool) vs **player caravan** (real pawns).
- **Never say “sally” / “sallies”** in player-facing copy. Prefer **interception caravan**, **ambush**, or **intercept** (code may still use Feature C / sally internally).

Globe icons and static `Texture2D` / `Material`: `Core/WORLD_MAP_ICONS.md`.
Label heights and hub layout: `UI_WINDOWS.md`.

## Punctuation and tone

- **Do not use em dashes (`—`) ever.** Use parentheses, commas, or a short second sentence instead.
  - Bad: `Settlement Name — Tier 2`
  - Good: `Settlement Name (Tier 2)` or `Settlement Name, Tier 2`
- Avoid decorative middle dots (`·`) in tooltips unless RimWorld vanilla uses them in the same context.
- Prefer short labels in lists; put detail in tooltips or formula lines under the label.
- Write like a game manual, not a design doc. No internal terms (`weight`, `tier weight sum`, `floor()`, `divisor`).

## Action Log (row messages)

Applies to keyed strings used as `SpreadLogEntry.message` (Action Log Action column). Not Details dialog titles, not `FormatRaidLogSuffix` margin chips / win-chance chips (those stay Title Case UI chips).

1. **One sentence, sentence case.** Capitalize the first word and proper names / placeholders only.
2. **Trailing period** on every full row line. Parenthetical suffixes and raid outcome chips are exceptions.
3. **No em dashes; no arrows** (`→`, `->`, `-->`) in rows. Prefer `to`, parentheses, or `{0} to {1}`.
4. **Active voice, actor first** when Actor A is that actor: `{0} skipped …`, `{0} dispatched …`.
5. **Short category tags only** (same casing, colon, space): `RAID:`, `TRADER:`, `TRADE:`, `ROAD:`, `EXPAND:`, `GROW:`, `UPGRADE:`, `TURTLE:`, `VANGUARD:`, `INVASION:`, `DESPERATION:`, `CONQUEST:`, `REVOLT:`, `FORTIFY:`, `OUTPOST UPGRADE:`, `MAJOR INCIDENT:`, `MINOR INCIDENT:`, `ISOLATION:`, `SCOUT:`, `INTEL:`, `SABOTAGE:`, `DISINFORMATION:`, `DIPLOMACY:`, `POLLUTION:`, `ADV BUILD:`, `FORTIFICATION:`. Drop long ALL-CAPS essays (`RAID ABORTED:`, `EXPANSION ATTEMPT:`, `ROAD BUILDING ATTEMPT:`, `ISOLATION PRESSURE:`). Every `AddLog` must set `SpreadLogCategory` (or rely on Infer from tag/`isRaid`/Diplomacy highlight); do not filter the Action Log by parsing localized message text alone. Pollution filter also includes raid rows with pollution path flags.
6. **Skip template:** `TAG: {0} skipped (reason).` (lowercase `skipped`, reason in parentheses, trailing period).
7. **NPC mobiles:** traveler / raid force / WD trader in prose. Do not write “Caravan is underway” for NPC raids. **Exception:** keep the established **Caravan clash** event label until interception letters are renamed in the same pass.
8. **Band pick rows:** keep an English `Band ` substring in EN `TSA_WD_Log_Raid_BandPicked` / `BandFallback` (Action Log tiny-font heuristic). Historical scribed rows keep whatever text was stored at write time.

## Recruiting outpost (player model)

Layout for the recruiting dialog (two-column production picker): `UI_WINDOWS.md`.

- **Recruit count:** Social (1 per 10 average Social this cycle) **plus** extra recruits from nearby settlement tiers.
- **Neighbor bonus:** Up to the **top 3** nearby **NPC settlements** **per faction** (by tier, then distance) add **neighbor points** (Tier 1=1, Tier 2=2, Tier 3=3.5, Tier 4=5). Hostile neighbors use the **hostile neighbor efficiency** setting (default 50%) and are framed as **defectors** (never “black market”). Sum those contributors after the hostile discount. **Every 3 combined points = +1 extra recruit**. Dialog: contributing rows highlighted; other in-range rows greyed; **contributing hostile rows** get a slight **red background**. (WD outposts are player-only and never count as partners.)
- **Social rule:** Explain on Current/Average Social tooltips (1 pawn per 10 Social at cycle end). Do not repeat in expected-outcome tooltip.
- **Expected outcome tooltip:** One compact formula line: Social pawns + neighbor pawns × Expert %, optional skill focus, result. Do not dump multi-line soft-bonus essays when Expert is already in the line.
- **Xenotypes:** Rolled from nearby factions (including hostiles at reduced weight); list settlements with faction icons where helpful.
- **Skill training:** Picking a specific skill costs **30% fewer recruits** that cycle and guarantees a minimum level in that skill only. **Any** = full count, random skills.
- **Inspect line:** `Producing: {count} pawns capable in {skill} ({days})` when a skill is selected; `Producing: {count} pawns ({days})` when training any pawn. Gizmo button keeps `Recruiting: {skill}` / `Recruiting any Pawn`.
- **Cycle length** is never affected by skill choice.
- **Founding nearby:** Same partner pool as yield (hostiles/defectors count toward the minimum).

## Trading outpost (nearby partners)

- Same nearby partner pool as recruiting (top 3 per faction in radius). Neutral/allied contribute full tier silver; hostiles contribute at hostile neighbor efficiency (**black market** flavor; do not say defectors).
- Partner list labels stay one line with **effective** silver. Do not append long black-market text to the label. Put the efficiency note in the row tooltip / nearby header tip.
- Contributing hostile rows use a slight red background (same as recruiting).
- Founding and production nearby minimum use the partner pool (hostiles count).
- Stats Yield tip: one compact formula line (tier silver × Social% × Expert).

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

### Settlement intel (soft FoW) keys

- **Tracking vs Active:** Enable Fog of War = map fog, discovery, scouts, sight gizmos from day 0. Activates only gates when raids use imperfect knowledge. Master tip (`TSA_WD_Intel_EnableSettlementIntelTip`) owns that split; do not repeat it on every privacy toggle.
- Vocabulary in player tips: prefer **Line of sight** / **Awareness range** (tiles) over MaxLOS or bare “Awareness” alone when a full label fits. Always show both numbers when quoting range (e.g. “Line of sight: 12 tiles / Awareness range: 18 tiles”). **Line of sight** = see everything; **Awareness range** = Tier **3+** sites and **WD caravans** with T2+ strength still spotted farther out (T2 sites need LOS). Clear tiles + bright icons = LOS; grey shroud + dim icons = Awareness band; black = unexplored. Colony/outpost tips: “Each pawn adds X Line of sight and Y Awareness range.” Code symbols use `awareness*` / `GetAwarenessRadius` (not player-facing). No LookTargets, discovery ticks, or “always sees everything” while FoW is on. NPC mobiles are **WD caravans** / travelers (not player caravans). Do not revive four-band target-size ring copy.
- Settings: `Translations_Settings_Intel_*.xml` — kill switch `TSA_WD_Intel_EnableSettlementIntel*`, stage gate `TSA_WD_Intel_GateSettlementIntelTip`, privacy toggles (`PlayerColoniesAlwaysKnown`, `NotifyJumpUndiscovered`, `UncoverOnRaid`, `ListUndiscoveredTravelers`, `ThreatKnownOnly`, `SpottedCaravansRemainVisible`), `HeaderSightRings` / `LosFrac`, Awareness knobs for settlements/travelers/scouts, Got Burned, View World as / Show FoW tips. Nested knobs only when kill switch on and gate ≠ Never; tooltips must not hardcode Def numbers (settings helpers append defaults). Scout Awareness + scout speed mult (`ScoutSpeedMult`, Def 1.2× vs walking) live under Scouts. Legacy `RingFracT*` keys retired from UI (fields stay scribed). `PlayerCaravanMaxLos` keys stay unused (caravans use Scout Awareness); keep Scribe field for save compat.
- Save migration note (docs only): never clear discovery sets or explored FoW blobs for sight redesigns; seed `intelLosFrac` from legacy T1 once via `intelSightTwoRingMigrated`.
- Player scout (Build menu): `TSA_WD_Intel_PlayerScout_*` (gizmo tip, build tip, muster status/cancel, strength/pawn gates). Float-menu tip must call out prepare time and strength cost (`FormatBuildMenuTooltip`). Describe trap-style Shift+click waypoints + asphalt muster (pawn count × 5); do not say Enter-to-launch. Combat/lifetime letters: `TSA_WD_Letter_PlayerScoutDestroyed_*` / `LifetimeExpired_*`; notify toggles `TSA_WD_Notify_PlayerScoutDestroyed` / `LifetimeExpired` in Notifications keyed XML.
- Inspect tab: `TSA_WD_SettlementIntel_*` in `Translations_Outposts_English.xml` (tab label, sections Known to / Got Burned / Line of sight / sites and travelers in sight). No sticky-outpost UX keys.
- Traveler label: `TSA_WD_ScoutCaravan` in `Translations_Travelers_WorldMap_English.xml`. Say **scout traveler / Scout Caravan** (strength pool), not a pawn-bearing NPC caravan.
- Action Log discovery: `TSA_WD_Log_Intel_Discovered` (`INTEL: {0} discovered {1}.`) from `Discover` / `DiscoverTraveler` in `WorldComponent_SettlementIntel`. Actor is the observer site/traveler when known. Verbose logging mirrors the same row via `WDVerbose`.
- Letter/list fogging: `TSA_WD_Intel_UnknownSite`, `TSA_WD_Intel_FoggedName`, `TSA_WD_Intel_SettlementUndiscoveredTip` via `WdPlayerIntel`.
- Tone bar: world-threat alert (`Alert_WDWorldThreat` + threat notification keys). Short concrete sentences; define terms inline; parentheses for secondary facts.
