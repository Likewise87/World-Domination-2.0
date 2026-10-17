using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Once-per-day mothballed need hygiene for outpost occupants and prisoners.
    /// Pawns are despawned (not WorldPawns), so needs do not tick; frozen low meters dump on leave.
    /// Does not spend virtual food when topping Food.
    /// </summary>
    public static class Outpost_OccupantNeedsProgression
    {
        /// <summary>
        /// Def names for comfort needs filled while mothballed.
        /// Food/Rest resolve via NeedDefOf; Joy/Beauty/Comfort/Outdoors via DefDatabase
        /// (those NeedDefOf fields are absent on this RW API surface).
        /// </summary>
        private static readonly string[] NamedNeedsToFill =
        {
            "Joy",
            "Beauty",
            "Comfort",
            "Outdoors"
        };

        /// <summary>Max Food / Rest / Joy / Beauty / Comfort / Outdoors when present on the pawn.</summary>
        public static void FillMothballedHumanlikeNeeds(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return;
            if (pawn.RaceProps == null || !pawn.RaceProps.Humanlike) return;
            if (pawn.needs == null) return;

            FillNeed(pawn, NeedDefOf.Food);
            FillNeed(pawn, NeedDefOf.Rest);

            for (int i = 0; i < NamedNeedsToFill.Length; i++)
            {
                NeedDef def = DefDatabase<NeedDef>.GetNamedSilentFail(NamedNeedsToFill[i]);
                FillNeed(pawn, def);
            }
        }

        private static void FillNeed(Pawn pawn, NeedDef def)
        {
            if (def == null) return;
            Need need = pawn.needs.TryGetNeed(def);
            if (need == null) return;
            need.CurLevelPercentage = 1f;
        }

        /// <summary>Once per in-game day: top comfort needs for mothballed occupants.</summary>
        public static void TickOccupantsNeedsOneDay(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return;
            List<Pawn> list = outpost.Occupants;
            if (list == null || list.Count == 0) return;

            for (int i = 0; i < list.Count; i++)
                FillMothballedHumanlikeNeeds(list[i]);
        }

        /// <summary>Once per in-game day: top comfort needs for mothballed prisoners.</summary>
        public static void TickPrisonersNeedsOneDay(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return;
            List<Pawn> list = outpost.Prisoners;
            if (list == null || list.Count == 0) return;

            for (int i = 0; i < list.Count; i++)
                FillMothballedHumanlikeNeeds(list[i]);
        }
    }
}
