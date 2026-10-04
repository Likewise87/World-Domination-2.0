using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Bridged water: land-like movement difficulty with a proper Terrain-tab explanation.
    /// Vanilla early-outs Ocean as Impassable (1000) and writes that into the explanation builder;
    /// a postfix alone would leave the tip saying Impassable while the number is fixed.
    /// </summary>
    [HarmonyPatch(typeof(WorldPathGrid), nameof(WorldPathGrid.CalculatedMovementDifficultyAt))]
    public static class Patch_CalculatedMovementDifficultyAt_WdBridge
    {
        public static bool Prefix(PlanetTile tile, int? ticksAbs, StringBuilder explanation, ref float __result)
        {
            if (!WorldComponent_WdBridges.IsBridgedWaterTile(tile)) return true;

            if (explanation != null && explanation.Length > 0)
                explanation.AppendLine();

            const float bridgeBase = 1f;
            explanation?.Append("TSA_WD_Bridge_MovementDifficultyBase".Translate()
                + ": " + bridgeBase.ToStringWithSign("0.#"));

            __result = bridgeBase + WorldPathGrid.GetCurrentWinterMovementDifficultyOffset(
                tile, ticksAbs ?? GenTicks.TicksAbs, explanation);
            return false;
        }
    }

    /// <summary>
    /// Live passability for bridged water so <see cref="World.Impassable"/> / FindPath neighbor
    /// rejection cannot stay stuck on a stale path-grid cache entry.
    /// </summary>
    [HarmonyPatch(typeof(WorldPathGrid), nameof(WorldPathGrid.Passable))]
    public static class Patch_WorldPathGrid_Passable_WdBridge
    {
        public static void Postfix(PlanetTile tile, ref bool __result)
        {
            if (__result) return;
            if (!WorldComponent_WdBridges.IsBridgedWaterTile(tile)) return;
            __result = true;
        }
    }

    [HarmonyPatch(typeof(WorldPathGrid), nameof(WorldPathGrid.PassableFast))]
    public static class Patch_WorldPathGrid_PassableFast_WdBridge
    {
        public static void Postfix(PlanetTile tile, ref bool __result)
        {
            if (__result) return;
            if (!WorldComponent_WdBridges.IsBridgedWaterTile(tile)) return;
            __result = true;
        }
    }

    /// <summary>
    /// Belt-and-suspenders for tile inspect + Terrain tab, which both gate movement difficulty
    /// on <see cref="World.Impassable"/>.
    /// </summary>
    [HarmonyPatch(typeof(World), nameof(World.Impassable))]
    public static class Patch_World_Impassable_WdBridge
    {
        public static void Postfix(PlanetTile tileID, ref bool __result)
        {
            if (!__result) return;
            if (!WorldComponent_WdBridges.IsBridgedWaterTile(tileID)) return;
            __result = false;
        }
    }

    /// <summary>Expose potentialRoads on bridged ocean tiles so draw + road speed work despite allowRoads=false.</summary>
    [HarmonyPatch(typeof(SurfaceTile), "get_Roads")]
    public static class Patch_SurfaceTile_Roads_WdBridge
    {
        public static void Postfix(SurfaceTile __instance, ref List<SurfaceTile.RoadLink> __result)
        {
            if (__result != null || __instance == null) return;
            if (!__instance.WaterCovered) return;
            if (__instance.potentialRoads == null || __instance.potentialRoads.Count == 0) return;

            // Tile.tile is public on the Tile base; do not rely on AccessTools field lookup.
            PlanetTile pt = __instance.tile;
            int tileId = pt.tileId;
            if (tileId < 0) return;
            if (!WorldComponent_WdBridges.IsBridgedWaterTile(tileId)) return;
            __result = __instance.potentialRoads;
        }
    }
}
