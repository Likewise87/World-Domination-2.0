using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// The one outpost-origin shipping path (Warehouse tab Ship Now, AllPlayerGear). Validates
    /// everything before touching the store, then withdraws rows, uniques and virtual food and spawns
    /// the goods traveler. Any failure after a withdraw puts everything back.
    /// </summary>
    public static class OutpostStorageShipping
    {
        /// <summary>
        /// Withdraws <paramref name="rows"/> from the outpost store (warehouse list or armory stock),
        /// takes <paramref name="uniques"/> out of the armory (or accepts already-detached ones), withdraws
        /// <paramref name="virtualFood"/> and launches. Charges the usual delivery strength cost.
        /// </summary>
        public static bool TryLaunch(
            WorldObject_WD_Outpost sender,
            List<ThingDefCountClass> rows,
            List<Thing> uniques,
            float virtualFood,
            WorldObject destination,
            bool viaDropPod)
        {
            if (sender == null || destination == null) return false;
            var armory = CompOutpostArmory.Get(sender);
            if (armory == null) return false;

            bool hasRows = rows != null && rows.Exists(tc => tc?.thingDef != null && tc.count > 0);
            bool hasUniques = uniques != null && uniques.Count > 0;
            bool hasVirtual = virtualFood > 0.001f;
            if (!hasRows && !hasUniques && !hasVirtual)
            {
                Messages.Message("TSA_WD_Warehouse_ShipNothingSelected".Translate(), sender, MessageTypeDefOf.RejectInput);
                return false;
            }

            if (!Outpost_Warehouse_Delivery.IsValidShipmentDestination(
                    destination, sender, rows, uniques?.Count ?? 0, virtualFood, out string rejectKey))
            {
                Messages.Message(rejectKey.Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }

            if (!CanLaunchFrom(sender, viaDropPod, out string launchRejectKey))
            {
                Messages.Message(launchRejectKey.Translate(sender.LabelCap), sender, MessageTypeDefOf.RejectInput);
                return false;
            }

            if (viaDropPod
                && !PlayerPawnDropPodUtility.TryConsumeComponents(
                    PlayerPawnDropPodUtility.ComponentCostPerLaunch, out string componentReason))
            {
                Messages.Message(
                    componentReason ?? "TSA_WD_PawnDropPod_AllWouldAbort".Translate(),
                    sender,
                    MessageTypeDefOf.RejectInput);
                return false;
            }

            var withdrawn = new List<ThingDefCountClass>();
            if (hasRows)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    ThingDefCountClass want = rows[i];
                    if (want?.thingDef == null || want.count <= 0) continue;
                    int got = armory.WithdrawUpToMatching(want, want.count);
                    if (got > 0)
                        withdrawn.Add(CompOutpostWarehouse.PlainStockRow(want, got));
                    if (got < want.count)
                    {
                        RollBack(sender, armory, withdrawn, null, 0f);
                        Messages.Message("TSA_WD_Warehouse_ShipInsufficient".Translate(), sender, MessageTypeDefOf.RejectInput);
                        return false;
                    }
                }
            }

            var takenUniques = new List<Thing>();
            if (hasUniques)
            {
                for (int i = 0; i < uniques.Count; i++)
                {
                    Thing t = uniques[i];
                    if (t == null || t.Destroyed) continue;
                    if (armory.Uniques.Contains(t))
                    {
                        Thing taken = armory.Uniques.Take(t);
                        if (taken != null) takenUniques.Add(taken);
                    }
                    else if (t.holdingOwner == null)
                    {
                        // Already detached, e.g. stripped from a pawn for shipping.
                        takenUniques.Add(t);
                    }
                }
            }

            if (hasVirtual && !Outpost_Warehouse_Delivery.TryWithdrawVirtualFood(sender, virtualFood))
            {
                RollBack(sender, armory, withdrawn, takenUniques, 0f);
                Messages.Message("TSA_WD_Warehouse_ShipInsufficientVirtual".Translate(), sender, MessageTypeDefOf.RejectInput);
                return false;
            }

            Outpost_Warehouse_Delivery.CyanDeliveryMouseOverlayActive = false;
            if (!WorldActions_Traveler.SpawnDeliveryTravelerFrom(
                    sender, withdrawn, destination, viaDropPod, hasVirtual ? virtualFood : 0f, takenUniques))
            {
                RollBack(sender, armory, withdrawn, takenUniques, hasVirtual ? virtualFood : 0f);
                Messages.Message("TSA_WD_Warehouse_InvalidDestination".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }

            string msgKey = viaDropPod ? "TSA_WD_Warehouse_ShipLaunchedDropPod" : "TSA_WD_Warehouse_ShipLaunched";
            Messages.Message(msgKey.Translate(destination.LabelCap), sender, MessageTypeDefOf.PositiveEvent);
            Window_OutpostOverview.InvalidateCache();
            return true;
        }

        /// <summary>
        /// Sender-side gates (manual defense, pod research, strength) that callers must pass before
        /// stripping pawns or withdrawing anything. <paramref name="rejectKey"/> may take the sender label as {0}.
        /// </summary>
        public static bool CanLaunchFrom(WorldObject_WD_Outpost sender, bool viaDropPod, out string rejectKey)
        {
            rejectKey = null;
            if (sender == null || sender.Destroyed)
            {
                rejectKey = "TSA_WD_Warehouse_InvalidDestination";
                return false;
            }
            if (sender.ManualDefenseActive)
            {
                rejectKey = "TSA_WD_Armory_FailManualDefense";
                return false;
            }
            if (viaDropPod && !RapidResponseUtility.TransportPodsResearched())
            {
                rejectKey = "TSA_WD_RapidResponse_DropPodsNeedResearch";
                return false;
            }
            float cost = WorldDominationMod.settings?.outpostDeliveryStrengthCost ?? 50f;
            CompViralSpread viral = sender.GetComponent<CompViralSpread>();
            if (viral == null || viral.strength < cost)
            {
                rejectKey = "TSA_WD_Warehouse_AutoShipInsufficientStrength";
                return false;
            }
            return true;
        }

        private static void RollBack(
            WorldObject_WD_Outpost sender,
            CompOutpostArmory armory,
            List<ThingDefCountClass> rows,
            List<Thing> uniques,
            float virtualFood)
        {
            if (rows != null)
            {
                for (int i = 0; i < rows.Count; i++)
                    armory.DepositStockRow(rows[i]);
            }
            if (uniques != null)
            {
                for (int i = 0; i < uniques.Count; i++)
                {
                    Thing t = uniques[i];
                    if (t == null || t.Destroyed || t.holdingOwner != null) continue;
                    if (!armory.Uniques.TryAddOrTransfer(t, canMergeWithExistingStacks: false) && !t.Destroyed)
                        t.Destroy(DestroyMode.Vanish);
                }
            }
            if (virtualFood > 0.001f)
            {
                var logi = sender.GetComponent<CompOutpostLogistics>();
                if (logi != null) CompOutpostLogistics.AddVirtualFoodNutrition(logi, virtualFood);
            }
        }
    }
}
