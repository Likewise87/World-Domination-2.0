# Sim architecture

How to use this file: open when adding sim, raids, travelers, daily-loop, or strength numbers. Do not use it for keyed copy (`COPY_STYLE.md`), globe icons (`Core/WORLD_MAP_ICONS.md`), tiles (`Core/PLANET_LAYERS.md`), hub windows (`UI_WINDOWS.md`), or Def XML (`Guardrails/DEFS_GUIDE.md`). Index of all guidance: `Guardrails/GUIDANCE.md`. After a code change that moves an owner, flow, or naming alias, edit this file in the same pass.

World-model ownership (one colony, player-only outposts, NPC holdings = Settlements) lives in the always-on rule `Guardrails/.cursor/rules/wd-product-docs.mdc`. Do not rewrite it here. Tick/alloc/save rules: `Guardrails/.cursor/rules/tsa-world-domination.mdc`. **Save compatibility** is a hard priority in those rules — never remove Scribe keys or delete legacy scribed fields.

## Folders

| Folder | Holds |
|--------|-------|
| `Core/` | Comps, snapshot, stats, range, planet guards, overlays |
| `WorldActions/` | Daily orchestrator, growth, roads, traders, incidents, diplomacy, interception |
| `Outposts/` | Player WD outposts, types, actions, warehouse, food logistics. Trading/Recruiting nearby SSoT: `Outpost_Trading` partner collect + type-aware `Outpost_EstablishmentRequirements.MeetsMinNearbySettlements` (hostiles count). Embassy nearby: `Outpost_Embassy.IsEligiblePartnerFaction`. |
| `Travelers/` | World travelers, pathing, arrival, Harmony bootstrap |
| `RaidLogic/` | Raid assess/finalize, gate, simulated resolve, colony executor |
| `UI/` | Dashboard, stats, diplomacy, alerts, raid-detail windows |
| `Settings/` | `WorldDominationSettings` + `Settings_Window_*` menus |
| `Patches/` | Dedicated Harmony patches (settlement gizmos, goodwill, overlays) |
| `Buildings/` | Map buildings tied to WD |
| `Compat/` | Optional-mod bridges (reflection only; see `Guardrails/OPTIONAL_MODS.md`) |
| `Debug/` | Dev/debug helpers |
| `Decontamination/` | Decontamination flow |
| `Guardrails/` | Developer guardrails and Cursor rules (do not ship to players) |
| `dev/` | Local one-off scripts only (not guardrails) |

Also: `RoadBlocks/`, `SpikeTraps/`, `WorldGen/`, `Quests/`, `Gizmos/`. There is no `Letters/` code folder; letter types live with their owners.

## Who owns state

| Class | File |
|-------|------|
| `WorldComponent_SpreadManager` | `WorldActions/WorldActions_Orchestrator.cs` |
| `CompViralSpread` | `Core/CompViralSpread.cs` (on every settlement/outpost) |
| `WorldObject_WD_Outpost` | `Outposts/WorldObject_WD_Outpost.cs` |
| `WorldObject_Traveler` | `Travelers/WorldObject_Traveler.cs` |

Other WorldComponents: interception (`WorldActions/Interception/WorldComponent_InterceptionScheduler.cs`), logistics (`Outposts/FoodLogistics/WD_Outpost_FoodLogistics_Core.cs`), road blocks (`RoadBlocks/WorldComponent_RoadBlocks.cs`), traps (`SpikeTraps/WorldComponent_SpikeTraps.cs`), player pawn favorites (`Core/WorldComponent_PlayerPawnFavorites.cs`), Join Stamp (`Core/WorldComponent_PlayerPawnJoinTimes.cs`).

## Daily loop

`WorldComponent_SpreadManager.WorldComponentTick` (`WorldActions_Orchestrator.cs`):

