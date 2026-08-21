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
        /// Whether <paramref name="toRemove"/> is allowed for bulk remove from the outpost UI / action.
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

        /// <summary>Same as <see cref="BulkRemovalSelectionIsAllowed(WorldObject_WD_Outpost, IReadOnlyList{Pawn})"/> using thing IDs from the pawns tab selection.</summary>
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

        /// <summary>Whether adding <paramref name="extraIfNotYetSelected"/> to the selection (when not already selected) would still be an allowed bulk removal.</summary>
        public static bool BulkRemovalSelectionIsAllowedWithExtra(
            WorldObject_WD_Outpost outpost,
            HashSet<string> selectedThingIds,
            Pawn extraIfNotYetSelected)
        {
            if (outpost == null || selectedThingIds == null || extraIfNotYetSelected?.ThingID == null) return false;
            if (selectedThingIds.Contains(extraIfNotYetSelected.ThingID))
                return true;
            var h = new HashSet<string>(selectedThingIds);
            h.Add(extraIfNotYetSelected.ThingID);
            return BulkRemovalSelectionIsAllowed(outpost, h);
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
