using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Odyssey passenger shuttles travel as <see cref="TravellingTransporters"/> and, after landing, as a
    /// <see cref="Caravan"/> with <see cref="Caravan.Shuttle"/> set. Those caravans have
    /// <see cref="Caravan.CantMove"/> true and cannot walk, so <see cref="Caravan_PathFollower.Destination"/>
    /// may not match the current tile even when the group is parked. WD establishment must treat them like
    /// pod-spawned caravans: same tile and not mid-move, without requiring destination parity.
    /// Stored at outposts as <see cref="Building_PassengerShuttle"/> things (not pawns), alongside VF vehicle pawns.
    /// </summary>
    public static class OdysseyShuttleOutpostEstablishmentCompat
    {
        public static bool CaravanUsesPassengerShuttleForTravel(Caravan caravan)
        {
            if (caravan == null || caravan.Destroyed) return false;
            if (!ModsConfig.OdysseyActive) return false;
            return caravan.Shuttle != null;
        }

        public static bool IsPassengerShuttle(Thing thing) =>
            ModsConfig.OdysseyActive && thing is Building_PassengerShuttle;

        public static bool TryStoreShuttlesFromCaravan(WorldObject_WD_Outpost outpost, Caravan caravan)
        {
            if (outpost == null || caravan == null || caravan.Destroyed || !ModsConfig.OdysseyActive)
                return false;

            var shuttles = new List<Building_PassengerShuttle>();
            CollectShuttlesFromCaravanInventory(caravan, shuttles);

            bool storedAny = false;
            for (int i = 0; i < shuttles.Count; i++)
            {
                if (outpost.StorePassengerShuttle(shuttles[i], caravan))
                    storedAny = true;
            }

            return storedAny;
        }

        /// <summary>
        /// Shuttles ride in a colonist's inventory. During founding, colonists join the outpost before dissolve runs,
        /// so the caravan inventory scan no longer sees them — peel shuttles off each transferring pawn instead.
        /// </summary>
        public static bool TryStoreShuttlesFromPawnInventory(
            WorldObject_WD_Outpost outpost,
            Pawn pawn,
            Caravan sourceCaravan = null!)
        {
            if (outpost == null || pawn == null || pawn.Destroyed || !ModsConfig.OdysseyActive)
                return false;

            var inventory = pawn.inventory?.innerContainer;
            if (inventory == null || inventory.Count == 0) return false;

            var shuttles = new List<Building_PassengerShuttle>();
            for (int i = inventory.Count - 1; i >= 0; i--)
            {
                if (inventory[i] is Building_PassengerShuttle shuttle && !shuttles.Contains(shuttle))
                    shuttles.Add(shuttle);
            }

            bool storedAny = false;
            for (int i = 0; i < shuttles.Count; i++)
            {
                if (outpost.StorePassengerShuttle(shuttles[i], sourceCaravan))
                    storedAny = true;
            }

            return storedAny;
        }

        /// <summary>Safety net after multi-colonist founding: shuttle carrier may already be an occupant.</summary>
        public static bool TryStoreShuttlesFromOccupants(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || !ModsConfig.OdysseyActive) return false;
            var occupants = outpost.Occupants;
            if (occupants == null || occupants.Count == 0) return false;

            bool storedAny = false;
            for (int i = 0; i < occupants.Count; i++)
            {
                if (TryStoreShuttlesFromPawnInventory(outpost, occupants[i]))
                    storedAny = true;
            }

            return storedAny;
        }

        private static void CollectShuttlesFromCaravanInventory(Caravan caravan, List<Building_PassengerShuttle> shuttles)
        {
            if (caravan == null || shuttles == null) return;

            var items = CaravanInventoryUtility.AllInventoryItems(caravan);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is Building_PassengerShuttle shuttle && !shuttles.Contains(shuttle))
                    shuttles.Add(shuttle);
            }

            Building_PassengerShuttle cached = caravan.Shuttle;
            if (cached != null && !cached.Destroyed && !shuttles.Contains(cached))
                shuttles.Add(cached);
        }

        /// <summary>Vanilla allows one passenger shuttle per caravan.</summary>
        public static void AttachStoredShuttlesToCaravan(Caravan caravan, IReadOnlyList<Building_PassengerShuttle> shuttles)
        {
            if (caravan == null || caravan.Destroyed || shuttles == null || shuttles.Count == 0 || !ModsConfig.OdysseyActive)
                return;

            // Do not read caravan.Shuttle before attaching — that caches null and hides launch/refuel gizmos until RecacheInventory.
            if (CaravanInventoryUtility.FindShuttle(caravan) != null)
            {
                if (shuttles.Count > 0)
                {
                    Messages.Message(
                        "TSA_WD_Outpost_ShuttleCaravanAlreadyHasOne".Translate(),
                        caravan,
                        MessageTypeDefOf.RejectInput,
                        false);
                }
                return;
            }

            Building_PassengerShuttle shuttle = shuttles[0];
            if (shuttle == null || shuttle.Destroyed) return;

            var pawns = caravan.PawnsListForReading;
            if (pawns == null || pawns.Count == 0)
            {
                Messages.Message(
                    "TSA_WD_Outpost_ShuttleNeedsHumanColonist".Translate(),
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            if (!HasHumanColonistCarrier(pawns, caravan.Faction))
            {
                Messages.Message(
                    "TSA_WD_Outpost_ShuttleNeedsHumanColonist".Translate(),
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            CaravanInventoryUtility.GiveThing(caravan, shuttle);
            caravan.RecacheInventory();

            if (CaravanInventoryUtility.FindShuttle(caravan) == null)
            {
                Messages.Message(
                    "TSA_WD_Outpost_ShuttleAttachFailed".Translate(shuttle.LabelCap),
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            ResetShuttleLaunchCooldown(shuttle);

            if (shuttles.Count > 1)
            {
                Messages.Message(
                    "TSA_WD_Outpost_ShuttleOnlyOnePerCaravan".Translate((shuttles.Count - 1).ToString()),
                    caravan,
                    MessageTypeDefOf.CautionInput,
                    false);
            }
        }

        private static bool HasHumanColonistCarrier(List<Pawn> pawns, Faction faction)
        {
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (p.RaceProps?.Humanlike == true && p.Faction == faction)
                    return true;
            }

            return false;
        }

        /// <summary>Outpost storage counts as a full service stop — allow immediate relaunch.</summary>
        private static void ResetShuttleLaunchCooldown(Building_PassengerShuttle shuttle)
        {
            CompLaunchable launchable = shuttle?.LaunchableComp;
            if (launchable?.Props == null) return;
            launchable.lastLaunchTick = Find.TickManager.TicksGame - launchable.Props.cooldownTicks;
        }
    }
}