1. Once per day (`60000` ticks): `CalculateDailyBudget` → `DailyWorldSnapshot.Build` (`Core/DailyWorldSnapshot.cs`; enumerates **WD participants** only via `IsWdParticipant`) → revolt / forward assault (Vanguard|Invasion) / **Isolation Pressure** (`WorldActions_IsolationPressure`: when fewer than 2 hostile factions threaten the player, one faction not on per-faction CD may be forced to found 1–2 settlements 10–15 tiles from the player; chance Def 40%; stage gate Def Always; founding raid+defense shields) / diplomacy / threat → enqueue faction action slots. Special-event cooldowns: `WorldActions_SpecialEventCooldown` (war / revolt / forward assault; optional shared). **Strategy on Settlement Loss** is reactive (`WorldActions_DesperationRaid.NotifyNpcSettlementLost`): cluster pressure (hostile offense within 15 tiles of any cluster site (membership edge 20) ÷ cluster offense) + min size + `gateThreatDesperation` (Def Always; **not** set by Easy/Medium/Hard presets) → `settlementLossStrategyFireChance` (Def 40%; fail does not stamp CD) → fork Turtle vs desperation by equal-share relative strength × likelihood slider; shared anti-spam CD on `desperationRaidCooldownByFaction` (Def 2 days). Reactive Turtle uses ally radius (migrate outside / fortify-in-place if zero migrants). Daily Turtle stays separate (`WorldActions_Turtle.TryTrigger`: leaves need offense ≥400; pack into existing dig-ins — primary, T3/T4, up to 2 reserved strong T2 vessels — preferring T3-cap room for multi-T3 consolidate / ally reinforcements; rare last-resort T4-cap overflow on existing hubs; no turtle founding). Incident obliteration does not start Strategy. **NPC Fortify** (`WorldActions_NpcFortify`): threatened settlements build the shared turtle kit (`WorldActions_FortifyKit`) locally in phases (road traps + ensure road exit → road blocks → AT turrets); Turtle / desperation place the full kit instantly via `TryPlaceFortifyKit` (T1: Spike + Light block + 1 Light AT). If Fortify is picked but cannot launch, orchestrator re-rolls once among other eligible weights. **Reactive loss bus and daily threats require `ProgramState.Playing`** (no Strategy / FA / Turtle / action queue during world gen or Select Starting Site). Vanguard packs 5–7 far sites and mass-relocates; Invasion reuses the same pick gates but launches 5–7 coordinated raids with homes staying (`WorldActions_Raid.TryLaunchCoordinatedInvasionRaid`). Pack→rally→absorb→Raid for Desperation: `WorldActions_AssaultRally` (`TravelerMission.DesperationRally`; `isDesperationRaid`). Legacy in-flight Invasion pack/rally travelers still resolve via AssaultRally / pack-up refound.
2. `ticksUntilNextAction` → `ExecuteNextAction` → `WorldActions_Raid.AttemptRaid` (`RaidLogic/Raid_Manager.cs`) and sibling action attempts.
3. Staggered eval: `pendingRaid.EvaluateNext` → `WorldActions_Raid.FinalizeRaid` spawns a traveler.
4. Arrival: `WD_PathFollower.ArrivalAction` → `WorldActions_Traveler.ExecuteArrival` (`Travelers/WorldActions_Traveler.cs`).

## Caravan clash (player vs traveler)

Temporary vanilla `Ambush` map + `WD_MapComponent_CaravanClash` tracker (`Travelers/WD_MapComponent_CaravanClash.cs`). Start: `WD_CaravanClashUtility.StartInterceptionEncounter`. Win: notify only — player exits via vanilla reform caravan; enemy cleanup + Ambush destroy on `MapRemoved`. Defeat / unresolved map close: always `RespawnNewTraveler` when `encounterActive && !playerHasWon` (do not trust dying-map hostile lists), then Ambush teardown. No second clash while a loaded Ambush clash map occupies the tile (`TileHasBusyCaravanClashAmbush`). Outpost manual defense uses a separate hard-close lifecycle (`RaidLogic/WD_MapComponent_OutpostDefense.cs`). Mid-fight drafted map-edge auto-caravan exit is blocked on active clash and outpost-defense maps (`Patches/Patch_WdTempEncounterExitMap.cs` + `Travelers/WD_TempEncounterExitMapUtility.cs`) by denying leave actions — not by patching `ExitMapGrid.MapUsesExitGrid` (that getter is UI-hot via `ExitMapGridUpdate`). Colony homes and NPC settlement attacks stay vanilla. WD clash also suppresses vanilla `CaravansBattlefield.CheckWonBattle` for the Ambush tracker lifetime so empty pre-raid maps cannot latch WonBattle or double-letter after WD victory.

## Raid path

`WorldActions_Raid` (`RaidLogic/Raid_Manager.cs`) → `RaidLaunchGate` (`RaidLogic/RaidLaunchGate.cs`) → traveler → on arrival `Raid_Simulated.ExecuteTravelerRaid` (`RaidLogic/Raid_Simulated.cs`) or colony incident / outpost defense.

SSoT types: `RaidCasualtyModel`, `RaidContribEntry` (`RaidLogic/Raid_MathSnapshot.cs`), `SettlementAttackRangeUtil` (`Core/SettlementAttackRangeUtil.cs`).

## Strength is not one number

Pick one and name it. Do not mix them.

| Kind | Where |
|------|-------|
| Offense pool | `CompViralSpread.offensiveStrength` (alias `strength`) |
| Deployable | `WorldActions_Utils.GetAvailableRaidStrength` = strength minus garrison retain. Wrappers: `GetDeployableOffense`, `RapidResponseUtility.GetDeployableStrength` |
| Ranking total | `CompViralSpread.GetTotalLocalDefensePower` (offensive + defensive). Summed in `WorldStatsUtils`. Equal-share relative strength: `RelativeToNpcEqualShare` / `GetLivingNpcStrengthTotals` (Strategy fork + leader/underdog/coalition) |
| Storyteller points | `RaidLaunchGate.GetColonyStorytellerDefense` → `StorytellerUtility.DefaultThreatPointsNow`. Clamp: `RaidPointsHelper.ClampRaidPointsToStorytellerBand` |

