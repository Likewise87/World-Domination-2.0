using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Trading outpost: silver from nearby NPC settlement tiers at timer expiry. Tier sum uses the world snapshot at payout (same probe as UI), capped at top 3 settlements per faction. Social yield multiplier uses the same time-weighted average cumulative Social as other outposts' delivery capacity (<see cref="WorldObject_WD_Outpost.Tick"/>). T1=100, T2=200, T3=350, T4=500.</summary>
    public static class Outpost_Trading
    {
        /// <summary>Extra silver yield per cumulative Social above the def's MinCumulativeSkill Social requirement (1.0 = 100% tier sum).</summary>
        public const float TradingSocialYieldBonusPerLevel = 0.05f;

        /// <summary>Max NPC settlements per faction that contribute to trading silver / recruiting pools.</summary>
        public const int MaxContributingPartnersPerFaction = 3;

        /// <summary>Gold deliveries per unit of silver-equivalent yield, from vanilla market values (silver 1, gold 10 → 10).</summary>
        public static int GoldPerSilverAmount
        {
            get
            {
                float silverMv = ThingDefOf.Silver != null
                    ? ThingDefOf.Silver.GetStatValueAbstract(StatDefOf.MarketValue)
                    : 1f;
                ThingDef gold = ThingDefOf.Gold ?? DefDatabase<ThingDef>.GetNamedSilentFail("Gold");
                float goldMv = gold != null
                    ? gold.GetStatValueAbstract(StatDefOf.MarketValue)
                    : 10f;
                if (silverMv <= 0f) silverMv = 1f;
                return Mathf.Max(1, Mathf.RoundToInt(goldMv / silverMv));
            }
        }
        private struct TradingRadiusProbeCache
        {
            public int Tile;
            public int Radius;
            public int SilverSum;
            public int SettlementCount;
            public int BuiltTick;
            public int WorldObjectCountSnapshot;
            public int LogicVersion;
            /// <summary>Sorted by distance then label; tooltip lines (name + tier).</summary>
            public List<string> PartnerTooltipLines;
        }

        private struct TradingPartnerSortEntry
        {
            public string Label;
            public int DistanceTiles;
            public SettlementTier Tier;
        }

        /// <summary>Non-hostile NPC settlement within recruiting/trading radius (WD outposts are player-only and never partners).</summary>
        public struct NearbyPartnerInfo
        {
            public Faction Faction;
            public SettlementTier Tier;
            public string Label;
            public int DistanceTiles;
            public WorldObject WorldObject;
            /// <summary>True when this partner is among the top <see cref="MaxContributingPartnersPerFaction"/> for its faction.</summary>
            public bool ContributesToFaction;
        }

        private static readonly List<NearbyPartnerInfo> markScratch = new List<NearbyPartnerInfo>(8);
        private static readonly Dictionary<Faction, List<NearbyPartnerInfo>> partnersByFactionScratch =
            new Dictionary<Faction, List<NearbyPartnerInfo>>();

        /// <summary>Tier weight for recruiting xenotype pool (same ratios as <see cref="SilverForTier"/>).</summary>
        public static float RecruitingTierWeight(SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.T1: return 1f;
                case SettlementTier.T2: return 2f;
                case SettlementTier.T3: return 3.5f;
                case SettlementTier.T4: return 5f;
                default: return 1f;
            }
        }

        /// <summary>Neutral/allied NPC settlements within radius. Player outposts never count; there are no NPC WD outposts.</summary>
        public static void CollectNearbyPartners(WorldObject_WD_Outpost outpost, List<NearbyPartnerInfo> results)
        {
            results?.Clear();
            if (outpost == null || results == null || Find.WorldGrid == null) return;
            int tile = outpost.Tile;
            int radius = GetNearbyRadiusTiles(outpost);
            Faction playerFaction = Faction.OfPlayer;
            var settlements = Find.WorldObjects.Settlements;
            if (settlements == null) return;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement settlement = settlements[i];
                if (settlement == null || settlement.Tile < 0) continue;
                if (settlement.Faction == null || settlement.Faction.IsPlayer || settlement.Faction == playerFaction
                    || WorldActions_Utils.SafeHostileTo(settlement.Faction, playerFaction))
                    continue;
                int dist = (int)Find.WorldGrid.ApproxDistanceInTiles(tile, settlement.Tile);
                if (dist > radius) continue;

                var comp = settlement.GetComponent<CompViralSpread>();
                results.Add(new NearbyPartnerInfo
                {
                    Faction = settlement.Faction,
                    Tier = comp?.tier ?? SettlementTier.T1,
                    Label = settlement.LabelCap,
                    DistanceTiles = dist,
                    WorldObject = settlement,
                    ContributesToFaction = false
                });
            }
        }

        /// <summary>Collect in-radius partners and mark top <see cref="MaxContributingPartnersPerFaction"/> per faction.</summary>
        public static void CollectNearbyPartnersMarked(WorldObject_WD_Outpost outpost, List<NearbyPartnerInfo> results)
        {
            CollectNearbyPartners(outpost, results);
            MarkTopContributorsPerFaction(results, MaxContributingPartnersPerFaction);
        }

        /// <summary>
        /// Marks up to <paramref name="maxPerFaction"/> partners per faction as contributors
        /// (highest tier, then closer distance, then label).
        /// </summary>
        public static void MarkTopContributorsPerFaction(List<NearbyPartnerInfo> partners, int maxPerFaction)
        {
            if (partners == null || partners.Count == 0 || maxPerFaction <= 0) return;

            partnersByFactionScratch.Clear();
            for (int i = 0; i < partners.Count; i++)
            {
                var p = partners[i];
                p.ContributesToFaction = false;
                partners[i] = p;
                if (p.Faction == null) continue;
                if (!partnersByFactionScratch.TryGetValue(p.Faction, out List<NearbyPartnerInfo> list))
                {
                    list = new List<NearbyPartnerInfo>(4);
                    partnersByFactionScratch[p.Faction] = list;
                }
                list.Add(p);
            }

            foreach (var kv in partnersByFactionScratch)
            {
                markScratch.Clear();
                markScratch.AddRange(kv.Value);
                markScratch.Sort(ComparePartnerContributionRank);
                int take = Mathf.Min(maxPerFaction, markScratch.Count);
                for (int t = 0; t < take; t++)
                {
                    WorldObject wo = markScratch[t].WorldObject;
                    for (int i = 0; i < partners.Count; i++)
                    {
                        if (partners[i].WorldObject != wo) continue;
                        var p = partners[i];
                        p.ContributesToFaction = true;
                        partners[i] = p;
                        break;
                    }
                }
            }
        }

        /// <summary>Higher tier first, then closer, then label (stable pick for top-N).</summary>
        private static int ComparePartnerContributionRank(NearbyPartnerInfo a, NearbyPartnerInfo b)
        {
            int c = b.Tier.CompareTo(a.Tier);
            if (c != 0) return c;
            c = a.DistanceTiles.CompareTo(b.DistanceTiles);
            if (c != 0) return c;
            return string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Short tier label for settlement rows (T1 … T4).</summary>
        public static string FormatTierShortLabel(SettlementTier tier) => "T" + ((int)tier + 1);

        /// <summary>Player-facing tier label (Tier 1 … Tier 4).</summary>
        public static string FormatTierLabel(SettlementTier tier)
        {
            string key = tier switch
            {
                SettlementTier.T1 => "TSA_WD_Tier1",
                SettlementTier.T2 => "TSA_WD_Tier2",
                SettlementTier.T3 => "TSA_WD_Tier3",
                SettlementTier.T4 => "TSA_WD_Tier4",
                _ => "TSA_WD_Tier1"
            };
            return OutpostTranslationUtil.Key(key);
        }

        /// <summary>Marked contributors first, then tier, distance, label (dialog settlement list).</summary>
        public static void CollectSortedNearbyPartners(WorldObject_WD_Outpost outpost, List<NearbyPartnerInfo> results)
        {
            results?.Clear();
            if (outpost == null || results == null) return;
            CollectNearbyPartnersMarked(outpost, results);
            results.Sort((a, b) =>
            {
                int c = b.ContributesToFaction.CompareTo(a.ContributesToFaction);
                if (c != 0) return c;
                c = b.Tier.CompareTo(a.Tier);
                if (c != 0) return c;
                c = a.DistanceTiles.CompareTo(b.DistanceTiles);
                return c != 0 ? c : string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
            });
        }

        /// <summary>One-line partner label: settlement name, tier, and tier silver.</summary>
        public static string FormatPartnerRowLabel(NearbyPartnerInfo partner)
        {
            int silver = SilverForTier(partner.Tier);
            return OutpostTranslationUtil.Key(
                "TSA_WD_Trading_PartnerRow",
                partner.Label,
                FormatTierShortLabel(partner.Tier),
                silver.ToString());
        }

        /// <summary>Tooltip for one nearby partner (no combined totals; those are in the dialog footer).</summary>
        public static string BuildPartnerRowTooltip(NearbyPartnerInfo partner)
        {
            int silver = SilverForTier(partner.Tier);
            return OutpostTranslationUtil.Key(
                "TSA_WD_Trading_PartnerRowTip",
                partner.Label,
                FormatTierLabel(partner.Tier),
                partner.Faction?.Name ?? partner.Faction?.def?.LabelCap ?? "?",
                partner.DistanceTiles.ToString(),
                silver.ToString());
        }

        /// <summary>Tier silver values per settlement tier (footer rule tooltip).</summary>
        public static string GetTierSilverDetailText() => OutpostTranslationUtil.Key("TSA_WD_Trading_TierSilverTip");

        /// <summary>Footer rule line below the settlement list in the trading dialog.</summary>
        public static string GetFooterRuleText() => OutpostTranslationUtil.Key("TSA_WD_Trading_FooterRule");

        /// <summary>Footer total line: combined tier silver from contributing settlements.</summary>
        public static string GetFooterTierSumLine(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            return OutpostTranslationUtil.Key("TSA_WD_Trading_FooterTierSum", GetSilverFromNearbyTiers(outpost).ToString());
        }

        /// <summary>Footer result line: expected delivery for the selected commodity after Social multiplier this cycle.</summary>
        public static string GetFooterExpectedLine(WorldObject_WD_Outpost outpost, float avgSocial)
        {
            if (outpost == null) return "";
            ThingDef product = outpost.GetProducingDefForCurrentCycle() ?? outpost.SelectedProductionDef ?? ThingDefOf.Silver;
            int expected = ComputeTradingAmountForOutpost(outpost, avgSocial, product);
            return OutpostTranslationUtil.Key("TSA_WD_Trading_FooterExpected", expected.ToString(), product?.LabelCap ?? "silver");
        }

        /// <summary>Footer result line: expected silver after Social multiplier this cycle.</summary>
        public static string GetFooterExpectedSilverLine(WorldObject_WD_Outpost outpost, float avgSocial)
        {
            return GetFooterExpectedLine(outpost, avgSocial);
        }

        private static readonly Dictionary<int, TradingRadiusProbeCache> tradingRadiusProbeByOutpostId = new Dictionary<int, TradingRadiusProbeCache>();
        private const int TradingRadiusProbeTTLTicks = 2500;
        /// <summary>Bump when silver/partner contribution rules change so stale probes cannot linger.</summary>
        private const int TradingRadiusProbeLogicVersion = 3;

        /// <summary>Clears cached silver/radius probe (e.g. after load or if you detect stale state).</summary>
        public static void InvalidateTradingRadiusProbeCache(WorldObject_WD_Outpost outpost)
        {
            if (outpost != null)
                tradingRadiusProbeByOutpostId.Remove(outpost.ID);
        }

        /// <summary>Clear all trading-radius probes (call after world load).</summary>
        public static void ClearAllTradingRadiusProbeCaches()
        {
            tradingRadiusProbeByOutpostId.Clear();
        }

        private static void GetTradingRadiusProbe(WorldObject_WD_Outpost outpost, out int silver, out int settlementCount)
        {
            GetTradingRadiusProbe(outpost, out silver, out settlementCount, out _);
        }

        private static void GetTradingRadiusProbe(WorldObject_WD_Outpost outpost, out int silver, out int settlementCount, out List<string> partnerTooltipLines)
        {
            silver = 0;
            settlementCount = 0;
            partnerTooltipLines = null;
            if (outpost == null || Find.WorldGrid == null) return;
            int tile = outpost.Tile;
            int radius = GetNearbyRadiusTiles(outpost);
            int woc = Find.WorldObjects.AllWorldObjects.Count;
            int tick = Find.TickManager.TicksGame;
            if (tradingRadiusProbeByOutpostId.TryGetValue(outpost.ID, out TradingRadiusProbeCache e)
                && e.Tile == tile && e.Radius == radius && e.WorldObjectCountSnapshot == woc
                && e.LogicVersion == TradingRadiusProbeLogicVersion
                && tick - e.BuiltTick < TradingRadiusProbeTTLTicks)
            {
                silver = e.SilverSum;
                settlementCount = e.SettlementCount;
                partnerTooltipLines = e.PartnerTooltipLines;
                return;
            }

            int totalSilver = 0;
            var sortBuffer = new List<TradingPartnerSortEntry>();
            var partners = new List<NearbyPartnerInfo>();
            CollectNearbyPartnersMarked(outpost, partners);
            for (int i = 0; i < partners.Count; i++)
            {
                var p = partners[i];
                if (p.ContributesToFaction)
                    totalSilver += SilverForTier(p.Tier);
                sortBuffer.Add(new TradingPartnerSortEntry
                {
                    Label = p.Label,
                    DistanceTiles = p.DistanceTiles,
                    Tier = p.Tier
                });
            }

            sortBuffer.Sort((a, b) =>
            {
                int c = a.DistanceTiles.CompareTo(b.DistanceTiles);
                return c != 0 ? c : string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
            });
            var lines = new List<string>(sortBuffer.Count);
            string lineKey = "TSA_WD_Biome_Tooltip_TradingNearbyPartnerLine";
            foreach (var p in sortBuffer)
            {
                lines.Add(OutpostTranslationUtil.Key(lineKey, p.Label, p.Tier.ToString()));
            }

            silver = totalSilver;
            settlementCount = partners.Count;
            partnerTooltipLines = lines;
            tradingRadiusProbeByOutpostId[outpost.ID] = new TradingRadiusProbeCache
            {
                Tile = tile,
                Radius = radius,
                SilverSum = silver,
                SettlementCount = settlementCount,
                BuiltTick = tick,
                WorldObjectCountSnapshot = woc,
                LogicVersion = TradingRadiusProbeLogicVersion,
                PartnerTooltipLines = lines
            };
        }

        /// <summary>Extra tooltip block: header then one line per NPC settlement in trading radius (sorted by distance).</summary>
        public static string GetNearbyTradingPartnersTooltipAppendix(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            GetTradingRadiusProbe(outpost, out _, out _, out List<string> partnerLines);
            string header = OutpostTranslationUtil.Key("TSA_WD_Biome_Tooltip_TradingNearbyPartnersHeader");
            if (partnerLines == null || partnerLines.Count == 0)
            {
                string none = OutpostTranslationUtil.Key("TSA_WD_Biome_Tooltip_TradingNearbyPartnersNone");
                return header + "\n" + none;
            }
            return header + "\n" + string.Join("\n", partnerLines);
        }

        /// <summary>Silver per tier at cycle end (snapshot).</summary>
        public static int SilverForTier(SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.T1: return 100;
                case SettlementTier.T2: return 200;
                case SettlementTier.T3: return 350;
                case SettlementTier.T4: return 500;
                default: return 100;
            }
        }

        /// <summary>Radius (tiles) for nearby NPC settlements; from def extension minNearbyRadiusTiles, default 12.</summary>
        public static int GetNearbyRadiusTiles(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.def == null) return 12;
            var ext = outpost.def.GetModExtension<OutpostDefExtension>();
            return ext != null && ext.minNearbyRadiusTiles > 0 ? ext.minNearbyRadiusTiles : 12;
        }

        /// <summary>Sum silver from contributing partners (top <see cref="MaxContributingPartnersPerFaction"/> per faction).</summary>
        public static int GetSilverFromNearbyTiers(WorldObject_WD_Outpost outpost)
        {
            GetTradingRadiusProbe(outpost, out int silver, out _);
            return silver;
        }

        /// <summary>Max Social required in <c>MinCumulativeSkill</c> across all requirement sets (AND-combined); 0 if Social is not listed.</summary>
        public static int GetMinSocialBaselineFromDef(WorldObjectDef def)
        {
            var ext = def?.GetModExtension<OutpostDefExtension>();
            if (ext?.MinCumulativeSkill == null) return 0;
            int maxSocial = 0;
            foreach (var set in ext.MinCumulativeSkill)
            {
                if (set == null) continue;
                foreach (var kv in set.GetRequirements())
                {
                    if (kv.Key == SkillDefOf.Social && kv.Value > maxSocial)
                        maxSocial = kv.Value;
                }
            }
            return maxSocial;
        }

        /// <summary>1.0 at baseline Social from def; +<see cref="TradingSocialYieldBonusPerLevel"/> per cumulative Social above that (no penalty below).</summary>
        public static float GetTradingSocialYieldMultiplier(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 1f;
            int total = Outpost_EstablishmentRequirements.GetCumulativeOutpostSkillForSkill(outpost, SkillDefOf.Social);
            return GetTradingSocialYieldMultiplier(outpost.def, total);
        }

        /// <summary>Same as <see cref="GetTradingSocialYieldMultiplier(WorldObject_WD_Outpost)"/> but with an explicit total Social (e.g. time-weighted average over the cycle).</summary>
        public static float GetTradingSocialYieldMultiplier(WorldObjectDef def, int totalCumulativeSocial)
        {
            if (def == null) return 1f;
            int baseline = GetMinSocialBaselineFromDef(def);
            if (baseline <= 0) return 1f;
            int excess = Mathf.Max(0, totalCumulativeSocial - baseline);
            return 1f + TradingSocialYieldBonusPerLevel * excess;
        }

        /// <summary>Silver and gold commodity options for the trading dialog.</summary>
        public static List<ThingDef> GetTradingCommodityOptions()
        {
            var list = new List<ThingDef>(2);
            if (ThingDefOf.Silver != null) list.Add(ThingDefOf.Silver);
            ThingDef gold = ThingDefOf.Gold ?? DefDatabase<ThingDef>.GetNamedSilentFail("Gold");
            if (gold != null && !list.Contains(gold)) list.Add(gold);
            return list;
        }

        public static bool IsGoldTradingProduct(ThingDef def)
        {
            if (def == null) return false;
            if (ThingDefOf.Gold != null && def == ThingDefOf.Gold) return true;
            return def.defName == "Gold";
        }

        /// <summary>Delivery amount for the chosen commodity. Silver uses the full tier×Social yield; gold is 1/{GoldPerSilverAmount} of that.</summary>
        public static int ComputeTradingAmountForOutpost(WorldObject_WD_Outpost outpost, float? averageCumulativeSocialThisCycle = null, ThingDef product = null)
        {
            int silverEquivalent = ComputeTradingSilverForOutpost(outpost, averageCumulativeSocialThisCycle);
            product ??= outpost?.GetProducingDefForCurrentCycle() ?? outpost?.SelectedProductionDef ?? ThingDefOf.Silver;
            if (IsGoldTradingProduct(product))
                return Mathf.Max(0, Mathf.RoundToInt(silverEquivalent / (float)GoldPerSilverAmount));
            return silverEquivalent;
        }

        public static string FormatTradingAmountLine(WorldObject_WD_Outpost outpost, float social, ThingDef product)
        {
            if (product == null) return "";
            int amount = ComputeTradingAmountForOutpost(outpost, social, product);
            return OutpostTranslationUtil.Key("TSA_WD_Trading_Info_ExpectedAmount", amount.ToString(), product.LabelCap);
        }

        /// <summary>Tier silver × Social yield (rounded) × global output multiplier. Pass <paramref name="averageCumulativeSocialThisCycle"/> for payout parity with recruiting (time-weighted Social); null uses current outpost Social.</summary>
        public static int ComputeTradingSilverForOutpost(WorldObject_WD_Outpost outpost, float? averageCumulativeSocialThisCycle = null)
        {
            if (outpost == null) return 0;
            int tierSum = GetSilverFromNearbyTiers(outpost);
            int socialForMult = averageCumulativeSocialThisCycle.HasValue
                ? Mathf.RoundToInt(averageCumulativeSocialThisCycle.Value)
                : Outpost_EstablishmentRequirements.GetCumulativeOutpostSkillForSkill(outpost, SkillDefOf.Social);
            float skillMult = GetTradingSocialYieldMultiplier(outpost.def, socialForMult);
            int afterSkill = Mathf.Max(0, Mathf.RoundToInt(tierSum * skillMult));
            return Outpost_Production_Utils.ScaleOutputStackCount(afterSkill);
        }

        /// <summary>When timer expires: silver from nearby tiers × Social multiplier (using time-weighted average Social for the cycle). True if a delivery was launched.</summary>
        public static bool Produce(WorldObject_WD_Outpost outpost, float averageCumulativeSocialThisCycle)
        {
            if (outpost == null) return false;
            float minStrength = WorldDominationMod.settings?.outpostDeliveryMinStrength ?? 100f;
            var comp = outpost.GetComponent<CompViralSpread>();
            if (comp != null && comp.strength < minStrength) return false;

            ThingDef product = outpost.GetProducingDefForCurrentCycle() ?? outpost.SelectedProductionDef ?? ThingDefOf.Silver;
            if (product == null) return false;

            int amount = ComputeTradingAmountForOutpost(outpost, averageCumulativeSocialThisCycle, product);
            if (amount <= 0) return false;

            var items = new List<ThingDefCountClass> { new ThingDefCountClass(product, amount) };
            WorldActions_Traveler.SpawnOutpostDeliveryTraveler(outpost, items);
            return true;
        }

        /// <summary>Uses the outpost's current cumulative Social for the multiplier.</summary>
        public static string GetDetailedMathTooltip(WorldObject_WD_Outpost outpost)
        {
            int social = outpost == null ? 0 : Outpost_EstablishmentRequirements.GetCumulativeOutpostSkillForSkill(outpost, SkillDefOf.Social);
            return GetDetailedMathTooltip(outpost, social);
        }

        /// <summary>Crisp, line-broken breakdown: per-partner silver, sum, Social multiplier, and expected outcome for the given Social value.</summary>
        public static string GetDetailedMathTooltip(WorldObject_WD_Outpost outpost, float socialForMultiplier)
        {
            if (outpost == null) return "";
            var partners = new List<NearbyPartnerInfo>();
            CollectNearbyPartnersMarked(outpost, partners);
            partners.Sort((a, b) =>
            {
                int c = b.ContributesToFaction.CompareTo(a.ContributesToFaction);
                if (c != 0) return c;
                c = a.DistanceTiles.CompareTo(b.DistanceTiles);
                return c != 0 ? c : string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
            });

            var lines = new List<string>();
            lines.Add(OutpostTranslationUtil.Key("TSA_WD_Trading_Math_Header"));

            int sum = 0;
            int contributingCount = 0;
            foreach (var p in partners)
            {
                if (!p.ContributesToFaction) continue;
                contributingCount++;
                int s = SilverForTier(p.Tier);
                sum += s;
                lines.Add(OutpostTranslationUtil.Key(
                    "TSA_WD_Trading_Math_PartnerLine",
                    p.Label,
                    FormatTierShortLabel(p.Tier),
                    s.ToString()));
            }
            if (contributingCount == 0)
                lines.Add(OutpostTranslationUtil.Key("TSA_WD_Trading_Math_None"));
            lines.Add(OutpostTranslationUtil.Key("TSA_WD_Trading_Math_Sum", sum.ToString()));
            lines.Add("");

            int socialInt = Mathf.RoundToInt(socialForMultiplier);
            int minSocial = GetMinSocialBaselineFromDef(outpost.def);
            float mult = GetTradingSocialYieldMultiplier(outpost.def, socialInt);
            int multPct = Mathf.RoundToInt(mult * 100f);
            int perPointPct = Mathf.RoundToInt(TradingSocialYieldBonusPerLevel * 100f);

            lines.Add(OutpostTranslationUtil.Key("TSA_WD_Trading_Math_MinSocialPoints", minSocial.ToString()));
            lines.Add(OutpostTranslationUtil.Key("TSA_WD_Trading_Math_MultAtMin", "100"));
            lines.Add(OutpostTranslationUtil.Key("TSA_WD_Trading_Math_BonusPerPoint", perPointPct.ToString()));
            lines.Add(OutpostTranslationUtil.Key("TSA_WD_Trading_Math_SocialAtOutpost", socialInt.ToString()));
            lines.Add(OutpostTranslationUtil.Key("TSA_WD_Trading_Math_ResultingMult", multPct.ToString()));
            lines.Add("");

            ThingDef product = outpost.GetProducingDefForCurrentCycle() ?? outpost.SelectedProductionDef ?? ThingDefOf.Silver;
            int silverEquivalent = ComputeTradingSilverForOutpost(outpost, socialForMultiplier);
            int expected = ComputeTradingAmountForOutpost(outpost, socialForMultiplier, product);
            string productLabel = product?.LabelCap ?? (ThingDefOf.Silver?.LabelCap ?? "silver");
            lines.Add(OutpostTranslationUtil.Key("TSA_WD_Trading_Math_ExpectedHeader"));
            lines.Add(OutpostTranslationUtil.Key("TSA_WD_Trading_Math_ExpectedLine", sum.ToString(), multPct.ToString(), silverEquivalent.ToString()));
            if (IsGoldTradingProduct(product))
                lines.Add(OutpostTranslationUtil.Key("TSA_WD_Trading_Math_GoldConversion", silverEquivalent.ToString(), GoldPerSilverAmount.ToString(), expected.ToString(), productLabel));
            else
                lines.Add(OutpostTranslationUtil.Key("TSA_WD_Trading_Math_ExpectedCommodity", expected.ToString(), productLabel));

            return string.Join("\n", lines.ToArray());
        }

        /// <summary>Stats-tab tooltip for Social yield bonus (baseline, excess skill, per-point rate).</summary>
        public static string GetSocialYieldStatsTooltip(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            int social = Outpost_EstablishmentRequirements.GetCumulativeOutpostSkillForSkill(outpost, SkillDefOf.Social);
            int baseline = GetMinSocialBaselineFromDef(outpost.def);
            int excess = Mathf.Max(0, social - baseline);
            int perLevelPct = Mathf.RoundToInt(TradingSocialYieldBonusPerLevel * 100f);
            int relativePct = Mathf.RoundToInt((GetTradingSocialYieldMultiplier(outpost) - 1f) * 100f);
            string key = "TSA_WD_OutpostStats_Row_TradingSocialMultTip";
            string tip = key.Translate(baseline, perLevelPct, social, excess, relativePct).ToString();
            if (tip == key || tip.Contains("TSA_WD_OutpostStats_Row_TradingSocialMultTip"))
            {
                tip = "100% yield at baseline Social (" + baseline + "). Each Social above baseline adds +"
                    + perLevelPct + "% output (current Social: " + social + ", +" + excess
                    + " above baseline → +" + relativePct + "% total bonus).";
            }
            return tip;
        }

        /// <summary>Tooltip for trading: formula line (tier × Social yield × global mult), plus radius explanation.</summary>
        public static string GetProductionTooltip(WorldObject_WD_Outpost outpost)
        {
            int tierSum = GetSilverFromNearbyTiers(outpost);
            int silver = ComputeTradingSilverForOutpost(outpost);
            int skillPct = Mathf.RoundToInt(GetTradingSocialYieldMultiplier(outpost) * 100f);
            int totalSocial = Outpost_EstablishmentRequirements.GetCumulativeOutpostSkillForSkill(outpost, SkillDefOf.Social);
            int baselineSocial = GetMinSocialBaselineFromDef(outpost?.def);
            string formula = OutpostTranslationUtil.Key(
                "TSA_WD_Production_TooltipTrading_Formula",
                silver.ToString(),
                tierSum.ToString(),
                skillPct.ToString(),
                totalSocial.ToString(),
                baselineSocial.ToString());
            int radius = GetNearbyRadiusTiles(outpost);
            string detail = OutpostTranslationUtil.Key("TSA_WD_Production_TooltipTrading", radius.ToString());
            return formula + "\n\n" + detail;
        }

        /// <summary>Count of neutral/allied NPC settlements within trading radius.</summary>
        public static int GetNearbySettlementCount(WorldObject_WD_Outpost outpost)
        {
            GetTradingRadiusProbe(outpost, out _, out int count);
            return count;
        }

        /// <summary>Same delivery wording as other outposts: "237 silver" via <see cref="Outpost_Production_Utils.FormatDeliveryProductLine"/>.</summary>
        public static string GetTradingDeliveryProductLine(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            ThingDef product = outpost.GetProducingDefForCurrentCycle() ?? outpost.SelectedProductionDef ?? ThingDefOf.Silver;
            if (product == null) return "";
            int amount = ComputeTradingAmountForOutpost(outpost, null, product);
            var list = new List<ThingDefCountClass> { new ThingDefCountClass(product, amount) };
            return Outpost_Production_Utils.FormatDeliveryProductLine(list) ?? "";
        }

        /// <summary>Dashboard-style line; same yield text as inspect (<see cref="GetTradingDeliveryProductLine"/>).</summary>
        public static string GetProductionSummaryLine(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            float cycleDays = Outpost_Production_Utils.GetProductionCycleDays(outpost);
            string cycleStr = cycleDays.ToString("F0");
            string yieldStr = GetTradingDeliveryProductLine(outpost);
            if (string.IsNullOrEmpty(yieldStr))
                yieldStr = OutpostTranslationUtil.Key("TSA_WD_Trading_SummaryZeroSilver", ThingDefOf.Silver?.LabelCap ?? "silver");
            return OutpostTranslationUtil.Key("TSA_WD_Prod_TradingSummary", yieldStr, cycleStr);
        }
    }
}
