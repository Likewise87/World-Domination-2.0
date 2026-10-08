# Sim architecture

How to use this file: open when adding sim, raids, travelers, daily-loop, or strength numbers. Do not use it for keyed copy (`COPY_STYLE.md`), globe icons (`Core/WORLD_MAP_ICONS.md`), tiles (`Core/PLANET_LAYERS.md`), hub windows (`UI_WINDOWS.md`), or Def XML (`Guardrails/DEFS_GUIDE.md`). Index of all guidance: `Guardrails/GUIDANCE.md`. After a code change that moves an owner, flow, or naming alias, edit this file in the same pass.

World-model ownership (one colony, player-only outposts, NPC holdings = Settlements) lives in the always-on rule `Guardrails/.cursor/rules/wd-product-docs.mdc`. Do not rewrite it here. Tick/alloc/save rules: `Guardrails/.cursor/rules/tsa-world-domination.mdc`. **Save compatibility** is a hard priority in those rules — never remove Scribe keys or delete legacy scribed fields.

## Folders

| Folder | Holds |
|--------|-------|
| `Core/` | Comps, snapshot, stats, range, planet guards, overlays |
| `WorldActions/` | Daily orchestrator, growth, roads, traders, incidents, diplomacy, interception |
| `Outposts/` | Player WD outposts, types, actions, warehouse, armory (`Outposts/Armory/`), food logistics. Warehouse Storage tab: All Player Gear-style table (checkbox select, Type/Quality/Count, filterable headers, send amounts default to max), Land/Drop Pod method, regular auto-ship (colony/warehouse dest, per-warehouse Include Apparel/Weapons/Food/Drugs/Medicine flags in `CompOutpostWarehouse.AutoShipIncludes`), ad hoc send through `OutpostStorageShipping.TryLaunch` (armory items may target any player outpost), and Convert to virtual food for checked food rows only. Remote founding (`RemoteOutpostEstablishUtility`): founders from one colony map or one WD outpost; costs/pod components stay `GetPlayerColonyMap` + warehouses. Trading/Recruiting nearby SSoT: `Outpost_Trading` partner collect + type-aware `Outpost_EstablishmentRequirements.MeetsMinNearbySettlements` (hostiles count). Embassy nearby: `Outpost_Embassy.IsEligiblePartnerFaction`. Extra outpost/upgrade XML from Bambaryla absorb: `Defs/WorldObjects/WD_OutpostUpgrades_Bambaryla.xml`, Deepchem Drill (`MayRequire` Vanilla Chemfuel Expanded), Raw Shaping, Chemical Refinery (Biofuel defName) + legacy Deepchem Factory. |
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

| `CompOutpostArmory` | `Outposts/Armory/CompOutpostArmory.cs` (on every outpost) |

Other WorldComponents: interception (`WorldActions/Interception/WorldComponent_InterceptionScheduler.cs`), logistics (`Outposts/FoodLogistics/WD_Outpost_FoodLogistics_Core.cs`), road blocks (`RoadBlocks/WorldComponent_RoadBlocks.cs`), traps (`SpikeTraps/WorldComponent_SpikeTraps.cs`), player pawn favorites (`Core/WorldComponent_PlayerPawnFavorites.cs`), Join Stamp (`Core/WorldComponent_PlayerPawnJoinTimes.cs`), drop-pod crash-evac ambush prime (`Travelers/WorldComponent_DropPodCrashEvac.cs`).

Player pawn drop-pod AA: `WorldActions_Traveler_MortarAa.ResolveRapidResponseDropPodAaHit` (per-pawn hit/kill/crash) + `WdDropPodCrashUtility` / `WorldObject_WD_DropPodCrashSite`. Crash maps reuse `MapComponent_ReinforcementTimer` with a hostile Settlement proxy for ally timing.

## Outpost Ideology slave removal (SELECT vs COMMIT)

SSoT: `OutpostPawnIdeologyUtil` (`Outposts/OutpostPawnIdeologyUtil.cs`).

| Layer | API | Rule |
|-------|-----|------|
| **SELECT** (checkboxes / select-all) | `CanToggleOutpostRemovalSelection` | Free non-slave humanlikes always selectable; slaves (and escort dependents) only when a free occupant is already selected. Leave-behind is **not** checked here. |
| **Prune** | `PruneDependentRemovalSelection` | After selection mutations: drop slaves if no free occupant remains selected. |
| **COMMIT** (transfer / remove / drop-pods enable) | `BulkRemovalSelectionIsAllowed` | Full evacuate OK; else keep ≥1 free on outpost; slaves leaving need a free leaver. Tips via `TryGetBulkRemovalRejectReason`. |

Do not call COMMIT from SELECT gates (`BulkRemovalSelectionIsAllowedWithExtra` forwards to SELECT only).

