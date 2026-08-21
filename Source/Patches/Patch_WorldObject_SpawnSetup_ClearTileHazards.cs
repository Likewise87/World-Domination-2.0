using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Occupying world objects clear road blocks and spike traps on their tile
    /// (player establish, NPC expand/conquer, vanilla quests).
    /// </summary>
    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.SpawnSetup))]
    public static class Patch_WorldObject_SpawnSetup_ClearTileHazards
    {
        [HarmonyPostfix]
        public static void Postfix(WorldObject __instance)
        {
            if (__instance == null || __instance.Destroyed) return;
            if (!ShouldClearHazardsUnder(__instance)) return;

            int tileId = __instance.Tile.tileId;
            if (tileId < 0) return;

            WorldActions_RoadBlocks.ClearIfPresent(tileId);
            WorldActions_SpikeTraps.ClearIfPresent(tileId);
        }

        private static bool ShouldClearHazardsUnder(WorldObject wo)
        {
            if (wo is Settlement) return true;
            if (wo is WorldObject_WD_Outpost) return true;
            if (wo is Site) return true;
            if (wo is DestroyedSettlement) return true;
            return false;
        }
    }
}
