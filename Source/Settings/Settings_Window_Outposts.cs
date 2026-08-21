using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Merged Outpost + Food/Logistics settings. Scrollable (fixed tall inner rect like Dialog_DiplomacySettings).</summary>
    public class Dialog_FoodSettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;
        private bool generalExpanded = true;
        private bool establishmentExpanded;
        private bool productionExpanded;
        private bool foodExpanded;
        private bool conquestExpanded;

        public override Vector2 InitialSize => new Vector2(850f, 750f);

        public Dialog_FoodSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnOutpostSettings".Translate();
            optionalTitle = null;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            float contentWidth = contentRect.width - 24f;
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 2300f);

            Widgets.BeginScrollView(contentRect, ref scrollPosition, scrollViewRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(scrollViewRect);
            var s = WorldDominationMod.settings;
            bool advanced = s.showAdvancedSettings;
            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => { s.ResetOutpost(); s.ResetFoodLogistics(); },
                () => { generalExpanded = establishmentExpanded = productionExpanded = foodExpanded = conquestExpanded = true; },
                () => { generalExpanded = establishmentExpanded = productionExpanded = foodExpanded = conquestExpanded = false; });

            // --- GENERAL SETTINGS ---
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Outpost_HeaderGeneral".Translate(), ref generalExpanded, SettingsUI.SectionHeaderColor))
            {
            l.CheckboxLabeled("TSA_WD_Outpost_EnableLaunchAttack".Translate(), ref s.enableOutpostLaunchAttack,
                SettingsUI.TooltipWithDefault("TSA_WD_Outpost_EnableLaunchAttackTooltip".Translate(), WorldDominationSettings.DefEnableOutpostLaunchAttack));
            l.CheckboxLabeled("TSA_WD_Outpost_EnableBuildRoads".Translate(), ref s.enableOutpostBuildRoads,
                SettingsUI.TooltipWithDefault("TSA_WD_Outpost_EnableBuildRoadsTooltip".Translate(), WorldDominationSettings.DefEnableOutpostBuildRoads));
            l.CheckboxLabeled("TSA_WD_Outpost_EnableBuildRoadBlocks".Translate(), ref s.enableOutpostBuildRoadBlocks,
                SettingsUI.TooltipWithDefault("TSA_WD_Outpost_EnableBuildRoadBlocksTooltip".Translate(), WorldDominationSettings.DefEnableOutpostBuildRoadBlocks));
            l.CheckboxLabeled("TSA_WD_Outpost_EnableBuildTraps".Translate(), ref s.enableOutpostBuildTraps,
                SettingsUI.TooltipWithDefault("TSA_WD_Outpost_EnableBuildTrapsTooltip".Translate(), WorldDominationSettings.DefEnableOutpostBuildTraps));
            s.outpostMinDistanceTiles = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_MinDistance".Translate(), (float)s.outpostMinDistanceTiles, 1f, 20f,
                "TSA_WD_Outpost_MinDistanceTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefOutpostMinDistanceTiles);
            s.outpostBuildCostMultiplier = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_BuildCostMult".Translate(), s.outpostBuildCostMultiplier, 0.0f, 3f,
                "TSA_WD_Outpost_BuildCostMultTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefOutpostBuildCostMultiplier);
            s.cooldownPlayerOutpostRaidDays = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_RaidProtectionCooldownDays".Translate(), s.cooldownPlayerOutpostRaidDays, 0.1f, 30f,
                "TSA_WD_Outpost_RaidProtectionCooldownDaysTooltip".Translate(), 0.1f, SliderFormat.Fixed1, WorldDominationSettings.DefCooldownPlayerOutpostRaidDays);
            s.raidTargetRadius = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_PlayerOutpostAttackRange".Translate(), s.raidTargetRadius, 5f, 200f,
                "TSA_WD_Raid_PlayerOutpostAttackRangeTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefRaidTargetRadius);
            s.playerOutpostBaseDefensiveStrength = SettingsUI.LabeledSlider(l, "TSA_WD_Growth_PlayerOutpostDefensiveStrength".Translate(), s.playerOutpostBaseDefensiveStrength, 0f, 1000f,
                "TSA_WD_Growth_PlayerOutpostDefensiveStrengthTooltip".Translate(), 10f, SliderFormat.Fixed0, WorldDominationSettings.DefPlayerOutpostBaseDefensiveStrength);
            bool pollutionEcologyBefore = s.pollutionEcologyPenaltyEnabled;
            l.CheckboxLabeled("TSA_WD_Outpost_PollutionEcologyPenalty".Translate(), ref s.pollutionEcologyPenaltyEnabled,
                SettingsUI.TooltipWithDefault("TSA_WD_Outpost_PollutionEcologyPenaltyTooltip".Translate(), WorldDominationSettings.DefPollutionEcologyPenaltyEnabled));
            if (pollutionEcologyBefore != s.pollutionEcologyPenaltyEnabled)
                WD_WorldLayer_ProductivityOverlay.InvalidateAndDirtyIfActive();
            l.CheckboxLabeled("TSA_WD_Outpost_UpgradesCostMaterials".Translate(), ref s.outpostUpgradesCostMaterials,
                SettingsUI.TooltipWithDefault("TSA_WD_Outpost_UpgradesCostMaterialsTooltip".Translate(), WorldDominationSettings.DefOutpostUpgradesCostMaterials));
            l.CheckboxLabeled("TSA_WD_Outpost_UpgradesRequireResearch".Translate(), ref s.outpostUpgradesRequireResearch,
                SettingsUI.TooltipWithDefault("TSA_WD_Outpost_UpgradesRequireResearchTooltip".Translate(), WorldDominationSettings.DefOutpostUpgradesRequireResearch));
            }
            l.Gap(12f);

            // --- ESTABLISHMENT REQUIREMENTS ---
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Outpost_HeaderEstablishment".Translate(), ref establishmentExpanded, SettingsUI.SectionHeaderColor))
            {
                l.CheckboxLabeled("TSA_WD_Outpost_ReqBiome".Translate(), ref s.outpostReqBiome, SettingsUI.TooltipWithDefault("TSA_WD_Outpost_ReqBiome_Tooltip".Translate(), WorldDominationSettings.DefOutpostReqBiome));
                l.CheckboxLabeled("TSA_WD_Outpost_ReqFertility".Translate(), ref s.outpostReqFertility, SettingsUI.TooltipWithDefault("TSA_WD_Outpost_ReqFertility_Tooltip".Translate(), WorldDominationSettings.DefOutpostReqFertility));
                l.CheckboxLabeled("TSA_WD_Outpost_ReqAnimals".Translate(), ref s.outpostReqAnimalAbundance, SettingsUI.TooltipWithDefault("TSA_WD_Outpost_ReqAnimals_Tooltip".Translate(), WorldDominationSettings.DefOutpostReqAnimalAbundance));
                l.CheckboxLabeled("TSA_WD_Outpost_ReqFish".Translate(), ref s.outpostReqFishAbundance, SettingsUI.TooltipWithDefault("TSA_WD_Outpost_ReqFish_Tooltip".Translate(), WorldDominationSettings.DefOutpostReqFishAbundance));
                l.CheckboxLabeled("TSA_WD_Outpost_ReqMining".Translate(), ref s.outpostReqMiningTerrain, SettingsUI.TooltipWithDefault("TSA_WD_Outpost_ReqMining_Tooltip".Translate(), WorldDominationSettings.DefOutpostReqMiningTerrain));
                l.CheckboxLabeled("TSA_WD_Outpost_ReqResearch".Translate(), ref s.outpostReqResearch, SettingsUI.TooltipWithDefault("TSA_WD_Outpost_ReqResearch_Tooltip".Translate(), WorldDominationSettings.DefOutpostReqResearch));
                l.CheckboxLabeled("TSA_WD_Outpost_ReqNearby".Translate(), ref s.outpostReqNearbySettlements, SettingsUI.TooltipWithDefault("TSA_WD_Outpost_ReqNearby_Tooltip".Translate(), WorldDominationSettings.DefOutpostReqNearbySettlements));
                l.CheckboxLabeled("TSA_WD_Outpost_ReqMinPawns".Translate(), ref s.outpostReqMinPawns, SettingsUI.TooltipWithDefault("TSA_WD_Outpost_ReqMinPawns_Tooltip".Translate(), WorldDominationSettings.DefOutpostReqMinPawns));
                l.CheckboxLabeled("TSA_WD_Outpost_ReqMinSkill".Translate(), ref s.outpostReqMinSkill, SettingsUI.TooltipWithDefault("TSA_WD_Outpost_ReqMinSkill_Tooltip".Translate(), WorldDominationSettings.DefOutpostReqMinSkill));
                l.CheckboxLabeled("TSA_WD_Outpost_ReqCost".Translate(), ref s.outpostReqCost, SettingsUI.TooltipWithDefault("TSA_WD_Outpost_ReqCost_Tooltip".Translate(), WorldDominationSettings.DefOutpostReqCost));
            }
            l.Gap(12f);

            // --- OUTPOST PRODUCTION (core) ---
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Outpost_HeaderProduction".Translate(), ref productionExpanded, SettingsUI.SectionHeaderColor))
            {
            s.outpostProductionTimeMultiplier = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_ProductionTimeMult".Translate(), s.outpostProductionTimeMultiplier, 0.01f, 4f,
                "TSA_WD_Outpost_ProductionTimeMultTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefOutpostProductionTimeMultiplier);
            s.outpostProductionOutputMultiplier = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_ProductionOutputMult".Translate(), s.outpostProductionOutputMultiplier, 0.01f, 4f,
                "TSA_WD_Outpost_ProductionOutputMultTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefOutpostProductionOutputMultiplier);
            float prevAuraPct = s.warehouseAuraBonusPct;
            float prevAuraRad = s.warehouseAuraRadiusTiles;
            s.warehouseAuraBonusPct = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_WarehouseAuraBonus".Translate(), s.warehouseAuraBonusPct, 0f, 1f,
                "TSA_WD_Outpost_WarehouseAuraBonusTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefWarehouseAuraBonusPct);
            s.warehouseAuraRadiusTiles = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_WarehouseAuraRadius".Translate(), s.warehouseAuraRadiusTiles, 0f, 80f,
                "TSA_WD_Outpost_WarehouseAuraRadiusTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefWarehouseAuraRadiusTiles);
            if (Mathf.Abs(prevAuraPct - s.warehouseAuraBonusPct) > 1e-6f
                || Mathf.Abs(prevAuraRad - s.warehouseAuraRadiusTiles) > 1e-6f)
                OutpostWarehouseAuraUtility.InvalidateCache();
            bool embassyHostilesBefore = s.embassyMayGainGoodwillWithHostiles;
            l.CheckboxLabeled("TSA_WD_Outpost_EmbassyHostileGoodwill".Translate(), ref s.embassyMayGainGoodwillWithHostiles,
                SettingsUI.TooltipWithDefault("TSA_WD_Outpost_EmbassyHostileGoodwillTooltip".Translate(), WorldDominationSettings.DefEmbassyMayGainGoodwillWithHostiles));
            if (embassyHostilesBefore != s.embassyMayGainGoodwillWithHostiles)
                Outpost_Embassy.ClearAllProbeCaches();
            s.outpostSilverValuePerSkillPerCycle = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_SilverValuePerSkill".Translate(), s.outpostSilverValuePerSkillPerCycle, 20f, 300f,
                "TSA_WD_Outpost_SilverValuePerSkillTooltip".Translate(), 10f, SliderFormat.Fixed0, WorldDominationSettings.DefOutpostSilverValuePerSkillPerCycle);
            l.CheckboxLabeled("TSA_WD_Outpost_ClampSkillsAtLevel20".Translate(), ref s.clampOutpostSkillsAtLevel20,
                SettingsUI.TooltipWithDefault("TSA_WD_Outpost_ClampSkillsAtLevel20Tooltip".Translate(), WorldDominationSettings.DefClampOutpostSkillsAtLevel20));
            s.outpostOccupantSkillXpPerProductionCycle = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_OccupantSkillXpPerCycle".Translate(), s.outpostOccupantSkillXpPerProductionCycle, 0f, 20000f,
                "TSA_WD_Outpost_OccupantSkillXpPerCycleTooltip".Translate(), 50f, SliderFormat.Fixed0, WorldDominationSettings.DefOutpostOccupantSkillXpPerProductionCycle);
            s.outpostOccupantSkillXpMaxLevel = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_OccupantSkillXpMaxLevel".Translate(), s.outpostOccupantSkillXpMaxLevel, 0f, 20f,
                "TSA_WD_Outpost_OccupantSkillXpMaxLevelTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefOutpostOccupantSkillXpMaxLevel);
            l.Gap(8f);
            SettingsUI.DrawHeader(l, "TSA_WD_Outpost_HeaderAcademy".Translate());
            s.academyBaseXpPerDay = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_AcademyBaseXpPerDay".Translate(), s.academyBaseXpPerDay, 1f, 10000f,
                "TSA_WD_Outpost_AcademyBaseXpPerDayTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefAcademyBaseXpPerDay);
            s.academyMinTeacherSkill = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_AcademyMinTeacherSkill".Translate(), s.academyMinTeacherSkill, 0f, 20f,
                "TSA_WD_Outpost_AcademyMinTeacherSkillTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefAcademyMinTeacherSkill);
            s.academyTeachCapOffset = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_AcademyTeachCapOffset".Translate(), s.academyTeachCapOffset, 0f, 20f,
                "TSA_WD_Outpost_AcademyTeachCapOffsetTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefAcademyTeachCapOffset);
            l.CheckboxLabeled("TSA_WD_Outpost_AcademyUseFlatDirectXp".Translate(), ref s.academyUseFlatDirectXp,
                SettingsUI.TooltipWithDefault("TSA_WD_Outpost_AcademyUseFlatDirectXpTooltip".Translate(), WorldDominationSettings.DefAcademyUseFlatDirectXp));
            l.Gap(8f);
            SettingsUI.DrawHeader(l, "TSA_WD_Outpost_HeaderExperts".Translate());
            s.expertReferenceSkillLevel = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_ExpertReferenceSkill".Translate(), s.expertReferenceSkillLevel, 1f, 40f,
                "TSA_WD_Outpost_ExpertReferenceSkillTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefExpertReferenceSkillLevel);
            s.expertStrategistMaxBonusPct = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_ExpertStrategistMax".Translate(), s.expertStrategistMaxBonusPct, 0f, 1f,
                "TSA_WD_Outpost_ExpertStrategistMaxTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefExpertStrategistMaxBonusPct);
            s.expertEntertainerMaxBonusPct = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_ExpertEntertainerMax".Translate(), s.expertEntertainerMaxBonusPct, 0f, 1f,
                "TSA_WD_Outpost_ExpertEntertainerMaxTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefExpertEntertainerMaxBonusPct);
            s.expertCookMaxBonusPct = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_ExpertCookMax".Translate(), s.expertCookMaxBonusPct, 0f, 1f,
                "TSA_WD_Outpost_ExpertCookMaxTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefExpertCookMaxBonusPct);
            s.expertDoctorMaxBonusPct = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_ExpertDoctorMax".Translate(), s.expertDoctorMaxBonusPct, 0f, 1f,
                "TSA_WD_Outpost_ExpertDoctorMaxTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefExpertDoctorMaxBonusPct);
            s.expertEngineerMaxBonusPct = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_ExpertEngineerMax".Translate(), s.expertEngineerMaxBonusPct, 0f, 1f,
                "TSA_WD_Outpost_ExpertEngineerMaxTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefExpertEngineerMaxBonusPct);
            s.expertEngineerConstructionRadiusMaxBonusPct = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_ExpertEngineerRadiusMax".Translate(), s.expertEngineerConstructionRadiusMaxBonusPct, 0f, 1f,
                "TSA_WD_Outpost_ExpertEngineerRadiusMaxTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefExpertEngineerConstructionRadiusMaxBonusPct);
            s.expertRecruiterMaxBonusPct = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_ExpertWardenMaxBonus".Translate(), s.expertRecruiterMaxBonusPct, 0f, 1f,
                "TSA_WD_Outpost_ExpertWardenMaxBonusTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefExpertRecruiterMaxBonusPct);
            if (advanced)
            {
                l.Gap(8f);
                SettingsUI.DrawHeader(l, "TSA_WD_Outpost_HeaderStrengthRecovery".Translate());
                s.outpostDefensiveRecoveryFractionPerDay = SettingsUI.LabeledSlider(l,
                    "TSA_WD_Outpost_StrengthRecovery_DefFraction".Translate(), s.outpostDefensiveRecoveryFractionPerDay, 0.01f, 0.50f,
                    "TSA_WD_Outpost_StrengthRecovery_DefFractionTooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefOutpostDefensiveRecoveryFractionPerDay);
                s.outpostOffensiveRecoveryFractionPerDay = SettingsUI.LabeledSlider(l,
                    "TSA_WD_Outpost_StrengthRecovery_OffFraction".Translate(), s.outpostOffensiveRecoveryFractionPerDay, 0.01f, 0.50f,
                    "TSA_WD_Outpost_StrengthRecovery_OffFractionTooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefOutpostOffensiveRecoveryFractionPerDay);
                s.outpostDefensiveRecoveryMinFlatPerDay = SettingsUI.LabeledSlider(l,
                    "TSA_WD_Outpost_StrengthRecovery_DefMinFlat".Translate(), s.outpostDefensiveRecoveryMinFlatPerDay, 0f, 300f,
                    "TSA_WD_Outpost_StrengthRecovery_DefMinFlatTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefOutpostDefensiveRecoveryMinFlatPerDay);
                s.outpostOffensiveRecoveryMinFlatPerDay = SettingsUI.LabeledSlider(l,
                    "TSA_WD_Outpost_StrengthRecovery_OffMinFlat".Translate(), s.outpostOffensiveRecoveryMinFlatPerDay, 0f, 300f,
                    "TSA_WD_Outpost_StrengthRecovery_OffMinFlatTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefOutpostOffensiveRecoveryMinFlatPerDay);
                s.outpostOccupantHealSeverityPerDay = SettingsUI.LabeledSlider(l,
                    "TSA_WD_Outpost_OccupantHealSeverity".Translate(), s.outpostOccupantHealSeverityPerDay, 0f, 10f,
                    "TSA_WD_Outpost_OccupantHealSeverityTooltip".Translate(), 0.1f, SliderFormat.Fixed1,
                    WorldDominationSettings.DefOutpostOccupantHealSeverityPerDay);
            }
            }
            l.Gap(12f);

            // --- Food logistics ---
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Food_Header".Translate(), ref foodExpanded, SettingsUI.SectionHeaderColor))
            {
            l.CheckboxLabeled("TSA_WD_Food_Active".Translate(), ref s.foodLogisticsActive,
                SettingsUI.TooltipWithDefault("TSA_WD_Food_ActiveTooltip".Translate(), WorldDominationSettings.DefFoodLogisticsActive));

            if (s.foodLogisticsActive)
            {
                if (advanced)
                {
                l.Gap(6f);

                s.foodConsumptionPerPawn = SettingsUI.LabeledSlider(l,
                    "TSA_WD_Food_ConsPawn".Translate(), s.foodConsumptionPerPawn, 0.1f, 10f,
                    "TSA_WD_Food_ConsPawnTooltip".Translate(), 0.1f, SliderFormat.Fixed1, WorldDominationSettings.DefFoodConsumptionPerPawn);

                s.foodProductionPerOutpostBase = SettingsUI.LabeledSlider(l,
                    "TSA_WD_Food_ProdPerOutpostBase".Translate(), s.foodProductionPerOutpostBase, 0f, 20f,
                    "TSA_WD_Food_ProdPerOutpostBaseTooltip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefFoodProductionPerOutpostBase);

                s.virtualFoodTileMultiplierFloor = SettingsUI.LabeledSlider(l,
                    "TSA_WD_Food_VirtualTileMinMult".Translate(), s.virtualFoodTileMultiplierFloor, 0f, 1f,
                    "TSA_WD_Food_VirtualTileMinMultTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefVirtualFoodTileMultiplierFloor);

                s.maxFoodPerOutpost = SettingsUI.LabeledSlider(l,
                    "TSA_WD_Food_MaxFoodPerOutpost".Translate(), s.maxFoodPerOutpost, 50f, 1000f,
                    "TSA_WD_Food_MaxFoodPerOutpostTooltip".Translate(), 25f, SliderFormat.Fixed0, WorldDominationSettings.DefMaxFoodPerOutpost);

                s.maxLogisticsRange = (int)SettingsUI.LabeledSlider(l,
                    "TSA_WD_Food_MaxRange".Translate(), s.maxLogisticsRange, 5f, 50f,
                    "TSA_WD_Food_MaxRangeTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefMaxLogisticsRange);
                }
            }
            else
            {
                l.Gap(10f);
                GUI.color = Color.gray;
                l.Label("  <i>" + "TSA_WD_Food_DisabledWarning".Translate() + "</i>");
                GUI.color = Color.white;
            }
            }

            l.Gap(18f);
            // --- OUTPOST AFTER CONQUEST (bottom of scroll: virtual pawns when founding on ruins) ---
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Outpost_HeaderAfterConquest".Translate(), ref conquestExpanded, SettingsUI.SectionHeaderColor))
            {
            l.CheckboxLabeled("TSA_WD_Outpost_AfterConquestEnabled".Translate(), ref s.outpostAfterConquestEnabled,
                SettingsUI.TooltipWithDefault("TSA_WD_Outpost_AfterConquestEnabledTooltip".Translate(), WorldDominationSettings.DefOutpostAfterConquestEnabled));
            if (s.outpostAfterConquestEnabled)
            {
                s.conquestFoundingPawnsT1 = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_ConquestPawnsT1".Translate(), s.conquestFoundingPawnsT1, 0f, 40f,
                    "TSA_WD_Outpost_ConquestPawnsT1Tooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefConquestFoundingPawnsT1);
                s.conquestFoundingPawnsT2 = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_ConquestPawnsT2".Translate(), s.conquestFoundingPawnsT2, 0f, 40f,
                    "TSA_WD_Outpost_ConquestPawnsT2Tooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefConquestFoundingPawnsT2);
                s.conquestFoundingPawnsT3 = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_ConquestPawnsT3".Translate(), s.conquestFoundingPawnsT3, 0f, 40f,
                    "TSA_WD_Outpost_ConquestPawnsT3Tooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefConquestFoundingPawnsT3);
                s.conquestFoundingPawnsT4 = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_ConquestPawnsT4".Translate(), s.conquestFoundingPawnsT4, 0f, 40f,
                    "TSA_WD_Outpost_ConquestPawnsT4Tooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefConquestFoundingPawnsT4);
                s.conquestFoundingMinRelevantSkill = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_ConquestMinRelevantSkill".Translate(), s.conquestFoundingMinRelevantSkill, 0f, 20f,
                    "TSA_WD_Outpost_ConquestMinRelevantSkillTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefConquestFoundingMinRelevantSkill);
            }
            else
            {
                l.Gap(6f);
                GUI.color = Color.gray;
                l.Label("  <i>" + "TSA_WD_Outpost_AfterConquestDisabledHint".Translate() + "</i>");
                GUI.color = Color.white;
            }
            }

            l.End();
            Widgets.EndScrollView();
        }
    }
}