**Player Ideo assign (Ideology DLC active only):** `ApplyPlayerPrimaryIdeoIfActive` sets the pawn to `Faction.OfPlayer.ideos.PrimaryIdeo` via `SetIdeo`. Call sites: `RecruitPrisonersBatch` (after `SetFaction`) and `Outpost_Recruiting.GenerateRecruitPawn` (Recruiting outpost production and conquest founding pawns). No-op when Ideology is inactive or there is no primary Ideo.

## Outpost Armory (loose gear)

SSoT: `CompOutpostArmory` + `OutpostArmoryUtility` (`Outposts/Armory/`). Opt-out experimental setting `experimentalOutpostArmory`. The Type filter shared by the Armory dialog and All Player Gear (`ArmoryTypeFilter`: Guns / Bows / Melee / Grenades / Ammo / Armor / Headgear / Torso / Legs / Medicine / Drugs / Food / Other) is owned by `OutpostArmoryUtility.TypeFilterFor` (single display kind), `MatchesTypeFilter` (Headgear / Torso / Legs match by apparel coverage and overlap) and `BuildTypeFilterChoices`. Armor is the vanilla `ApparelArmor` category tree (helmets plus body armor). UI must not re-classify.

Two stores side by side, because one model cannot hold both:

| Store | Type | Holds | Scribe key |
|-------|------|-------|------------|
| `Stock` | `List<ThingDefCountClass>` | Everything ordinary, grouped by `def + stuff + quality` | `armoryStock` (non-warehouse) |
| `uniques` | `ThingOwner<Thing>` (`ParentHolder` null, `dontTickContents`) | Items passing `IsIrreplaceable`: bladelink, art, quest tags, generated names, tainted apparel (taint is never cleared) | `armoryUniques` |

**One item store per outpost.** On a warehouse, `CompOutpostArmory.Stock` *is* `CompOutpostWarehouse.storedItems` (`UsesWarehouseStock`), so the Armory dialog and the Warehouse Storage tab read and write the same rows. A warehouse's own `armoryStock` stays scribed for save compatibility but is not the live store. Consequences: armory rows on a warehouse count toward warehouse capacity and auto-ship; `ArmoryRows()` is the filtered view (`IsArmoryItem`) for gear UIs; `GetTotalItemCount` counts gear only; `ClearAndDestroyAll` never clears warehouse stock. Store rows are always plain (`CompOutpostWarehouse.PlainStockRow`): runtime `WdStockExtras` are stripped on deposit so `SameStockIdentity` merges cleanly.

Intake SSoT: `OutpostStorageUtility` (`TryStoreThing`, `DepositRows`, `DepositUniques`). Caravan dissolve, pod arrival, delivery arrival and traveler refunds all go through it: armory first, then warehouse stock; anything neither accepts is refunded to the colony, never destroyed.

Destination SSoT: `Outpost_Warehouse_Delivery.IsValidShipmentDestination(target, sender, rows, uniqueCount, virtualFood, out rejectKey)`. Armory items (gear, food, drugs, medicine, ammo) and uniques may go to any player outpost, colony or warehouse; other goods only to a colony or warehouse; virtual food only to another player outpost. No caller re-implements this.

Food is always stored as items. Nothing auto-converts on arrival or intake. `OutpostFoodConversion.ConvertRows` is the only path to virtual food: the Warehouse tab converts checked food rows at their send amounts, the Armory dialog converts all stored food. Both clamp to `PoolHeadroom` and leave the remainder as items.

Grouping only works because `OutpostArmoryUtility.Normalize` heals to max hit points on deposit, which collapses `CompOutpostWarehouse.StockKey` down to `def + stuff + quality`. Taint and biocoding are never cleared anywhere: tainted or biocoded gear is irreplaceable and kept as a real Thing in `uniques` (repaired, otherwise unchanged). Biocoded items get a "biocoded" tag via `StoredThingTag`; tainted apparel has no label tag but is drawn in `TaintedColor` (yellow) with `TaintedTip` in the Armory and All Player Gear. Under CE, deposit also unloads the magazine (rounds credited as ammo stock) so guns stay stackable.

Occupant moves are map-free: never `TryDropEquipment` (routes through `MapHeld`) and never `Wear(ap, dropReplacedApparel: true)` (silently destroys the replaced apparel when `pawn.Map == null`). Conflicting apparel goes to the store first.

Teardown: `WorldObject_WD_Outpost.Destroy` / `PostRemove` call `ClearAndDestroyAll`; loose stock dies with the outpost and retreat caravans do not evacuate it.

