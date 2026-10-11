using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Playing-time world debug helpers for <see cref="Dialog_WdWorldDebug"/>.
    /// Uses debug-neutral Messages (not <c>TSA_WD_WorldSetup_*</c>).
    /// </summary>
    public static class WD_WorldDebugOps
    {
        private enum DebugRaidType : byte
        {
            Land,
            DropPod,
            Gravship
        }

        private static readonly float[] RaidStrengthPresets = { 100f, 250f, 500f, 1000f, 2000f };

        private static int pendingRoadFromTile = -1;
        private static int pendingBridgeFromTile = -1;
        private static readonly HashSet<int> BridgeBankScratch = new HashSet<int>();
        private static readonly Dictionary<int, List<int>> BridgeChainScratch = new Dictionary<int, List<int>>();
        private static readonly HashSet<int> BridgeReservedScratch = new HashSet<int>();

        public static string ActiveToolLabel { get; private set; }

        public static void TickClearIfIdle()
        {
            if (ActiveToolLabel == null) return;
            if (Find.WorldTargeter != null && Find.WorldTargeter.IsTargeting) return;
            if (Find.WindowStack != null && Find.WindowStack.IsOpen<FloatMenu>()) return;
            ActiveToolLabel = null;
        }

        public static void CancelActive()
        {
            if (Find.WorldTargeter != null && Find.WorldTargeter.IsTargeting)
                Find.WorldTargeter.StopTargeting();
            FloatMenu menu = Find.WindowStack?.WindowOfType<FloatMenu>();
            if (menu != null)
                Find.WindowStack.TryRemove(menu);
            pendingRoadFromTile = -1;
            pendingBridgeFromTile = -1;
            WD_WorldLayer_BridgeTargetFill.Hide();
            BridgeBankScratch.Clear();
            BridgeChainScratch.Clear();
            ActiveToolLabel = null;
        }

        public static bool TryToggleOff(string label)
        {
            if (ActiveToolLabel != label) return false;
            CancelActive();
            return true;
        }

        /// <summary>Start a multi-click world tool that runs <paramref name="tool"/> (uses mouse tile) on each click.</summary>
        public static void BeginRepeatClick(string label, string hint, Action tool)
        {
            if (TryToggleOff(label)) return;
            CancelActive();
            ActiveToolLabel = label;
            if (!hint.NullOrEmpty())
                Messages.Message(hint, MessageTypeDefOf.NeutralEvent);
            Find.WorldTargeter.BeginTargeting(
                _ =>
                {
                    tool?.Invoke();
                    return false;
                },
                true,
                null,
                false,
                null,
                null,
                null);
        }

        /// <summary>Run an action that itself opens menus / targeters; tracks label for toggle-off.</summary>
        public static void BeginAction(string label, Action begin)
        {
            if (TryToggleOff(label)) return;
            CancelActive();
            ActiveToolLabel = label;
            begin?.Invoke();
        }

        public static void RunInstant(Action action)
        {
            CancelActive();
            action?.Invoke();
        }

        public static void BeginPlaceAdvancedBuilding()
        {
            const string label = "Place advanced building";
            if (TryToggleOff(label)) return;
            CancelActive();
            ActiveToolLabel = label;

            var opts = new List<FloatMenuOption>();
            void Add(AdvancedBuildingDef def)
            {
                if (def == null) return;
                AdvancedBuildingDef captured = def;
                opts.Add(new FloatMenuOption(captured.LabelText,
                    () => BeginPlaceAdvancedBuildingTargeting(captured)));
            }
            Add(AdvancedBuildingDefOf.TSA_WD_AdvBuilding_Silo);
            Add(AdvancedBuildingDefOf.TSA_WD_AdvBuilding_Hospital);
            Add(AdvancedBuildingDefOf.TSA_WD_AdvBuilding_WatchTower);
            Add(AdvancedBuildingDefOf.TSA_WD_AdvBuilding_Stable);
            Add(AdvancedBuildingDefOf.TSA_WD_AdvBuilding_MortarTurret);
            Add(AdvancedBuildingDefOf.TSA_WD_AdvBuilding_AaTurret);
            if (opts.Count == 0)
            {
                Messages.Message("WD debug: no advanced building defs loaded.", MessageTypeDefOf.RejectInput);
                ActiveToolLabel = null;
                return;
            }
            DebugActions_FloatMenus.OpenCentered(opts);
        }

        private static void BeginPlaceAdvancedBuildingTargeting(AdvancedBuildingDef def)
        {
            Messages.Message(
                $"WD debug: click an NPC settlement to place a {def.LabelText} nearby. Right-click or Esc to stop.",
                MessageTypeDefOf.NeutralEvent);
            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    if (!TryGetSurfaceTile(target, out int tile)) return false;
                    Settlement s = SettlementAtTile(tile);
                    if (s == null || s.Faction == null || s.Faction.IsPlayer)
                    {
                        Messages.Message("WD debug: click an NPC settlement (not player).", MessageTypeDefOf.RejectInput);
                        return false;
                    }
                    if (WorldComponent_AdvancedBuildings.Get()?.Has(s, def) == true)
                    {
                        Messages.Message($"WD debug: {s.LabelCap} already owns a {def.LabelText}.", MessageTypeDefOf.RejectInput);
                        return false;
                    }
                    if (!WorldActions_AdvancedBuildings.FindBuildTile(s, def, out int buildTile))
                    {
                        Messages.Message("WD debug: no buildable tile within range.", MessageTypeDefOf.RejectInput);
                        return false;
                    }
                    var b = WorldActions_AdvancedBuildings.Place(def, s, buildTile);
                    Messages.Message(
                        b != null
                            ? $"WD debug: placed {def.LabelText} for {s.LabelCap} on tile {buildTile}."
                            : "WD debug: place failed.",
                        b != null ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput);
                    return false;
                },
                true,
                null,
                false,
                null,
                null,
                t =>
                {
                    if (!TryGetSurfaceTile(t, out int tile)) return false;
                    Settlement s = SettlementAtTile(tile);
                    return s != null && s.Faction != null && !s.Faction.IsPlayer;
                });
        }

        public static void BeginDestroyRoad()
        {
            BeginRepeatClick(
                "Destroy road",
                "WD debug: click a tile to strip land road links. Right-click or Esc to stop.",
                () =>
                {
                    int tile = GenWorld.MouseTile();
                    if (tile < 0 || !PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(tile))
                    {
                        Messages.Message("WD debug: invalid tile.", MessageTypeDefOf.RejectInput);
                        return;
                    }
                    if (!WD_WorldRoadEditUtility.TryRemoveRoadsAtTile(tile, out int removed) || removed <= 0)
                    {
                        Messages.Message("WD debug: no road on that tile.", MessageTypeDefOf.RejectInput);
                        return;
                    }
                    Messages.Message($"WD debug: removed {removed} road link(s).", MessageTypeDefOf.PositiveEvent);
                });
        }

        public static void BeginDestroyBridge()
        {
            BeginRepeatClick(
                "Destroy bridge",
                "WD debug: click a bank or water tile to destroy a bridge. Right-click or Esc to stop.",
                () =>
                {
                    int tile = GenWorld.MouseTile();
                    if (tile < 0 || !PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(tile))
                    {
                        Messages.Message("WD debug: invalid tile.", MessageTypeDefOf.RejectInput);
                        return;
                    }
                    if (!WD_WorldRoadEditUtility.TryDestroyBridgeAtTile(tile, out string fail))
                    {
                        Messages.Message(fail.NullOrEmpty() ? "WD debug: no bridge there." : ("WD debug: " + fail),
                            MessageTypeDefOf.RejectInput);
                        return;
                    }
                    Messages.Message("WD debug: bridge destroyed.", MessageTypeDefOf.PositiveEvent);
                });
        }

        public static void BeginPlaceRoad()
        {
            const string label = "Place road";
            if (TryToggleOff(label)) return;
            CancelActive();
            ActiveToolLabel = label;

            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Dirt road", () => BeginPlaceRoadTargeting(SettlementTier.T1)),
                new FloatMenuOption("Stone road", () => BeginPlaceRoadTargeting(SettlementTier.T2)),
                new FloatMenuOption("Asphalt road", () => BeginPlaceRoadTargeting(SettlementTier.T3))
            };
            DebugActions_FloatMenus.OpenCentered(opts);
        }

        private static void BeginPlaceRoadTargeting(SettlementTier tier)
        {
            pendingRoadFromTile = -1;
            RoadDef road = WD_WorldRoadEditUtility.ResolveRoadDef(tier);
            if (road == null)
            {
                Messages.Message("WD debug: road def missing for that tier.", MessageTypeDefOf.RejectInput);
                ActiveToolLabel = null;
                return;
            }

            Messages.Message(
                "WD debug: click start tile, then end tile to place a road path. Right-click or Esc to stop.",
                MessageTypeDefOf.NeutralEvent);
            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    if (!TryGetSurfaceTile(target, out int tile)) return false;
                    if (pendingRoadFromTile < 0)
                    {
                        pendingRoadFromTile = tile;
                        Messages.Message("WD debug: click end tile.", MessageTypeDefOf.NeutralEvent);
                        return false;
                    }

                    int from = pendingRoadFromTile;
                    pendingRoadFromTile = -1;
                    if (!WD_WorldRoadEditUtility.TryPlaceRoadAlongPath(from, tile, road, out string fail))
                    {
                        Messages.Message(
                            fail.NullOrEmpty() ? "WD debug: no path between tiles." : ("WD debug: " + fail),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    Messages.Message("WD debug: road placed.", MessageTypeDefOf.PositiveEvent);
                    return false;
                },
                true,
                null,
                false,
                null,
                null,
                t => TryGetSurfaceTile(t, out _));
        }

        public static void BeginPlaceBridge()
        {
            const string label = "Place bridge";
            if (TryToggleOff(label)) return;
            CancelActive();
            ActiveToolLabel = label;

            pendingBridgeFromTile = -1;
            BridgeBankScratch.Clear();
            BridgeChainScratch.Clear();
            WD_WorldLayer_BridgeTargetFill.Hide();
            Messages.Message(
                "WD debug: click a water-adjacent bank, then the far bank. Right-click or Esc to stop.",
                MessageTypeDefOf.NeutralEvent);
            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    if (!TryGetSurfaceTile(target, out int tile)) return false;
                    WorldGrid grid = Find.WorldGrid;
                    if (grid == null || grid[tile].WaterCovered)
                    {
                        Messages.Message("WD debug: start bank must be land.", MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (pendingBridgeFromTile < 0)
                    {
                        if (!WdBridgeGeometry.IsLandAdjacentToWater(tile))
                        {
                            Messages.Message("WD debug: start bank must be adjacent to water.", MessageTypeDefOf.RejectInput);
                            return false;
                        }
                        BridgeReservedScratch.Clear();
                        WorldActions_BuildBridge.CollectReservedBankTiles(BridgeReservedScratch);
                        if (BridgeReservedScratch.Contains(tile))
                        {
                            Messages.Message("WD debug: that bank is already used by a bridge.", MessageTypeDefOf.RejectInput);
                            return false;
                        }

                        pendingBridgeFromTile = tile;
                        BridgeChainScratch.Clear();
                        BridgeBankScratch.Clear();
                        WdBridgeGeometry.CollectFarBanksWithChains(
                            tile, BridgeReservedScratch, BridgeBankScratch, BridgeChainScratch);
                        WD_WorldLayer_BridgeTargetFill.Show(BridgeBankScratch);
                        Messages.Message("WD debug: click far bank.", MessageTypeDefOf.NeutralEvent);
                        return false;
                    }

                    int from = pendingBridgeFromTile;
                    pendingBridgeFromTile = -1;
                    if (!WD_WorldRoadEditUtility.TryPlaceBridgeBetweenBanks(from, tile, out string fail))
                    {
                        Messages.Message(
                            fail.NullOrEmpty() ? "WD debug: bridge place failed." : ("WD debug: " + fail),
                            MessageTypeDefOf.RejectInput);
                        WD_WorldLayer_BridgeTargetFill.Hide();
                        BridgeBankScratch.Clear();
                        BridgeChainScratch.Clear();
                        return false;
                    }

                    Messages.Message("WD debug: bridge placed.", MessageTypeDefOf.PositiveEvent);
                    WD_WorldLayer_BridgeTargetFill.Hide();
                    BridgeBankScratch.Clear();
                    BridgeChainScratch.Clear();
                    return false;
                },
                true,
                null,
                false,
                null,
                null,
                t =>
                {
                    if (!TryGetSurfaceTile(t, out int tile)) return false;
                    WorldGrid grid = Find.WorldGrid;
                    if (grid == null || grid[tile].WaterCovered) return false;
                    if (pendingBridgeFromTile < 0)
                        return WdBridgeGeometry.IsLandAdjacentToWater(tile);
                    return BridgeBankScratch.Contains(tile) || BridgeChainScratch.ContainsKey(tile);
                });
        }

        public static void DamageFortification500() => AdjustFortificationAtMouse(-500f);

        public static void HealFortification500() => AdjustFortificationAtMouse(500f);

        private static void AdjustFortificationAtMouse(float delta)
        {
            int tile = GenWorld.MouseTile();
            if (tile < 0 || !PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(tile))
            {
                Messages.Message("WD debug: invalid tile.", MessageTypeDefOf.RejectInput);
                return;
            }

            WorldComponent_RoadBlocks blocks = WorldComponent_RoadBlocks.Get();
            if (blocks != null && blocks.TryGet(tile, out _))
            {
                if (!blocks.TryAdjustHealth(tile, delta, out float newHealth))
                {
                    Messages.Message($"WD debug: failed to adjust road block on tile {tile}.", MessageTypeDefOf.RejectInput);
                    return;
                }
                ReportFortHp("road block", tile, delta, newHealth, destroyed: newHealth <= 0f);
                return;
            }

            WorldComponent_SpikeTraps traps = WorldComponent_SpikeTraps.Get();
            if (traps != null && traps.TryGet(tile, out SpikeTrapRecord trap) && trap != null)
            {
                float maxHp = WorldDominationMod.settings != null
                    ? WorldDominationMod.settings.GetSpikeTrapMaxHealth(trap.kind)
                    : WorldDominationSettings.DefSpikeTrapSpikeMaxHealth;
                trap.health = Mathf.Clamp(trap.health + delta, 0f, maxHp);
                if (trap.health <= 0f)
                {
                    traps.TryClear(tile);
                    ReportFortHp("spike trap", tile, delta, 0f, destroyed: true);
                    return;
                }
                ReportFortHp("spike trap", tile, delta, trap.health, destroyed: false);
                return;
            }

            WorldObject_AT_Turret at = FindAtTurretAt(tile);
            if (at != null)
            {
                if (delta < 0f)
                {
                    at.ApplyDamage(-delta);
                    if (at.Destroyed)
                    {
                        ReportFortHp("AT turret", tile, delta, 0f, destroyed: true);
                        return;
                    }
                    ReportFortHp("AT turret", tile, delta, at.strength, destroyed: false);
                    return;
                }

                float max = WorldDominationMod.settings != null
                    ? WorldDominationMod.settings.GetAtTurretMaxStrength(at.tier)
                    : WorldDominationSettings.GetAtTurretMaxStrengthDefault(at.tier);
                at.strength = Mathf.Min(max, at.strength + delta);
                ReportFortHp("AT turret", tile, delta, at.strength, destroyed: false);
                return;
            }

            WorldObject_WD_ArtilleryTurret art = FindArtilleryAt(tile);
            if (art != null)
            {
                if (delta < 0f)
                {
                    bool destroyed = art.ApplyDamage(-delta);
                    ReportFortHp("artillery turret", tile, delta, destroyed ? 0f : art.strength, destroyed);
                    return;
                }

                art.strength = Mathf.Min(art.MaxStrength, art.strength + delta);
                ReportFortHp("artillery turret", tile, delta, art.strength, destroyed: false);
                return;
            }

            Messages.Message(
                "WD debug: no damageable fortification on that tile (economic advanced buildings have no HP — use Destroy).",
                MessageTypeDefOf.RejectInput);
        }

        private static void ReportFortHp(string kind, int tile, float delta, float newHealth, bool destroyed)
        {
            if (destroyed)
            {
                Messages.Message($"WD debug: {kind} on tile {tile} destroyed.", MessageTypeDefOf.NeutralEvent);
                return;
            }
            string verb = delta < 0f ? "damaged" : "healed";
            Messages.Message(
                $"WD debug: {verb} {kind} on tile {tile} → HP {newHealth:F0}.",
                MessageTypeDefOf.PositiveEvent);
        }

        public static void BeginForceRaidFromTarget()
        {
            const string label = "Force raid from target";
            if (TryToggleOff(label)) return;
            CancelActive();
            ActiveToolLabel = label;

            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Land", () => BeginForceRaidTargeting(forceDropPod: false, forceGravship: false)),
                new FloatMenuOption("Drop pod", () => BeginForceRaidTargeting(forceDropPod: true, forceGravship: false)),
                new FloatMenuOption("Gravship", () => BeginForceRaidTargeting(forceDropPod: false, forceGravship: true))
            };
            DebugActions_FloatMenus.OpenCentered(opts);
        }

        private static void BeginForceRaidTargeting(bool forceDropPod, bool forceGravship)
        {
            string mode = forceGravship ? "gravship" : forceDropPod ? "drop-pod" : "land";
            Messages.Message(
                $"WD debug: click an NPC settlement to force a {mode} raid. Right-click or Esc to stop.",
                MessageTypeDefOf.NeutralEvent);
            Find.WorldTargeter.BeginTargeting(
                _ =>
                {
                    DebugActions_ForceSettlementRaid.TryForceRaidAtMouse(forceDropPod, forceGravship);
                    return false;
                },
                true,
                null,
                false,
                null,
                null,
                t =>
                {
                    if (!TryGetSurfaceTile(t, out int tile)) return false;
                    Settlement s = SettlementAtTile(tile);
                    return s != null && s.Faction != null && !s.Faction.IsPlayer;
                });
        }

        public static void BeginCreateRaid()
        {
            const string label = "Create raid";
            if (TryToggleOff(label)) return;
            CancelActive();
            ActiveToolLabel = label;
            OpenCreateRaidTypeMenu(null);
        }

        public static void BeginCreateOutpostOrColonyRaid()
        {
            const string label = "Create outpost / colony raid";
            if (TryToggleOff(label)) return;
            CancelActive();
            ActiveToolLabel = label;

            Messages.Message(
                "WD debug: click a player outpost or colony.",
                MessageTypeDefOf.NeutralEvent);
            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    if (!TryGetSurfaceTile(target, out int tile)) return false;
                    WorldObject site = FindPlayerRaidTargetAtTile(tile);
                    if (site == null)
                    {
                        Messages.Message(
                            "WD debug: click a player WD outpost or player colony.",
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }
                    OpenCreateRaidTypeMenu(site);
                    return true;
                },
                true,
                null,
                false,
                null,
                null,
                t =>
                {
                    if (!TryGetSurfaceTile(t, out int tile)) return false;
                    return FindPlayerRaidTargetAtTile(tile) != null;
                });
        }

        private static void OpenCreateRaidTypeMenu(WorldObject playerSite)
        {
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Land", () => OpenRaidFactionMenu(DebugRaidType.Land, playerSite)),
                new FloatMenuOption("Drop pod", () => OpenRaidFactionMenu(DebugRaidType.DropPod, playerSite)),
                new FloatMenuOption("Gravship", () => OpenRaidFactionMenu(DebugRaidType.Gravship, playerSite))
            };
            DebugActions_FloatMenus.OpenCentered(opts);
        }

        private static void OpenRaidFactionMenu(DebugRaidType type, WorldObject playerSite)
        {
            if (type == DebugRaidType.Gravship && !GravshipRaidsCompat.IsAvailable())
            {
                Messages.Message(
                    "WD debug: Gravship Raids + Odyssey required for gravship raids.",
                    MessageTypeDefOf.RejectInput);
                ActiveToolLabel = null;
                return;
            }

            var factions = new List<Faction>();
            CollectHostileWdFactions(factions);
            if (factions.Count == 0)
            {
                Messages.Message("WD debug: no hostile WD participant factions.", MessageTypeDefOf.RejectInput);
                ActiveToolLabel = null;
                return;
            }

            var opts = new List<FloatMenuOption>();
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                Faction captured = f;
                opts.Add(new FloatMenuOption(
                    captured.Name,
                    () => OpenRaidStrengthMenu(type, captured, playerSite),
                    captured.def?.FactionIcon,
                    captured.Color));
            }
            DebugActions_FloatMenus.OpenCentered(opts);
        }

        private static void OpenRaidStrengthMenu(DebugRaidType type, Faction faction, WorldObject playerSite)
        {
            var opts = new List<FloatMenuOption>();
            for (int i = 0; i < RaidStrengthPresets.Length; i++)
            {
                float strength = RaidStrengthPresets[i];
                float captured = strength;
                opts.Add(new FloatMenuOption(
                    strength.ToString("F0"),
                    () =>
                    {
                        if (playerSite != null)
                            BeginCreatePlayerSiteRaidTargeting(type, faction, captured, playerSite);
                        else
                            BeginCreateRaidTargeting(type, faction, captured);
                    }));
            }
            DebugActions_FloatMenus.OpenCentered(opts);
        }

        private static void BeginCreateRaidTargeting(DebugRaidType type, Faction faction, float strength)
        {
            Messages.Message(
                "WD debug: click origin tile, then destination tile.",
                MessageTypeDefOf.NeutralEvent);

            int originTile = -1;
            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    if (!TryGetSurfaceTile(target, out int tile)) return false;
                    if (originTile < 0)
                    {
                        originTile = tile;
                        Messages.Message("WD debug: click destination tile.", MessageTypeDefOf.NeutralEvent);
                        return false;
                    }

                    int destTile = tile;
                    if (type == DebugRaidType.Land)
                    {
                        WorldObject raidTarget = FindRaidTargetAtTile(destTile);
                        if (raidTarget != null)
                            SpawnLandRaid(faction, strength, originTile, raidTarget, useOutpostDef: raidTarget is WorldObject_WD_Outpost);
                        else
                            SpawnDebugRaidTransit(faction, strength, originTile, destTile);
                        return true;
                    }

                    WorldObject ballisticTarget = FindRaidTargetAtTile(destTile);
                    if (ballisticTarget == null)
                    {
                        Messages.Message(
                            "WD debug: drop pod / gravship need a raid target world object on the destination tile.",
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    SpawnBallisticRaid(type, faction, strength, originTile, ballisticTarget);
                    return true;
                },
                true,
                null,
                false,
                null,
                null,
                t => TryGetSurfaceTile(t, out _));
        }

        private static void BeginCreatePlayerSiteRaidTargeting(
            DebugRaidType type, Faction faction, float strength, WorldObject playerSite)
        {
            if (playerSite == null || playerSite.Destroyed)
            {
                Messages.Message("WD debug: target site is gone.", MessageTypeDefOf.RejectInput);
                ActiveToolLabel = null;
                return;
            }

            Messages.Message(
                $"WD debug: click origin tile for raid against {playerSite.LabelCap}.",
                MessageTypeDefOf.NeutralEvent);

            bool useOutpostDef = playerSite is WorldObject_WD_Outpost;
            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    if (!TryGetSurfaceTile(target, out int originTile)) return false;
                    if (type == DebugRaidType.Land)
                    {
                        SpawnLandRaid(faction, strength, originTile, playerSite, useOutpostDef);
                        return true;
                    }

                    SpawnBallisticRaid(type, faction, strength, originTile, playerSite);
                    return true;
                },
                true,
                null,
                false,
                null,
                null,
                t => TryGetSurfaceTile(t, out _));
        }

        private static void SpawnLandRaid(
            Faction faction, float strength, int originTile, WorldObject target, bool useOutpostDef)
        {
            string defName = useOutpostDef ? "TSA_WD_Traveler_Outpost_Raid" : "TSA_WD_Traveler_Raid";
            WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                Messages.Message($"WD debug: missing {defName}.", MessageTypeDefOf.RejectInput);
                return;
            }

            var traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.SetFaction(faction);
            traveler.mission = TravelerMission.Raid;
            traveler.originObject = null;
            traveler.targetObject = target;
            traveler.cachedTargetKind = RaidLaunchGate.ClassifyTarget(target);
            traveler.travelerStrength = strength;
            traveler.initialStrength = strength;
            traveler.spawnTick = Find.TickManager.TicksGame;
            traveler.Tile = originTile;
            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(target.Tile, traveler));
            Messages.Message(
                $"WD debug: land raid ({strength:F0}) {faction.Name} → {target.LabelCap} from {originTile}.",
                MessageTypeDefOf.PositiveEvent);
        }

        private static void SpawnDebugRaidTransit(Faction faction, float strength, int originTile, int destTile)
        {
            WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_Raid");
            if (def == null)
            {
                Messages.Message("WD debug: missing TSA_WD_Traveler_Raid.", MessageTypeDefOf.RejectInput);
                return;
            }

            var traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.SetFaction(faction);
            traveler.mission = TravelerMission.DebugRaidTransit;
            traveler.originObject = null;
            traveler.targetObject = null;
            traveler.travelerStrength = strength;
            traveler.initialStrength = strength;
            traveler.spawnTick = Find.TickManager.TicksGame;
            traveler.Tile = originTile;
            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(destTile, traveler));
            Messages.Message(
                $"WD debug: bait transit ({strength:F0}) {faction.Name} {originTile} → {destTile}.",
                MessageTypeDefOf.PositiveEvent);
        }

        private static void SpawnBallisticRaid(
            DebugRaidType type, Faction faction, float strength, int originTile, WorldObject target)
        {
            bool grav = type == DebugRaidType.Gravship;
            if (grav && !GravshipRaidsCompat.IsAvailable())
            {
                Messages.Message(
                    "WD debug: Gravship Raids + Odyssey required for gravship raids.",
                    MessageTypeDefOf.RejectInput);
                return;
            }

            string defName = grav ? "TSA_WD_Traveler_RaidGravship" : "TSA_WD_Traveler_RaidDropPod";
            TravelerMission mission = grav ? TravelerMission.RaidGravship : TravelerMission.RaidDropPod;
            WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                Messages.Message($"WD debug: missing {defName}.", MessageTypeDefOf.RejectInput);
                return;
            }

            var traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = originTile;
            traveler.SetFaction(faction);
            traveler.mission = mission;
            traveler.ticksPerMove = WorldActions_Traveler.GetDropPodTicksPerMove();
            traveler.originObject = null;
            traveler.targetObject = target;
            traveler.cachedTargetKind = RaidLaunchGate.ClassifyTarget(target);
            traveler.travelerStrength = strength;
            traveler.initialStrength = strength;
            traveler.projectedArrivalStrength = strength;
            traveler.spawnTick = Find.TickManager.TicksGame;
            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(target.Tile, traveler));
            AntiAirFireUtils.WakeAllForDropPod(traveler);
            string mode = grav ? "gravship" : "drop-pod";
            Messages.Message(
                $"WD debug: {mode} raid ({strength:F0}) {faction.Name} → {target.LabelCap} from {originTile}.",
                MessageTypeDefOf.PositiveEvent);
        }

        private static void CollectHostileWdFactions(List<Faction> into)
        {
            into.Clear();
            var factions = Find.FactionManager?.AllFactionsListForReading;
            if (factions == null) return;
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                if (f == null || f.IsPlayer || f.defeated || f.def == null || f.def.hidden)
                    continue;
                if (!WorldActions_Utils.IsWdParticipant(f)) continue;
                if (!WorldActions_Utils.SafeHostileTo(f, Faction.OfPlayer)) continue;
                into.Add(f);
            }
        }

        private static WorldObject FindPlayerRaidTargetAtTile(int tile)
        {
            if (tile < 0 || Find.WorldObjects == null) return null;
            WorldObject_WD_Outpost outpost = null;
            Settlement colony = null;
            foreach (WorldObject o in Find.WorldObjects.ObjectsAt(tile))
            {
                if (o == null || o.Destroyed) continue;
                if (o is WorldObject_WD_Outpost op && op.Faction == Faction.OfPlayer)
                    outpost = op;
                else if (o is Settlement s && s.Faction == Faction.OfPlayer)
                    colony = s;
            }
            if (outpost != null) return outpost;
            return colony;
        }

        private static WorldObject FindRaidTargetAtTile(int tile)
        {
            if (tile < 0 || Find.WorldObjects == null) return null;
            WorldObject_WD_Outpost outpost = null;
            Settlement settlement = null;
            foreach (WorldObject o in Find.WorldObjects.ObjectsAt(tile))
            {
                if (o == null || o.Destroyed) continue;
                if (o is WorldObject_WD_Outpost op)
                    outpost = op;
                else if (o is Settlement s)
                    settlement = s;
            }
            if (outpost != null) return outpost;
            if (settlement != null) return settlement;
            return null;
        }

        private static WorldObject_AT_Turret FindAtTurretAt(int tile)
        {
            if (Find.WorldObjects == null) return null;
            foreach (WorldObject o in Find.WorldObjects.ObjectsAt(tile))
            {
                if (o is WorldObject_AT_Turret t && !t.Destroyed)
                    return t;
            }
            return null;
        }

        private static WorldObject_WD_ArtilleryTurret FindArtilleryAt(int tile)
        {
            if (Find.WorldObjects == null) return null;
            foreach (WorldObject o in Find.WorldObjects.ObjectsAt(tile))
            {
                if (o is WorldObject_WD_ArtilleryTurret t && !t.Destroyed)
                    return t;
            }
            return null;
        }

        private static bool TryGetSurfaceTile(GlobalTargetInfo target, out int tile)
        {
            tile = -1;
            if (!target.IsValid) return false;
            tile = target.Tile.tileId;
            return tile >= 0 && PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(tile);
        }

        private static Settlement SettlementAtTile(int tile)
        {
            if (Find.WorldObjects == null) return null;
            foreach (WorldObject o in Find.WorldObjects.ObjectsAt(tile))
            {
                if (o is Settlement s && !s.Destroyed)
                    return s;
            }
            return null;
        }
    }
}
