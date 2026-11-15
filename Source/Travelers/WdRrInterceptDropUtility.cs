using System.Collections.Generic;
using System.Text;
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
        /// <summary>
        /// Fire-and-forget unescorted cargo: free prisoners/slaves into the world (non-player),
        /// destroy mechanoids. Never forms a player caravan or returns to origin.
        /// </summary>
        public static void DisposeUnescortedDropPodCargo(List<Pawn> pawns)
        {
            if (pawns == null || pawns.Count == 0) return;

            int freed = 0;
            int lostMechs = 0;
            var look = new List<Pawn>();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (p.Spawned) p.DeSpawn();
                p.holdingOwner?.Remove(p);

                if (p.RaceProps?.IsMechanoid == true)
                {
                    lostMechs++;
                    p.Destroy(DestroyMode.Vanish);
                    continue;
                }

                bool captive = p.IsPrisonerOfColony || OutpostPawnIdeologyUtil.IsSlaveHumanlike(p);
                if (p.guest != null)
                    p.guest.SetGuestStatus(null);

                Faction releaseTo = p.HomeFaction;
                if (releaseTo == null || releaseTo.IsPlayer || releaseTo == Faction.OfPlayer)
                    releaseTo = null;
                if (releaseTo != null)
                    p.SetFaction(releaseTo);
                else if (p.Faction == Faction.OfPlayer || p.Faction?.IsPlayer == true)
                    p.SetFaction(null);

                Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.Decide);
                if (captive || p.RaceProps?.Humanlike == true)
                    freed++;
                look.Add(p);
            }
            pawns.Clear();

            if (freed <= 0 && lostMechs <= 0) return;
            var sb = new StringBuilder();
            if (freed > 0 && lostMechs > 0)
                sb.Append("TSA_WD_PawnDropPod_UnescortedLostFreedBoth".Translate(freed.ToString(), lostMechs.ToString()));
            else if (freed > 0)
                sb.Append("TSA_WD_PawnDropPod_UnescortedFreed".Translate(freed.ToString()));
            else
                sb.Append("TSA_WD_PawnDropPod_UnescortedLostMechs".Translate(lostMechs.ToString()));
            Find.LetterStack.ReceiveLetter(
                "TSA_WD_PawnDropPod_UnescortedLostLabel".Translate(),
                sb.ToString(),
                LetterDefOf.NegativeEvent,
                look.Count > 0 ? new LookTargets(look) : null);
        }

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

        public static void ReturnPawnsToOrigin(WorldObject origin, List<Pawn> pawns, int fallbackTile, bool unescortedFireAndForget = false)
        {
            if (pawns == null || pawns.Count == 0) return;
            if (unescortedFireAndForget)
            {
                DisposeUnescortedDropPodCargo(pawns);
                return;
            }

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
