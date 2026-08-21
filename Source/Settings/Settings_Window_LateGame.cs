using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_LateGameSettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;
        private bool midExpanded = true;
        private bool lateExpanded = true;
        private bool bribeCostExpanded = true;

        public override Vector2 InitialSize => new Vector2(850f, 750f);

        public Dialog_LateGameSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnLateGame".Translate();
            optionalTitle = null;
        }

        public override void PreClose()
        {
            base.PreClose();
            WorldDominationMod.settings?.NormalizeEscalationConstraints();
            if (Current.ProgramState != ProgramState.Playing) return;
            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.Notify_WeightsChanged();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            float contentWidth = contentRect.width - 24f;
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 2200f);

            Widgets.BeginScrollView(contentRect, ref scrollPosition, scrollViewRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(scrollViewRect);
            var s = WorldDominationMod.settings;

            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetLateGame(),
                () => { midExpanded = lateExpanded = bribeCostExpanded = true; },
                () => { midExpanded = lateExpanded = bribeCostExpanded = false; });

            l.CheckboxLabeled("TSA_WD_Difficulty_EnableLateGame".Translate(), ref s.enableLateGameScaling,
                SettingsUI.TooltipWithDefault("TSA_WD_Difficulty_EnableLateGameTooltip".Translate(), WorldDominationSettings.DefEnableLateGameScaling));

            l.Gap(8f);
            DrawBribeCostSettings(l, s);

            if (!s.enableLateGameScaling)
            {
                l.End();
                Widgets.EndScrollView();
                return;
            }

            l.Gap(4f);
            l.CheckboxLabeled("TSA_WD_Difficulty_EnableGoodwillDrain".Translate(), ref s.enableGoodwillDrain,
                SettingsUI.TooltipWithDefault("TSA_WD_Difficulty_EnableGoodwillDrainTooltip".Translate(), WorldDominationSettings.DefEnableGoodwillDrain));

            if (s.enableGoodwillDrain)
            {
                s.goodwillDrainIntervalDays = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Difficulty_GoodwillDrainInterval".Translate(), s.goodwillDrainIntervalDays, 1f, 60f,
                    "TSA_WD_Difficulty_GoodwillDrainIntervalTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefGoodwillDrainIntervalDays));
            }

            l.Gap(8f);

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_MidGame_Header".Translate(), ref midExpanded, SettingsUI.SectionHeaderColor))
            {
                s.midGameShareThreshold = SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_ShareThreshold".Translate(), s.midGameShareThreshold, 0f, 1f,
                    "TSA_WD_MidGame_ShareThresholdTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefMidGameShareThreshold);

                s.midGameOutpostStrengthThreshold = SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_OutpostStrengthThreshold".Translate(), s.midGameOutpostStrengthThreshold, 100f, 25000f,
                    "TSA_WD_MidGame_OutpostStrengthThresholdTooltip".Translate(), 100f, SliderFormat.Fixed0, WorldDominationSettings.DefMidGameOutpostStrengthThreshold);

                s.NormalizeEscalationThresholds();
                l.GapLine();

                s.midGameRaidBiasPct = SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_RaidBias".Translate(), s.midGameRaidBiasPct, 0f, 2f,
                    "TSA_WD_MidGame_RaidBiasTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefMidGameRaidBiasPct);

                s.midGameGrowthMult = SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_GrowthMult".Translate(), s.midGameGrowthMult, 1f, 3f,
                    "TSA_WD_MidGame_GrowthMultTooltip".Translate(), 0.05f, SliderFormat.Multiplier, WorldDominationSettings.DefMidGameGrowthMult);

                s.midGameAttackRangeBonusPct = SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_AttackRangeBonus".Translate(), s.midGameAttackRangeBonusPct, 0f, 2f,
                    "TSA_WD_MidGame_AttackRangeBonusTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefMidGameAttackRangeBonusPct);

                l.CheckboxLabeled("TSA_WD_MidGame_ScaleAllyRadius".Translate(), ref s.enableMidGameAllyRadiusScaling,
                    SettingsUI.TooltipWithDefault("TSA_WD_MidGame_ScaleAllyRadiusTooltip".Translate(), WorldDominationSettings.DefEnableMidGameAllyRadiusScaling));
                if (s.enableMidGameAllyRadiusScaling)
                {
                    s.midGameAllyRadiusBonusPct = SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_AllyRadiusBonus".Translate(), s.midGameAllyRadiusBonusPct, 0f, 2f,
                        "TSA_WD_MidGame_AllyRadiusBonusTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefMidGameAllyRadiusBonusPct);
                }

                s.midGameGarrisonBoostPct = SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_GarrisonBoost".Translate(), s.midGameGarrisonBoostPct, 0f, 1f,
                    "TSA_WD_MidGame_GarrisonBoostTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefMidGameGarrisonBoostPct);

                s.midGameExpandTowardPlayerMaxTiles = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_ExpandTiles".Translate(), s.midGameExpandTowardPlayerMaxTiles, 1f, 12f,
                    "TSA_WD_MidGame_ExpandTilesTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefMidGameExpandTowardPlayerMaxTiles));

                bool midMortar = s.enableMidGameT4SettlementMortar;
                l.CheckboxLabeled("TSA_WD_MidGame_OnlyFireT4MortarsAtPlayer".Translate(), ref midMortar,
                    SettingsUI.TooltipWithDefault("TSA_WD_MidGame_T4Mortar_TargetPlayerTooltip".Translate(), WorldDominationSettings.DefEnableMidGameT4SettlementMortar));
                if (midMortar != s.enableMidGameT4SettlementMortar)
                {
                    s.enableMidGameT4SettlementMortar = midMortar;
                    s.NormalizeEscalationT4Flags();
                }

                bool midAa = s.enableMidGameT4SettlementAntiAir;
                l.CheckboxLabeled("TSA_WD_MidGame_OnlyFireT4AntiAirAtPlayer".Translate(), ref midAa,
                    SettingsUI.TooltipWithDefault("TSA_WD_MidGame_T4AA_TargetPlayerTooltip".Translate(), WorldDominationSettings.DefEnableMidGameT4SettlementAntiAir));
                if (midAa != s.enableMidGameT4SettlementAntiAir)
                {
                    s.enableMidGameT4SettlementAntiAir = midAa;
                    s.NormalizeEscalationT4Flags();
                }

                l.CheckboxLabeled("TSA_WD_MidGame_EnableOutpostIncidents".Translate(), ref s.enableMidGameOutpostIncidents,
                    SettingsUI.TooltipWithDefault("TSA_WD_MidGame_EnableOutpostIncidentsTooltip".Translate(), WorldDominationSettings.DefEnableMidGameOutpostIncidents));

                s.midGameOutpostIncidentSeverity = SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_OutpostIncSev".Translate(), s.midGameOutpostIncidentSeverity, 10f, 500f,
                    "TSA_WD_MidGame_OutpostIncSevTooltip".Translate(), 5f, SliderFormat.Fixed0, WorldDominationSettings.DefMidGameOutpostIncidentSeverity);

                s.midGameOutpostIncidentDailyChance = SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_OutpostIncChance".Translate(), s.midGameOutpostIncidentDailyChance, 0f, 1f,
                    "TSA_WD_MidGame_OutpostIncChanceTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefMidGameOutpostIncidentDailyChance);

                if (s.enableGoodwillDrain)
                {
                    s.midGameGoodwillDrainAmount = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_GoodwillDrainAmount".Translate(), s.midGameGoodwillDrainAmount, 0f, 50f,
                        "TSA_WD_MidGame_GoodwillDrainAmountTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefMidGameGoodwillDrainAmount));
                }
            }

            l.Gap(4f);
            GUI.color = Color.gray;
            l.Label("TSA_WD_Difficulty_ActivationHint".Translate());
            GUI.color = Color.white;

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_LateGame_HeaderScaling".Translate(), ref lateExpanded, SettingsUI.SectionHeaderColor))
            {
                s.lateGameShareThreshold = SettingsUI.LabeledSlider(l, "TSA_WD_Difficulty_ShareThreshold".Translate(), s.lateGameShareThreshold, 0f, 1f,
                    "TSA_WD_Difficulty_ShareThresholdTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefLateGameShareThreshold);

                s.lateGameOutpostStrengthThreshold = SettingsUI.LabeledSlider(l, "TSA_WD_Difficulty_OutpostStrengthThreshold".Translate(), s.lateGameOutpostStrengthThreshold, 100f, 25000f,
                    "TSA_WD_Difficulty_OutpostStrengthThresholdTooltip".Translate(), 100f, SliderFormat.Fixed0, WorldDominationSettings.DefLateGameOutpostStrengthThreshold);

                s.NormalizeEscalationThresholds();
                l.GapLine();

                s.lateGameRaidBiasPct = SettingsUI.LabeledSlider(l, "TSA_WD_Difficulty_RaidBias".Translate(), s.lateGameRaidBiasPct, 0f, 2f,
                    "TSA_WD_Difficulty_RaidBiasTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefLateGameRaidBiasPct);

                s.lateGameGrowthMult = SettingsUI.LabeledSlider(l, "TSA_WD_Difficulty_GrowthMult".Translate(), s.lateGameGrowthMult, 1f, 3f,
                    "TSA_WD_Difficulty_GrowthMultTooltip".Translate(), 0.05f, SliderFormat.Multiplier, WorldDominationSettings.DefLateGameGrowthMult);

                s.lateGameAttackRangeBonusPct = SettingsUI.LabeledSlider(l, "TSA_WD_LateGame_AttackRangeBonus".Translate(), s.lateGameAttackRangeBonusPct, 0f, 2f,
                    "TSA_WD_LateGame_AttackRangeBonusTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefLateGameAttackRangeBonusPct);

                l.CheckboxLabeled("TSA_WD_LateGame_ScaleAllyRadius".Translate(), ref s.enableLateGameAllyRadiusScaling,
                    SettingsUI.TooltipWithDefault("TSA_WD_LateGame_ScaleAllyRadiusTooltip".Translate(), WorldDominationSettings.DefEnableLateGameAllyRadiusScaling));
                if (s.enableLateGameAllyRadiusScaling)
                {
                    s.lateGameAllyRadiusBonusPct = SettingsUI.LabeledSlider(l, "TSA_WD_LateGame_AllyRadiusBonus".Translate(), s.lateGameAllyRadiusBonusPct, 0f, 2f,
                        "TSA_WD_LateGame_AllyRadiusBonusTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefLateGameAllyRadiusBonusPct);
                }

                s.lateGameGarrisonBoostPct = SettingsUI.LabeledSlider(l, "TSA_WD_Difficulty_GarrisonBoost".Translate(), s.lateGameGarrisonBoostPct, 0f, 1f,
                    "TSA_WD_Difficulty_GarrisonBoostTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefLateGameGarrisonBoostPct);

                s.lateGameExpandTowardPlayerMaxTiles = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Difficulty_ExpandTiles".Translate(), s.lateGameExpandTowardPlayerMaxTiles, 1f, 12f,
                    "TSA_WD_Difficulty_ExpandTilesTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefLateGameExpandTowardPlayerMaxTiles));

                bool lateMortar = s.enableT4SettlementMortar;
                l.CheckboxLabeled("TSA_WD_LateGame_OnlyFireT4MortarsAtPlayer".Translate(), ref lateMortar,
                    SettingsUI.TooltipWithDefault("TSA_WD_T4Mortar_TargetPlayerTooltip".Translate(), WorldDominationSettings.DefEnableT4SettlementMortar));
                if (lateMortar != s.enableT4SettlementMortar)
                {
                    s.enableT4SettlementMortar = lateMortar;
                    s.NormalizeEscalationT4Flags();
                }

                bool lateAa = s.enableT4SettlementAntiAir;
                l.CheckboxLabeled("TSA_WD_LateGame_OnlyFireT4AntiAirAtPlayer".Translate(), ref lateAa,
                    SettingsUI.TooltipWithDefault("TSA_WD_T4AA_TargetPlayerTooltip".Translate(), WorldDominationSettings.DefEnableT4SettlementAntiAir));
                if (lateAa != s.enableT4SettlementAntiAir)
                {
                    s.enableT4SettlementAntiAir = lateAa;
                    s.NormalizeEscalationT4Flags();
                }

                l.CheckboxLabeled("TSA_WD_Difficulty_EnableOutpostIncidents".Translate(), ref s.enableOutpostIncidents,
                    SettingsUI.TooltipWithDefault("TSA_WD_Difficulty_EnableOutpostIncidentsTooltip".Translate(), WorldDominationSettings.DefEnableOutpostIncidents));

                s.outpostIncidentSeverity = SettingsUI.LabeledSlider(l, "TSA_WD_Difficulty_OutpostIncSev".Translate(), s.outpostIncidentSeverity, 10f, 500f,
                    "TSA_WD_Difficulty_OutpostIncSevTooltip".Translate(), 5f, SliderFormat.Fixed0, WorldDominationSettings.DefOutpostIncidentSeverity);

                s.outpostIncidentDailyChance = SettingsUI.LabeledSlider(l, "TSA_WD_Difficulty_OutpostIncChance".Translate(), s.outpostIncidentDailyChance, 0f, 1f,
                    "TSA_WD_Difficulty_OutpostIncChanceTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefOutpostIncidentDailyChance);

                if (s.enableGoodwillDrain)
                {
                    s.lateGameGoodwillDrainAmount = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_LateGame_GoodwillDrainAmount".Translate(), s.lateGameGoodwillDrainAmount, 0f, 50f,
                        "TSA_WD_LateGame_GoodwillDrainAmountTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefLateGameGoodwillDrainAmount));
                }
            }

            l.End();
            Widgets.EndScrollView();
        }

        private void DrawBribeCostSettings(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Bribe_CostHeader".Translate(), ref bribeCostExpanded, SettingsUI.SectionHeaderColor))
                return;

            s.bribeSettlementSilverPerStrength = SettingsUI.LabeledSlider(l, "TSA_WD_Bribe_SettlementSilverPerStr".Translate(), s.bribeSettlementSilverPerStrength, 0.5f, 10f,
                "TSA_WD_Bribe_SettlementSilverPerStrTip".Translate(), 0.1f, SliderFormat.Fixed1, WorldDominationSettings.DefBribeSettlementSilverPerStrength);
            s.bribeCaravanSilverPerStrengthEarly = SettingsUI.LabeledSlider(l, "TSA_WD_Bribe_CaravanSilverPerStrEarly".Translate(), s.bribeCaravanSilverPerStrengthEarly, 0.5f, 10f,
                "TSA_WD_Bribe_CaravanSilverPerStrEarlyTip".Translate(), 0.1f, SliderFormat.Fixed1, WorldDominationSettings.DefBribeCaravanSilverPerStrengthEarly);
            s.bribeCaravanSilverPerStrengthMid = SettingsUI.LabeledSlider(l, "TSA_WD_Bribe_CaravanSilverPerStrMid".Translate(), s.bribeCaravanSilverPerStrengthMid, 0.5f, 10f,
                "TSA_WD_Bribe_CaravanSilverPerStrMidTip".Translate(), 0.1f, SliderFormat.Fixed1, WorldDominationSettings.DefBribeCaravanSilverPerStrengthMid);
            s.bribeCaravanSilverPerStrengthLate = SettingsUI.LabeledSlider(l, "TSA_WD_Bribe_CaravanSilverPerStrLate".Translate(), s.bribeCaravanSilverPerStrengthLate, 0.5f, 10f,
                "TSA_WD_Bribe_CaravanSilverPerStrLateTip".Translate(), 0.1f, SliderFormat.Fixed1, WorldDominationSettings.DefBribeCaravanSilverPerStrengthLate);
        }
    }
}
