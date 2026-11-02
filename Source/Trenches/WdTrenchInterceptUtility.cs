using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    public static class WdTrenchInterceptUtility
    {
        public static bool HasResolvedOwner(WorldObject_Traveler traveler, WorldObject owner)
        {
            if (traveler == null || owner == null) return false;
            if (traveler.trenchResolvedOwnerIds == null) return false;
            return traveler.trenchResolvedOwnerIds.Contains(owner.ID);
        }

        public static void MarkResolved(WorldObject_Traveler traveler, WorldObject owner)
        {
            if (traveler == null || owner == null) return;
            traveler.trenchResolvedOwnerIds ??= new List<int>();
            if (!traveler.trenchResolvedOwnerIds.Contains(owner.ID))
                traveler.trenchResolvedOwnerIds.Add(owner.ID);
        }

        public static void CopyResolvedOwners(WorldObject_Traveler from, WorldObject_Traveler to)
        {
            if (from == null || to == null) return;
            if (from.trenchResolvedOwnerIds == null || from.trenchResolvedOwnerIds.Count == 0) return;
            to.trenchResolvedOwnerIds ??= new List<int>();
            for (int i = 0; i < from.trenchResolvedOwnerIds.Count; i++)
            {
                int id = from.trenchResolvedOwnerIds[i];
                if (!to.trenchResolvedOwnerIds.Contains(id))
                    to.trenchResolvedOwnerIds.Add(id);
            }
        }

        public static bool OwnerBusy(WorldObject owner)
        {
            if (owner == null || owner.Destroyed) return true;
            if (owner is WorldObject_WD_Outpost op)
            {
                if (op.ManualDefenseActive) return true;
                if (WD_MapComponent_OutpostDefense.HasActiveEncounterFor(op)) return true;
            }
            return false;
        }
    }
}
