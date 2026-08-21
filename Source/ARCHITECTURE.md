# Sim architecture

How to use this file: open when adding sim, raids, travelers, daily-loop, or strength numbers. Do not use it for keyed copy (`COPY_STYLE.md`), globe icons (`Core/WORLD_MAP_ICONS.md`), tiles (`Core/PLANET_LAYERS.md`), hub windows (`UI_WINDOWS.md`), or Def XML (`dev/DEFS_GUIDE.md`). Index of all guidance: `dev/GUIDANCE.md`. After a code change that moves an owner, flow, or naming alias, edit this file in the same pass.

World-model ownership (one colony, player-only outposts, NPC holdings = Settlements) lives in the always-on rule `dev/.cursor/rules/wd-product-docs.mdc`. Do not rewrite it here. Tick/alloc/save rules: `dev/.cursor/rules/tsa-world-domination.mdc`.

## Folders

| Folder | Holds |
|--------|-------|
| `Core/` | Comps, snapshot, stats, range, planet guards, overlays |
| `WorldActions/` | Daily orchestrator, growth, roads, traders, incidents, diplomacy, interception |
| `Outposts/` | Player WD outposts, types, actions, warehouse, food logistics |
| `Travelers/` | World travelers, pathing, arrival, Harmony bootstrap |
| `RaidLogic/` | Raid assess/finalize, gate, simulated resolve, colony executor |
| `UI/` | Dashboard, stats, diplomacy, alerts, raid-detail windows |
| `Settings/` | `WorldDominationSettings` + `Settings_Window_*` menus |
| `Patches/` | Dedicated Harmony patches (settlement gizmos, goodwill, overlays) |
| `Buildings/` | Map buildings tied to WD |
| `Compat/` | Optional-mod bridges |
| `Debug/` | Dev/debug helpers |
| `Decontamination/` | Decontamination flow |
| `dev/` | Developer docs and Cursor rules (do not ship to players) |

Also: `RoadBlocks/`, `SpikeTraps/`, `WorldGen/`, `Quests/`, `Gizmos/`. There is no `Letters/` code folder; letter types live with their owners.

## Who owns state

| Class | File |
|-------|------|
| `WorldComponent_SpreadManager` | `WorldActions/WorldActions_Orchestrator.cs` |
| `CompViralSpread` | `Core/CompViralSpread.cs` (on every settlement/outpost) |
| `WorldObject_WD_Outpost` | `Outposts/WorldObject_WD_Outpost.cs` |
| `WorldObject_Traveler` | `Travelers/WorldObject_Traveler.cs` |

Other WorldComponents: interception (`WorldActions/Interception/WorldComponent_InterceptionScheduler.cs`), logistics (`Outposts/FoodLogistics/WD_Outpost_FoodLogistics_Core.cs`), road blocks (`RoadBlocks/WorldComponent_RoadBlocks.cs`), traps (`SpikeTraps/WorldComponent_SpikeTraps.cs`).

## Daily loop

`WorldComponent_SpreadManager.WorldComponentTick` (`WorldActions_Orchestrator.cs`):

1. Once per day (`60000` ticks): `CalculateDailyBudget` → `DailyWorldSnapshot.Build` (`Core/DailyWorldSnapshot.cs`) → diplomacy / revolt / threat → enqueue faction action slots.
2. `ticksUntilNextAction` → `ExecuteNextAction` → `WorldActions_Raid.AttemptRaid` (`RaidLogic/Raid_Manager.cs`) and sibling action attempts.
3. Staggered eval: `pendingRaid.EvaluateNext` → `WorldActions_Raid.FinalizeRaid` spawns a traveler.
4. Arrival: `WD_PathFollower.ArrivalAction` → `WorldActions_Traveler.ExecuteArrival` (`Travelers/WorldActions_Traveler.cs`).

## Raid path

`WorldActions_Raid` (`RaidLogic/Raid_Manager.cs`) → `RaidLaunchGate` (`RaidLogic/RaidLaunchGate.cs`) → traveler → on arrival `Raid_Simulated.ExecuteTravelerRaid` (`RaidLogic/Raid_Simulated.cs`) or colony incident / outpost defense.

SSoT types: `RaidCasualtyModel`, `RaidContribEntry` (`RaidLogic/Raid_MathSnapshot.cs`), `SettlementAttackRangeUtil` (`Core/SettlementAttackRangeUtil.cs`).

## Strength is not one number

Pick one and name it. Do not mix them.

| Kind | Where |
|------|-------|
| Offense pool | `CompViralSpread.offensiveStrength` (alias `strength`) |
| Deployable | `WorldActions_Utils.GetAvailableRaidStrength` = strength minus garrison retain. Wrappers: `GetDeployableOffense`, `RapidResponseUtility.GetDeployableStrength` |
| Ranking total | `CompViralSpread.GetTotalLocalDefensePower` (offensive + defensive). Summed in `WorldStatsUtils` |
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

## GUI perf: never scan AllWorldObjects per frame

Right-side `Alert`s and world-map `WorldComponentOnGUI` run every frame; `AlertsReadout` re-checks active alerts continuously. Do not walk `Find.WorldObjects.AllWorldObjects` in these paths. Read maintained/throttled registries instead:

- **Player outposts:** `WdPlayerOutpostCache.PlayerOutposts` (`Core/WdPlayerOutpostCache.cs`) — throttled snapshot (one scan per ~1800 ticks), self-healing (backwards-clock guard). Used by `Alert_WDOutpostUnusedExperts`, `Alert_WDOutpostNoProduction`, `Alert_WDConstructionInsufficientStrength`, `Alert_WDDropPodDeliveryInAaRange`, and the underlays' player-outpost draw. Consumers still null/`Destroyed`-check each element (a since-destroyed outpost can linger up to one interval; guard `AlertReport.CulpritIs`).
- **Travelers:** `WorldObject_Traveler.LiveTravelers` (maintained in SpawnSetup/Destroy). `WorldComponent_SpreadManager.FinalizeInit` calls `WorldObject_Traveler.RebuildLiveRegistry()` + `WdPlayerOutpostCache.Invalidate()` so stale static state cannot carry across save loads in one session.
- Underlay raid-target caches are tick-gated (30t), not per-frame. Alert `GetLabel()` strings are cached (no per-frame `Translate`).

## Settings defaults

`Def*` constants on settings are the defaults. Tooltips never hardcode them (`COPY_STYLE.md`).

## Do not copy (sim)

- NPC settlement attack range: call `SettlementAttackRangeUtil.GetNpcSettlementAttackRangeWithZeal`. Player outpost range is a different knob (`raidTargetRadius` + strategist in `Action_Outpost_LaunchAttack`); do not fold it into the NPC util.
- Raid outcome interpolation: `RaidCasualtyModel.GetForecast` / `Resolve`. Do not add a second interpolator.
- Timed buffs on SpreadManager: Leader / Nimble / Expansionist / coalition are already parallel stacks (`currentWorldLeader`, `currentWeakestUnderdog`, `expansionistZealFaction`, `antiLeaderCoalition*`). Do not add a fifth loose expiry field.
