using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
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
        /// <summary>Daily auto delivery always ships non-gear goods; Armory categories only when opted in.</summary>
        public bool autoShipApparel;
        /// <summary>Includes CE ammo.</summary>
        public bool autoShipWeapons;
        public bool autoShipFood;
        public bool autoShipDrugs;
        public bool autoShipMedicine;
        /// <summary>Set in <see cref="Initialize"/> for newly created warehouses; cleared on load so cleared destinations stay cleared.</summary>
        private bool pendingApplyDefaultColonyShipDest;

        /// <summary>Dated food slots; see <see cref="WarehouseFoodLot"/>. Empty when spoilage is disabled.</summary>
        public List<WarehouseFoodLot> foodLots = new List<WarehouseFoodLot>();
        /// <summary>Day the fridge lost power, or -1 while it is cold. Refrigerated food spoils <see cref="FridgeOutageDays"/> days after this.</summary>
        public int fridgeOutageStartDay = -1;
        /// <summary>Goods that did not fit; runtime only. Sent to the colony by <see cref="FlushOverflow"/>.</summary>
        private readonly List<ThingDefCountClass> pendingOverflow = new List<ThingDefCountClass>();

        /// <summary>Days a refrigerated stack survives without power before it is lost.</summary>
        public const int FridgeOutageDays = 3;
        private const int MaintainIntervalTicks = 250;

        public static bool CapacityEnabled =>
            WorldDominationMod.settings?.warehouseCapacityEnabled ?? WorldDominationSettings.DefWarehouseCapacityEnabled;

        public static bool SpoilageEnabled =>
            WorldDominationMod.settings?.warehouseFoodSpoilageEnabled ?? WorldDominationSettings.DefWarehouseFoodSpoilageEnabled;

        public static int SpoilDays =>
            Mathf.Clamp(Mathf.RoundToInt(WorldDominationMod.settings?.warehouseFoodSpoilDays ?? WorldDominationSettings.DefWarehouseFoodSpoilDays), 5, 60);

        private WorldObject_WD_Outpost Outpost => parent as WorldObject_WD_Outpost;

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
            Scribe_Values.Look(ref autoShipApparel, "autoShipApparel", false);
            Scribe_Values.Look(ref autoShipWeapons, "autoShipWeapons", false);
            Scribe_Values.Look(ref autoShipFood, "autoShipFood", false);
            Scribe_Values.Look(ref autoShipDrugs, "autoShipDrugs", false);
            Scribe_Values.Look(ref autoShipMedicine, "autoShipMedicine", false);
            Scribe_Collections.Look(ref foodLots, "foodLots", LookMode.Deep);
            Scribe_Values.Look(ref fridgeOutageStartDay, "fridgeOutageStartDay", -1);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                pendingApplyDefaultColonyShipDest = false;
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (storedItems == null)
                    storedItems = new List<ThingDefCountClass>();
                else
                    PruneUnusableMinifiedStock(storedItems);
                // Old saves have no lots: existing food becomes one lot dated today.
                foodLots = foodLots ?? new List<WarehouseFoodLot>();
                foodLots.RemoveAll(l => l == null || l.def == null || l.count <= 0);
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
            if (parent == null || parent.Destroyed) return;
            if (parent.IsHashIntervalTick(MaintainIntervalTicks))
                Maintain();
            if (!autoShipEnabled) return;
            if (!parent.IsHashIntervalTick(GenDate.TicksPerDay)) return;
            TryAutoShipEntireStock();
        }

        // ---- Capacity ----

        /// <summary>Base capacity setting plus built warehouse upgrades, in kg.</summary>
        public float CapacityKg =>
            (WorldDominationMod.settings?.warehouseCapacityKg ?? WorldDominationSettings.DefWarehouseCapacityKg)
            + (Outpost?.GetWarehouseCapacityBonusKg() ?? 0f);

        public float UsedMassKg
        {
            get
            {
                float total = 0f;
                if (storedItems == null) return total;
                for (int i = 0; i < storedItems.Count; i++)
                {
                    var e = storedItems[i];
                    if (e?.thingDef == null || e.count <= 0) continue;
                    total += RowUnitMass(e) * e.count;
                }
                return total;
            }
        }

        public float FreeMassKg => Mathf.Max(0f, CapacityKg - UsedMassKg);

        private static float RowUnitMass(ThingDefCountClass e) =>
            e.thingDef.GetStatValueAbstract(StatDefOf.Mass, e.stuff);

        /// <summary>How many units of <paramref name="row"/> still fit (whole units).</summary>
        public int FitCount(ThingDefCountClass row)
        {
            if (row?.thingDef == null || row.count <= 0) return 0;
            if (!CapacityEnabled) return row.count;
            float unit = RowUnitMass(row);
            if (unit <= 0.0001f) return row.count;
            float free = CapacityKg - UsedMassKg;
            if (free <= 0f) return 0;
            return Mathf.Min(row.count, Mathf.FloorToInt(free / unit + 0.0001f));
        }

        /// <summary>
        /// Stores as much of <paramref name="row"/> as fits and queues the rest as overflow. Returns the
        /// stored count. Call <see cref="FlushOverflow"/> once the whole delivery is in.
        /// </summary>
        public int DepositRowCapped(ThingDefCountClass row)
        {
            if (row?.thingDef == null || row.count <= 0) return 0;
            if (IsUnusableMinifiedDef(row.thingDef)) return 0;
            if (storedItems == null) storedItems = new List<ThingDefCountClass>();

            int fit = FitCount(row);
            if (fit > 0)
                MergeCount(storedItems, PlainStockRow(row, fit));
            if (fit < row.count)
                MergeCount(pendingOverflow, PlainStockRow(row, row.count - fit));
            if (fit > 0 && IsSpoilingFood(row.thingDef))
                ReconcileFoodLots();
            return fit;
        }

        /// <summary>
        /// Sends queued overflow straight to the colony and sends the "Warehouse is filled" letter.
        /// No-op when nothing is queued.
        /// </summary>
        public void FlushOverflow()
        {
            if (pendingOverflow.Count == 0) return;
            var sent = new List<ThingDefCountClass>(pendingOverflow);
            pendingOverflow.Clear();

            SettlementBuyUtility.RefundItems(null, sent);

            if (!(WorldDominationMod.settings?.notifyWarehouseFull ?? WorldDominationSettings.DefNotifyWarehouseFull))
                return;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("TSA_WD_Warehouse_Full_Letter".Translate(parent.LabelCap, Mathf.RoundToInt(CapacityKg)));
            for (int i = 0; i < sent.Count; i++)
                sb.AppendLine("- " + sent[i].thingDef.LabelCap + " " + sent[i].count + "x");
            Find.LetterStack.ReceiveLetter(
                "TSA_WD_Warehouse_Full_LetterLabel".Translate(parent.LabelCap),
                sb.ToString().TrimEnd(), LetterDefOf.NegativeEvent, parent);
        }

        // ---- Food lots, spoilage, fridge ----

        /// <summary>Food that gets a dated slot: ingestibles with a rot timer.</summary>
        public static bool IsSpoilingFood(ThingDef def)
        {
            if (def == null || !def.IsIngestible) return false;
            CompProperties_Rottable rot = def.GetCompProperties<CompProperties_Rottable>();
            return rot != null && rot.daysToRotStart < 1000f;
        }

        /// <summary>Fridge capacity (kg of food) from built upgrades.</summary>
        public float FridgeCapacityKg => SpoilageEnabled ? (Outpost?.GetFridgeCapacityKg() ?? 0f) : 0f;

        public float FridgeUsedKg
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < foodLots.Count; i++)
                {
                    var l = foodLots[i];
                    if (l.refrigerated) total += l.def.GetStatValueAbstract(StatDefOf.Mass) * l.count;
                }
                return total;
            }
        }

        /// <summary>False while the warehouse has fridges but no power.</summary>
        public bool FridgePowered
        {
            get
            {
                WorldObject_WD_Outpost o = Outpost;
                return o == null || FridgeCapacityKg <= 0f || o.IsPowered;
            }
        }

        public int DaysUntilSpoil(WarehouseFoodLot lot)
        {
            if (lot == null) return 0;
            if (lot.refrigerated)
                return fridgeOutageStartDay < 0 ? int.MaxValue : Mathf.Max(0, fridgeOutageStartDay + FridgeOutageDays - GenDate.DaysPassed);
            return Mathf.Max(0, lot.arrivalDay + SpoilDays - GenDate.DaysPassed);
        }

        /// <summary>
        /// Brings <see cref="foodLots"/> in line with <see cref="storedItems"/>. Surplus in the store is a
        /// new arrival dated today; a shortfall is a withdrawal, taken oldest dated lot first and
        /// refrigerated food last. Then fills the fridge with the oldest food.
        /// </summary>
        public void ReconcileFoodLots()
        {
            if (foodLots == null) foodLots = new List<WarehouseFoodLot>();
            if (!SpoilageEnabled)
            {
                foodLots.Clear();
                fridgeOutageStartDay = -1;
                return;
            }

            int today = GenDate.DaysPassed;
            var storeTotals = new Dictionary<ThingDef, int>();
            if (storedItems != null)
            {
                for (int i = 0; i < storedItems.Count; i++)
                {
                    var e = storedItems[i];
                    if (e?.thingDef == null || e.count <= 0 || !IsSpoilingFood(e.thingDef)) continue;
                    storeTotals.TryGetValue(e.thingDef, out int n);
                    storeTotals[e.thingDef] = n + e.count;
                }
            }

            var lotTotals = new Dictionary<ThingDef, int>();
            for (int i = 0; i < foodLots.Count; i++)
            {
                lotTotals.TryGetValue(foodLots[i].def, out int n);
                lotTotals[foodLots[i].def] = n + foodLots[i].count;
            }

            foreach (var kv in storeTotals)
            {
                lotTotals.TryGetValue(kv.Key, out int inLots);
                if (kv.Value > inLots)
                    AddLot(kv.Key, kv.Value - inLots, today, false);
            }
            foreach (var kv in lotTotals)
            {
                storeTotals.TryGetValue(kv.Key, out int inStore);
                if (inStore < kv.Value)
                    TrimLots(kv.Key, kv.Value - inStore);
            }

            RebalanceFridge(today);
            foodLots.RemoveAll(l => l.count <= 0);
        }

        private void AddLot(ThingDef def, int count, int day, bool refrigerated)
        {
            for (int i = 0; i < foodLots.Count; i++)
            {
                var l = foodLots[i];
                if (l.def == def && l.refrigerated == refrigerated && (refrigerated || l.arrivalDay == day))
                {
                    l.count += count;
                    return;
                }
            }
            foodLots.Add(new WarehouseFoodLot { def = def, count = count, arrivalDay = day, refrigerated = refrigerated });
        }

        /// <summary>Removes <paramref name="amount"/> of <paramref name="def"/> from lots: oldest dated first, fridge last.</summary>
        private void TrimLots(ThingDef def, int amount)
        {
            while (amount > 0)
            {
                WarehouseFoodLot pick = null;
                for (int i = 0; i < foodLots.Count; i++)
                {
                    var l = foodLots[i];
                    if (l.def != def || l.count <= 0) continue;
                    if (pick == null
                        || (pick.refrigerated && !l.refrigerated)
                        || (pick.refrigerated == l.refrigerated && l.arrivalDay < pick.arrivalDay))
                        pick = l;
                }
                if (pick == null) return;
                int take = Mathf.Min(pick.count, amount);
                pick.count -= take;
                amount -= take;
            }
        }

        /// <summary>Moves the oldest dated food into free fridge space; pushes food out if the fridge shrank.</summary>
        private void RebalanceFridge(int today)
        {
            float cap = FridgeCapacityKg;
            float used = FridgeUsedKg;

            // Fridge shrank (or was removed): excess goes back to dated lots, dated today.
            for (int i = 0; i < foodLots.Count && used > cap + 0.001f; i++)
            {
                var l = foodLots[i];
                if (!l.refrigerated || l.count <= 0) continue;
                float unit = l.def.GetStatValueAbstract(StatDefOf.Mass);
                int move = unit <= 0.0001f ? l.count : Mathf.Min(l.count, Mathf.CeilToInt((used - cap) / unit));
                l.count -= move;
                used -= unit * move;
                AddLot(l.def, move, today, false);
            }

            while (cap - used > 0.001f)
            {
                WarehouseFoodLot oldest = null;
                for (int i = 0; i < foodLots.Count; i++)
                {
                    var l = foodLots[i];
                    if (l.refrigerated || l.count <= 0) continue;
                    if (oldest == null || l.arrivalDay < oldest.arrivalDay) oldest = l;
                }
                if (oldest == null) break;
                float unit = oldest.def.GetStatValueAbstract(StatDefOf.Mass);
                int fit = unit <= 0.0001f ? oldest.count : Mathf.Min(oldest.count, Mathf.FloorToInt((cap - used) / unit + 0.0001f));
                if (fit <= 0) break;
                oldest.count -= fit;
                used += unit * fit;
                AddLot(oldest.def, fit, today, true);
                foodLots.RemoveAll(l => l.count <= 0);
            }
        }

        /// <summary>Periodic upkeep: ledger sync, fridge power watch, and spoilage.</summary>
        public void Maintain()
        {
            if (parent == null || parent.Destroyed) return;
            if (pendingOverflow.Count > 0) FlushOverflow();
            ReconcileFoodLots();
            if (!SpoilageEnabled) return;

            int today = GenDate.DaysPassed;
            bool hasFridgeFood = false;
            for (int i = 0; i < foodLots.Count; i++)
                if (foodLots[i].refrigerated) { hasFridgeFood = true; break; }

            if (FridgePowered)
            {
                fridgeOutageStartDay = -1;
            }
            else if (hasFridgeFood)
            {
                if (fridgeOutageStartDay < 0)
                {
                    fridgeOutageStartDay = today;
                    NotifySpoilage("TSA_WD_Warehouse_FridgeNoPower", parent.LabelCap, FridgeOutageDays);
                }
            }
            else
            {
                fridgeOutageStartDay = -1;
            }

            int spoiled = 0;
            var spoiledLines = new List<string>();
            for (int i = foodLots.Count - 1; i >= 0; i--)
            {
                var l = foodLots[i];
                bool spoils = l.refrigerated
                    ? fridgeOutageStartDay >= 0 && today - fridgeOutageStartDay >= FridgeOutageDays
                    : today - l.arrivalDay >= SpoilDays;
                if (!spoils) continue;
                int removed = RemoveFromStore(l.def, l.count);
                spoiled += removed;
                spoiledLines.Add(l.def.LabelCap + " " + removed + "x");
                foodLots.RemoveAt(i);
            }
            if (spoiled > 0)
            {
                if (!foodLots.Exists(l => l.refrigerated))
                    fridgeOutageStartDay = -1;
                NotifySpoilage("TSA_WD_Warehouse_FoodSpoiled", parent.LabelCap, string.Join(", ", spoiledLines));
            }
        }

        private void NotifySpoilage(string key, params NamedArgument[] args)
        {
            Messages.Message(key.Translate(args), parent, MessageTypeDefOf.NegativeEvent, false);
        }

        private int RemoveFromStore(ThingDef def, int amount)
        {
            int remaining = amount;
            for (int i = 0; i < storedItems.Count && remaining > 0; i++)
            {
                var e = storedItems[i];
                if (e?.thingDef != def || e.count <= 0) continue;
                int take = Mathf.Min(e.count, remaining);
                e.count -= take;
                remaining -= take;
            }
            PruneEmpty(storedItems);
            return amount - remaining;
        }

        /// <summary>
        /// Daily full-stock ship when enabled. Empty stock and missing strength bail before withdraw/spawn work.
        /// Physical warehouse stock only — never withdraws or ships virtual food pool.
        /// </summary>
        public void TryAutoShipEntireStock()
        {
            if (!autoShipEnabled) return;
            if (!(parent is WorldObject_WD_Outpost warehouse) || warehouse.Faction != Faction.OfPlayer)
                return;
            if (!HasAutoShippableStock()) return;

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

            // Physical rows only; virtual food is ad-hoc Ship Now exclusive.
            var request = new List<ThingDefCountClass>();
            for (int i = 0; i < storedItems.Count; i++)
            {
                var e = storedItems[i];
                if (e?.thingDef == null || e.count <= 0) continue;
                if (!AutoShipIncludes(e.thingDef)) continue;
                request.Add(new ThingDefCountClass(e.thingDef, e.count)
                {
                    stuff = e.stuff,
                    quality = e.quality
                });
            }
            if (request.Count == 0) return;

            bool viaDropPod = dispatchViaDropPod && RapidResponseUtility.TransportPodsResearched();
            if (!OutpostStorageShipping.TryLaunch(warehouse, request, null, 0f, dest, viaDropPod))
                return;
            lastAutoShipTick = Find.TickManager.TicksGame;
        }

        /// <summary>Non-gear goods always ship; Armory categories only when their flag is on.</summary>
        public bool AutoShipIncludes(ThingDef def)
        {
            if (def == null) return false;
            if (!OutpostArmoryUtility.IsArmoryItem(def)) return true;
            switch (OutpostArmoryUtility.BucketFor(def))
            {
                case ArmoryBucket.Armor: return autoShipApparel;
                case ArmoryBucket.Food: return autoShipFood;
                case ArmoryBucket.Drugs: return autoShipDrugs;
                case ArmoryBucket.Medicine: return autoShipMedicine;
                default: return autoShipWeapons;
            }
        }

        /// <summary>True when daily auto delivery would ship at least one stored row.</summary>
        public bool HasAutoShippableStock()
        {
            if (storedItems == null) return false;
            for (int i = 0; i < storedItems.Count; i++)
            {
                var e = storedItems[i];
                if (e?.thingDef != null && e.count > 0 && AutoShipIncludes(e.thingDef))
                    return true;
            }
            return false;
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
            ReconcileFoodLots();
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
            ReconcileFoodLots();
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
                DepositRowCapped(entry);
            }
        }

        /// <summary>
        /// Row with def+stuff+quality only. Store rows never carry <see cref="WdStockExtras"/>: those live
        /// in a runtime-only table, and requests rebuilt from def+stuff+quality must still match.
        /// </summary>
        public static ThingDefCountClass PlainStockRow(ThingDefCountClass src, int count)
        {
            if (src?.thingDef == null) return null;
            return new ThingDefCountClass(src.thingDef, count)
            {
                stuff = src.stuff,
                quality = src.quality
            };
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
            ReconcileFoodLots();
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

        /// <summary>Stable UI/ship key for a stock row (def + stuff + quality + payment extras).</summary>
        public static string StockKey(ThingDefCountClass e)
        {
            if (e?.thingDef == null) return "";
            bool worn = WdStockExtrasTable.GetWornByCorpse(e);
            bool bio = WdStockExtrasTable.GetBiocoded(e);
            int hp = WdStockExtrasTable.GetHitPoints(e);
            return e.thingDef.defName + "\0" + (e.stuff?.defName ?? "") + "\0" + ((int)e.quality).ToString()
                + "\0" + (worn ? "1" : "0")
                + "\0" + hp.ToString()
                + "\0" + (bio ? "1" : "0");
        }

        public static bool SameStockIdentity(ThingDefCountClass a, ThingDefCountClass b)
        {
            if (a == null || b == null) return false;
            if (a.thingDef != b.thingDef || a.stuff != b.stuff || a.quality != b.quality)
                return false;
            if (WdStockExtrasTable.GetWornByCorpse(a) != WdStockExtrasTable.GetWornByCorpse(b))
                return false;
            if (WdStockExtrasTable.GetBiocoded(a) != WdStockExtrasTable.GetBiocoded(b))
                return false;
            return WdStockExtrasTable.GetHitPoints(a) == WdStockExtrasTable.GetHitPoints(b);
        }

        internal static void MergeCount(List<ThingDefCountClass> list, ThingDefCountClass add)
        {
            if (list == null || add?.thingDef == null || add.count <= 0) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (!SameStockIdentity(list[i], add)) continue;
                list[i].count += add.count;
                return;
            }
            list.Add(CloneStockRowPreservingExtras(add, add.count));
        }

        /// <summary>Clone a stock row, preserving attached <see cref="WdStockExtras"/> when present.</summary>
        public static ThingDefCountClass CloneStockRowPreservingExtras(ThingDefCountClass src, int count)
        {
            if (src == null) return null;
            var clone = new ThingDefCountClass(src.thingDef, count)
            {
                stuff = src.stuff,
                quality = src.quality
            };
            WdStockExtrasTable.CopyTo(src, clone);
            return clone;
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

        internal static void PruneEmpty(List<ThingDefCountClass> list)
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
        internal static void PruneUnusableMinifiedStock(List<ThingDefCountClass> list)
        {
            if (list == null) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var e = list[i];
                if (e == null || e.thingDef == null || e.count <= 0 || IsUnusableMinifiedDef(e.thingDef))
                    list.RemoveAt(i);
            }
        }

        internal static bool IsUnusableMinifiedDef(ThingDef def) =>
            def?.thingClass != null && typeof(MinifiedThing).IsAssignableFrom(def.thingClass);

        /// <summary>
        /// Preserve stuff from the live thing when the def is stuffable and the candidate still CanMake it.
        /// Does not invent random stuff on deposit (unlike scavenging generation).
        /// </summary>
        internal static ThingDef ResolveStuffForDeposit(ThingDef def, ThingDef candidateStuff)
        {
            if (def == null || !def.MadeFromStuff) return null;
            if (candidateStuff != null && candidateStuff.IsStuff && candidateStuff.stuffProps != null
                && candidateStuff.stuffProps.CanMake(def))
                return candidateStuff;
            return null;
        }
    }
}
