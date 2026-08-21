using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    public class CompProperties_OutpostWarehouse : WorldObjectCompProperties
    {
        public CompProperties_OutpostWarehouse() => compClass = typeof(CompOutpostWarehouse);
    }

    /// <summary>Abstract item storage for warehouse outposts (def + count + stuff + quality; no rot or ticking).</summary>
    public class CompOutpostWarehouse : WorldObjectComp
    {
        public List<ThingDefCountClass> storedItems = new List<ThingDefCountClass>();
        public int shipDestinationWorldObjectId = -1;
        /// <summary>When true, upgrade launches from this warehouse and auto goods ships use drop pods.</summary>
        public bool dispatchViaDropPod;
        /// <summary>When true, once per day ship entire stock to the configured destination.</summary>
        public bool autoShipEnabled;
        public int lastAutoShipTick = -999999;
        /// <summary>Set in <see cref="Initialize"/> for newly created warehouses; cleared on load so cleared destinations stay cleared.</summary>
        private bool pendingApplyDefaultColonyShipDest;

        public override void Initialize(WorldObjectCompProperties props)
        {
            base.Initialize(props);
            pendingApplyDefaultColonyShipDest = true;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref storedItems, "storedItems", LookMode.Deep);
            Scribe_Values.Look(ref shipDestinationWorldObjectId, "shipDestinationWorldObjectId", -1);
            Scribe_Values.Look(ref dispatchViaDropPod, "dispatchViaDropPod", false);
            Scribe_Values.Look(ref autoShipEnabled, "autoShipEnabled", false);
            Scribe_Values.Look(ref lastAutoShipTick, "lastAutoShipTick", -999999);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                pendingApplyDefaultColonyShipDest = false;
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (storedItems == null)
                    storedItems = new List<ThingDefCountClass>();
                else
                    PruneUnusableMinifiedStock(storedItems);
            }
        }

        /// <summary>New warehouses default ship destination to the nearest player colony. No-op after load or if already set.</summary>
        public void TryApplyDefaultColonyShipDestination()
        {
            if (!pendingApplyDefaultColonyShipDest) return;
            pendingApplyDefaultColonyShipDest = false;
            if (shipDestinationWorldObjectId >= 0) return;
            if (!(parent is WorldObject_WD_Outpost warehouse) || warehouse.Faction != Faction.OfPlayer)
                return;
            if (!Outpost_Warehouse_Delivery.TryFindNearestPlayerColony(warehouse.Tile, out WorldObject colony))
                return;
            if (!Outpost_Warehouse_Delivery.IsValidItemDeliveryDestination(colony, warehouse))
                return;
            shipDestinationWorldObjectId = colony.ID;
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!autoShipEnabled) return;
            if (parent == null || parent.Destroyed) return;
            if (!parent.IsHashIntervalTick(GenDate.TicksPerDay)) return;
            TryAutoShipEntireStock();
        }

        /// <summary>
        /// Daily full-stock ship when enabled. Empty stock and missing strength bail before withdraw/spawn work.
        /// </summary>
        public void TryAutoShipEntireStock()
        {
            if (!autoShipEnabled) return;
            if (!(parent is WorldObject_WD_Outpost warehouse) || warehouse.Faction != Faction.OfPlayer)
                return;
            if (GetTotalStoredItemCount() <= 0) return;

            WorldObject dest = ResolveShipDestination();
            if (dest == null) return;

            float cost = WorldDominationMod.settings?.outpostDeliveryStrengthCost ?? 50f;
            CompViralSpread viral = warehouse.GetComponent<CompViralSpread>();
            if (viral == null || viral.strength < cost)
            {
                Messages.Message(
                    "TSA_WD_Warehouse_AutoShipInsufficientStrength".Translate(warehouse.LabelCap),
                    warehouse,
                    MessageTypeDefOf.RejectInput);
                return;
            }

            var request = new List<ThingDefCountClass>();
            for (int i = 0; i < storedItems.Count; i++)
            {
                var e = storedItems[i];
                if (e?.thingDef == null || e.count <= 0) continue;
                request.Add(new ThingDefCountClass(e.thingDef, e.count)
                {
                    stuff = e.stuff,
                    quality = e.quality
                });
            }
            if (request.Count == 0) return;

            if (!TryWithdraw(request)) return;

            bool viaDropPod = dispatchViaDropPod && RapidResponseUtility.TransportPodsResearched();
            WorldActions_Traveler.SpawnOutpostDeliveryTraveler(warehouse, request, dest, viaDropPod);
            lastAutoShipTick = Find.TickManager.TicksGame;
            string msgKey = viaDropPod ? "TSA_WD_Warehouse_ShipLaunchedDropPod" : "TSA_WD_Warehouse_ShipLaunched";
            Messages.Message(msgKey.Translate(dest.LabelCap), warehouse, MessageTypeDefOf.PositiveEvent);
        }

        public static CompOutpostWarehouse Get(WorldObject_WD_Outpost outpost) =>
            outpost?.GetComponent<CompOutpostWarehouse>();

        /// <summary>Total count across all rows for <paramref name="def"/> (any stuff/quality).</summary>
        public int GetStoredCount(ThingDef def)
        {
            if (def == null || storedItems == null) return 0;
            int total = 0;
            for (int i = 0; i < storedItems.Count; i++)
            {
                var e = storedItems[i];
                if (e?.thingDef == def && e.count > 0)
                    total += e.count;
            }
            return total;
        }

        /// <summary>Count for a specific def+stuff+quality stock row.</summary>
        public int GetStoredCountMatching(ThingDefCountClass match)
        {
            if (match?.thingDef == null || storedItems == null) return 0;
            int total = 0;
            for (int i = 0; i < storedItems.Count; i++)
            {
                var e = storedItems[i];
                if (e == null || e.count <= 0) continue;
                if (SameStockIdentity(e, match))
                    total += e.count;
            }
            return total;
        }

        /// <summary>Total count of any stored stone-block defs (defName starting with "Blocks"); used by flexible AnyStoneBlocks upgrade costs.</summary>
        public int GetStoredStoneBlocksCount()
        {
            if (storedItems == null) return 0;
            int total = 0;
            for (int i = 0; i < storedItems.Count; i++)
            {
                var e = storedItems[i];
                if (e?.thingDef?.defName != null && e.thingDef.defName.StartsWith("Blocks"))
                    total += e.count;
            }
            return total;
        }

        /// <summary>Removes up to <paramref name="amount"/> of <paramref name="def"/>; returns how many were actually withdrawn.</summary>
        public int WithdrawUpTo(ThingDef def, int amount)
        {
            if (def == null || amount <= 0 || storedItems == null) return 0;
            int remaining = amount;
            for (int i = 0; i < storedItems.Count && remaining > 0; i++)
            {
                var e = storedItems[i];
                if (e?.thingDef != def || e.count <= 0) continue;
                int take = e.count < remaining ? e.count : remaining;
                e.count -= take;
                remaining -= take;
            }
            PruneEmpty(storedItems);
            return amount - remaining;
        }

        /// <summary>Removes up to <paramref name="amount"/> matching def+stuff+quality; returns how many were withdrawn.</summary>
        public int WithdrawUpToMatching(ThingDefCountClass match, int amount)
        {
            if (match?.thingDef == null || amount <= 0 || storedItems == null) return 0;
            int remaining = amount;
            for (int i = 0; i < storedItems.Count && remaining > 0; i++)
            {
                var e = storedItems[i];
                if (e == null || e.count <= 0 || !SameStockIdentity(e, match)) continue;
                int take = e.count < remaining ? e.count : remaining;
                e.count -= take;
                remaining -= take;
            }
            PruneEmpty(storedItems);
            return amount - remaining;
        }

        /// <summary>Removes up to <paramref name="amount"/> of any stone-block defs; returns how many were actually withdrawn.</summary>
        public int WithdrawStoneBlocksUpTo(int amount)
        {
            if (amount <= 0 || storedItems == null) return 0;
            int remaining = amount;
            for (int i = 0; i < storedItems.Count && remaining > 0; i++)
            {
                var e = storedItems[i];
                if (e?.thingDef?.defName == null || !e.thingDef.defName.StartsWith("Blocks")) continue;
                int take = e.count < remaining ? e.count : remaining;
                e.count -= take;
                remaining -= take;
            }
            PruneEmpty(storedItems);
            return amount - remaining;
        }

        public int GetTotalStackKinds() => storedItems?.Count ?? 0;

        /// <summary>Sum of all stack counts across every stored item kind.</summary>
        public int GetTotalStoredItemCount()
        {
            if (storedItems == null) return 0;
            int sum = 0;
            for (int i = 0; i < storedItems.Count; i++)
            {
                var e = storedItems[i];
                if (e?.thingDef == null || e.count <= 0) continue;
                sum += e.count;
            }
            return sum;
        }

        public void TryDeposit(List<ThingDefCountClass> items)
        {
            if (items == null || items.Count == 0) return;
            if (storedItems == null) storedItems = new List<ThingDefCountClass>();
            for (int i = 0; i < items.Count; i++)
            {
                var entry = items[i];
                if (entry?.thingDef == null || entry.count <= 0) continue;
                // Abstract minified-crate defs are unrecoverable in def+count storage; never keep them.
                if (IsUnusableMinifiedDef(entry.thingDef)) continue;
                MergeCount(storedItems, entry);
            }
        }

        public void TryDepositThings(IEnumerable<Thing> things)
        {
            if (things == null) return;
            var list = new List<ThingDefCountClass>();
            foreach (Thing t in things)
            {
                if (t?.def == null || t.Destroyed) continue;
                // Pods/caravans carry sculptures as MinifiedThing crates; store the inner def, never the crate.
                Thing content = t.GetInnerIfMinified() ?? t;
                if (content?.def == null || content.Destroyed) continue;
                if (IsUnusableMinifiedDef(content.def)) continue;
                int count = content.stackCount > 0 ? content.stackCount : 1;
                var row = new ThingDefCountClass(content.def, count)
                {
                    stuff = ResolveStuffForDeposit(content.def, content.Stuff)
                };
                if (content.TryGetQuality(out QualityCategory q))
                    row.quality = q;
                MergeCount(list, row);
            }
            TryDeposit(list);
        }

        public bool TryWithdraw(List<ThingDefCountClass> request)
        {
            if (request == null || request.Count == 0) return false;
            for (int i = 0; i < request.Count; i++)
            {
                var r = request[i];
                if (r?.thingDef == null || r.count <= 0) return false;
                if (GetStoredCountMatching(r) < r.count) return false;
            }
            for (int i = 0; i < request.Count; i++)
            {
                var r = request[i];
                SubtractCountMatching(storedItems, r);
            }
            PruneEmpty(storedItems);
            return true;
        }

        public string GetInspectSummary()
        {
            if (storedItems == null || storedItems.Count == 0)
                return "TSA_WD_Warehouse_InspectEmpty".Translate();
            return "TSA_WD_Warehouse_InspectStock".Translate(storedItems.Count);
        }

        /// <summary>Overview Produces column: same kinds/total metrics as the Stats tab.</summary>
        public string GetOverviewStoresLine()
        {
            int kinds = GetTotalStackKinds();
            int totalItems = GetTotalStoredItemCount();
            return "TSA_WD_OutpostStats_Row_WarehouseKinds".Translate() + ": " + kinds + "\n"
                + "TSA_WD_OutpostStats_Row_WarehouseTotalItems".Translate() + ": " + totalItems;
        }

        public WorldObject ResolveShipDestination()
        {
            if (!(parent is WorldObject_WD_Outpost warehouse))
                return null;

            WorldObject stored = null;
            if (shipDestinationWorldObjectId >= 0)
            {
                stored = Find.WorldObjects?.AllWorldObjects?.Find(
                    o => o != null && o.ID == shipDestinationWorldObjectId);
            }

            if (Outpost_Warehouse_Delivery.IsValidItemDeliveryDestination(stored, warehouse))
                return stored;

            // Stored dest gone or unset: always fall back to nearest player colony when possible.
            if (!Outpost_Warehouse_Delivery.TryFindNearestPlayerColony(warehouse.Tile, out WorldObject colony)
                || !Outpost_Warehouse_Delivery.IsValidItemDeliveryDestination(colony, warehouse))
            {
                shipDestinationWorldObjectId = -1;
                return null;
            }

            shipDestinationWorldObjectId = colony.ID;
            return colony;
        }

        /// <summary>Stable UI/ship key for a stock row (def + stuff + quality).</summary>
        public static string StockKey(ThingDefCountClass e)
        {
            if (e?.thingDef == null) return "";
            return e.thingDef.defName + "\0" + (e.stuff?.defName ?? "") + "\0" + ((int)e.quality).ToString();
        }

        private static bool SameStockIdentity(ThingDefCountClass a, ThingDefCountClass b) =>
            a != null && b != null
            && a.thingDef == b.thingDef
            && a.stuff == b.stuff
            && a.quality == b.quality;

        private static void MergeCount(List<ThingDefCountClass> list, ThingDefCountClass add)
        {
            if (list == null || add?.thingDef == null || add.count <= 0) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (!SameStockIdentity(list[i], add)) continue;
                list[i].count += add.count;
                return;
            }
            list.Add(new ThingDefCountClass(add.thingDef, add.count)
            {
                stuff = add.stuff,
                quality = add.quality
            });
        }

        private static void SubtractCountMatching(List<ThingDefCountClass> list, ThingDefCountClass match)
        {
            if (list == null || match?.thingDef == null || match.count <= 0) return;
            int remaining = match.count;
            for (int i = 0; i < list.Count && remaining > 0; i++)
            {
                if (!SameStockIdentity(list[i], match)) continue;
                int take = list[i].count < remaining ? list[i].count : remaining;
                list[i].count -= take;
                remaining -= take;
            }
        }

        private static void PruneEmpty(List<ThingDefCountClass> list)
        {
            if (list == null) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null || list[i].thingDef == null || list[i].count <= 0)
                    list.RemoveAt(i);
            }
        }

        /// <summary>
        /// Removes saved "Minified things" rows. Inner sculptures were never stored, so these stacks
        /// cannot be shipped (MakeDeliveryThing rejects MinifiedThing) and would vanish on withdraw.
        /// </summary>
        private static void PruneUnusableMinifiedStock(List<ThingDefCountClass> list)
        {
            if (list == null) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var e = list[i];
                if (e == null || e.thingDef == null || e.count <= 0 || IsUnusableMinifiedDef(e.thingDef))
                    list.RemoveAt(i);
            }
        }

        private static bool IsUnusableMinifiedDef(ThingDef def) =>
            def?.thingClass != null && typeof(MinifiedThing).IsAssignableFrom(def.thingClass);

        /// <summary>
        /// Preserve stuff from the live thing when the def is stuffable and the candidate still CanMake it.
        /// Does not invent random stuff on deposit (unlike scavenging generation).
        /// </summary>
        private static ThingDef ResolveStuffForDeposit(ThingDef def, ThingDef candidateStuff)
        {
            if (def == null || !def.MadeFromStuff) return null;
            if (candidateStuff != null && candidateStuff.IsStuff && candidateStuff.stuffProps != null
                && candidateStuff.stuffProps.CanMake(def))
                return candidateStuff;
            return null;
        }
    }
}
