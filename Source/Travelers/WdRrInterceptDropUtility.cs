using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Rapid Response pods that intercepted a traveler: tag origin + pawns so temp-map teardown
    /// returns living and downed fighters instead of deleting them.
    /// Dest-targeted extras are not registered here.
    /// </summary>
    public static class WdRrInterceptDropUtility
    {
        public static void RegisterOnMap(Map map, WorldObject origin, IReadOnlyList<Pawn> pawns)
        {
            if (map == null || pawns == null || pawns.Count == 0) return;

            WD_MapComponent_CaravanClash clash = map.GetComponent<WD_MapComponent_CaravanClash>();
            if (clash != null && !clash.IsCampClash)
                clash.RegisterInterceptRrDrop(origin, pawns);

            WD_MapComponent_OutpostDefense defense = map.GetComponent<WD_MapComponent_OutpostDefense>();
            defense?.RegisterInterceptRrDrop(origin, pawns);
        }

        public static bool IsTagged(Map map, Pawn pawn)
        {
            if (map == null || pawn == null) return false;
            WD_MapComponent_CaravanClash clash = map.GetComponent<WD_MapComponent_CaravanClash>();
            if (clash != null && clash.IsInterceptRrTagged(pawn))
                return true;
            WD_MapComponent_OutpostDefense defense = map.GetComponent<WD_MapComponent_OutpostDefense>();
            return defense != null && defense.IsInterceptRrTagged(pawn);
        }

        public static void ReturnPawnsToOrigin(WorldObject origin, List<Pawn> pawns, int fallbackTile)
        {
            if (pawns == null || pawns.Count == 0) return;

            var living = new List<Pawn>();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (p.Spawned) p.DeSpawn();
                p.holdingOwner?.Remove(p);
                living.Add(p);
            }
            pawns.Clear();
            if (living.Count == 0) return;

            WorldObject_WD_Outpost originOutpost = origin as WorldObject_WD_Outpost;
            if (originOutpost != null && !originOutpost.Destroyed)
            {
                for (int i = 0; i < living.Count; i++)
                {
                    Pawn p = living[i];
                    if (p == null || p.Destroyed) continue;
                    if (p.Faction != Faction.OfPlayer)
                        p.SetFaction(Faction.OfPlayer);
                    originOutpost.AddPawn(p, null);
                }
                return;
            }

            MapParent originColony = origin as MapParent;
            if (originColony != null && !(originColony is WorldObject_WD_Outpost)
                && !originColony.Destroyed && originColony.HasMap)
            {
                RapidResponseUtility.DropPawnsViaDropPods(living, originColony.Map);
                return;
            }

            int tile = fallbackTile;
            if (origin != null && !origin.Destroyed)
                tile = origin.Tile.tileId;
            if (tile < 0 || !Find.WorldGrid.InBounds(tile))
                tile = Find.AnyPlayerHomeMap?.Tile.tileId ?? tile;
            if (tile >= 0 && Find.WorldGrid.InBounds(tile))
                CaravanMaker.MakeCaravan(living, Faction.OfPlayer, tile, true);
        }

        public static void DumpPawnsAsCaravan(IReadOnlyList<Pawn> pawns, int tile)
        {
            if (pawns == null || pawns.Count == 0) return;
            var living = new List<Pawn>();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (p.Spawned) p.DeSpawn();
                p.holdingOwner?.Remove(p);
                if (p.Faction != Faction.OfPlayer)
                    p.SetFaction(Faction.OfPlayer);
                living.Add(p);
            }
            if (living.Count == 0) return;
            if (tile < 0 || !Find.WorldGrid.InBounds(tile))
                tile = Find.AnyPlayerHomeMap?.Tile.tileId ?? 0;
            CaravanMaker.MakeCaravan(living, Faction.OfPlayer, tile, true);
        }
    }
}
