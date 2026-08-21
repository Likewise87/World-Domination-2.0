using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Single Caravan.GetGizmos postfix: vanilla result then WD caravan gizmos in fixed order.</summary>
    [HarmonyPatch(typeof(Caravan), nameof(Caravan.GetGizmos))]
    public static class Patch_CaravanGetGizmos
    {
        private static readonly List<PlanetTile> neighborTiles = new List<PlanetTile>();
        private static readonly HashSet<int> neighborIDs = new HashSet<int>();
        private static readonly List<(Settlement Settlement, CompViralSpread Comp)> neighborScratch =
            new List<(Settlement, CompViralSpread)>();

        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Caravan __instance)
        {
            if (__result != null)
            {
                foreach (Gizmo g in __result)
                    yield return g;
            }

            if (__instance == null || __instance.Destroyed)
                yield break;

            FillNeighborNpcSettlements(__instance, neighborScratch);

            foreach (Gizmo g in Patch_CaravanFoundOutpostGizmo.GetGizmos(__instance))
                yield return g;
            foreach (Gizmo g in Patch_DisinformationGizmo.GetGizmos(__instance, neighborScratch))
                yield return g;
            foreach (Gizmo g in Patch_SabotageGizmo.GetGizmos(__instance, neighborScratch))
                yield return g;
            foreach (Gizmo g in Patch_FortifyGizmo.GetGizmos(__instance, neighborScratch))
                yield return g;
            foreach (Gizmo g in Patch_CaravanChaseCancelGizmo.GetGizmos(__instance))
                yield return g;
        }

        /// <summary>NPC surface settlements on neighboring tiles that have CompViralSpread (hostiles included).</summary>
        public static void FillNeighborNpcSettlements(Caravan caravan, List<(Settlement Settlement, CompViralSpread Comp)> dest)
        {
            dest.Clear();
            if (caravan == null || Find.WorldGrid == null || Find.WorldObjects == null) return;

            neighborTiles.Clear();
            Find.WorldGrid.GetTileNeighbors(caravan.Tile, neighborTiles);
            neighborIDs.Clear();
            for (int i = 0; i < neighborTiles.Count; i++)
                neighborIDs.Add(neighborTiles[i].tileId);

            var settlements = Find.WorldObjects.Settlements;
            if (settlements == null) return;
            for (int si = 0; si < settlements.Count; si++)
            {
                Settlement s = settlements[si];
                if (s == null || !neighborIDs.Contains(s.Tile.tileId) || s.Faction == null || s.Faction.IsPlayer)
                    continue;
                if (!WorldActions_Utils.IsWdSurfaceWorldObject(s)) continue;
                CompViralSpread comp = s.GetComponent<CompViralSpread>();
                if (comp != null)
                    dest.Add((s, comp));
            }
        }
    }
}
