using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// After a humanlike colony prisoner is recruited, if they have a scheduled destination,
    /// send them there as a caravan with travel pemmican. Same-colony destination clears and stays put.
    /// </summary>
    [HarmonyPatch(typeof(RecruitUtility), nameof(RecruitUtility.Recruit))]
    public static class Patch_RecruitUtility_Recruit_PrisonerSchedule
    {
        public static void Prefix(Pawn pawn, out bool __state)
        {
            __state = pawn != null
                && !pawn.Destroyed
                && pawn.RaceProps?.Humanlike == true
                && pawn.IsPrisonerOfColony
                && pawn.Spawned;
        }

        public static void Postfix(Pawn pawn, Faction faction, bool __state)
        {
            if (!__state) return;
            if (pawn == null || pawn.Destroyed || pawn.Dead) return;
            if (faction == null || !faction.IsPlayer) return;
            if (pawn.RaceProps?.Humanlike != true) return;

            var schedule = WorldComponent_PrisonerRecruitSchedule.Get();
            if (schedule == null) return;
            if (!schedule.TryGetDestination(pawn.ThingID, out WorldObject_WD_Outpost outpost, out MapParent colony))
                return;

            MapParent currentColony = pawn.Map?.Parent as MapParent;
            string thingId = pawn.ThingID;
            int food = PlayerPawnTransferUtility.RecruitTravelPemmicanPerPawn;

            if (outpost != null)
            {
                if (!PlayerPawnTransferUtility.TryTransferMapPawnToOutpostWithPemmican(pawn, outpost, food))
                    return;

                schedule.Clear(thingId);
                Messages.Message(
                    "TSA_WD_Prisoners_RecruitSent".Translate(pawn.LabelShortCap, outpost.LabelCap),
                    outpost,
                    MessageTypeDefOf.TaskCompletion,
                    false);
                Window_Prisoners.InvalidateCache();
                return;
            }

            if (colony != null)
            {
                // Same colony: stay put after vanilla recruit.
                if (currentColony != null && currentColony.ID == colony.ID)
                {
                    schedule.Clear(thingId);
                    Window_Prisoners.InvalidateCache();
                    return;
                }

                if (!PlayerPawnTransferUtility.TryTransferMapPawnToColonyWithPemmican(pawn, colony, food))
                    return;

                schedule.Clear(thingId);
                Messages.Message(
                    "TSA_WD_Prisoners_RecruitSent".Translate(pawn.LabelShortCap, colony.LabelCap),
                    colony,
                    MessageTypeDefOf.TaskCompletion,
                    false);
                Window_Prisoners.InvalidateCache();
            }
        }
    }
}