Shipping reuses the goods traveler. `OutpostStorageShipping.TryLaunch` is the one outpost send path (Armory dialog, Warehouse tab, All Player Gear, warehouse auto-ship): it checks the destination rule, then `CanLaunchFrom` (manual defense, pod research, strength), withdraws rows, uniques and virtual food, and rolls all three back if `SpawnDeliveryTravelerFrom` returns false. `ColonyArmoryLaunchUtility.TryLaunch` does the pawn-free colony launch (free, since a colony has no `CompViralSpread` pool); it re-checks map stacks at launch and puts everything back on the map if the spawn fails. All Player Gear strips outpost pawn gear into the store via `TryStoreFromPawn` only after `CanLaunchFrom` passes, so a reject leaves it stored, not lost. Rows at an outpost under manual defense, gear in world caravans, and worn/carried gear on a map under an active hostile assault/siege lord (or an active WD outpost-defense / caravan-clash encounter) are not selectable. Ordinary rows ride `deliveryItems`; uniques ride `cargoUniques` on the traveler. Cargo destroyed in transit is gone, including strength-depletion attrition; only a genuine `AbortTraveler` cancellation refunds (`RefundCargoToOrigin`: outpost origin re-stores via `OutpostStorageUtility`, colony origin drops on the map).

## Outpost occupant skill XP

SSoT: `Outpost_OccupantProgression` (`Outposts/Outpost_OccupantProgression.cs`).

