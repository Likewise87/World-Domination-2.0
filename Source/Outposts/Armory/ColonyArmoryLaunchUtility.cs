using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Pawn-free colony to outpost shipping. Picks storable items straight off a colony map and
    /// hands them to the normal goods traveler, so the cargo still flies across the world map and
    /// can still be intercepted. A colony has no <see cref="CompViralSpread"/> pool, so the launch
    /// costs no strength, matching colony-origin road building.
    /// </summary>
    public static class ColonyArmoryLaunchUtility
    {
        /// <summary>One pickable stack on a colony map, rebuilt on open and on an interval.</summary>
        public class ColonyItemEntry
        {
            public Thing Thing;
            public int Available;
            public string Label;
        }

        private static readonly List<Thing> tmpCommitted = new List<Thing>();

        /// <summary>
        /// Spawned, storable, uncommitted stacks on <paramref name="map"/>. Never call from a draw
        /// path: this walks the lister and the lord manager (Performance Rule 10).
        /// </summary>
        public static List<ColonyItemEntry> BuildPickableItems(Map map)
        {
            var result = new List<ColonyItemEntry>();
            if (map == null) return result;

            CollectCommittedThings(map, tmpCommitted);
            List<Thing> haulables = map.listerThings?.ThingsInGroup(ThingRequestGroup.HaulableEver);
            if (haulables != null)
            {
                for (int i = 0; i < haulables.Count; i++)
                {
                    Thing t = haulables[i];
                    if (!IsPickable(map, t)) continue;
                    if (tmpCommitted.Contains(t)) continue;
                    result.Add(new ColonyItemEntry
                    {
                        Thing = t,
                        Available = t.stackCount,
                        Label = t.LabelCapNoCount
                    });
                }
            }
            tmpCommitted.Clear();
            return result;
        }

        private static bool IsPickable(Map map, Thing t)
        {
            if (t == null || t.Destroyed || !t.Spawned) return false;
            if (t.def == null || !OutpostArmoryUtility.IsArmoryItem(t.def)) return false;
            if (t.stackCount <= 0) return false;
            if (t.IsBurning()) return false;
            if (map.reservationManager != null && map.reservationManager.IsReservedByAnyoneOf(t, Faction.OfPlayer)) return false;
            if (map.physicalInteractionReservationManager != null && map.physicalInteractionReservationManager.IsReserved(t)) return false;
            return true;
        }

        /// <summary>Stacks already promised to a forming caravan; shipping them too would double-commit.</summary>
        private static void CollectCommittedThings(Map map, List<Thing> outThings)
        {
            outThings.Clear();
            List<Lord> lords = map.lordManager?.lords;
            if (lords == null) return;
            for (int i = 0; i < lords.Count; i++)
            {
                if (!(lords[i]?.LordJob is LordJob_FormAndSendCaravan form)) continue;
                List<TransferableOneWay> transferables = form.transferables;
                if (transferables == null) continue;
                for (int j = 0; j < transferables.Count; j++)
                {
                    List<Thing> things = transferables[j]?.things;
                    if (things == null) continue;
                    for (int k = 0; k < things.Count; k++)
                        if (things[k] != null) outThings.Add(things[k]);
                }
            }
        }

        /// <summary>
        /// Removes the picked counts from the colony map (or from colonists, via <paramref name="holders"/>)
        /// and sends them to <paramref name="destination"/>. Map stacks are re-checked at launch, so
        /// anything reserved or promised to a forming caravan since the list was built is skipped.
        /// Ordinary items become abstract delivery rows; irreplaceable ones ride as real Things.
        /// Returns false when nothing shippable was picked; a failed spawn puts everything back on the map.
        /// </summary>
        public static bool TryLaunch(
            MapParent colony,
            Dictionary<Thing, int> picked,
            WorldObject destination,
            bool viaDropPod,
            Dictionary<Thing, Pawn> holders = null)
        {
            if (colony?.Map == null || destination == null || picked == null || picked.Count == 0) return false;

            var preview = new List<ThingDefCountClass>();
            int uniquePreview = 0;
            foreach (var kv in picked)
            {
                if (kv.Key?.def == null || kv.Value <= 0) continue;
                if (OutpostArmoryUtility.IsIrreplaceable(kv.Key)) uniquePreview++;
                else preview.Add(new ThingDefCountClass(kv.Key.def, kv.Value));
            }
            if (!Outpost_Warehouse_Delivery.IsValidShipmentDestination(
                    destination, colony, preview, uniquePreview, 0f, out string rejectKey))
            {
                Messages.Message(rejectKey.Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }
            if (viaDropPod && !RapidResponseUtility.TransportPodsResearched())
            {
                Messages.Message("TSA_WD_RapidResponse_DropPodsNeedResearch".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }
            if (viaDropPod
                && !PlayerPawnDropPodUtility.TryConsumeComponents(
                    PlayerPawnDropPodUtility.ComponentCostPerLaunch, out string componentReason))
            {
                Messages.Message(
                    componentReason ?? "TSA_WD_PawnDropPod_AllWouldAbort".Translate(),
                    colony,
                    MessageTypeDefOf.RejectInput);
                return false;
            }

            Map map = colony.Map;
            var rows = new List<ThingDefCountClass>();
            var uniques = new List<Thing>();

            CollectCommittedThings(map, tmpCommitted);
            foreach (var kv in picked)
            {
                Thing source = kv.Key;
                int want = kv.Value;
                if (source == null || source.Destroyed || want <= 0) continue;

                Thing taken;
                if (holders != null && holders.TryGetValue(source, out Pawn holder) && holder != null)
                {
                    taken = OutpostArmoryUtility.TryTakeFromPawn(holder, source, want, out _);
                }
                else
                {
                    if (!IsPickable(map, source) || tmpCommitted.Contains(source)) continue;
                    if (want > source.stackCount) want = source.stackCount;
                    taken = want >= source.stackCount ? source : source.SplitOff(want);
                    if (taken != null && taken.Spawned) taken.DeSpawn(DestroyMode.Vanish);
                }
                if (taken == null || taken.Destroyed) continue;

                if (OutpostArmoryUtility.IsIrreplaceable(taken))
                {
                    uniques.Add(taken);
                    continue;
                }

                OutpostArmoryUtility.Normalize(taken);
                var row = new ThingDefCountClass(taken.def, taken.stackCount)
                {
                    stuff = CompOutpostWarehouse.ResolveStuffForDeposit(taken.def, taken.Stuff)
                };
                if (taken.TryGetQuality(out QualityCategory q)) row.quality = q;
                CompOutpostWarehouse.MergeCount(rows, row);
                taken.Destroy(DestroyMode.Vanish);
            }
            tmpCommitted.Clear();

            if (rows.Count == 0 && uniques.Count == 0) return false;

            if (!WorldActions_Traveler.SpawnDeliveryTravelerFrom(colony, rows, destination, viaDropPod, 0f, uniques))
            {
                SettlementBuyUtility.RefundItems(colony, rows);
                IntVec3 cell = WorldActions_Traveler.FindColonyDeliveryOrTradeDropCell(map);
                for (int i = 0; i < uniques.Count; i++)
                {
                    Thing t = uniques[i];
                    if (t == null || t.Destroyed || t.Spawned || t.holdingOwner != null) continue;
                    if (!GenPlace.TryPlaceThing(t, cell, map, ThingPlaceMode.Near) && !t.Destroyed)
                        t.Destroy(DestroyMode.Vanish);
                }
                return false;
            }

            string msgKey = viaDropPod ? "TSA_WD_Warehouse_ShipLaunchedDropPod" : "TSA_WD_Warehouse_ShipLaunched";
            Messages.Message(msgKey.Translate(destination.LabelCap), colony, MessageTypeDefOf.PositiveEvent);
            return true;
        }
    }
}
