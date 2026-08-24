using System.Collections.Generic;

using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// After settlement map victory, claim remaining defeat-faction buildings (vanilla Claim designator)
    /// and unforbid held contents so storage is lootable before the player leaves.
    /// </summary>
    public static class WdSettlementDefeatClaimUtility
    {
        private static readonly HashSet<int> ClaimedMapIds = new HashSet<int>();

        public static void TryClaimDefeatedFactionBuildings(Map map, Faction defeatedFaction)
        {
            if (map == null || defeatedFaction == null || defeatedFaction.IsPlayer) return;
            if (!ClaimedMapIds.Add(map.uniqueID)) return;

            var buildings = map.listerThings?.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            if (buildings == null || buildings.Count == 0) return;

            // Snapshot — SetFaction can mutate listing membership.
            var snapshot = new List<Thing>(buildings);
            Faction player = Faction.OfPlayer;

            for (int i = 0; i < snapshot.Count; i++)
            {
                Thing thing = snapshot[i];
                if (thing == null || thing.Destroyed) continue;
                if (thing.Faction != defeatedFaction) continue;
                if (!thing.ClaimableBy(player).Accepted) continue;

                thing.SetFaction(player, null);
                UnforbidThing(thing);

                if (thing is IThingHolder holder)
                    UnforbidHeldThings(holder);
            }
        }

        private static void UnforbidThing(Thing thing)
        {
            if (thing == null || thing.Destroyed) return;
            if (thing.TryGetComp<CompForbiddable>() == null) return;
            thing.SetForbidden(false, false);
        }

        private static void UnforbidHeldThings(IThingHolder holder)
        {
            ThingOwner owned = holder.GetDirectlyHeldThings();
            if (owned == null) return;

            for (int i = 0; i < owned.Count; i++)
                UnforbidThing(owned[i]);
        }
    }
}
