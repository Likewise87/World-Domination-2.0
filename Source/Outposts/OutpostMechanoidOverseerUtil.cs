using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Biotech: release mechanitor bandwidth while mechs are mothballed in <see cref="WorldObject_WD_Outpost.StoredMechanoids"/>.
    /// Sticky prior-overseer ThingID lives on the outpost; restore only when co-located on withdraw.
    /// </summary>
    public static class OutpostMechanoidOverseerUtil
    {
        private static Game scrubbedDeadLinksForGame;

        /// <summary>Clear Overseer (free bandwidth) and remember prior overseer for sticky restore.</summary>
        public static void ClearOverseerForMothball(WorldObject_WD_Outpost outpost, Pawn mech)
        {
            if (!ModsConfig.BiotechActive || outpost == null || mech == null || mech.Destroyed || mech.Dead)
                return;
            if (!OutpostPawnClassificationUtil.IsMechanoidWorker(mech))
                return;

            Pawn overseer = MechanitorUtility.GetOverseer(mech);
            if (overseer != null && !overseer.Destroyed)
                outpost.RememberMothballMechOverseer(mech, overseer);

            if (overseer != null)
                MechanitorUtility.ForceDisconnectMechFromOverseer(mech);
        }

        /// <summary>Clear Overseer on every live stored mech (PostLoad migrate + StoreMechanoid).</summary>
        public static void ClearOverseersForAllStoredMechanoids(WorldObject_WD_Outpost outpost)
        {
            if (!ModsConfig.BiotechActive || outpost == null) return;
            List<Pawn> list = outpost.StoredMechanoids;
            if (list == null || list.Count == 0) return;
            for (int i = 0; i < list.Count; i++)
                ClearOverseerForMothball(outpost, list[i]);
        }

        /// <summary>
        /// Once per Game instance: drop Overseer links to dead/destroyed/missing mechs on player mechanitors
        /// (maps, caravans, and mothballed outpost occupants).
        /// </summary>
        public static void ScrubDeadOverseerSubjectsOncePerGame()
        {
            if (!ModsConfig.BiotechActive) return;
            if (Current.Game == null || Current.Game == scrubbedDeadLinksForGame) return;
            scrubbedDeadLinksForGame = Current.Game;

            HashSet<Pawn> seen = new HashSet<Pawn>();
            void ScrubIfNew(Pawn p)
            {
                if (p == null || p.Destroyed || !seen.Add(p)) return;
                ScrubDeadSubjectsOnMechanitor(p);
            }

            List<Map> maps = Find.Maps;
            if (maps != null)
            {
                for (int mi = 0; mi < maps.Count; mi++)
                {
                    Map map = maps[mi];
                    if (map?.mapPawns == null) continue;
                    List<Pawn> free = map.mapPawns.FreeColonistsSpawned;
                    if (free == null) continue;
                    for (int i = 0; i < free.Count; i++)
                        ScrubIfNew(free[i]);
                }
            }

            List<Caravan> caravans = Find.WorldObjects?.Caravans;
            if (caravans != null)
            {
                for (int ci = 0; ci < caravans.Count; ci++)
                {
                    Caravan caravan = caravans[ci];
                    if (caravan?.PawnsListForReading == null) continue;
                    List<Pawn> pawns = caravan.PawnsListForReading;
                    for (int i = 0; i < pawns.Count; i++)
                        ScrubIfNew(pawns[i]);
                }
            }

            List<WorldObject> worldObjects = Find.WorldObjects?.AllWorldObjects;
            if (worldObjects != null)
            {
                for (int wi = 0; wi < worldObjects.Count; wi++)
                {
                    if (worldObjects[wi] is not WorldObject_WD_Outpost outpost) continue;
                    List<Pawn> occ = outpost.Occupants;
                    if (occ == null) continue;
                    for (int i = 0; i < occ.Count; i++)
                        ScrubIfNew(occ[i]);
                }
            }
        }

        private static void ScrubDeadSubjectsOnMechanitor(Pawn mechanitor)
        {
            if (mechanitor?.mechanitor == null || mechanitor.relations == null) return;

            List<DirectPawnRelation> relations = mechanitor.relations.DirectRelations;
            if (relations == null || relations.Count == 0) return;

            for (int i = relations.Count - 1; i >= 0; i--)
            {
                DirectPawnRelation rel = relations[i];
                if (rel == null || rel.def != PawnRelationDefOf.Overseer) continue;
                Pawn subject = rel.otherPawn;
                if (subject != null && !subject.Destroyed && !subject.Dead) continue;
                if (subject != null)
                    mechanitor.relations.TryRemoveDirectRelation(PawnRelationDefOf.Overseer, subject);
                else
                    mechanitor.relations.RemoveDirectRelation(rel);
            }
        }

        /// <summary>
        /// Re-link sticky overseer if present among <paramref name="coLocated"/> and CanControlMech accepts.
        /// </summary>
        public static bool TryRestoreStickyOverseer(WorldObject_WD_Outpost outpost, Pawn mech, IReadOnlyList<Pawn> coLocated)
        {
            if (!ModsConfig.BiotechActive || outpost == null || mech == null || mech.Destroyed || mech.Dead)
                return false;
            if (MechanitorUtility.GetOverseer(mech) != null)
                return false;

            string overseerId = outpost.PeekMothballMechOverseerId(mech);
            if (overseerId.NullOrEmpty()) return false;

            Pawn overseer = FindPawnByThingId(coLocated, overseerId);
            if (overseer == null || overseer.Destroyed || overseer.Dead || overseer.mechanitor == null)
                return false;

            AcceptanceReport can = MechanitorUtility.CanControlMech(overseer, mech);
            if (!can.Accepted) return false;

            overseer.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
            overseer.mechanitor.AssignPawnControlGroup(mech, preferredWorkMode: null);
            outpost.ClearMothballMechOverseer(mech);
            return true;
        }

        /// <summary>After forming a caravan from an outpost, restore sticky overseers for mechs whose overseer left with them.</summary>
        public static void TryRestoreStickyOverseersInCaravan(WorldObject_WD_Outpost outpost, Caravan caravan)
        {
            if (caravan?.PawnsListForReading == null) return;
            TryRestoreStickyOverseersAmong(outpost, caravan.PawnsListForReading);
        }

        /// <summary>Restore sticky overseers for mechs when the prior overseer is in the same pawn set (caravan / drop-pod launch group).</summary>
        public static void TryRestoreStickyOverseersAmong(WorldObject_WD_Outpost outpost, IReadOnlyList<Pawn> coLocated)
        {
            if (!ModsConfig.BiotechActive || outpost == null || coLocated == null) return;
            for (int i = 0; i < coLocated.Count; i++)
            {
                Pawn p = coLocated[i];
                if (p == null || !OutpostPawnClassificationUtil.IsMechanoidWorker(p)) continue;
                TryRestoreStickyOverseer(outpost, p, coLocated);
            }
        }

        private static Pawn FindPawnByThingId(IReadOnlyList<Pawn> list, string thingId)
        {
            if (list == null || thingId.NullOrEmpty()) return null;
            for (int i = 0; i < list.Count; i++)
            {
                Pawn p = list[i];
                if (p == null) continue;
                if (p.ThingID == thingId || p.GetUniqueLoadID() == thingId)
                    return p;
            }
            return null;
        }
    }
}
