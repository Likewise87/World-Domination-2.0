using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public enum PlayerPawnTransferDestinationKind
    {
        ExitHere,
        Colony,
        Outpost
    }

    public struct PlayerPawnTransferDestination
    {
        public PlayerPawnTransferDestinationKind kind;
        public MapParent? colony;
        public WorldObject_WD_Outpost? outpost;

        public int Tile => kind == PlayerPawnTransferDestinationKind.Colony
            ? colony?.Tile ?? -1
            : outpost?.Tile ?? -1;

        public string Label => kind == PlayerPawnTransferDestinationKind.Colony
            ? colony?.LabelCap ?? "—"
            : outpost?.LabelCap ?? "—";

        public GlobalTargetInfo JumpTarget => kind == PlayerPawnTransferDestinationKind.Colony
            ? new GlobalTargetInfo(colony!)
            : new GlobalTargetInfo(outpost!);
    }

    public static class PlayerPawnTransferUtility
    {
        /// <summary>Travel food packed onto recruit / transfer caravans (long shelf life, light mass).</summary>
        public const int TravelPemmicanPerPawn = 150;

        /// <summary>Virtual outpost food charged per this many travel pemmican.</summary>
        public const int PemmicanPerVirtualFood = 50;

        /// <summary>Virtual food cost for one full <see cref="TravelPemmicanPerPawn"/> pack.</summary>
        public static int VirtualFoodPerTravelPack =>
            TravelPemmicanPerPawn / PemmicanPerVirtualFood;

        public static bool GiveFoodOnPrisonerRecruitTransfer =>
            WorldDominationMod.settings?.giveFoodOnPrisonerRecruitTransfer ?? WorldDominationSettings.DefGiveFoodOnPrisonerRecruitTransfer;

        public static bool GiveFoodOnAllPlayerPawnsTransfer =>
            WorldDominationMod.settings?.giveFoodOnAllPlayerPawnsTransfer ?? WorldDominationSettings.DefGiveFoodOnAllPlayerPawnsTransfer;

        /// <summary>Pemmican per pawn for post-recruit prisoner transfers, or 0 when the setting is off.</summary>
        public static int RecruitTravelPemmicanPerPawn =>
            GiveFoodOnPrisonerRecruitTransfer ? TravelPemmicanPerPawn : 0;

        /// <summary>Humanlike who can escort animals/mechs/vehicles (not a VF vehicle pawn).</summary>
        public static bool IsEscortHumanlike(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return false;
            if (pawn.RaceProps?.Humanlike != true) return false;
            if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(pawn)) return false;
            return true;
        }

        private static bool WouldLeaveMechanoidsWithoutOccupants(WorldObject_WD_Outpost outpost, int occupantsBeingRemoved)
        {
            if (outpost == null) return false;
            return outpost.StoredMechanoidPawnCount > 0
                && occupantsBeingRemoved > 0
                && outpost.Occupants.Count == occupantsBeingRemoved;
        }

        public static bool IsMovableTransferEntry(PlayerPawnRosterEntry e)
        {
            if (e == null || !e.isMovable) return false;
            if (e.outpostRole == PlayerPawnOutpostRole.StoredShuttle)
            {
                return e.sourceOutpost != null
                    && e.shuttle != null
                    && !e.shuttle.Destroyed
                    && OdysseyShuttleOutpostEstablishmentCompat.IsPassengerShuttle(e.shuttle);
            }
            if (e.pawn == null) return false;
            if (e.sourceOutpost != null) return true;
            return e.locationKind == PlayerPawnLocationKind.Colony && e.mapParent != null && e.mapParent.HasMap;
        }

        /// <summary>True if the pawn can be launched into a caravan right now.</summary>
        public static bool IsCapableOfImmediateTransfer(Pawn pawn, out string reasonKey)
        {
            reasonKey = "";
            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                reasonKey = "TSA_WD_PawnTransfer_NotTravelReady_Dead".Translate(pawn?.LabelShort ?? "");
                return false;
            }
            if (pawn.Downed)
            {
                reasonKey = "TSA_WD_PawnTransfer_NotTravelReady_Downed".Translate(pawn.LabelShort);
                return false;
            }
            if (pawn.InMentalState)
            {
                reasonKey = "TSA_WD_PawnTransfer_NotTravelReady_Mental".Translate(pawn.LabelShort);
                return false;
            }
            if (IsInActiveLabor(pawn))
            {
                reasonKey = "TSA_WD_PawnTransfer_NotTravelReady_Labor".Translate(pawn.LabelShort);
                return false;
            }
            // Vehicle Framework vehicle pawns are not walkers; CapableOf(Moving) is often false even when the vehicle can caravan.
            if (!VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(pawn)
                && pawn.health?.capacities != null
                && !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
            {
                reasonKey = "TSA_WD_PawnTransfer_NotTravelReady_Immobile".Translate(pawn.LabelShort);
                return false;
            }
            return true;
        }

        public static bool IsCapableOfImmediateTransfer(Pawn pawn) =>
            IsCapableOfImmediateTransfer(pawn, out _);

        private static bool IsInActiveLabor(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return false;
            if (HediffDefOf.PregnancyLabor != null && pawn.health.hediffSet.HasHediff(HediffDefOf.PregnancyLabor))
                return true;
            if (HediffDefOf.PregnancyLaborPushing != null && pawn.health.hediffSet.HasHediff(HediffDefOf.PregnancyLaborPushing))
                return true;
            return false;
        }

        private static bool RejectIfAnyNotTravelReady(IReadOnlyList<PlayerPawnRosterEntry> selected)
        {
            for (int i = 0; i < selected.Count; i++)
            {
                Pawn p = selected[i]?.pawn;
                if (p == null) continue;
                if (!IsCapableOfImmediateTransfer(p, out string reason))
                {
                    Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                    return true;
                }
            }
            return false;
        }

        public static void TryTransfer(
            IReadOnlyList<PlayerPawnRosterEntry> selected,
            PlayerPawnTransferDestination destination,
            HashSet<WorldObject_WD_Outpost> skipEmptyConfirmFor = null,
            HashSet<WorldObject_WD_Outpost> skipBudgetGateFor = null)
        {
            if (selected == null || selected.Count == 0)
            {
                Messages.Message("TSA_WD_PawnTransfer_NoSelection".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            for (int i = 0; i < selected.Count; i++)
            {
                if (!IsMovableTransferEntry(selected[i]))
                {
                    Messages.Message("TSA_WD_PawnTransfer_NotMovable".Translate(), MessageTypeDefOf.RejectInput, false);
                    return;
                }
            }

            if (RejectIfAnyNotTravelReady(selected))
                return;

            if (destination.kind != PlayerPawnTransferDestinationKind.ExitHere && destination.Tile < 0)
            {
                Messages.Message("TSA_WD_PawnTransfer_InvalidDestination".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (destination.kind == PlayerPawnTransferDestinationKind.Outpost
                && destination.outpost != null
                && AllFromSameOutpost(selected, destination.outpost))
            {
                Messages.Message("TSA_WD_PawnTransfer_SameDestination".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (destination.kind == PlayerPawnTransferDestinationKind.Outpost
                && destination.outpost != null
                && destination.outpost.ManualDefenseActive)
            {
                Messages.Message("TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (destination.kind == PlayerPawnTransferDestinationKind.Colony
                && destination.colony != null
                && AllFromSameColony(selected, destination.colony))
            {
                Messages.Message("TSA_WD_PawnTransfer_SameDestination".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            var byOutpost = new Dictionary<WorldObject_WD_Outpost, List<PlayerPawnRosterEntry>>();
            var byColony = new Dictionary<MapParent, List<PlayerPawnRosterEntry>>();
            for (int i = 0; i < selected.Count; i++)
            {
                PlayerPawnRosterEntry e = selected[i];
                if (e.sourceOutpost != null)
                {
                    if (!byOutpost.TryGetValue(e.sourceOutpost, out var list))
                    {
                        list = new List<PlayerPawnRosterEntry>();
                        byOutpost[e.sourceOutpost] = list;
                    }
                    list.Add(e);
                }
                else if (e.mapParent != null)
                {
                    if (!byColony.TryGetValue(e.mapParent, out var list))
                    {
                        list = new List<PlayerPawnRosterEntry>();
                        byColony[e.mapParent] = list;
                    }
                    list.Add(e);
                }
            }

            foreach (var kv in byOutpost)
            {
                if (!ValidateOutpostGroup(kv.Key, kv.Value, out string reject))
                {
                    Messages.Message(reject, MessageTypeDefOf.RejectInput, false);
                    return;
                }
            }

            foreach (var kv in byColony)
            {
                if (!ValidateColonyGroup(kv.Key, kv.Value, out string reject))
                {
                    Messages.Message(reject, MessageTypeDefOf.RejectInput, false);
                    return;
                }
            }

            // Offensive-strength budget: choose who transfers, stays, or is lost when over budget.
            foreach (var kv in byOutpost)
            {
                if (skipBudgetGateFor != null && skipBudgetGateFor.Contains(kv.Key)) continue;
                if (!OutpostStrengthBudget.NeedsWithdrawBudgetGate(kv.Key, kv.Value)) continue;
                WorldObject_WD_Outpost gatedOutpost = kv.Key;
                List<PlayerPawnRosterEntry> gatedGroup = kv.Value;
                float avail = OutpostStrengthBudget.GetAvailableForWithdraw(gatedOutpost);
                Find.WindowStack.Add(new Dialog_OutpostStrengthWithdraw(
                    gatedOutpost,
                    gatedGroup,
                    avail,
                    (take, stay, lost, willEmptyOutpost) =>
                    {
                        OutpostStrengthBudget.DestroyLostPawns(gatedOutpost, lost);
                        // stay: remain on the outpost (not transferred, not destroyed).
                        var next = new List<PlayerPawnRosterEntry>();
                        for (int i = 0; i < selected.Count; i++)
                        {
                            PlayerPawnRosterEntry e = selected[i];
                            if (e?.sourceOutpost == gatedOutpost) continue;
                            next.Add(e);
                        }
                        if (take != null)
                        {
                            for (int i = 0; i < take.Count; i++)
                                next.Add(take[i]);
                        }
                        if (next.Count == 0)
                        {
                            Messages.Message(
                                "TSA_WD_StrengthBudget_NoneTransferred".Translate(gatedOutpost.Label),
                                MessageTypeDefOf.NeutralEvent,
                                false);
                            return;
                        }
                        // The fate dialog already warned about abandoning gatedOutpost (if applicable) with
                        // full knowledge of the lost pawns; do not re-prompt for the same outcome below.
                        // DestroyLostPawns also drops available strength, so do not re-open the budget gate
                        // for the same outpost — the player already resolved it.
                        HashSet<WorldObject_WD_Outpost> skipEmpty = skipEmptyConfirmFor;
                        if (willEmptyOutpost)
                        {
                            skipEmpty = skipEmptyConfirmFor != null
                                ? new HashSet<WorldObject_WD_Outpost>(skipEmptyConfirmFor)
                                : new HashSet<WorldObject_WD_Outpost>();
                            skipEmpty.Add(gatedOutpost);
                        }
                        HashSet<WorldObject_WD_Outpost> skipBudget = skipBudgetGateFor != null
                            ? new HashSet<WorldObject_WD_Outpost>(skipBudgetGateFor)
                            : new HashSet<WorldObject_WD_Outpost>();
                        skipBudget.Add(gatedOutpost);
                        TryTransfer(next, destination, skipEmpty, skipBudget);
                    }));
                return;
            }

            bool needsConfirm = false;
            foreach (var kv in byOutpost)
            {
                if (skipEmptyConfirmFor != null && skipEmptyConfirmFor.Contains(kv.Key)) continue;
                if (GroupEmptiesOutpost(kv.Key, kv.Value))
                {
                    needsConfirm = true;
                    break;
                }
            }

            if (needsConfirm)
            {
                string warnLabel = "TSA_WD_PawnTransfer_MultipleOutposts".Translate();
                if (byOutpost.Count == 1)
                {
                    foreach (var k in byOutpost.Keys) { warnLabel = k.Label; break; }
                }
                Dialog_MessageBox confirm = Dialog_MessageBox.CreateConfirmation(
                    "TSA_WD_RemoveLastPawnWarning".Translate(warnLabel),
                    () => ExecuteAllTransfers(byOutpost, byColony, destination),
                    destructive: true);
                Find.WindowStack.Add(confirm);
                return;
            }

            ExecuteAllTransfers(byOutpost, byColony, destination);
        }

        public struct PlayerPawnTransferAssignment
        {
            public PlayerPawnRosterEntry entry;
            public PlayerPawnTransferDestination destination;
        }

        /// <summary>
        /// Transfer pawns that may each have a different destination. Same origin + same dest = one caravan.
        /// Validates cumulative leave-behind rules per origin before any launch.
        /// </summary>
        public static bool TryTransferWithPerPawnDestinations(
            IReadOnlyList<PlayerPawnTransferAssignment> assignments,
            HashSet<WorldObject_WD_Outpost> skipEmptyConfirmFor = null,
            HashSet<WorldObject_WD_Outpost> skipBudgetGateFor = null)
        {
            if (assignments == null || assignments.Count == 0)
            {
                Messages.Message("TSA_WD_PawnTransfer_NoSelection".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            var entries = new List<PlayerPawnRosterEntry>(assignments.Count);
            for (int i = 0; i < assignments.Count; i++)
            {
                PlayerPawnRosterEntry e = assignments[i].entry;
                if (!IsMovableTransferEntry(e))
                {
                    Messages.Message("TSA_WD_PawnTransfer_NotMovable".Translate(), MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                if (!IsCapableOfImmediateTransfer(e.pawn, out string reason))
                {
                    Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                PlayerPawnTransferDestination destination = assignments[i].destination;
                if (destination.kind != PlayerPawnTransferDestinationKind.ExitHere && destination.Tile < 0)
                {
                    Messages.Message("TSA_WD_PawnTransfer_InvalidDestination".Translate(), MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                if (destination.kind == PlayerPawnTransferDestinationKind.Outpost
                    && destination.outpost != null
                    && e.sourceOutpost == destination.outpost)
                {
                    Messages.Message("TSA_WD_PawnTransfer_SameDestination".Translate(), MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                if (destination.kind == PlayerPawnTransferDestinationKind.Outpost
                    && destination.outpost != null
                    && destination.outpost.ManualDefenseActive)
                {
                    Messages.Message("TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate(), MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                if (destination.kind == PlayerPawnTransferDestinationKind.Colony
                    && destination.colony != null
                    && e.sourceOutpost == null
                    && e.mapParent == destination.colony)
                {
                    Messages.Message("TSA_WD_PawnTransfer_SameDestination".Translate(), MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                entries.Add(e);
            }

            // Cumulative leavers per origin (all dests combined).
            var byOutpostAll = new Dictionary<WorldObject_WD_Outpost, List<PlayerPawnRosterEntry>>();
            var byColonyAll = new Dictionary<MapParent, List<PlayerPawnRosterEntry>>();
            for (int i = 0; i < entries.Count; i++)
            {
                PlayerPawnRosterEntry e = entries[i];
                if (e.sourceOutpost != null)
                {
                    if (!byOutpostAll.TryGetValue(e.sourceOutpost, out var list))
                    {
                        list = new List<PlayerPawnRosterEntry>();
                        byOutpostAll[e.sourceOutpost] = list;
                    }
                    list.Add(e);
                }
                else if (e.mapParent != null)
                {
                    if (!byColonyAll.TryGetValue(e.mapParent, out var list))
                    {
                        list = new List<PlayerPawnRosterEntry>();
                        byColonyAll[e.mapParent] = list;
                    }
                    list.Add(e);
                }
            }

            foreach (var kv in byOutpostAll)
            {
                if (!ValidateOutpostGroup(kv.Key, kv.Value, out string reject))
                {
                    Messages.Message(reject, MessageTypeDefOf.RejectInput, false);
                    return false;
                }
            }

            foreach (var kv in byColonyAll)
            {
                if (!ValidateColonyGroup(kv.Key, kv.Value, out string reject))
                {
                    Messages.Message(reject, MessageTypeDefOf.RejectInput, false);
                    return false;
                }
            }

            // Offensive-strength budget: same gate as TryTransfer, so Smart Assign can't bypass it.
            foreach (var kv in byOutpostAll)
            {
                if (skipBudgetGateFor != null && skipBudgetGateFor.Contains(kv.Key)) continue;
                if (!OutpostStrengthBudget.NeedsWithdrawBudgetGate(kv.Key, kv.Value)) continue;
                WorldObject_WD_Outpost gatedOutpost = kv.Key;
                float avail = OutpostStrengthBudget.GetAvailableForWithdraw(gatedOutpost);
                Find.WindowStack.Add(new Dialog_OutpostStrengthWithdraw(
                    gatedOutpost,
                    kv.Value,
                    avail,
                    (take, stay, lost, willEmptyOutpost) =>
                    {
                        OutpostStrengthBudget.DestroyLostPawns(gatedOutpost, lost);
                        var takeIds = new HashSet<string>();
                        if (take != null)
                        {
                            for (int i = 0; i < take.Count; i++)
                                if (!take[i].thingId.NullOrEmpty()) takeIds.Add(take[i].thingId);
                        }
                        // stay/lost pawns are dropped from the batch; take pawns keep their original per-pawn destination.
                        var nextAssignments = new List<PlayerPawnTransferAssignment>();
                        for (int i = 0; i < assignments.Count; i++)
                        {
                            PlayerPawnRosterEntry e = assignments[i].entry;
                            if (e?.sourceOutpost == gatedOutpost && !takeIds.Contains(e.thingId)) continue;
                            nextAssignments.Add(assignments[i]);
                        }
                        if (nextAssignments.Count == 0)
                        {
                            Messages.Message(
                                "TSA_WD_StrengthBudget_NoneTransferred".Translate(gatedOutpost.Label),
                                MessageTypeDefOf.NeutralEvent,
                                false);
                            return;
                        }
                        HashSet<WorldObject_WD_Outpost> skipEmpty = skipEmptyConfirmFor;
                        if (willEmptyOutpost)
                        {
                            skipEmpty = skipEmptyConfirmFor != null
                                ? new HashSet<WorldObject_WD_Outpost>(skipEmptyConfirmFor)
                                : new HashSet<WorldObject_WD_Outpost>();
                            skipEmpty.Add(gatedOutpost);
                        }
                        HashSet<WorldObject_WD_Outpost> skipBudget = skipBudgetGateFor != null
                            ? new HashSet<WorldObject_WD_Outpost>(skipBudgetGateFor)
                            : new HashSet<WorldObject_WD_Outpost>();
                        skipBudget.Add(gatedOutpost);
                        TryTransferWithPerPawnDestinations(nextAssignments, skipEmpty, skipBudget);
                    }));
                return true;
            }

            bool needsConfirm = false;
            string warnLabel = "TSA_WD_PawnTransfer_MultipleOutposts".Translate();
            foreach (var kv in byOutpostAll)
            {
                if (skipEmptyConfirmFor != null && skipEmptyConfirmFor.Contains(kv.Key)) continue;
                if (GroupEmptiesOutpost(kv.Key, kv.Value))
                {
                    needsConfirm = true;
                    if (byOutpostAll.Count == 1)
                        warnLabel = kv.Key.Label;
                    break;
                }
            }

            void Execute()
            {
                var outpostGroups = new Dictionary<(WorldObject_WD_Outpost source, int destKey), (List<PlayerPawnRosterEntry> list, PlayerPawnTransferDestination dest)>();
                var colonyGroups = new Dictionary<(MapParent source, int destKey), (List<PlayerPawnRosterEntry> list, PlayerPawnTransferDestination dest)>();

                for (int i = 0; i < assignments.Count; i++)
                {
                    PlayerPawnRosterEntry e = assignments[i].entry;
                    PlayerPawnTransferDestination dest = assignments[i].destination;
                    int destKey = DestGroupKey(dest);
                    if (e.sourceOutpost != null)
                    {
                        var key = (e.sourceOutpost, destKey);
                        if (!outpostGroups.TryGetValue(key, out var g))
                        {
                            g = (new List<PlayerPawnRosterEntry>(), dest);
                            outpostGroups[key] = g;
                        }
                        g.list.Add(e);
                        outpostGroups[key] = g;
                    }
                    else if (e.mapParent != null)
                    {
                        var key = (e.mapParent, destKey);
                        if (!colonyGroups.TryGetValue(key, out var g))
                        {
                            g = (new List<PlayerPawnRosterEntry>(), dest);
                            colonyGroups[key] = g;
                        }
                        g.list.Add(e);
                        colonyGroups[key] = g;
                    }
                }

                foreach (var kv in outpostGroups)
                    ExecuteOutpostGroupTransfer(kv.Key.source, kv.Value.list, kv.Value.dest);
                foreach (var kv in colonyGroups)
                    ExecuteColonyGroupTransfer(kv.Key.source, kv.Value.list, kv.Value.dest);
            }

            if (needsConfirm)
            {
                Dialog_MessageBox confirm = Dialog_MessageBox.CreateConfirmation(
                    "TSA_WD_RemoveLastPawnWarning".Translate(warnLabel),
                    Execute,
                    destructive: true);
                Find.WindowStack.Add(confirm);
                return true;
            }

            Execute();
            return true;
        }

        private static int DestGroupKey(PlayerPawnTransferDestination dest)
        {
            if (dest.kind == PlayerPawnTransferDestinationKind.ExitHere)
                return -1;
            if (dest.kind == PlayerPawnTransferDestinationKind.Outpost && dest.outpost != null)
                return dest.outpost.ID;
            if (dest.kind == PlayerPawnTransferDestinationKind.Colony && dest.colony != null)
                return unchecked((int)0x40000000) ^ dest.colony.ID;
            return dest.Tile;
        }

        private static bool AllFromSameOutpost(IReadOnlyList<PlayerPawnRosterEntry> selected, WorldObject_WD_Outpost outpost)
        {
            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i].sourceOutpost != outpost) return false;
            }
            return true;
        }

        private static bool AllFromSameColony(IReadOnlyList<PlayerPawnRosterEntry> selected, MapParent colony)
        {
            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i].mapParent != colony || selected[i].sourceOutpost != null) return false;
            }
            return true;
        }

        private static void ExecuteAllTransfers(
            Dictionary<WorldObject_WD_Outpost, List<PlayerPawnRosterEntry>> byOutpost,
            Dictionary<MapParent, List<PlayerPawnRosterEntry>> byColony,
            PlayerPawnTransferDestination destination)
        {
            foreach (var kv in byOutpost)
                ExecuteOutpostGroupTransfer(kv.Key, kv.Value, destination);
            foreach (var kv in byColony)
                ExecuteColonyGroupTransfer(kv.Key, kv.Value, destination);
        }

        private static bool ValidateOutpostGroup(WorldObject_WD_Outpost outpost, List<PlayerPawnRosterEntry> group, out string reject)
        {
            reject = null!;
            if (outpost != null && outpost.ManualDefenseActive)
            {
                reject = "TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate();
                return false;
            }

            var occupants = new List<Pawn>();
            var stored = new List<Pawn>();
            var mechs = new List<Pawn>();
            int shuttleCount = 0;

            for (int i = 0; i < group.Count; i++)
            {
                PlayerPawnRosterEntry entry = group[i];
                if (entry.outpostRole == PlayerPawnOutpostRole.StoredShuttle)
                {
                    Thing? shuttle = entry.shuttle;
                    if (shuttle != null
                        && !shuttle.Destroyed
                        && OdysseyShuttleOutpostEstablishmentCompat.IsPassengerShuttle(shuttle)
                        && outpost.StoredPassengerShuttles.Contains(shuttle))
                        shuttleCount++;
                    continue;
                }

                Pawn p = entry.pawn;
                if (p == null) continue;
                switch (entry.outpostRole)
                {
                    case PlayerPawnOutpostRole.Occupant:
                        if (outpost.Occupants.Contains(p)) occupants.Add(p);
                        break;
                    case PlayerPawnOutpostRole.StoredTransport:
                        if (outpost.StoredAnimalsAndVehicles.Contains(p)) stored.Add(p);
                        break;
                    case PlayerPawnOutpostRole.StoredMechanoid:
                        if (outpost.StoredMechanoids.Contains(p)) mechs.Add(p);
                        break;
                }
            }

            if (shuttleCount > 0)
            {
                bool hasEscort = false;
                for (int i = 0; i < occupants.Count; i++)
                {
                    if (IsEscortHumanlike(occupants[i]) && IsCapableOfImmediateTransfer(occupants[i]))
                    {
                        hasEscort = true;
                        break;
                    }
                }
                if (!hasEscort)
                {
                    reject = "TSA_WD_Outpost_ShuttleNeedsHumanColonist".Translate();
                    return false;
                }
            }

            if ((stored.Count > 0 || mechs.Count > 0) && occupants.Count == 0)
            {
                reject = "TSA_WD_Pawns_RemoveSlaveAccompanimentRequiredTip".Translate();
                return false;
            }

            if (occupants.Count > 0 && !OutpostPawnIdeologyUtil.BulkRemovalSelectionIsAllowed(outpost, occupants))
            {
                reject = "TSA_WD_Pawns_RemoveSlaveAccompanimentRequiredTip".Translate();
                return false;
            }

            if (WouldLeaveMechanoidsWithoutOccupants(outpost, occupants.Count))
            {
                reject = "TSA_WD_Pawns_RemoveLastOccupantMechanoidsRemain".Translate();
                return false;
            }

            return true;
        }

        /// <summary>Colony transfer: leave ≥1 escort humanlike behind; non-humans/slaves need an escort leaver.</summary>
        public static bool ValidateColonyLeavingPawns(MapParent mapParent, IReadOnlyList<Pawn> leaving, out string reject)
        {
            reject = null!;
            if (mapParent?.Map == null || leaving == null || leaving.Count == 0)
            {
                reject = "TSA_WD_PawnTransfer_NoSelection".Translate();
                return false;
            }

            Map map = mapParent.Map;
            var leave = new HashSet<Pawn>();
            for (int i = 0; i < leaving.Count; i++)
            {
                Pawn p = leaving[i];
                if (p != null && !p.Destroyed && !p.Dead)
                    leave.Add(p);
            }
            if (leave.Count == 0)
            {
                reject = "TSA_WD_PawnTransfer_NoSelection".Translate();
                return false;
            }

            int escortLeaving = 0;
            int nonSlaveEscortLeaving = 0;
            int nonEscortLeaving = 0;
            bool anySlaveLeaving = false;
            foreach (Pawn p in leave)
            {
                if (IsEscortHumanlike(p))
                {
                    escortLeaving++;
                    if (!OutpostPawnIdeologyUtil.IsSlaveHumanlike(p))
                        nonSlaveEscortLeaving++;
                    else
                        anySlaveLeaving = true;
                }
                else
                {
                    nonEscortLeaving++;
                    if (OutpostPawnIdeologyUtil.IsSlaveHumanlike(p))
                        anySlaveLeaving = true;
                }
            }

            if ((nonEscortLeaving > 0 || anySlaveLeaving) && nonSlaveEscortLeaving == 0)
            {
                reject = "TSA_WD_Pawns_RemoveSlaveAccompanimentRequiredTip".Translate();
                return false;
            }

            int remainingEscorts = 0;
            int remainingNonSlaveEscorts = 0;
            CollectColonyMapPlayerPawns(map, out List<Pawn> mapPawns);
            for (int i = 0; i < mapPawns.Count; i++)
            {
                Pawn p = mapPawns[i];
                if (leave.Contains(p)) continue;
                if (IsEscortHumanlike(p))
                {
                    remainingEscorts++;
                    if (!OutpostPawnIdeologyUtil.IsSlaveHumanlike(p))
                        remainingNonSlaveEscorts++;
                }
            }

            if (remainingEscorts < 1)
            {
                reject = "TSA_WD_PawnTransfer_LeaveOneBehind".Translate();
                return false;
            }

            if (ModsConfig.IdeologyActive && remainingNonSlaveEscorts < 1)
            {
                reject = "TSA_WD_Pawns_RemoveSlaveAccompanimentRequiredTip".Translate();
                return false;
            }

            return true;
        }

        public static bool ColonyBulkSelectionIsAllowedWithExtra(
            MapParent mapParent,
            HashSet<string> selectedThingIds,
            Pawn extraIfNotYetSelected,
            IReadOnlyList<PlayerPawnRosterEntry> roster)
        {
            if (mapParent == null || selectedThingIds == null || extraIfNotYetSelected?.ThingID == null || roster == null)
                return false;
            if (selectedThingIds.Contains(extraIfNotYetSelected.ThingID))
                return true;

            var leaving = new List<Pawn>();
            for (int i = 0; i < roster.Count; i++)
            {
                PlayerPawnRosterEntry e = roster[i];
                if (e.mapParent != mapParent || e.sourceOutpost != null) continue;
                if (e.pawn == null || e.thingId.NullOrEmpty()) continue;
                if (selectedThingIds.Contains(e.thingId) || e.thingId == extraIfNotYetSelected.ThingID)
                    leaving.Add(e.pawn);
            }

            return ValidateColonyLeavingPawns(mapParent, leaving, out _);
        }

        private static bool ValidateColonyGroup(MapParent mapParent, List<PlayerPawnRosterEntry> group, out string reject)
        {
            var leaving = new List<Pawn>(group.Count);
            for (int i = 0; i < group.Count; i++)
            {
                Pawn p = group[i].pawn;
                if (p != null) leaving.Add(p);
            }
            return ValidateColonyLeavingPawns(mapParent, leaving, out reject);
        }

        /// <summary>Public leave-behind / ideology checks for a colony group (remote establish, transfer).</summary>
        public static bool TryValidateColonyLeavingGroup(MapParent mapParent, List<PlayerPawnRosterEntry> group, out string reject) =>
            ValidateColonyGroup(mapParent, group, out reject);

        /// <summary>
        /// Manual add-to-outpost: leftover caravan must be empty or keep an escort; moving non-escorts/slaves need a free escort
        /// unless the whole caravan is selected (dissolve). Empty outposts cannot receive only animals/vehicles/mechs.
        /// </summary>
        public static bool ValidateCaravanAddToOutpostSelection(
            WorldObject_WD_Outpost outpost,
            Caravan caravan,
            HashSet<string> selectedIds,
            out string reject)
        {
            reject = null!;
            if (outpost == null || caravan == null || caravan.Destroyed || selectedIds == null || selectedIds.Count == 0)
            {
                reject = "TSA_WD_PawnTransfer_NoSelection".Translate();
                return false;
            }

            if (outpost.ManualDefenseActive)
            {
                reject = "TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate();
                return false;
            }

            var reading = caravan.PawnsListForReading;
            if (reading == null || reading.Count == 0)
            {
                reject = "TSA_WD_AddToOutpost_NoValidPawns".Translate();
                return false;
            }

            var selected = new List<Pawn>();
            var remaining = new List<Pawn>();
            int livingOnCaravan = 0;
            for (int i = 0; i < reading.Count; i++)
            {
                Pawn p = reading[i];
                if (p == null || p.Destroyed || p.Dead || p.ThingID.NullOrEmpty()) continue;
                livingOnCaravan++;
                if (selectedIds.Contains(p.ThingID))
                    selected.Add(p);
                else
                    remaining.Add(p);
            }

            if (selected.Count == 0)
            {
                reject = "TSA_WD_PawnTransfer_NoSelection".Translate();
                return false;
            }

            bool fullDissolve = remaining.Count == 0 && selected.Count == livingOnCaravan;

            // Leftover caravan: empty (dissolve) OR ≥1 escort; Ideology: ≥1 non-slave escort if anyone remains.
            if (remaining.Count > 0)
            {
                int remainingEscorts = 0;
                int remainingNonSlaveEscorts = 0;
                for (int i = 0; i < remaining.Count; i++)
                {
                    Pawn p = remaining[i];
                    if (!IsEscortHumanlike(p)) continue;
                    remainingEscorts++;
                    if (!OutpostPawnIdeologyUtil.IsSlaveHumanlike(p))
                        remainingNonSlaveEscorts++;
                }

                if (remainingEscorts < 1)
                {
                    reject = "TSA_WD_AddToOutpost_LeaveEscortBehind".Translate();
                    return false;
                }

                if (ModsConfig.IdeologyActive && remainingNonSlaveEscorts < 1)
                {
                    reject = "TSA_WD_Pawns_RemoveSlaveAccompanimentRequiredTip".Translate();
                    return false;
                }
            }

            // Moving-set accompaniment unless full dissolve.
            if (!fullDissolve)
            {
                bool anyNonEscortOrSlave = false;
                int nonSlaveEscortsMoving = 0;
                for (int i = 0; i < selected.Count; i++)
                {
                    Pawn p = selected[i];
                    if (IsEscortHumanlike(p))
                    {
                        if (OutpostPawnIdeologyUtil.IsSlaveHumanlike(p))
                            anyNonEscortOrSlave = true;
                        else
                            nonSlaveEscortsMoving++;
                    }
                    else
                    {
                        anyNonEscortOrSlave = true;
                    }
                }

                if (anyNonEscortOrSlave && nonSlaveEscortsMoving < 1)
                {
                    reject = "TSA_WD_Pawns_RemoveSlaveAccompanimentRequiredTip".Translate();
                    return false;
                }
            }

            // Empty outpost cannot receive only animals/vehicles/mechs.
            int livingOccupants = CountLivingOccupants(outpost);
            if (livingOccupants == 0)
            {
                bool anyNonEscortSelected = false;
                bool anyEscortSelected = false;
                for (int i = 0; i < selected.Count; i++)
                {
                    Pawn p = selected[i];
                    if (IsEscortHumanlike(p))
                        anyEscortSelected = true;
                    else
                        anyNonEscortSelected = true;
                }

                if (anyNonEscortSelected && !anyEscortSelected)
                {
                    reject = "TSA_WD_AddToOutpost_NeedOccupantWithTransport".Translate();
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checkbox probe: if <paramref name="thingId"/> is already selected, whether deselecting it stays valid;
        /// otherwise whether selecting it stays valid.
        /// </summary>
        public static bool CaravanAddBulkSelectionIsAllowedWithToggle(
            WorldObject_WD_Outpost outpost,
            Caravan caravan,
            HashSet<string> selectedThingIds,
            string thingId,
            out string reject)
        {
            reject = null!;
            if (outpost == null || caravan == null || selectedThingIds == null || thingId.NullOrEmpty())
                return false;

            var probe = new HashSet<string>(selectedThingIds);
            if (probe.Contains(thingId))
                probe.Remove(thingId);
            else
                probe.Add(thingId);

            if (probe.Count == 0)
                return true; // empty selection is idle UI, not an invalid transfer

            return ValidateCaravanAddToOutpostSelection(outpost, caravan, probe, out reject);
        }

        public static bool TryAddSelectedCaravanPawnsToOutpost(
            WorldObject_WD_Outpost outpost,
            Caravan caravan,
            HashSet<string> selectedIds,
            out string reject)
        {
            reject = null!;
            if (!ValidateCaravanAddToOutpostSelection(outpost, caravan, selectedIds, out reject))
                return false;

            var reading = caravan.PawnsListForReading;
            if (reading == null) return false;

            var selectedEscorts = new List<Pawn>();
            var selectedNonEscorts = new List<Pawn>();
            int livingEscortsOnCaravan = 0;
            for (int i = 0; i < reading.Count; i++)
            {
                Pawn p = reading[i];
                if (p == null || p.Destroyed || p.Dead || p.ThingID.NullOrEmpty()) continue;
                if (IsEscortHumanlike(p))
                    livingEscortsOnCaravan++;
                if (!selectedIds.Contains(p.ThingID)) continue;
                if (IsEscortHumanlike(p))
                    selectedEscorts.Add(p);
                else
                    selectedNonEscorts.Add(p);
            }

            bool fullDissolve = selectedEscorts.Count > 0 && selectedEscorts.Count == livingEscortsOnCaravan;

            if (fullDissolve)
            {
                // Last humanlike AddCaravanPawnToOutpost dissolves animals/vehicles/mechs.
                for (int i = 0; i < selectedEscorts.Count; i++)
                {
                    if (caravan.Destroyed) break;
                    Pawn p = selectedEscorts[i];
                    if (p == null || p.Destroyed || p.Dead) continue;
                    if (!CaravanStillHasPawn(caravan, p)) continue;
                    outpost.AddCaravanPawnToOutpost(p, caravan);
                }
                return true;
            }

            for (int i = 0; i < selectedNonEscorts.Count; i++)
            {
                if (caravan.Destroyed) break;
                Pawn p = selectedNonEscorts[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (!CaravanStillHasPawn(caravan, p)) continue;
                outpost.AddCaravanPawnToOutpostRouted(p, caravan);
            }

            for (int i = 0; i < selectedEscorts.Count; i++)
            {
                if (caravan.Destroyed) break;
                Pawn p = selectedEscorts[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (!CaravanStillHasPawn(caravan, p)) continue;
                outpost.AddCaravanPawnToOutpost(p, caravan);
            }

            return true;
        }

        private static int CountLivingOccupants(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.Occupants == null) return 0;
            int n = 0;
            for (int i = 0; i < outpost.Occupants.Count; i++)
            {
                Pawn p = outpost.Occupants[i];
                if (p != null && !p.Destroyed && !p.Dead) n++;
            }
            return n;
        }

        private static bool CaravanStillHasPawn(Caravan caravan, Pawn pawn)
        {
            if (caravan == null || caravan.Destroyed || pawn == null) return false;
            var list = caravan.PawnsListForReading;
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], pawn)) return true;
            }
            return false;
        }

        private static void CollectColonyMapPlayerPawns(Map map, out List<Pawn> result)
        {
            result = new List<Pawn>();
            Faction player = Faction.OfPlayer;
            if (map?.mapPawns?.AllPawnsSpawned == null || player == null) return;
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (p.Faction != player) continue;
                result.Add(p);
            }
        }

        private static bool GroupEmptiesOutpost(WorldObject_WD_Outpost outpost, List<PlayerPawnRosterEntry> group)
        {
            int occRemoved = 0;
            for (int i = 0; i < group.Count; i++)
            {
                if (group[i].outpostRole == PlayerPawnOutpostRole.Occupant
                    && group[i].pawn != null
                    && outpost.Occupants.Contains(group[i].pawn))
                    occRemoved++;
            }
            return occRemoved > 0 && outpost.Occupants.Count == occRemoved;
        }

        private static void ExecuteOutpostGroupTransfer(
            WorldObject_WD_Outpost source,
            List<PlayerPawnRosterEntry> group,
            PlayerPawnTransferDestination destination)
        {
            var occupants = new List<Pawn>();
            var stored = new List<Pawn>();
            var mechs = new List<Pawn>();
            var shuttles = new List<Building_PassengerShuttle>();

            for (int i = 0; i < group.Count; i++)
            {
                PlayerPawnRosterEntry entry = group[i];
                if (entry.outpostRole == PlayerPawnOutpostRole.StoredShuttle)
                {
                    if (entry.shuttle is Building_PassengerShuttle shuttle
                        && !shuttle.Destroyed
                        && source.StoredPassengerShuttles.Contains(shuttle))
                        shuttles.Add(shuttle);
                    continue;
                }

                Pawn p = entry.pawn;
                if (p == null) continue;
                switch (entry.outpostRole)
                {
                    case PlayerPawnOutpostRole.Occupant:
                        if (source.Occupants.Contains(p)) occupants.Add(p);
                        break;
                    case PlayerPawnOutpostRole.StoredTransport:
                        if (source.StoredAnimalsAndVehicles.Contains(p)) stored.Add(p);
                        break;
                    case PlayerPawnOutpostRole.StoredMechanoid:
                        if (source.StoredMechanoids.Contains(p)) mechs.Add(p);
                        break;
                }
            }

            source.RemovePawnsAndStoredTransportAndMechanoidsAsCaravan(occupants, stored, mechs, shuttles);

            Caravan? caravan = Find.WorldSelector.SingleSelectedObject as Caravan;
            if (caravan == null || caravan.Destroyed || caravan.Tile != source.Tile)
                caravan = Find.WorldObjects.PlayerControlledCaravanAt(source.Tile);
            if (caravan == null || caravan.Destroyed)
            {
                Messages.Message("TSA_WD_PawnTransfer_CaravanFailed".Translate(), MessageTypeDefOf.NegativeEvent, false);
                return;
            }

            int moved = occupants.Count + stored.Count + mechs.Count;
            if (GiveFoodOnAllPlayerPawnsTransfer)
                PackTravelPemmicanFromOutpost(caravan, moved, source);
            RouteOrParkCaravan(caravan, source, destination, moved, source.LabelCap);
        }

        private static void ExecuteColonyGroupTransfer(
            MapParent source,
            List<PlayerPawnRosterEntry> group,
            PlayerPawnTransferDestination destination)
        {
            if (source?.Map == null)
            {
                Messages.Message("TSA_WD_PawnTransfer_CaravanFailed".Translate(), MessageTypeDefOf.NegativeEvent, false);
                return;
            }

            var removed = new List<Pawn>(group.Count);
            for (int i = 0; i < group.Count; i++)
            {
                Pawn p = group[i].pawn;
                if (p == null || p.Destroyed || p.Dead) continue;
                if (!PrepareMapPawnForTransfer(p)) continue;
                removed.Add(p);
            }

            if (removed.Count == 0)
            {
                Messages.Message("TSA_WD_PawnTransfer_CaravanFailed".Translate(), MessageTypeDefOf.NegativeEvent, false);
                return;
            }

            Caravan caravan = CaravanMaker.MakeCaravan(removed, Faction.OfPlayer, source.Tile, true);
            VehicleFrameworkOutpostDissolveCompat.TryAutoBoardPawnsIntoSelectedVehicles(caravan, removed);
            if (caravan == null || caravan.Destroyed)
            {
                Messages.Message("TSA_WD_PawnTransfer_CaravanFailed".Translate(), MessageTypeDefOf.NegativeEvent, false);
                return;
            }

            Find.WorldSelector?.ClearSelection();
            Find.WorldSelector?.Select(caravan, false);

            if (GiveFoodOnAllPlayerPawnsTransfer)
                PackTravelPemmicanFromColony(caravan, removed.Count, source.Map);
            RouteOrParkCaravan(caravan, source, destination, removed.Count, source.LabelCap);
        }

        private static bool PrepareMapPawnForTransfer(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return false;

            pawn.ownership?.UnclaimAll();
            VehicleFrameworkOutpostDissolveCompat.TryEjectPawnFromHostingVehicle(pawn);

            if (pawn.Spawned)
                pawn.DeSpawn();

            pawn.holdingOwner?.Remove(pawn);
            return !pawn.Destroyed && !pawn.Dead;
        }

        private static void RouteOrParkCaravan(
            Caravan caravan,
            WorldObject sourceLogObject,
            PlayerPawnTransferDestination destination,
            int movedCount,
            string sourceLabel,
            bool showMessages = true)
        {
            if (destination.kind == PlayerPawnTransferDestinationKind.ExitHere)
            {
                Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry(
                    "TSA_WD_PawnTransfer_Log".Translate(movedCount, sourceLabel, destination.Label),
                    sourceLogObject,
                    destination.outpost!));
                if (showMessages)
                {
                    Messages.Message(
                        "TSA_WD_PawnTransfer_ExitedHere".Translate(destination.Label),
                        caravan,
                        MessageTypeDefOf.TaskCompletion,
                        false);
                }
                Window_AllPlayerPawns.InvalidateCache();
                return;
            }

            PlanetTile destTile = PlanetSurfaceWorldActions.PlanetTileForWdTravel(destination.Tile, caravan);
            CaravanArrivalAction? arrival = null;
            if (destination.kind == PlayerPawnTransferDestinationKind.Colony
                && destination.colony is Settlement settlement)
            {
                arrival = new CaravanArrivalAction_Enter(settlement);
            }

            caravan.pather.StartPath(destTile, arrival, false, false);

            WorldObject destLog = destination.kind == PlayerPawnTransferDestinationKind.Outpost
                ? destination.outpost!
                : destination.colony!;
            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry(
                "TSA_WD_PawnTransfer_Log".Translate(movedCount, sourceLabel, destination.Label),
                sourceLogObject,
                destLog));

            if (showMessages)
            {
                if (destination.kind == PlayerPawnTransferDestinationKind.Colony)
                {
                    Messages.Message(
                        "TSA_WD_PawnTransfer_CaravanRoutedToColony".Translate(destination.Label),
                        destination.colony,
                        MessageTypeDefOf.TaskCompletion,
                        false);
                }
                else
                {
                    Messages.Message(
                        "TSA_WD_PawnTransfer_CaravanRoutedToOutpost".Translate(destination.Label),
                        destination.outpost,
                        MessageTypeDefOf.TaskCompletion,
                        false);
                }
            }

            Window_AllPlayerPawns.InvalidateCache();
        }

        /// <summary>
        /// After a colony prisoner is recruited: despawn from map, form a caravan, optionally pack pemmican,
        /// and path to a player outpost (auto-add / manual join uses existing outpost arrival behavior).
        /// </summary>
        public static bool TryTransferMapPawnToOutpostWithPemmican(Pawn pawn, WorldObject_WD_Outpost outpost, int pemmican)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return false;
            if (outpost == null || outpost.Destroyed) return false;
            if (outpost.Faction == null || !outpost.Faction.IsPlayer) return false;
            if (outpost.ManualDefenseActive)
            {
                Messages.Message("TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (!IsCapableOfImmediateTransfer(pawn, out string reason))
            {
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            MapParent source = pawn.Map?.Parent as MapParent;
            if (source == null || !source.HasMap)
            {
                // Already off-map (edge case); still try caravan from current tile if on world.
                return false;
            }

            if (!PrepareMapPawnForTransfer(pawn))
                return false;

            Caravan caravan = CaravanMaker.MakeCaravan(Gen.YieldSingle(pawn), Faction.OfPlayer, source.Tile, true);
            if (caravan == null || caravan.Destroyed)
                return false;

            PackTravelPemmicanFromColonyAmount(caravan, pemmican, source.Map);

            Find.WorldSelector?.ClearSelection();
            Find.WorldSelector?.Select(caravan, false);

            var dest = new PlayerPawnTransferDestination
            {
                kind = PlayerPawnTransferDestinationKind.Outpost,
                outpost = outpost
            };
            RouteOrParkCaravan(caravan, source, dest, 1, source.LabelCap);
            return true;
        }

        /// <summary>
        /// After recruitment: send a map pawn to another player colony with optional pemmican.
        /// </summary>
        public static bool TryTransferMapPawnToColonyWithPemmican(Pawn pawn, MapParent colony, int pemmican)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return false;
            if (colony == null || colony.Destroyed || !colony.HasMap) return false;
            if (colony.Faction == null || !colony.Faction.IsPlayer) return false;
            if (!IsCapableOfImmediateTransfer(pawn, out string reason))
            {
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            MapParent source = pawn.Map?.Parent as MapParent;
            if (source == null || !source.HasMap) return false;
            if (source.ID == colony.ID) return true;

            if (!PrepareMapPawnForTransfer(pawn))
                return false;

            Caravan caravan = CaravanMaker.MakeCaravan(Gen.YieldSingle(pawn), Faction.OfPlayer, source.Tile, true);
            if (caravan == null || caravan.Destroyed)
                return false;

            PackTravelPemmicanFromColonyAmount(caravan, pemmican, source.Map);

            Find.WorldSelector?.ClearSelection();
            Find.WorldSelector?.Select(caravan, false);

            var dest = new PlayerPawnTransferDestination
            {
                kind = PlayerPawnTransferDestinationKind.Colony,
                colony = colony
            };
            RouteOrParkCaravan(caravan, source, dest, 1, source.LabelCap);
            return true;
        }

        /// <summary>
        /// Send an unspawned player pawn (e.g. just-recruited outpost captive) from a world tile to a colony.
        /// </summary>
        public static bool TrySendUnspawnedPawnFromTileToColonyWithPemmican(
            Pawn pawn,
            PlanetTile fromTile,
            MapParent colony,
            int pemmican,
            WorldObject sourceLogObject)
        {
            if (pawn == null) return false;
            var dest = new PlayerPawnTransferDestination
            {
                kind = PlayerPawnTransferDestinationKind.Colony,
                colony = colony
            };
            return TrySendUnspawnedPawnsFromTileWithPemmican(
                new List<Pawn> { pawn },
                fromTile,
                dest,
                pemmican,
                sourceLogObject,
                showRouteMessages: true);
        }

        /// <summary>
        /// Send one or more unspawned player pawns from a world tile to a colony or outpost (one caravan).
        /// Tops travel food up to <see cref="TravelPemmicanPerPawn"/> nutrition per pawn when packing is requested.
        /// </summary>
        public static bool TrySendUnspawnedPawnsFromTileWithPemmican(
            List<Pawn> pawns,
            PlanetTile fromTile,
            PlayerPawnTransferDestination destination,
            int pemmicanPerPawn,
            WorldObject sourceLogObject,
            bool showRouteMessages = true)
        {
            if (pawns == null || pawns.Count == 0) return false;
            if (!fromTile.Valid) return false;
            if (destination.kind == PlayerPawnTransferDestinationKind.Colony)
            {
                MapParent colony = destination.colony;
                if (colony == null || colony.Destroyed || !colony.HasMap) return false;
                if (colony.Faction == null || !colony.Faction.IsPlayer) return false;
            }
            else if (destination.kind == PlayerPawnTransferDestinationKind.Outpost)
            {
                WorldObject_WD_Outpost outpost = destination.outpost;
                if (outpost == null || outpost.Destroyed) return false;
                if (outpost.Faction == null || !outpost.Faction.IsPlayer) return false;
            }
            else
                return false;

            var ready = new List<Pawn>(pawns.Count);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                if (pawn.Spawned) pawn.DeSpawn();
                pawn.holdingOwner?.Remove(pawn);
                if (Find.WorldPawns != null && Find.WorldPawns.Contains(pawn))
                    Find.WorldPawns.RemovePawn(pawn);
                ready.Add(pawn);
            }
            if (ready.Count == 0) return false;

            Caravan caravan = CaravanMaker.MakeCaravan(ready, Faction.OfPlayer, fromTile, true);
            if (caravan == null || caravan.Destroyed)
                return false;

            if (pemmicanPerPawn > 0)
            {
                if (sourceLogObject is WorldObject_WD_Outpost originOutpost)
                    PackTravelPemmicanFromOutpost(caravan, ready.Count, originOutpost);
                else if (sourceLogObject is MapParent mapParent && mapParent.HasMap)
                    PackTravelPemmicanFromColonyAmount(caravan, pemmicanPerPawn * ready.Count, mapParent.Map);
                else
                    PackTravelPemmican(caravan, GetTravelPemmicanShortfall(caravan, ready.Count));
            }

            RouteOrParkCaravan(
                caravan,
                sourceLogObject ?? (destination.kind == PlayerPawnTransferDestinationKind.Colony
                    ? (WorldObject)destination.colony
                    : destination.outpost),
                destination,
                ready.Count,
                sourceLogObject?.LabelCap ?? fromTile.ToString(),
                showRouteMessages);
            return true;
        }

        /// <summary>Adds pemmican stacks to a caravan inventory (respects stack limit).</summary>
        public static void PackTravelPemmican(Caravan caravan, int pemmican)
        {
            if (caravan == null || pemmican <= 0 || ThingDefOf.Pemmican == null) return;
            int remaining = pemmican;
            int stackLimit = ThingDefOf.Pemmican.stackLimit;
            while (remaining > 0)
            {
                Thing food = ThingMaker.MakeThing(ThingDefOf.Pemmican);
                if (food == null) break;
                food.stackCount = Mathf.Min(remaining, stackLimit);
                caravan.AddPawnOrItem(food, false);
                remaining -= food.stackCount;
            }
        }

        /// <summary>
        /// Pemmican still needed so caravan edible inventory reaches
        /// <paramref name="requestedUnits"/> × <see cref="TravelPemmicanPerPawn"/> nutrition.
        /// Existing meals (any nutrition-giving ingestible, including pemmican) count toward the threshold.
        /// </summary>
        public static int GetTravelPemmicanShortfall(Caravan caravan, int requestedUnits)
        {
            if (caravan == null || requestedUnits <= 0) return 0;
            float per = PemmicanNutrition;
            if (per <= 0.0001f) return 0;
            float gap = requestedUnits * NutritionPerTravelPack - SumCaravanEdibleNutrition(caravan);
            if (gap <= 0.0001f) return 0;
            return Mathf.Max(1, Mathf.CeilToInt(gap / per - 1e-5f));
        }

        /// <summary>
        /// Packs travel pemmican funded by map food nutrition equal to that pemmican amount.
        /// Converts to whole travel packs (<see cref="TravelPemmicanPerPawn"/>), then tops up only the nutrition shortfall.
        /// </summary>
        public static int PackTravelPemmicanFromColonyAmount(Caravan caravan, int pemmicanAmount, Map map)
        {
            if (pemmicanAmount <= 0) return 0;
            int units = (pemmicanAmount + TravelPemmicanPerPawn - 1) / TravelPemmicanPerPawn;
            return PackTravelPemmicanFromColony(caravan, units, map);
        }

        /// <summary>
        /// Funds travel pemmican from a colony map: removes any nutrition-giving food (prefer sooner spoil)
        /// equal to the nutrition still needed to reach one <see cref="TravelPemmicanPerPawn"/> pack per unit,
        /// counting meals already in caravan inventory. Packs only the shortfall. Warns for unfunded units.
        /// </summary>
        public static int PackTravelPemmicanFromColony(Caravan caravan, int requestedUnits, Map map)
        {
            if (caravan == null || requestedUnits <= 0) return 0;

            int needed = GetTravelPemmicanShortfall(caravan, requestedUnits);
            if (needed <= 0)
                return requestedUnits;

            if (!IsColonyAutoTravelFoodEnabled(map))
                return 0;

            float per = PemmicanNutrition;
            if (map == null || per <= 0.0001f)
            {
                int skipped = CountUnfundedTravelUnits(caravan, requestedUnits);
                WarnUnfundedTravelFood(caravan, skipped);
                return requestedUnits - skipped;
            }

            List<Thing> foods = CollectMapEdibleFoods(map);
            float available = SumListedNutrition(foods);
            int canPack = Mathf.Min(needed, Mathf.FloorToInt(available / per + 1e-4f));
            if (canPack > 0 && TryConsumeMapNutrition(foods, canPack * per))
                PackTravelPemmican(caravan, canPack);

            int unfunded = CountUnfundedTravelUnits(caravan, requestedUnits);
            if (unfunded > 0)
                WarnUnfundedTravelFood(caravan, unfunded);

            return requestedUnits - unfunded;
        }

        /// <summary>True when the map's player settlement has auto travel-food enabled (default on).</summary>
        public static bool IsColonyAutoTravelFoodEnabled(Map map)
        {
            if (map?.Parent is not Settlement settlement) return false;
            if (settlement.Faction == null || !settlement.Faction.IsPlayer) return false;
            CompViralSpread comp = settlement.GetComponent<CompViralSpread>();
            // No comp: treat as on so transfers still work on edge cases.
            return comp == null || comp.autoFeedTransferredPawns;
        }

        /// <summary>
        /// Funds travel pemmican from an origin outpost's virtual food for the nutrition still needed
        /// to reach one <see cref="TravelPemmicanPerPawn"/> pack per unit (meals already in inventory count).
        /// Packs only the shortfall. Warns for unfunded units.
        /// When food logistics is off or the outpost has no logistics comp, packs the shortfall with no charge.
        /// </summary>
        /// <returns>Number of units at or above a full travel pack after packing.</returns>
        public static int PackTravelPemmicanFromOutpost(Caravan caravan, int requestedUnits, WorldObject_WD_Outpost origin)
        {
            if (caravan == null || requestedUnits <= 0) return 0;

            int needed = GetTravelPemmicanShortfall(caravan, requestedUnits);
            if (needed <= 0)
                return requestedUnits;

            bool logisticsActive = WorldDominationMod.settings?.foodLogisticsActive == true;
            CompOutpostLogistics logi = logisticsActive ? origin?.GetComponent<CompOutpostLogistics>() : null;
            if (logi == null || PemmicanPerVirtualFood <= 0)
            {
                PackTravelPemmican(caravan, needed);
                return requestedUnits;
            }

            int canPack;
            if (logi.currentFood * PemmicanPerVirtualFood >= needed - 0.001f)
            {
                canPack = needed;
            }
            else
            {
                canPack = Mathf.Max(0, Mathf.FloorToInt(logi.currentFood * PemmicanPerVirtualFood + 1e-4f));
                canPack = Mathf.Min(canPack, needed);
            }

            if (canPack > 0)
            {
                logi.currentFood -= canPack / (float)PemmicanPerVirtualFood;
                PackTravelPemmican(caravan, canPack);
            }

            int unfunded = CountUnfundedTravelUnits(caravan, requestedUnits);
            if (unfunded > 0)
                WarnUnfundedTravelFood(caravan, unfunded);

            return requestedUnits - unfunded;
        }

        private static float PemmicanNutrition
        {
            get
            {
                if (ThingDefOf.Pemmican == null) return 0f;
                return ThingDefOf.Pemmican.GetStatValueAbstract(StatDefOf.Nutrition);
            }
        }

        private static float NutritionPerTravelPack
        {
            get
            {
                float per = PemmicanNutrition;
                if (per <= 0f) return 0f;
                return TravelPemmicanPerPawn * per;
            }
        }

        /// <summary>Nutrition from any nutrition-giving ingestible already in the caravan (pawn inventories).</summary>
        private static float SumCaravanEdibleNutrition(Caravan caravan)
        {
            if (caravan == null) return 0f;
            float total = 0f;
            foreach (Thing thing in CaravanInventoryUtility.AllInventoryItems(caravan))
            {
                if (thing == null || thing.Destroyed) continue;
                if (thing.def?.ingestible == null || !thing.def.IsNutritionGivingIngestible) continue;
                float perUnit = thing.GetStatValue(StatDefOf.Nutrition, true);
                if (perUnit <= 0f) continue;
                total += perUnit * thing.stackCount;
            }
            return total;
        }

        private static int CountUnfundedTravelUnits(Caravan caravan, int requestedUnits)
        {
            if (requestedUnits <= 0) return 0;
            float per = NutritionPerTravelPack;
            if (per <= 0.0001f) return requestedUnits;
            int covered = Mathf.FloorToInt(SumCaravanEdibleNutrition(caravan) / per + 0.001f);
            return Mathf.Max(0, requestedUnits - covered);
        }

        private static float SumListedNutrition(List<Thing> foods)
        {
            if (foods == null) return 0f;
            float available = 0f;
            for (int i = 0; i < foods.Count; i++)
            {
                Thing t = foods[i];
                if (t == null || t.Destroyed || t.stackCount <= 0) continue;
                float per = t.GetStatValue(StatDefOf.Nutrition, true);
                if (per <= 0f) continue;
                available += per * t.stackCount;
            }
            return available;
        }

        private static void WarnUnfundedTravelFood(Caravan caravan, int unfunded)
        {
            if (unfunded <= 0) return;
            string msg = unfunded == 1
                ? "TSA_WD_PawnTransfer_WithoutFood".Translate()
                : "TSA_WD_PawnTransfer_WithoutFoodPlural".Translate(unfunded);
            Messages.Message(msg, caravan, MessageTypeDefOf.CautionInput, false);
        }

        private static List<Thing> CollectMapEdibleFoods(Map map)
        {
            var foods = new List<Thing>();
            if (map?.listerThings == null) return foods;

            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree))
            {
                if (!IsColonyTransferEdible(thing)) continue;
                foods.Add(thing);
            }

            // Prefer sooner spoil; non-rottable last.
            foods.Sort(CompareSpoilSoonest);
            return foods;
        }

        private static bool IsColonyTransferEdible(Thing thing)
        {
            if (thing == null || thing.Destroyed || !thing.Spawned) return false;
            if (thing.def?.ingestible == null || !thing.def.IsNutritionGivingIngestible) return false;
            if (thing.Faction != null && thing.Faction != Faction.OfPlayer) return false;
            float perUnit = thing.GetStatValue(StatDefOf.Nutrition, true);
            return perUnit > 0f;
        }

        private static int CompareSpoilSoonest(Thing a, Thing b)
        {
            float ka = SpoilSortKey(a);
            float kb = SpoilSortKey(b);
            int cmp = ka.CompareTo(kb);
            if (cmp != 0) return cmp;
            return (a?.thingIDNumber ?? 0).CompareTo(b?.thingIDNumber ?? 0);
        }

        private static float SpoilSortKey(Thing thing)
        {
            CompRottable rot = thing?.TryGetComp<CompRottable>();
            if (rot == null) return float.MaxValue;
            return rot.TicksUntilRotAtCurrentTemp;
        }

        /// <summary>Removes map food totaling at least <paramref name="nutritionNeeded"/>. Returns false if short.</summary>
        private static bool TryConsumeMapNutrition(List<Thing> foods, float nutritionNeeded)
        {
            if (foods == null || nutritionNeeded <= 0.0001f) return false;

            float available = 0f;
            for (int i = 0; i < foods.Count; i++)
            {
                Thing t = foods[i];
                if (t == null || t.Destroyed || t.stackCount <= 0) continue;
                float per = t.GetStatValue(StatDefOf.Nutrition, true);
                if (per <= 0f) continue;
                available += per * t.stackCount;
                if (available >= nutritionNeeded - 0.0001f) break;
            }
            if (available < nutritionNeeded - 0.0001f) return false;

            float remaining = nutritionNeeded;
            for (int i = 0; i < foods.Count && remaining > 0.0001f; i++)
            {
                Thing t = foods[i];
                if (t == null || t.Destroyed || t.stackCount <= 0) continue;
                float per = t.GetStatValue(StatDefOf.Nutrition, true);
                if (per <= 0f) continue;

                int take = Mathf.Min(t.stackCount, Mathf.CeilToInt(remaining / per - 1e-5f));
                if (take <= 0) take = 1;
                take = Mathf.Min(take, t.stackCount);

                Thing split = t.SplitOff(take);
                if (split != null && !split.Destroyed)
                    split.Destroy(DestroyMode.Vanish);

                remaining -= per * take;
            }

            return remaining <= 0.0001f;
        }
    }
}
