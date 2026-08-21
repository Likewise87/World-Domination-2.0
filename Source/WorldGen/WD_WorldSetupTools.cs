using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public enum WdWorldSetupTool
    {
        None,
        PlaceSettlement,
        RemoveSettlement,
        StrengthPlus,
        StrengthMinus,
        TierUp,
        TierDown,
        Turret,
        RoadBlock,
        Trap,
        PlaceRoad,
        RemoveFortify,
        RemoveRoad
    }

    /// <summary>World-targeter tools for Dialog_WdWorldSetup (works during Select Starting Site).</summary>
    public static class WD_WorldSetupTools
    {
        private static int pendingRoadFromTile = -1;

        public static WdWorldSetupTool ActiveTool { get; private set; }

        public static void TickClearIfIdle()
        {
            if (ActiveTool == WdWorldSetupTool.None) return;
            if (Find.WorldTargeter != null && Find.WorldTargeter.IsTargeting) return;
            if (Find.WindowStack != null && Find.WindowStack.IsOpen<FloatMenu>()) return;
            ActiveTool = WdWorldSetupTool.None;
            pendingRoadFromTile = -1;
        }

        public static bool TryToggleOff(WdWorldSetupTool tool)
        {
            if (ActiveTool != tool) return false;
            CancelActive();
            return true;
        }

        public static void CancelActive()
        {
            ActiveTool = WdWorldSetupTool.None;
            pendingRoadFromTile = -1;
            if (Find.WorldTargeter != null && Find.WorldTargeter.IsTargeting)
                Find.WorldTargeter.StopTargeting();
            CloseOpenFloatMenu();
        }

        private static void Activate(WdWorldSetupTool tool)
        {
            if (ActiveTool != WdWorldSetupTool.None && ActiveTool != tool)
            {
                pendingRoadFromTile = -1;
                if (Find.WorldTargeter != null && Find.WorldTargeter.IsTargeting)
                    Find.WorldTargeter.StopTargeting();
                CloseOpenFloatMenu();
            }
            ActiveTool = tool;
        }

        private static void CloseOpenFloatMenu()
        {
            FloatMenu menu = Find.WindowStack?.WindowOfType<FloatMenu>();
            if (menu != null)
                Find.WindowStack.TryRemove(menu);
        }

        public static void BeginPlaceSettlement()
        {
            Activate(WdWorldSetupTool.PlaceSettlement);
            var tierOpts = new List<FloatMenuOption>();
            foreach (SettlementTier tier in Enum.GetValues(typeof(SettlementTier)))
            {
                SettlementTier captured = tier;
                tierOpts.Add(new FloatMenuOption(
                    captured.ToString(),
                    () => OpenFactionMenuForSpawn(captured)));
            }
            DebugActions_FloatMenus.OpenCentered(tierOpts);
        }

        private static void OpenFactionMenuForSpawn(SettlementTier tier)
        {
            var factionOpts = new List<FloatMenuOption>();
            var factions = Find.FactionManager?.AllFactionsListForReading;
            if (factions == null || factions.Count == 0)
            {
                Messages.Message("TSA_WD_WorldSetup_NoFactions".Translate(), MessageTypeDefOf.RejectInput);
                CancelActive();
                return;
            }

            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                if (f == null || f.IsPlayer || f.defeated || f.def == null || f.def.hidden)
                    continue;
                if (WorldActions_Utils.IsExcludedFaction(f))
                    continue;

                Faction captured = f;
                Texture2D icon = captured.def.FactionIcon;
                factionOpts.Add(new FloatMenuOption(
                    captured.Name,
                    () => BeginSpawnSettlementTargeting(tier, captured),
                    icon,
                    captured.Color));
            }

            if (factionOpts.Count == 0)
            {
                Messages.Message("TSA_WD_WorldSetup_NoFactions".Translate(), MessageTypeDefOf.RejectInput);
                CancelActive();
                return;
            }

            DebugActions_FloatMenus.OpenCentered(factionOpts);
        }

        private static void BeginSpawnSettlementTargeting(SettlementTier tier, Faction faction)
        {
            Messages.Message(
                "TSA_WD_WorldSetup_PlaceSettlementHint".Translate(tier.ToString(), faction.Name),
                MessageTypeDefOf.NeutralEvent);

            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    if (!WD_SettlementEditUtility.TryGetValidSettlementTile(target, out int tile, true, out string fail))
                    {
                        if (!string.IsNullOrEmpty(fail))
                            Messages.Message(fail, MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (!WD_SettlementEditUtility.TrySpawnWdSettlementAt(tile, tier, faction, out string spawnFail, true))
                    {
                        Messages.Message(spawnFail ?? "TSA_WD_WorldSetup_SpawnFailed".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    Messages.Message(
                        "TSA_WD_WorldSetup_PlacedSettlement".Translate(tier.ToString(), faction.Name, tile),
                        MessageTypeDefOf.PositiveEvent);
                    return false;
                },
                true,
                null,
                false,
                null,
                null,
                t => WD_SettlementEditUtility.TryGetValidSettlementTile(t, out _, true, out _));
        }

        public static void BeginRemoveSettlement()
        {
            Activate(WdWorldSetupTool.RemoveSettlement);
            Messages.Message("TSA_WD_WorldSetup_RemoveSettlementHint".Translate(), MessageTypeDefOf.NeutralEvent);
            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    int tile = target.IsValid ? target.Tile : -1;
                    if (!WD_SettlementEditUtility.TryRemoveNpcSettlementAtTile(tile, out string msg))
                    {
                        Messages.Message(
                            msg ?? "TSA_WD_WorldSetup_NeedSettlement".Translate(),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }
                    Messages.Message(msg, MessageTypeDefOf.PositiveEvent);
                    return false;
                },
                true,
                null,
                false,
                null,
                null,
                t =>
                {
                    WorldObject obj = WD_SettlementEditUtility.FindSettlementOrOutpostAt(t.IsValid ? t.Tile : -1);
                    return obj is Settlement s && WD_SettlementLayoutUtility.IsRecreateTargetSettlement(s);
                });
        }

        public static void BeginAdjustStrength(float delta)
        {
            Activate(delta > 0f ? WdWorldSetupTool.StrengthPlus : WdWorldSetupTool.StrengthMinus);
            Messages.Message("TSA_WD_WorldSetup_ClickSettlementHint".Translate(), MessageTypeDefOf.NeutralEvent);
            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    int tile = target.IsValid ? target.Tile : -1;
                    if (!WD_SettlementEditUtility.TryAdjustStrengthAtTile(tile, delta, out string msg))
                    {
                        Messages.Message("TSA_WD_WorldSetup_NeedSettlement".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }
                    Messages.Message(msg, MessageTypeDefOf.PositiveEvent);
                    return false;
                },
                true,
                null,
                false,
                null,
                null,
                t => WD_SettlementEditUtility.FindSettlementOrOutpostAt(t.IsValid ? t.Tile : -1) != null);
        }

        public static void BeginAdjustTier(int delta)
        {
            Activate(delta > 0 ? WdWorldSetupTool.TierUp : WdWorldSetupTool.TierDown);
            Messages.Message("TSA_WD_WorldSetup_ClickSettlementHint".Translate(), MessageTypeDefOf.NeutralEvent);
            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    int tile = target.IsValid ? target.Tile : -1;
                    if (!WD_SettlementEditUtility.TryAdjustTierAtTile(tile, delta, out string msg))
                    {
                        Messages.Message("TSA_WD_WorldSetup_NeedSettlement".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }
                    Messages.Message(msg, MessageTypeDefOf.PositiveEvent);
                    return false;
                },
                true,
                null,
                false,
                null,
                null,
                t => WD_SettlementEditUtility.FindSettlementOrOutpostAt(t.IsValid ? t.Tile : -1) != null);
        }

        public static void BeginPlaceRoadBlock()
        {
            Activate(WdWorldSetupTool.RoadBlock);
            var kindOpts = new List<FloatMenuOption>();
            foreach (RoadBlockKind kind in Enum.GetValues(typeof(RoadBlockKind)))
            {
                if (!RoadBlockKindUtil.IsPlaceableFromUi(kind)) continue;
                RoadBlockKind captured = kind;
                kindOpts.Add(new FloatMenuOption(
                    RoadBlockKindUtil.LabelKey(captured).Translate(),
                    () => BeginPlaceRoadBlockTargeting(captured)));
            }
            if (kindOpts.Count == 0)
            {
                Messages.Message("TSA_WD_WorldSetup_NoRoadBlockKinds".Translate(), MessageTypeDefOf.RejectInput);
                CancelActive();
                return;
            }
            DebugActions_FloatMenus.OpenCentered(kindOpts);
        }

        private static void BeginPlaceRoadBlockTargeting(RoadBlockKind kind)
        {
            var factions = new List<Faction>();
            DebugActions_FloatMenus.CollectDebugFactions(factions);
            if (factions.Count == 0)
            {
                Messages.Message("TSA_WD_WorldSetup_NoFactions".Translate(), MessageTypeDefOf.RejectInput);
                CancelActive();
                return;
            }
            Faction builder = factions[0];
            Messages.Message("TSA_WD_WorldSetup_PlaceFortifyHint".Translate(), MessageTypeDefOf.NeutralEvent);
            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    if (!TryGetSurfaceTile(target, out int tile)) return false;
                    WorldComponent_RoadBlocks blocks = WorldComponent_RoadBlocks.Get();
                    if (blocks == null || !blocks.TryPlace(tile, builder, kind))
                    {
                        Messages.Message("TSA_WD_WorldSetup_PlaceFortifyFailed".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }
                    Messages.Message("TSA_WD_WorldSetup_PlaceFortifyDone".Translate(), MessageTypeDefOf.PositiveEvent);
                    return false;
                },
                true,
                null,
                false,
                null,
                null,
                t => TryGetSurfaceTile(t, out int tile) && WorldActions_RoadBlocks.IsValidBuildTile(tile, kind));
        }

        public static void BeginPlaceSpikeTrap()
        {
            Activate(WdWorldSetupTool.Trap);
            var factions = new List<Faction>();
            DebugActions_FloatMenus.CollectDebugFactions(factions);
            if (factions.Count == 0)
            {
                Messages.Message("TSA_WD_WorldSetup_NoFactions".Translate(), MessageTypeDefOf.RejectInput);
                CancelActive();
                return;
            }

            var factionOpts = new List<FloatMenuOption>();
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                Faction captured = f;
                factionOpts.Add(new FloatMenuOption(
                    captured.Name,
                    () => OpenSpikeTrapKindMenu(captured),
                    captured.def?.FactionIcon,
                    captured.Color));
            }
            DebugActions_FloatMenus.OpenCentered(factionOpts);
        }

        private static void OpenSpikeTrapKindMenu(Faction faction)
        {
            var kindOpts = new List<FloatMenuOption>();
            foreach (SpikeTrapKind kind in Enum.GetValues(typeof(SpikeTrapKind)))
            {
                SpikeTrapKind captured = kind;
                kindOpts.Add(new FloatMenuOption(
                    SpikeTrapKindUtil.LabelKey(captured).Translate(),
                    () => BeginPlaceSpikeTrapTargeting(faction, captured)));
            }
            DebugActions_FloatMenus.OpenCentered(kindOpts);
        }

        private static void BeginPlaceSpikeTrapTargeting(Faction faction, SpikeTrapKind kind)
        {
            Messages.Message("TSA_WD_WorldSetup_PlaceFortifyHint".Translate(), MessageTypeDefOf.NeutralEvent);
            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    if (!TryGetSurfaceTile(target, out int tile)) return false;
                    WorldComponent_SpikeTraps traps = WorldComponent_SpikeTraps.Get();
                    if (traps == null || !traps.TryPlace(tile, faction, kind))
                    {
                        Messages.Message("TSA_WD_WorldSetup_PlaceFortifyFailed".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }
                    Messages.Message("TSA_WD_WorldSetup_PlaceFortifyDone".Translate(), MessageTypeDefOf.PositiveEvent);
                    return false;
                },
                true,
                null,
                false,
                null,
                null,
                t => TryGetSurfaceTile(t, out int tile) && WorldActions_SpikeTraps.IsValidBuildTile(tile, kind, faction));
        }

        public static void BeginPlaceAtTurret()
        {
            Activate(WdWorldSetupTool.Turret);
            var factions = new List<Faction>();
            DebugActions_FloatMenus.CollectDebugFactions(factions);
            if (factions.Count == 0)
            {
                Messages.Message("TSA_WD_WorldSetup_NoFactions".Translate(), MessageTypeDefOf.RejectInput);
                CancelActive();
                return;
            }

            var factionOpts = new List<FloatMenuOption>();
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                Faction captured = f;
                factionOpts.Add(new FloatMenuOption(
                    captured.Name,
                    () => OpenAtTurretTierMenu(captured),
                    captured.def?.FactionIcon,
                    captured.Color));
            }
            DebugActions_FloatMenus.OpenCentered(factionOpts);
        }

        private static void OpenAtTurretTierMenu(Faction faction)
        {
            var tierOpts = new List<FloatMenuOption>();
            foreach (AtTurretTier tier in Enum.GetValues(typeof(AtTurretTier)))
            {
                AtTurretTier captured = tier;
                tierOpts.Add(new FloatMenuOption(
                    AtTurretUtility.LabelKey(captured).Translate(),
                    () => BeginPlaceAtTurretTargeting(faction, captured)));
            }
            DebugActions_FloatMenus.OpenCentered(tierOpts);
        }

        private static void BeginPlaceAtTurretTargeting(Faction faction, AtTurretTier tier)
        {
            Messages.Message("TSA_WD_WorldSetup_PlaceFortifyHint".Translate(), MessageTypeDefOf.NeutralEvent);
            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    if (!TryGetSurfaceTile(target, out int tile)) return false;
                    WorldObject_AT_Turret turret = AtTurretUtility.TrySpawn(
                        tile, faction, tier, null, null, requirePlayerBuildSite: false);
                    if (turret == null)
                    {
                        Messages.Message("TSA_WD_WorldSetup_PlaceFortifyFailed".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }
                    Messages.Message("TSA_WD_WorldSetup_PlaceFortifyDone".Translate(), MessageTypeDefOf.PositiveEvent);
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
                    if (AtTurretUtility.TileHasAtTurret(tile)) return false;
                    return faction?.IsPlayer == true
                        ? AtTurretUtility.IsPlayerBuildableTurretTile(tile)
                        : AtTurretUtility.IsEmptyOffRoadTurretSite(tile);
                });
        }

        public static void BeginRemoveFortification()
        {
            Activate(WdWorldSetupTool.RemoveFortify);
            Messages.Message("TSA_WD_WorldSetup_RemoveFortifyHint".Translate(), MessageTypeDefOf.NeutralEvent);
            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    if (!TryGetSurfaceTile(target, out int tile)) return false;
                    bool cleared = WorldActions_RoadBlocks.ClearIfPresent(tile);
                    cleared |= WorldActions_SpikeTraps.ClearIfPresent(tile);
                    WorldObject_AT_Turret turret = AtTurretUtility.FindTurretAt(tile);
                    if (turret != null && !turret.Destroyed)
                    {
                        turret.Destroy();
                        cleared = true;
                    }
                    if (!cleared)
                    {
                        Messages.Message("TSA_WD_WorldSetup_RemoveFortifyNone".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }
                    Messages.Message("TSA_WD_WorldSetup_RemoveFortifyDone".Translate(), MessageTypeDefOf.PositiveEvent);
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
                    return WorldComponent_RoadBlocks.Get()?.HasBlockAt(tile) == true
                        || WorldComponent_SpikeTraps.Get()?.HasTrapAt(tile) == true
                        || AtTurretUtility.TileHasAtTurret(tile);
                });
        }

        public static void BeginPlaceRoad()
        {
            Activate(WdWorldSetupTool.PlaceRoad);
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("TSA_WD_WorldSetup_RoadDirt".Translate(),
                    () => BeginPlaceRoadTargeting(SettlementTier.T1)),
                new FloatMenuOption("TSA_WD_WorldSetup_RoadStone".Translate(),
                    () => BeginPlaceRoadTargeting(SettlementTier.T2)),
                new FloatMenuOption("TSA_WD_WorldSetup_RoadAsphalt".Translate(),
                    () => BeginPlaceRoadTargeting(SettlementTier.T3))
            };
            DebugActions_FloatMenus.OpenCentered(opts);
        }

        private static void BeginPlaceRoadTargeting(SettlementTier tier)
        {
            pendingRoadFromTile = -1;
            RoadDef road = WD_WorldRoadEditUtility.ResolveRoadDef(tier);
            if (road == null)
            {
                Messages.Message("TSA_WD_WorldSetup_RoadDefMissing".Translate(), MessageTypeDefOf.RejectInput);
                CancelActive();
                return;
            }

            Messages.Message("TSA_WD_WorldSetup_PlaceRoadHint".Translate(), MessageTypeDefOf.NeutralEvent);
            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    if (!TryGetSurfaceTile(target, out int tile)) return false;
                    if (pendingRoadFromTile < 0)
                    {
                        pendingRoadFromTile = tile;
                        Messages.Message("TSA_WD_WorldSetup_PlaceRoadPickEnd".Translate(), MessageTypeDefOf.NeutralEvent);
                        return false;
                    }

                    int from = pendingRoadFromTile;
                    pendingRoadFromTile = -1;
                    if (!WD_WorldRoadEditUtility.TryPlaceRoadAlongPath(from, tile, road, out string fail))
                    {
                        Messages.Message(fail ?? "TSA_WD_WorldSetup_RoadNoPath".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    Messages.Message("TSA_WD_WorldSetup_PlaceRoadDone".Translate(), MessageTypeDefOf.PositiveEvent);
                    return false;
                },
                true,
                null,
                false,
                null,
                null,
                t => TryGetSurfaceTile(t, out _));
        }

        public static void BeginRemoveRoad()
        {
            Activate(WdWorldSetupTool.RemoveRoad);
            Messages.Message("TSA_WD_WorldSetup_RemoveRoadHint".Translate(), MessageTypeDefOf.NeutralEvent);
            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    if (!TryGetSurfaceTile(target, out int tile)) return false;
                    if (!WD_WorldRoadEditUtility.TryRemoveRoadsAtTile(tile, out int removed) || removed <= 0)
                    {
                        Messages.Message("TSA_WD_WorldSetup_RemoveRoadNone".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }
                    Messages.Message("TSA_WD_WorldSetup_RemoveRoadDone".Translate(removed), MessageTypeDefOf.PositiveEvent);
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
                    if (!(Find.WorldGrid?[tile] is SurfaceTile surface)) return false;
                    return (surface.potentialRoads != null && surface.potentialRoads.Count > 0)
                        || (surface.Roads != null && surface.Roads.Count > 0);
                });
        }

        private static bool TryGetSurfaceTile(GlobalTargetInfo target, out int tile)
        {
            tile = -1;
            if (!target.IsValid) return false;
            tile = target.Tile;
            return tile >= 0 && PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(tile);
        }
    }
}
