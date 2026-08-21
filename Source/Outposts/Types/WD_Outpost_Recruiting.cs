using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Recruiting outpost: base recruits from Social (10 per recruit) plus neighbor tier bonus; optional skill priority on spawned pawns.</summary>
    public static class Outpost_Recruiting
    {
        /// <summary>Social skill required per base recruit (strict: 9→0, 19→1, 20→2).</summary>
        public const float SocialPerRecruit = 10f;

        /// <summary>Sum of <see cref="Outpost_Trading.RecruitingTierWeight"/> over partners is divided by this for bonus recruits.</summary>
        public const int NeighborBonusDivisor = 3;

        /// <summary>Recruit count multiplier when a specific skill is selected (30% fewer recruits).</summary>
        public const float PrioritySkillRecruitMultiplier = 0.7f;

        public const int PrioritySkillRecruitPenaltyPercent = 30;

        public struct XenotypePoolEntry
        {
            public XenotypeDef Xenotype;
            public float Weight;
        }

        public struct PawnKindPoolEntry
        {
            public PawnKindDef Kind;
            public float Weight;
        }

        private static readonly List<Outpost_Trading.NearbyPartnerInfo> nearbyPartnerScratch = new List<Outpost_Trading.NearbyPartnerInfo>();
        private static readonly Dictionary<Faction, float> factionTierWeightScratch = new Dictionary<Faction, float>();
        private static readonly List<XenotypePoolEntry> xenotypePoolScratch = new List<XenotypePoolEntry>();
        private static readonly List<PawnKindPoolEntry> pawnKindPoolScratch = new List<PawnKindPoolEntry>();
        private static readonly List<PawnKindPoolEntry> pawnKindFactionTempScratch = new List<PawnKindPoolEntry>();
        private static readonly List<XenotypeChance> factionXenotypeScratch = new List<XenotypeChance>();
        private static readonly Dictionary<PawnKindDef, float> pawnKindWeightScratch = new Dictionary<PawnKindDef, float>();
        private static readonly Dictionary<PawnKindDef, float> pawnKindAccumScratch = new Dictionary<PawnKindDef, float>();

        /// <summary>Total Social at outpost (effective, for running average capacity).</summary>
        public static float GetDeliveryDrivingCapacity(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.VirtualPawns == null) return 0f;
            float sum = 0f;
            var pawns = outpost.VirtualPawns;
            for (int i = 0; i < pawns.Count; i++)
                sum += pawns[i].social;
            return OutpostSkillScaling.ToEffective(sum);
        }

        public static float GetDeliveryDrivingCapacityRaw(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.VirtualPawns == null) return 0f;
            float sum = 0f;
            var pawns = outpost.VirtualPawns;
            for (int i = 0; i < pawns.Count; i++)
                sum += pawns[i].social;
            return sum;
        }

        /// <summary>
        /// Builds a weighted xenotype pool from nearby settlements/outposts.
        /// Per distinct faction: sum tier weights of its partners in range, multiply that faction's xenotype chances, aggregate and normalize.
        /// </summary>
        public static void BuildXenotypePool(WorldObject_WD_Outpost outpost, List<XenotypePoolEntry> pool)
        {
            pool?.Clear();
            if (pool == null) return;

            if (!ModsConfig.BiotechActive)
            {
                pool.Add(new XenotypePoolEntry { Xenotype = XenotypeDefOf.Baseliner, Weight = 1f });
                return;
            }

            Outpost_Trading.CollectNearbyPartnersMarked(outpost, nearbyPartnerScratch);
            if (nearbyPartnerScratch.Count == 0)
            {
                pool.Add(new XenotypePoolEntry { Xenotype = XenotypeDefOf.Baseliner, Weight = 1f });
                return;
            }

            factionTierWeightScratch.Clear();
            for (int i = 0; i < nearbyPartnerScratch.Count; i++)
            {
                var partner = nearbyPartnerScratch[i];
                if (!partner.ContributesToFaction) continue;
                Faction faction = partner.Faction;
                if (faction?.def == null) continue;
                float tierWeight = Outpost_Trading.RecruitingTierWeight(partner.Tier);
                if (factionTierWeightScratch.TryGetValue(faction, out float existing))
                    factionTierWeightScratch[faction] = existing + tierWeight;
                else
                    factionTierWeightScratch[faction] = tierWeight;
            }

            var accumulated = new Dictionary<XenotypeDef, float>();
            float total = 0f;
            foreach (var kv in factionTierWeightScratch)
            {
                FactionDef factionDef = kv.Key.def;
                float factionWeight = kv.Value;
                if (factionDef == null || factionWeight <= 0f) continue;
                AddFactionXenotypeContribution(factionDef, factionWeight, accumulated, ref total);
            }

            if (total <= 0f || accumulated.Count == 0)
            {
                pool.Add(new XenotypePoolEntry { Xenotype = XenotypeDefOf.Baseliner, Weight = 1f });
                return;
            }

            foreach (var kv in accumulated)
                pool.Add(new XenotypePoolEntry { Xenotype = kv.Key, Weight = kv.Value / total });

            pool.Sort((a, b) => b.Weight.CompareTo(a.Weight));
        }

        /// <summary>Build a xenotype pool from one faction's xenotype set (e.g. conquered settlement owner).</summary>
        public static void BuildXenotypePoolFromFaction(Faction faction, List<XenotypePoolEntry> pool)
        {
            pool?.Clear();
            if (pool == null) return;

            if (!ModsConfig.BiotechActive)
            {
                pool.Add(new XenotypePoolEntry { Xenotype = XenotypeDefOf.Baseliner, Weight = 1f });
                return;
            }

            if (faction?.def == null)
            {
                pool.Add(new XenotypePoolEntry { Xenotype = XenotypeDefOf.Baseliner, Weight = 1f });
                return;
            }

            var accumulated = new Dictionary<XenotypeDef, float>();
            float total = 0f;
            AddFactionXenotypeContribution(faction.def, 1f, accumulated, ref total);

            if (total <= 0f || accumulated.Count == 0)
            {
                pool.Add(new XenotypePoolEntry { Xenotype = XenotypeDefOf.Baseliner, Weight = 1f });
                return;
            }

            foreach (var kv in accumulated)
                pool.Add(new XenotypePoolEntry { Xenotype = kv.Key, Weight = kv.Value / total });

            pool.Sort((a, b) => b.Weight.CompareTo(a.Weight));
        }

        /// <summary>
        /// Humanlike pawn kinds from this faction's Combat pawn-group makers only.
        /// Weight = selectionWeight / cost so expensive elites stay rare like raid generation.
        /// Used by recruiting, conquest founding, and auto-resolve captives.
        /// </summary>
        public static void BuildPawnKindPoolFromFaction(Faction faction, List<PawnKindPoolEntry> pool)
        {
            pool?.Clear();
            if (pool == null) return;

            pawnKindWeightScratch.Clear();
            FactionDef def = faction?.def;
            bool addedCombat = false;
            if (def != null)
            {
                var makers = def.pawnGroupMakers;
                if (makers != null)
                {
                    for (int i = 0; i < makers.Count; i++)
                    {
                        PawnGroupMaker maker = makers[i];
                        if (maker == null || maker.kindDef != PawnGroupKindDefOf.Combat) continue;
                        AddCombatPawnGenOptions(maker.options);
                        AddCombatPawnGenOptions(maker.guards);
                        addedCombat = true;
                    }
                }

                // No Combat makers (unusual): fall back to basic member only.
                if (!addedCombat)
                    AddPawnKindWeight(def.basicMemberKind, 1f);
            }

            float total = 0f;
            foreach (var kv in pawnKindWeightScratch)
                total += kv.Value;

            if (total <= 0f || pawnKindWeightScratch.Count == 0)
            {
                pool.Add(new PawnKindPoolEntry { Kind = PawnKindDefOf.Colonist, Weight = 1f });
                return;
            }

            foreach (var kv in pawnKindWeightScratch)
                pool.Add(new PawnKindPoolEntry { Kind = kv.Key, Weight = kv.Value / total });

            pool.Sort((a, b) => b.Weight.CompareTo(a.Weight));
        }

        /// <summary>
        /// Builds a weighted pawn-kind pool from nearby settlements/outposts.
        /// Per distinct faction: sum tier weights of its partners in range, multiply that faction's
        /// Combat raid kind chances, aggregate and normalize.
        /// </summary>
        public static void BuildPawnKindPool(WorldObject_WD_Outpost outpost, List<PawnKindPoolEntry> pool)
        {
            pool?.Clear();
            if (pool == null) return;

            Outpost_Trading.CollectNearbyPartnersMarked(outpost, nearbyPartnerScratch);
            if (nearbyPartnerScratch.Count == 0)
            {
                pool.Add(new PawnKindPoolEntry { Kind = PawnKindDefOf.Colonist, Weight = 1f });
                return;
            }

            factionTierWeightScratch.Clear();
            for (int i = 0; i < nearbyPartnerScratch.Count; i++)
            {
                var partner = nearbyPartnerScratch[i];
                if (!partner.ContributesToFaction) continue;
                Faction faction = partner.Faction;
                if (faction?.def == null) continue;
                float tierWeight = Outpost_Trading.RecruitingTierWeight(partner.Tier);
                if (factionTierWeightScratch.TryGetValue(faction, out float existing))
                    factionTierWeightScratch[faction] = existing + tierWeight;
                else
                    factionTierWeightScratch[faction] = tierWeight;
            }

            pawnKindAccumScratch.Clear();
            float total = 0f;
            foreach (var kv in factionTierWeightScratch)
            {
                float factionWeight = kv.Value;
                if (factionWeight <= 0f) continue;
                BuildPawnKindPoolFromFaction(kv.Key, pawnKindFactionTempScratch);
                for (int i = 0; i < pawnKindFactionTempScratch.Count; i++)
                {
                    var entry = pawnKindFactionTempScratch[i];
                    if (entry.Kind == null || entry.Weight <= 0f) continue;
                    float w = entry.Weight * factionWeight;
                    if (pawnKindAccumScratch.TryGetValue(entry.Kind, out float existing))
                        pawnKindAccumScratch[entry.Kind] = existing + w;
                    else
                        pawnKindAccumScratch[entry.Kind] = w;
                    total += w;
                }
            }

            if (total <= 0f || pawnKindAccumScratch.Count == 0)
            {
                pool.Add(new PawnKindPoolEntry { Kind = PawnKindDefOf.Colonist, Weight = 1f });
                return;
            }

            foreach (var kv in pawnKindAccumScratch)
                pool.Add(new PawnKindPoolEntry { Kind = kv.Key, Weight = kv.Value / total });

            pool.Sort((a, b) => b.Weight.CompareTo(a.Weight));
        }

        /// <summary>Roll one pawn kind from a normalized pool; Colonist when empty.</summary>
        public static PawnKindDef RollPawnKindFromPool(List<PawnKindPoolEntry> pool)
        {
            if (pool == null || pool.Count == 0)
                return PawnKindDefOf.Colonist;

            float roll = Rand.Value;
            float acc = 0f;
            for (int i = 0; i < pool.Count; i++)
            {
                acc += pool[i].Weight;
                if (roll <= acc)
                    return pool[i].Kind ?? PawnKindDefOf.Colonist;
            }

            return pool[pool.Count - 1].Kind ?? PawnKindDefOf.Colonist;
        }

        private static void AddCombatPawnGenOptions(List<PawnGenOption> options)
        {
            if (options == null) return;
            for (int i = 0; i < options.Count; i++)
            {
                PawnGenOption opt = options[i];
                if (opt?.kind == null) continue;
                float selection = Mathf.Max(0.01f, opt.selectionWeight);
                float cost = Mathf.Max(1f, opt.Cost);
                AddPawnKindWeight(opt.kind, selection / cost);
            }
        }

        private static void AddPawnKindWeight(PawnKindDef kind, float weight)
        {
            if (!IsRecruitableFactionPawnKind(kind) || weight <= 0f) return;
            if (pawnKindWeightScratch.TryGetValue(kind, out float existing))
                pawnKindWeightScratch[kind] = existing + weight;
            else
                pawnKindWeightScratch[kind] = weight;
        }

        /// <summary>Humanlike non-animal kinds suitable for a player colonist recruit.</summary>
        public static bool IsRecruitableFactionPawnKind(PawnKindDef kind)
        {
            if (kind?.race == null) return false;
            RaceProperties race = kind.RaceProps;
            if (race == null || !race.Humanlike) return false;
            if (race.IsMechanoid || race.Animal) return false;
            return true;
        }

        /// <summary>Roll one xenotype from a normalized pool; returns Baseliner when pool empty or Biotech inactive.</summary>
        public static XenotypeDef RollXenotypeFromPool(List<XenotypePoolEntry> pool)
        {
            if (!ModsConfig.BiotechActive || pool == null || pool.Count == 0)
                return XenotypeDefOf.Baseliner;

            float roll = Rand.Value;
            float cumulative = 0f;
            for (int i = 0; i < pool.Count; i++)
            {
                cumulative += pool[i].Weight;
                if (roll <= cumulative)
                    return pool[i].Xenotype ?? XenotypeDefOf.Baseliner;
            }

            return pool[pool.Count - 1].Xenotype ?? XenotypeDefOf.Baseliner;
        }

        /// <summary>Sum of tier weights from contributing partners (top 3 per faction) in recruiting radius.</summary>
        public static float GetTierWeightSum(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 0f;
            Outpost_Trading.CollectNearbyPartnersMarked(outpost, nearbyPartnerScratch);
            float sum = 0f;
            for (int i = 0; i < nearbyPartnerScratch.Count; i++)
            {
                if (!nearbyPartnerScratch[i].ContributesToFaction) continue;
                sum += Outpost_Trading.RecruitingTierWeight(nearbyPartnerScratch[i].Tier);
            }
            return sum;
        }

        public static int GetBaseRecruitsFromSocial(float avgSocial)
        {
            return Mathf.FloorToInt(avgSocial / SocialPerRecruit);
        }

        public static int GetNeighborBonusRecruits(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 0;
            return Mathf.FloorToInt(GetTierWeightSum(outpost) / NeighborBonusDivisor);
        }

        /// <summary>Tier point value used for neighbor bonus (T1=1, T2=2, T3=3.5, T4=5).</summary>
        public static float GetTierPoints(SettlementTier tier) => Outpost_Trading.RecruitingTierWeight(tier);

        /// <summary>One-line rule: how neighbor points become extra recruits.</summary>
        public static string GetNeighborBonusRuleText()
            => OutpostTranslationUtil.Key("TSA_WD_Recruiting_NeighborBonusRule", NeighborBonusDivisor.ToString());

        /// <summary>Tier point values per settlement tier (footer rule tooltip).</summary>
        public static string GetNeighborBonusTierPointsDetailText()
            => OutpostTranslationUtil.Key("TSA_WD_Recruiting_NeighborBonusTierPointsTip");

        /// <summary>Footer rule line below the settlement list in the recruiting dialog.</summary>
        public static string GetNeighborBonusFooterRuleText()
            => OutpostTranslationUtil.Key("TSA_WD_Recruiting_NeighborBonusFooterRule", NeighborBonusDivisor.ToString());

        /// <summary>Footer total line: combined neighbor points from all settlements.</summary>
        public static string GetNeighborBonusFooterTotalLine(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            return OutpostTranslationUtil.Key("TSA_WD_Recruiting_NeighborBonusFooterTotal", GetTierWeightSum(outpost).ToString("0.#"));
        }

        /// <summary>Footer result line: extra pawns from neighbor points this cycle.</summary>
        public static string GetNeighborBonusFooterResultLine(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            return OutpostTranslationUtil.Key("TSA_WD_Recruiting_NeighborBonusFooterResult", GetNeighborBonusRecruits(outpost).ToString());
        }

        /// <summary>Conversion line: combined points → extra recruits.</summary>
        public static string GetNeighborBonusConversionLine(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            float sum = GetTierWeightSum(outpost);
            int bonus = GetNeighborBonusRecruits(outpost);
            return OutpostTranslationUtil.Key(
                "TSA_WD_Recruiting_Math_NeighborConversion",
                sum.ToString("0.#"),
                NeighborBonusDivisor.ToString(),
                bonus.ToString());
        }

        /// <summary>Short tier label for settlement rows (T1 … T4).</summary>
        public static string FormatTierShortLabel(SettlementTier tier) => Outpost_Trading.FormatTierShortLabel(tier);

        /// <summary>Player-facing tier label (Tier 1 … Tier 4).</summary>
        public static string FormatTierLabel(SettlementTier tier) => Outpost_Trading.FormatTierLabel(tier);

        /// <summary>Tooltip for one nearby partner: location, points, xenotypes. Totals are shown below the list.</summary>
        public static string BuildPartnerRowTooltip(Outpost_Trading.NearbyPartnerInfo partner)
        {
            return OutpostTranslationUtil.Key(
                "TSA_WD_Recruiting_PartnerRowTip",
                partner.Label,
                FormatTierLabel(partner.Tier),
                partner.Faction?.Name ?? partner.Faction?.def?.LabelCap ?? "?",
                partner.DistanceTiles.ToString(),
                GetTierPoints(partner.Tier).ToString("0.#"));
        }

        /// <summary>Gizmo label: Recruiting any Pawn or Recruiting: Shooting.</summary>
        public static string GetRecruitingFocusLabel(WorldObject_WD_Outpost outpost)
        {
            var skill = outpost?.SelectedRecruitPrioritySkill;
            if (skill == null)
                return OutpostTranslationUtil.Key("TSA_WD_Gizmo_RecruitingAny");
            return OutpostTranslationUtil.Key("TSA_WD_Gizmo_RecruitingSkill", skill.LabelCap);
        }

        /// <summary>Inspect / overview product line: expected recruit count and optional skill focus (no "Recruiting" prefix).</summary>
        public static string GetInspectProductLine(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            float avgSocial = outpost.GetCapacityForYieldPreview();
            int count = ComputeRecruitCount(outpost, avgSocial);
            var skill = outpost.SelectedRecruitPrioritySkill;
            if (skill != null)
                return OutpostTranslationUtil.Key("TSA_WD_Recruiting_Inspect_WithSkill", count.ToString(), skill.LabelCap);
            return OutpostTranslationUtil.Key("TSA_WD_Recruiting_Inspect_Any", count.ToString());
        }

        /// <summary>Collect sorted nearby partners for UI (dialog, tooltips).</summary>
        public static void CollectSortedNearbyPartners(WorldObject_WD_Outpost outpost, List<Outpost_Trading.NearbyPartnerInfo> results)
            => Outpost_Trading.CollectSortedNearbyPartners(outpost, results);

        /// <summary>One-line partner label: settlement name, tier, and neighbor points.</summary>
        public static string FormatPartnerRowLabel(Outpost_Trading.NearbyPartnerInfo partner)
        {
            float pts = GetTierPoints(partner.Tier);
            return OutpostTranslationUtil.Key(
                "TSA_WD_Recruiting_PartnerRow",
                partner.Label,
                FormatTierShortLabel(partner.Tier),
                pts.ToString("0.#"));
        }

        /// <summary>Social + neighbor recruits, scaled by global output multiplier, before skill-training penalty.</summary>
        public static int ComputeRecruitCountBeforePriorityPenalty(WorldObject_WD_Outpost outpost, float avgSocial)
        {
            if (outpost == null) return 0;
            int raw = GetBaseRecruitsFromSocial(avgSocial) + GetNeighborBonusRecruits(outpost);
            return Outpost_Production_Utils.ScaleOutputStackCount(Mathf.Max(0, raw));
        }

        /// <summary>Base Social recruits + neighbor tier bonus, optional skill-training penalty, then output scaling already applied in pre-penalty step.</summary>
        public static int ComputeRecruitCount(WorldObject_WD_Outpost outpost, float avgSocial)
        {
            int count = ComputeRecruitCountBeforePriorityPenalty(outpost, avgSocial);
            if (outpost?.SelectedRecruitPrioritySkill != null)
                count = Mathf.Max(0, Mathf.FloorToInt(count * PrioritySkillRecruitMultiplier));
            return count;
        }

        public static string GetPrioritySkillRowFormula(SkillDef skill)
        {
            if (skill == null)
                return OutpostTranslationUtil.Key("TSA_WD_Recruiting_PriorityAnyFormula");
            return OutpostTranslationUtil.Key(
                "TSA_WD_Recruiting_PrioritySkillFormula",
                PrioritySkillRecruitPenaltyPercent.ToString(),
                GetPrioritySkillMinLevel().ToString());
        }

        public static string GetPrioritySkillRowTooltip(SkillDef skill)
        {
            if (skill == null)
                return OutpostTranslationUtil.Key("TSA_WD_Recruiting_PriorityAnyTip");
            return OutpostTranslationUtil.Key(
                "TSA_WD_Recruiting_PrioritySkillTip",
                PrioritySkillRecruitPenaltyPercent.ToString(),
                GetPrioritySkillMinLevel().ToString(),
                skill.LabelCap);
        }

        public static int GetPrioritySkillMinLevel()
        {
            return WorldDominationMod.settings?.GetConquestFoundingMinRelevantSkillClamped()
                ?? WorldDominationSettings.DefConquestFoundingMinRelevantSkill;
        }

        /// <summary>All skills for the priority picker (load order).</summary>
        public static List<SkillDef> GetPrioritySkillCandidates()
        {
            var list = new List<SkillDef>();
            var all = DefDatabase<SkillDef>.AllDefsListForReading;
            if (all == null) return list;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null) list.Add(all[i]);
            }
            return list;
        }

        /// <summary>Stats row value: skill label or Any.</summary>
        public static string GetPrioritySkillDisplayLine(WorldObject_WD_Outpost outpost)
        {
            var skill = outpost?.SelectedRecruitPrioritySkill;
            if (skill == null)
                return OutpostTranslationUtil.Key("TSA_WD_Recruiting_PriorityAny");
            return skill.LabelCap;
        }

        /// <summary>When timer expires: recruits from Social + neighbor bonus; generate pawns; send caravan if redirected, otherwise keep them at this outpost.</summary>
        public static bool Produce(WorldObject_WD_Outpost outpost, float avgSocial)
        {
            if (outpost == null) return false;
            int pawnCount = ComputeRecruitCount(outpost, avgSocial);
            if (pawnCount <= 0) return false;

            float minStrength = WorldDominationMod.settings?.outpostDeliveryMinStrength ?? 100f;
            var comp = outpost.GetComponent<CompViralSpread>();
            bool redirect = comp != null && comp.redirectionTargetTile >= 0;
            if (redirect && comp.strength < minStrength) return false;

            BuildXenotypePool(outpost, xenotypePoolScratch);
            BuildPawnKindPool(outpost, pawnKindPoolScratch);
            SkillDef prioritySkill = outpost.SelectedRecruitPrioritySkill;

            var pawnList = new List<Pawn>();
            for (int i = 0; i < pawnCount; i++)
            {
                XenotypeDef xenotype = RollXenotypeFromPool(xenotypePoolScratch);
                PawnKindDef kind = RollPawnKindFromPool(pawnKindPoolScratch);
                Pawn p = GenerateRecruitPawn(xenotype, prioritySkill, kind);
                if (p != null) pawnList.Add(p);
            }
            if (pawnList.Count == 0) return false;

            if (!redirect)
            {
                int stayed = 0;
                for (int i = 0; i < pawnList.Count; i++)
                {
                    if (outpost.AddPawn(pawnList[i], null))
                        stayed++;
                    else
                        pawnList[i]?.Destroy();
                }
                if (stayed <= 0) return false;
                Messages.Message("TSA_WD_RecruitsStayedAtOutpost".Translate(stayed, outpost.Label), outpost, MessageTypeDefOf.NeutralEvent);
                return true;
            }

            Caravan caravan = CaravanMaker.MakeCaravan(pawnList, Faction.OfPlayer, outpost.Tile, true);
            PlayerPawnTransferUtility.PackTravelPemmicanFromOutpost(caravan, pawnList.Count, outpost);

            float cost = WorldDominationMod.settings?.outpostDeliveryStrengthCost ?? 50f;
            if (comp != null) comp.strength = Mathf.Max(0, comp.strength - cost);

            int destTile = comp.redirectionTargetTile;
            caravan.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(destTile, outpost), null, false, false);

            Messages.Message("TSA_WD_RecruitsRedirected".Translate(pawnList.Count, outpost.Label), outpost, MessageTypeDefOf.NeutralEvent);
            return true;
        }

        public static Pawn GenerateRecruitPawn(XenotypeDef xenotype, SkillDef prioritySkill = null, PawnKindDef pawnKind = null)
        {
            PawnKindDef kind = IsRecruitableFactionPawnKind(pawnKind) ? pawnKind : PawnKindDefOf.Colonist;
            Pawn p;
            if (ModsConfig.BiotechActive && xenotype != null)
            {
                var req = new PawnGenerationRequest(
                    kind,
                    Faction.OfPlayer,
                    PawnGenerationContext.NonPlayer,
                    -1,
                    forceGenerateNewPawn: true,
                    canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: true,
                    forcedXenotype: xenotype);
                p = PawnGenerator.GeneratePawn(req);
            }
            else
            {
                var baselineReq = new PawnGenerationRequest(
                    kind,
                    Faction.OfPlayer,
                    PawnGenerationContext.NonPlayer,
                    -1,
                    forceGenerateNewPawn: true,
                    canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: true);
                p = PawnGenerator.GeneratePawn(baselineReq);
            }

            // Some faction kinds refuse violence-capable generation; fall back to a plain colonist.
            if (p == null && kind != PawnKindDefOf.Colonist)
                return GenerateRecruitPawn(xenotype, prioritySkill, PawnKindDefOf.Colonist);

            ApplyPrioritySkillFloor(p, prioritySkill);
            return p;
        }

        private static void ApplyPrioritySkillFloor(Pawn pawn, SkillDef prioritySkill)
        {
            if (pawn?.skills == null || prioritySkill == null) return;
            EnsureEffectiveSkillLevelAtLeast(pawn.skills.GetSkill(prioritySkill), GetPrioritySkillMinLevel());
        }

        /// <summary>
        /// Floors each outpost-relevant skill to the conquest founding min (settings), and may grant Minor passion.
        /// Used when generating founding workers with no caravan (conquest, buy settlement, debug simulate).
        /// </summary>
        public static void ApplyFoundingRelevantSkillFloors(Pawn pawn, WorldObjectDef outpostDef, int? minRelevantSkillOverride = null)
        {
            if (pawn?.skills == null || outpostDef == null) return;
            List<SkillDef> relevant = WorldObject_WD_Outpost.GetRelevantSkillDefs(outpostDef);
            if (relevant == null || relevant.Count == 0) return;

            int minLevel = minRelevantSkillOverride
                ?? WorldDominationMod.settings?.GetConquestFoundingMinRelevantSkillClamped()
                ?? WorldDominationSettings.DefConquestFoundingMinRelevantSkill;
            minLevel = Mathf.Clamp(minLevel, 0, 20);

            for (int i = 0; i < relevant.Count; i++)
            {
                SkillDef skill = relevant[i];
                if (skill == null) continue;
                SkillRecord record = pawn.skills.GetSkill(skill);
                if (record == null || record.TotallyDisabled) continue;
                EnsureEffectiveSkillLevelAtLeast(record, minLevel);
                if (record.passion != Passion.Major && Rand.Chance(0.5f))
                    record.passion = Passion.Minor;
            }
        }

        /// <summary>
        /// Ensures <see cref="SkillRecord.Level"/> (aptitude-aware) is at least <paramref name="minLevel"/>.
        /// Vanilla's Level setter writes <c>levelInt</c> only; with negative Biotech aptitude,
        /// <c>record.Level = 4</c> can still read as 3. Compensate by targeting levelInt = min - Aptitude.
        /// </summary>
        private static void EnsureEffectiveSkillLevelAtLeast(SkillRecord record, int minLevel)
        {
            if (record == null || record.TotallyDisabled) return;
            minLevel = Mathf.Clamp(minLevel, SkillRecord.MinLevel, SkillRecord.MaxLevel);
            if (record.Level >= minLevel) return;

            int aptitude = record.Aptitude;
            int desiredLevelInt = Mathf.Clamp(minLevel - aptitude, SkillRecord.MinLevel, SkillRecord.MaxLevel);
            record.Level = desiredLevelInt;

            // Extreme aptitude vs clamp: bump until effective Level meets the floor or we hit max.
            int guard = 0;
            while (record.Level < minLevel && record.levelInt < SkillRecord.MaxLevel && guard++ < 25)
                record.Level = record.levelInt + 1;
        }

        /// <summary>True when every relevant skill for this outpost type is usable on the pawn (none TotallyDisabled).</summary>
        public static bool PawnCanUseAllRelevantSkills(Pawn pawn, WorldObjectDef outpostDef)
        {
            if (pawn?.skills == null || outpostDef == null) return false;
            List<SkillDef> relevant = WorldObject_WD_Outpost.GetRelevantSkillDefs(outpostDef);
            if (relevant == null || relevant.Count == 0) return true;
            for (int i = 0; i < relevant.Count; i++)
            {
                SkillDef skill = relevant[i];
                if (skill == null) continue;
                if (pawn.skills.GetSkill(skill).TotallyDisabled) return false;
            }
            return true;
        }

        private static void AddFactionXenotypeContribution(
            FactionDef factionDef,
            float factionTierWeightSum,
            Dictionary<XenotypeDef, float> accumulated,
            ref float total)
        {
            if (factionDef == null || factionTierWeightSum <= 0f) return;

            if (!ModsConfig.BiotechActive || !factionDef.humanlikeFaction)
            {
                AddWeight(accumulated, XenotypeDefOf.Baseliner, factionTierWeightSum, ref total);
                return;
            }

            factionXenotypeScratch.Clear();
            if (factionDef.BaselinerChance > 0f)
                factionXenotypeScratch.Add(new XenotypeChance(XenotypeDefOf.Baseliner, factionDef.BaselinerChance));

            XenotypeSet set = factionDef.xenotypeSet;
            if (set != null)
            {
                for (int i = 0; i < set.Count; i++)
                {
                    XenotypeChance entry = set[i];
                    if (entry.xenotype == null || entry.xenotype == XenotypeDefOf.Baseliner) continue;
                    factionXenotypeScratch.Add(entry);
                }
            }

            if (factionXenotypeScratch.Count == 0)
            {
                AddWeight(accumulated, XenotypeDefOf.Baseliner, factionTierWeightSum, ref total);
                return;
            }

            float chanceSum = 0f;
            for (int i = 0; i < factionXenotypeScratch.Count; i++)
                chanceSum += factionXenotypeScratch[i].chance;

            if (chanceSum <= 0f)
            {
                AddWeight(accumulated, XenotypeDefOf.Baseliner, factionTierWeightSum, ref total);
                return;
            }

            for (int i = 0; i < factionXenotypeScratch.Count; i++)
            {
                XenotypeChance entry = factionXenotypeScratch[i];
                float normalized = entry.chance / chanceSum;
                AddWeight(accumulated, entry.xenotype, normalized * factionTierWeightSum, ref total);
            }
        }

        private static void AddWeight(Dictionary<XenotypeDef, float> accumulated, XenotypeDef xenotype, float weight, ref float total)
        {
            if (xenotype == null || weight <= 0f) return;
            if (accumulated.TryGetValue(xenotype, out float existing))
                accumulated[xenotype] = existing + weight;
            else
                accumulated[xenotype] = weight;
            total += weight;
        }

        /// <summary>Compact expected-outcome breakdown for the recruiting dialog tooltip.</summary>
        public static string GetDetailedMathTooltip(WorldObject_WD_Outpost outpost, float avgSocial)
        {
            if (outpost == null) return "";

            int baseRec = GetBaseRecruitsFromSocial(avgSocial);
            float neighborPts = GetTierWeightSum(outpost);
            int neighborBonus = GetNeighborBonusRecruits(outpost);
            int beforePenalty = ComputeRecruitCountBeforePriorityPenalty(outpost, avgSocial);
            int total = ComputeRecruitCount(outpost, avgSocial);
            bool hasPenalty = outpost.SelectedRecruitPrioritySkill != null;

            var lines = new List<string>(4);

            lines.Add(OutpostTranslationUtil.Key(
                "TSA_WD_Recruiting_Math_SocialLine",
                avgSocial.ToString("F0"),
                baseRec.ToString()));

            lines.Add(OutpostTranslationUtil.Key(
                "TSA_WD_Recruiting_Math_NeighborLine",
                neighborPts.ToString("0.#"),
                neighborBonus.ToString()));

            if (hasPenalty)
            {
                lines.Add(OutpostTranslationUtil.Key(
                    "TSA_WD_Recruiting_Math_SkillPenaltyLine",
                    PrioritySkillRecruitPenaltyPercent.ToString()));

                float product = beforePenalty * PrioritySkillRecruitMultiplier;
                lines.Add(OutpostTranslationUtil.Key(
                    "TSA_WD_Recruiting_Math_ResultWithPenalty",
                    beforePenalty.ToString(),
                    PrioritySkillRecruitMultiplier.ToString("0.#"),
                    product.ToString("F1"),
                    total.ToString()));
            }
            else
            {
                lines.Add(OutpostTranslationUtil.Key(
                    "TSA_WD_Recruiting_Math_ResultNoPenalty",
                    beforePenalty.ToString()));
            }

            return string.Join("\n", lines.ToArray());
        }

        /// <summary>Tooltip for recruiting gizmo / legacy callers.</summary>
        public static string GetProductionTooltip(WorldObject_WD_Outpost outpost, float avgSocial)
        {
            int predicted = ComputeRecruitCount(outpost, avgSocial);
            string t = OutpostTranslationUtil.Key(
                "TSA_WD_Production_TooltipRecruiting",
                SocialPerRecruit.ToString("F0"),
                avgSocial.ToString("F1"),
                predicted.ToString());

            string detail = GetDetailedMathTooltip(outpost, avgSocial);
            if (!string.IsNullOrEmpty(detail))
                t += "\n\n" + detail;

            string poolTip = GetXenotypePoolTooltipAppendix(outpost);
            if (!string.IsNullOrEmpty(poolTip))
                t += "\n\n" + poolTip;
            string kindTip = GetPawnKindPoolTooltipAppendix(outpost);
            if (!string.IsNullOrEmpty(kindTip))
                t += "\n\n" + kindTip;
            return t;
        }

        /// <summary>One line per xenotype in the local recruiting pool with percentage chance.</summary>
        public static string GetXenotypePoolTooltipAppendix(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            BuildXenotypePool(outpost, xenotypePoolScratch);
            if (xenotypePoolScratch.Count == 0) return "";

            string header = OutpostTranslationUtil.Key("TSA_WD_Recruiting_XenotypePoolHeader");
            var lines = new List<string>(xenotypePoolScratch.Count);
            for (int i = 0; i < xenotypePoolScratch.Count; i++)
            {
                var entry = xenotypePoolScratch[i];
                string label = entry.Xenotype?.LabelCap ?? XenotypeDefOf.Baseliner.LabelCap;
                string pct = (entry.Weight * 100f).ToString("F0") + "%";
                lines.Add(OutpostTranslationUtil.Key("TSA_WD_Recruiting_XenotypePoolLine", label, pct));
            }

            return header + "\n" + string.Join("\n", lines);
        }

        /// <summary>One line per pawn kind in the local recruiting pool with percentage chance.</summary>
        public static string GetPawnKindPoolTooltipAppendix(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            BuildPawnKindPool(outpost, pawnKindPoolScratch);
            if (pawnKindPoolScratch.Count == 0) return "";

            string header = OutpostTranslationUtil.Key("TSA_WD_Recruiting_PawnKindPoolHeader");
            var lines = new List<string>(pawnKindPoolScratch.Count);
            for (int i = 0; i < pawnKindPoolScratch.Count; i++)
            {
                var entry = pawnKindPoolScratch[i];
                string label = entry.Kind?.LabelCap ?? PawnKindDefOf.Colonist.LabelCap;
                string pct = (entry.Weight * 100f).ToString("F0") + "%";
                lines.Add(OutpostTranslationUtil.Key("TSA_WD_Recruiting_PawnKindPoolLine", label, pct));
            }

            return header + "\n" + string.Join("\n", lines);
        }

        /// <summary>Compact one-line summary for stats/inspect, e.g. "Pigskin 80%, Baseliner 20%".</summary>
        public static string GetXenotypePoolSummaryLine(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            BuildXenotypePool(outpost, xenotypePoolScratch);
            if (xenotypePoolScratch.Count == 0) return "";

            var parts = new List<string>(xenotypePoolScratch.Count);
            for (int i = 0; i < xenotypePoolScratch.Count; i++)
            {
                var entry = xenotypePoolScratch[i];
                string label = entry.Xenotype?.LabelCap ?? "Baseliner";
                parts.Add(label + " " + (entry.Weight * 100f).ToString("F0") + "%");
            }
            return string.Join(", ", parts);
        }

        /// <summary>Compact one-line summary for stats/inspect, e.g. "Pirate 60%, Drifter 40%".</summary>
        public static string GetPawnKindPoolSummaryLine(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            BuildPawnKindPool(outpost, pawnKindPoolScratch);
            if (pawnKindPoolScratch.Count == 0) return "";

            var parts = new List<string>(pawnKindPoolScratch.Count);
            for (int i = 0; i < pawnKindPoolScratch.Count; i++)
            {
                var entry = pawnKindPoolScratch[i];
                string label = entry.Kind?.LabelCap ?? PawnKindDefOf.Colonist.LabelCap;
                parts.Add(label + " " + (entry.Weight * 100f).ToString("F0") + "%");
            }
            return string.Join(", ", parts);
        }

        /// <summary>Summary line for gizmo/overview.</summary>
        public static string GetProductionSummaryLine(WorldObject_WD_Outpost outpost, float avgSocial)
        {
            if (outpost == null) return "";
            return GetInspectProductLine(outpost);
        }

        /// <summary>Inline xenotype lines for dialog (header omitted).</summary>
        public static List<string> GetXenotypePoolDisplayLines(WorldObject_WD_Outpost outpost)
        {
            var result = new List<string>();
            if (outpost == null) return result;
            BuildXenotypePool(outpost, xenotypePoolScratch);
            for (int i = 0; i < xenotypePoolScratch.Count; i++)
            {
                var entry = xenotypePoolScratch[i];
                string label = entry.Xenotype?.LabelCap ?? XenotypeDefOf.Baseliner.LabelCap;
                string pct = (entry.Weight * 100f).ToString("F0") + "%";
                result.Add(OutpostTranslationUtil.Key("TSA_WD_Recruiting_XenotypePoolLine", label, pct));
            }
            return result;
        }

        /// <summary>Inline pawn-kind lines for dialog (header omitted).</summary>
        public static List<string> GetPawnKindPoolDisplayLines(WorldObject_WD_Outpost outpost)
        {
            var result = new List<string>();
            if (outpost == null) return result;
            BuildPawnKindPool(outpost, pawnKindPoolScratch);
            for (int i = 0; i < pawnKindPoolScratch.Count; i++)
            {
                var entry = pawnKindPoolScratch[i];
                string label = entry.Kind?.LabelCap ?? PawnKindDefOf.Colonist.LabelCap;
                string pct = (entry.Weight * 100f).ToString("F0") + "%";
                result.Add(OutpostTranslationUtil.Key("TSA_WD_Recruiting_PawnKindPoolLine", label, pct));
            }
            return result;
        }
    }
}
