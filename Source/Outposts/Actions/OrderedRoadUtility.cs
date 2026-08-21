using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public struct RoadTargetSelection
    {
        public int TargetTile;
        public string TargetName;
        public List<int> PathTiles;
        /// <summary>Intermediate waypoints (excludes origin and <see cref="TargetTile"/>).</summary>
        public List<int> WaypointTiles;
        public int WorkTile;
        public int SegmentCount;
    }

    public static class OrderedRoadUtility
    {
        public const int GoodwillFloor = 10;

        public static bool FactionHasActivePlayerOrder(Faction faction)
        {
            if (faction == null || faction.IsPlayer) return false;
            var settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s.Faction != faction) continue;
                if (s.GetComponent<CompViralSpread>() is CompViralSpread comp && comp.HasActivePlayerOrderedRoadProject)
                    return true;
            }
            return false;
        }

        public static bool CanShowOrderRoadGizmo(Settlement settlement, out string disabledReason)
        {
            disabledReason = null;
            if (settlement == null || settlement.Destroyed || settlement.Faction == null || settlement.Faction.IsPlayer)
                return false;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(settlement))
                return false;

            Faction player = Faction.OfPlayerSilentFail;
            FactionRelationKind kind = WorldActions_Utils.SafeRelationKindWith(settlement.Faction, player);
            if (kind != FactionRelationKind.Ally && kind != FactionRelationKind.Neutral)
                return false;

            var comp = settlement.GetComponent<CompViralSpread>();
            if (comp == null) return false;

            if (comp.HasActivePlayerOrderedRoadProject)
            {
                disabledReason = "TSA_WD_OrderedRoad_AlreadyBuilding".Translate();
                return true;
            }

            if (comp.roadTargetTile != -1)
            {
                disabledReason = "TSA_WD_OrderedRoad_SettlementBusy".Translate();
                return false;
            }

            if (comp.IsRoadOnCooldown)
            {
                float daysLeft = (comp.roadCooldownTick - Find.TickManager.TicksGame) / 60000f;
                disabledReason = "TSA_WD_OnCooldown".Translate(daysLeft.ToString("F1"));
            }

            return true;
        }

        public static void ApplyPlayerOrderedRoadProject(Settlement builder, CompViralSpread comp, RoadTargetSelection selection,
            int totalCost, float perSegmentRate)
        {
            if (builder == null || comp == null) return;

            comp.playerOrderedRoad = true;
            comp.playerOrderedRoadGoodwillPaid = totalCost;
            comp.playerOrderedRoadGoodwillRefunded = false;
            comp.playerOrderedRoadInitialSegments = selection.SegmentCount;
            comp.playerOrderedRoadBaseCost = 0;
            comp.playerOrderedRoadPerSegmentRate = perSegmentRate;

            comp.roadIsClearing = false;
            comp.roadTargetTile = selection.TargetTile;
            comp.roadProgress = 0f;
            comp.roadTargetName = selection.TargetName ?? string.Empty;
            comp.cachedRoadPathTiles = selection.PathTiles != null ? new List<int>(selection.PathTiles) : new List<int>();
            comp.roadWaypointTiles = selection.WaypointTiles != null ? new List<int>(selection.WaypointTiles) : new List<int>();
            comp.lastPathSourceTile = builder.Tile;
            comp.roadTargetUsesDetachedStart = true;
            comp.cachedWorkTile = selection.WorkTile;
        }

        public static string FormatRoadProgressLabel(CompViralSpread comp)
        {
            if (comp == null || !comp.HasActivePlayerOrderedRoadProject)
                return "—";
            string insufficient = comp.GetInsufficientStrengthConstructionMessage();
            if (insufficient != null)
                return insufficient;
            string target = comp.roadTargetName.NullOrEmpty() ? "?" : comp.roadTargetName;
            return target + " (" + (Mathf.Min(1f, comp.roadProgress) * 100f).ToString("F0") + "%)";
        }
    }
}
