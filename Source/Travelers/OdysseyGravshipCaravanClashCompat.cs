using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Caravan-clash exclusion for Odyssey gravships and landed player homes.
    /// Blocks clash on: in-flight <see cref="Gravship"/>, transport-flagged pawns,
    /// MapParent/Settlement/outpost tiles, <see cref="GravshipLaunch"/> markers,
    /// and loaded player-home / GravEngine maps. Does not treat GravEngine cargo in inventory.
    /// </summary>
    public static class OdysseyGravshipCaravanClashCompat
    {
        public static bool ShouldSkipPlayerCaravanClash(Caravan caravan)
        {
            if (caravan == null || caravan.Destroyed)
                return false;

            if (TileBlocksPlayerCaravanClash(caravan.Tile))
            {
                LogSkip("tile blocks caravan clash (home / MapParent / gravship)");
                return true;
            }

            if (!ModsConfig.OdysseyActive)
                return false;

            Gravship current = Find.CurrentGravship;
            var pawns = caravan.PawnsListForReading;
            if (pawns == null) return false;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || p.Destroyed) continue;
                if (p.BeingTransportedOnGravship)
                {
                    LogSkip("pawn BeingTransportedOnGravship");
                    return true;
                }
                if (current != null && current.ContainsPawn(p))
                {
                    LogSkip("pawn on Find.CurrentGravship");
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when this tile must not host a WD player caravan clash (colony / landing / launch site / in-flight).
        /// Safe to call with Odyssey off — Odyssey-only checks are gated.
        /// </summary>
        public static bool TileBlocksPlayerCaravanClash(PlanetTile tile)
        {
            if (!tile.Valid) return false;

            if (TileHasInFlightGravship(tile))
                return true;

            foreach (WorldObject wo in Find.WorldObjects.ObjectsAt(tile))
            {
                if (wo == null || wo.Destroyed) continue;
                // Temporary Ambush clash sites are allowed; everything else with a map parent blocks.
                if (wo is MapParent mp)
                {
                    if (mp.def == WorldObjectDefOf.Ambush) continue;
                    return true;
                }
                if (wo is DestroyedSettlement || wo is WorldObject_WD_Outpost)
                    return true;
                if (ModsConfig.OdysseyActive && wo is GravshipLaunch)
                    return true;
            }

            if (TileHasLoadedPlayerHomeOrGravEngineMap(tile))
                return true;

            return false;
        }

        /// <summary>int tile-id overload for same-tile callers that still use bare ids (surface WD travel).</summary>
        public static bool TileBlocksPlayerCaravanClash(int tileId)
        {
            if (tileId < 0) return false;
            PlanetLayer surface = Find.WorldGrid?.Surface;
            if (surface == null) return false;
            return TileBlocksPlayerCaravanClash(new PlanetTile(tileId, surface));
        }

        /// <summary>Only <see cref="Gravship"/> — not GravshipLaunch, not MapParent with GravEngine.</summary>
        public static bool IsInFlightGravship(WorldObject wo) =>
            ModsConfig.OdysseyActive && wo is Gravship;

        public static bool TileHasInFlightGravship(PlanetTile tile)
        {
            if (!ModsConfig.OdysseyActive || !tile.Valid) return false;
            foreach (WorldObject wo in Find.WorldObjects.ObjectsAt(tile))
            {
                if (IsInFlightGravship(wo))
                    return true;
            }
            return false;
        }

        private static bool TileHasLoadedPlayerHomeOrGravEngineMap(PlanetTile tile)
        {
            var maps = Current.Game?.Maps;
            if (maps == null) return false;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map == null || map.Tile != tile) continue;
                if (map.IsPlayerHome)
                    return true;
                if (ModsConfig.OdysseyActive && MapHasGravEngine(map))
                    return true;
            }
            return false;
        }

        private static bool MapHasGravEngine(Map map)
        {
            if (map?.listerThings == null) return false;
            var list = map.listerThings.ThingsOfDef(ThingDefOf.GravEngine);
            return list != null && list.Count > 0;
        }

        private static void LogSkip(string reason)
        {
            if (Prefs.DevMode)
                Log.Message($"[TSA WD] Skipping caravan clash: {reason}");
        }
    }
}
