using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Ideology guest status for outpost occupants (slaves vs free colonists). No-op when Ideology is inactive.</summary>
    public static class OutpostPawnIdeologyUtil
    {
        public static bool IsSlaveHumanlike(Pawn pawn)
        {
            if (pawn?.RaceProps?.Humanlike != true) return false;
            return ModsConfig.IdeologyActive && pawn.IsSlave;
        }

        /// <summary>Humanlike colonist who is not a slave (required to remain at the outpost when using bulk remove rules).</summary>
        public static bool IsNonSlaveHumanlikeColonist(Pawn pawn)
        {
            if (pawn?.RaceProps?.Humanlike != true || pawn.Dead) return false;
            return !IsSlaveHumanlike(pawn);
        }

        public static bool AnySlaveInList(IReadOnlyList<Pawn> pawns)
        {
            if (pawns == null) return false;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (IsSlaveHumanlike(pawns[i])) return true;
            }

            return false;
        }

        /// <summary>At least one humanlike who is not a slave (required in the removal set when any slave is removed, unless fully evacuating).</summary>
        public static bool AnyNonSlaveHumanlikeInList(IReadOnlyList<Pawn> pawns)
        {
            if (pawns == null) return false;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p?.RaceProps?.Humanlike != true) continue;
                if (!IsSlaveHumanlike(p))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// True when <paramref name="selectedThingIds"/> already includes at least one non-slave humanlike occupant of <paramref name="outpost"/>.
        /// </summary>
        public static bool SelectionIncludesNonSlaveOccupant(WorldObject_WD_Outpost outpost, HashSet<string> selectedThingIds)
        {
            if (outpost?.Occupants == null || selectedThingIds == null || selectedThingIds.Count == 0) return false;
            for (int i = 0; i < outpost.Occupants.Count; i++)
            {
                Pawn p = outpost.Occupants[i];
                if (p?.ThingID == null || !selectedThingIds.Contains(p.ThingID)) continue;
                if (IsNonSlaveHumanlikeColonist(p))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// SELECT gate (checkboxes / select-all): free non-slave humanlikes always selectable; slaves only when a free occupant is already selected.
        /// Does not enforce leave-behind — that is COMMIT-only via <see cref="BulkRemovalSelectionIsAllowed"/>.
        /// </summary>
        public static bool CanToggleOutpostRemovalSelection(
            WorldObject_WD_Outpost outpost,
            HashSet<string> selectedThingIds,
            Pawn candidate)
        {
            if (outpost == null || selectedThingIds == null || candidate?.ThingID == null) return false;
            if (selectedThingIds.Contains(candidate.ThingID))
                return true;
            if (IsNonSlaveHumanlikeColonist(candidate))
                return true;
            return SelectionIncludesNonSlaveOccupant(outpost, selectedThingIds);
        }

        /// <summary>
        /// After selection changes: drop selected slaves when no non-slave humanlike occupant remains selected.
        /// Returns true if the set was modified. Caller should also drop escort-dependent stored entries when no occupant remains.
        /// </summary>
        public static bool PruneDependentRemovalSelection(WorldObject_WD_Outpost outpost, HashSet<string> selectedThingIds)
        {
            if (outpost?.Occupants == null || selectedThingIds == null || selectedThingIds.Count == 0) return false;
            if (SelectionIncludesNonSlaveOccupant(outpost, selectedThingIds)) return false;

            bool changed = false;
            for (int i = 0; i < outpost.Occupants.Count; i++)
            {
                Pawn p = outpost.Occupants[i];
                if (p?.ThingID == null || !IsSlaveHumanlike(p)) continue;
                if (selectedThingIds.Remove(p.ThingID))
                    changed = true;
            }
            return changed;
        }

        /// <summary>
        /// COMMIT: whether <paramref name="toRemove"/> is allowed for bulk remove / transfer from the outpost.
        /// Full evacuation (every occupant in the set) is always allowed. Otherwise: at least one non-slave humanlike
        /// must remain on the outpost, and any removal that includes slaves must also include at least one non-slave humanlike leaver.
        /// </summary>
        public static bool BulkRemovalSelectionIsAllowed(WorldObject_WD_Outpost outpost, IReadOnlyList<Pawn> toRemove)
        {
            if (outpost?.Occupants == null || toRemove == null || toRemove.Count == 0) return false;

            var remove = new HashSet<Pawn>();
            for (int i = 0; i < toRemove.Count; i++)
            {
                Pawn p = toRemove[i];
                if (p != null) remove.Add(p);
            }

            if (remove.Count == 0) return false;

            bool fullEvacuation = true;
            for (int i = 0; i < outpost.Occupants.Count; i++)
            {
                Pawn o = outpost.Occupants[i];
                if (o == null || o.Destroyed || o.Dead) continue;
                if (!remove.Contains(o))
                {
                    fullEvacuation = false;
                    break;
                }
            }

            if (fullEvacuation)
                return true;

            if (!BulkRemovalKeepsMinimumNonSlave(outpost, toRemove, out _))
                return false;

            if (AnySlaveInList(toRemove) && !AnyNonSlaveHumanlikeInList(toRemove))
                return false;

            return true;
        }

        /// <summary>COMMIT: same as <see cref="BulkRemovalSelectionIsAllowed(WorldObject_WD_Outpost, IReadOnlyList{Pawn})"/> using thing IDs.</summary>
        public static bool BulkRemovalSelectionIsAllowed(WorldObject_WD_Outpost outpost, HashSet<string> selectedThingIds)
        {
            if (outpost?.Occupants == null || selectedThingIds == null || selectedThingIds.Count == 0) return false;
            var list = new List<Pawn>();
            for (int i = 0; i < outpost.Occupants.Count; i++)
            {
                Pawn p = outpost.Occupants[i];
                if (p?.ThingID != null && selectedThingIds.Contains(p.ThingID))
                    list.Add(p);
            }

            if (list.Count == 0) return false;
            return BulkRemovalSelectionIsAllowed(outpost, list);
        }

        /// <summary>
        /// SELECT gate (legacy name). Forwards to <see cref="CanToggleOutpostRemovalSelection"/> — does not run COMMIT leave-behind checks.
        /// </summary>
        public static bool BulkRemovalSelectionIsAllowedWithExtra(
            WorldObject_WD_Outpost outpost,
            HashSet<string> selectedThingIds,
            Pawn extraIfNotYetSelected)
        {
            return CanToggleOutpostRemovalSelection(outpost, selectedThingIds, extraIfNotYetSelected);
        }

        /// <summary>
        /// COMMIT reject tip for an invalid bulk removal set. False when allowed (no tip).
        /// Leave-behind → <c>TSA_WD_RemoveBulk_NeedOneNonSlave</c>; accompaniment → <c>TSA_WD_Pawns_RemoveSlaveAccompanimentRequiredTip</c>.
        /// </summary>
        public static bool TryGetBulkRemovalRejectReason(WorldObject_WD_Outpost outpost, IReadOnlyList<Pawn> toRemove, out string reject)
        {
            reject = null;
            if (BulkRemovalSelectionIsAllowed(outpost, toRemove))
                return false;

            if (toRemove == null || toRemove.Count == 0)
            {
                reject = "TSA_WD_Pawns_RemoveSlaveAccompanimentRequiredTip".Translate();
                return true;
            }

            if (AnySlaveInList(toRemove) && !AnyNonSlaveHumanlikeInList(toRemove))
            {
                reject = "TSA_WD_Pawns_RemoveSlaveAccompanimentRequiredTip".Translate();
                return true;
            }

            if (!BulkRemovalKeepsMinimumNonSlave(outpost, toRemove, out _))
            {
                reject = "TSA_WD_RemoveBulk_NeedOneNonSlave".Translate();
                return true;
            }

            reject = "TSA_WD_Pawns_RemoveSlaveAccompanimentRequiredTip".Translate();
            return true;
        }

        /// <summary>
        /// When the outpost will still have occupants after <paramref name="toRemove"/>, at least one non-slave humanlike
        /// must remain. Full evacuation (nobody left) is allowed — caller handles destroy / last-pawn confirmation.
        /// </summary>
        public static bool BulkRemovalKeepsMinimumNonSlave(WorldObject_WD_Outpost outpost, IReadOnlyList<Pawn> toRemove, out int remainingNonSlaveHumanlikes)
        {
            remainingNonSlaveHumanlikes = 0;
            if (outpost?.Occupants == null) return false;
            var remove = new HashSet<Pawn>();
            if (toRemove != null)
            {
                for (int i = 0; i < toRemove.Count; i++)
                {
                    Pawn p = toRemove[i];
                    if (p != null) remove.Add(p);
                }
            }

            int remainingOccupants = 0;
            for (int i = 0; i < outpost.Occupants.Count; i++)
            {
                Pawn o = outpost.Occupants[i];
                if (o == null || o.Destroyed || o.Dead) continue;
                if (remove.Contains(o)) continue;
                remainingOccupants++;
                if (IsNonSlaveHumanlikeColonist(o))
                    remainingNonSlaveHumanlikes++;
            }

            if (remainingOccupants == 0)
                return true;
            return remainingNonSlaveHumanlikes >= 1;
        }
    }
}
