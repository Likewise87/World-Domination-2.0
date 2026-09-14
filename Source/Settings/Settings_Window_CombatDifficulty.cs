using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_CombatDifficultySettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;

        private bool escalationExpanded = true;
        private bool tooExpanded = true;
        private bool maraudExpanded = true;
        private bool ambushExpanded = true;
        private bool vanguardInvasionExpanded = true;
        private bool desperationExpanded = true;
        private bool turtleExpanded = true;
        private bool isolationPressureExpanded = true;
        private bool strongWarExpanded = true;
        private bool raidPressureExpanded = true;
        private bool clampExpanded;
        private bool stageCombatExpanded = true;
        private bool arrivalExpanded;
        private bool advancedWorldExpanded;
        private bool garrisonExpanded = true;
        private bool t4MortarFeatureExpanded = true;
        private bool t4AaFeatureExpanded = true;

        public override Vector2 InitialSize => new Vector2(850f, 750f);

        public Dialog_CombatDifficultySettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnCombatDifficulty".Translate();
            optionalTitle = null;
        }

        public override void PreClose()
        {
            base.PreClose();
            WorldDominationMod.settings?.NormalizeEscalationConstraints();
            WorldDominationMod.settings?.NormalizeRaidClampFractions();
            if (Current.ProgramState != ProgramState.Playing) return;
            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.Notify_WeightsChanged();
        }

        private void SetAllExpanded(bool expanded)
        {
            escalationExpanded = tooExpanded = maraudExpanded = ambushExpanded =
                vanguardInvasionExpanded = desperationExpanded = turtleExpanded =
                isolationPressureExpanded = strongWarExpanded =
                raidPressureExpanded = clampExpanded = stageCombatExpanded =
                arrivalExpanded = advancedWorldExpanded = garrisonExpanded =
                t4MortarFeatureExpanded = t4AaFeatureExpanded = expanded;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            float contentWidth = contentRect.width - 24f;
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 6200f);

            Widgets.BeginScrollView(contentRect, ref scrollPosition, scrollViewRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(scrollViewRect);
            var s = WorldDominationMod.settings;

            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetCombatDifficulty(),
                () => SetAllExpanded(true),
                () => SetAllExpanded(false));
            SettingsUI.DrawSettingsSearchBar(l);

            DrawRaidPressure(l, s);
            DrawEscalationStages(l, s);
            DrawTargetOfOpportunity(l, s);
            DrawMarauding(l, s);
            DrawAmbush(l, s);
            DrawVanguardAndInvasion(l, s);
            DrawDesperation(l, s);
            DrawTurtle(l, s);
            DrawIsolationPressure(l, s);
            DrawStrongWar(l, s);
            DrawStageCombatBonuses(l, s);
            DrawArrivalStyles(l, s);
            DrawAdvancedWorldCombat(l, s);
            DrawGarrison(l, s);
            DrawT4MortarFeature(l, s);
            DrawT4AntiAirFeature(l, s);

            l.End();
            Widgets.EndScrollView();
        }

        private static void ApplyGate(WorldDominationSettings s, System.Action apply)
        {
            apply();
            s.SyncLegacyThreatFlagsFromGates();
        }

        private void DrawEscalationStages(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Combat_HeaderEscalation".Translate(), ref escalationExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Combat_HeaderEscalationTip".Translate()))
                return;

            SettingsUI.DrawCheckbox(l, "TSA_WD_Difficulty_EnableLateGame".Translate(), ref s.enableLateGameScaling,
                "TSA_WD_Difficulty_EnableLateGameTooltip".Translate(),
                defaultValue: WorldDominationSettings.DefEnableLateGameScaling);

            if (!s.enableLateGameScaling)
                return;

            s.midGameShareThreshold = SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_ShareThreshold".Translate(), s.midGameShareThreshold, 0f, 1f,
                "TSA_WD_MidGame_ShareThresholdTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefMidGameShareThreshold);
            s.midGameOutpostStrengthThreshold = SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_OutpostStrengthThreshold".Translate(), s.midGameOutpostStrengthThreshold, 100f, 25000f,
                "TSA_WD_MidGame_OutpostStrengthThresholdTooltip".Translate(), 100f, SliderFormat.Fixed0, WorldDominationSettings.DefMidGameOutpostStrengthThreshold);
            s.midGameDaysThreshold = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_DaysThreshold".Translate(), s.midGameDaysThreshold, 10f, 300f,
                "TSA_WD_MidGame_DaysThresholdTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefMidGameDaysThreshold));
            s.lateGameShareThreshold = SettingsUI.LabeledSlider(l, "TSA_WD_Difficulty_ShareThreshold".Translate(), s.lateGameShareThreshold, 0f, 1f,
                "TSA_WD_Difficulty_ShareThresholdTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefLateGameShareThreshold);
            s.lateGameOutpostStrengthThreshold = SettingsUI.LabeledSlider(l, "TSA_WD_Difficulty_OutpostStrengthThreshold".Translate(), s.lateGameOutpostStrengthThreshold, 100f, 25000f,
                "TSA_WD_Difficulty_OutpostStrengthThresholdTooltip".Translate(), 100f, SliderFormat.Fixed0, WorldDominationSettings.DefLateGameOutpostStrengthThreshold);
            s.lateGameDaysThreshold = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_LateGame_DaysThreshold".Translate(), s.lateGameDaysThreshold, 10f, 300f,
                "TSA_WD_LateGame_DaysThresholdTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefLateGameDaysThreshold));
            s.NormalizeEscalationThresholds();

            l.Gap(4f);
            GUI.color = Color.gray;
            l.Label("TSA_WD_Difficulty_ActivationHint".Translate());
            GUI.color = Color.white;
        }

        private void DrawTargetOfOpportunity(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Experimental_HeaderTargetOfOpportunity".Translate(), ref tooExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Combat_HeaderTargetOfOpportunityTip".Translate()))
                return;

            SettingsUI.EnumDropdownApply(l, "TSA_WD_Combat_GateWhen".Translate(), s.gateThreatToO,
                v => ApplyGate(s, () => s.gateThreatToO = v),
                WdEscalation.ThreatGateLabel,
                "TSA_WD_Combat_GateToOTip".Translate());
            if (s.gateThreatToO == WdThreatStageGate.Never)
                return;

            s.targetOfOpportunityEligibilityRollPct = SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_ToORollPct".Translate(), s.targetOfOpportunityEligibilityRollPct, 0f, 1f,
                "TSA_WD_Experimental_ToORollPctTip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefTargetOfOpportunityEligibilityRollPct);
            s.targetOfOpportunityMinRatioAdvantage = SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_ToOMinRatioAdvantage".Translate(), s.targetOfOpportunityMinRatioAdvantage, 0f, 2f,
                "TSA_WD_Experimental_ToOMinRatioAdvantageTip".Translate(), 0.05f, SliderFormat.Fixed2, WorldDominationSettings.DefTargetOfOpportunityMinRatioAdvantage);
            s.targetOfOpportunityMaxRetargets = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_ToOMaxRetargets".Translate(), s.targetOfOpportunityMaxRetargets, 0f, 10f,
                "TSA_WD_Experimental_ToOMaxRetargetsTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefTargetOfOpportunityMaxRetargets));
            s.targetChangesMaxLifetime = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_TargetChangesMaxLifetime".Translate(), s.targetChangesMaxLifetime, 0f, 15f,
                "TSA_WD_Experimental_TargetChangesMaxLifetimeTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefTargetChangesMaxLifetime));
        }

        private void DrawMarauding(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Experimental_HeaderMarauding".Translate(), ref maraudExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Combat_HeaderMaraudingTip".Translate()))
                return;

            SettingsUI.EnumDropdownApply(l, "TSA_WD_Combat_GateWhen".Translate(), s.gateThreatMaraud,
                v => ApplyGate(s, () => s.gateThreatMaraud = v),
                WdEscalation.ThreatGateLabel,
                "TSA_WD_Combat_GateMaraudTip".Translate());
            if (s.gateThreatMaraud == WdThreatStageGate.Never)
                return;

            s.maraudingChanceToOccurPct = SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_MaraudChancePct".Translate(), s.maraudingChanceToOccurPct, 0f, 1f,
                "TSA_WD_Experimental_MaraudChancePctTip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefMaraudingChanceToOccurPct);
            s.maraudingMinSurvivingStrengthAbsolute = SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_MaraudMinStrength".Translate(), s.maraudingMinSurvivingStrengthAbsolute, 200f, 2000f,
                "TSA_WD_Experimental_MaraudMinStrengthTip".Translate(), 5f, SliderFormat.Fixed0, WorldDominationSettings.DefMaraudingMinSurvivingStrengthAbsolute);
            s.maraudingMaxChainedTargets = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Experimental_MaraudMaxChain".Translate(), s.maraudingMaxChainedTargets, 0f, 10f,
                "TSA_WD_Experimental_MaraudMaxChainTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefMaraudingMaxChainedTargets));
        }

        private void DrawAmbush(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Experimental_HeaderSettlementAmbush".Translate(), ref ambushExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Combat_HeaderSettlementAmbushTip".Translate()))
                return;

            SettingsUI.EnumDropdownApply(l, "TSA_WD_Combat_GateWhen".Translate(), s.gateThreatAmbush,
                v => ApplyGate(s, () => s.gateThreatAmbush = v),
                WdEscalation.ThreatGateLabel,
                "TSA_WD_Combat_GateAmbushTip".Translate());
            if (s.gateThreatAmbush == WdThreatStageGate.Never)
                return;

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

        private void DrawVanguardAndInvasion(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Combat_HeaderVanguardAndInvasion".Translate(), ref vanguardInvasionExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Combat_HeaderVanguardAndInvasionTip".Translate()))
                return;

            SettingsUI.EnumDropdownApply(l, "TSA_WD_Combat_GateVanguard".Translate(), s.gateThreatVanguard,
                v => ApplyGate(s, () => s.gateThreatVanguard = v),
                WdEscalation.ThreatGateLabel,
                "TSA_WD_Combat_GateVanguardTip".Translate());
            SettingsUI.EnumDropdownApply(l, "TSA_WD_Combat_GateInvasion".Translate(), s.gateThreatInvasion,
                v => ApplyGate(s, () => s.gateThreatInvasion = v),
                WdEscalation.ThreatGateLabel,
                "TSA_WD_Combat_GateInvasionTip".Translate());

            if (s.gateThreatVanguard == WdThreatStageGate.Never && s.gateThreatInvasion == WdThreatStageGate.Never)
                return;

            s.forwardAssaultChance = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_ForwardAssaultChance".Translate(), s.forwardAssaultChance, 0f, 1f,
                "TSA_WD_Diplo_ForwardAssaultChanceTooltip".Translate(), 0.002f, SliderFormat.PercentDecimal, WorldDominationSettings.DefForwardAssaultChance);
            s.vanguardVsInvasionPickChance = SettingsUI.LabeledSlider(l, "TSA_WD_Combat_VanguardVsInvasionPickChance".Translate(), s.vanguardVsInvasionPickChance, 0f, 1f,
                "TSA_WD_Combat_VanguardVsInvasionPickChanceTip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefVanguardVsInvasionPickChance);
            SettingsUI.DrawCheckbox(l,
                "TSA_WD_Combat_FallBackToInvasion".Translate(),
                ref s.fallBackToInvasionIfVanguardClusterFails,
                "TSA_WD_Combat_FallBackToInvasionTip".Translate(),
                defaultValue: WorldDominationSettings.DefFallBackToInvasionIfVanguardClusterFails);
            s.forwardAssaultTopPct = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_ForwardAssaultTopPct".Translate(), s.forwardAssaultTopPct, 0.05f, 1f,
                "TSA_WD_Diplo_ForwardAssaultTopPctTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefForwardAssaultTopPct);
            s.forwardAssaultMinDistanceFromPlayer = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_ForwardAssaultMinDist".Translate(), s.forwardAssaultMinDistanceFromPlayer, 5f, 80f,
                "TSA_WD_Diplo_ForwardAssaultMinDistTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefForwardAssaultMinDistanceFromPlayer);
            if (s.gateThreatVanguard != WdThreatStageGate.Never)
            {
                s.vanguardClusterMinDistFromColony = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Combat_VanguardClusterMinDist".Translate(), s.vanguardClusterMinDistFromColony, 8f, 40f,
                    "TSA_WD_Combat_VanguardClusterMinDistTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefVanguardClusterMinDistFromColony));
                s.vanguardClusterMaxDistFromColony = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Combat_VanguardClusterMaxDist".Translate(), s.vanguardClusterMaxDistFromColony, 8f, 48f,
                    "TSA_WD_Combat_VanguardClusterMaxDistTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefVanguardClusterMaxDistFromColony));
                if (s.vanguardClusterMaxDistFromColony < s.vanguardClusterMinDistFromColony)
                    s.vanguardClusterMaxDistFromColony = s.vanguardClusterMinDistFromColony;
                s.vanguardFoundSitesMin = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Combat_VanguardFoundSitesMin".Translate(), s.vanguardFoundSitesMin, 1f, 4f,
                    "TSA_WD_Combat_VanguardFoundSitesMinTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefVanguardFoundSitesMin));
                s.vanguardFoundSitesMax = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Combat_VanguardFoundSitesMax".Translate(), s.vanguardFoundSitesMax, 1f, 4f,
                    "TSA_WD_Combat_VanguardFoundSitesMaxTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefVanguardFoundSitesMax));
                if (s.vanguardFoundSitesMax < s.vanguardFoundSitesMin)
                    s.vanguardFoundSitesMax = s.vanguardFoundSitesMin;
                s.vanguardMergeRadiusTiles = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Combat_VanguardMergeRadius".Translate(), s.vanguardMergeRadiusTiles, 1f, 8f,
                    "TSA_WD_Combat_VanguardMergeRadiusTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefVanguardMergeRadiusTiles));
            }
            if (!s.useSharedSpecialEventCooldown)
            {
                s.forwardAssaultCooldownDays = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_ForwardAssaultCooldown".Translate(), s.forwardAssaultCooldownDays, 0.5f, 60f,
                    "TSA_WD_Diplo_ForwardAssaultCooldownTooltip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefForwardAssaultCooldownDays);
            }
            if (SettingsUI.SearchMatches("TSA_WD_Combat_InvasionIgnoresCooldownHint".Translate()))
            {
                GUI.color = Color.gray;
                l.Label("TSA_WD_Combat_InvasionIgnoresCooldownHint".Translate());
                GUI.color = Color.white;
            }
        }

        private void DrawDesperation(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Diplo_DesperationRaidHeader".Translate(), ref desperationExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Combat_HeaderDesperationTip".Translate()))
                return;

            SettingsUI.EnumDropdownApply(l, "TSA_WD_Combat_GateWhen".Translate(), s.gateThreatDesperation,
                v => ApplyGate(s, () => s.gateThreatDesperation = v),
                WdEscalation.ThreatGateLabel,
                "TSA_WD_Combat_GateDesperationTip".Translate());
            if (s.gateThreatDesperation == WdThreatStageGate.Never)
                return;

            s.settlementLossStrategyFireChance = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_StrategyFireChance".Translate(), s.settlementLossStrategyFireChance, 0f, 1f,
                "TSA_WD_Diplo_StrategyFireChanceTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefSettlementLossStrategyFireChance);

            // UI is on/off; runtime still uses desperationChanceOnLoss (1 = designed bands, 0 = Turtle only).
            bool desperationPossible = s.desperationChanceOnLoss > 0.001f;
            SettingsUI.DrawCheckbox(l,
                "TSA_WD_Diplo_DesperationChance".Translate(),
                ref desperationPossible,
                DesperationRaidPossibleTooltip(s),
                defaultValue: WorldDominationSettings.DefDesperationChanceOnLoss > 0.001f);
            s.desperationChanceOnLoss = desperationPossible ? 1f : 0f;

            s.desperationCooldownDays = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_DesperationCooldown".Translate(), s.desperationCooldownDays, 0.1f, 15f,
                "TSA_WD_Diplo_DesperationCooldownTooltip".Translate(), 0.1f, SliderFormat.Fixed1, WorldDominationSettings.DefDesperationCooldownDays);
        }

        /// <summary>
        /// Injects live cluster / pressure / band numbers into the Desperation Raid possible? tip.
        /// </summary>
        private static string DesperationRaidPossibleTooltip(WorldDominationSettings s)
        {
            int clusterTiles = WorldActions_DesperationRaid.ClusterEdgeTiles;
            int pressureTiles = Mathf.RoundToInt(WorldActions_Turtle.ClusterThreatBandTiles);
            int pressurePct = Mathf.RoundToInt(Mathf.Max(0f, s.turtlePressureRatio) * 100f);
            int strongSharePct = Mathf.RoundToInt(WorldDominationSettings.DefSettlementLossStrategyMidRelative * 100f);
            int weakSharePct = Mathf.RoundToInt(WorldDominationSettings.DefSettlementLossStrategyDyingRelative * 100f);
            int midDespPct = Mathf.RoundToInt(WorldDominationSettings.DefSettlementLossStrategyMidBaseChance * 100f);
            int weakDespPct = Mathf.RoundToInt(WorldDominationSettings.DefSettlementLossStrategyDyingBaseChance * 100f);
            return "TSA_WD_Diplo_DesperationChanceTooltip".Translate(
                clusterTiles,
                pressurePct,
                pressureTiles,
                midDespPct,
                100 - midDespPct,
                weakDespPct,
                100 - weakDespPct,
                strongSharePct,
                weakSharePct);
        }

        private void DrawTurtle(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Combat_TurtleHeader".Translate(), ref turtleExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Combat_HeaderTurtleTip".Translate()))
                return;

            SettingsUI.EnumDropdownApply(l, "TSA_WD_Combat_GateWhen".Translate(), s.gateThreatTurtle,
                v => ApplyGate(s, () => s.gateThreatTurtle = v),
                WdEscalation.ThreatGateLabel,
                "TSA_WD_Combat_GateTurtleTip".Translate());
            if (s.gateThreatTurtle == WdThreatStageGate.Never)
                return;

            s.turtleChance = SettingsUI.LabeledSlider(l, "TSA_WD_Combat_TurtleChance".Translate(), s.turtleChance, 0f, 1f,
                "TSA_WD_Combat_TurtleChanceTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefTurtleChance);
            s.turtleCooldownDays = SettingsUI.LabeledSlider(l, "TSA_WD_Combat_TurtleCooldown".Translate(), s.turtleCooldownDays, 0.1f, 30f,
                "TSA_WD_Combat_TurtleCooldownTooltip".Translate(), 0.1f, SliderFormat.Fixed1, WorldDominationSettings.DefTurtleCooldownDays);
            s.turtleMinClusterSize = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Combat_TurtleMinCluster".Translate(), s.turtleMinClusterSize, 2f, 10f,
                "TSA_WD_Combat_TurtleMinClusterTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefTurtleMinClusterSize));
            s.turtleMaxPackSettlements = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Combat_TurtleMaxPack".Translate(), s.turtleMaxPackSettlements, 1f, 10f,
                "TSA_WD_Combat_TurtleMaxPackTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefTurtleMaxPackSettlements));
            s.turtlePressureRatio = SettingsUI.LabeledSlider(l, "TSA_WD_Combat_TurtlePressureRatio".Translate(), s.turtlePressureRatio, 0f, 10f,
                "TSA_WD_Combat_TurtlePressureRatioTooltip".Translate(), 0.1f, SliderFormat.Percent, WorldDominationSettings.DefTurtlePressureRatio);
        }

        private void DrawIsolationPressure(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Combat_IsolationPressureHeader".Translate(), ref isolationPressureExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Combat_IsolationPressureHeaderTip".Translate()))
                return;

            SettingsUI.EnumDropdownApply(l, "TSA_WD_Combat_GateWhen".Translate(), s.gateThreatIsolationPressure,
                v => ApplyGate(s, () => s.gateThreatIsolationPressure = v),
                WdEscalation.ThreatGateLabel,
                "TSA_WD_Combat_GateIsolationPressureTip".Translate());
            if (s.gateThreatIsolationPressure == WdThreatStageGate.Never)
                return;

            s.isolationPressureCooldownDays = SettingsUI.LabeledSlider(l, "TSA_WD_Combat_IsolationPressureCooldown".Translate(), s.isolationPressureCooldownDays, 0.5f, 60f,
                "TSA_WD_Combat_IsolationPressureCooldownTip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefIsolationPressureCooldownDays);
        }

        private void DrawStrongWar(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Diplo_StrongFactionWarHeader".Translate(), ref strongWarExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Combat_HeaderStrongFactionWarTip".Translate()))
                return;

            SettingsUI.EnumDropdownApply(l, "TSA_WD_Combat_GateWhen".Translate(), s.gateThreatStrongFactionWar,
                v => ApplyGate(s, () => s.gateThreatStrongFactionWar = v),
                WdEscalation.ThreatGateLabel,
                "TSA_WD_Combat_GateStrongWarTip".Translate());
            SettingsUI.DrawCheckbox(l,
                "TSA_WD_Diplo_StrongFactionWarEnable".Translate(),
                ref s.enableStrongFactionWar,
                "TSA_WD_Diplo_StrongFactionWarEnableTooltip".Translate(),
                defaultValue: WorldDominationSettings.DefEnableStrongFactionWar);
            if (!s.enableStrongFactionWar || s.gateThreatStrongFactionWar == WdThreatStageGate.Never)
                return;

            s.strongFactionWarChance = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_StrongFactionWarChance".Translate(), s.strongFactionWarChance, 0f, 1f,
                "TSA_WD_Diplo_StrongFactionWarChanceTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefStrongFactionWarChance);
            s.strongFactionWarTopPct = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_StrongFactionWarTopPct".Translate(), s.strongFactionWarTopPct, 0.05f, 1f,
                "TSA_WD_Diplo_StrongFactionWarTopPctTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefStrongFactionWarTopPct);
            if (!s.useSharedSpecialEventCooldown)
            {
                s.strongFactionWarCooldownDays = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_StrongFactionWarCooldown".Translate(), s.strongFactionWarCooldownDays, 0.5f, 60f,
                    "TSA_WD_Diplo_StrongFactionWarCooldownTooltip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefStrongFactionWarCooldownDays);
            }
        }

        private void DrawRaidPressure(Listing_Standard l, WorldDominationSettings s)
        {
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Raid_HeaderPlayer".Translate(), ref raidPressureExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Combat_HeaderPlayerRaidsTip".Translate()))
            {
                SettingsUI.DrawCheckbox(l, "TSA_WD_Raid_AllowPlayer".Translate(), ref s.allowPlayerRaid,
                    "TSA_WD_Raid_AllowPlayerTooltip".Translate(),
                    defaultValue: WorldDominationSettings.DefAllowPlayerRaid);

                if (s.allowPlayerRaid)
                {
                    s.cooldownPlayerRaidDays = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_CdPlayer".Translate(), s.cooldownPlayerRaidDays, 0f, 15f,
                        "TSA_WD_Raid_CdPlayerTooltip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefCdPlayerRaidDays);
                }
                else
                {
                    l.Gap(6f);
                    GUI.color = Color.gray;
                    l.Label("    <i>" + "TSA_WD_Raid_DisabledPlayer".Translate() + "</i>");
                    GUI.color = Color.white;
                    l.Gap(6f);
                }

                SettingsUI.DrawCheckbox(l, "TSA_WD_Raid_AllowOutpost".Translate(), ref s.allowPlayerOutpostRaid,
                    "TSA_WD_Raid_AllowOutpostTooltip".Translate(),
                    defaultValue: WorldDominationSettings.DefAllowPlayerOutpostRaid);

                s.maxPlayerWdRaidsPerDay = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Raid_MaxPerDay".Translate(), s.maxPlayerWdRaidsPerDay, 1f, 10f,
                    "TSA_WD_Raid_MaxPerDayTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefMaxPlayerWdRaidsPerDay));
                s.maxPlayerWdRaidsPer4Days = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Raid_MaxPer4Days".Translate(), s.maxPlayerWdRaidsPer4Days, 1f, 20f,
                    "TSA_WD_Raid_MaxPer4DaysTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefMaxPlayerWdRaidsPer4Days));
                s.maxPlayerWdRaidsPer7Days = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Raid_MaxPer7Days".Translate(), s.maxPlayerWdRaidsPer7Days, 1f, 30f,
                    "TSA_WD_Raid_MaxPer7DaysTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefMaxPlayerWdRaidsPer7Days));
                s.ClampPlayerWdRaidRateCaps();

                l.Gap(4f);
                GUI.color = Color.gray;
                l.Label("TSA_WD_Threat_StorytellerMovedHint".Translate());
                GUI.color = Color.white;
            }

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Threat_HeaderWDClamp".Translate(), ref clampExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Combat_HeaderRaidClampTip".Translate()))
            {
                SettingsUI.DrawCheckbox(l,
                    "TS_WD_CaravanRaidAlwaysUseStrength".Translate(),
                    ref s.alwaysUseStrengthAsRaidPoints,
                    "TS_WD_CaravanRaidAlwaysUseStrengthTooltip".Translate(),
                    defaultValue: WorldDominationSettings.DefAlwaysUseStrengthAsRaidPoints);

                SettingsUI.DrawCheckbox(l,
                    "TS_WD_OutpostDefenseAlwaysUseStrength".Translate(),
                    ref s.alwaysUseStrengthAsOutpostDefenseRaidPoints,
                    "TS_WD_OutpostDefenseAlwaysUseStrengthTooltip".Translate(),
                    defaultValue: WorldDominationSettings.DefAlwaysUseStrengthAsOutpostDefenseRaidPoints);

                if (!s.alwaysUseStrengthAsRaidPoints)
                {
                    SettingsUI.DrawCheckbox(l,
                        "TS_WD_RaidClamp_ScaleWithEscalation".Translate(),
                        ref s.scaleRaidClampWithEscalation,
                        "TS_WD_RaidClamp_ScaleWithEscalationTooltip".Translate(),
                        defaultValue: WorldDominationSettings.DefScaleRaidClampWithEscalation);

                    bool showStageBands = s.scaleRaidClampWithEscalation && s.enableLateGameScaling;
                    if (s.scaleRaidClampWithEscalation && !s.enableLateGameScaling)
                    {
                        l.Gap(4f);
                        GUI.color = Color.gray;
                        l.Label("TS_WD_RaidClamp_EscalationOffHint".Translate());
                        GUI.color = Color.white;
                        l.Gap(4f);
                    }

                    if (showStageBands)
                    {
                        s.earlyRaidClampMinStorytellerFraction = SettingsUI.LabeledSlider(l, "TS_WD_RaidClamp_EarlyMin".Translate(), s.earlyRaidClampMinStorytellerFraction, 0.05f, 2f,
                            "TS_WD_RaidClamp_EarlyMinTooltip".Translate(), 0.05f, SliderFormat.Multiplier, WorldDominationSettings.DefEarlyRaidClampMinStorytellerFrac);
                        s.earlyRaidClampMaxStorytellerFraction = SettingsUI.LabeledSlider(l, "TS_WD_RaidClamp_EarlyMax".Translate(), s.earlyRaidClampMaxStorytellerFraction, 0.5f, 50f,
                            "TS_WD_RaidClamp_EarlyMaxTooltip".Translate(), 0.05f, SliderFormat.Multiplier, WorldDominationSettings.DefEarlyRaidClampMaxStorytellerFrac);
                        s.midRaidClampMinStorytellerFraction = SettingsUI.LabeledSlider(l, "TS_WD_RaidClamp_MidMin".Translate(), s.midRaidClampMinStorytellerFraction, 0.05f, 2f,
                            "TS_WD_RaidClamp_MidMinTooltip".Translate(), 0.05f, SliderFormat.Multiplier, WorldDominationSettings.DefMidRaidClampMinStorytellerFrac);
                        s.midRaidClampMaxStorytellerFraction = SettingsUI.LabeledSlider(l, "TS_WD_RaidClamp_MidMax".Translate(), s.midRaidClampMaxStorytellerFraction, 0.5f, 50f,
                            "TS_WD_RaidClamp_MidMaxTooltip".Translate(), 0.05f, SliderFormat.Multiplier, WorldDominationSettings.DefMidRaidClampMaxStorytellerFrac);
                        s.lateRaidClampMinStorytellerFraction = SettingsUI.LabeledSlider(l, "TS_WD_RaidClamp_LateMin".Translate(), s.lateRaidClampMinStorytellerFraction, 0.05f, 2f,
                            "TS_WD_RaidClamp_LateMinTooltip".Translate(), 0.05f, SliderFormat.Multiplier, WorldDominationSettings.DefLateRaidClampMinStorytellerFrac);
                        s.lateRaidClampMaxStorytellerFraction = SettingsUI.LabeledSlider(l, "TS_WD_RaidClamp_LateMax".Translate(), s.lateRaidClampMaxStorytellerFraction, 0.5f, 50f,
                            "TS_WD_RaidClamp_LateMaxTooltip".Translate(), 0.05f, SliderFormat.Multiplier, WorldDominationSettings.DefLateRaidClampMaxStorytellerFrac);
                    }
                    else
                    {
                        s.caravanRaidPointsMinStorytellerFraction = SettingsUI.LabeledSlider(l, "TS_WD_CaravanRaidMinFrac".Translate(), s.caravanRaidPointsMinStorytellerFraction, 0.05f, 2f,
                            "TS_WD_CaravanRaidMinFracTooltip".Translate(), 0.05f, SliderFormat.Multiplier, WorldDominationSettings.DefCaravanRaidMinStorytellerFrac);
                        s.caravanRaidPointsMaxStorytellerFraction = SettingsUI.LabeledSlider(l, "TS_WD_CaravanRaidMaxFrac".Translate(), s.caravanRaidPointsMaxStorytellerFraction, 0.5f, 50f,
                            "TS_WD_CaravanRaidMaxFracTooltip".Translate(), 0.1f, SliderFormat.Multiplier, WorldDominationSettings.DefCaravanRaidMaxStorytellerFrac);
                    }

                    s.NormalizeRaidClampFractions();
                }

                s.minRaidPoints = SettingsUI.LabeledSlider(l, "TS_WD_Threat_MinPoints".Translate(), s.minRaidPoints, 50f, 500f,
                    "TS_WD_Threat_MinPointsTooltip".Translate(), 1.0f, SliderFormat.Fixed0, WorldDominationSettings.DefMinRaidPoints);
                s.maxRaidPoints = SettingsUI.LabeledSlider(l, "TS_WD_Threat_MaxPoints".Translate(), s.maxRaidPoints, 1000f, 20000f,
                    "TS_WD_Threat_MaxPointsTooltip".Translate(), 100f, SliderFormat.Fixed0, WorldDominationSettings.DefMaxRaidPoints);
            }
        }

        private void DrawStageCombatBonuses(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Combat_HeaderStageBonuses".Translate(), ref stageCombatExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Combat_HeaderStageBonusesTip".Translate()))
                return;

            if (!s.enableLateGameScaling)
            {
                GUI.color = Color.gray;
                l.Label("TSA_WD_Combat_StageBonusesNeedEscalation".Translate());
                GUI.color = Color.white;
                return;
            }

            SettingsUI.DrawHeader(l, "TSA_WD_MidGame_Header".Translate(), SettingsUI.SectionHeaderColor);
            s.midGameRaidBiasPct = SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_RaidBias".Translate(), s.midGameRaidBiasPct, 0f, 2f,
                "TSA_WD_MidGame_RaidBiasTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefMidGameRaidBiasPct);
            s.midGameGrowthMult = SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_GrowthMult".Translate(), s.midGameGrowthMult, 1f, 3f,
                "TSA_WD_MidGame_GrowthMultTooltip".Translate(), 0.05f, SliderFormat.Multiplier, WorldDominationSettings.DefMidGameGrowthMult);
            s.midGameAttackRangeBonusPct = SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_AttackRangeBonus".Translate(), s.midGameAttackRangeBonusPct, 0f, 2f,
                "TSA_WD_MidGame_AttackRangeBonusTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefMidGameAttackRangeBonusPct);
            SettingsUI.DrawCheckbox(l, "TSA_WD_MidGame_ScaleAllyRadius".Translate(), ref s.enableMidGameAllyRadiusScaling,
                "TSA_WD_MidGame_ScaleAllyRadiusTooltip".Translate(),
                defaultValue: WorldDominationSettings.DefEnableMidGameAllyRadiusScaling);
            if (s.enableMidGameAllyRadiusScaling)
            {
                s.midGameAllyRadiusBonusPct = SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_AllyRadiusBonus".Translate(), s.midGameAllyRadiusBonusPct, 0f, 2f,
                    "TSA_WD_MidGame_AllyRadiusBonusTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefMidGameAllyRadiusBonusPct);
            }
            s.midGameGarrisonBoostPct = SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_GarrisonBoost".Translate(), s.midGameGarrisonBoostPct, 0f, 1f,
                "TSA_WD_MidGame_GarrisonBoostTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefMidGameGarrisonBoostPct);
            s.midGameExpandTowardPlayerMaxTiles = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_ExpandTiles".Translate(), s.midGameExpandTowardPlayerMaxTiles, 1f, 12f,
                "TSA_WD_MidGame_ExpandTilesTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefMidGameExpandTowardPlayerMaxTiles));
            SettingsUI.DrawCheckbox(l, "TSA_WD_MidGame_EnableOutpostIncidents".Translate(), ref s.enableMidGameOutpostIncidents,
                "TSA_WD_MidGame_EnableOutpostIncidentsTooltip".Translate(),
                defaultValue: WorldDominationSettings.DefEnableMidGameOutpostIncidents);
            s.midGameOutpostIncidentSeverity = SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_OutpostIncSev".Translate(), s.midGameOutpostIncidentSeverity, 10f, 500f,
                "TSA_WD_MidGame_OutpostIncSevTooltip".Translate(), 5f, SliderFormat.Fixed0, WorldDominationSettings.DefMidGameOutpostIncidentSeverity);
            s.midGameOutpostIncidentDailyChance = SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_OutpostIncChance".Translate(), s.midGameOutpostIncidentDailyChance, 0f, 1f,
                "TSA_WD_MidGame_OutpostIncChanceTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefMidGameOutpostIncidentDailyChance);

            SettingsUI.DrawHeader(l, "TSA_WD_LateGame_HeaderScaling".Translate(), SettingsUI.SectionHeaderColor);
            s.lateGameRaidBiasPct = SettingsUI.LabeledSlider(l, "TSA_WD_Difficulty_RaidBias".Translate(), s.lateGameRaidBiasPct, 0f, 2f,
                "TSA_WD_Difficulty_RaidBiasTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefLateGameRaidBiasPct);
            s.lateGameGrowthMult = SettingsUI.LabeledSlider(l, "TSA_WD_Difficulty_GrowthMult".Translate(), s.lateGameGrowthMult, 1f, 3f,
                "TSA_WD_Difficulty_GrowthMultTooltip".Translate(), 0.05f, SliderFormat.Multiplier, WorldDominationSettings.DefLateGameGrowthMult);
            s.lateGameAttackRangeBonusPct = SettingsUI.LabeledSlider(l, "TSA_WD_LateGame_AttackRangeBonus".Translate(), s.lateGameAttackRangeBonusPct, 0f, 2f,
                "TSA_WD_LateGame_AttackRangeBonusTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefLateGameAttackRangeBonusPct);
            SettingsUI.DrawCheckbox(l, "TSA_WD_LateGame_ScaleAllyRadius".Translate(), ref s.enableLateGameAllyRadiusScaling,
                "TSA_WD_LateGame_ScaleAllyRadiusTooltip".Translate(),
                defaultValue: WorldDominationSettings.DefEnableLateGameAllyRadiusScaling);
            if (s.enableLateGameAllyRadiusScaling)
            {
                s.lateGameAllyRadiusBonusPct = SettingsUI.LabeledSlider(l, "TSA_WD_LateGame_AllyRadiusBonus".Translate(), s.lateGameAllyRadiusBonusPct, 0f, 2f,
                    "TSA_WD_LateGame_AllyRadiusBonusTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefLateGameAllyRadiusBonusPct);
            }
            s.lateGameGarrisonBoostPct = SettingsUI.LabeledSlider(l, "TSA_WD_Difficulty_GarrisonBoost".Translate(), s.lateGameGarrisonBoostPct, 0f, 1f,
                "TSA_WD_Difficulty_GarrisonBoostTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefLateGameGarrisonBoostPct);
            s.lateGameExpandTowardPlayerMaxTiles = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Difficulty_ExpandTiles".Translate(), s.lateGameExpandTowardPlayerMaxTiles, 1f, 12f,
                "TSA_WD_Difficulty_ExpandTilesTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefLateGameExpandTowardPlayerMaxTiles));
            SettingsUI.DrawCheckbox(l, "TSA_WD_Difficulty_EnableOutpostIncidents".Translate(), ref s.enableOutpostIncidents,
                "TSA_WD_Difficulty_EnableOutpostIncidentsTooltip".Translate(),
                defaultValue: WorldDominationSettings.DefEnableOutpostIncidents);
            s.outpostIncidentSeverity = SettingsUI.LabeledSlider(l, "TSA_WD_Difficulty_OutpostIncSev".Translate(), s.outpostIncidentSeverity, 10f, 500f,
                "TSA_WD_Difficulty_OutpostIncSevTooltip".Translate(), 5f, SliderFormat.Fixed0, WorldDominationSettings.DefOutpostIncidentSeverity);
            s.outpostIncidentDailyChance = SettingsUI.LabeledSlider(l, "TSA_WD_Difficulty_OutpostIncChance".Translate(), s.outpostIncidentDailyChance, 0f, 1f,
                "TSA_WD_Difficulty_OutpostIncChanceTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefOutpostIncidentDailyChance);
        }

        private void DrawArrivalStyles(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Raid_HeaderArrivalStyles".Translate(), ref arrivalExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Combat_HeaderArrivalStylesTip".Translate()))
                return;

            s.dropPodRaidChanceT3 = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_DropPodChanceT3".Translate(), s.dropPodRaidChanceT3, 0f, 1f,
                "TSA_WD_Raid_DropPodChanceT3Tip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefDropPodRaidChanceT3);
            s.dropPodRaidChance = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_DropPodChance".Translate(), s.dropPodRaidChance, 0f, 1f,
                "TSA_WD_Raid_DropPodChanceTip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefDropPodRaidChance);
            SettingsUI.TechLevelDropdown(l, "TSA_WD_Raid_DropPodMinTech".Translate(), s.dropPodRaidMinTechLevel,
                v => s.dropPodRaidMinTechLevel = v,
                "TSA_WD_Raid_DropPodMinTechTip".Translate(), WorldDominationSettings.DefDropPodRaidMinTechLevel);
            s.dropPodRaidAttritionMult = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_DropPodAttritionMult".Translate(), s.dropPodRaidAttritionMult, 1f, 10f,
                "TSA_WD_Raid_DropPodAttritionMultTip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefDropPodRaidAttritionMult);
            s.colonySiegeRaidChance = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_ColonySiegeChance".Translate(), s.colonySiegeRaidChance, 0f, 1f,
                "TSA_WD_Raid_ColonySiegeChanceTip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefColonySiegeRaidChance);
        }

        private void DrawAdvancedWorldCombat(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Combat_HeaderAdvancedWorld".Translate(), ref advancedWorldExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Combat_HeaderAdvancedWorldTip".Translate()))
                return;

            SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_AT_TargetPlayerTravelers".Translate(),
                ref s.enableAtTurretTargetPlayerTravelers,
                "TSA_WD_Experimental_AT_TargetPlayerTravelersTip".Translate(),
                defaultValue: WorldDominationSettings.DefEnableAtTurretTargetPlayerTravelers);
            SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_AT_TargetPlayerCaravans".Translate(),
                ref s.enableAtTurretTargetPlayerCaravans,
                "TSA_WD_Experimental_AT_TargetPlayerCaravansTip".Translate(),
                defaultValue: WorldDominationSettings.DefEnableAtTurretTargetPlayerCaravans);
            if (s.enableAtTurretTargetPlayerCaravans)
            {
                s.minPlayerCaravanVisibilityToTarget = SettingsUI.LabeledSlider(l,
                    "TSA_WD_Experimental_MinPlayerCaravanVisibility".Translate(),
                    s.minPlayerCaravanVisibilityToTarget,
                    WorldDominationSettings.MinPlayerCaravanVisibilityToTargetClampLow,
                    WorldDominationSettings.MinPlayerCaravanVisibilityToTargetClampHigh,
                    "TSA_WD_Experimental_MinPlayerCaravanVisibilityTip".Translate(),
                    0.01f, SliderFormat.Percent,
                    WorldDominationSettings.DefMinPlayerCaravanVisibilityToTarget);
            }

            SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_T4GravshipRaids".Translate(),
                ref s.experimentalT4GravshipRaids,
                "TSA_WD_Experimental_T4GravshipRaidsTip".Translate(),
                defaultValue: WorldDominationSettings.DefExperimentalT4GravshipRaids);
            if (s.experimentalT4GravshipRaids)
            {
                s.experimentalGravshipRaidChanceT4 = SettingsUI.LabeledSlider(l,
                    "TSA_WD_Experimental_GravshipRaidChanceT4".Translate(),
                    s.experimentalGravshipRaidChanceT4, 0f, 1f,
                    "TSA_WD_Experimental_GravshipRaidChanceT4Tip".Translate(),
                    0.05f, SliderFormat.Percent, WorldDominationSettings.DefExperimentalGravshipRaidChanceT4);
            }
        }

        private void DrawGarrison(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_BtnGarrisonSettings".Translate(), ref garrisonExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Combat_HeaderGarrisonTip".Translate()))
                return;

            SettingsUI.DrawHeader(l, "TSA_WD_HeaderTribalGarrisons".Translate(), SettingsUI.SectionHeaderColor);
            s.kcsgMultTribalT1 = SettingsUI.LabeledSlider(l, "TSA_WD_Garrison_T1".Translate(), s.kcsgMultTribalT1, 0.1f, 10f,
                "TSA_WD_Garrison_Tooltip".Translate(), 0.1f, SliderFormat.Multiplier, WorldDominationSettings.DefKcsgMultTribalT1);
            s.kcsgMultTribalT2 = SettingsUI.LabeledSlider(l, "TSA_WD_Garrison_T2".Translate(), s.kcsgMultTribalT2, 0.1f, 10f,
                "TSA_WD_Garrison_Tooltip".Translate(), 0.1f, SliderFormat.Multiplier, WorldDominationSettings.DefKcsgMultTribalT2);
            s.kcsgMultTribalT3 = SettingsUI.LabeledSlider(l, "TSA_WD_Garrison_T3".Translate(), s.kcsgMultTribalT3, 0.1f, 10f,
                "TSA_WD_Garrison_Tooltip".Translate(), 0.1f, SliderFormat.Multiplier, WorldDominationSettings.DefKcsgMultTribalT3);
            s.kcsgMultTribalT4 = SettingsUI.LabeledSlider(l, "TSA_WD_Garrison_T4".Translate(), s.kcsgMultTribalT4, 0.1f, 10f,
                "TSA_WD_Garrison_Tooltip".Translate(), 0.1f, SliderFormat.Multiplier, WorldDominationSettings.DefKcsgMultTribalT4);

            SettingsUI.DrawHeader(l, "TSA_WD_HeaderGenericGarrisons".Translate(), SettingsUI.SectionHeaderColor);
            s.kcsgMultGenericT1 = SettingsUI.LabeledSlider(l, "TSA_WD_Garrison_T1".Translate(), s.kcsgMultGenericT1, 0.1f, 10f,
                "TSA_WD_Garrison_Tooltip".Translate(), 0.1f, SliderFormat.Multiplier, WorldDominationSettings.DefKcsgMultGenericT1);
            s.kcsgMultGenericT2 = SettingsUI.LabeledSlider(l, "TSA_WD_Garrison_T2".Translate(), s.kcsgMultGenericT2, 0.1f, 10f,
                "TSA_WD_Garrison_Tooltip".Translate(), 0.1f, SliderFormat.Multiplier, WorldDominationSettings.DefKcsgMultGenericT2);
            s.kcsgMultGenericT3 = SettingsUI.LabeledSlider(l, "TSA_WD_Garrison_T3".Translate(), s.kcsgMultGenericT3, 0.1f, 10f,
                "TSA_WD_Garrison_Tooltip".Translate(), 0.1f, SliderFormat.Multiplier, WorldDominationSettings.DefKcsgMultGenericT3);
            s.kcsgMultGenericT4 = SettingsUI.LabeledSlider(l, "TSA_WD_Garrison_T4".Translate(), s.kcsgMultGenericT4, 0.1f, 10f,
                "TSA_WD_Garrison_Tooltip".Translate(), 0.1f, SliderFormat.Multiplier, WorldDominationSettings.DefKcsgMultGenericT4);

            SettingsUI.DrawHeader(l, "TSA_WD_HeaderDynamicGarrison".Translate(), SettingsUI.SectionHeaderColor);
            s.garrisonOffensiveStrengthMinScale = SettingsUI.LabeledSlider(l, "TSA_WD_Garrison_MinScale".Translate(), s.garrisonOffensiveStrengthMinScale, 0f, 1f,
                "TSA_WD_Garrison_MinScaleTooltip".Translate(), 0.05f, SliderFormat.PercentDecimal, WorldDominationSettings.DefGarrisonOffensiveStrengthMinScale);
        }

        private void DrawT4MortarFeature(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_T4Mortar_Header".Translate(), ref t4MortarFeatureExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Combat_HeaderT4MortarTip".Translate()))
                return;

            SettingsUI.EnumDropdownApply(l, "TSA_WD_Combat_GateWhen".Translate(), s.gateThreatT4MortarVsPlayer,
                v => ApplyGate(s, () => s.gateThreatT4MortarVsPlayer = v),
                WdEscalation.ThreatGateLabel,
                "TSA_WD_Combat_GateT4MortarTip".Translate());

            SettingsUI.TechLevelDropdown(l, "TSA_WD_T4Mortar_MinTech".Translate(), s.npcT4MortarMinTechLevel,
                v => s.npcT4MortarMinTechLevel = v,
                "TSA_WD_T4Mortar_MinTechTip".Translate(), WorldDominationSettings.DefNpcT4MortarMinTechLevel);

            SettingsUI.DrawCheckbox(l, "TSA_WD_T4Mortar_EnableAll".Translate(), ref s.enableNpcT4Mortar,
                "TSA_WD_T4Mortar_EnableAllTooltip".Translate(),
                defaultValue: WorldDominationSettings.DefEnableNpcT4Mortar);

            if (!s.enableNpcT4Mortar)
            {
                if (SettingsUI.SearchMatches("TSA_WD_T4Mortar_DisabledHint".Translate()))
                {
                    l.Gap(6f);
                    GUI.color = Color.gray;
                    l.Label("    <i>" + "TSA_WD_T4Mortar_DisabledHint".Translate() + "</i>");
                    GUI.color = Color.white;
                }
                return;
            }

            s.npcMortarRange = SettingsUI.LabeledSlider(l, "TSA_WD_T4Mortar_Range".Translate(), s.npcMortarRange, 10f, 250f,
                "TSA_WD_T4Mortar_RangeTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefNpcMortarRange);
            s.npcMortarCooldownDays = SettingsUI.LabeledSlider(l, "TSA_WD_T4Mortar_Cooldown".Translate(), s.npcMortarCooldownDays, 0.1f, 20f,
                "TSA_WD_T4Mortar_CooldownTooltip".Translate(), 0.05f, SliderFormat.Fixed1, WorldDominationSettings.DefNpcMortarCooldownDays);
            s.npcMortarDamage = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_NpcMortarDamage".Translate(), s.npcMortarDamage, 0f, 600f,
                "TSA_WD_Settings_NpcMortarDamageTooltip".Translate(), 10f, SliderFormat.Fixed0, WorldDominationSettings.DefNpcMortarDamage);
            s.npcMortarSkillEquivalent = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_NpcMortarSkillEquivalent".Translate(), s.npcMortarSkillEquivalent, 0f, 40f,
                "TSA_WD_Settings_NpcMortarSkillEquivalentTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefNpcMortarSkillEquivalent);
            s.npcMortarHitChance0To50PctRange = SettingsUI.LabeledSlider(l, "TSA_WD_T4Mortar_HitBand0To50".Translate(), s.npcMortarHitChance0To50PctRange, 0f, 1f,
                "TSA_WD_T4Mortar_HitBand0To50Tooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefNpcMortarHitChance0To50PctRange);
            s.npcMortarHitChance51To75PctRange = SettingsUI.LabeledSlider(l, "TSA_WD_T4Mortar_HitBand51To75".Translate(), s.npcMortarHitChance51To75PctRange, 0f, 1f,
                "TSA_WD_T4Mortar_HitBand51To75Tooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefNpcMortarHitChance51To75PctRange);
            s.npcMortarHitChance76To100PctRange = SettingsUI.LabeledSlider(l, "TSA_WD_T4Mortar_HitBand76To100".Translate(), s.npcMortarHitChance76To100PctRange, 0f, 1f,
                "TSA_WD_T4Mortar_HitBand76To100Tooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefNpcMortarHitChance76To100PctRange);
        }

        private void DrawT4AntiAirFeature(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Outpost_HeaderAntiAir".Translate(), ref t4AaFeatureExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_Combat_HeaderAntiAirTip".Translate()))
                return;

            SettingsUI.EnumDropdownApply(l, "TSA_WD_Combat_GateWhen".Translate(), s.gateThreatT4AntiAirVsPlayer,
                v => ApplyGate(s, () => s.gateThreatT4AntiAirVsPlayer = v),
                WdEscalation.ThreatGateLabel,
                "TSA_WD_Combat_GateT4AntiAirTip".Translate());

            SettingsUI.DrawCheckbox(l, "TSA_WD_T4AA_EnableAll".Translate(), ref s.enableNpcT4AntiAir,
                "TSA_WD_T4AA_EnableAllTooltip".Translate(),
                defaultValue: WorldDominationSettings.DefEnableNpcT4AntiAir);

            if (s.enableNpcT4AntiAir)
            {
                s.npcAntiAirRange = SettingsUI.LabeledSlider(l, "TSA_WD_T4AA_Range".Translate(), s.npcAntiAirRange, 10f, 250f,
                    "TSA_WD_T4AA_RangeTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefNpcAntiAirRange);
                s.npcAntiAirCooldownSeconds = SettingsUI.LabeledSlider(l, "TSA_WD_T4AA_Cooldown".Translate(), s.npcAntiAirCooldownSeconds, 5f, 300f,
                    "TSA_WD_T4AA_CooldownTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefNpcAntiAirCooldownSeconds);
                s.npcAntiAirDamage = SettingsUI.LabeledSlider(l, "TSA_WD_T4AA_Damage".Translate(), s.npcAntiAirDamage, 100f, 2000f,
                    "TSA_WD_T4AA_DamageTooltip".Translate(), 10f, SliderFormat.Fixed0, WorldDominationSettings.DefNpcAntiAirDamage);
                s.npcAntiAirSkillEquivalent = SettingsUI.LabeledSlider(l, "TSA_WD_T4AA_Skill".Translate(), s.npcAntiAirSkillEquivalent, 0f, 40f,
                    "TSA_WD_T4AA_SkillTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefNpcAntiAirSkillEquivalent);
                s.npcAntiAirHitChance0To50PctRange = SettingsUI.LabeledSlider(l, "TSA_WD_T4AA_HitBand0To50".Translate(), s.npcAntiAirHitChance0To50PctRange, 0f, 1f,
                    "TSA_WD_T4AA_HitBand0To50Tooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefNpcAntiAirHitChance0To50PctRange);
                s.npcAntiAirHitChance51To75PctRange = SettingsUI.LabeledSlider(l, "TSA_WD_T4AA_HitBand51To75".Translate(), s.npcAntiAirHitChance51To75PctRange, 0f, 1f,
                    "TSA_WD_T4AA_HitBand51To75Tooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefNpcAntiAirHitChance51To75PctRange);
                s.npcAntiAirHitChance76To100PctRange = SettingsUI.LabeledSlider(l, "TSA_WD_T4AA_HitBand76To100".Translate(), s.npcAntiAirHitChance76To100PctRange, 0f, 1f,
                    "TSA_WD_T4AA_HitBand76To100Tooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefNpcAntiAirHitChance76To100PctRange);
                s.npcAntiAirVsMortarHitChance = SettingsUI.LabeledSlider(l, "TSA_WD_T4AA_VsMortar".Translate(), s.npcAntiAirVsMortarHitChance, 0f, 1f,
                    "TSA_WD_T4AA_VsMortarTooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefNpcAntiAirVsMortarHitChance);
            }

            if (SettingsUI.SearchMatches("TSA_WD_T4Mortar_ScanSharedNote".Translate())
                || SettingsUI.SearchMatches("TSA_WD_Settings_InterceptionScanIntervalSec".Translate()))
            {
                l.GapLine();
                GUI.color = Color.gray;
                l.Label("TSA_WD_T4Mortar_ScanSharedNote".Translate());
                GUI.color = Color.white;
            }

            float scanSec = s.interceptionScanIntervalTicks / 60f;
            scanSec = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_InterceptionScanIntervalSec".Translate(), scanSec, 5f, 120f,
                "TSA_WD_Settings_InterceptionScanIntervalSecTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefInterceptionScanIntervalTicks / 60f);
            s.interceptionScanIntervalTicks = Mathf.Max(60, Mathf.RoundToInt(scanSec * 60f));
        }

        private static SettlementTier DrawAmbushMinTierSlider(Listing_Standard l, SettlementTier current)
        {
            string label = "TSA_WD_Experimental_AmbushMinTier".Translate();
            string tip = "TSA_WD_Experimental_AmbushMinTierTip".Translate();
            if (!SettingsUI.SearchMatches(label, tip))
                return current;

            l.Gap(2f);
            Rect r = l.GetRect(24f);
            TooltipHandler.TipRegion(r, SettingsUI.TooltipWithDefault(
                tip,
                AmbushMinTierLabel(WorldDominationSettings.DefSettlementAmbushMinTier)));
            string suffix = AmbushMinTierLabel(current);
            Widgets.Label(r.LeftPart(0.5f), $"{label}: {suffix.Colorize(Color.cyan)}");
            float next = Widgets.HorizontalSlider(r.RightPart(0.5f), (int)current, (int)SettlementTier.T1, (int)SettlementTier.T4, false, null, null, null, 1f);
            return (SettlementTier)Mathf.RoundToInt(next);
        }

        private static int DrawAmbushMaxConcurrentSlider(Listing_Standard l, int current)
        {
            string label = "TSA_WD_Experimental_AmbushMaxConcurrent".Translate();
            string tip = "TSA_WD_Experimental_AmbushMaxConcurrentTip".Translate();
            if (!SettingsUI.SearchMatches(label, tip))
                return current;

            l.Gap(2f);
            Rect r = l.GetRect(24f);
            TooltipHandler.TipRegion(r, SettingsUI.TooltipWithDefault(
                tip,
                (float)WorldDominationSettings.DefSettlementAmbushMaxConcurrent,
                SliderFormat.Fixed0));
            current = Mathf.Clamp(current, 0, 32);
            string suffix = current <= 0
                ? "TSA_WD_Experimental_AmbushMaxConcurrentUnlimited".Translate().ToString()
                : current.ToString();
            Widgets.Label(r.LeftPart(0.5f), $"{label}: {suffix.Colorize(Color.cyan)}");
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
    }
}
