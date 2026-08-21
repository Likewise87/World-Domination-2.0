using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_ExperimentalSettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;
        private bool worldActionsExpanded = true;
        private bool targetOfOpportunityExpanded = true;
        private bool maraudingExpanded = true;
        private bool settlementAmbushExpanded = true;
        private bool iconsExpanded = true;
        private bool controlsExpanded = true;
        private bool pollutionExpanded = true;
        private bool upkeepExpanded = true;
        private bool baseGenerationExpanded = true;

        public override Vector2 InitialSize => new Vector2(850f, 700f);

        public Dialog_ExperimentalSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnExperimental".Translate();
            optionalTitle = null;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            float contentWidth = contentRect.width - 24f;
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 2850f);

            Widgets.BeginScrollView(contentRect, ref scrollPosition, scrollViewRect);
            Listing_Standard l = new Listing_Standard();
            l.Begin(scrollViewRect);

            var s = WorldDominationMod.settings;
            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetExperimental(),
                () =>
                {
                    worldActionsExpanded = targetOfOpportunityExpanded = maraudingExpanded =
                        settlementAmbushExpanded = iconsExpanded = controlsExpanded =
                        pollutionExpanded = upkeepExpanded = baseGenerationExpanded = true;
                },
                () =>
                {
                    worldActionsExpanded = targetOfOpportunityExpanded = maraudingExpanded =
                        settlementAmbushExpanded = iconsExpanded = controlsExpanded =
                        pollutionExpanded = upkeepExpanded = baseGenerationExpanded = false;
                });

            SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_OutpostWithdrawStrengthBudget".Translate(),
                ref s.experimentalOutpostWithdrawStrengthBudget,
                "TSA_WD_Experimental_OutpostWithdrawStrengthBudgetTip".Translate(),
                defaultValue: WorldDominationSettings.DefExperimentalOutpostWithdrawStrengthBudget);
            SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_OutpostDefenseDeployBudget".Translate(),
                ref s.experimentalOutpostDefenseDeployBudget,
                "TSA_WD_Experimental_OutpostDefenseDeployBudgetTip".Translate(),
                defaultValue: WorldDominationSettings.DefExperimentalOutpostDefenseDeployBudget);

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Experimental_HeaderBaseGeneration".Translate(), ref baseGenerationExpanded, SettingsUI.SectionHeaderColor))
            {
                SettingsUI.DrawCheckbox(l, "TSA_WD_Garrison_AdaptiveTerrainPrep".Translate(),
                    ref s.kcsgAdaptiveTerrainPrep,
                    "TSA_WD_Garrison_AdaptiveTerrainPrepTooltip".Translate(),
                    defaultValue: WorldDominationSettings.DefKcsgAdaptiveTerrainPrep);
                if (s.kcsgAdaptiveTerrainPrep)
                {
                    SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_AlwaysClearKcsgRect".Translate(),
                        ref s.experimentalAlwaysClearKcsgRect,
                        "TSA_WD_Experimental_AlwaysClearKcsgRectTip".Translate(),
                        defaultValue: WorldDominationSettings.DefExperimentalAlwaysClearKcsgRect);
                    if (!s.experimentalAlwaysClearKcsgRect)
                    {
                        s.kcsgBlockedFlattenThreshold = SettingsUI.LabeledSlider(l, "TSA_WD_Garrison_BlockedFlattenThreshold".Translate(), s.kcsgBlockedFlattenThreshold, 0.05f, 0.75f,
                            "TSA_WD_Garrison_BlockedFlattenThresholdTooltip".Translate(), 0.05f, SliderFormat.PercentDecimal, WorldDominationSettings.DefKcsgBlockedFlattenThreshold);
                    }
                    SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_KcsgRectBlend".Translate(),
                        ref s.experimentalKcsgRectBlend,
                        "TSA_WD_Experimental_KcsgRectBlendTip".Translate(),
                        defaultValue: WorldDominationSettings.DefExperimentalKcsgRectBlend);
                }
            }

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Experimental_HeaderTargetOfOpportunity".Translate(), ref targetOfOpportunityExpanded, SettingsUI.SectionHeaderColor))
            {
                SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_TargetOfOpportunity".Translate(),
                    ref s.experimentalTargetOfOpportunity,
                    "TSA_WD_Experimental_TargetOfOpportunityTip".Translate(),
                    defaultValue: WorldDominationSettings.DefExperimentalTargetOfOpportunity);
                if (s.experimentalTargetOfOpportunity)
                {
                    s.targetOfOpportunityEligibilityRollPct = SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_ToORollPct".Translate(), s.targetOfOpportunityEligibilityRollPct, 0f, 1f,
                        "TSA_WD_Experimental_ToORollPctTip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefTargetOfOpportunityEligibilityRollPct);
                    s.targetOfOpportunityMinRatioAdvantage = SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_ToOMinRatioAdvantage".Translate(), s.targetOfOpportunityMinRatioAdvantage, 0f, 2f,
                        "TSA_WD_Experimental_ToOMinRatioAdvantageTip".Translate(), 0.05f, SliderFormat.Fixed2, WorldDominationSettings.DefTargetOfOpportunityMinRatioAdvantage);
                    s.targetOfOpportunityMaxRetargets = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_ToOMaxRetargets".Translate(), s.targetOfOpportunityMaxRetargets, 0f, 10f,
                        "TSA_WD_Experimental_ToOMaxRetargetsTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefTargetOfOpportunityMaxRetargets));
                    s.targetChangesMaxLifetime = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_TargetChangesMaxLifetime".Translate(), s.targetChangesMaxLifetime, 0f, 15f,
                        "TSA_WD_Experimental_TargetChangesMaxLifetimeTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefTargetChangesMaxLifetime));
                }
            }

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Experimental_HeaderMarauding".Translate(), ref maraudingExpanded, SettingsUI.SectionHeaderColor))
            {
                SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_ContinueAfterConquest".Translate(),
                    ref s.experimentalContinueAfterConquest,
                    "TSA_WD_Experimental_ContinueAfterConquestTip".Translate(),
                    defaultValue: WorldDominationSettings.DefExperimentalContinueAfterConquest);
                if (s.experimentalContinueAfterConquest)
                {
                    s.maraudingChanceToOccurPct = SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_MaraudChancePct".Translate(), s.maraudingChanceToOccurPct, 0f, 1f,
                        "TSA_WD_Experimental_MaraudChancePctTip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefMaraudingChanceToOccurPct);
                    s.maraudingMinSurvivingStrengthAbsolute = SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_MaraudMinStrength".Translate(), s.maraudingMinSurvivingStrengthAbsolute, 200f, 2000f,
                        "TSA_WD_Experimental_MaraudMinStrengthTip".Translate(), 5f, SliderFormat.Fixed0, WorldDominationSettings.DefMaraudingMinSurvivingStrengthAbsolute);
                    s.maraudingMaxChainedTargets = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_MaraudMaxChain".Translate(), s.maraudingMaxChainedTargets, 0f, 10f,
                        "TSA_WD_Experimental_MaraudMaxChainTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefMaraudingMaxChainedTargets));
                }
            }

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Experimental_HeaderSettlementAmbush".Translate(), ref settlementAmbushExpanded, SettingsUI.SectionHeaderColor))
            {
                SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_SettlementAmbush".Translate(),
                    ref s.experimentalSettlementAmbush,
                    "TSA_WD_Experimental_SettlementAmbushTip".Translate(),
                    defaultValue: WorldDominationSettings.DefExperimentalSettlementAmbush);
                if (s.experimentalSettlementAmbush)
                {
                    s.settlementAmbushChancePct = SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_AmbushChancePct".Translate(), s.settlementAmbushChancePct, 0f, 1f,
                        "TSA_WD_Experimental_AmbushChancePctTip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefSettlementAmbushChancePct);
                    s.settlementAmbushMinStrengthRatio = SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_AmbushMinRatio".Translate(), s.settlementAmbushMinStrengthRatio, 0f, 3f,
                        "TSA_WD_Experimental_AmbushMinRatioTip".Translate(), 0.05f, SliderFormat.Fixed2, WorldDominationSettings.DefSettlementAmbushMinStrengthRatio);
                    s.settlementAmbushMaxStrengthRatio = SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_AmbushMaxRatio".Translate(), s.settlementAmbushMaxStrengthRatio, RapidResponseUtility.MinMaxStrengthRatio, RapidResponseUtility.MaxMaxStrengthRatio,
                        "TSA_WD_Experimental_AmbushMaxRatioTip".Translate(), 0.05f, SliderFormat.Multiplier, WorldDominationSettings.DefSettlementAmbushMaxStrengthRatio);
                    SettlementTier prevMinTier = s.settlementAmbushMinTier;
                    s.settlementAmbushMinTier = DrawAmbushMinTierSlider(l, s.settlementAmbushMinTier);
                    if (s.settlementAmbushMinTier != prevMinTier)
                        WorldComponent_SettlementWatchIndex.Get()?.Invalidate();
                    s.settlementAmbushMaxConcurrent = DrawAmbushMaxConcurrentSlider(l, s.settlementAmbushMaxConcurrent);
                    float prevWatchRange = s.settlementAmbushWatchRangeTiles;
                    s.settlementAmbushWatchRangeTiles = SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_AmbushWatchRange".Translate(), s.settlementAmbushWatchRangeTiles, 1f, 40f,
                        "TSA_WD_Experimental_AmbushWatchRangeTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefSettlementAmbushWatchRangeTiles);
                    if (!Mathf.Approximately(prevWatchRange, s.settlementAmbushWatchRangeTiles))
                        WorldComponent_SettlementWatchIndex.Get()?.Invalidate();
                }
            }

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Experimental_HeaderWorldActionsRaidLogic".Translate(), ref worldActionsExpanded, SettingsUI.SectionHeaderColor))
            {
                SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_ColonyWorldBuild".Translate(),
                    ref s.experimentalColonyWorldBuild,
                    "TSA_WD_Experimental_ColonyWorldBuildTip".Translate(),
                    defaultValue: WorldDominationSettings.DefExperimentalColonyWorldBuild);
                SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_PlayerConquestRaze".Translate(),
                    ref s.experimentalPlayerConquestRaze,
                    "TSA_WD_Experimental_PlayerConquestRazeTip".Translate(),
                    defaultValue: WorldDominationSettings.DefExperimentalPlayerConquestRaze);
                SettingsUI.DrawCheckbox(l, "TSA_WD_Quest_EnableFirstOutpost".Translate(),
                    ref s.enableFirstOutpostQuest,
                    "TSA_WD_Quest_EnableFirstOutpostTip".Translate(),
                    defaultValue: WorldDominationSettings.DefEnableFirstOutpostQuest);
                SettingsUI.DrawCheckbox(l, "TSA_WD_Quest_EnableCommonEnemySettlement".Translate(),
                    ref s.enableCommonEnemySettlementQuest,
                    "TSA_WD_Quest_EnableCommonEnemySettlementTip".Translate(),
                    defaultValue: WorldDominationSettings.DefEnableCommonEnemySettlementQuest);
                SettingsUI.DrawCheckbox(l, "TSA_WD_Quest_EnableColonyRoadLink".Translate(),
                    ref s.enableColonyRoadLinkQuest,
                    "TSA_WD_Quest_EnableColonyRoadLinkTip".Translate(),
                    defaultValue: WorldDominationSettings.DefEnableColonyRoadLinkQuest);
                bool victoryQuestWas = s.enableWorldDominationVictoryQuest;
                SettingsUI.DrawCheckbox(l, "TSA_WD_Quest_EnableWorldDominationVictory".Translate(),
                    ref s.enableWorldDominationVictoryQuest,
                    "TSA_WD_Quest_EnableWorldDominationVictoryTip".Translate(),
                    defaultValue: WorldDominationSettings.DefEnableWorldDominationVictoryQuest);
                if (Current.ProgramState == ProgramState.Playing
                    && victoryQuestWas != s.enableWorldDominationVictoryQuest)
                {
                    if (s.enableWorldDominationVictoryQuest)
                        WdWorldDominationVictoryQuestHelper.TryLaunchNowIfEligible();
                    else
                        WdWorldDominationVictoryQuestHelper.RemoveActiveIfAny();
                }
                SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_AT_TargetPlayerTravelers".Translate(),
                    ref s.enableAtTurretTargetPlayerTravelers,
                    "TSA_WD_Experimental_AT_TargetPlayerTravelersTip".Translate(),
                    defaultValue: WorldDominationSettings.DefEnableAtTurretTargetPlayerTravelers);
                SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_AT_TargetPlayerCaravans".Translate(),
                    ref s.enableAtTurretTargetPlayerCaravans,
                    "TSA_WD_Experimental_AT_TargetPlayerCaravansTip".Translate(),
                    defaultValue: WorldDominationSettings.DefEnableAtTurretTargetPlayerCaravans);

                SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_OpportunityIgnoreEscalationGate".Translate(),
                    ref s.opportunityFeaturesIgnoreEscalationGate,
                    "TSA_WD_Experimental_OpportunityIgnoreEscalationGateTip".Translate(),
                    defaultValue: WorldDominationSettings.DefOpportunityFeaturesIgnoreEscalationGate);

                SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_EnableWorldMapSounds".Translate(),
                    ref s.enableWorldMapSounds,
                    "TSA_WD_Experimental_EnableWorldMapSoundsTip".Translate(),
                    defaultValue: WorldDominationSettings.DefEnableWorldMapSounds);
            }

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Notify_HeaderWorldMapIcons".Translate(), ref iconsExpanded, SettingsUI.SectionHeaderColor))
            {
                bool prevOutpostTravelerIcons = s.alwaysShowOutpostTravelerIconsRegardlessOfZoom;
                bool prevSettlementIcons = s.alwaysShowSettlementIconsRegardlessOfZoom;

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_AlwaysShowOutpostTravelerIcons".Translate(),
                    ref s.alwaysShowOutpostTravelerIconsRegardlessOfZoom,
                    "TSA_WD_Notify_AlwaysShowOutpostTravelerIconsTip".Translate(),
                    defaultValue: WorldDominationSettings.DefAlwaysShowOutpostTravelerIconsRegardlessOfZoom);

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_AlwaysShowSettlementIcons".Translate(),
                    ref s.alwaysShowSettlementIconsRegardlessOfZoom,
                    "TSA_WD_Notify_AlwaysShowSettlementIconsTip".Translate(),
                    defaultValue: WorldDominationSettings.DefAlwaysShowSettlementIconsRegardlessOfZoom);

                if (prevOutpostTravelerIcons != s.alwaysShowOutpostTravelerIconsRegardlessOfZoom
                    || prevSettlementIcons != s.alwaysShowSettlementIconsRegardlessOfZoom)
                {
                    Patch_WdWorldObjectNoExpandingIcon.NotifyIconModeChanged();
                }
            }

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Experimental_HeaderControls".Translate(), ref controlsExpanded, SettingsUI.SectionHeaderColor))
            {
                DrawWorldMapOverlayHoldKeyRow(l, s);
                SettingsUI.DrawCheckbox(l, "TSA_WD_AutoAdd_DefaultOn".Translate(),
                    ref s.autoAddPawnsOnArrivalDefault,
                    "TSA_WD_AutoAdd_DefaultOnTip".Translate(),
                    defaultValue: WorldDominationSettings.DefAutoAddPawnsOnArrivalDefault);
                SettingsUI.DrawCheckbox(l, "TSA_WD_TravelFood_PrisonerRecruit".Translate(),
                    ref s.giveFoodOnPrisonerRecruitTransfer,
                    "TSA_WD_TravelFood_PrisonerRecruitTip".Translate(),
                    defaultValue: WorldDominationSettings.DefGiveFoodOnPrisonerRecruitTransfer);
                SettingsUI.DrawCheckbox(l, "TSA_WD_TravelFood_AllPlayerPawns".Translate(),
                    ref s.giveFoodOnAllPlayerPawnsTransfer,
                    "TSA_WD_TravelFood_AllPlayerPawnsTip".Translate(),
                    defaultValue: WorldDominationSettings.DefGiveFoodOnAllPlayerPawnsTransfer);
                SettingsUI.DrawCheckbox(l, "TSA_WD_OutpostSim_ShowInWdMenu".Translate(),
                    ref s.showOutpostRequirementsPreviewInWdMenu,
                    "TSA_WD_OutpostSim_ShowInWdMenuTip".Translate(),
                    defaultValue: WorldDominationSettings.DefShowOutpostRequirementsPreviewInWdMenu);
            }

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Experimental_HeaderUpkeep".Translate(), ref upkeepExpanded, SettingsUI.SectionHeaderColor))
            {
                SettingsUI.DrawCheckbox(l, "TSA_WD_Upkeep_Enable".Translate(),
                    ref s.enableOutpostUpkeep,
                    "TSA_WD_Upkeep_EnableTip".Translate(),
                    defaultValue: WorldDominationSettings.DefEnableOutpostUpkeep);
                if (s.enableOutpostUpkeep)
                {
                    s.upkeepSilverPerOccupant = (int)SettingsUI.LabeledSlider(l,
                        "TSA_WD_Upkeep_SilverPerOccupant".Translate(),
                        s.upkeepSilverPerOccupant, 1f, 200f,
                        "TSA_WD_Upkeep_SilverPerOccupantTip".Translate(),
                        1f, SliderFormat.Fixed0, WorldDominationSettings.DefUpkeepSilverPerOccupant);
                    s.upkeepIntervalDays = (int)SettingsUI.LabeledSlider(l,
                        "TSA_WD_Upkeep_IntervalDays".Translate(),
                        s.upkeepIntervalDays, 1f, 60f,
                        "TSA_WD_Upkeep_IntervalDaysTip".Translate(),
                        1f, SliderFormat.Fixed0, WorldDominationSettings.DefUpkeepIntervalDays);
                }
            }

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Experimental_HeaderPollution".Translate(), ref pollutionExpanded, SettingsUI.SectionHeaderColor))
            {
                SettingsUI.DrawCheckbox(l, "TSA_WD_Pollution_EnableTravelerDamage".Translate(),
                    ref s.travelerPollutionDamageEnabled,
                    "TSA_WD_Pollution_EnableTravelerDamageTip".Translate(),
                    defaultValue: WorldDominationSettings.DefTravelerPollutionDamageEnabled);

                SettingsUI.DrawCheckbox(l, "TSA_WD_Pollution_WasterImmunity".Translate(),
                    ref s.wasterPollutionImmunityEnabled,
                    "TSA_WD_Pollution_WasterImmunityTip".Translate(),
                    defaultValue: WorldDominationSettings.DefWasterPollutionImmunityEnabled);

                if (s.travelerPollutionDamageEnabled)
                {
                    SettingsUI.DrawCheckbox(l, "TSA_WD_Pollution_DamageRaiders".Translate(),
                        ref s.pollutionDamageRaiders,
                        "TSA_WD_Pollution_DamageRaidersTip".Translate(),
                        defaultValue: WorldDominationSettings.DefPollutionDamageRaiders);
                    SettingsUI.DrawCheckbox(l, "TSA_WD_Pollution_DamageExpansion".Translate(),
                        ref s.pollutionDamageExpansion,
                        "TSA_WD_Pollution_DamageExpansionTip".Translate(),
                        defaultValue: WorldDominationSettings.DefPollutionDamageExpansion);
                    SettingsUI.DrawCheckbox(l, "TSA_WD_Pollution_DamageConstruction".Translate(),
                        ref s.pollutionDamageConstruction,
                        "TSA_WD_Pollution_DamageConstructionTip".Translate(),
                        defaultValue: WorldDominationSettings.DefPollutionDamageConstruction);
                    SettingsUI.DrawCheckbox(l, "TSA_WD_Pollution_DamageTraders".Translate(),
                        ref s.pollutionDamageTraders,
                        "TSA_WD_Pollution_DamageTradersTip".Translate(),
                        defaultValue: WorldDominationSettings.DefPollutionDamageTraders);
                    SettingsUI.DrawCheckbox(l, "TSA_WD_Pollution_DamagePlayer".Translate(),
                        ref s.pollutionDamagePlayerTravelers,
                        "TSA_WD_Pollution_DamagePlayerTip".Translate(),
                        defaultValue: WorldDominationSettings.DefPollutionDamagePlayerTravelers);

                    s.pollutionDamageIgnoreBelow = SettingsUI.LabeledSlider(l,
                        "TSA_WD_Pollution_IgnoreBelow".Translate(),
                        s.pollutionDamageIgnoreBelow, 0f, 0.5f,
                        "TSA_WD_Pollution_IgnoreBelowTip".Translate(),
                        0.01f, SliderFormat.Percent, WorldDominationSettings.DefPollutionDamageIgnoreBelow);
                    s.pollutionDamageAtThreshold = SettingsUI.LabeledSlider(l,
                        "TSA_WD_Pollution_DamageAtThreshold".Translate(),
                        s.pollutionDamageAtThreshold, 0f, 100f,
                        "TSA_WD_Pollution_DamageAtThresholdTip".Translate(),
                        1f, SliderFormat.Fixed0, WorldDominationSettings.DefPollutionDamageAtThreshold);
                    s.pollutionDamageAtFull = SettingsUI.LabeledSlider(l,
                        "TSA_WD_Pollution_DamageAtFull".Translate(),
                        s.pollutionDamageAtFull, 0f, 1000f,
                        "TSA_WD_Pollution_DamageAtFullTip".Translate(),
                        5f, SliderFormat.Fixed0, WorldDominationSettings.DefPollutionDamageAtFull);
                    s.pollutionDamageRadius = Mathf.RoundToInt(SettingsUI.LabeledSlider(l,
                        "TSA_WD_Pollution_DamageRadius".Translate(),
                        s.pollutionDamageRadius, 0f, 10f,
                        "TSA_WD_Pollution_DamageRadiusTip".Translate(),
                        1f, SliderFormat.Fixed0, WorldDominationSettings.DefPollutionDamageRadius));
                    s.npcSettlementDecontaminationStrengthCost = SettingsUI.LabeledSlider(l,
                        "TSA_WD_Pollution_NpcDecontamCost".Translate(),
                        s.npcSettlementDecontaminationStrengthCost, 0f, 100f,
                        "TSA_WD_Pollution_NpcDecontamCostTip".Translate(),
                        1f, SliderFormat.Fixed0, WorldDominationSettings.DefNpcSettlementDecontaminationStrengthCost);
                }

                SettingsUI.DrawCheckbox(l, "TSA_WD_Pollution_PathCost".Translate(),
                    ref s.pollutionPathCostEnabled,
                    "TSA_WD_Pollution_PathCostTip".Translate(),
                    defaultValue: WorldDominationSettings.DefPollutionPathCostEnabled);
                SettingsUI.DrawCheckbox(l, "TSA_WD_Pollution_PathRepath".Translate(),
                    ref s.pollutionPathRepathEnabled,
                    "TSA_WD_Pollution_PathRepathTip".Translate(),
                    defaultValue: WorldDominationSettings.DefPollutionPathRepathEnabled);
                SettingsUI.DrawCheckbox(l, "TSA_WD_Pollution_PathPreCommitCancel".Translate(),
                    ref s.pollutionPathPreCommitCancelEnabled,
                    "TSA_WD_Pollution_PathPreCommitCancelTip".Translate(),
                    defaultValue: WorldDominationSettings.DefPollutionPathPreCommitCancelEnabled);
            }

            l.End();
            Widgets.EndScrollView();
        }

        private static void DrawWorldMapOverlayHoldKeyRow(Listing_Standard l, WorldDominationSettings s)
        {
            l.Gap(2f);
            Rect row = l.GetRect(24f);
            string tip = SettingsUI.TooltipWithDefault(
                "TSA_WD_WorldMapOverlayHoldKey_Tooltip".Translate(),
                FormatOverlayHoldKey(WorldDominationSettings.DefWorldMapOverlayHoldKey));
            TooltipHandler.TipRegion(row, tip);

            Rect labelRect = new Rect(row.x, row.y, row.width - 110f, row.height);
            Rect btnRect = new Rect(row.xMax - 106f, row.y, 106f, row.height);
            Widgets.Label(labelRect, "TSA_WD_WorldMapOverlayHoldKey".Translate());
            if (Widgets.ButtonText(btnRect, FormatOverlayHoldKey(s.worldMapOverlayHoldKey)))
            {
                var options = new List<FloatMenuOption>();
                foreach (KeyCode key in OverlayHoldKeyChoices)
                {
                    KeyCode captured = key;
                    options.Add(new FloatMenuOption(FormatOverlayHoldKey(captured), () => s.worldMapOverlayHoldKey = captured));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private static readonly KeyCode[] OverlayHoldKeyChoices =
        {
            KeyCode.LeftAlt, KeyCode.RightAlt,
            KeyCode.LeftControl, KeyCode.RightControl,
            KeyCode.LeftShift, KeyCode.RightShift,
            KeyCode.A, KeyCode.B, KeyCode.C, KeyCode.D, KeyCode.E, KeyCode.F, KeyCode.G, KeyCode.H,
            KeyCode.I, KeyCode.J, KeyCode.K, KeyCode.L, KeyCode.M, KeyCode.N, KeyCode.O, KeyCode.P,
            KeyCode.Q, KeyCode.R, KeyCode.S, KeyCode.T, KeyCode.U, KeyCode.V, KeyCode.W, KeyCode.X,
            KeyCode.Y, KeyCode.Z
        };

        private static SettlementTier DrawAmbushMinTierSlider(Listing_Standard l, SettlementTier current)
        {
            l.Gap(2f);
            Rect r = l.GetRect(24f);
            TooltipHandler.TipRegion(r, SettingsUI.TooltipWithDefault(
                "TSA_WD_Experimental_AmbushMinTierTip".Translate(),
                AmbushMinTierLabel(WorldDominationSettings.DefSettlementAmbushMinTier)));
            string suffix = AmbushMinTierLabel(current);
            Widgets.Label(r.LeftPart(0.5f), $"{"TSA_WD_Experimental_AmbushMinTier".Translate()}: {suffix.Colorize(Color.cyan)}");
            float next = Widgets.HorizontalSlider(r.RightPart(0.5f), (int)current, (int)SettlementTier.T1, (int)SettlementTier.T4, false, null, null, null, 1f);
            return (SettlementTier)Mathf.RoundToInt(next);
        }

        private static int DrawAmbushMaxConcurrentSlider(Listing_Standard l, int current)
        {
            l.Gap(2f);
            Rect r = l.GetRect(24f);
            TooltipHandler.TipRegion(r, SettingsUI.TooltipWithDefault(
                "TSA_WD_Experimental_AmbushMaxConcurrentTip".Translate(),
                (float)WorldDominationSettings.DefSettlementAmbushMaxConcurrent,
                SliderFormat.Fixed0));
            current = Mathf.Clamp(current, 0, 32);
            string suffix = current <= 0
                ? "TSA_WD_Experimental_AmbushMaxConcurrentUnlimited".Translate().ToString()
                : current.ToString();
            Widgets.Label(r.LeftPart(0.5f), $"{"TSA_WD_Experimental_AmbushMaxConcurrent".Translate()}: {suffix.Colorize(Color.cyan)}");
            return Mathf.RoundToInt(Widgets.HorizontalSlider(r.RightPart(0.5f), current, 0f, 32f, false, null, null, null, 1f));
        }

        private static string AmbushMinTierLabel(SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.T4: return "TSA_WD_Tier4".Translate();
                case SettlementTier.T3: return "TSA_WD_Tier3".Translate();
                case SettlementTier.T2: return "TSA_WD_Tier2".Translate();
                default: return "TSA_WD_Tier1".Translate();
            }
        }

        private static string FormatOverlayHoldKey(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.LeftAlt: return "Left Alt";
                case KeyCode.RightAlt: return "Right Alt";
                case KeyCode.LeftControl: return "Left Ctrl";
                case KeyCode.RightControl: return "Right Ctrl";
                case KeyCode.LeftShift: return "Left Shift";
                case KeyCode.RightShift: return "Right Shift";
                default: return key.ToString();
            }
        }
    }
}
