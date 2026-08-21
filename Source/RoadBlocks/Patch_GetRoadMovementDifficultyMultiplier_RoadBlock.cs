using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Bakes flat road-block difficulty into the road multiplier so both caravan <see cref="Caravan_PathFollower.CostToMove"/>
    /// and <see cref="WorldPathing"/> A* see: terrain × road (+ RotR modifiers) + blockPenalty.
    /// Must run after Roads of the Rim, which multiplies <c>__result</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="WorldInspectPane"/> calls this as <c>(selectedTile, PlanetTile.Invalid)</c> when showing
    /// Movement difficulty — vanilla then rewrites <c>toTile</c> to a road neighbor for display.
    /// We capture validity in Prefix so the penalty still applies to the inspected tile.
    /// </remarks>
    [HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.GetRoadMovementDifficultyMultiplier))]
    [HarmonyAfter("Mlie.RoadsOfTheRim")]
    public static class Patch_GetRoadMovementDifficultyMultiplier_RoadBlock
    {
        [HarmonyPrefix]
        public static void Prefix(PlanetTile toTile, out bool __state)
        {
            __state = toTile.Valid;
        }

        [HarmonyPostfix]
        public static void Postfix(PlanetTile fromTile, PlanetTile toTile, StringBuilder explanation, bool __state, ref float __result)
        {
            // Hop cost: penalty on tile being entered (to).
            // Tile inspect/display: to started invalid — apply on from (the inspected tile).
            PlanetTile blockTile = __state ? toTile : fromTile;
            if (!blockTile.Valid) return;

            float penalty = WorldComponent_RoadBlocks.GetFlatPenalty(blockTile.tileId);
            if (penalty > 0f)
            {
                // Cost uses terrain * roadMult. We want terrain * road + penalty
                // ⇒ roadMult' = road + penalty / terrain.
                float terrain = WorldPathGrid.CalculatedMovementDifficultyAt(blockTile, false);
                if (terrain < 0.01f) terrain = 0.01f;
                __result += penalty / terrain;

                if (explanation != null)
                {
                    explanation.AppendLine();
                    explanation.Append("TSA_WD_RoadBlock_MovementExplanation".Translate(penalty.ToString("0.#")));
                    explanation.AppendLine();
                }
            }

            // WD FindPath pollution cost (approach A): only while WdPollutionPathContext is active.
            if (!WdPollutionPathContext.Active) return;
            if (!__state || !toTile.Valid) return;

            var s = WorldDominationMod.settings;
            if (s == null || !s.travelerPollutionDamageEnabled || !s.pollutionPathCostEnabled) return;

            float pollution01 = WorldTileProductivity.GetTilePollution01(toTile.tileId);
            float dmg = s.GetPollutionExitDamage(pollution01);
            if (dmg <= 0.01f) return;

            float terrainP = WorldPathGrid.CalculatedMovementDifficultyAt(toTile, false);
            if (terrainP < 0.01f) terrainP = 0.01f;
            float pollutionPenalty = dmg * WdPollutionPathContext.DamageToRoadMultScale * WdPollutionPathContext.Weight;
            __result += pollutionPenalty / terrainP;
        }
    }

    /// <summary>
    /// Without Roads of the Rim: reduce winter movement cost by the configured road winter %.
    /// With RotR, winter is applied via their <c>winterFactor</c> (written from settings); we still
    /// annotate the Terrain-tab explanation with the configured reduction.
    /// </summary>
    [HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.GetRoadMovementDifficultyMultiplier))]
    [HarmonyBefore("Mlie.RoadsOfTheRim")]
    public static class Patch_GetRoadMovementDifficultyMultiplier_WinterReduction
    {
        [HarmonyPostfix]
        public static void Postfix(PlanetTile fromTile, PlanetTile toTile, StringBuilder explanation, ref float __result)
        {
            if (__result >= 0.999f) return; // no road on this edge

            PlanetTile roadTile = toTile.Valid ? toTile : fromTile;
            if (!roadTile.Valid) return;

            RoadDef road = FindRoadDefBetween(fromTile, toTile.Valid ? toTile : roadTile);
            if (road == null) return;

            float winterReduction = WorldActions_Roads.GetWinterReductionForRoadDef(road);
            if (winterReduction <= 0.001f) return;

            string roadLabel = road.LabelCap;
            float pct = winterReduction * 100f;

            // RotR applies winter via winterFactor; only annotate the Terrain tooltip.
            if (WorldActions_Roads.RoadsOfTheRimActive)
            {
                if (explanation != null)
                {
                    explanation.AppendLine();
                    explanation.Append("TSA_WD_RoadBuilding_WinterReductionTerrainTip".Translate(
                        roadLabel, pct.ToString("F0")));
                }
                return;
            }

            float total = WorldPathGrid.CalculatedMovementDifficultyAt(roadTile, false);
            float winter = WorldPathGrid.GetCurrentWinterMovementDifficultyOffset(roadTile);
            if (total < 0.01f) return;

            if (winter > 0.001f)
            {
                // Shrink winter's share of total difficulty (RotR winterFactor shape, biome/hill factors = 0).
                float modified = (total - winter * winterReduction) / total;
                float before = __result;
                __result *= modified;

                if (explanation != null && !Mathf.Approximately(before, __result))
                {
                    explanation.AppendLine();
                    explanation.Append("TSA_WD_RoadBuilding_WinterReductionExplanation".Translate(
                        roadLabel,
                        pct.ToString("F0"),
                        modified.ToString("0.###")));
                }
            }
            else if (explanation != null)
            {
                // Still show configured reduction on Terrain tip when winter is not active on this tile.
                explanation.AppendLine();
                explanation.Append("TSA_WD_RoadBuilding_WinterReductionTerrainTipInactive".Translate(
                    roadLabel, pct.ToString("F0")));
            }
        }

        private static RoadDef FindRoadDefBetween(PlanetTile fromTile, PlanetTile toTile)
        {
            if (!fromTile.Valid) return null;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return null;
            if (!(grid[fromTile] is SurfaceTile surface)) return null;
            var roads = surface.Roads;
            if (roads == null) return null;

            PlanetTile target = toTile;
            if (!target.Valid)
                target = grid.FindMostReasonableAdjacentTileForDisplayedPathCost(fromTile);

            for (int i = 0; i < roads.Count; i++)
            {
                if (roads[i].neighbor == target)
                    return roads[i].road;
            }
            return null;
        }
    }
}
