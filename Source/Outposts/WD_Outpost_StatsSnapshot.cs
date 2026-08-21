using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public sealed class OutpostStatRow
    {
        public string Label = "";
        public string Value = "";
        public string Tooltip = "";
        public Color? ValueColor;
        /// <summary>When set, value text wraps within the value column instead of truncating.</summary>
        public bool WrapValue;
    }

    public sealed class OutpostStatsSection
    {
        public string Title = "";
        public bool FullWidth;
        public readonly List<OutpostStatRow> Rows = new List<OutpostStatRow>();
    }

    /// <summary>Read-only aggregate of outpost metrics for the Stats inspector tab.</summary>
    public sealed class OutpostStatsSnapshot
    {
        public readonly List<OutpostStatsSection> Sections = new List<OutpostStatsSection>();

        public static OutpostStatsSnapshot Build(WorldObject worldObject)
        {
            var snap = new OutpostStatsSnapshot();
            if (worldObject == null) return snap;

            CompViralSpread comp = worldObject.GetComponent<CompViralSpread>();
            if (worldObject is WorldObject_WD_Outpost playerOutpost && playerOutpost.Faction == Faction.OfPlayer)
            {
                WorldDominationSettings settings = WorldDominationMod.settings;
                bool logisticsActive = settings != null && settings.foodLogisticsActive;
                WorldComponent_LogisticsManager manager = Find.World?.GetComponent<WorldComponent_LogisticsManager>();
                CompOutpostLogistics logi = playerOutpost.GetComponent<CompOutpostLogistics>();

                if (playerOutpost.IsMortarOutpost)
                {
                    snap.Sections.Add(BuildMortarTurretSection(playerOutpost, fullWidth: true));
                    if (AntiAirFireUtils.HasAntiAirUpgrade(playerOutpost))
                        snap.Sections.Add(BuildAntiAirTurretSection(playerOutpost, fullWidth: true));
                }
                else
                {
                    snap.Sections.Add(BuildProductionSection(playerOutpost, fullWidth: true));
                }
                snap.Sections.Add(BuildCombatSection(worldObject, comp, CombatStatsPresentation.PlayerOutpost, fullWidth: true));
                snap.Sections.Add(BuildPawnsSection(playerOutpost));
                snap.Sections.Add(BuildFoodSection(playerOutpost, manager, logi, logisticsActive));
                snap.Sections.Add(BuildRoadBuildingSection(playerOutpost, comp));
                snap.Sections.Add(BuildMiscSection(playerOutpost));
                return snap;
            }

            if (comp != null)
            {
                snap.Sections.Add(BuildCombatSection(worldObject, comp, CombatStatsPresentation.WorldSettlement, fullWidth: true));
                snap.Sections.Add(BuildWorldActionCooldownsSection(comp));
                snap.Sections.Add(BuildSettlementMiscSection(comp));
            }

            return snap;
        }

        public static OutpostStatsSnapshot Build(WorldObject_WD_Outpost outpost) => Build((WorldObject)outpost);

        private enum CombatStatsPresentation
        {
            PlayerOutpost,
            WorldSettlement,
        }

        private static OutpostStatsSection BuildPawnsSection(WorldObject_WD_Outpost outpost)
        {
            CountStoredTransport(outpost, out int animals, out int vehicles);
            int mechs = outpost.StoredMechanoidPawnCount;
            int humanoids = outpost.PawnCount;
            int total = humanoids + animals + vehicles + mechs;

            var section = new OutpostStatsSection
            {
                Title = "TSA_WD_OutpostStats_Section_Pawns".Translate().ToString()
            };
            AddRow(section, "TSA_WD_OutpostStats_Row_TotalPawns", total.ToString(), "TSA_WD_OutpostStats_Row_TotalPawnsTip");
            AddRow(section, "TSA_WD_OutpostStats_Row_Humanoids", humanoids.ToString(), "TSA_WD_OutpostStats_Row_HumanoidsTip");
            AddRow(section, "TSA_WD_OutpostStats_Row_Animals", animals.ToString(), "TSA_WD_OutpostStats_Row_AnimalsTip");
            AddRow(section, "TSA_WD_OutpostStats_Row_Vehicles", vehicles.ToString(), "TSA_WD_OutpostStats_Row_VehiclesTip");
            AddRow(section, "TSA_WD_OutpostStats_Row_Mechanoids", mechs.ToString(), "TSA_WD_OutpostStats_Row_MechanoidsTip");
            return section;
        }

        private static OutpostStatsSection BuildSkillsRows(WorldObject_WD_Outpost outpost, OutpostStatsSection section)
        {
            string skillName = WorldObject_WD_Outpost.GetRelevantSkillName(outpost.def);
            float totalRaw = GetProductionRelevantSkillSumRaw(outpost);
            float totalEffective = GetProductionRelevantSkillSum(outpost);
            int contributors = outpost.WorkerPawnCount;
            float avgRelevant = contributors > 0 ? totalRaw / contributors : 0f;

            string totalDisplay = OutpostSkillScaling.FormatRawEffective(totalRaw);
            string totalTip = "TSA_WD_OutpostStats_Row_TotalRelevantSkillTip".Translate().ToString();
            if (OutpostSkillScaling.IsDiminished(totalRaw))
                totalTip = totalTip + "\n\n" + OutpostSkillScaling.BuildBandBreakdownTip(totalRaw);

            AddRowWithLabel(section,
                "TSA_WD_OutpostStats_Row_TotalRelevantSkill".Translate(skillName).ToString(),
                totalDisplay,
                totalTip);
            AddRow(section, "TSA_WD_OutpostStats_Row_AvgRelevantSkill",
                avgRelevant.ToString("F1"),
                "TSA_WD_OutpostStats_Row_AvgRelevantSkillTip");
            if (IsConstructionOutpostRelevantSkill(outpost.def))
            {
                float cRaw = outpost.TotalConstructionSkillRaw();
                AddRow(section, "TSA_WD_OutpostStats_Row_ConstructionSkill",
                    OutpostSkillScaling.FormatRawEffective(cRaw),
                    "TSA_WD_OutpostStats_Row_ConstructionSkillTip");
            }

            return section;
        }

        /// <summary>
        /// Total / avg relevant skill rows only when production (or scavenging) actually scales with a skill.
        /// Hidden for warehouse, power plant, mortar, research, rapid response, academy, etc.
        /// </summary>
        private static bool ShouldShowProductionSkillRows(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.def == null) return false;
            if (Outpost_Production_Utils.IsScavengingOutpost(outpost.def))
                return true;
            if (!Outpost_Production_Utils.UsesPhysicalGoodsProductionSkill(outpost.def))
                return false;
            var skills = WorldObject_WD_Outpost.GetRelevantSkillDefs(outpost.def);
            return skills != null && skills.Count > 0;
        }

        private static bool IsConstructionOutpostRelevantSkill(WorldObjectDef def)
        {
            var skills = WorldObject_WD_Outpost.GetRelevantSkillDefs(def);
            if (skills == null) return false;
            for (int i = 0; i < skills.Count; i++)
            {
                if (skills[i] == SkillDefOf.Construction)
                    return true;
            }
            return false;
        }

        private static OutpostStatsSection BuildRoadBuildingSection(WorldObject_WD_Outpost outpost, CompViralSpread comp)
        {
            var section = new OutpostStatsSection
            {
                Title = "TSA_WD_OutpostStats_Section_RoadBuilding".Translate().ToString()
            };

            float construction = outpost.TotalConstructionSkill();
            float engineerRoad = OutpostExpertUtility.GetEngineerRoadSpeedBonus(outpost);
            float effectiveConstruction = construction * (1f + engineerRoad);
            var roadSkillBonuses = new List<OutpostStatsTooltipUtil.BonusLine>();
            OutpostStatsTooltipUtil.AddExpertBonusLines(outpost, ExpertEffect.RoadSpeed, roadSkillBonuses);
            var constructionRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_RoadConstructionSkill",
                effectiveConstruction.ToString("F0"),
                OutpostStatsTooltipUtil.BuildMultiplierTooltip(
                    "TSA_WD_OutpostStats_Row_RoadConstructionSkillTip".Translate().ToString(),
                    construction, "F0", roadSkillBonuses, effectiveConstruction, "F0"));
            MarkBoostedIf(constructionRow, engineerRoad > 1e-6f);

            AddRow(section, "TSA_WD_OutpostStats_Row_RoadHighestTier",
                WorldActions_Roads.GetHighestBuildableRoadTierLabel(construction),
                "TSA_WD_OutpostStats_Row_RoadHighestTierTip");
            AddRow(section, "TSA_WD_OutpostStats_Row_RoadBlockHighestKind",
                WorldActions_RoadBlocks.GetHighestBuildableKindLabel(construction),
                "TSA_WD_OutpostStats_Row_RoadBlockHighestKindTip");
            AddRow(section, "TSA_WD_OutpostStats_Row_TrapHighestKind",
                WorldActions_SpikeTraps.GetHighestBuildableKindLabel(construction),
                "TSA_WD_OutpostStats_Row_TrapHighestKindTip");
            AddRow(section, "TSA_WD_OutpostStats_Row_AtTurretHighestTier",
                WorldActions_AtTurrets.GetHighestBuildableTierLabel(construction),
                "TSA_WD_OutpostStats_Row_AtTurretHighestTierTip");

            var roadTimeDirt = AddRowReturn(section, "TSA_WD_OutpostStats_Row_RoadTimeDirt",
                FormatRoadSegmentDays(WorldActions_Roads.GetEstimatedDaysPerRoadSegment(outpost, SettlementTier.T1)),
                "TSA_WD_OutpostStats_Row_RoadTimeDirtTip".Translate().ToString());
            var roadTimeStone = AddRowReturn(section, "TSA_WD_OutpostStats_Row_RoadTimeStone",
                FormatRoadSegmentDays(WorldActions_Roads.GetEstimatedDaysPerRoadSegment(outpost, SettlementTier.T2)),
                "TSA_WD_OutpostStats_Row_RoadTimeStoneTip".Translate().ToString());
            var roadTimeAsphalt = AddRowReturn(section, "TSA_WD_OutpostStats_Row_RoadTimeAsphalt",
                FormatRoadSegmentDays(WorldActions_Roads.GetEstimatedDaysPerRoadSegment(outpost, SettlementTier.T3)),
                "TSA_WD_OutpostStats_Row_RoadTimeAsphaltTip".Translate().ToString());
            var roadTimeBlockLight = AddRowReturn(section, "TSA_WD_OutpostStats_Row_RoadTimeRoadBlockLight",
                FormatRoadSegmentDays(WorldActions_RoadBlocks.GetEstimatedDaysPerRoadBlockSegment(outpost, RoadBlockKind.Light)),
                "TSA_WD_OutpostStats_Row_RoadTimeRoadBlockTip".Translate().ToString());
            var roadTimeBlockNormal = AddRowReturn(section, "TSA_WD_OutpostStats_Row_RoadTimeRoadBlockNormal",
                FormatRoadSegmentDays(WorldActions_RoadBlocks.GetEstimatedDaysPerRoadBlockSegment(outpost, RoadBlockKind.Normal)),
                "TSA_WD_OutpostStats_Row_RoadTimeRoadBlockTip".Translate().ToString());
            var roadTimeBlockHeavy = AddRowReturn(section, "TSA_WD_OutpostStats_Row_RoadTimeRoadBlockHeavy",
                FormatRoadSegmentDays(WorldActions_RoadBlocks.GetEstimatedDaysPerRoadBlockSegment(outpost, RoadBlockKind.Heavy)),
                "TSA_WD_OutpostStats_Row_RoadTimeRoadBlockTip".Translate().ToString());
            var roadTimeSpike = AddRowReturn(section, "TSA_WD_OutpostStats_Row_RoadTimeSpikeTrapSpike",
                FormatRoadSegmentDays(WorldActions_SpikeTraps.GetEstimatedDaysPerSpikeTrapSegment(outpost, SpikeTrapKind.Spike)),
                "TSA_WD_OutpostStats_Row_RoadTimeSpikeTrapTip".Translate().ToString());
            var roadTimeCaltrops = AddRowReturn(section, "TSA_WD_OutpostStats_Row_RoadTimeSpikeTrapCaltrops",
                FormatRoadSegmentDays(WorldActions_SpikeTraps.GetEstimatedDaysPerSpikeTrapSegment(outpost, SpikeTrapKind.Caltrops)),
                "TSA_WD_OutpostStats_Row_RoadTimeSpikeTrapTip".Translate().ToString());
            var roadTimeAtLight = AddRowReturn(section, "TSA_WD_OutpostStats_Row_RoadTimeAtTurretLight",
                FormatRoadSegmentDays(WorldActions_AtTurrets.GetEstimatedDaysPerAtTurret(outpost, AtTurretTier.Light)),
                "TSA_WD_OutpostStats_Row_RoadTimeAtTurretTip".Translate().ToString());
            var roadTimeAtMedium = AddRowReturn(section, "TSA_WD_OutpostStats_Row_RoadTimeAtTurretMedium",
                FormatRoadSegmentDays(WorldActions_AtTurrets.GetEstimatedDaysPerAtTurret(outpost, AtTurretTier.Medium)),
                "TSA_WD_OutpostStats_Row_RoadTimeAtTurretTip".Translate().ToString());
            var roadTimeAtHeavy = AddRowReturn(section, "TSA_WD_OutpostStats_Row_RoadTimeAtTurretHeavy",
                FormatRoadSegmentDays(WorldActions_AtTurrets.GetEstimatedDaysPerAtTurret(outpost, AtTurretTier.Heavy)),
                "TSA_WD_OutpostStats_Row_RoadTimeAtTurretTip".Translate().ToString());
            if (engineerRoad > 1e-6f)
            {
                MarkBoosted(roadTimeDirt);
                MarkBoosted(roadTimeStone);
                MarkBoosted(roadTimeAsphalt);
                MarkBoosted(roadTimeBlockLight);
                MarkBoosted(roadTimeBlockNormal);
                MarkBoosted(roadTimeBlockHeavy);
                MarkBoosted(roadTimeSpike);
                MarkBoosted(roadTimeCaltrops);
                MarkBoosted(roadTimeAtLight);
                MarkBoosted(roadTimeAtMedium);
                MarkBoosted(roadTimeAtHeavy);
            }

            WorldDominationSettings settings = WorldDominationMod.settings;
            float baseRoadRange = settings != null ? settings.maxRoadRange : WorldDominationSettings.DefMaxRoadRange;
            float baseRoadBlockRange = settings != null ? settings.maxRoadBlockRange : WorldDominationSettings.DefMaxRoadBlockRange;
            float baseSpikeTrapRange = settings != null ? settings.maxSpikeTrapRange : WorldDominationSettings.DefMaxSpikeTrapRange;
            float baseAtTurretRange = WorldActions_AtTurrets.GetMaxRange();
            float engineerRadius = OutpostExpertUtility.GetEngineerConstructionRadiusBonus(outpost);
            float effectiveRoadRange = baseRoadRange * (1f + engineerRadius);
            float effectiveRoadBlockRange = baseRoadBlockRange * (1f + engineerRadius);
            float effectiveSpikeTrapRange = baseSpikeTrapRange * (1f + engineerRadius);
            float effectiveAtTurretRange = baseAtTurretRange * (1f + engineerRadius);
            var radiusBonuses = new List<OutpostStatsTooltipUtil.BonusLine>();
            OutpostStatsTooltipUtil.AddExpertBonusLines(outpost, ExpertEffect.ConstructionRadius, radiusBonuses);

            var roadRangeRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_RoadBuildRadius",
                effectiveRoadRange.ToString("F0"),
                OutpostStatsTooltipUtil.BuildMultiplierTooltip(
                    "TSA_WD_OutpostStats_Row_RoadBuildRadiusTip".Translate().ToString(),
                    baseRoadRange, "F0", radiusBonuses, effectiveRoadRange, "F0"));
            MarkBoostedIf(roadRangeRow, engineerRadius > 1e-6f);

            var roadBlockBuildRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_RoadBlockBuildRadius",
                effectiveRoadBlockRange.ToString("F0"),
                OutpostStatsTooltipUtil.BuildMultiplierTooltip(
                    "TSA_WD_OutpostStats_Row_RoadBlockBuildRadiusTip".Translate().ToString(),
                    baseRoadBlockRange, "F0", radiusBonuses, effectiveRoadBlockRange, "F0"));
            MarkBoostedIf(roadBlockBuildRow, engineerRadius > 1e-6f);

            var roadBlockClearRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_RoadBlockClearRadius",
                effectiveRoadBlockRange.ToString("F0"),
                OutpostStatsTooltipUtil.BuildMultiplierTooltip(
                    "TSA_WD_OutpostStats_Row_RoadBlockClearRadiusTip".Translate().ToString(),
                    baseRoadBlockRange, "F0", radiusBonuses, effectiveRoadBlockRange, "F0"));
            MarkBoostedIf(roadBlockClearRow, engineerRadius > 1e-6f);

            var spikeTrapBuildRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_SpikeTrapBuildRadius",
                effectiveSpikeTrapRange.ToString("F0"),
                OutpostStatsTooltipUtil.BuildMultiplierTooltip(
                    "TSA_WD_OutpostStats_Row_SpikeTrapBuildRadiusTip".Translate().ToString(),
                    baseSpikeTrapRange, "F0", radiusBonuses, effectiveSpikeTrapRange, "F0"));
            MarkBoostedIf(spikeTrapBuildRow, engineerRadius > 1e-6f);

            var spikeTrapClearRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_SpikeTrapClearRadius",
                effectiveSpikeTrapRange.ToString("F0"),
                OutpostStatsTooltipUtil.BuildMultiplierTooltip(
                    "TSA_WD_OutpostStats_Row_SpikeTrapClearRadiusTip".Translate().ToString(),
                    baseSpikeTrapRange, "F0", radiusBonuses, effectiveSpikeTrapRange, "F0"));
            MarkBoostedIf(spikeTrapClearRow, engineerRadius > 1e-6f);

            var atTurretBuildRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_AtTurretBuildRadius",
                effectiveAtTurretRange.ToString("F0"),
                OutpostStatsTooltipUtil.BuildMultiplierTooltip(
                    "TSA_WD_OutpostStats_Row_AtTurretBuildRadiusTip".Translate().ToString(),
                    baseAtTurretRange, "F0", radiusBonuses, effectiveAtTurretRange, "F0"));
            MarkBoostedIf(atTurretBuildRow, engineerRadius > 1e-6f);

            int atTurretUsed = AtTurretUtility.CountTurretsBuiltBySite(outpost)
                + AtTurretUtility.CountInFlightTurretCrewsFrom(outpost);
            int atTurretMax = AtTurretUtility.PlayerPerSiteMax;
            AddRow(section, "TSA_WD_OutpostStats_Row_AtTurretMax",
                atTurretUsed + "/" + atTurretMax,
                "TSA_WD_OutpostStats_Row_AtTurretMaxTip");

            string currentlyBuilding;
            if (comp != null && (comp.roadTargetTile != -1
                || WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp)
                || WorldActions_SpikeTraps.HasActiveSpikeTrapProject(comp)
                || WorldActions_AtTurrets.HasActiveAtTurretProject(comp)
                || WorldActions_Decontamination.HasActiveDecontaminationProject(comp)))
            {
                string insufficient = comp.GetInsufficientStrengthConstructionMessage();
                if (insufficient != null)
                    currentlyBuilding = insufficient;
                else if (comp.roadTargetTile != -1)
                {
                    currentlyBuilding = comp.GetActiveRoadProjectLabel()
                        + " (" + (Mathf.Min(1f, comp.roadProgress) * 100f).ToString("F0") + "%)";
                }
                else if (WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp))
                {
                    currentlyBuilding = comp.GetActiveRoadBlockProjectLabel()
                        + " (" + (Mathf.Min(1f, comp.roadBlockProgress) * 100f).ToString("F0") + "%)";
                }
                else if (WorldActions_SpikeTraps.HasActiveSpikeTrapProject(comp))
                {
                    currentlyBuilding = comp.GetActiveSpikeTrapProjectLabel()
                        + " (" + (Mathf.Min(1f, comp.spikeTrapProgress) * 100f).ToString("F0") + "%)";
                }
                else if (WorldActions_AtTurrets.HasActiveAtTurretProject(comp))
                {
                    currentlyBuilding = AtTurretUtility.LabelKey(comp.selectedAtTurretTier).Translate()
                        + " (" + (Mathf.Min(1f, comp.atTurretProgress) * 100f).ToString("F0") + "%)";
                }
                else
                {
                    currentlyBuilding = "TSA_WD_Inspect_DecontaminationBuild".Translate()
                        + " (" + (Mathf.Min(1f, comp.decontamProgress) * 100f).ToString("F0") + "%)";
                }
            }
            else
                currentlyBuilding = "TSA_WD_OutpostStats_Value_RoadNone".Translate().ToString();
            AddRow(section, "TSA_WD_OutpostStats_Row_RoadCurrentlyBuilding",
                currentlyBuilding,
                "TSA_WD_OutpostStats_Row_RoadCurrentlyBuildingTip");

            return section;
        }

        private static string FormatRoadSegmentDays(float days)
        {
            if (days < 0f) return "—";
            return "TSA_WD_Outpost_Delivery_DaysLeft".Translate(days.ToString("F2")).ToString();
        }

        private static OutpostStatsSection BuildCombatSection(
            WorldObject worldObject,
            CompViralSpread comp,
            CombatStatsPresentation presentation,
            bool fullWidth = false)
        {
            var section = new OutpostStatsSection
            {
                Title = "TSA_WD_OutpostStats_Section_Combat".Translate().ToString(),
                FullWidth = fullWidth,
            };
            if (comp == null) return section;

            WorldDominationSettings settings = WorldDominationMod.settings;
            WorldComponent_SpreadManager manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            var lookup = WorldActions_Utils.GetWorldObjectsWithCompByFaction();
            var playerOutpost = worldObject as WorldObject_WD_Outpost;

            float offCur = comp.offensiveStrength;
            float defCur = comp.defensiveStrength;
            float offMax = comp.GetMaxOffensiveStrength();
            float defMax = comp.GetBaseDefensiveStrength();
            float totalCur = offCur + defCur;
            float totalMax = offMax + defMax;

            string strengthTipKey = presentation == CombatStatsPresentation.WorldSettlement
                ? "TSA_WD_OutpostStats_Row_StrengthSettlementTip"
                : "TSA_WD_OutpostStats_Row_StrengthTip";
            var strengthRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_Strength",
                totalCur.ToString("F0") + " / " + totalMax.ToString("F0"),
                strengthTipKey.Translate().ToString());

            string offTipKey = presentation == CombatStatsPresentation.WorldSettlement
                ? "TSA_WD_OutpostStats_Row_OffensiveStrengthSettlementTip"
                : "TSA_WD_OutpostStats_Row_OffensiveStrengthTip";
            var offRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_OffensiveStrength",
                offCur.ToString("F0") + " / " + offMax.ToString("F0"),
                offTipKey.Translate().ToString());

            float retainFloor = WorldActions_Utils.GetGarrisonRetainFloor(comp, settings);
            AddRow(section, "TSA_WD_OutpostStats_Row_MinGarrison",
                retainFloor.ToString("F0"),
                presentation == CombatStatsPresentation.WorldSettlement
                    ? "TSA_WD_OutpostStats_Row_MinGarrisonSettlementTip"
                    : "TSA_WD_OutpostStats_Row_MinGarrisonTip");

            var defRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_DefensiveStrength",
                defCur.ToString("F0") + " / " + defMax.ToString("F0"),
                "");
            float defUpgradeFlat = 0f;
            if (presentation == CombatStatsPresentation.PlayerOutpost && playerOutpost != null)
            {
                float baseDef = settings != null ? settings.playerOutpostBaseDefensiveStrength : 100f;
                var flatBonuses = new List<OutpostStatsTooltipUtil.BonusLine>();
                OutpostStatsTooltipUtil.AddUpgradeFlatLines(playerOutpost, d => d.defensiveStrengthBonus, flatBonuses);
                defUpgradeFlat = playerOutpost.GetOutpostUpgradeDefensiveBonus();
                defRow.Tooltip = OutpostStatsTooltipUtil.BuildFlatAdditionTooltip(
                    "TSA_WD_OutpostStats_Row_DefensiveStrengthTip".Translate().ToString(),
                    baseDef, "F0", flatBonuses, defMax, "F0");
                MarkBoostedIf(defRow, defUpgradeFlat > 1e-6f);
            }
            else
            {
                defRow.Tooltip = "TSA_WD_OutpostStats_Row_DefensiveStrengthTip".Translate().ToString();
            }

            if (presentation == CombatStatsPresentation.PlayerOutpost)
            {
                float offRecResult = comp.GetInspectDailyOffensiveRecovery();
                float offRecUpgrade = 0f;
                float offRecExpert = 0f;
                float rrBonus = 0f;
                var offRecBonuses = new List<OutpostStatsTooltipUtil.BonusLine>();
                if (playerOutpost != null)
                {
                    offRecUpgrade = playerOutpost.GetOutpostOffensiveRecoveryUpgradeMultiplierBonus();
                    OutpostStatsTooltipUtil.AddUpgradePercentLines(playerOutpost,
                        d => d.offensiveRecoveryBonus, offRecBonuses);
                    offRecExpert = playerOutpost.GetOutpostExpertOffensiveRecoveryMultiplierBonus();
                    OutpostStatsTooltipUtil.AddExpertBonusLines(playerOutpost, ExpertEffect.OffensiveRecovery, offRecBonuses);
                    if (playerOutpost.IsRapidResponseOutpost)
                    {
                        rrBonus = playerOutpost.GetRapidResponseOffensiveRecoveryBonus();
                        if (rrBonus > 0.001f)
                        {
                            offRecBonuses.Add(new OutpostStatsTooltipUtil.BonusLine
                            {
                                Source = "TSA_WD_OutpostStats_RRRecoveryMutator".Translate().ToString(),
                                Fraction = rrBonus
                            });
                        }
                    }

                    float rrStrBonus = playerOutpost.GetRapidResponseOffensiveStrengthBonus();
                    MarkBoostedIf(strengthRow, rrStrBonus > 1e-6f || defUpgradeFlat > 1e-6f);
                    MarkBoostedIf(offRow, rrStrBonus > 1e-6f);
                }
                float offRecMult = 1f + offRecUpgrade + offRecExpert + rrBonus;
                float offRecBaseline = offRecMult > 1e-6f ? offRecResult / offRecMult : offRecResult;
                var offRecRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_OffensiveRecovery",
                    "+" + offRecResult.ToString("F0"),
                    OutpostStatsTooltipUtil.BuildMultiplierTooltip(
                        "TSA_WD_OutpostStats_Row_OffensiveRecoveryTip".Translate().ToString(),
                        offRecBaseline, "F0", offRecBonuses, offRecResult, "F0"));
                MarkBoostedIf(offRecRow, offRecUpgrade + offRecExpert + rrBonus > 1e-6f);
            }

            float defRecResult = comp.GetInspectDailyDefensiveRecovery();
            var defRecBonuses = new List<OutpostStatsTooltipUtil.BonusLine>();
            float defRecExpert = 0f;
            if (presentation == CombatStatsPresentation.PlayerOutpost && playerOutpost != null)
            {
                defRecExpert = playerOutpost.GetOutpostDefensiveRecoveryMultiplierBonus();
                OutpostStatsTooltipUtil.AddExpertBonusLines(playerOutpost, ExpertEffect.DefensiveRecovery, defRecBonuses);
            }
            float defRecMult = 1f + defRecExpert;
            float defRecBaseline = defRecMult > 1e-6f ? defRecResult / defRecMult : defRecResult;
            var defRecRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_DefensiveRecovery",
                "+" + defRecResult.ToString("F0"),
                OutpostStatsTooltipUtil.BuildMultiplierTooltip(
                    "TSA_WD_OutpostStats_Row_DefensiveRecoveryTip".Translate().ToString(),
                    defRecBaseline, "F0", defRecBonuses, defRecResult, "F0"));
            MarkBoostedIf(defRecRow, defRecExpert > 1e-6f);

            if (presentation == CombatStatsPresentation.PlayerOutpost && playerOutpost != null)
            {
                WorldDominationSettings healSettings = WorldDominationMod.settings;
                float healBase = healSettings?.outpostOccupantHealSeverityPerDay ?? WorldDominationSettings.DefOutpostOccupantHealSeverityPerDay;
                float healUpgrade = playerOutpost.GetHospitalOccupantHealMultiplierBonus();
                float healExpert = playerOutpost.GetOutpostExpertOccupantHealMultiplierBonus();
                float healPerDay = playerOutpost.GetEffectiveOccupantHealSeverityPerDay();
                var healBonuses = new List<OutpostStatsTooltipUtil.BonusLine>();
                OutpostStatsTooltipUtil.AddUpgradePercentLines(playerOutpost,
                    d => d.category == OutpostUpgradeCategory.Hospital ? d.offensiveRecoveryBonus : 0f, healBonuses);
                OutpostStatsTooltipUtil.AddExpertBonusLines(playerOutpost, ExpertEffect.OccupantHeal, healBonuses);
                var healRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_OccupantHeal",
                    healPerDay.ToString("F2"),
                    OutpostStatsTooltipUtil.BuildMultiplierTooltip(
                        "TSA_WD_OutpostStats_Row_OccupantHealTip".Translate().ToString(),
                        healBase, "F2", healBonuses, healPerDay, "F2"));
                MarkBoostedIf(healRow, healUpgrade + healExpert > 1e-6f);

                float resistBase = OutpostPrisonerResistanceScaling.GetBaseDropPerDay(playerOutpost);
                float resistBonus = OutpostPrisonerResistanceScaling.GetWardenBonusFraction(playerOutpost);
                float resistDaily = OutpostPrisonerResistanceScaling.GetDailyDrop(playerOutpost);
                var resistRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_PrisonerResistance",
                    "-" + resistDaily.ToString("F1"),
                    OutpostPrisonerResistanceScaling.BuildStatsTabTooltip(playerOutpost));
                MarkBoostedIf(resistRow, resistBonus > 1e-6f && resistBase > 1e-6f);
            }

            AppendCombatRaidRows(section, worldObject, comp, settings, manager, lookup, presentation);

            if (presentation == CombatStatsPresentation.PlayerOutpost && playerOutpost != null && playerOutpost.IsRapidResponseOutpost)
            {
                float capBonus = playerOutpost.GetRapidResponseOffensiveStrengthBonus() * 100f;
                float recBonus = playerOutpost.GetRapidResponseOffensiveRecoveryBonus() * 100f;
                var rrStrRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_RRStrengthBonus",
                    capBonus.ToString("F0") + "%",
                    "TSA_WD_OutpostStats_Row_RRStrengthBonusTip".Translate().ToString());
                MarkBoostedIf(rrStrRow, capBonus > 1e-6f);
                var rrRecRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_RRRecoveryBonus",
                    recBonus.ToString("F0") + "%",
                    "TSA_WD_OutpostStats_Row_RRRecoveryBonusTip".Translate().ToString());
                MarkBoostedIf(rrRecRow, recBonus > 1e-6f);
            }

            return section;
        }

        private static OutpostStatsSection BuildCombatSection(WorldObject_WD_Outpost outpost, CompViralSpread comp)
            => BuildCombatSection(outpost, comp, CombatStatsPresentation.PlayerOutpost);

        private static void AppendCombatRaidRows(
            OutpostStatsSection section,
            WorldObject worldObject,
            CompViralSpread comp,
            WorldDominationSettings settings,
            WorldComponent_SpreadManager manager,
            Dictionary<Faction, List<WorldObject>> lookup,
            CombatStatsPresentation presentation)
        {
            if (settings == null || worldObject == null) return;

            if (presentation == CombatStatsPresentation.PlayerOutpost && worldObject is WorldObject_WD_Outpost playerOutpost)
            {
                float baseRange = settings.raidTargetRadius;
                float strategistBonus = OutpostExpertUtility.GetStrategistAttackRangeBonusFraction(playerOutpost);
                float effectiveRange = baseRange * (1f + strategistBonus);
                var attackBonuses = new List<OutpostStatsTooltipUtil.BonusLine>();
                OutpostStatsTooltipUtil.AddExpertBonusLines(playerOutpost, ExpertEffect.AttackRange, attackBonuses);
                var attackRangeRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_AttackRange",
                    effectiveRange.ToString("F0"),
                    OutpostStatsTooltipUtil.BuildMultiplierTooltip(
                        "TSA_WD_OutpostStats_Row_AttackRangeTip".Translate().ToString(),
                        baseRange, "F0", attackBonuses, effectiveRange, "F0"));
                MarkBoostedIf(attackRangeRow, strategistBonus > 1e-6f);
            }
            else if (presentation == CombatStatsPresentation.WorldSettlement
                && worldObject is Settlement settlement
                && settlement.Faction != null
                && !settlement.Faction.IsPlayer
                && WorldActions_Utils.IsWdSurfaceTile(settlement.Tile))
            {
                float range = SettlementAttackRangeUtil.GetNpcSettlementAttackRangeWithZeal(settlement, settings, manager);
                bool hasZeal = manager != null
                    && settlement.Faction == manager.expansionistZealFaction
                    && Find.TickManager.TicksGame < manager.expansionistZealExpiryTick;
                string value = hasZeal
                    ? "TSA_WD_OutpostStats_Row_AttackRangeZealValue".Translate(range.ToString("F0")).ToString()
                    : range.ToString("F0");
                var attackRangeRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_AttackRange",
                    value,
                    SettlementAttackRangeUtil.BuildSettlementAttackRangeTooltip(settlement, settings, manager, hasZeal, range));
                MarkBoostedIf(attackRangeRow, hasZeal);

                AppendNpcT4TurretStatRows(section, settlement, comp, settings);
            }

            string attackCdValue = FormatAttackCooldownValue(comp);
            Color? attackCdColor = comp.IsRaidOnCooldown ? Color.yellow : Color.green;
            AddRow(section, "TSA_WD_OutpostStats_Row_AttackCooldown", attackCdValue,
                "TSA_WD_OutpostStats_Row_AttackCooldownTip", attackCdColor);

            string defenseCdValue = FormatDefenseCooldownValue(comp);
            Color? defenseCdColor = comp.IsDefenseOnCooldown ? Color.green : CompViralSpread.RaidVulnerableColor;
            AddRow(section, "TSA_WD_OutpostStats_Row_RaidProtectionCooldown", defenseCdValue,
                "TSA_WD_OutpostStats_Row_RaidProtectionCooldownTip", defenseCdColor);

            float allyRadius = AllyRadiusPreview.GetRadius(worldObject, settings, manager);
            var allyPreview = AllyRadiusPreview.Build(worldObject, settings, manager);
            float allyStr = allyPreview.TotalStrength;
            float tunnelBonus = AllyRadiusUtil.GetTunnelBonus(worldObject);

            var allyRadiusRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_AllyRadius",
                allyRadius.ToString("F0"),
                AllyRadiusUtil.BuildTooltip(worldObject, settings, manager));
            MarkBoostedIf(allyRadiusRow, tunnelBonus > 1e-6f);
            AddRowRawTip(section, "TSA_WD_OutpostStats_Row_AllyStrength",
                allyStr.ToString("F0"),
                AppendAllyPreviewTip("TSA_WD_OutpostStats_Row_AllyStrengthTip".Translate().ToString(), allyPreview.tooltip));

            float localDefensePower = comp.GetTotalLocalDefensePower();
            float totalDefense = localDefensePower + allyStr;
            AddRowRawTip(section, "TSA_WD_OutpostStats_Row_TotalDefenseStrength",
                totalDefense.ToString("F0"),
                BuildTotalDefenseStrengthTip(comp, allyStr));

            float selfAttack = WorldActions_Utils.GetAvailableRaidStrength(comp, settings);
            float totalAttack = selfAttack + allyStr;
            AddRowRawTip(section, "TSA_WD_OutpostStats_Row_TotalAttackStrength",
                totalAttack.ToString("F0"),
                BuildTotalAttackStrengthTip(selfAttack, allyStr));
        }

        /// <summary>Static T4 NPC mortar/AA range and nominal cooldown rows (not remaining cooldown).</summary>
        private static void AppendNpcT4TurretStatRows(
            OutpostStatsSection section,
            Settlement settlement,
            CompViralSpread comp,
            WorldDominationSettings settings)
        {
            if (settlement == null || comp == null || settings == null) return;
            if (comp.tier != SettlementTier.T4) return;

            bool mortarEligible = settings.enableNpcT4Mortar;
            bool aaEligible = settings.enableNpcT4AntiAir;
            if (!mortarEligible && !aaEligible) return;

            TechLevel minTech = settings.npcT4MortarMinTechLevel;
            if (settlement.Faction?.def == null || settlement.Faction.def.techLevel < minTech)
                return;

            if (mortarEligible)
            {
                float mortarRange = settings.npcMortarRange;
                AddRow(section, "TSA_WD_OutpostStats_Row_MortarRange",
                    mortarRange.ToString("F0"),
                    "TSA_WD_OutpostStats_Row_NpcMortarRangeTip");
                AddRow(section, "TSA_WD_OutpostStats_Row_MortarCooldown",
                    settings.npcMortarCooldownDays.ToString("F1"),
                    "TSA_WD_OutpostStats_Row_NpcMortarCooldownTip");
            }

            if (aaEligible)
            {
                float aaRange = AntiAirFireUtils.GetNpcAntiAirMaxRangeTiles();
                AddRow(section, "TSA_WD_OutpostStats_Row_AntiAirRange",
                    aaRange.ToString("F0"),
                    "TSA_WD_OutpostStats_Row_NpcAntiAirRangeTip");
                AddRow(section, "TSA_WD_OutpostStats_Row_AntiAirCooldown",
                    settings.npcAntiAirCooldownSeconds.ToString("F0"),
                    "TSA_WD_OutpostStats_Row_NpcAntiAirCooldownTip");
            }
        }

        private static string AppendAllyPreviewTip(string baseTip, string allyLines)
        {
            if (string.IsNullOrEmpty(allyLines)) return baseTip ?? "";
            var sb = new StringBuilder(baseTip ?? "");
            if (sb.Length > 0) sb.AppendLine().AppendLine();
            sb.Append(allyLines);
            return sb.ToString();
        }

        private static string BuildTotalDefenseStrengthTip(CompViralSpread comp, float allyDefense)
        {
            return "TSA_WD_OutpostStats_Row_TotalDefenseStrengthTip".Translate(
                comp.defensiveStrength.ToString("F0"),
                comp.offensiveStrength.ToString("F0"),
                allyDefense.ToString("F0")).ToString();
        }

        private static string BuildTotalAttackStrengthTip(float selfAttack, float allyAttack)
        {
            return "TSA_WD_OutpostStats_Row_TotalAttackStrengthTip".Translate(
                selfAttack.ToString("F0"),
                allyAttack.ToString("F0")).ToString();
        }

        private static string FormatAttackCooldownValue(CompViralSpread comp)
        {
            if (comp == null || !comp.IsRaidOnCooldown)
                return "TSA_WD_OutpostStats_Value_ReadyToAttack".Translate().ToString();
            return FormatCooldownDaysRemaining(comp.raidCooldownTick);
        }

        private static string FormatDefenseCooldownValue(CompViralSpread comp)
        {
            if (comp == null || !comp.IsDefenseOnCooldown)
                return "TSA_WD_OutpostStats_Value_CanBeAttacked".Translate().ToString();
            return FormatCooldownDaysRemaining(comp.defenseCooldownTick);
        }

        private static string FormatCooldownDaysRemaining(int cooldownEndTick)
        {
            float days = Mathf.Max(0f, (cooldownEndTick - Find.TickManager.TicksGame) / 60000f);
            return "TSA_WD_Outpost_Delivery_DaysLeft".Translate(days.ToString("F1")).ToString();
        }

        /// <summary>Remaining days and settings nominal, e.g. "0.6 Days (1.0 Days)".</summary>
        private static string FormatRemainingAndNominalDays(int cooldownEndTick, float nominalDays)
        {
            float remaining = 0f;
            if (cooldownEndTick > 0)
                remaining = Mathf.Max(0f, (cooldownEndTick - Find.TickManager.TicksGame) / 60000f);
            return "TSA_WD_OutpostStats_Value_CooldownRemainingNominal".Translate(
                remaining.ToString("F1"),
                Mathf.Max(0f, nominalDays).ToString("F1")).ToString();
        }

        /// <summary>Ready label with configured CD, e.g. "Ready to Send Trader (CD: 2.0 Days)".</summary>
        private static string FormatReadyWithConfiguredCd(string readyValueKey, float nominalDays)
            => "TSA_WD_OutpostStats_Value_ReadyWithCd".Translate(
                readyValueKey.Translate().ToString(),
                Mathf.Max(0f, nominalDays).ToString("F1")).ToString();

        private static bool IsCooldownTickActive(int cooldownEndTick)
            => cooldownEndTick > 0 && Find.TickManager.TicksGame < cooldownEndTick;

        private static string WorldActionCooldownTip(string baseTipKey)
        {
            return baseTipKey.Translate().ToString()
                + "\n\n"
                + "TSA_WD_OutpostStats_Tip_CooldownNominalInBrackets".Translate();
        }

        private static void AddWorldActionCooldownRow(
            OutpostStatsSection section,
            string labelKey,
            string readyValueKey,
            string tipKey,
            int cooldownEndTick,
            float nominalDays,
            bool invertColors = false)
        {
            bool onCd = IsCooldownTickActive(cooldownEndTick);
            string value = onCd
                ? FormatRemainingAndNominalDays(cooldownEndTick, nominalDays)
                : FormatReadyWithConfiguredCd(readyValueKey, nominalDays);
            // Actor actions: green when ready, yellow on CD.
            // Defense / incident (negative events): yellow when exposed, green while protected.
            Color color = invertColors
                ? (onCd ? Color.green : Color.yellow)
                : (onCd ? Color.yellow : Color.green);
            AddRowRawTip(section, labelKey, value, WorldActionCooldownTip(tipKey));
            if (section.Rows.Count > 0)
                section.Rows[section.Rows.Count - 1].ValueColor = color;
        }

        /// <summary>Short actor cooldowns shown in minutes (Feature C origin ambush: 3 min at 1x).</summary>
        private static void AddWorldActionCooldownRowMinutes(
            OutpostStatsSection section,
            string labelKey,
            string readyValueKey,
            string tipKey,
            int cooldownEndTick,
            int nominalTicks)
        {
            bool onCd = IsCooldownTickActive(cooldownEndTick);
            float remainingMin = 0f;
            if (onCd)
                remainingMin = Mathf.Max(0f, (cooldownEndTick - Find.TickManager.TicksGame) / 3600f);
            float nominalMin = Mathf.Max(0f, nominalTicks / 3600f);
            string value = onCd
                ? "TSA_WD_OutpostStats_Value_CooldownRemainingNominalMinutes".Translate(
                    remainingMin.ToString("F1"), nominalMin.ToString("F1")).ToString()
                : "TSA_WD_OutpostStats_Value_ReadyWithCdMinutes".Translate(
                    readyValueKey.Translate().ToString(), nominalMin.ToString("F1")).ToString();
            AddRowRawTip(section, labelKey, value, tipKey.Translate().ToString());
            if (section.Rows.Count > 0)
                section.Rows[section.Rows.Count - 1].ValueColor = onCd ? Color.yellow : Color.green;
        }

        /// <summary>NPC settlement world-action per-type cooldowns (remaining and settings nominal).</summary>
        private static OutpostStatsSection BuildWorldActionCooldownsSection(CompViralSpread comp)
        {
            var section = new OutpostStatsSection
            {
                Title = "TSA_WD_OutpostStats_Section_WorldActionCooldowns".Translate().ToString(),
                FullWidth = true
            };
            if (comp == null) return section;

            var seth = WorldDominationMod.settings;
            float roadNominal = seth?.cooldownGrowDays ?? WorldDominationSettings.DefCdGrowDays;
            float expandNominal = seth?.cooldownExpandDays ?? WorldDominationSettings.DefCdExpandDays;
            float raidNominal = seth?.cooldownRaidDays ?? WorldDominationSettings.DefCdRaidDays;
            float defenseNominal = seth?.cooldownBeingRaidedDays ?? WorldDominationSettings.DefCdBeingRaidedDays;
            float traderNominal = seth?.cooldownTraderDays ?? WorldDominationSettings.DefCdTraderDays;
            float fortifyNominal = seth?.cooldownFortifyDays ?? WorldDominationSettings.DefCdFortifyDays;
            float incidentNominal = seth?.cooldownIncidentDays ?? WorldDominationSettings.DefCdIncidentDays;

            // Order matches World Actions cooldowns; incident stays last.
            AddWorldActionCooldownRow(section, "TSA_WD_Daily_CdGrow",
                "TSA_WD_OutpostStats_Value_ReadyToBuildRoad", "TSA_WD_Daily_CdGrowTip",
                comp.roadCooldownTick, roadNominal);
            AddWorldActionCooldownRow(section, "TSA_WD_Daily_CdExpand",
                "TSA_WD_OutpostStats_Value_ReadyToExpand", "TSA_WD_Daily_CdExpandTip",
                comp.expansionCooldownTick, expandNominal);
            AddWorldActionCooldownRow(section, "TSA_WD_Daily_CdRaid",
                "TSA_WD_OutpostStats_Value_ReadyToRaid", "TSA_WD_Daily_CdRaidTip",
                comp.raidCooldownTick, raidNominal);
            AddWorldActionCooldownRow(section, "TSA_WD_Daily_CdBeingRaided",
                "TSA_WD_OutpostStats_Value_ReadyCanBeRaided", "TSA_WD_Daily_CdBeingRaidedTip",
                comp.defenseCooldownTick, defenseNominal, invertColors: true);
            AddWorldActionCooldownRow(section, "TSA_WD_Daily_CdTrader",
                "TSA_WD_OutpostStats_Value_ReadyToSendTrader", "TSA_WD_Daily_CdTraderTip",
                comp.traderCooldownTick, traderNominal);
            if (seth != null && seth.experimentalSettlementAmbush
                && comp.tier >= seth.settlementAmbushMinTier)
                AddWorldActionCooldownRowMinutes(section, "TSA_WD_OutpostStats_Row_AmbushInterception",
                    "TSA_WD_OutpostStats_Value_ReadyToAmbush", "TSA_WD_OutpostStats_Row_AmbushInterceptionTip",
                    comp.ambushCooldownTick, SettlementAmbushUtility.OriginCooldownTicks);
            AddWorldActionCooldownRow(section, "TSA_WD_Daily_CdFortify",
                "TSA_WD_OutpostStats_Value_ReadyToFortify", "TSA_WD_Daily_CdFortifyTip",
                comp.fortifyCooldownTick, fortifyNominal);
            AddWorldActionCooldownRow(section, "TSA_WD_OutpostStats_Row_MayExperienceIncident",
                "TSA_WD_OutpostStats_Value_ReadyForIncidents", "TSA_WD_Daily_CdIncidentTip",
                comp.incidentCooldownTick, incidentNominal, invertColors: true);
            return section;
        }

        /// <summary>Settlement misc: NPC auto-decontamination cadence.</summary>
        private static OutpostStatsSection BuildSettlementMiscSection(CompViralSpread comp)
        {
            var section = new OutpostStatsSection
            {
                Title = "TSA_WD_OutpostStats_Section_Misc".Translate().ToString(),
                FullWidth = true
            };
            if (comp == null) return section;

            float decontamNominal = CompViralSpread.NpcAutoDecontamIntervalDays;
            AddWorldActionCooldownRow(section, "TSA_WD_OutpostStats_Row_DecontaminationCooldown",
                "TSA_WD_OutpostStats_Value_ReadyToDecontaminate", "TSA_WD_OutpostStats_Row_DecontaminationCooldownTip",
                comp.NpcDecontamAssessCooldownEndTick, decontamNominal);
            return section;
        }

        /// <summary>Player outpost misc: decontamination prep time and range.</summary>
        private static OutpostStatsSection BuildMiscSection(WorldObject_WD_Outpost outpost)
        {
            var section = new OutpostStatsSection
            {
                Title = "TSA_WD_OutpostStats_Section_Misc".Translate().ToString()
            };

            float engineerRoad = OutpostExpertUtility.GetEngineerRoadSpeedBonus(outpost);
            var roadTimeDecontam = AddRowReturn(section, "TSA_WD_OutpostStats_Row_RoadTimeDecontamination",
                FormatRoadSegmentDays(WorldActions_Decontamination.GetEstimatedDaysPerSegment(outpost)),
                "TSA_WD_OutpostStats_Row_RoadTimeDecontaminationTip".Translate().ToString());
            if (engineerRoad > 1e-6f)
                MarkBoosted(roadTimeDecontam);

            WorldDominationSettings settings = WorldDominationMod.settings;
            float baseDecontamRange = settings != null ? settings.maxDecontaminationRange : WorldDominationSettings.DefMaxDecontaminationRange;
            float engineerRadius = OutpostExpertUtility.GetEngineerConstructionRadiusBonus(outpost);
            float effectiveDecontamRange = baseDecontamRange * (1f + engineerRadius);
            var radiusBonuses = new List<OutpostStatsTooltipUtil.BonusLine>();
            OutpostStatsTooltipUtil.AddExpertBonusLines(outpost, ExpertEffect.ConstructionRadius, radiusBonuses);

            var decontamRangeRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_DecontaminationRadius",
                effectiveDecontamRange.ToString("F0"),
                OutpostStatsTooltipUtil.BuildMultiplierTooltip(
                    "TSA_WD_OutpostStats_Row_DecontaminationRadiusTip".Translate().ToString(),
                    baseDecontamRange, "F0", radiusBonuses, effectiveDecontamRange, "F0"));
            MarkBoostedIf(decontamRangeRow, engineerRadius > 1e-6f);

            return section;
        }

        /// <summary>Public so the Food tab can render the identical food breakdown in its left column.</summary>
        public static OutpostStatsSection BuildFoodSection(
            WorldObject_WD_Outpost outpost,
            WorldComponent_LogisticsManager manager,
            CompOutpostLogistics logi,
            bool logisticsActive)
        {
            var section = new OutpostStatsSection
            {
                Title = "TSA_WD_OutpostStats_Section_Food".Translate().ToString()
            };

            var settings = WorldDominationMod.settings;
            float baseProd = settings?.foodProductionPerOutpostBase ?? WorldDominationSettings.DefFoodProductionPerOutpostBase;
            float consumptionPerPawn = settings?.foodConsumptionPerPawn ?? WorldDominationSettings.DefFoodConsumptionPerPawn;
            float demand = manager?.GetDailyDemand(outpost) ?? 0f;
            float dailyProd = manager?.GetDailyProduction(outpost) ?? baseProd;
            bool isFoodProducer = Outpost_Production_Utils.IsFoodProducerOutpost(outpost.def);
            int eatingPawns = outpost.CountOccupantsConsumingFood();

            string demandTip = "TSA_WD_OutpostStats_Row_FoodDemandTip".Translate().ToString();
            if (demandTip.Contains("TSA_WD_")) demandTip = "Total virtual food consumed per day at this outpost.";
            string demandBreakdown = "TSA_WD_OutpostStats_Row_FoodDemandBreakdown".Translate(eatingPawns.ToString(), consumptionPerPawn.ToString("F1"), demand.ToString("F1")).ToString();
            if (demandBreakdown.Contains("TSA_WD_"))
                demandBreakdown = eatingPawns + " eating pawns × " + consumptionPerPawn.ToString("F1") + " food needed per day = " + demand.ToString("F1");
            demandTip = demandTip + "\n\n" + demandBreakdown;

            if (!logisticsActive)
            {
                AddRowRawTip(section, "TSA_WD_OutpostStats_Row_FoodDemand", demand.ToString("F1"), demandTip);
                AddBaseFoodProductionRow(section, outpost, baseProd);
                return section;
            }

            float netLocal = dailyProd - demand;
            Color netCol = netLocal > 0.01f ? Color.green : (netLocal < -0.01f ? Color.red : Color.yellow);

            AddRowRawTip(section, "TSA_WD_OutpostStats_Row_FoodDemand", demand.ToString("F1"), demandTip);
            AddBaseFoodProductionRow(section, outpost, baseProd);

            string prodTipKey = "TSA_WD_OutpostStats_Row_FoodProductionTip";
            string prodTip = prodTipKey.Translate().ToString();
            if (prodTip == prodTipKey || prodTip.Contains("TSA_WD_"))
                prodTip = "Total virtual food produced per day at this outpost.";
            if (isFoodProducer)
            {
                string skillName = WorldObject_WD_Outpost.GetRelevantSkillName(outpost.def);
                float totalSkillRaw = Outpost_Production_Utils.IsFarmingOutpost(outpost.def)
                    ? outpost.TotalPlantsSkillRaw()
                    : outpost.TotalHuntingSkillRaw();
                float totalSkillEff = OutpostSkillScaling.ToEffective(totalSkillRaw);
                float tileMult = WorldComponent_LogisticsManager.GetVirtualFoodEffectiveTileMultiplier(outpost);
                int tilePct = Mathf.RoundToInt(tileMult * 100f);
                string tileStatLabel = Outpost_Production_Utils.IsHuntingOutpost(outpost.def)
                    ? "TSA_WD_Biome_ColAnimals".Translate().ToString()
                    : (Outpost_Production_Utils.IsFishingOutpost(outpost.def)
                        ? "TSA_WD_Biome_ColFish".Translate().ToString()
                        : "TSA_WD_Biome_ColFertility".Translate().ToString());
                string extraKey = "TSA_WD_OutpostStats_Row_FoodProductionHubTip";
                string extra = extraKey.Translate(
                    skillName,
                    totalSkillEff.ToString("F0"),
                    dailyProd.ToString("F1"),
                    tilePct.ToString(),
                    tileStatLabel,
                    totalSkillRaw.ToString("F0")).ToString();
                if (extra == extraKey || extra.Contains("TSA_WD_"))
                    extra = "Food hub: base food production + (" + totalSkillEff.ToString("F0") + " effective " + skillName
                        + " × " + tilePct + "% " + tileStatLabel + ") = " + dailyProd.ToString("F1") + " total."
                        + (OutpostSkillScaling.IsDiminished(totalSkillRaw)
                            ? " Raw skill: " + totalSkillRaw.ToString("F0") + "."
                            : "");
                if (OutpostSkillScaling.IsDiminished(totalSkillRaw))
                    extra = extra + "\n\n" + OutpostSkillScaling.BuildBandBreakdownTip(totalSkillRaw);
                prodTip = prodTip + "\n\n" + extra;
                string softTip = Outpost_Production_Utils.BuildSoftProductionBonusTooltip(outpost);
                if (!string.IsNullOrEmpty(softTip))
                    prodTip = prodTip + "\n\n" + softTip;
                string softSuffix = Outpost_Production_Utils.BuildSoftProductionBonusSuffix(outpost);
                if (!string.IsNullOrEmpty(softSuffix))
                    prodTip = prodTip + "\n" + softSuffix.Trim();
            }
            else
            {
                string baseOnlyKey = "TSA_WD_OutpostStats_Row_FoodProductionBaseOnlyTip";
                string baseOnly = baseOnlyKey.Translate(consumptionPerPawn.ToString("F1"), eatingPawns.ToString()).ToString();
                if (baseOnly == baseOnlyKey || baseOnly.Contains("TSA_WD_"))
                    baseOnly = "Non-hub outposts do not add skill or tile production (base food production still includes outpost upgrades).";
                prodTip = prodTip + "\n\n" + baseOnly;
            }

            AddRowRawTip(section, "TSA_WD_OutpostStats_Row_FoodProduction", dailyProd.ToString("F1"), prodTip);
            if (isFoodProducer && outpost != null)
            {
                bool foodTileBoosted =
                    (Outpost_Production_Utils.IsFarmingOutpost(outpost.def) || Outpost_Production_Utils.IsRanchOutpost(outpost.def))
                        && outpost.GetBuiltUpgradeTileFertilityBonus() > 1e-6f
                    || Outpost_Production_Utils.IsHuntingOutpost(outpost.def)
                        && outpost.GetBuiltUpgradeTileAnimalAbundanceBonus() > 1e-6f
                    || Outpost_Production_Utils.IsFishingOutpost(outpost.def)
                        && outpost.GetBuiltUpgradeTileFishAbundanceBonus() > 1e-6f;
                bool softBoosted = OutpostWarehouseAuraUtility.GetExpertAndWarehouseProductionBonusFraction(outpost) > 1e-6f;
                if (foodTileBoosted || softBoosted)
                    MarkBoosted(section.Rows[section.Rows.Count - 1]);
            }
            AddRow(section, "TSA_WD_OutpostStats_Row_FoodNetLocal",
                netLocal.ToString("F1"),
                "TSA_WD_OutpostStats_Row_FoodNetLocalTip",
                valueColor: netCol);

            if (logi != null)
            {
                float maxFood = CompOutpostLogistics.GetEffectiveMaxFoodFor(outpost);
                Color storageCol = logi.currentFood <= maxFood * 0.2f ? Color.red
                    : (logi.currentFood <= maxFood * 0.5f ? Color.yellow : Color.green);
                AddRow(section, "TSA_WD_OutpostStats_Row_FoodStorage",
                    logi.currentFood.ToString("F1") + " / " + maxFood.ToString("F0"),
                    "TSA_Logistics_CurrentMaxFood_Tooltip",
                    valueColor: storageCol);
            }

            if (isFoodProducer)
            {
                float outgoing = manager?.GetOutgoingManualSumForTile(outpost.Tile) ?? 0f;
                AddRow(section, "TSA_WD_OutpostStats_Row_FoodOutgoing", outgoing.ToString("F1"), "TSA_WD_OutpostStats_Row_FoodOutgoingTip", valueColor: Color.red);
            }
            else
            {
                float incoming = manager?.GetIncomingManualWeightedSumForTile(outpost.Tile) ?? 0f;
                AddRow(section, "TSA_WD_OutpostStats_Row_FoodIncoming", incoming.ToString("F1"), "TSA_WD_OutpostStats_Row_FoodIncomingTip", valueColor: Color.green);
            }

            float netLogistics = manager?.GetLogisticsNetDailyForOutpost(outpost) ?? 0f;
            string netSign = netLogistics >= 0f ? "+" : "";
            Color netLogisticsCol = netLogistics > 0.1f ? Color.green : (netLogistics < -0.1f ? Color.red : Color.yellow);
            AddRow(section, "TSA_Logistics_FoodChangePerDay_Label",
                netSign + netLogistics.ToString("F1"),
                "TSA_Logistics_FoodChangePerDay_Tooltip",
                valueColor: netLogisticsCol);

            return section;
        }

        /// <summary>
        /// Settings base plus flat upgrade production (Hydroponics Basins). Same addend as
        /// <see cref="WorldComponent_LogisticsManager"/> daily production.
        /// </summary>
        private static void AddBaseFoodProductionRow(OutpostStatsSection section, WorldObject_WD_Outpost outpost, float settingsBase)
        {
            float upgradeFlat = outpost != null ? outpost.GetBuiltUpgradeFoodProductionFlatBonus() : 0f;
            float effectiveBase = settingsBase + upgradeFlat;
            string intro = "TSA_WD_OutpostStats_Row_FoodBaseProductionTip".Translate().ToString();
            if (intro.Contains("TSA_WD_"))
                intro = "Universal virtual food produced per day by every outpost before skill or tile bonuses.";
            string baseLine = "TSA_WD_OutpostStats_Row_FoodProductionBaseLine".Translate(settingsBase.ToString("F1")).ToString();
            if (baseLine.Contains("TSA_WD_"))
                baseLine = "Base Production: +" + settingsBase.ToString("F1");
            string upgLine = "TSA_WD_OutpostStats_Row_FoodProductionUpgradeLine".Translate(upgradeFlat.ToString("F1")).ToString();
            if (upgLine.Contains("TSA_WD_"))
                upgLine = "Outpost Upgrades: +" + upgradeFlat.ToString("F1");
            var row = AddRowReturn(
                section,
                "TSA_WD_OutpostStats_Row_FoodBaseProduction",
                effectiveBase.ToString("F1"),
                intro + "\n\n" + baseLine + "\n" + upgLine);
            MarkBoostedIf(row, upgradeFlat > 1e-6f);
        }

        private static OutpostStatsSection BuildProductionSection(WorldObject_WD_Outpost outpost, bool fullWidth = false)
        {
            var section = new OutpostStatsSection
            {
                Title = "TSA_WD_OutpostStats_Section_Production".Translate().ToString(),
                FullWidth = fullWidth,
            };

            AppendRelevantTileRows(outpost, section);
            if (ShouldShowProductionSkillRows(outpost))
                BuildSkillsRows(outpost, section);

            bool showsCycle = ShowsProductionCycleAndTimer(outpost);
            if (showsCycle)
            {
                float cycleDays = Outpost_Production_Utils.GetProductionCycleDays(outpost);
                AddRow(section, "TSA_WD_OutpostStats_Row_CycleDays", cycleDays.ToString("F1"), "TSA_WD_OutpostStats_Row_CycleDaysTip");
            }

            bool nothingSelected = LacksPhysicalGoodsProductionSelection(outpost);
            string noneSelectedValue = "TSA_WD_OutpostStats_Value_NothingToProduce".Translate().ToString();
            var pauseReasons = outpost.GetProductionPauseReasons();
            bool paused = pauseReasons != null && pauseReasons.Count > 0;

            if (showsCycle)
            {
                string timeLeft;
                string timeLeftTip;
                Color? timeLeftColor = null;
                if (nothingSelected)
                {
                    timeLeft = noneSelectedValue;
                    timeLeftTip = "TSA_WD_OutpostStats_Row_NothingToProduceTip".Translate().ToString();
                    timeLeftColor = Color.red;
                }
                else
                {
                    timeLeft = outpost.GetProductionTimeLeftForOverview();
                    if (string.IsNullOrEmpty(timeLeft))
                        timeLeft = "—";
                    timeLeftTip = paused
                        ? string.Join("\n", pauseReasons)
                        : "TSA_WD_OutpostStats_Row_TimeLeftTip".Translate().ToString();
                    if (paused)
                        timeLeftColor = Color.yellow;
                }

                var timeLeftRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_TimeLeft", timeLeft, timeLeftTip);
                if (timeLeftColor.HasValue)
                    timeLeftRow.ValueColor = timeLeftColor;
            }

            string na = "TSA_WD_OutpostStats_Value_NA".Translate().ToString();
            AppendProductionTypeRows(outpost, section, nothingSelected, na);

            return section;
        }

        /// <summary>False for outposts with no delivery cycle (warehouse, power plant, mortar, rapid response, research).</summary>
        private static bool ShowsProductionCycleAndTimer(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.def == null) return false;
            if (outpost.IsResearchOutpost) return false;
            if (Outpost_Production_Utils.IsWarehouseOutpost(outpost.def)) return false;
            if (Outpost_Production_Utils.IsPowerPlantOutpost(outpost.def)) return false;
            if (Outpost_Production_Utils.IsMortarOutpost(outpost.def)) return false;
            if (Outpost_Production_Utils.IsRapidResponseOutpost(outpost.def)) return false;
            return true;
        }

        /// <summary>Type-specific production metrics instead of one generic skill/output pair.</summary>
        private static void AppendProductionTypeRows(WorldObject_WD_Outpost outpost, OutpostStatsSection section, bool nothingSelected, string na)
        {
            if (outpost?.def == null) return;
            WorldObjectDef def = outpost.def;

            if (Outpost_Production_Utils.IsRecruitingOutpost(def))
            {
                AppendRecruitingProductionRows(outpost, section, nothingSelected, na);
                return;
            }
            if (Outpost_Production_Utils.IsTradingOutpost(def))
            {
                AppendTradingProductionRows(outpost, section, nothingSelected, na);
                return;
            }
            if (Outpost_Production_Utils.IsEmbassyOutpost(def))
            {
                AppendEmbassyProductionRows(outpost, section, nothingSelected, na);
                return;
            }
            if (Outpost_Production_Utils.IsScavengingOutpost(def))
            {
                AppendScavengingProductionRows(outpost, section, nothingSelected, na);
                return;
            }
            if (Outpost_Production_Utils.IsAcademyOutpost(def))
            {
                AppendAcademyProductionRows(outpost, section, nothingSelected, na);
                return;
            }
            if (outpost.IsResearchOutpost)
            {
                AppendResearchProductionRows(outpost, section);
                return;
            }
            if (outpost.IsPowerPlantOutpost)
            {
                AppendPowerPlantProductionRows(outpost, section);
                return;
            }
            if (outpost.IsRapidResponseOutpost)
            {
                AppendRapidResponseProductionRows(outpost, section);
                return;
            }
            if (Outpost_Production_Utils.IsMortarOutpost(def))
            {
                AppendMortarProductionRows(outpost, section);
                return;
            }
            if (Outpost_Production_Utils.IsWarehouseOutpost(def))
            {
                AppendWarehouseProductionRows(outpost, section);
                return;
            }

            AppendPhysicalGoodsProductionRows(outpost, section, nothingSelected, na);
        }

        private static void AppendRecruitingProductionRows(WorldObject_WD_Outpost outpost, OutpostStatsSection section, bool nothingSelected, string na)
        {
            float social = outpost.GetTotalRelevantSkill();
            float cycleSocial = outpost.GetCapacityForYieldPreview();
            int recruits = nothingSelected ? 0 : Outpost_Recruiting.ComputeRecruitCount(outpost, cycleSocial);

            AddRow(section, "TSA_WD_OutpostStats_Row_RecruitingSocial", social.ToString("F0"), "TSA_WD_OutpostStats_Row_RecruitingSocialTip");

            string yieldKey = "TSA_WD_OutpostStats_Row_RecruitingYield";
            string yieldVal = yieldKey.Translate(recruits.ToString()).ToString();
            if (yieldVal == yieldKey || yieldVal.Contains("TSA_WD_"))
                yieldVal = "~" + recruits + " recruit(s) this cycle";
            string yieldTip = Outpost_Recruiting.GetDetailedMathTooltip(outpost, cycleSocial);
            if (string.IsNullOrEmpty(yieldTip))
            {
                yieldTip = "TSA_WD_OutpostStats_Row_RecruitingYieldTip".Translate(
                    Outpost_Recruiting.SocialPerRecruit.ToString("F0"),
                    cycleSocial.ToString("F1"),
                    recruits.ToString()).ToString();
            }
            AddRowRawTip(section, "TSA_WD_OutpostStats_Row_RecruitingYield", nothingSelected ? na : yieldVal, yieldTip);

            int nearby = Outpost_Trading.GetNearbySettlementCount(outpost);
            string nearbyTip = Outpost_Trading.GetNearbyTradingPartnersTooltipAppendix(outpost);
            if (string.IsNullOrEmpty(nearbyTip))
                nearbyTip = "TSA_WD_OutpostStats_Row_NearbySettlementsTip".Translate().ToString();
            else
            {
                string bonusNote = "TSA_WD_OutpostStats_Row_RecruitingNearbyBonusTip".Translate(Outpost_Recruiting.NeighborBonusDivisor).ToString();
                if (!bonusNote.Contains("TSA_WD_")) nearbyTip = nearbyTip + "\n\n" + bonusNote;
            }
            AddRowRawTip(section, "TSA_WD_OutpostStats_Row_NearbySettlements", nearby.ToString(), nearbyTip);

            AddRowRawTip(section, "TSA_WD_OutpostStats_Row_RecruitingPriority",
                Outpost_Recruiting.GetPrioritySkillDisplayLine(outpost),
                "TSA_WD_OutpostStats_Row_RecruitingPriorityTip".Translate().ToString());

            string poolSummary = Outpost_Recruiting.GetXenotypePoolSummaryLine(outpost);
            if (string.IsNullOrEmpty(poolSummary))
                poolSummary = na;
            AddRowRawTip(section, "TSA_WD_OutpostStats_Row_RecruitingXenotypePool",
                poolSummary,
                Outpost_Recruiting.GetXenotypePoolTooltipAppendix(outpost));

            string kindSummary = Outpost_Recruiting.GetPawnKindPoolSummaryLine(outpost);
            if (string.IsNullOrEmpty(kindSummary))
                kindSummary = na;
            AddRowRawTip(section, "TSA_WD_OutpostStats_Row_RecruitingPawnKindPool",
                kindSummary,
                Outpost_Recruiting.GetPawnKindPoolTooltipAppendix(outpost));
        }

        private static void AppendTradingProductionRows(WorldObject_WD_Outpost outpost, OutpostStatsSection section, bool nothingSelected, string na)
        {
            int nearby = Outpost_Trading.GetNearbySettlementCount(outpost);
            string nearbyTip = Outpost_Trading.GetNearbyTradingPartnersTooltipAppendix(outpost);
            if (string.IsNullOrEmpty(nearbyTip))
                nearbyTip = "TSA_WD_OutpostStats_Row_NearbySettlementsTip".Translate().ToString();
            AddRowRawTip(section, "TSA_WD_OutpostStats_Row_NearbySettlements", nearby.ToString(), nearbyTip);

            int social = Outpost_EstablishmentRequirements.GetCumulativeOutpostSkillForSkill(outpost, SkillDefOf.Social);
            float mult = Outpost_Trading.GetTradingSocialYieldMultiplier(outpost);
            int relativePct = Mathf.RoundToInt((mult - 1f) * 100f);
            string multDisplay = relativePct == 0 ? "0%" : (relativePct > 0 ? "+" : "") + relativePct.ToString() + "%";
            AddRowRawTip(section, "TSA_WD_OutpostStats_Row_TradingSocialMult", multDisplay,
                Outpost_Trading.GetSocialYieldStatsTooltip(outpost));

            string yield = nothingSelected ? na : Outpost_Trading.GetTradingDeliveryProductLine(outpost);
            if (string.IsNullOrEmpty(yield)) yield = na;
            AddRow(section, "TSA_WD_OutpostStats_Row_TradingSilver", yield, "TSA_WD_OutpostStats_Row_TradingSilverTip");
        }

        private static void AppendEmbassyProductionRows(WorldObject_WD_Outpost outpost, OutpostStatsSection section, bool nothingSelected, string na)
        {
            int nearby = Outpost_Embassy.GetNearbySettlementCount(outpost);
            AddRow(section, "TSA_WD_OutpostStats_Row_NearbySettlements", nearby.ToString(), "TSA_WD_OutpostStats_Row_EmbassyNearbyTip");

            float socialRaw = Outpost_Embassy.GetDeliveryDrivingCapacityRaw(outpost);
            float socialEff = OutpostSkillScaling.ToEffective(socialRaw);
            AddRow(section, "TSA_WD_OutpostStats_Row_EmbassySocial",
                OutpostSkillScaling.FormatRawEffective(socialRaw),
                "TSA_WD_OutpostStats_Row_EmbassySocialTip");

            float mult = Outpost_Embassy.GetSocialMultiplier(outpost.GetCapacityForYieldPreview());
            string multDisplay = Mathf.RoundToInt(mult * 100f).ToString() + "%";
            AddRowRawTip(section, "TSA_WD_OutpostStats_Row_EmbassySocialMult", multDisplay,
                Outpost_Embassy.GetSocialMultStatsTooltip(outpost));

            int expected = nothingSelected ? 0 : Outpost_Embassy.ComputeExpectedGoodwillTotal(outpost, outpost.GetCapacityForYieldPreview());
            string yield = nothingSelected ? na : ("+" + expected);
            string goodwillTip = nothingSelected
                ? "TSA_WD_OutpostStats_Row_EmbassyGoodwillTip".Translate().ToString()
                : Outpost_Embassy.GetProductionTooltip(outpost);
            AddRowRawTip(section, "TSA_WD_OutpostStats_Row_EmbassyGoodwill", yield, goodwillTip);
        }

        private static void AppendScavengingProductionRows(WorldObject_WD_Outpost outpost, OutpostStatsSection section, bool nothingSelected, string na)
        {
            int workers = outpost.WorkerPawnCount;
            AddRow(section, "TSA_WD_OutpostStats_Row_ScavengingWorkers", workers.ToString(), "TSA_WD_OutpostStats_Row_ScavengingWorkersTip");

            var kind = Outpost_Scavenging.GetEffectiveKind(outpost);
            if (!kind.HasValue || nothingSelected)
            {
                AddRow(section, "TSA_WD_OutpostStats_Row_TypeSummary", na, "TSA_WD_OutpostStats_Row_NothingToProduceTip");
                return;
            }

            Outpost_Scavenging.ScavengingKind tier = kind.Value;
            float perPawn = Outpost_Scavenging.GetMarketValuePerPawn(tier);
            float totalMv = Outpost_Scavenging.GetTotalDeliveryMarketValue(outpost, tier);
            string tierLabel = Outpost_Scavenging.GetKindShortLabel(tier);

            string yieldKey = "TSA_WD_OutpostStats_Row_ScavengingYield";
            string yieldVal = yieldKey.Translate(workers.ToString(), totalMv.ToString("F0"), tierLabel).ToString();
            if (yieldVal == yieldKey || yieldVal.Contains("TSA_WD_"))
                yieldVal = workers + " garrisoned pawns → ~" + totalMv.ToString("F0") + " value (" + tierLabel + ")";
            string yieldTip = Outpost_Scavenging.GetKindRequirementTooltip(tier)
                + "\n\n"
                + workers + " × " + perPawn.ToString("F0") + " silver/pawn = " + totalMv.ToString("F0") + " target market value per cycle.";
            AddRowRawTip(section, "TSA_WD_OutpostStats_Row_ScavengingYield", yieldVal, yieldTip);
        }

        private static void AppendAcademyProductionRows(WorldObject_WD_Outpost outpost, OutpostStatsSection section, bool nothingSelected, string na)
        {
            var skill = Outpost_Academy.GetSkillForCurrentCycle(outpost) ?? outpost.SelectedAcademySkill;
            string skillLabel = skill?.LabelCap ?? na;
            AddRowWithLabel(section,
                "TSA_WD_OutpostStats_Row_AcademySkill".Translate().ToString(),
                skillLabel,
                "TSA_WD_OutpostStats_Row_AcademySkillTip".Translate().ToString());

            string summary = nothingSelected ? na : Outpost_Academy.GetInspectProductLine(outpost);
            if (string.IsNullOrEmpty(summary)) summary = na;
            string tip = summary;
            if (!nothingSelected)
            {
                string softTip = Outpost_Production_Utils.BuildSoftProductionBonusTooltip(outpost);
                if (!string.IsNullOrEmpty(softTip))
                    tip = tip + "\n\n" + softTip;
                string softSuffix = Outpost_Production_Utils.BuildSoftProductionBonusSuffix(outpost);
                if (!string.IsNullOrEmpty(softSuffix))
                    tip = tip + "\n" + softSuffix.Trim();
            }
            var academyRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_TypeSummary", summary, tip);
            if (!nothingSelected
                && OutpostWarehouseAuraUtility.GetExpertAndWarehouseProductionBonusFraction(outpost) > 1e-6f)
                MarkBoostedIf(academyRow, true);
        }

        private static void AppendResearchProductionRows(WorldObject_WD_Outpost outpost, OutpostStatsSection section)
        {
            var researchRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_TypeSummary",
                Outpost_Research.GetStatsSummaryLine(outpost),
                Outpost_Research.GetStatsSummaryTooltip(outpost));
            float resUpgrade = outpost.GetResearchUpgradeEfficiencyBonus();
            float resExpert = OutpostExpertUtility.GetCombinedProductionBonus(outpost);
            MarkBoostedIf(researchRow, Outpost_Research.CanResearchNow(outpost, out _) && resUpgrade + resExpert > 1e-6f);
        }

        private static void AppendPowerPlantProductionRows(WorldObject_WD_Outpost outpost, OutpostStatsSection section)
        {
            var powerRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_PowerWatts",
                Outpost_PowerPlant.FormatWatts(Outpost_PowerPlant.GetRemotePowerWatts(outpost)),
                "TSA_WD_OutpostStats_Row_PowerWattsTip".Translate().ToString());
            MarkBoostedIf(powerRow, outpost.GetRemotePowerUpgradeBonus() > 1e-6f);
            AddRow(section, "TSA_WD_OutpostStats_Row_TypeSummary", Outpost_PowerPlant.GetInspectProductLine(outpost), "TSA_WD_OutpostStats_Row_TypeSummaryTip");
        }

        private static void AppendRapidResponseProductionRows(WorldObject_WD_Outpost outpost, OutpostStatsSection section)
        {
            var comp = outpost.GetComponent<CompViralSpread>();
            float deployable = RapidResponseUtility.GetDeployableStrength(outpost, comp);
            var deployRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_RRDeployable", deployable.ToString("F0"),
                "TSA_WD_OutpostStats_Row_RRDeployableTip".Translate().ToString());
            MarkBoostedIf(deployRow, outpost.GetRapidResponseOffensiveStrengthBonus() > 1e-6f);
            AddRow(section, "TSA_WD_OutpostStats_Row_RRInterceptRange",
                RapidResponseUtility.GetRangeTiles(outpost).ToString("F0"),
                OutpostStatsTooltipUtil.BuildConfigurableRangeTooltip(
                    "TSA_WD_OutpostStats_Row_RRInterceptRangeTip".Translate().ToString(),
                    null,
                    RapidResponseUtility.GetConfiguredMaxRangeTiles(),
                    RapidResponseUtility.GetRangeTiles(outpost)));
            AddRow(section, "TSA_WD_OutpostStats_Row_RRDropPodRange",
                RapidResponseUtility.GetDropPodRangeTiles().ToString("F0"),
                "TSA_WD_OutpostStats_Row_RRDropPodRangeTip");
            AddRow(section, "TSA_WD_OutpostStats_Row_TypeSummary", WD_Outpost_RapidResponse.GetInspectStatusLine(outpost), "TSA_WD_OutpostStats_Row_TypeSummaryTip");
        }

        private static OutpostStatsSection BuildMortarTurretSection(WorldObject_WD_Outpost outpost, bool fullWidth = false)
        {
            var section = new OutpostStatsSection
            {
                Title = "TSA_WD_OutpostStats_Section_Mortar".Translate().ToString(),
                FullWidth = fullWidth,
            };
            AppendMortarProductionRows(outpost, section, includeAa: false, includeTabHint: false);
            AppendMortarAccuracyBandRows(outpost, section, tipKey: "TSA_WD_OutpostStats_Row_MortarAccTip");
            return section;
        }

        private static OutpostStatsSection BuildAntiAirTurretSection(WorldObject_WD_Outpost outpost, bool fullWidth = false)
        {
            var section = new OutpostStatsSection
            {
                Title = "TSA_WD_OutpostStats_Section_AntiAir".Translate().ToString(),
                FullWidth = fullWidth,
            };

            var aaRangeExpertBonuses = new List<OutpostStatsTooltipUtil.BonusLine>();
            OutpostStatsTooltipUtil.AddExpertBonusLines(outpost, ExpertEffect.MortarAntiAirRange, aaRangeExpertBonuses);
            float aaStrategist = OutpostExpertUtility.GetStrategistAttackRangeBonusFraction(outpost);
            var aaRangeRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_AntiAirRange",
                AntiAirFireUtils.GetPlayerAntiAirMaxRangeTiles(outpost).ToString("F0"),
                OutpostStatsTooltipUtil.BuildConfigurableRangeTooltip(
                    "TSA_WD_OutpostStats_Row_AntiAirRangeTip".Translate().ToString(),
                    null,
                    AntiAirFireUtils.GetPlayerAntiAirConfiguredMaxRangeTiles(outpost),
                    AntiAirFireUtils.GetPlayerAntiAirMaxRangeTiles(outpost),
                    aaRangeExpertBonuses));
            MarkBoostedIf(aaRangeRow, aaStrategist > 1e-6f);

            var seth = WorldDominationMod.settings;
            float baseAaDmg = seth?.antiAirBaseDamage ?? WorldDominationSettings.DefAntiAirBaseDamage;
            float aaDmg = AntiAirFireUtils.GetAntiAirDamage(outpost);
            var aaDmgBonuses = new List<OutpostStatsTooltipUtil.BonusLine>();
            OutpostStatsTooltipUtil.AddUpgradeFlatLines(outpost, d => d.mortarShellDamageBonus, aaDmgBonuses);
            var aaDmgRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_AntiAirDamage",
                aaDmg.ToString("F0"),
                OutpostStatsTooltipUtil.BuildFlatAdditionTooltip(
                    "TSA_WD_OutpostStats_Row_AntiAirDamageTip".Translate().ToString(),
                    baseAaDmg, "F0", aaDmgBonuses, aaDmg, "F0"));
            MarkBoostedIf(aaDmgRow, outpost.GetBuiltUpgradeMortarShellDamageBonus() > 1e-6f);

            float aaCdSec = AntiAirFireUtils.GetAntiAirEffectiveCooldownSeconds(outpost, out float aaUpg);
            var aaCdRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_AntiAirCooldown",
                aaCdSec.ToString("F0"),
                BuildAntiAirCooldownTooltip(outpost, aaCdSec, aaUpg));
            MarkBoostedIf(aaCdRow, aaUpg > 1e-6f || outpost.GetSkillSum(SkillDefOf.Shooting) > 1e-6f);

            var aaComp = outpost.GetComponent<CompViralSpread>();
            string aaReady = aaComp != null && aaComp.IsAntiAirOnCooldown
                ? "TSA_WD_OutpostStats_Value_AntiAirCooldown".Translate(
                    ((aaComp.antiAirCooldownTick - Find.TickManager.TicksGame) / 60f).ToString("F0")).ToString()
                : "TSA_WD_OutpostStats_Value_ReadyToAttack".Translate().ToString();
            AddRow(section, "TSA_WD_OutpostStats_Row_AntiAirStatus", aaReady, "TSA_WD_OutpostStats_Row_AntiAirStatusTip");

            AppendAntiAirAccuracyBandRows(outpost, section);

            float vsMort = seth?.antiAirVsMortarHitChance ?? WorldDominationSettings.DefAntiAirVsMortarHitChance;
            AddRow(section, "TSA_WD_OutpostStats_Row_AntiAirVsMortar",
                (Mathf.Clamp01(vsMort) * 100f).ToString("F0") + "%",
                "TSA_WD_OutpostStats_Row_AntiAirVsMortarTip");
            return section;
        }

        private static void AppendMortarProductionRows(WorldObject_WD_Outpost outpost, OutpostStatsSection section)
            => AppendMortarProductionRows(outpost, section, includeAa: true, includeTabHint: false);

        private static void AppendMortarProductionRows(WorldObject_WD_Outpost outpost, OutpostStatsSection section, bool includeAa, bool includeTabHint)
        {
            var seth = WorldDominationMod.settings;
            float baseDmg = seth?.mortarBaseShellDamage ?? WorldDominationSettings.DefMortarBaseShellDamage;
            float dmg = MortarFireUtils.GetPlayerMortarShellDamage(outpost);
            var dmgBonuses = new List<OutpostStatsTooltipUtil.BonusLine>();
            OutpostStatsTooltipUtil.AddUpgradeFlatLines(outpost, d => d.mortarShellDamageBonus, dmgBonuses);
            var dmgRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_MortarDamage",
                dmg.ToString("F0"),
                OutpostStatsTooltipUtil.BuildFlatAdditionTooltip(
                    "TSA_WD_OutpostStats_Row_MortarDamageTip".Translate().ToString(),
                    baseDmg, "F0", dmgBonuses, dmg, "F0"));
            MarkBoostedIf(dmgRow, outpost.GetBuiltUpgradeMortarShellDamageBonus() > 1e-6f);

            float absoluteMaxRange = MortarFireUtils.GetPlayerMortarConfiguredMaxRangeTiles(outpost);
            float configuredRange = MortarFireUtils.GetPlayerMortarMaxRangeTiles(outpost);
            var rangeBonuses = new List<OutpostStatsTooltipUtil.BonusLine>();
            OutpostStatsTooltipUtil.AddUpgradeFlatLines(outpost, d => d.mortarRangeBonus, rangeBonuses);
            var rangeExpertBonuses = new List<OutpostStatsTooltipUtil.BonusLine>();
            OutpostStatsTooltipUtil.AddExpertBonusLines(outpost, ExpertEffect.MortarAntiAirRange, rangeExpertBonuses);
            float strategistRange = OutpostExpertUtility.GetStrategistAttackRangeBonusFraction(outpost);
            var rangeRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_MortarRange",
                configuredRange.ToString("F0"),
                OutpostStatsTooltipUtil.BuildConfigurableRangeTooltip(
                    "TSA_WD_OutpostStats_Row_MortarRangeTip".Translate().ToString(),
                    rangeBonuses,
                    absoluteMaxRange,
                    configuredRange,
                    rangeExpertBonuses));
            MarkBoostedIf(rangeRow,
                outpost.GetBuiltUpgradeMortarRangeBonus() > 1e-6f
                || strategistRange > 1e-6f);

            float cooldownDays = MortarFireUtils.GetPlayerMortarEffectiveCooldownDays(
                outpost, out float baseCd, out float durationMult, out float fromSkillCd, out float fromUpgradeCd);
            var cdRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_MortarCooldown",
                cooldownDays.ToString("F1"),
                BuildMortarCooldownTooltip(outpost, baseCd, durationMult, fromSkillCd, fromUpgradeCd, cooldownDays));
            MarkBoostedIf(cdRow, fromSkillCd > 1e-6f || fromUpgradeCd > 1e-6f);

            float bestShoot = outpost.GetHighestVirtualPawnSkill(SkillDefOf.Shooting);
            float shootSum = outpost.GetSkillSum(SkillDefOf.Shooting);
            AddRowRawTip(section, "TSA_WD_OutpostStats_Row_MortarShooting",
                bestShoot.ToString("F0"),
                "TSA_WD_OutpostStats_Row_MortarShootingTip".Translate(bestShoot.ToString("F0"), shootSum.ToString("F0")).ToString());

            if (includeAa && AntiAirFireUtils.HasAntiAirUpgrade(outpost))
            {
                float baseAaDmg = seth?.antiAirBaseDamage ?? WorldDominationSettings.DefAntiAirBaseDamage;
                float aaDmg = AntiAirFireUtils.GetAntiAirDamage(outpost);
                var aaDmgBonuses = new List<OutpostStatsTooltipUtil.BonusLine>();
                OutpostStatsTooltipUtil.AddUpgradeFlatLines(outpost, d => d.mortarShellDamageBonus, aaDmgBonuses);
                var aaDmgRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_AntiAirDamage",
                    aaDmg.ToString("F0"),
                    OutpostStatsTooltipUtil.BuildFlatAdditionTooltip(
                        "TSA_WD_OutpostStats_Row_AntiAirDamageTip".Translate().ToString(),
                        baseAaDmg, "F0", aaDmgBonuses, aaDmg, "F0"));
                MarkBoostedIf(aaDmgRow, outpost.GetBuiltUpgradeMortarShellDamageBonus() > 1e-6f);

                float aaCdSec = AntiAirFireUtils.GetAntiAirEffectiveCooldownSeconds(outpost, out float aaUpg);
                var aaCdRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_AntiAirCooldown",
                    aaCdSec.ToString("F0"),
                    BuildAntiAirCooldownTooltip(outpost, aaCdSec, aaUpg));
                MarkBoostedIf(aaCdRow, aaUpg > 1e-6f || shootSum > 1e-6f);

                var aaComp = outpost.GetComponent<CompViralSpread>();
                string aaReady = aaComp != null && aaComp.IsAntiAirOnCooldown
                    ? "TSA_WD_OutpostStats_Value_AntiAirCooldown".Translate(
                        ((aaComp.antiAirCooldownTick - Find.TickManager.TicksGame) / 60f).ToString("F0")).ToString()
                    : "TSA_WD_OutpostStats_Value_ReadyToAttack".Translate().ToString();
                AddRow(section, "TSA_WD_OutpostStats_Row_AntiAirStatus", aaReady, "TSA_WD_OutpostStats_Row_AntiAirStatusTip");
            }

            // includeTabHint kept for signature compatibility; Mortar tab removed.
            _ = includeTabHint;
        }

        private static void AppendMortarAccuracyBandRows(
            WorldObject_WD_Outpost outpost,
            OutpostStatsSection section,
            string tipKey,
            string label50 = "TSA_WD_OutpostStats_Row_MortarAcc50",
            string label75 = "TSA_WD_OutpostStats_Row_MortarAcc75",
            string label100 = "TSA_WD_OutpostStats_Row_MortarAcc100")
        {
            var seth = WorldDominationMod.settings;
            float best = outpost.GetHighestVirtualPawnSkill(SkillDefOf.Shooting);
            float fromBest = best * WorldDominationSettings.MortarHitFlatBonusPerBestShootingLevel;
            float fromUpg = outpost.GetBuiltUpgradeMortarHitChanceBonus();

            float band50 = seth?.mortarHitChance0To50PctRange ?? WorldDominationSettings.DefMortarHitChance0To50PctRange;
            float band75 = seth?.mortarHitChance51To75PctRange ?? WorldDominationSettings.DefMortarHitChance51To75PctRange;
            float band100 = seth?.mortarHitChance76To100PctRange ?? WorldDominationSettings.DefMortarHitChance76To100PctRange;
            float r50 = Mathf.Clamp01(band50 + fromBest + fromUpg);
            float r75 = Mathf.Clamp01(band75 + fromBest + fromUpg);
            float r100 = Mathf.Clamp01(band100 + fromBest + fromUpg);

            var hitBonuses = BuildMortarHitChanceBonusLines(outpost, best, fromBest);
            string tipIntro = tipKey.Translate().ToString();

            var row50 = AddRowReturn(section, label50,
                (r50 * 100f).ToString("F0") + "%",
                OutpostStatsTooltipUtil.BuildHitChanceTooltip(tipIntro, band50, hitBonuses, r50));
            var row75 = AddRowReturn(section, label75,
                (r75 * 100f).ToString("F0") + "%",
                OutpostStatsTooltipUtil.BuildHitChanceTooltip(tipIntro, band75, hitBonuses, r75));
            var row100 = AddRowReturn(section, label100,
                (r100 * 100f).ToString("F0") + "%",
                OutpostStatsTooltipUtil.BuildHitChanceTooltip(tipIntro, band100, hitBonuses, r100));
            bool boosted = fromBest > 1e-6f || fromUpg > 1e-6f;
            MarkBoostedIf(row50, boosted);
            MarkBoostedIf(row75, boosted);
            MarkBoostedIf(row100, boosted);
        }

        /// <summary>Anti-Air accuracy vs pods/aerials: AA band settings + best shooter only (no mortar hit upgrades).</summary>
        private static void AppendAntiAirAccuracyBandRows(WorldObject_WD_Outpost outpost, OutpostStatsSection section)
        {
            var seth = WorldDominationMod.settings;
            float best = outpost.GetHighestVirtualPawnSkill(SkillDefOf.Shooting);
            float fromBest = best * WorldDominationSettings.MortarHitFlatBonusPerBestShootingLevel;

            float band50 = seth?.antiAirHitChance0To50PctRange ?? WorldDominationSettings.DefAntiAirHitChance0To50PctRange;
            float band75 = seth?.antiAirHitChance51To75PctRange ?? WorldDominationSettings.DefAntiAirHitChance51To75PctRange;
            float band100 = seth?.antiAirHitChance76To100PctRange ?? WorldDominationSettings.DefAntiAirHitChance76To100PctRange;
            float r50 = Mathf.Clamp01(band50 + fromBest);
            float r75 = Mathf.Clamp01(band75 + fromBest);
            float r100 = Mathf.Clamp01(band100 + fromBest);

            var hitBonuses = new List<OutpostStatsTooltipUtil.BonusLine>();
            if (fromBest > 1e-6f)
            {
                hitBonuses.Add(new OutpostStatsTooltipUtil.BonusLine
                {
                    Source = "TSA_WD_OutpostStats_Tip_BestShooter".Translate(best.ToString("F0")).ToString(),
                    Fraction = fromBest
                });
            }
            string tipIntro = "TSA_WD_OutpostStats_Row_AntiAirAccTip".Translate().ToString();

            var row50 = AddRowReturn(section, "TSA_WD_OutpostStats_Row_AntiAirAcc50",
                (r50 * 100f).ToString("F0") + "%",
                OutpostStatsTooltipUtil.BuildHitChanceTooltip(tipIntro, band50, hitBonuses, r50));
            var row75 = AddRowReturn(section, "TSA_WD_OutpostStats_Row_AntiAirAcc75",
                (r75 * 100f).ToString("F0") + "%",
                OutpostStatsTooltipUtil.BuildHitChanceTooltip(tipIntro, band75, hitBonuses, r75));
            var row100 = AddRowReturn(section, "TSA_WD_OutpostStats_Row_AntiAirAcc100",
                (r100 * 100f).ToString("F0") + "%",
                OutpostStatsTooltipUtil.BuildHitChanceTooltip(tipIntro, band100, hitBonuses, r100));
            bool boosted = fromBest > 1e-6f;
            MarkBoostedIf(row50, boosted);
            MarkBoostedIf(row75, boosted);
            MarkBoostedIf(row100, boosted);
        }

        private static List<OutpostStatsTooltipUtil.BonusLine> BuildMortarHitChanceBonusLines(
            WorldObject_WD_Outpost outpost, float bestShoot, float fromBest)
        {
            var lines = new List<OutpostStatsTooltipUtil.BonusLine>();
            if (fromBest > 1e-6f)
            {
                lines.Add(new OutpostStatsTooltipUtil.BonusLine
                {
                    Source = "TSA_WD_OutpostStats_Tip_BestShooter".Translate(bestShoot.ToString("F0")).ToString(),
                    Fraction = fromBest
                });
            }
            OutpostStatsTooltipUtil.AddUpgradeFlatLines(outpost, d => d.mortarHitChanceBonus, lines);
            return lines;
        }

        private static string BuildMortarCooldownTooltip(
            WorldObject_WD_Outpost outpost,
            float baseCd,
            float durationMult,
            float fromSkill,
            float fromUpgrade,
            float resultDays)
        {
            var reductions = new List<OutpostStatsTooltipUtil.BonusLine>();
            float shootSum = outpost.GetSkillSum(SkillDefOf.Shooting);
            if (fromSkill > 1e-6f)
            {
                reductions.Add(new OutpostStatsTooltipUtil.BonusLine
                {
                    Source = "TSA_WD_OutpostStats_Tip_CumShooting".Translate(shootSum.ToString("F0")).ToString(),
                    Fraction = fromSkill
                });
            }
            OutpostStatsTooltipUtil.AddUpgradePercentLines(outpost, d => d.mortarCooldownReduction, reductions);
            _ = fromUpgrade;
            return OutpostStatsTooltipUtil.BuildDurationReductionTooltip(
                "TSA_WD_OutpostStats_Row_MortarCooldownTip".Translate().ToString(),
                baseCd, "F1",
                reductions,
                durationMult,
                WorldDominationSettings.MortarCooldownMultiplierFloor,
                resultDays, "F1");
        }

        private static string BuildAntiAirCooldownTooltip(WorldObject_WD_Outpost outpost, float resultSec, float fromUpgrade)
        {
            var seth = WorldDominationMod.settings;
            float baseSec = Mathf.Max(1f, seth?.cooldownAntiAirSeconds ?? WorldDominationSettings.DefCooldownAntiAirSeconds);
            float floorSec = Mathf.Max(1f, seth?.antiAirCooldownFloorSeconds ?? WorldDominationSettings.DefAntiAirCooldownFloorSeconds);
            float shootSum = outpost.GetSkillSum(SkillDefOf.Shooting);
            float fromSkill = WorldDominationSettings.MortarCooldownReductionPerCumulativeShootingSkill * shootSum;
            float rawMult = Mathf.Max(0f, 1f - fromSkill - fromUpgrade);
            float durationMult = Mathf.Max(WorldDominationSettings.MortarCooldownMultiplierFloor, rawMult);
            float beforeAbsFloor = baseSec * durationMult;

            var reductions = new List<OutpostStatsTooltipUtil.BonusLine>();
            if (fromSkill > 1e-6f)
            {
                reductions.Add(new OutpostStatsTooltipUtil.BonusLine
                {
                    Source = "TSA_WD_OutpostStats_Tip_CumShooting".Translate(shootSum.ToString("F0")).ToString(),
                    Fraction = fromSkill
                });
            }
            OutpostStatsTooltipUtil.AddUpgradePercentLines(outpost, d => d.mortarCooldownReduction, reductions);

            string absNote = null;
            if (beforeAbsFloor + 1e-4f > resultSec && resultSec <= floorSec + 1e-4f)
            {
                absNote = "TSA_WD_OutpostStats_Tip_AntiAirAbsFloor".Translate(
                    floorSec.ToString("F0"),
                    beforeAbsFloor.ToString("F0")).ToString();
            }

            return OutpostStatsTooltipUtil.BuildDurationReductionTooltip(
                "TSA_WD_OutpostStats_Row_AntiAirCooldownTip".Translate().ToString(),
                baseSec, "F0",
                reductions,
                durationMult,
                WorldDominationSettings.MortarCooldownMultiplierFloor,
                resultSec, "F0",
                absNote);
        }

        private static void AppendWarehouseProductionRows(WorldObject_WD_Outpost outpost, OutpostStatsSection section)
        {
            var seth = WorldDominationMod.settings;
            float baseRadius = seth?.warehouseAuraRadiusTiles ?? WorldDominationSettings.DefWarehouseAuraRadiusTiles;
            float baseBonus = seth?.warehouseAuraBonusPct ?? WorldDominationSettings.DefWarehouseAuraBonusPct;
            float radiusUpg = outpost.GetWarehouseAuraRadiusUpgradeBonus();
            float bonusUpg = outpost.GetWarehouseAuraBonusUpgradeBonus();
            float effectiveRadius = OutpostWarehouseAuraUtility.GetWarehouseAuraRadiusTiles(outpost);
            float effectiveBonus = OutpostWarehouseAuraUtility.GetWarehouseAuraBonusFraction(outpost);

            var radiusBonuses = new List<OutpostStatsTooltipUtil.BonusLine>();
            OutpostStatsTooltipUtil.AddUpgradeFlatLines(outpost, d => d.warehouseAuraRadiusBonus, radiusBonuses);
            var radiusRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_WarehouseAuraRadius",
                effectiveRadius.ToString("F0"),
                OutpostStatsTooltipUtil.BuildFlatAdditionTooltip(
                    "TSA_WD_OutpostStats_Row_WarehouseAuraRadiusTip".Translate().ToString(),
                    baseRadius, "F0", radiusBonuses, effectiveRadius, "F0"));
            MarkBoostedIf(radiusRow, radiusUpg > 1e-6f);

            var bonusBonuses = new List<OutpostStatsTooltipUtil.BonusLine>();
            OutpostStatsTooltipUtil.AddUpgradePercentLines(outpost, d => d.warehouseAuraBonus, bonusBonuses);
            for (int i = 0; i < bonusBonuses.Count; i++)
            {
                var b = bonusBonuses[i];
                b.Fraction = Mathf.Round(b.Fraction * 100f);
                bonusBonuses[i] = b;
            }
            var bonusRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_WarehouseAuraBonus",
                (effectiveBonus * 100f).ToString("F0") + "%",
                OutpostStatsTooltipUtil.BuildFlatAdditionTooltip(
                    "TSA_WD_OutpostStats_Row_WarehouseAuraBonusTip".Translate().ToString(),
                    baseBonus * 100f, "F0", bonusBonuses, effectiveBonus * 100f, "F0"));
            MarkBoostedIf(bonusRow, bonusUpg > 1e-6f);

            var whComp = CompOutpostWarehouse.Get(outpost);
            int kinds = whComp?.GetTotalStackKinds() ?? 0;
            int totalItems = whComp?.GetTotalStoredItemCount() ?? 0;
            AddRow(section, "TSA_WD_OutpostStats_Row_WarehouseKinds", kinds.ToString(), "TSA_WD_OutpostStats_Row_WarehouseKindsTip");
            AddRow(section, "TSA_WD_OutpostStats_Row_WarehouseTotalItems", totalItems.ToString(), "TSA_WD_OutpostStats_Row_WarehouseTotalItemsTip");
        }

        private static void AppendPhysicalGoodsProductionRows(WorldObject_WD_Outpost outpost, OutpostStatsSection section, bool nothingSelected, string na)
        {
            WorldObjectDef def = outpost.def;
            string skillName = WorldObject_WD_Outpost.GetRelevantSkillName(def);

            if (Outpost_Production_Utils.IsHuntingOutpost(def))
            {
                PawnKindDef prey = outpost.GetProducingPawnKindForCurrentCycle() ?? outpost.SelectedPawnKindForHunting;
                AddRow(section, "TSA_WD_OutpostStats_Row_HuntingPrey", prey?.LabelCap ?? na, "TSA_WD_OutpostStats_Row_HuntingPreyTip");
            }
            if (Outpost_Production_Utils.IsFishingOutpost(def))
            {
                ThingDef fish = outpost.GetProducingFishForCurrentCycle() ?? outpost.SelectedFishDef;
                AddRow(section, "TSA_WD_OutpostStats_Row_FishingCatch", fish?.LabelCap ?? na, "TSA_WD_OutpostStats_Row_FishingCatchTip");
            }

            // Farming, hunting, ranch, and mining: full cumulative skill drives output (shown as Total skill above).
            if (!ProductionSkillRowIsRedundantWithTotalSkill(def))
            {
                float skillAssigned = Outpost_Production.GetDeliveryDrivingCapacity(outpost);
                AddRowWithLabel(section,
                    "TSA_WD_OutpostStats_Row_ProductionSkillAssigned".Translate(skillName).ToString(),
                    skillAssigned.ToString("F1"),
                    "TSA_WD_OutpostStats_Row_ProductionSkillAssignedTip".Translate().ToString());
            }

            string expectedOutput;
            Color? expectedColor = null;
            if (nothingSelected)
            {
                expectedOutput = na;
                expectedColor = Color.red;
            }
            else
            {
                expectedOutput = outpost.GetProductionLineForOverview();
                if (string.IsNullOrEmpty(expectedOutput))
                    expectedOutput = "—";
            }

            var expectedRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_ExpectedOutput",
                expectedOutput,
                "TSA_WD_OutpostStats_Row_ExpectedOutputTip".Translate().ToString());
            if (expectedColor.HasValue)
                expectedRow.ValueColor = expectedColor;
            if (!nothingSelected)
            {
                float cap = Outpost_Production.GetDeliveryDrivingCapacity(outpost);
                string formula = Outpost_Production_Formula.BuildDeliveryFormulaTooltip(outpost, cap, true);
                if (!string.IsNullOrEmpty(formula))
                {
                    string factorTip = Outpost_Production_Utils.BuildProductionOutputFactorTooltip(outpost);
                    expectedRow.Tooltip = string.IsNullOrEmpty(factorTip)
                        ? formula
                        : formula + "\n\n" + factorTip;
                }
                float prodUpgrade = outpost.GetProductionUpgradeEfficiencyBonus();
                float prodExpert = OutpostExpertUtility.OutpostHasProductionBonusPath(outpost)
                    ? OutpostExpertUtility.GetCombinedProductionBonus(outpost)
                    : 0f;
                float prodWarehouse = OutpostWarehouseAuraUtility.GetBestWarehouseAuraBonus(outpost);
                float tileUpg = 0f;
                if (Outpost_Production_Utils.IsFarmingOutpost(def) || Outpost_Production_Utils.IsRanchOutpost(def))
                    tileUpg = outpost.GetBuiltUpgradeTileFertilityBonus();
                else if (Outpost_Production_Utils.IsHuntingOutpost(def))
                    tileUpg = outpost.GetBuiltUpgradeTileAnimalAbundanceBonus();
                else if (Outpost_Production_Utils.IsFishingOutpost(def))
                    tileUpg = outpost.GetBuiltUpgradeTileFishAbundanceBonus();
                else if (Outpost_Production_Utils.IsMiningOutpost(def))
                    tileUpg = outpost.GetBuiltUpgradeTileMiningBonus();
                if (prodUpgrade + prodExpert + prodWarehouse + tileUpg > 1e-6f && !expectedColor.HasValue)
                    MarkBoosted(expectedRow);
            }
        }

        private static bool ProductionSkillRowIsRedundantWithTotalSkill(WorldObjectDef def)
        {
            return Outpost_Production_Utils.IsFoodProducerOutpost(def)
                || Outpost_Production_Utils.IsMiningOutpost(def);
        }

        /// <summary>True when this outpost type requires a production pick but none is configured yet.</summary>
        public static bool LacksPhysicalGoodsProductionSelection(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.def == null) return false;
            WorldObjectDef def = outpost.def;

            if (!Outpost_Production_Utils.UsesPhysicalGoodsProductionSkill(def))
                return false;

            if (outpost.IsRapidResponseOutpost || outpost.IsResearchOutpost)
                return false;

            if (Outpost_Production_Utils.IsScavengingOutpost(def))
                return !outpost.HasSelectedScavengingKind;

            if (Outpost_Production_Utils.IsAcademyOutpost(def))
                return outpost.SelectedAcademySkill == null;

            if (Outpost_Production_Utils.IsHuntingOutpost(def))
                return outpost.SelectedPawnKindForHunting == null;

            if (Outpost_Production_Utils.IsFishingOutpost(def))
                return outpost.SelectedFishDef == null;

            if (Outpost_Production_Utils.IsTradingOutpost(def)
                || Outpost_Production_Utils.IsFarmingOutpost(def)
                || Outpost_Production_Utils.IsRanchOutpost(def)
                || Outpost_Production_Utils.IsMiningOutpost(def)
                || Outpost_Production_Utils.IsProductionOrTradingOutpost(def))
                return outpost.SelectedProductionDef == null;

            return false;
        }

        private static void AppendRelevantTileRows(WorldObject_WD_Outpost outpost, OutpostStatsSection section)
        {
            var grid = Find.WorldGrid;
            if (grid == null || outpost.Tile < 0 || outpost.Tile >= grid.TilesCount) return;
            if (grid[outpost.Tile].WaterCovered) return;

            int tile = outpost.Tile;
            var def = outpost.def;

            if (Outpost_Production_Utils.IsFarmingOutpost(def) || Outpost_Production_Utils.IsRanchOutpost(def))
            {
                float fertUpg = outpost.GetBuiltUpgradeTileFertilityBonus();
                int basePct = Mathf.RoundToInt(WorldTileProductivity.GetFarmingFertilityScore(tile, 0f) * 100f);
                string linesFert = WorldTileProductivity.BuildOutpostUpgradeProductivityLines(outpost, d => d.tileFertilityBonus);
                AddRowRawTip(section, "TSA_WD_OutpostStats_Row_TileFertilityBase",
                    "TSA_WD_Biome_FertilityPercent".Translate(basePct).ToString(),
                    WorldTileProductivity.GetFarmingFertilityTooltipText(tile, 0f, null));
                if (fertUpg > 1e-6f)
                {
                    var upgRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_TileFertilityUpgrade",
                        FormatPercentagePointsValue(fertUpg),
                        AppendUpgradeBlock("TSA_WD_OutpostStats_Row_TileFertilityUpgradeTip".Translate().ToString(), linesFert));
                    upgRow.ValueColor = Color.green;
                }
            }
            else if (Outpost_Production_Utils.IsHuntingOutpost(def))
            {
                float huntUpg = outpost.GetBuiltUpgradeTileAnimalAbundanceBonus();
                int basePct = Mathf.RoundToInt(WorldTileProductivity.GetHuntingScore(tile, 0f) * 100f);
                string linesHunt = WorldTileProductivity.BuildOutpostUpgradeProductivityLines(outpost, d => d.tileAnimalAbundanceBonus);
                AddRowRawTip(section, "TSA_WD_OutpostStats_Row_TileAnimalAbundanceBase",
                    "TSA_WD_Biome_AnimalsPercent".Translate(basePct).ToString(),
                    WorldTileProductivity.GetHuntingScoreTooltipText(tile, 0f, null));
                if (huntUpg > 1e-6f)
                {
                    var upgRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_TileAnimalAbundanceUpgrade",
                        FormatPercentagePointsValue(huntUpg),
                        AppendUpgradeBlock("TSA_WD_OutpostStats_Row_TileAnimalAbundanceUpgradeTip".Translate().ToString(), linesHunt));
                    upgRow.ValueColor = Color.green;
                }
            }
            else if (Outpost_Production_Utils.IsFishingOutpost(def))
            {
                float fishUpg = outpost.GetBuiltUpgradeTileFishAbundanceBonus();
                int basePct = Mathf.RoundToInt(WorldTileProductivity.GetFishingScore(tile, 0f) * 100f);
                string linesFish = WorldTileProductivity.BuildOutpostUpgradeProductivityLines(outpost, d => d.tileFishAbundanceBonus);
                AddRowRawTip(section, "TSA_WD_OutpostStats_Row_TileFishAbundanceBase",
                    "TSA_WD_Biome_FishPercent".Translate(basePct).ToString(),
                    WorldTileProductivity.GetFishingScoreTooltipText(tile, 0f, null));
                if (fishUpg > 1e-6f)
                {
                    var upgRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_TileFishAbundanceUpgrade",
                        FormatPercentagePointsValue(fishUpg),
                        AppendUpgradeBlock("TSA_WD_OutpostStats_Row_TileFishAbundanceUpgradeTip".Translate().ToString(), linesFish));
                    upgRow.ValueColor = Color.green;
                }
            }
            else if (Outpost_Production_Utils.IsMiningOutpost(def))
            {
                float mineUpg = outpost.GetBuiltUpgradeTileMiningBonus();
                int basePct = Mathf.RoundToInt(WorldTileProductivity.GetMiningOutputMultiplier(tile, 0f) * 100f);
                string linesMine = WorldTileProductivity.BuildOutpostUpgradeProductivityLines(outpost, d => d.tileMiningBonus);
                AddRowRawTip(section, "TSA_WD_OutpostStats_Row_TileMiningBase",
                    "TSA_WD_Biome_MiningPercent".Translate(basePct).ToString(),
                    WorldTileProductivity.GetMiningEfficiencyTooltipText(tile, 0f, null));
                if (mineUpg > 1e-6f)
                {
                    var upgRow = AddRowReturn(section, "TSA_WD_OutpostStats_Row_TileMiningUpgrade",
                        FormatPercentagePointsValue(mineUpg),
                        AppendUpgradeBlock("TSA_WD_OutpostStats_Row_TileMiningUpgradeTip".Translate().ToString(), linesMine));
                    upgRow.ValueColor = Color.green;
                }
            }
        }

        private static float GetProductionRelevantSkillSum(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 0f;
            if (Outpost_Production_Utils.IsScavengingOutpost(outpost.def))
                return outpost.WorkerPawnCount;
            return outpost.GetTotalRelevantSkill();
        }

        private static float GetProductionRelevantSkillSumRaw(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 0f;
            if (Outpost_Production_Utils.IsScavengingOutpost(outpost.def))
                return outpost.WorkerPawnCount;
            return outpost.GetTotalRelevantSkillRaw();
        }

        private static void CountStoredTransport(WorldObject_WD_Outpost outpost, out int animals, out int vehicles)
        {
            animals = 0;
            vehicles = 0;
            List<Pawn> stored = outpost?.StoredAnimalsAndVehicles;
            if (stored != null)
            {
                for (int i = 0; i < stored.Count; i++)
                {
                    Pawn p = stored[i];
                    if (p == null || p.Destroyed || p.Dead) continue;
                    if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(p))
                        vehicles++;
                    else
                        animals++;
                }
            }

            if (ModsConfig.OdysseyActive)
            {
                List<Thing> shuttles = outpost?.StoredPassengerShuttles;
                if (shuttles != null)
                {
                    for (int i = 0; i < shuttles.Count; i++)
                    {
                        Thing t = shuttles[i];
                        if (t != null && !t.Destroyed && OdysseyShuttleOutpostEstablishmentCompat.IsPassengerShuttle(t))
                            vehicles++;
                    }
                }
            }
        }

        private static string BuildHospitalHealUpgradeLines(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.BuiltUpgradeLevels == null || outpost.BuiltUpgradeLevels.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var kv in outpost.BuiltUpgradeLevels.OrderBy(x => x.Key))
            {
                if (kv.Value <= 0) continue;
                var upgradeDef = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (upgradeDef == null || upgradeDef.category != OutpostUpgradeCategory.Hospital) continue;
                float b = upgradeDef.offensiveRecoveryBonus * kv.Value;
                if (Mathf.Abs(b) < 1e-6f) continue;
                int pct = Mathf.RoundToInt(b * 100f);
                string signed = (pct >= 0 ? "+" : "") + pct.ToString() + "%";
                sb.AppendLine("TSA_WD_ProductivityTooltip_MutatorLine".Translate(upgradeDef.LabelCap, signed).ToString());
            }
            return sb.ToString().TrimEnd();
        }

        private static string BuildUpgradeLines(WorldObject_WD_Outpost outpost, Func<OutpostUpgradeDef, float> bonusPerLevel, bool asPercentPoints)
        {
            if (outpost?.BuiltUpgradeLevels == null || outpost.BuiltUpgradeLevels.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var kv in outpost.BuiltUpgradeLevels.OrderBy(x => x.Key))
            {
                if (kv.Value <= 0) continue;
                var upgradeDef = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (upgradeDef == null) continue;
                float b = bonusPerLevel(upgradeDef) * kv.Value;
                if (Mathf.Abs(b) < 1e-6f) continue;
                string signed = asPercentPoints
                    ? ((Mathf.RoundToInt(b * 100f) >= 0 ? "+" : "") + Mathf.RoundToInt(b * 100f) + " pp")
                    : ((b >= 0f ? "+" : "") + b.ToString("F0"));
                sb.AppendLine("TSA_WD_ProductivityTooltip_MutatorLine".Translate(upgradeDef.LabelCap, signed).ToString());
            }
            return sb.ToString().TrimEnd();
        }

        private static string FormatPercentagePointsValue(float bonusFraction)
        {
            if (Mathf.Abs(bonusFraction) < 1e-6f) return "—";
            int pp = Mathf.RoundToInt(bonusFraction * 100f);
            return (pp >= 0 ? "+" : "") + pp + " pp";
        }

        private static string AppendUpgradeBlock(string baseTip, string upgradeLines)
        {
            if (string.IsNullOrEmpty(upgradeLines)) return baseTip ?? "";
            var sb = new StringBuilder(baseTip ?? "");
            if (sb.Length > 0) sb.AppendLine().AppendLine();
            sb.AppendLine("TSA_WD_ProductivityTooltip_OutpostUpgradesHeader".Translate());
            sb.Append(upgradeLines);
            return sb.ToString();
        }

        /// <summary>Green value color when an upgrade or expert currently boosts this metric.</summary>
        private static void MarkBoosted(OutpostStatRow row)
        {
            if (row == null) return;
            row.ValueColor = Color.green;
        }

        private static void MarkBoostedIf(OutpostStatRow row, bool boosted)
        {
            if (boosted) MarkBoosted(row);
        }

        private static void AddRow(OutpostStatsSection section, string labelKey, string value, string tipKey)
        {
            string tip = string.IsNullOrEmpty(tipKey) ? "" : tipKey.Translate().ToString();
            AddRowWithLabel(section, labelKey.Translate().ToString(), value, tip);
        }

        private static void AddRowRawTip(OutpostStatsSection section, string labelKey, string value, string rawTooltip)
        {
            AddRowWithLabel(section, labelKey.Translate().ToString(), value, rawTooltip ?? "");
        }

        /// <summary><paramref name="tip"/> is a ready-to-display tooltip (already translated), not a key.</summary>
        private static OutpostStatRow AddRowReturn(OutpostStatsSection section, string labelKey, string value, string tip)
        {
            var row = new OutpostStatRow
            {
                Label = labelKey.Translate().ToString(),
                Value = value ?? "—",
                Tooltip = tip ?? ""
            };
            section.Rows.Add(row);
            return row;
        }

        private static void AddRow(OutpostStatsSection section, string labelKey, string value, string tipKey, Color? valueColor)
        {
            AddRow(section, labelKey, value, tipKey);
            if (section.Rows.Count > 0)
                section.Rows[section.Rows.Count - 1].ValueColor = valueColor;
        }

        private static void AddRowWithLabel(OutpostStatsSection section, string label, string value, string tooltip)
        {
            section.Rows.Add(new OutpostStatRow
            {
                Label = label ?? "",
                Value = value ?? "—",
                Tooltip = tooltip ?? ""
            });
        }
    }
}