Also owns the once-per-day mothballed passes from `WorldObject_WD_Outpost.Tick`: occupant/prisoner virtual injury heal, stored-animal aging, (when Vehicle Framework is present) stored-vehicle component repair via `VehicleFrameworkOutpostDissolveCompat.TryRepairVehicleOneDay` (percent of each damaged part's MaxHealth), and (when Combat Extended is active and experimental opt-out is on) CE basic Primary ammo via `OutpostCeAmmoCompat` / `TickOccupantsPassiveAmmoOneDay` (2 magazines/day, inventory cap 8).

| Source | When | Amount | Skills |
|--------|------|--------|--------|
| Production cycle payout | Successful recruiting / trading / embassy / scavenging / item delivery | Settings `outpostOccupantSkillXpPerProductionCycle` (Def 5000) via `ApplyPayoutSkillXp` | `GetRelevantSkillDefs` |
| Academy lesson | Academy cycle complete | Academy lesson XP only — **no** `ApplyPayoutSkillXp` | Selected teaching skill |
| Research | Once per active research day (`TickResearch` + `lastResearchSkillXpDay`) | Flat 500 | Intellectual |
| Mortar | Committed ground shot (manual / defensive) | Flat 500 | Shooting |
| Anti-air | Once per engagement volley (`AntiAirFireUtils.ExecuteEngage`) | Flat 500 | Shooting |
| Rapid Response win | World clash win (3 resolve paths; `rapidResponseWinXpGranted` guard) | Flat 500 each | Shooting + Melee |
| Build complete | Successful outpost-origin road / block / trap / AT / decontam work | 300 / 500 / 700 by tier | Construction |
| Recruit resistance | Daily prisoner recruit tick when any resistance is reduced (`OutpostPrisonerUtility.TickPrisonerRecruitmentOneDay`) | Pool = reduced × settings `outpostRecruitSocialXpPerResistance` (Def 400); equal split via `ApplySkillXpPoolSplit` among uncapped humanlikes | Social |
| Outpost upgrade | — | None | — |

Humanlike occupants only; respects `outpostOccupantSkillXpMaxLevel`. Most event amounts are code constants; recruit Social XP is settings-driven (pool per resistance reduced). Colony world-build and clearing/removal projects grant no Construction event XP.

## Daily loop

`WorldComponent_SpreadManager.WorldComponentTick` (`WorldActions_Orchestrator.cs`):

1. Once per day (`60000` ticks): `CalculateDailyBudget` → `DailyWorldSnapshot.Build` (`Core/DailyWorldSnapshot.cs`; enumerates **WD participants** only via `IsWdParticipant`) → revolt / forward assault (Vanguard|Invasion) / **Isolation Pressure** (`WorldActions_IsolationPressure`: when fewer than 3 hostile factions threaten the player, one faction not on per-faction CD may be forced to found 1–2 settlements 10–15 tiles from the player; chance Def 40%; stage gate Def Always; founding raid+defense shields) / diplomacy / threat → enqueue faction action slots. Special-event cooldowns: `WorldActions_SpecialEventCooldown` (war / revolt / forward assault; optional shared). **Strategy on Settlement Loss** is reactive (`WorldActions_DesperationRaid.NotifyNpcSettlementLost`): cluster pressure (hostile offense within 15 tiles of any cluster site (membership edge 20) ÷ cluster offense) + min size + `gateThreatDesperation` (Def Always; **not** set by Easy/Medium/Hard presets) → `settlementLossStrategyFireChance` (Def 40%; fail does not stamp CD) → fork Turtle vs desperation by equal-share relative strength × likelihood slider; shared anti-spam CD on `desperationRaidCooldownByFaction` (Def 2 days). **After Strategy**, `WorldActions_Utils.TryMarkDefeatedIfNoSettlementsLeft` sets vanilla `faction.defeated` when that loss emptied the faction (skips mid-refound PackUp/Turtle/AssaultRally travelers); `WorldStatsUtils` keeps defeated zero-rows on World Stats / dashboard rank (`GetLivingNpcStrengthTotals` still skips them for sim N). Load: `ClearFalseDefeatedFlags` on `FinalizeInit(fromLoad)` (heal stuck defeated while surface settlements remain); true-wipe mark only on settled `ExecuteWhenFinished` bootstrap (`MarkTrueWipeDefeatedFlagsIfSettled`) - never mass-mark in early FinalizeInit. Reactive Turtle uses ally radius (migrate outside / fortify-in-place if zero migrants). Daily Turtle stays separate (`WorldActions_Turtle.TryTrigger`: leaves need offense ≥400; pack into existing dig-ins — primary, T3/T4, up to 2 reserved strong T2 vessels — preferring T3-cap room for multi-T3 consolidate / ally reinforcements; rare last-resort T4-cap overflow on existing hubs; no turtle founding). Incident obliteration does not start Strategy (but still marks defeated if last site). **NPC Fortify** (`WorldActions_NpcFortify`): threatened settlements build the shared turtle kit (`WorldActions_FortifyKit`) locally in phases (road traps + ensure road exit → road blocks → AT turrets); Turtle / desperation place the full kit instantly via `TryPlaceFortifyKit` (T1: Spike + Light block + 1 Light AT). If Fortify is picked but cannot launch, orchestrator re-rolls once among other eligible weights. **Reactive loss bus and daily threats require `ProgramState.Playing`** (no Strategy / FA / Turtle / action queue during world gen or Select Starting Site). Vanguard packs 5–7 far sites and mass-relocates; Invasion reuses the same pick gates but launches 5–7 coordinated raids with homes staying (`WorldActions_Raid.TryLaunchCoordinatedInvasionRaid`). Pack→rally→absorb→Raid for Desperation: `WorldActions_AssaultRally` (`TravelerMission.DesperationRally`; `isDesperationRaid`). Legacy in-flight Invasion pack/rally travelers still resolve via AssaultRally / pack-up refound.
2. `ticksUntilNextAction` → `ExecuteNextAction` → `WorldActions_Raid.AttemptRaid` (`RaidLogic/Raid_Manager.cs`) and sibling action attempts.
3. Staggered eval: `pendingRaid.EvaluateNext` → `WorldActions_Raid.FinalizeRaid` spawns a traveler.
4. Arrival: `WD_PathFollower.ArrivalAction` → `WorldActions_Traveler.ExecuteArrival` (`Travelers/WorldActions_Traveler.cs`).

### Mid/Late attrition rest

When `gateThreatAttritionRest` passes (`WdEscalation.PassesGate`, Def FromMid), walking travelers that hit `attritionRestMinRatio` of `initialStrength` from **attrition only** stop (`StopDead`), regenerate toward 100% of initial (`TravelerAttritionRest`), then `StartPath` again. Suppress begin-rest if recently hit by mortar/AT/AA (`lastHostileFireTick` + fire-grace days), a hostile AT can engage them, or remaining path ETA ≤ near-dest buffer (Def 0.1 days). Hostile strength damage while resting (`TravelerAttritionRest.NotifyHostileFire`: mortar/AT/AA shells, open-field traveler/caravan/AT clashes, spike traps) cancels rest and resumes the journey immediately; resting travelers also leave if a hostile AT becomes able to engage. Attrition still clamps at the rest floor when rest is suppressed; combat/traps/pollution can push live strength below without snapping up. Forecast helpers in `TravelUtils` (`GetMinTravelEfficiency` via `TravelerAttritionRest`) floor predicted efficiency at the rest ratio so raid gates / projected arrival assume ≥80% when the feature is active. Regen Def is 2%/hour (~10h from 80% to 100%). V1 has no special rest-camp clash map.

## Caravan clash (player vs traveler)

Temporary vanilla `Ambush` map + `WD_MapComponent_CaravanClash` tracker (`Travelers/WD_MapComponent_CaravanClash.cs`). Start: `WD_CaravanClashUtility.StartInterceptionEncounter`. Win: notify only — player exits via vanilla reform caravan; enemy cleanup + Ambush destroy on `MapRemoved`. Defeat / unresolved map close: always `RespawnNewTraveler` when `encounterActive && !playerHasWon` (do not trust dying-map hostile lists), then Ambush teardown. Aerial/shuttle leave (Odyssey `PassengerShuttle` / VF aerial / pods): shared bus `Travelers/WD_TempEncounterAerialLeaveUtility.cs` fans launch + world-airborne hooks to clash and outpost defense (`CaravanClashAerialFleeHooks` + AA leave events). Clash: lose + pawns survive (`playerFled`); Ambush teardown waits until the escape craft leaves. Outpost defense (`RaidLogic/WD_MapComponent_OutpostDefense.cs`): same Phase A/B leave (standing = Spawned on-map borrowed); win absorbs extras + landed shuttle into the outpost; lose after flee keeps the map until craft is gone. Stored transport / VF on defense maps: footprint spawn with 2-cell pad (`WD_OutpostDefenseEncounterUtility`); if any hull lands outside the wall ring, attacker arrival gets +10s. No second clash while a loaded Ambush clash map occupies the tile (`TileHasBusyCaravanClashAmbush`). Mid-fight drafted map-edge auto-caravan exit is blocked on active clash and outpost-defense maps (`Patches/Patch_WdTempEncounterExitMap.cs` + `Travelers/WD_TempEncounterExitMapUtility.cs`) by denying leave actions — not by patching `ExitMapGrid.MapUsesExitGrid` (that getter is UI-hot via `ExitMapGridUpdate`). Colony homes and NPC settlement attacks stay vanilla. WD clash also suppresses vanilla `CaravansBattlefield.CheckWonBattle` for the Ambush tracker lifetime so empty pre-raid maps cannot latch WonBattle or double-letter after WD victory.

**Player Camp walk-over:** same tracker on the existing Camp map (`StartCampClashEncounter` / `TryInterceptRaidAtPlayerCamp`). No Ambush site and no Ambush teardown. First fortress-choke-eligible hostile column with a living player pawn on the camp fights there; further walkers pass while `TileHasBusyCampClash`. Player win: traveler stays dead (leftovers discarded on Camp). Player loss / flee / camp map removed mid-fight: `RespawnNewTraveler` continues the mission. Bridged-water camps set `IsBridgeClash` for spawn steering and re-apply `ApplyEncounterApproaches` from the traveler’s last world tile.

**Rapid Response pawn pods:** drop onto any loaded map on the tile (clash, camp, outpost defense, colony). Intercept (target is a traveler): tag origin; temp-map teardown returns living and downed tagged pawns to that Rapid Response. Dest outpost: extras of that outpost (join on win; loss uses extra survivor rules). Dest gone mid-flight: caravan on the tile. No map + ballistic enemy: land as a caravan, enemy keeps flying. Never `AddPawn` into a virtual garrison while `ManualDefenseActive` with no map.

## Raid path

`WorldActions_Raid` (`RaidLogic/Raid_Manager.cs`) → `RaidLaunchGate` (`RaidLogic/RaidLaunchGate.cs`) → traveler → on arrival `Raid_Simulated.ExecuteTravelerRaid` (`RaidLogic/Raid_Simulated.cs`) or colony incident / outpost defense. On non-ballistic hops, `WD_PathFollower` same-tile choke diverts eligible ground columns (`IsFortressChokeEligibleMission`: Raid / MassRelocation / DesperationRally / TurtleConsolidate) that step onto a player Mortar or Rapid Response outpost (`TryInterceptRaidAtFortressOutpost`), the player colony tile (`TryInterceptRaidAtPlayerColony` → colony map raid), or a player Camp with living pawns (`TryInterceptRaidAtPlayerCamp` → camp map clash, resume on loss). Feature B marauding after a drop-pod first strike continues as a walking `Raid` (pods are one-shot). WD colony / outpost / interception / reinforcement raids pin `ImmediateAttack` then `WdRaidParmsUtility.EnsureFactionCompatibleRaidParms` may swap to a faction-accepted Combat strategy and derive arrival from that strategy’s `arriveModes` (no curated mapping). Empty outpost launches abort without auto-victory.

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

## Forward Assault Vanguard

SSoT: `WorldActions_Vanguard` + `WorldActions_PackUp` + `WdSettlementClusterUtility` (+ `WorldActions_AssaultRally` geo partition / rally math).

- **Seed:** player front outpost facing the pack if far enough from the colony; else colony annulus toward the pack (`vanguardClusterMin/MaxDistFromColony`). Colony keep-out on reserved tiles.
- **Fewer sites:** pack still 5–7 homes; reserve `vanguardFoundSitesMin/Max` (Def 2–3) tiles with **peer distance 2** (one free tile between). No outpost `MinDistanceTiles` pass for Vanguard.
- **Geo columns:** `PartitionAssemblies` (path-cap local clusters) → assign assemblies to dig-ins (closest unused, reuse when columns exceed sites). Per home: destroy → MassRelocation on that tile (no teleport fold). Multi-source: local rally → host wait/absorb → march to dig-in. Solo: straight to dig-in.
- **Orphan absorb:** same-tile rally orphans only (`TickVanguardRallyHost`); `vanguardMergeRadiusTiles` is legacy UI (not mid-route fold).
- **Arrival:** absorb into existing same-faction `subType=Vanguard` settlement within 1 tile; else found. Redirect/refound uses the same tight blocker pad.

## Player world-build materials

SSoT for costs: `Defs/WorldBuild/` (`WD_Roads.xml`, `WD_AT_Turrets.xml`, `WD_RoadBlocks.xml`, `WD_SpikeTraps.xml`). Runtime gate/format: `ColonyWorldBuildRequirements`. Stock check/deduct (colony map + warehouses): `ColonyWorldBuildMaterials` (shared with outpost upgrades). Charge once per player crew launch (not on clear, not NPC Fortify). Progress ≥ 100% stalls on missing materials like strength; inspect/gizmo use `GetInsufficientConstructionMessage`; alert `Alert_WDConstructionInsufficientMaterials`.

**Build Bridge** (water spans): separate tool from Build Road (`Action_Outpost_BuildBridge` / `WorldActions_BuildBridge`). Targeting is bank-to-bank water corridor (≤10 `WaterCovered` tiles; `MaxBridgeTargetRange = 11`). Valid banks are collected once and drawn with `WD_WorldLayer_BridgeTargetFill` (not the outpost ApproxDistance disk). Incomplete stub banks stay clickable for resume; reserved overlay is complete spans plus other active projects. Always paints **TSA_WD_StoneBridge** on water only (`tilesPerSegment` is not 25 so vanilla world gen cannot steal inland stone routes). Land-land links wrongly painted as stone bridge are rewritten to StoneRoad on world init. Materials: AnyStoneBlocks at **2×** stone-road segment cost **per water tile**, deducted when **that segment’s crew** launches via `TryFinalizeMaterialsOrAbort` (stored on traveler); targeting does not require stone for the remaining span (stall at 100% like other world-build). Refunded on clean abort only if `constructionWorkApplied` is still false (kill → no material refund). Bridged-tile pathgrid base stays land-like (`~1f` + winter) so the stone movement multiplier is not double-dipped with stone’s `0.5` base. Targeting **persists** the span on `CompViralSpread` (`bridgeProgress`, `cachedWorkTile`); outpost ticks accrue at **stone-road (T2) duration per water tile** (not per land-water edge). Arrival paints that water tile’s chain edges (back edge plus far-bank close when the water sits on land). In-flight **legacy** crews whose `cachedPathTiles` still equal the full span paint remaining edges at once. Membership: `WorldComponent_WdBridges` (O(1) `IsBridgedWaterTile`). Pathing: Harmony on `CalculatedMovementDifficultyAt` + `SurfaceTile.Roads` (no biome swap). Draw: `WD_WorldLayer_BridgeRoads`. Blocks/traps allowed on bridged water; AT turrets forbidden.

**Deconstruct Bridge**: menu leaf next to Build Bridge; one-click either bank walks a **partial stub** (`TryResolveExistingBridgeFromBank`). Chain is oriented near-outpost first; teardown peels **from the far end**, one water tile per T2 crew. Occupancy on **that water tile** (world objects / busy ambush/camp — not blocks/traps/banks). Occupied → cancel that crew, leave remaining links, yellow `LetterDefOf.NeutralEvent` (toggle `notifyBridgeDeconstructBlocked`, default on). Crews may path **over water** (bridged or not) along the span. Each unlink wipes forts on those water tiles, `RemoveRoadLink`, registry/path costs, then `WdBridgeTravelerImpact` for those tiles. Cancelled **build** stubs are torn down with this tool.

**Construction abort refunds** (shared): `ColonyWorldBuildRequirements.RefundConstructionAbort` — strength always; materials iff costs stored and work not applied. Used by `CancelMission` and DestroyActive* for road / road block / spike trap / AT / bridge build.

Caravan clashes **and Camp** on bridged water use `MapGeneratorDef` `WD_BridgeClash` (`GenStep_WD_BridgeClash`: ocean + granite bridge deck). Deck axis uses mortar/FRD tangent-plane world→local axes, snapped to the nearest **22.5°**. Player/hostile spawn ends follow each caravan’s world approach tile. Centerline may drift ±1 cell, every 40–70 cells between tilts. Ancient 2×1 barriers only on cardinal decks; diagonals use 1×1 rails to the map ends (entry/exit faces stay open). Layout stored on `MapComponent_WdBridgeClashLayout`. Clash map gen owned next to `WD_CaravanClashUtility`; Camp override via `Patch_WdBridgeCamp`. Abandoned camp leftovers: vanilla 30-day `TimeoutComp` shortened to 5 days (10 hours on bridged water) via `Patch_AbandonedCampTimeout`.

## NPC settlement subtypes (tile-aware)

SSoT: `NpcSettlementSubtypeUtil` (`Core/NpcSettlementSubtypeUtil.cs`), called from `CompViralSpread.GetRandomSubType` with `parent.Tile`.

- **Camp** (`subType` `Camp`): mixed everyday layouts. Normal (non-extreme) tiles only; T1/T2 pool member.
- **Refuge** (`subType` `Refuge`): stripped barracks/kitchen/stockpile layouts. Extreme tiles only (farming fertility 0 and plant-density rank below logging floor). T1 extreme pool + Mining if hills; T2 extreme is Refuge only (no Production / Slavery). Unknown / unset tile is never treated as extreme.
- **Bootstrap:** `WorldObjectMaker` runs `CompViralSpread.Initialize` before callers set `Tile`, so NPC settlements set `npcSettlementBootstrapPending` + a provisional subtype (never blank). `TryCompleteNpcSettlementBootstrap` (CompTick / `EnsureAllSettlementsInitialized` / PostLoadInit / inspect) applies tile-aware `ApplyRandomTier` or subtype re-pick once `Tile` is valid. Invariant: participant NPC settlements never keep an empty `subType` after Tile is known.
- **Specialty gates:** Farming fertility ≥ 30%; Logging fertility ≥ 15%; Mining base score ≥ 0.5 (SmallHills+). Only enter the T1 pool when the tile passes.
- **Layout resolve:** `Patch_KCSG` / `WdMgNestSpawner` build `TSA_{Tribal|Generic}_{tier}_{token}`. `LayoutTokenForSubtype` maps scribed `Slavery` → `Prison` SettlementLayoutDef names. Loot tables still key on `Slavery`.
- **Display:** keyed `TSA_WD_SubType_*` (Slavery / Prison alias → Prison Village; Camp / Refuge / specialties / Fortress / Citadel / Vanguard / Generic) via `GetSubtypeInspectLabel`.
- **Expand seed bias (early annulus only):** score ~3 ring tiles with `ExpandTileAttractiveness` (max fertility / hunting / mining), then one `TryFindFirstValidFromSeed` from the best. Mid/late toward-player and isolation stay first-valid. Landed tile’s `PickSubtype` sets the settlement type.

## Mid / Late escalation

SSoT: `WdEscalation` (`Core/WdEscalation.cs`) + latched state on `WorldComponent_SpreadManager`.

- **Candidate** (metrics only): Mid/Late when **any** of share, absolute outpost strength, or (if enabled) elapsed days (`TicksGame / TicksPerDay`) meets that stage’s threshold. Day OR is gated by `enableMidGameDaysThreshold` / `enableLateGameDaysThreshold`. Master switch: `enableLateGameScaling`.
- **Latch**: `escalationStageLatch` never decreases. Active stage = latch (candidate cannot drop Mid/Late once reached).
- **First apply seed**: when `!escalationLatchInited`, seed latch + `escalationLetterNotifiedStage` from the candidate with **no** letter (old saves / day-0 new games).
- **Letters**: one-shot `LetterDefOf.NegativeEvent` when notified floor rises; body from `BuildStageLetterText` (intro + `BuildActiveEffectsTooltip`). Debug Force Mid/Late always re-letters.
- **Debug Force Early**: clears latch + caches to None. Next metrics pass may re-raise Mid/Late from thresholds (raise settings if you want to stay early).
- Master switch off clears **cached** Mid/Late effects only; latch and letter floor stay.

## Naming (UI vs code)

Code IDs stay the old names. UI strings are the new ones.

| UI | Code |
|----|------|
| Nimble | `activeUnderdogs`, `underdogBuff*`, per-faction CD maps, `enableUnderdogBuff` |
| Expansionist | `expansionistZealFaction`, `expansionistZealExpiryTick`, `enableExpansionistZeal` |
| Warden | `OutpostExpertRole.Recruiter`, `expertRecruiterThingId` |

Outpost expert slot unlock auto-assign (`autoAssignExpertsOnSlotUnlock`, default on) is owned by `OutpostExpertUtility.TryAutoAssignOnSlotUnlock`, toggled per outpost from `WITab_Outpost_Experts`; runs from `WorldObject_WD_Outpost.NotifyVirtualPawnsChanged` when `GetMaxExpertSlots` increases (covers `AddPawn`, prisoner recruit-in-place, and other occupant changes). Load primes a baseline so existing open capacity is not backfilled.

## Harmony

`HarmonyLoader` (`Travelers/DisableMemoryLeakWarning.cs`) scans the assembly for static `[HarmonyPatch]` classes. Settlement gizmos go through `Patch_SettlementGetGizmos` (`Patches/Patch_SettlementGetGizmos.cs`). Caravan gizmos go through `Patch_CaravanGetGizmos` (`Patches/Patch_CaravanGetGizmos.cs`). Do not add a second `Settlement.GetGizmos` or `Caravan.GetGizmos` postfix.

**Always-show world icons (do not regress):** `Patches/Patch_WdWorldObjectNoExpandingIcon.cs` keeps the upright ExpandingIcon at every zoom (no Material swap) for settlements / outposts / travelers / AT when the Experimental toggles are on. VeryClose blanks were a false `HiddenBehindTerrainNow` plus a wrong world-origin Dot (layer-origin facing + `TransitionPct` forced to 1); a remaining VeryClose blank with gates still saying would-draw is fixed by a `GUI.DrawTexture` re-blit after vanilla OnGUI that also applies `ExpandingIconRotation` (AT turrets, shells). Full rules and anti-patterns: `Core/WORLD_MAP_ICONS.md`. Do not “fix” Close/VeryClose by restoring Material for ForceFixedIcon objects.

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

- **Player outposts:** `WdPlayerOutpostCache.PlayerOutposts` (`Core/WdPlayerOutpostCache.cs`) — throttled snapshot (one scan per ~1800 ticks), self-healing (backwards-clock guard). Used by `Alert_WDOutpostUnusedExperts`, `Alert_WDOutpostNoProduction`, `Alert_WDConstructionInsufficientStrength`, `Alert_WDConstructionInsufficientMaterials`, `Alert_WDDropPodDeliveryInAaRange`, and the underlays' player-outpost draw. Consumers still null/`Destroyed`-check each element (a since-destroyed outpost can linger up to one interval; guard `AlertReport.CulpritIs`).
- **Travelers:** `WorldObject_Traveler.LiveTravelers` (SpawnSetup registration is idempotent; Destroy removes all registrations). `WorldActions_Orchestrator.FinalizeInit` calls `RebuildLiveRegistry()` (and again after remnant cleanup on load) + `WdPlayerOutpostCache.Invalidate()` so stale static state cannot carry across save loads in one session.
- **AT turrets:** `WorldObject_AT_Turret.LiveTurrets` (same SpawnSetup / Destroy / FinalizeInit rebuild). Caps, `HostileAtCanEngage`, and NPC nearest-AT pick this list — not `AllWorldObjects`.
- **Bridge / AT construction queues:** `WorldConstructionProjectRegistry` (comps with `bridgeSpanTiles` length >= 2 or `atTurretPlannedTiles`). Lock/reserve walks it; invalidate on commit/clear, rebuild on FinalizeInit. Do not use the 30s `WdPlayerOutpostCache` for these locks. Crew abort scans `LiveTravelers` by origin + mission.
- **Def catalogs (shells, ammo, etc.):** discover once after defs are loaded (lazy first use is fine). Hot paths only read the cached lists and cheap runtime flags (e.g. outpost upgrade tier). Example: assault artillery mortar shells in `RaidLogic/WD_AssaultArtillerySupport.cs` (`EnsureShellCatalog`) — never re-scan shells when building tooltips or dialog rows.
- Underlay raid-target caches are tick-gated (30t), not per-frame. Alert `GetLabel()` strings are cached (no per-frame `Translate`).
- **Hub list tables (many rows):** record the scroll outer height (`lastScrollViewportHeight`); in `DrawRow`, early-return when the row Y is outside `[scrollPos.y - rowH, scrollPos.y + viewportH)`. Keep full `viewRect` height so the scrollbar still matches the list. Never call `Translate(args)` inside `DrawRow` — cache those strings on the row at rebuild (or on the window when the filter/destination changes). Pattern: `Window_AllPlayerPawns`, `Window_AllPlayerGear`. A `MaxRows` collect/sort cap is separate from draw cost.

## Settings defaults

`Def*` constants on settings are the defaults. Tooltips never hardcode them (`COPY_STYLE.md`).

## Settlement tier promotion

SSoT for **promotion** (NPC settlements): Develop (`WorldActions_GrowthExpand.TryUpgrade`), Turtle deposit (`CompViralSpread.DepositStrengthWithRegionalPromotes`), and Gift/Buy/Bribe investment (`TryPromoteTierFromInvestment`). All share `CanPromoteNextTierRegionally` (`localMaxT*` within `expandMaxRadius`). Develop also requires same-tier neighbors. Strength refunds and `AddStrength` never promote (clamp to current tier max). `CheckTierUpdate` demotes only when asked. Pack-up founding via `SetState(massRelocationTier)` and worldgen are separate and can still place T4s without that regional gate.

## Do not copy (sim)

- NPC settlement attack range: call `SettlementAttackRangeUtil.GetNpcSettlementAttackRangeWithZeal`. Player outpost range is a different knob (`raidTargetRadius` + strategist in `Action_Outpost_LaunchAttack`); do not fold it into the NPC util.
- Raid outcome interpolation: `RaidCasualtyModel.GetForecast` / `Resolve`. Do not add a second interpolator.
- Fortify kit layout (r=1 AT, r=2 traps/blocks, ensure road exit, tier `GetKit`): `WorldActions_FortifyKit`. Daily NPC Fortify and Turtle / desperation instant kit share it (T1 included); do not add a second kit interpolator.
- Timed buffs on SpreadManager: Leader, Nimble (multi-faction set + per-faction CDs), Expansionist, and coalition use separate fields (`currentWorldLeader`, `activeUnderdogs`, `expansionistZealFaction`, `antiLeaderCoalition*`). Leader and Nimble are mutually exclusive for one faction (nimble clears leader; leader will not apply onto an active underdog). Coalition/zeal stay independent; if the coalition target becomes nimble, remaining coalition duration clamps to 3 days. Do not add a fifth loose expiry field.
