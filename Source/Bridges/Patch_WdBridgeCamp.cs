using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Caravan Camp on bridged water uses the same granite-bridge map as caravan clashes.
    /// </summary>
    [HarmonyPatch(typeof(MapParent), "get_MapGeneratorDef")]
    public static class Patch_MapParent_MapGeneratorDef_BridgeCamp
    {
        public static void Postfix(MapParent __instance, ref MapGeneratorDef __result)
        {
            if (__instance == null || WorldObjectDefOf.Camp == null) return;
            if (__instance.def != WorldObjectDefOf.Camp) return;
            if (!WorldComponent_WdBridges.IsBridgedWaterTile(__instance.Tile)) return;

            MapGeneratorDef bridge = DefDatabase<MapGeneratorDef>.GetNamedSilentFail("WD_BridgeClash");
            if (bridge != null)
                __result = bridge;
        }
    }

    /// <summary>
    /// Push caravan approach tiles before bridge-camp map gen so PlayerStartSpot matches the bank they came from.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_GetOrGenerateMap_BridgeCampApproaches
    {
        private static readonly FieldInfo PreviousTileField =
            AccessTools.Field(typeof(Caravan_PathFollower), "previousTileForDrawingIfInDoubt");

        public static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var m in AccessTools.GetDeclaredMethods(typeof(GetOrGenerateMapUtility)))
            {
                if (m.Name == nameof(GetOrGenerateMapUtility.GetOrGenerateMap))
                    yield return m;
            }
        }

        public static void Prefix(PlanetTile tile, WorldObjectDef suggestedMapParentDef)
        {
            if (suggestedMapParentDef == null || WorldObjectDefOf.Camp == null) return;
            if (suggestedMapParentDef != WorldObjectDefOf.Camp) return;
            if (!WorldComponent_WdBridges.IsBridgedWaterTile(tile)) return;

            Caravan caravan = GenStep_WD_BridgeClash.FindPlayerCaravanOnTile(tile);
            int from = ResolveApproachBank(tile.tileId, caravan);
            GenStep_WD_BridgeClash.PushEncounterApproaches(from, -1);
        }

        public static void Finalizer()
        {
            GenStep_WD_BridgeClash.ClearEncounterApproaches();
        }

        internal static int ResolveApproachBank(int waterTileId, Caravan caravan)
        {
            if (caravan == null) return -1;

            int from = GenStep_WD_BridgeClash.CameFromWorldTile(caravan);
            if (!WdBridgeGeometry.TryGetBridgedSpanBanks(waterTileId, out int bankA, out int bankB))
                return from;

            if (from == bankA || from == bankB) return from;

            WorldGrid grid = Find.WorldGrid;
            PlanetLayer layer = PlanetSurfaceWorldActions.LayerOf(caravan);
            PlanetTile tip = default;
            bool haveTip = false;
            if (PreviousTileField != null && caravan.pather != null
                && PreviousTileField.GetValue(caravan.pather) is PlanetTile pt && pt.tileId >= 0)
            {
                tip = pt;
                haveTip = true;
            }
            else if (from >= 0)
            {
                tip = new PlanetTile(from, layer);
                haveTip = true;
            }

            if (haveTip && grid != null)
            {
                if (tip.tileId == bankA) return bankA;
                if (tip.tileId == bankB) return bankB;
                float pa = grid.ApproxDistanceInTiles(tip, new PlanetTile(bankA, layer));
                float pb = grid.ApproxDistanceInTiles(tip, new PlanetTile(bankB, layer));
                return pa <= pb ? bankA : bankB;
            }

            return from >= 0 ? from : bankA;
        }
    }

    /// <summary>
    /// Camp enter uses a room-based cell filter that fails on an open bridge strip.
    /// Force spawn onto the approach-side bridge end.
    /// </summary>
    [HarmonyPatch(typeof(CaravanEnterMapUtility), nameof(CaravanEnterMapUtility.Enter),
        new Type[]
        {
            typeof(Caravan),
            typeof(Map),
            typeof(CaravanEnterMode),
            typeof(CaravanDropInventoryMode),
            typeof(bool),
            typeof(Predicate<IntVec3>)
        })]
    public static class Patch_CaravanEnterMapUtility_Enter_BridgeCamp
    {
        public static bool Prefix(
            Caravan caravan,
            Map map,
            CaravanEnterMode enterMode,
            CaravanDropInventoryMode dropInventoryMode,
            bool draftColonists)
        {
            if (!IsBridgeCampMap(map)) return true;

            int from = Patch_GetOrGenerateMap_BridgeCampApproaches.ResolveApproachBank(
                map.Tile.tileId, caravan);
            GenStep_WD_BridgeClash.ApplyEncounterApproaches(map, from, -1);

            if (!GenStep_WD_BridgeClash.TryFindStandableNear(
                    map, GenStep_WD_BridgeClash.PlayerEndSpawnCell(map), out IntVec3 spawn))
                return true;

            CaravanEnterMapUtility.Enter(caravan, map, _ => spawn, dropInventoryMode, draftColonists);
            return false;
        }

        private static bool IsBridgeCampMap(Map map)
        {
            if (map?.Parent == null || WorldObjectDefOf.Camp == null) return false;
            if (map.Parent.def != WorldObjectDefOf.Camp) return false;
            return WorldComponent_WdBridges.IsBridgedWaterTile(map.Tile);
        }
    }
}
