using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Embassy outpost: goodwill from in-range NPC settlements (highest tier per faction),
    /// scaled by cycle average effective Social. Includes Neutral/Ally; optionally temporary hostiles.
    /// Permanent enemies are never eligible. No item delivery.
    /// </summary>
    public static class Outpost_Embassy
    {
        private static readonly float[] SocialKnots = { 10f, 20f, 30f, 40f, 60f, 80f };
        private static readonly float[] SocialMults = { 0.50f, 1.00f, 1.25f, 1.50f, 1.75f, 2.00f };

        private struct EmbassyRadiusProbeCache
        {
            public int Tile;
            public int Radius;
            public int SettlementCount;
            public int BuiltTick;
            public int WorldObjectCountSnapshot;
            public bool HostilesAllowed;
            public List<NearbySettlementInfo> Partners;
            public List<FactionGoodwillPreview> FactionPreviews;
        }

        /// <summary>NPC settlement in embassy radius eligible for goodwill (see <see cref="IsEligiblePartnerFaction"/>).</summary>
        public struct NearbySettlementInfo
        {
            public Faction Faction;
            public SettlementTier Tier;
            public string Label;
            public int DistanceTiles;
            public Settlement Settlement;
            public bool ContributesToFaction;
            public float BasePoints;
        }

        /// <summary>Per-faction highest-tier contribution after Social mult (before goodwill room clamp).</summary>
        public struct FactionGoodwillPreview
        {
            public Faction Faction;
            public SettlementTier HighestTier;
            public float BasePoints;
            public int AwardBeforeClamp;
            public int AwardClamped;
            public int GoodwillRoom;
            public string SettlementLabel;
        }

        private static readonly Dictionary<int, EmbassyRadiusProbeCache> probeByOutpostId = new Dictionary<int, EmbassyRadiusProbeCache>();
        private const int ProbeTTLTicks = 2500;
        private static readonly List<NearbySettlementInfo> collectScratch = new List<NearbySettlementInfo>();
        private static readonly Dictionary<Faction, NearbySettlementInfo> bestByFactionScratch = new Dictionary<Faction, NearbySettlementInfo>();

        /// <summary>Eligible for embassy goodwill: not player, not permanent enemy; hostiles only if settings allow.</summary>
        public static bool IsEligiblePartnerFaction(Faction faction)
        {
            if (faction == null || faction.IsPlayer) return false;
            if (WorldActions_Utils.IsPermanentEnemyOfPlayer(faction)) return false;
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null) return false;
            if (!WorldActions_Utils.SafeHostileTo(faction, player)) return true;
            return WorldDominationMod.settings == null
                || WorldDominationMod.settings.embassyMayGainGoodwillWithHostiles;
        }

        public static float PointsForTier(SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.T1: return 1.5f;
                case SettlementTier.T2: return 3f;
                case SettlementTier.T3: return 5f;
                case SettlementTier.T4: return 8f;
                default: return 1.5f;
            }
        }

        public static int GetNearbyRadiusTiles(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.def == null) return 12;
            var ext = outpost.def.GetModExtension<OutpostDefExtension>();
            return ext != null && ext.minNearbyRadiusTiles > 0 ? ext.minNearbyRadiusTiles : 12;
        }

        /// <summary>Piecewise-linear Social multiplier; floored at 0.50 below Social 10.</summary>
        public static float GetSocialMultiplier(float effectiveCumSocial)
        {
            float s = Mathf.Max(0f, effectiveCumSocial);
            if (s <= SocialKnots[0])
                return SocialMults[0];
            for (int i = 1; i < SocialKnots.Length; i++)
            {
                if (s <= SocialKnots[i])
                {
                    float t = (s - SocialKnots[i - 1]) / (SocialKnots[i] - SocialKnots[i - 1]);
                    return Mathf.Lerp(SocialMults[i - 1], SocialMults[i], t);
                }
            }
            return SocialMults[SocialMults.Length - 1];
        }

        public static float GetDeliveryDrivingCapacity(WorldObject_WD_Outpost outpost) =>
            Outpost_Recruiting.GetDeliveryDrivingCapacity(outpost);

        public static float GetDeliveryDrivingCapacityRaw(WorldObject_WD_Outpost outpost) =>
            Outpost_Recruiting.GetDeliveryDrivingCapacityRaw(outpost);

        public static void InvalidateProbeCache(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return;
            probeByOutpostId.Remove(outpost.ID);
        }

        public static void ClearAllProbeCaches() => probeByOutpostId.Clear();

        /// <summary>Eligible NPC settlements within radius (no outposts). Permanent enemies excluded; hostiles depend on settings.</summary>
        public static void CollectNearbySettlements(WorldObject_WD_Outpost outpost, List<NearbySettlementInfo> results)
        {
            results?.Clear();
            if (outpost == null || results == null || Find.WorldGrid == null || Find.WorldObjects == null) return;
            int tile = outpost.Tile;
            int radius = GetNearbyRadiusTiles(outpost);
            Faction playerFaction = Faction.OfPlayer;
            var settlements = Find.WorldObjects.Settlements;
            if (settlements == null) return;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement settlement = settlements[i];
                if (settlement == null || settlement.Tile < 0) continue;
                if (settlement.Faction == null || settlement.Faction == playerFaction) continue;
                if (!IsEligiblePartnerFaction(settlement.Faction)) continue;
                int dist = (int)Find.WorldGrid.ApproxDistanceInTiles(tile, settlement.Tile);
                if (dist > radius) continue;
                var comp = settlement.GetComponent<CompViralSpread>();
                SettlementTier tier = comp?.tier ?? SettlementTier.T1;
                results.Add(new NearbySettlementInfo
                {
                    Faction = settlement.Faction,
                    Tier = tier,
                    Label = settlement.LabelCap,
                    DistanceTiles = dist,
                    Settlement = settlement,
                    ContributesToFaction = false,
                    BasePoints = PointsForTier(tier)
                });
            }
        }

        public static void CollectSortedNearbySettlements(WorldObject_WD_Outpost outpost, List<NearbySettlementInfo> results)
        {
            results?.Clear();
            if (outpost == null || results == null) return;
            CollectNearbySettlements(outpost, results);
            MarkHighestTierContributors(results);
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

        private static void MarkHighestTierContributors(List<NearbySettlementInfo> settlements)
        {
            bestByFactionScratch.Clear();
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s.Faction == null) continue;
                if (!bestByFactionScratch.TryGetValue(s.Faction, out var best)
                    || s.Tier > best.Tier
                    || (s.Tier == best.Tier && s.DistanceTiles < best.DistanceTiles))
                {
                    bestByFactionScratch[s.Faction] = s;
                }
            }

            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                bool contributes = s.Faction != null
                    && bestByFactionScratch.TryGetValue(s.Faction, out var best)
                    && best.Settlement == s.Settlement;
                s.ContributesToFaction = contributes;
                settlements[i] = s;
            }
        }

        private static void GetOrBuildProbe(WorldObject_WD_Outpost outpost, out EmbassyRadiusProbeCache cache)
        {
            cache = default;
            if (outpost == null) return;
            int tick = Find.TickManager.TicksGame;
            int worldCount = Find.WorldObjects?.AllWorldObjects?.Count ?? 0;
            int radius = GetNearbyRadiusTiles(outpost);
            bool hostilesAllowed = WorldDominationMod.settings == null
                || WorldDominationMod.settings.embassyMayGainGoodwillWithHostiles;
            if (probeByOutpostId.TryGetValue(outpost.ID, out cache)
                && cache.Tile == outpost.Tile
                && cache.Radius == radius
                && cache.WorldObjectCountSnapshot == worldCount
                && cache.HostilesAllowed == hostilesAllowed
                && tick - cache.BuiltTick < ProbeTTLTicks
                && cache.Partners != null)
            {
                return;
            }

            var partners = new List<NearbySettlementInfo>();
            CollectSortedNearbySettlements(outpost, partners);
            var previews = BuildFactionPreviews(partners, GetSocialMultiplier(outpost.GetCapacityForYieldPreview()));
            cache = new EmbassyRadiusProbeCache
            {
                Tile = outpost.Tile,
                Radius = radius,
                SettlementCount = partners.Count,
                BuiltTick = tick,
                WorldObjectCountSnapshot = worldCount,
                HostilesAllowed = hostilesAllowed,
                Partners = partners,
                FactionPreviews = previews
            };
            probeByOutpostId[outpost.ID] = cache;
        }

        public static int GetNearbySettlementCount(WorldObject_WD_Outpost outpost)
        {
            GetOrBuildProbe(outpost, out var cache);
            return cache.Partners?.Count ?? 0;
        }

        public static void CollectFactionPreviews(WorldObject_WD_Outpost outpost, float socialForMult, List<FactionGoodwillPreview> results)
        {
            results?.Clear();
            if (outpost == null || results == null) return;
            CollectNearbySettlements(outpost, collectScratch);
            MarkHighestTierContributors(collectScratch);
            results.AddRange(BuildFactionPreviews(collectScratch, GetSocialMultiplier(socialForMult)));
        }

        private static List<FactionGoodwillPreview> BuildFactionPreviews(List<NearbySettlementInfo> settlements, float socialMult)
        {
            var list = new List<FactionGoodwillPreview>();
            bestByFactionScratch.Clear();
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (!s.ContributesToFaction || s.Faction == null) continue;
                int room = GetGoodwillRoom(s.Faction);
                int beforeClamp = Mathf.Max(0, Mathf.RoundToInt(s.BasePoints * socialMult));
                int clamped = Mathf.Min(beforeClamp, Mathf.Max(0, room));
                list.Add(new FactionGoodwillPreview
                {
                    Faction = s.Faction,
                    HighestTier = s.Tier,
                    BasePoints = s.BasePoints,
                    AwardBeforeClamp = beforeClamp,
                    AwardClamped = clamped,
                    GoodwillRoom = room,
                    SettlementLabel = s.Label
                });
            }
            list.Sort((a, b) => string.Compare(
                a.Faction?.Name ?? "",
                b.Faction?.Name ?? "",
                StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public static int GetGoodwillRoom(Faction faction)
        {
            if (faction == null) return 0;
            int current = GoodwillChangeNotifier.GetPlayerGoodwill(faction);
            int cap = GoodwillCapUtility.MaxGoodwillCap();
            return Mathf.Max(0, cap - current);
        }

        public static int ComputeExpectedGoodwillTotal(WorldObject_WD_Outpost outpost, float averageEffectiveSocial)
        {
            var previews = new List<FactionGoodwillPreview>();
            CollectFactionPreviews(outpost, averageEffectiveSocial, previews);
            int sum = 0;
            for (int i = 0; i < previews.Count; i++)
                sum += previews[i].AwardClamped;
            return sum;
        }

        public static string FormatPartnerRowLabel(NearbySettlementInfo partner)
        {
            if (partner.ContributesToFaction)
            {
                return OutpostTranslationUtil.Key(
                    "TSA_WD_Embassy_PartnerRow",
                    partner.Label,
                    Outpost_Trading.FormatTierShortLabel(partner.Tier),
                    partner.BasePoints.ToString("0.#"));
            }
            return OutpostTranslationUtil.Key(
                "TSA_WD_Embassy_PartnerRowCovered",
                partner.Label,
                Outpost_Trading.FormatTierShortLabel(partner.Tier));
        }

        /// <summary>Up to <paramref name="maxFactionLines"/> faction payout lines for the embassy dialog outcome box.</summary>
        public static string FormatDialogExpectedOutput(WorldObject_WD_Outpost outpost, float socialForMultiplier, int maxFactionLines = 3)
        {
            if (outpost == null) return "";
            var previews = new List<FactionGoodwillPreview>();
            CollectFactionPreviews(outpost, socialForMultiplier, previews);
            if (previews.Count == 0)
                return OutpostTranslationUtil.Key("TSA_WD_Embassy_ExpectedGoodwill", "0");

            previews.Sort((a, b) =>
            {
                int c = b.AwardClamped.CompareTo(a.AwardClamped);
                return c != 0 ? c : string.Compare(a.Faction?.Name, b.Faction?.Name, StringComparison.OrdinalIgnoreCase);
            });

            var lines = new List<string>();
            int shown = Mathf.Min(maxFactionLines, previews.Count);
            for (int i = 0; i < shown; i++)
            {
                var p = previews[i];
                lines.Add(OutpostTranslationUtil.Key(
                    "TSA_WD_Embassy_OutputFactionLine",
                    p.Faction?.Name ?? "?",
                    p.AwardClamped.ToString()));
            }
            int remaining = previews.Count - shown;
            if (remaining > 0)
                lines.Add(OutpostTranslationUtil.Key("TSA_WD_Embassy_OutputMoreFactions", remaining.ToString()));
            return string.Join("\n", lines.ToArray());
        }

        public static string BuildPartnerRowTooltip(NearbySettlementInfo partner)
        {
            if (partner.ContributesToFaction)
            {
                return OutpostTranslationUtil.Key(
                    "TSA_WD_Embassy_PartnerRowTip",
                    partner.Label,
                    Outpost_Trading.FormatTierLabel(partner.Tier),
                    partner.Faction?.Name ?? "?",
                    partner.DistanceTiles.ToString(),
                    partner.BasePoints.ToString("0.#"));
            }
            return OutpostTranslationUtil.Key(
                "TSA_WD_Embassy_PartnerRowTipCovered",
                partner.Label,
                Outpost_Trading.FormatTierLabel(partner.Tier),
                partner.Faction?.Name ?? "?",
                partner.DistanceTiles.ToString());
        }

        public static string GetInspectProductLine(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            float avg = outpost.GetCapacityForYieldPreview();
            int total = ComputeExpectedGoodwillTotal(outpost, avg);
            return OutpostTranslationUtil.Key("TSA_WD_Embassy_InspectLine", total.ToString());
        }

        public static string GetProductionSummaryLine(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            float cycleDays = Outpost_Production_Utils.GetProductionCycleDays(outpost);
            float avg = outpost.GetCapacityForYieldPreview();
            int total = ComputeExpectedGoodwillTotal(outpost, avg);
            return OutpostTranslationUtil.Key(
                "TSA_WD_Embassy_SummaryLine",
                total.ToString(),
                cycleDays.ToString("F0"));
        }

        public static string GetProductionTooltip(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            return GetDetailedMathTooltip(outpost, outpost.GetCapacityForYieldPreview());
        }

        public static string GetDetailedMathTooltip(WorldObject_WD_Outpost outpost, float socialForMultiplier)
        {
            if (outpost == null) return "";
            float mult = GetSocialMultiplier(socialForMultiplier);
            int multPct = Mathf.RoundToInt(mult * 100f);
            var lines = new List<string>();
            lines.Add(OutpostTranslationUtil.Key("TSA_WD_Embassy_Math_Header"));
            lines.Add(OutpostTranslationUtil.Key(
                "TSA_WD_Embassy_Math_SocialLine",
                socialForMultiplier.ToString("F0"),
                multPct.ToString()));

            var previews = new List<FactionGoodwillPreview>();
            CollectFactionPreviews(outpost, socialForMultiplier, previews);
            if (previews.Count == 0)
                lines.Add(OutpostTranslationUtil.Key("TSA_WD_Embassy_Math_None"));
            else
            {
                for (int i = 0; i < previews.Count; i++)
                {
                    var p = previews[i];
                    lines.Add(OutpostTranslationUtil.Key(
                        "TSA_WD_Embassy_Math_FactionLine",
                        p.Faction?.Name ?? "?",
                        Outpost_Trading.FormatTierShortLabel(p.HighestTier),
                        p.BasePoints.ToString("0.#"),
                        multPct.ToString(),
                        p.AwardClamped.ToString()));
                }
            }

            int total = 0;
            for (int i = 0; i < previews.Count; i++)
                total += previews[i].AwardClamped;
            lines.Add(OutpostTranslationUtil.Key("TSA_WD_Embassy_Math_Total", total.ToString()));
            return string.Join("\n", lines.ToArray());
        }

        public static string GetSocialMultStatsTooltip(WorldObject_WD_Outpost outpost)
        {
            float raw = GetDeliveryDrivingCapacityRaw(outpost);
            float eff = OutpostSkillScaling.ToEffective(raw);
            float mult = GetSocialMultiplier(eff);
            return OutpostTranslationUtil.Key(
                "TSA_WD_OutpostStats_Row_EmbassySocialMultTip",
                raw.ToString("F0"),
                eff.ToString("F0"),
                Mathf.RoundToInt(mult * 100f).ToString());
        }

        /// <summary>Pay goodwill to each contributing faction. True if any goodwill was awarded.</summary>
        public static bool Produce(WorldObject_WD_Outpost outpost, float averageEffectiveSocialThisCycle)
        {
            if (outpost == null) return false;
            InvalidateProbeCache(outpost);

            float mult = GetSocialMultiplier(averageEffectiveSocialThisCycle);
            CollectNearbySettlements(outpost, collectScratch);
            MarkHighestTierContributors(collectScratch);

            bool anyPaid = false;
            for (int i = 0; i < collectScratch.Count; i++)
            {
                var s = collectScratch[i];
                if (!s.ContributesToFaction || s.Faction == null) continue;
                if (!IsEligiblePartnerFaction(s.Faction)) continue;

                int room = GetGoodwillRoom(s.Faction);
                if (room <= 0) continue;

                int award = Mathf.Min(Mathf.Max(0, Mathf.RoundToInt(s.BasePoints * mult)), room);
                if (award <= 0) continue;

                if (!GoodwillChangeNotifier.TryAffectPlayerGoodwill(s.Faction, award, out int now))
                    continue;

                GoodwillChangeNotifier.NotifyEmbassyCycle(s.Faction, award, now, outpost);
                anyPaid = true;
            }

            return anyPaid;
        }
    }
}