Attacker pool for gates: `RaidLaunchGate.SumAvailableAttPower` → `GetAvailableRaidStrength`.

## Naming (UI vs code)

Code IDs stay the old names. UI strings are the new ones.

| UI | Code |
|----|------|
| Nimble | `currentWeakestUnderdog`, `underdogBuff*`, `enableUnderdogBuff` |
| Expansionist | `expansionistZealFaction`, `expansionistZealExpiryTick`, `enableExpansionistZeal` |
| Warden | `OutpostExpertRole.Recruiter`, `expertRecruiterThingId` |

## Harmony

`HarmonyLoader` (`Travelers/DisableMemoryLeakWarning.cs`) scans the assembly for static `[HarmonyPatch]` classes. Settlement gizmos go through `Patch_SettlementGetGizmos` (`Patches/Patch_SettlementGetGizmos.cs`). Caravan gizmos go through `Patch_CaravanGetGizmos` (`Patches/Patch_CaravanGetGizmos.cs`). Do not add a second `Settlement.GetGizmos` or `Caravan.GetGizmos` postfix.

## Optional mods (no assembly dependencies)

Hard dependencies are only what `About/About.xml` lists under `<modDependencies>`. Everything else
(VFEPD, Worksites Expanded, CE, …) must stay optional at **runtime**: no compile-time references to
their DLLs unless listed as a dependency, and no reachable method body in the shipped WD assembly may
directly name optional-mod types.

- **XML:** `MayRequire`, `IfModActive` in `loadFolders.xml`, conditional `Patches/*.xml`.
- **C#:** `Source/Compat/*Compat.cs` — reflection + `ModLister` guards; never `using OptionalMod` in shipped code.
- **Pattern doc:** `Guardrails/OPTIONAL_MODS.md` (settlement storage scatter = base explicit lists + VFEPD-only category patch).

If you add optional integration, update `OPTIONAL_MODS.md` in the same pass.

## GUI perf: never scan AllWorldObjects or DefDatabase per frame

Right-side `Alert`s and world-map `WorldComponentOnGUI` run every frame; `AlertsReadout` re-checks active alerts continuously. Map float buttons and open dialogs are the same class of hot path. Do not walk `Find.WorldObjects.AllWorldObjects` or rediscover defs (`DefDatabase<T>.AllDefsListForReading`, category `DescendantThingDefs`, ammo-set walks) in these paths.

Read maintained/throttled registries instead:

- **Player outposts:** `WdPlayerOutpostCache.PlayerOutposts` (`Core/WdPlayerOutpostCache.cs`) — throttled snapshot (one scan per ~1800 ticks), self-healing (backwards-clock guard). Used by `Alert_WDOutpostUnusedExperts`, `Alert_WDOutpostNoProduction`, `Alert_WDConstructionInsufficientStrength`, `Alert_WDDropPodDeliveryInAaRange`, and the underlays' player-outpost draw. Consumers still null/`Destroyed`-check each element (a since-destroyed outpost can linger up to one interval; guard `AlertReport.CulpritIs`).
- **Travelers:** `WorldObject_Traveler.LiveTravelers` (maintained in SpawnSetup/Destroy). `WorldComponent_SpreadManager.FinalizeInit` calls `WorldObject_Traveler.RebuildLiveRegistry()` + `WdPlayerOutpostCache.Invalidate()` so stale static state cannot carry across save loads in one session.
- **Def catalogs (shells, ammo, etc.):** discover once after defs are loaded (lazy first use is fine). Hot paths only read the cached lists and cheap runtime flags (e.g. outpost upgrade tier). Example: assault artillery mortar shells in `RaidLogic/WD_AssaultArtillerySupport.cs` (`EnsureShellCatalog`) — never re-scan shells when building tooltips or dialog rows.
- Underlay raid-target caches are tick-gated (30t), not per-frame. Alert `GetLabel()` strings are cached (no per-frame `Translate`).

## Settings defaults

`Def*` constants on settings are the defaults. Tooltips never hardcode them (`COPY_STYLE.md`).

## Do not copy (sim)

- NPC settlement attack range: call `SettlementAttackRangeUtil.GetNpcSettlementAttackRangeWithZeal`. Player outpost range is a different knob (`raidTargetRadius` + strategist in `Action_Outpost_LaunchAttack`); do not fold it into the NPC util.
- Raid outcome interpolation: `RaidCasualtyModel.GetForecast` / `Resolve`. Do not add a second interpolator.
- Fortify kit layout (r=1 AT, r=2 traps/blocks, ensure road exit, tier `GetKit`): `WorldActions_FortifyKit`. Daily NPC Fortify and Turtle / desperation instant kit share it (T1 included); do not add a second kit interpolator.
- Timed buffs on SpreadManager: Leader / Nimble / Expansionist / coalition are already parallel stacks (`currentWorldLeader`, `currentWeakestUnderdog`, `expansionistZealFaction`, `antiLeaderCoalition*`). Do not add a fifth loose expiry field.
