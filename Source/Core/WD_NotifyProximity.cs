using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Proximity gate for Nearby world event letters (notification radius around the player).</summary>
    public static class WD_NotifyProximity
    {
        public static bool IsWithinPlayerNotificationRadius(int tile)
        {
            if (tile < 0 || Find.WorldGrid == null) return false;
            var seth = WorldDominationMod.settings;
            float radius = Mathf.Clamp(seth?.notificationRadiusTiles ?? WorldDominationSettings.DefNotificationRadiusTiles, 1f, 500f);

            Settlement colony = InfluenceUtils.GetPlayerColony();
            if (colony != null && colony.Tile >= 0)
                return DistanceTiles(tile, colony.Tile) <= radius;

            // No colony yet: notify if within radius of the nearest player WD outpost.
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null || Find.WorldObjects == null) return false;

            float best = float.MaxValue;
            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is not WorldObject_WD_Outpost o || o.Faction != player) continue;
                if (!WorldActions_Utils.IsWdSurfaceWorldObject(o) || o.Tile < 0) continue;
                float d = DistanceTiles(tile, o.Tile);
                if (d < best) best = d;
            }
            return best <= radius;
        }

        private static float DistanceTiles(int a, int b)
        {
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            return manager != null
                ? WorldActions_Utils.GetDistance(a, b, manager)
                : Find.WorldGrid.ApproxDistanceInTiles(a, b);
        }
    }
}
