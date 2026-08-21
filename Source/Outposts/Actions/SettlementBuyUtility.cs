using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Peaceful buy of ally/neutral WD NPC settlements: ask meter, colony/warehouse goods, optional goodwill.</summary>
    public static class SettlementBuyUtility
    {
        public const int GoodwillFloor = 10;
        public const int MinSellerSettlements = 4;

        public static bool CanShowBuyGizmo(Settlement settlement, out string disabledReason)
        {
            disabledReason = null;
            var s = WorldDominationMod.settings;
            if (s != null && !s.enableSettlementBuy)
            {
                disabledReason = "TSA_WD_BuySettlement_Disabled".Translate();
                return false;
            }

            if (settlement == null || settlement.Destroyed || settlement.Faction == null || settlement.Faction.IsPlayer)
                return false;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(settlement))
                return false;

            Faction player = Faction.OfPlayerSilentFail;
            FactionRelationKind kind = WorldActions_Utils.SafeRelationKindWith(settlement.Faction, player);
            if (kind != FactionRelationKind.Ally && kind != FactionRelationKind.Neutral)
                return false;

            if (settlement.GetComponent<CompViralSpread>() == null)
                return false;

            if (!SellerHasEnoughSettlements(settlement.Faction))
            {
                disabledReason = "TSA_WD_BuySettlement_TooFewSettlements".Translate(MinSellerSettlements);
                return true;
            }

            if (HasPendingBuyForSettlement(settlement))
            {
                disabledReason = "TSA_WD_BuySettlement_Pending".Translate();
                return true;
            }

            Map colony = Find.AnyPlayerHomeMap;
            if (colony == null)
            {
                disabledReason = "TSA_WD_BuySettlement_NoColony".Translate();
                return true;
            }

            return true;
        }

        public static bool IsEligibleSellerRelation(Faction faction)
        {
            if (faction == null || faction.IsPlayer) return false;
            FactionRelationKind kind = WorldActions_Utils.SafeRelationKindWith(faction, Faction.OfPlayerSilentFail);
            return kind == FactionRelationKind.Ally || kind == FactionRelationKind.Neutral;
        }

        public static int CountFactionSettlements(Faction faction)
        {
            if (faction == null || Find.WorldObjects?.Settlements == null) return 0;
            int count = 0;
            var list = Find.WorldObjects.Settlements;
            for (int i = 0; i < list.Count; i++)
            {
                Settlement s = list[i];
                if (s != null && !s.Destroyed && s.Faction == faction)
                    count++;
            }
            return count;
        }

        public static bool SellerHasEnoughSettlements(Faction faction) =>
            CountFactionSettlements(faction) >= MinSellerSettlements;

        public static float GetAskSilver(SettlementTier tier)
        {
            var s = WorldDominationMod.settings;
            if (s == null)
            {
                switch (tier)
                {
                    case SettlementTier.T2: return WorldDominationSettings.DefSettlementBuyAskT2;
                    case SettlementTier.T3: return WorldDominationSettings.DefSettlementBuyAskT3;
                    case SettlementTier.T4: return WorldDominationSettings.DefSettlementBuyAskT4;
                    default: return WorldDominationSettings.DefSettlementBuyAskT1;
                }
            }
            return s.GetSettlementBuyAskSilver(tier);
        }

        public static float SilverPerGoodwill =>
            WorldDominationMod.settings?.settlementBuySilverPerGoodwill ?? WorldDominationSettings.DefSettlementBuySilverPerGoodwill;

        public static float MaxGoodwillShare =>
            WorldDominationMod.settings?.settlementBuyMaxGoodwillShare ?? WorldDominationSettings.DefSettlementBuyMaxGoodwillShare;

        public static int MaxGoodwillPayable(Faction seller, float askSilver)
        {
            if (seller == null || askSilver <= 0f) return 0;
            float rate = Mathf.Max(1f, SilverPerGoodwill);
            int byShare = Mathf.FloorToInt(askSilver * Mathf.Clamp01(MaxGoodwillShare) / rate);
            int goodwill = GoodwillChangeNotifier.GetPlayerGoodwill(seller);
            int byFloor = Mathf.Max(0, goodwill - GoodwillFloor);
            return Mathf.Max(0, Mathf.Min(byShare, byFloor));
        }

        public static bool HasPendingBuyForSettlement(Settlement settlement)
        {
            if (settlement == null || Find.WorldObjects == null) return false;
            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_Traveler_SettlementBuy buy
                    && !buy.Destroyed
                    && buy.targetObject == settlement)
                    return true;
            }
            return false;
        }

        /// <summary>True when a player home map or at least one warehouse can originate a payment caravan.</summary>
        public static bool HasPlayerPaymentOrigin(int settlementTile, out string noOriginReason)
        {
            noOriginReason = null;
            Map colony = Find.AnyPlayerHomeMap;
            if (colony?.Parent != null)
                return true;
            var warehouses = GetContributingWarehouses(settlementTile);
            if (warehouses != null && warehouses.Count > 0)
                return true;
            noOriginReason = "TSA_WD_BuySettlement_NoOrigin".Translate();
            return false;
        }

        public static bool FactionHasActivePurchase(Faction faction)
        {
            if (faction == null || faction.IsPlayer || Find.WorldObjects == null) return false;
            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is WorldObject_Traveler_SettlementBuy buy) || buy.Destroyed || buy.completed)
                    continue;
                if (buy.sellerFaction == faction)
                    return true;
                if (buy.targetObject is Settlement s && s.Faction == faction)
                    return true;
            }
            return false;
        }

        public static SettlementTier GetCurrentSettlementTier(Settlement settlement)
        {
            return settlement?.GetComponent<CompViralSpread>()?.tier ?? SettlementTier.T1;
        }

        /// <summary>Live checks for an in-flight buy: target, relation, seller size, and locked deal tier.</summary>
        public static bool IsDealStillValid(WorldObject_Traveler_SettlementBuy buy) =>
            IsDealStillValid(buy, out _);

        public static bool IsDealStillValid(WorldObject_Traveler_SettlementBuy buy, out SettlementBuyFailReason failReason)
        {
            failReason = SettlementBuyFailReason.None;
            if (buy == null)
            {
                failReason = SettlementBuyFailReason.SettlementGone;
                return false;
            }
            if (!(buy.targetObject is Settlement settlement) || settlement.Destroyed)
            {
                failReason = SettlementBuyFailReason.SettlementGone;
                return false;
            }
            if (!IsEligibleSellerRelation(settlement.Faction))
            {
                failReason = SettlementBuyFailReason.BadRelation;
                return false;
            }
            if (!SellerHasEnoughSettlements(settlement.Faction))
            {
                failReason = SettlementBuyFailReason.TooFewSettlements;
                return false;
            }
            if (GetCurrentSettlementTier(settlement) != buy.dealTier)
            {
                failReason = SettlementBuyFailReason.TierMismatch;
                return false;
            }
            return true;
        }

        public enum SettlementBuyFailReason
        {
            None,
            LostInTransit,
            SettlementGone,
            BadRelation,
            TooFewSettlements,
            TierMismatch
        }

        public static List<WorldObject_WD_Outpost> GetContributingWarehouses(int settlementTile)
        {
            _ = settlementTile;
            var result = new List<WorldObject_WD_Outpost>();
            if (Find.WorldObjects == null) return result;
            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is WorldObject_WD_Outpost wo) || !Outpost_Warehouse_Delivery.IsWarehouseOutpost(wo)) continue;
                if (CompOutpostWarehouse.Get(wo) == null) continue;
                result.Add(wo);
            }
            return result;
        }

        public static List<ThingDefCountClass> BuildAvailablePool(int settlementTile)
        {
            var pool = new List<ThingDefCountClass>();
            Map colony = Find.AnyPlayerHomeMap;
            if (colony != null)
            {
                var things = colony.listerThings.AllThings;
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (t == null || !t.Spawned || t.Destroyed) continue;
                    // Loose items, or packed MinifiedThing crates (sculptures). Never placed buildings.
                    if (!TryGetColonyPaymentContent(t, out Thing content)) continue;
                    if (content.def == null || !IsBuyPaymentThing(content.def)) continue;
                    int count = content.stackCount > 0 ? content.stackCount : 1;
                    MergeStockRow(pool, MakeStockRowFromThing(content, count));
                }
            }

            var warehouses = GetContributingWarehouses(settlementTile);
            for (int w = 0; w < warehouses.Count; w++)
            {
                var comp = CompOutpostWarehouse.Get(warehouses[w]);
                if (comp?.storedItems == null) continue;
                for (int i = 0; i < comp.storedItems.Count; i++)
                {
                    var e = comp.storedItems[i];
                    if (e?.thingDef == null || e.count <= 0 || !IsBuyPaymentThing(e.thingDef)) continue;
                    MergeStockRow(pool, CloneStockRow(e, e.count));
                }
            }
            return pool;
        }

        public static bool IsBuyPaymentThing(ThingDef def)
        {
            if (def == null) return false;
            if (def.BaseMarketValue <= 0f) return false;
            if (def.IsCorpse || def.race != null) return false;
            if (def.tradeability == Tradeability.None) return false;
            // Never offer abstract minified-crate defs (warehouse also rejects these).
            if (def.thingClass != null && typeof(MinifiedThing).IsAssignableFrom(def.thingClass))
                return false;
            if (def.category == ThingCategory.Item)
                return true;
            // Inner art from a packed crate, or warehouse stock of the same (never a placed building).
            if (def.category == ThingCategory.Building && def.Minifiable)
                return true;
            return false;
        }

        /// <summary>
        /// Colony payment sources only: normal items, or MinifiedThing crates (pay with the inner thing).
        /// Deployed buildings on the map are never payment.
        /// </summary>
        private static bool TryGetColonyPaymentContent(Thing t, out Thing content)
        {
            content = null;
            if (t == null || t.Destroyed) return false;
            if (t is MinifiedThing)
            {
                content = t.GetInnerIfMinified();
                return content != null && !content.Destroyed;
            }
            if (t.def != null && t.def.category == ThingCategory.Item)
            {
                content = t;
                return true;
            }
            return false;
        }

        public static ThingDefCountClass MakeStockRowFromThing(Thing content, int count)
        {
            var row = new ThingDefCountClass(content.def, count)
            {
                stuff = ResolveStuffForPayment(content.def, content.Stuff)
            };
            if (content.TryGetQuality(out QualityCategory q))
                row.quality = q;
            return row;
        }

        public static ThingDefCountClass CloneStockRow(ThingDefCountClass src, int count) =>
            new ThingDefCountClass(src.thingDef, count)
            {
                stuff = src.stuff,
                quality = src.quality
            };

        private static void MergeStockRow(List<ThingDefCountClass> pool, ThingDefCountClass add)
        {
            if (pool == null || add?.thingDef == null || add.count <= 0) return;
            string key = CompOutpostWarehouse.StockKey(add);
            for (int i = 0; i < pool.Count; i++)
            {
                if (CompOutpostWarehouse.StockKey(pool[i]) != key) continue;
                pool[i].count += add.count;
                return;
            }
            pool.Add(CloneStockRow(add, add.count));
        }

        private static ThingDef ResolveStuffForPayment(ThingDef def, ThingDef candidateStuff)
        {
            if (def == null || !def.MadeFromStuff) return null;
            if (candidateStuff != null && candidateStuff.IsStuff && candidateStuff.stuffProps != null
                && candidateStuff.stuffProps.CanMake(def))
                return candidateStuff;
            return null;
        }

        /// <summary>
        /// Unit payment value including stuff and quality, multiplied by vanilla
        /// <see cref="StatDefOf.SellPriceFactor"/> (weapons are typically 0.20, same as trader sell price).
        /// </summary>
        public static float UnitMarketValue(ThingDefCountClass row)
        {
            if (row?.thingDef == null) return 0f;
            Thing probe = MakePaymentProbe(row);
            if (probe == null)
            {
                float baseMv = row.thingDef.BaseMarketValue;
                float abstractFactor = row.thingDef.GetStatValueAbstract(StatDefOf.SellPriceFactor);
                return baseMv * Mathf.Max(0f, abstractFactor);
            }
            float v = probe.GetStatValue(StatDefOf.MarketValue);
            v *= Mathf.Max(0f, probe.GetStatValue(StatDefOf.SellPriceFactor));
            probe.Destroy(DestroyMode.Vanish);
            return v;
        }

        public static float MarketValueOf(ThingDef def, int count)
        {
            if (def == null || count <= 0) return 0f;
            return UnitMarketValue(new ThingDefCountClass(def, 1)) * count;
        }

        public static float MarketValueOf(ThingDefCountClass row)
        {
            if (row?.thingDef == null || row.count <= 0) return 0f;
            return UnitMarketValue(row) * row.count;
        }

        public static float MarketValueOf(IEnumerable<ThingDefCountClass> items)
        {
            float total = 0f;
            if (items == null) return 0f;
            foreach (var tc in items)
            {
                if (tc?.thingDef == null || tc.count <= 0) continue;
                total += MarketValueOf(tc);
            }
            return total;
        }

        private static Thing MakePaymentProbe(ThingDefCountClass row)
            => CreatePaymentProbe(row);

        /// <summary>Probe thing for market value / gift goodwill valuation (caller must Destroy).</summary>
        public static Thing CreatePaymentProbe(ThingDefCountClass row)
        {
            if (row?.thingDef == null) return null;
            ThingDef stuff = null;
            if (row.thingDef.MadeFromStuff)
            {
                stuff = ResolveStuffForPayment(row.thingDef, row.stuff);
                if (stuff == null)
                    stuff = GenStuff.DefaultStuffFor(row.thingDef);
            }
            Thing t;
            try
            {
                t = stuff != null ? ThingMaker.MakeThing(row.thingDef, stuff) : ThingMaker.MakeThing(row.thingDef);
            }
            catch
            {
                return null;
            }
            t.stackCount = 1;
            var cq = t.TryGetComp<CompQuality>();
            if (cq != null)
                cq.SetQuality(row.quality, ArtGenerationContext.Outsider);
            return t;
        }

        public static string FormatStockLabel(ThingDefCountClass entry)
        {
            if (entry?.thingDef == null) return "";
            string label = entry.thingDef.LabelCap;
            if (entry.stuff != null)
            {
                string stuffAdj = entry.stuff.LabelAsStuff;
                if (!string.IsNullOrEmpty(stuffAdj))
                    label = stuffAdj.CapitalizeFirst() + " " + entry.thingDef.LabelCap;
            }
            if (DefHasQualityComp(entry.thingDef))
                label = label + " (" + entry.quality.GetLabel() + ")";
            return label;
        }

        private static bool DefHasQualityComp(ThingDef def)
        {
            if (def?.comps == null) return false;
            for (int i = 0; i < def.comps.Count; i++)
            {
                if (def.comps[i]?.compClass == typeof(CompQuality))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// UI shows silver to 2 decimals. Accept when rounded offer covers rounded ask.
        /// </summary>
        public static float RoundSilver(float silver) => Mathf.Round(silver * 100f) / 100f;

        public static bool MeetsAsk(float offeredSilver, float askSilver) =>
            RoundSilver(offeredSilver) + 0.0001f >= RoundSilver(askSilver);

        public static float RoundedOverspend(float offeredSilver, float askSilver) =>
            Mathf.Max(0f, RoundSilver(offeredSilver) - RoundSilver(askSilver));

        public static float RoundedRemaining(float offeredSilver, float askSilver) =>
            Mathf.Max(0f, RoundSilver(askSilver) - RoundSilver(offeredSilver));

        public static bool DeductPaymentItems(
            int settlementTile,
            List<ThingDefCountClass> items,
            out string reason,
            out HashSet<WorldObject_WD_Outpost> warehousesThatContributed)
        {
            reason = null;
            warehousesThatContributed = new HashSet<WorldObject_WD_Outpost>();
            if (items == null || items.Count == 0) return true;

            Map map = Find.AnyPlayerHomeMap;
            var warehouses = GetContributingWarehouses(settlementTile);

            for (int i = 0; i < items.Count; i++)
            {
                var tc = items[i];
                if (tc?.thingDef == null || tc.count <= 0) continue;
                int toRemove = tc.count;

                if (map != null)
                    toRemove -= DeductFromMapMatching(map, tc, toRemove);

                if (toRemove > 0 && warehouses != null)
                {
                    for (int w = 0; w < warehouses.Count && toRemove > 0; w++)
                    {
                        var comp = CompOutpostWarehouse.Get(warehouses[w]);
                        if (comp == null) continue;
                        int took = comp.WithdrawUpToMatching(tc, toRemove);
                        if (took > 0)
                            warehousesThatContributed.Add(warehouses[w]);
                        toRemove -= took;
                    }
                }

                if (toRemove > 0)
                {
                    reason = "TSA_WD_BuySettlement_DeductFailed".Translate(FormatStockLabel(tc));
                    return false;
                }
            }
            return true;
        }

        private static int DeductFromMapMatching(Map map, ThingDefCountClass match, int amount)
        {
            if (map == null || match?.thingDef == null || amount <= 0) return 0;
            int toRemove = amount;
            var pool = new List<Thing>();
            var things = map.listerThings.AllThings;
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t == null || !t.Spawned || t.Destroyed) continue;
                if (!TryGetColonyPaymentContent(t, out Thing content)) continue;
                if (content.def != match.thingDef) continue;
                if (!PaymentThingMatchesStock(content, match)) continue;
                pool.Add(t);
            }

            for (int i = 0; i < pool.Count && toRemove > 0; i++)
            {
                Thing t = pool[i];
                if (!TryGetColonyPaymentContent(t, out Thing content)) continue;
                int stack = content.stackCount > 0 ? content.stackCount : 1;
                int take = Mathf.Min(toRemove, stack);
                if (take >= stack)
                    t.Destroy(DestroyMode.Vanish);
                else if (ReferenceEquals(t, content))
                    t.SplitOff(take).Destroy(DestroyMode.Vanish);
                else
                {
                    // Minified crate: destroy whole crate when taking its single inner art piece.
                    t.Destroy(DestroyMode.Vanish);
                    take = stack;
                }
                toRemove -= take;
            }
            return amount - toRemove;
        }

        private static bool PaymentThingMatchesStock(Thing content, ThingDefCountClass match)
        {
            if (content == null || match?.thingDef == null) return false;
            if (content.def != match.thingDef) return false;
            ThingDef wantStuff = ResolveStuffForPayment(match.thingDef, match.stuff);
            ThingDef haveStuff = ResolveStuffForPayment(content.def, content.Stuff);
            if (wantStuff != haveStuff) return false;
            bool wantQ = DefHasQualityComp(match.thingDef);
            bool haveQ = content.TryGetQuality(out QualityCategory q);
            if (wantQ != haveQ) return false;
            if (wantQ && q != match.quality) return false;
            return true;
        }

        public static WorldObject ResolveBuyOrigin(
            int settlementTile,
            Map colonyMap,
            List<WorldObject_WD_Outpost> warehouses,
            HashSet<WorldObject_WD_Outpost> warehousesThatContributed)
        {
            _ = warehousesThatContributed;
            if (Find.WorldGrid == null)
                return colonyMap?.Parent;

            WorldObject nearestWh = FindNearestWarehouse(settlementTile, warehouses);
            WorldObject colony = colonyMap?.Parent;
            if (nearestWh == null) return colony;
            if (colony == null) return nearestWh;

            float dWh = Find.WorldGrid.ApproxDistanceInTiles(settlementTile, nearestWh.Tile);
            float dCol = Find.WorldGrid.ApproxDistanceInTiles(settlementTile, colony.Tile);
            return dWh <= dCol ? nearestWh : colony;
        }

        private static WorldObject_WD_Outpost FindNearestWarehouse(int tile, List<WorldObject_WD_Outpost> warehouses)
        {
            if (warehouses == null || warehouses.Count == 0 || Find.WorldGrid == null) return null;
            WorldObject_WD_Outpost nearest = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < warehouses.Count; i++)
            {
                if (warehouses[i] == null) continue;
                float d = Find.WorldGrid.ApproxDistanceInTiles(tile, warehouses[i].Tile);
                if (d < bestDist) { bestDist = d; nearest = warehouses[i]; }
            }
            return nearest;
        }

        public static void RefundItems(WorldObject origin, List<ThingDefCountClass> items)
        {
            if (items == null || items.Count == 0) return;

            if (origin is WorldObject_WD_Outpost wh && Outpost_Warehouse_Delivery.IsWarehouseOutpost(wh))
            {
                var comp = CompOutpostWarehouse.Get(wh);
                if (comp != null)
                {
                    comp.TryDeposit(items);
                    return;
                }
            }

            Map map = (origin as MapParent)?.Map ?? Find.AnyPlayerHomeMap;
            if (map == null) return;
            IntVec3 cell = WorldActions_Traveler.FindColonyDeliveryOrTradeDropCell(map);
            for (int i = 0; i < items.Count; i++)
            {
                var tc = items[i];
                if (tc?.thingDef == null || tc.count <= 0) continue;
                Thing thing = MakePaymentProbe(CloneStockRow(tc, 1));
                if (thing == null)
                {
                    thing = ThingMaker.MakeThing(tc.thingDef);
                }
                thing.stackCount = tc.count;
                // Art buildings go back as minified crates when possible.
                if (thing.def.Minifiable && thing.def.category == ThingCategory.Building)
                {
                    MinifiedThing mini = thing.MakeMinified();
                    GenPlace.TryPlaceThing(mini, cell, map, ThingPlaceMode.Near);
                }
                else
                    GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Near);
            }
        }

        public static void RefundPayment(WorldObject_Traveler_SettlementBuy buy, SettlementBuyFailReason failReason = SettlementBuyFailReason.SettlementGone)
        {
            if (buy == null || buy.paymentRefunded) return;
            buy.paymentRefunded = true;
            bool hadGoods = buy.paymentItems != null && buy.paymentItems.Count > 0;
            bool hadGw = buy.pendingGoodwill > 0;
            RefundItems(buy.originObject, buy.paymentItems);
            buy.paymentItems?.Clear();
            RefundGoodwill(buy);
            NotifyAborted(buy, failReason, refundedPayment: hadGoods || hadGw);
        }

        /// <summary>Caravan destroyed en route: goods and prepaid goodwill stay lost; no refund. Optional looter invests silver budget near the clash tile.</summary>
        public static void MarkPaymentLostInTransit(WorldObject_Traveler_SettlementBuy buy, Faction looter = null)
        {
            if (buy == null || buy.paymentRefunded || buy.completed) return;
            buy.paymentRefunded = true;

            float goodsMv = MarketValueOf(buy.paymentItems);
            int gw = Mathf.Max(0, buy.pendingGoodwill);
            float budget = goodsMv + gw * SilverPerGoodwill;
            int clashTile = buy.Tile;
            buy.paymentItems?.Clear();
            buy.pendingGoodwill = 0;

            if (looter != null && !looter.IsPlayer && budget > 0.01f)
            {
                SettlementCaravanLootUtility.AwardLootToFaction(looter, clashTile, budget, isGiftMission: false);
                return;
            }

            NotifyAborted(buy, SettlementBuyFailReason.LostInTransit, refundedPayment: false);
        }

        private static void RefundGoodwill(WorldObject_Traveler_SettlementBuy buy)
        {
            if (buy == null) return;
            int gw = Mathf.Max(0, buy.pendingGoodwill);
            buy.pendingGoodwill = 0;
            if (gw <= 0) return;
            Faction seller = buy.sellerFaction
                ?? (buy.targetObject as Settlement)?.Faction;
            if (seller == null) return;
            GoodwillChangeNotifier.RefundSettlementBuy(seller, buy.targetObject, gw);
        }

        private static void NotifyAborted(WorldObject_Traveler_SettlementBuy buy, SettlementBuyFailReason failReason, bool refundedPayment)
        {
            if (!(WorldDominationMod.settings?.notifySettlementBuyAborted ?? WorldDominationSettings.DefNotifySettlementBuyAborted))
                return;
            string label = "TSA_WD_BuySettlement_AbortLetterLabel".Translate();
            string target = buy?.targetObject?.LabelCap ?? "?";
            string text = failReason switch
            {
                SettlementBuyFailReason.LostInTransit =>
                    "TSA_WD_BuySettlement_AbortLetterTextLost".Translate(target),
                SettlementBuyFailReason.TierMismatch =>
                    "TSA_WD_BuySettlement_AbortLetterTextTierMismatch".Translate(target),
                SettlementBuyFailReason.BadRelation =>
                    "TSA_WD_BuySettlement_AbortLetterTextBadRelation".Translate(target),
                SettlementBuyFailReason.TooFewSettlements =>
                    "TSA_WD_BuySettlement_AbortLetterTextTooFewSettlements".Translate(target, MinSellerSettlements),
                SettlementBuyFailReason.SettlementGone =>
                    "TSA_WD_BuySettlement_AbortLetterTextSettlementGone".Translate(target),
                _ => refundedPayment
                    ? "TSA_WD_BuySettlement_AbortLetterTextRefund".Translate(target)
                    : "TSA_WD_BuySettlement_AbortLetterText".Translate(target)
            };
            Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.NeutralEvent, buy?.targetObject);
        }

        public static bool TryLaunchBuy(
            Settlement settlement,
            List<ThingDefCountClass> paymentItems,
            int pendingGoodwill,
            out string reason)
        {
            reason = null;
            if (settlement == null || settlement.Destroyed)
            {
                reason = "TSA_WD_BuySettlement_TargetGone".Translate();
                return false;
            }
            if (!IsEligibleSellerRelation(settlement.Faction))
            {
                reason = "TSA_WD_BuySettlement_BadRelation".Translate();
                return false;
            }
            if (!SellerHasEnoughSettlements(settlement.Faction))
            {
                reason = "TSA_WD_BuySettlement_TooFewSettlements".Translate(MinSellerSettlements);
                return false;
            }
            if (HasPendingBuyForSettlement(settlement))
            {
                reason = "TSA_WD_BuySettlement_Pending".Translate();
                return false;
            }

            var viral = settlement.GetComponent<CompViralSpread>();
            float ask = GetAskSilver(viral?.tier ?? SettlementTier.T1);
            float goodsMv = MarketValueOf(paymentItems);
            int gwSpend = Mathf.Max(0, pendingGoodwill);
            float gwMv = gwSpend * SilverPerGoodwill;
            if (!MeetsAsk(goodsMv + gwMv, ask))
            {
                reason = "TSA_WD_BuySettlement_UnderAsk".Translate();
                return false;
            }
            if (gwSpend > 0 && !GoodwillChangeNotifier.CanPayOrderedRoadCost(settlement.Faction, gwSpend, GoodwillFloor))
            {
                reason = "TSA_WD_BuySettlement_GoodwillTooHigh".Translate();
                return false;
            }

            var paymentCopy = new List<ThingDefCountClass>();
            if (paymentItems != null)
            {
                for (int i = 0; i < paymentItems.Count; i++)
                {
                    var tc = paymentItems[i];
                    if (tc?.thingDef == null || tc.count <= 0) continue;
                    paymentCopy.Add(CloneStockRow(tc, tc.count));
                }
            }

            if (!DeductPaymentItems(settlement.Tile, paymentCopy, out reason, out var contributed))
                return false;

            Map colonyMap = Find.AnyPlayerHomeMap;
            var warehouses = GetContributingWarehouses(settlement.Tile);
            WorldObject origin = ResolveBuyOrigin(settlement.Tile, colonyMap, warehouses, contributed);
            if (origin == null)
            {
                reason = "TSA_WD_BuySettlement_NoOrigin".Translate();
                RefundItems(colonyMap?.Parent, paymentCopy);
                return false;
            }

            bool goodwillPaid = false;
            if (gwSpend > 0)
            {
                if (!GoodwillChangeNotifier.TryPaySettlementBuy(settlement.Faction, settlement, gwSpend, out _))
                {
                    reason = "TSA_WD_BuySettlement_GoodwillTooHigh".Translate();
                    RefundItems(origin, paymentCopy);
                    return false;
                }
                goodwillPaid = true;
            }

            if (!WorldActions_Traveler.SpawnSettlementBuyTraveler(settlement, origin, paymentCopy, gwSpend, settlement.Faction))
            {
                reason = "TSA_WD_BuySettlement_SpawnFailed".Translate();
                RefundItems(origin, paymentCopy);
                if (goodwillPaid)
                    GoodwillChangeNotifier.RefundSettlementBuy(settlement.Faction, settlement, gwSpend);
                return false;
            }

            return true;
        }
    }
}
