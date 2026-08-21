using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Single-pawn and bulk remove from outpost (used by pawns tab / details); last-pawn removal confirms outpost destruction.</summary>
    public static class Outpost_RemovePawn
    {
        private static bool RejectIfFrozenDuringManualDefense(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || !outpost.ManualDefenseActive) return false;
            Messages.Message("TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate(), MessageTypeDefOf.RejectInput, false);
            return true;
        }

        private static bool WouldLeaveMechanoidsWithoutOccupants(WorldObject_WD_Outpost outpost, int occupantsBeingRemoved)
        {
            if (outpost == null) return false;
            return outpost.StoredMechanoidPawnCount > 0
                && occupantsBeingRemoved > 0
                && outpost.Occupants.Count == occupantsBeingRemoved;
        }

        /// <summary>Remove pawn from outpost (spawn as caravan). If it's the last pawn, show warning that outpost will be destroyed.</summary>
        public static void TryRemovePawn(WorldObject_WD_Outpost outpost, Pawn pawn)
        {
            if (outpost == null || pawn == null || !outpost.Occupants.Contains(pawn)) return;
            if (RejectIfFrozenDuringManualDefense(outpost)) return;

            if (OutpostPawnIdeologyUtil.IsSlaveHumanlike(pawn))
            {
                Messages.Message("TSA_WD_RemoveSlaveSingleForbidden".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (WouldLeaveMechanoidsWithoutOccupants(outpost, 1))
            {
                Messages.Message("TSA_WD_Pawns_RemoveLastOccupantMechanoidsRemain".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (OutpostStrengthBudget.WithdrawBudgetEnabled)
            {
                var rosterEntries = PlayerPawnRosterUtility.BuildTransferEntriesForOutpost(
                    outpost, new HashSet<string> { pawn.ThingID });
                if (OutpostStrengthBudget.NeedsWithdrawBudgetGate(outpost, rosterEntries))
                {
                    float avail = OutpostStrengthBudget.GetAvailableForWithdraw(outpost);
                    Find.WindowStack.Add(new Dialog_OutpostStrengthWithdraw(
                        outpost,
                        rosterEntries,
                        avail,
                        (take, stay, lost, willEmptyOutpost) =>
                        {
                            // DestroyLostPawns self-destroys the outpost if this empties it (fix covers this path too).
                            OutpostStrengthBudget.DestroyLostPawns(outpost, lost);
                            if (take == null || take.Count == 0)
                            {
                                Messages.Message(
                                    "TSA_WD_StrengthBudget_NoneTransferred".Translate(outpost.Label),
                                    MessageTypeDefOf.NeutralEvent,
                                    false);
                                return;
                            }
                            // Single-pawn context: DoRemove already handles the destroy-if-empty case.
                            DoRemove(outpost, take[0].pawn);
                        }));
                    return;
                }
            }

            bool isLastPawn = outpost.Occupants.Count == 1;
            if (isLastPawn)
            {
                Dialog_MessageBox confirm = Dialog_MessageBox.CreateConfirmation(
                    "TSA_WD_RemoveLastPawnWarning".Translate(outpost.Label),
                    () => DoRemove(outpost, pawn),
                    destructive: true);
                Find.WindowStack.Add(confirm);
            }
            else
            {
                DoRemove(outpost, pawn);
            }
        }

        private static void DoRemove(WorldObject_WD_Outpost outpost, Pawn pawn)
        {
            outpost.RemovePawnAsCaravan(pawn);
            if (outpost.Occupants != null && outpost.Occupants.Count == 0)
                outpost.Destroy();
        }

        /// <summary>Remove multiple pawns in one caravan. Illegal slave-only / slaves-only-remaining selections are blocked in UI; full evacuation still confirms outpost destruction.</summary>
        public static void TryRemovePawnsBulk(WorldObject_WD_Outpost outpost, List<Pawn> pawns)
        {
            if (outpost == null || pawns == null || pawns.Count == 0) return;
            if (RejectIfFrozenDuringManualDefense(outpost)) return;

            var valid = new List<Pawn>();
            var seen = new HashSet<Pawn>();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || !outpost.Occupants.Contains(p) || seen.Contains(p)) continue;
                seen.Add(p);
                valid.Add(p);
            }

            if (valid.Count == 0) return;

            if (!OutpostPawnIdeologyUtil.BulkRemovalSelectionIsAllowed(outpost, valid))
            {
                Messages.Message("TSA_WD_Pawns_RemoveSlaveAccompanimentRequiredTip".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (WouldLeaveMechanoidsWithoutOccupants(outpost, valid.Count))
            {
                Messages.Message("TSA_WD_Pawns_RemoveLastOccupantMechanoidsRemain".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            bool emptiesOutpost = outpost.Occupants.Count == valid.Count;

            void doRemove() => outpost.RemovePawnsAsCaravan(valid);

            if (emptiesOutpost)
            {
                Dialog_MessageBox confirm = Dialog_MessageBox.CreateConfirmation(
                    "TSA_WD_RemoveLastPawnWarning".Translate(outpost.Label),
                    doRemove,
                    destructive: true);
                Find.WindowStack.Add(confirm);
            }
            else
            {
                doRemove();
            }
        }

        public static void TryRemovePawnsAndStoredTransportBulk(
            WorldObject_WD_Outpost outpost,
            List<Pawn> pawns,
            List<Pawn> storedTransportPawns,
            List<Pawn> mechanoidPawns = null,
            List<Building_PassengerShuttle> storedShuttles = null)
        {
            if (outpost == null) return;
            if (RejectIfFrozenDuringManualDefense(outpost)) return;
            bool hasPawns = pawns != null && pawns.Count > 0;
            bool hasStored = storedTransportPawns != null && storedTransportPawns.Count > 0;
            bool hasMechs = mechanoidPawns != null && mechanoidPawns.Count > 0;
            bool hasShuttles = storedShuttles != null && storedShuttles.Count > 0;
            if (!hasPawns && !hasStored && !hasMechs && !hasShuttles) return;

            var validPawns = new List<Pawn>();
            var seenPawns = new HashSet<Pawn>();
            if (pawns != null)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn p = pawns[i];
                    if (p == null || !outpost.Occupants.Contains(p) || seenPawns.Contains(p)) continue;
                    seenPawns.Add(p);
                    validPawns.Add(p);
                }
            }

            var validStored = new List<Pawn>();
            var seenStored = new HashSet<Pawn>();
            if (storedTransportPawns != null)
            {
                for (int i = 0; i < storedTransportPawns.Count; i++)
                {
                    Pawn p = storedTransportPawns[i];
                    if (p == null || !outpost.StoredAnimalsAndVehicles.Contains(p) || seenStored.Contains(p)) continue;
                    seenStored.Add(p);
                    validStored.Add(p);
                }
            }

            var validMechs = new List<Pawn>();
            var seenMechs = new HashSet<Pawn>();
            if (mechanoidPawns != null)
            {
                for (int i = 0; i < mechanoidPawns.Count; i++)
                {
                    Pawn p = mechanoidPawns[i];
                    if (p == null || !outpost.StoredMechanoids.Contains(p) || seenMechs.Contains(p)) continue;
                    seenMechs.Add(p);
                    validMechs.Add(p);
                }
            }

            var validShuttles = new List<Building_PassengerShuttle>();
            var seenShuttles = new HashSet<Thing>();
            if (storedShuttles != null)
            {
                for (int i = 0; i < storedShuttles.Count; i++)
                {
                    Building_PassengerShuttle shuttle = storedShuttles[i];
                    if (shuttle == null || shuttle.Destroyed || seenShuttles.Contains(shuttle)) continue;
                    if (!outpost.StoredPassengerShuttles.Contains(shuttle)) continue;
                    seenShuttles.Add(shuttle);
                    validShuttles.Add(shuttle);
                }
            }

            if (validPawns.Count == 0 && validStored.Count == 0 && validMechs.Count == 0 && validShuttles.Count == 0) return;

            if ((validStored.Count > 0 || validMechs.Count > 0 || validShuttles.Count > 0) && validPawns.Count == 0)
            {
                Messages.Message("TSA_WD_Pawns_RemoveSlaveAccompanimentRequiredTip".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (validPawns.Count > 0 && !OutpostPawnIdeologyUtil.BulkRemovalSelectionIsAllowed(outpost, validPawns))
            {
                Messages.Message("TSA_WD_Pawns_RemoveSlaveAccompanimentRequiredTip".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (WouldLeaveMechanoidsWithoutOccupants(outpost, validPawns.Count))
            {
                Messages.Message("TSA_WD_Pawns_RemoveLastOccupantMechanoidsRemain".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            bool emptiesOutpost = validPawns.Count > 0 && outpost.Occupants.Count == validPawns.Count;

            void doRemove() => outpost.RemovePawnsAndStoredTransportAndMechanoidsAsCaravan(validPawns, validStored, validMechs, validShuttles);

            if (emptiesOutpost)
            {
                Dialog_MessageBox confirm = Dialog_MessageBox.CreateConfirmation(
                    "TSA_WD_RemoveLastPawnWarning".Translate(outpost.Label),
                    doRemove,
                    destructive: true);
                Find.WindowStack.Add(confirm);
            }
            else
            {
                doRemove();
            }
        }
    }
}
