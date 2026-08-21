using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_DiplomacySettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;
        private bool relationsExpanded = true;
        private bool goodwillExpanded = true;
        private bool alliedRaidExpanded = true;
        private bool orderedRoadExpanded = true;
        private bool settlementBuyExpanded = true;
        private bool diplomacyNegotiateExpanded = true;
        private bool factionBribeExpanded = true;
        private bool factionInvestmentExpanded = true;
        private bool eventsExpanded;
        private bool leaderExpanded = true;
        private bool underdogExpanded = true;
        private bool zealExpanded = true;
        private bool coalitionExpanded = true;

        public override Vector2 InitialSize => new Vector2(850f, 750f);

        public Dialog_DiplomacySettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnDiplomacy".Translate();
            optionalTitle = null;
        }

        private void SetAllExpanded(bool expanded)
        {
            relationsExpanded = goodwillExpanded = alliedRaidExpanded = orderedRoadExpanded =
                settlementBuyExpanded = diplomacyNegotiateExpanded = factionBribeExpanded = factionInvestmentExpanded = eventsExpanded = leaderExpanded =
                underdogExpanded = zealExpanded = coalitionExpanded = expanded;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            float contentWidth = contentRect.width - 24f;
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 3400f);

            Widgets.BeginScrollView(contentRect, ref scrollPosition, scrollViewRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(scrollViewRect);
            var s = WorldDominationMod.settings;
            bool advanced = s.showAdvancedSettings;
            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetDiplomacy(),
                () => SetAllExpanded(true),
                () => SetAllExpanded(false));

            // --- SECTION 1: DYNAMIC WORLD RELATIONS (core) ---
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Diplo_Header".Translate(), ref relationsExpanded, SettingsUI.SectionHeaderColor))
            {
                l.CheckboxLabeled(
                    "TSA_WD_Diplo_RandomEnable".Translate(),
                    ref s.enableRandomDiplomacy,
                    SettingsUI.TooltipWithDefault("TSA_WD_Diplo_RandomEnableTooltip".Translate(), WorldDominationSettings.DefEnableRandomDiplomacy)
                );

                if (s.enableRandomDiplomacy)
                {
                    s.diplomacyChangeChance = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_RandomChance".Translate(), s.diplomacyChangeChance, 0f, 1f,
                        "TSA_WD_Diplo_RandomChanceTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefDiplomacyChangeChance);
                }

                l.CheckboxLabeled(
                    "TSA_WD_Diplo_StrongFactionWarEnable".Translate(),
                    ref s.enableStrongFactionWar,
                    SettingsUI.TooltipWithDefault("TSA_WD_Diplo_StrongFactionWarEnableTooltip".Translate(), WorldDominationSettings.DefEnableStrongFactionWar)
                );
                if (s.enableStrongFactionWar)
                {
                    s.strongFactionWarChance = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_StrongFactionWarChance".Translate(), s.strongFactionWarChance, 0f, 1f,
                        "TSA_WD_Diplo_StrongFactionWarChanceTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefStrongFactionWarChance);
                    s.strongFactionWarTopPct = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_StrongFactionWarTopPct".Translate(), s.strongFactionWarTopPct, 0.05f, 1f,
                        "TSA_WD_Diplo_StrongFactionWarTopPctTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefStrongFactionWarTopPct);
                    l.CheckboxLabeled(
                        "TSA_WD_Diplo_StrongFactionWarRequireMidOrLate".Translate(),
                        ref s.strongFactionWarRequireMidOrLate,
                        SettingsUI.TooltipWithDefault("TSA_WD_Diplo_StrongFactionWarRequireMidOrLateTooltip".Translate(), WorldDominationSettings.DefStrongFactionWarRequireMidOrLate)
                    );
                }

                s.revoltChance = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_RevoltChance".Translate(), s.revoltChance, 0f, 1f,
                    "TSA_WD_Diplo_RevoltChanceTooltip".Translate(), 0.002f, SliderFormat.PercentDecimal, WorldDominationSettings.DefRevoltChance);

                l.GapLine();
                if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Diplo_VanillaGoodwillHeader".Translate(), ref goodwillExpanded, SettingsUI.SectionHeaderColor))
                {
                    s.maxGoodwill = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_MaxGoodwill".Translate(), s.maxGoodwill, 100f, 200f,
                        "TSA_WD_Diplo_MaxGoodwillTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefMaxGoodwill));
                    VanillaGoodwillSettingsUI.DrawListingRows(l, s);
                }

                l.GapLine();
                DrawAlliedRaidOrderSettings(l, s);
            }

            if (advanced)
            {
                l.Gap(18f);

                // --- SECTION 2: BUFFS & DEBUFFS (advanced) ---
                if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Diplo_GlobalEventsHeader".Translate(), ref eventsExpanded, SettingsUI.SectionHeaderColor))
                {
                    if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Diplo_Block_Leader".Translate(), ref leaderExpanded, SettingsUI.SectionHeaderColor))
                    {
                        l.CheckboxLabeled("TSA_WD_Diplo_Enable".Translate(), ref s.enableLeaderHandicap,
                            SettingsUI.TooltipWithDefault("TSA_WD_Diplo_LeaderHandicapTooltip".Translate(), WorldDominationSettings.DefEnableLeaderHandicap));
                        if (s.enableLeaderHandicap)
                        {
                            s.durLeaderHandicapDays = SettingsUI.LabeledSlider(l, "TSA_WD_Slider_Duration".Translate(), s.durLeaderHandicapDays, 0.5f, 60f,
                                "TSA_WD_Slider_DurationTooltip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefDurLeaderHandicapDays);
                            s.cdLeaderHandicapDays = SettingsUI.LabeledSlider(l, "TSA_WD_Slider_Cooldown".Translate(), s.cdLeaderHandicapDays, 0.5f, 60f,
                                "TSA_WD_Slider_CooldownTooltip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefCdLeaderHandicapDays);
                            s.leaderHandicapTriggerChance = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_TriggerChance".Translate(), s.leaderHandicapTriggerChance, 0f, 1f,
                                "TSA_WD_Diplo_LeaderTriggerChanceTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefLeaderHandicapTriggerChance);
                            s.leaderIncidentWeightMult = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_IncidentWeight".Translate(), s.leaderIncidentWeightMult, 0.5f, 4f,
                                "TSA_WD_Diplo_LeaderIncidentWeightTooltip".Translate(), 0.1f, SliderFormat.Fixed1, WorldDominationSettings.DefLeaderIncidentWeightMult);
                            s.leaderIncidentSeverityMult = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_IncidentSeverity".Translate(), s.leaderIncidentSeverityMult, 0.5f, 4f,
                                "TSA_WD_Diplo_LeaderIncidentSeverityTooltip".Translate(), 0.1f, SliderFormat.Fixed1, WorldDominationSettings.DefLeaderIncidentSeverityMult);
                            l.Gap(6f);
                        }
                    }

                    if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Diplo_Block_Underdog".Translate(), ref underdogExpanded, SettingsUI.SectionHeaderColor))
                    {
                        l.CheckboxLabeled("TSA_WD_Diplo_Enable".Translate(), ref s.enableUnderdogBuff,
                            SettingsUI.TooltipWithDefault("TSA_WD_Diplo_UnderdogBuffTooltip".Translate(), WorldDominationSettings.DefEnableUnderdogBuff));
                        if (s.enableUnderdogBuff)
                        {
                            s.durUnderdogBuffDays = SettingsUI.LabeledSlider(l, "TSA_WD_Slider_Duration".Translate(), s.durUnderdogBuffDays, 0.5f, 60f,
                                "TSA_WD_Slider_DurationTooltip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefDurUnderdogBuffDays);
                            s.cdUnderdogBuffDays = SettingsUI.LabeledSlider(l, "TSA_WD_Slider_Cooldown".Translate(), s.cdUnderdogBuffDays, 0.5f, 60f,
                                "TSA_WD_Slider_CooldownTooltip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefCdUnderdogBuffDays);
                            s.underdogBuffTriggerChance = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_TriggerChance".Translate(), s.underdogBuffTriggerChance, 0f, 1f,
                                "TSA_WD_Diplo_UnderdogTriggerChanceTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefUnderdogBuffTriggerChance);
                            s.underdogActionShareMult = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_ActionShareMult".Translate(), s.underdogActionShareMult, 1f, 4f,
                                "TSA_WD_Diplo_UnderdogActionShareTooltip".Translate(), 0.1f, SliderFormat.Fixed1, WorldDominationSettings.DefUnderdogActionShareMult);
                            s.underdogIncidentWeightMult = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_UnderdogIncidentWeight".Translate(), s.underdogIncidentWeightMult, 0.1f, 1f,
                                "TSA_WD_Diplo_UnderdogIncidentWeightTooltip".Translate(), 0.05f, SliderFormat.Fixed2, WorldDominationSettings.DefUnderdogIncidentWeightMult);
                            s.underdogIncidentSeverityMult = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_UnderdogIncidentSeverity".Translate(), s.underdogIncidentSeverityMult, 0.1f, 1f,
                                "TSA_WD_Diplo_UnderdogIncidentSeverityTooltip".Translate(), 0.05f, SliderFormat.Fixed2, WorldDominationSettings.DefUnderdogIncidentSeverityMult);
                            s.underdogGrowthGainMult = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_GrowthGainMult".Translate(), s.underdogGrowthGainMult, 1f, 4f,
                                "TSA_WD_Diplo_UnderdogGrowthGainTooltip".Translate(), 0.1f, SliderFormat.Fixed1, WorldDominationSettings.DefUnderdogGrowthGainMult);
                            l.Gap(6f);
                        }
                    }

                    if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Diplo_Block_Zeal".Translate(), ref zealExpanded, SettingsUI.SectionHeaderColor))
                    {
                        l.CheckboxLabeled("TSA_WD_Diplo_Enable".Translate(), ref s.enableExpansionistZeal,
                            SettingsUI.TooltipWithDefault("TSA_WD_Diplo_ExpansionistZealTooltip".Translate(), WorldDominationSettings.DefEnableExpansionistZeal));
                        if (s.enableExpansionistZeal)
                        {
                            s.durExpansionistZealDays = SettingsUI.LabeledSlider(l, "TSA_WD_Slider_Duration".Translate(), s.durExpansionistZealDays, 0.5f, 60f,
                                "TSA_WD_Slider_DurationTooltip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefDurExpansionistZealDays);
                            s.cdExpansionistZealDays = SettingsUI.LabeledSlider(l, "TSA_WD_Slider_Cooldown".Translate(), s.cdExpansionistZealDays, 0.5f, 60f,
                                "TSA_WD_Slider_CooldownTooltip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefCdExpansionistZealDays);
                            s.zealTriggerChance = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_TriggerChance".Translate(), s.zealTriggerChance, 0f, 1f,
                                "TSA_WD_Diplo_ZealTriggerChanceTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefZealTriggerChance);
                            s.zealRaidRangeMult = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_RaidRangeMult".Translate(), s.zealRaidRangeMult, 1f, 4f,
                                "TSA_WD_Diplo_ZealRaidRangeMultTooltip".Translate(), 0.1f, SliderFormat.Fixed1, WorldDominationSettings.DefZealRaidRangeMult);
                            s.zealAttritionMult = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_AttritionMult".Translate(), s.zealAttritionMult, 0.1f, 1f,
                                "TSA_WD_Diplo_ZealAttritionMultTooltip".Translate(), 0.05f, SliderFormat.Fixed2, WorldDominationSettings.DefZealAttritionMult);
                            l.Gap(10f);
                        }
                    }

                    if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Diplo_Block_Coalition".Translate(), ref coalitionExpanded, SettingsUI.SectionHeaderColor))
                    {
                        l.CheckboxLabeled("TSA_WD_Diplo_Enable".Translate(), ref s.enableAntiLeaderCoalition,
                            SettingsUI.TooltipWithDefault("TSA_WD_Diplo_AntiLeaderTooltip".Translate(), WorldDominationSettings.DefEnableAntiLeaderCoalition));
                        if (s.enableAntiLeaderCoalition)
                        {
                            s.durAntiLeaderCoalitionDays = SettingsUI.LabeledSlider(l, "TSA_WD_Slider_Duration".Translate(), s.durAntiLeaderCoalitionDays, 0.5f, 60f,
                                "TSA_WD_Slider_DurationTooltip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefDurAntiLeaderCoalitionDays);
                            s.cdAntiLeaderCoalitionDays = SettingsUI.LabeledSlider(l, "TSA_WD_Slider_Cooldown".Translate(), s.cdAntiLeaderCoalitionDays, 0.5f, 60f,
                                "TSA_WD_Slider_CooldownTooltip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefCdAntiLeaderCoalitionDays);
                            s.antiLeaderCoalitionTriggerChance = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_TriggerChance".Translate(), s.antiLeaderCoalitionTriggerChance, 0f, 1f,
                                "TSA_WD_Diplo_CoalitionTriggerChanceTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefAntiLeaderCoalitionTriggerChance);
                            l.Gap(6f);
                        }
                    }
                }
            }

            l.End();
            Widgets.EndScrollView();
        }

        private void DrawAlliedRaidOrderSettings(Listing_Standard l, WorldDominationSettings s)
        {
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Diplo_AlliedRaidOrdersHeader".Translate(), ref alliedRaidExpanded, SettingsUI.SectionHeaderColor))
            {
                s.alliedRaidOrderMinWinChance = SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_AlliedRaidMinWinChance".Translate(), s.alliedRaidOrderMinWinChance, 0f, 1f,
                    "TSA_WD_Diplo_AlliedRaidMinWinChanceTooltip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefAlliedRaidOrderMinWinChance);
                l.Gap(6f);
                DrawGoodwillCostRow(l, "TSA_WD_Diplo_AlliedRaidClaimCosts".Translate(), ref s.alliedRaidClaimCostT1, ref s.alliedRaidClaimCostT2, ref s.alliedRaidClaimCostT3, ref s.alliedRaidClaimCostT4,
                    "TSA_WD_Diplo_AlliedRaidClaimCostsTooltip".Translate(),
                    WorldDominationSettings.DefAlliedRaidClaimCostT1, WorldDominationSettings.DefAlliedRaidClaimCostT2, WorldDominationSettings.DefAlliedRaidClaimCostT3, WorldDominationSettings.DefAlliedRaidClaimCostT4);
                DrawGoodwillCostRow(l, "TSA_WD_Diplo_AlliedRaidAwardCosts".Translate(), ref s.alliedRaidAwardCostT1, ref s.alliedRaidAwardCostT2, ref s.alliedRaidAwardCostT3, ref s.alliedRaidAwardCostT4,
                    "TSA_WD_Diplo_AlliedRaidAwardCostsTooltip".Translate(),
                    WorldDominationSettings.DefAlliedRaidAwardCostT1, WorldDominationSettings.DefAlliedRaidAwardCostT2, WorldDominationSettings.DefAlliedRaidAwardCostT3, WorldDominationSettings.DefAlliedRaidAwardCostT4);
                DrawGoodwillCostRow(l, "TSA_WD_Diplo_ConquestAllyGiftGoodwill".Translate(), ref s.conquestAllyGiftGoodwillT1, ref s.conquestAllyGiftGoodwillT2, ref s.conquestAllyGiftGoodwillT3, ref s.conquestAllyGiftGoodwillT4,
                    "TSA_WD_Diplo_ConquestAllyGiftGoodwillTooltip".Translate(),
                    WorldDominationSettings.DefConquestAllyGiftGoodwillT1, WorldDominationSettings.DefConquestAllyGiftGoodwillT2, WorldDominationSettings.DefConquestAllyGiftGoodwillT3, WorldDominationSettings.DefConquestAllyGiftGoodwillT4);
            }

            l.Gap(12f);
            DrawOrderedRoadOrderSettings(l, s);
            l.Gap(12f);
            DrawSettlementBuySettings(l, s);
            DrawDiplomacyNegotiateSettings(l, s);
            l.Gap(12f);
            DrawFactionBribeSettings(l, s);
            l.Gap(12f);
            DrawFactionInvestmentSettings(l, s);
        }

        private void DrawFactionBribeSettings(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Bribe_SettingsHeader".Translate(), ref factionBribeExpanded, SettingsUI.SectionHeaderColor))
                return;

            l.CheckboxLabeled("TSA_WD_Bribe_Enable".Translate(), ref s.enableFactionBribe,
                SettingsUI.TooltipWithDefault("TSA_WD_Bribe_EnableTip".Translate(), WorldDominationSettings.DefEnableFactionBribe));
            if (!s.enableFactionBribe) return;

            s.bribeCeasefireDaysShort = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Bribe_DaysShort".Translate(), s.bribeCeasefireDaysShort, 1f, 60f,
                "TSA_WD_Bribe_DaysTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefBribeCeasefireDaysShort);
            s.bribeCeasefireDaysMedium = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Bribe_DaysMedium".Translate(), s.bribeCeasefireDaysMedium, 1f, 90f,
                "TSA_WD_Bribe_DaysTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefBribeCeasefireDaysMedium);
            s.bribeCeasefireDaysLong = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Bribe_DaysLong".Translate(), s.bribeCeasefireDaysLong, 1f, 120f,
                "TSA_WD_Bribe_DaysTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefBribeCeasefireDaysLong);
            s.bribeCeasefireDiscountMedium = SettingsUI.LabeledSlider(l, "TSA_WD_Bribe_DiscountMedium".Translate(), s.bribeCeasefireDiscountMedium, 0f, 0.5f,
                "TSA_WD_Bribe_DiscountTip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefBribeCeasefireDiscountMedium);
            s.bribeCeasefireDiscountLong = SettingsUI.LabeledSlider(l, "TSA_WD_Bribe_DiscountLong".Translate(), s.bribeCeasefireDiscountLong, 0f, 0.5f,
                "TSA_WD_Bribe_DiscountTip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefBribeCeasefireDiscountLong);
            s.bribeRaidAskFloorFraction = SettingsUI.LabeledSlider(l, "TSA_WD_Bribe_RaidAskFloor".Translate(), s.bribeRaidAskFloorFraction, 0f, 1f,
                "TSA_WD_Bribe_RaidAskFloorTip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefBribeRaidAskFloorFraction);
            s.bribeInvestmentFraction = SettingsUI.LabeledSlider(l, "TSA_WD_Bribe_InvestmentFraction".Translate(), s.bribeInvestmentFraction, 0f, 1f,
                "TSA_WD_Bribe_InvestmentFractionTip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefBribeInvestmentFraction);
            s.bribeCaravanInvestmentRadiusTiles = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Bribe_CaravanInvestRadius".Translate(), s.bribeCaravanInvestmentRadiusTiles, 5f, 100f,
                "TSA_WD_Bribe_CaravanInvestRadiusTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefBribeCaravanInvestmentRadiusTiles);
            s.bribeGoodwillDivisor = SettingsUI.LabeledSlider(l, "TSA_WD_Bribe_GoodwillBonusDivisor".Translate(), s.bribeGoodwillDivisor, 50f, 2000f,
                "TSA_WD_Bribe_GoodwillBonusDivisorTip".Translate(), 10f, SliderFormat.Fixed0, WorldDominationSettings.DefBribeGoodwillDivisor);
        }

        private void DrawFactionInvestmentSettings(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_FactionInvestment_SettingsHeader".Translate(), ref factionInvestmentExpanded, SettingsUI.SectionHeaderColor))
                return;

            l.CheckboxLabeled("TSA_WD_FactionInvestment_Enable".Translate(), ref s.enableFactionSettlementInvestment,
                SettingsUI.TooltipWithDefault("TSA_WD_FactionInvestment_EnableTip".Translate(), WorldDominationSettings.DefEnableFactionSettlementInvestment));
            if (!s.enableFactionSettlementInvestment) return;

            s.factionInvestmentStrengthPer100Silver = SettingsUI.LabeledSlider(l, "TSA_WD_FactionInvestment_StrengthPer100".Translate(), s.factionInvestmentStrengthPer100Silver, 0f, 50f,
                "TSA_WD_FactionInvestment_StrengthPer100Tip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefFactionInvestmentStrengthPer100Silver);
            s.factionInvestmentRadiusTiles = (int)SettingsUI.LabeledSlider(l, "TSA_WD_FactionInvestment_Radius".Translate(), s.factionInvestmentRadiusTiles, 5f, 60f,
                "TSA_WD_FactionInvestment_RadiusTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefFactionInvestmentRadiusTiles);
            s.factionInvestmentUpgradeT1ToT2Silver = SettingsUI.LabeledSlider(l, "TSA_WD_FactionInvestment_UpgradeT1T2".Translate(), s.factionInvestmentUpgradeT1ToT2Silver, 0f, 20000f,
                "TSA_WD_FactionInvestment_UpgradeTip".Translate(), 50f, SliderFormat.Fixed0, WorldDominationSettings.DefFactionInvestmentUpgradeT1ToT2Silver);
            s.factionInvestmentUpgradeT2ToT3Silver = SettingsUI.LabeledSlider(l, "TSA_WD_FactionInvestment_UpgradeT2T3".Translate(), s.factionInvestmentUpgradeT2ToT3Silver, 0f, 30000f,
                "TSA_WD_FactionInvestment_UpgradeTip".Translate(), 50f, SliderFormat.Fixed0, WorldDominationSettings.DefFactionInvestmentUpgradeT2ToT3Silver);
            s.factionInvestmentUpgradeT3ToT4Silver = SettingsUI.LabeledSlider(l, "TSA_WD_FactionInvestment_UpgradeT3T4".Translate(), s.factionInvestmentUpgradeT3ToT4Silver, 0f, 50000f,
                "TSA_WD_FactionInvestment_UpgradeTip".Translate(), 50f, SliderFormat.Fixed0, WorldDominationSettings.DefFactionInvestmentUpgradeT3ToT4Silver);
            s.factionInvestmentUpgradeSuccessChance = SettingsUI.LabeledSlider(l, "TSA_WD_FactionInvestment_UpgradeSuccessChance".Translate(), s.factionInvestmentUpgradeSuccessChance, 0f, 1f,
                "TSA_WD_FactionInvestment_UpgradeSuccessChanceTip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefFactionInvestmentUpgradeSuccessChance);
        }

        private void DrawSettlementBuySettings(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_BuySettlement_SettingsHeader".Translate(), ref settlementBuyExpanded, SettingsUI.SectionHeaderColor))
                return;

            l.CheckboxLabeled("TSA_WD_BuySettlement_Enable".Translate(), ref s.enableSettlementBuy,
                SettingsUI.TooltipWithDefault("TSA_WD_BuySettlement_EnableTip".Translate(), WorldDominationSettings.DefEnableSettlementBuy));
            if (!s.enableSettlementBuy) return;

            s.settlementBuyAskT1 = SettingsUI.LabeledSlider(l, "TSA_WD_BuySettlement_AskT1".Translate(), s.settlementBuyAskT1, 500f, 50000f,
                "TSA_WD_BuySettlement_AskTip".Translate(), 100f, SliderFormat.Fixed0, WorldDominationSettings.DefSettlementBuyAskT1);
            s.settlementBuyAskT2 = SettingsUI.LabeledSlider(l, "TSA_WD_BuySettlement_AskT2".Translate(), s.settlementBuyAskT2, 500f, 50000f,
                "TSA_WD_BuySettlement_AskTip".Translate(), 100f, SliderFormat.Fixed0, WorldDominationSettings.DefSettlementBuyAskT2);
            s.settlementBuyAskT3 = SettingsUI.LabeledSlider(l, "TSA_WD_BuySettlement_AskT3".Translate(), s.settlementBuyAskT3, 500f, 80000f,
                "TSA_WD_BuySettlement_AskTip".Translate(), 100f, SliderFormat.Fixed0, WorldDominationSettings.DefSettlementBuyAskT3);
            s.settlementBuyAskT4 = SettingsUI.LabeledSlider(l, "TSA_WD_BuySettlement_AskT4".Translate(), s.settlementBuyAskT4, 500f, 100000f,
                "TSA_WD_BuySettlement_AskTip".Translate(), 100f, SliderFormat.Fixed0, WorldDominationSettings.DefSettlementBuyAskT4);
            s.settlementBuySilverPerGoodwill = SettingsUI.LabeledSlider(l, "TSA_WD_BuySettlement_SilverPerGw".Translate(), s.settlementBuySilverPerGoodwill, 10f, 500f,
                "TSA_WD_BuySettlement_SilverPerGwTip".Translate(), 5f, SliderFormat.Fixed0, WorldDominationSettings.DefSettlementBuySilverPerGoodwill);
            s.settlementBuyMaxGoodwillShare = SettingsUI.LabeledSlider(l, "TSA_WD_BuySettlement_MaxGwShare".Translate(), s.settlementBuyMaxGoodwillShare, 0f, 1f,
                "TSA_WD_BuySettlement_MaxGwShareTip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefSettlementBuyMaxGoodwillShare);
        }

        private void DrawDiplomacyNegotiateSettings(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Negotiate_SettingsHeader".Translate(), ref diplomacyNegotiateExpanded, SettingsUI.SectionHeaderColor))
                return;

            l.CheckboxLabeled("TSA_WD_Negotiate_Enable".Translate(), ref s.enableDiplomacyNegotiate,
                SettingsUI.TooltipWithDefault("TSA_WD_Negotiate_EnableTip".Translate(), WorldDominationSettings.DefEnableDiplomacyNegotiate));
            if (!s.enableDiplomacyNegotiate) return;

            s.negotiateAskMinSilver = SettingsUI.LabeledSlider(l, "TSA_WD_Negotiate_AskMin".Translate(), s.negotiateAskMinSilver, 1000f, 20000f,
                "TSA_WD_Negotiate_AskMinTip".Translate(), 100f, SliderFormat.Fixed0, WorldDominationSettings.DefNegotiateAskMinSilver);
            s.negotiateAskMaxSilver = SettingsUI.LabeledSlider(l, "TSA_WD_Negotiate_AskMax".Translate(), s.negotiateAskMaxSilver, 5000f, 80000f,
                "TSA_WD_Negotiate_AskMaxTip".Translate(), 100f, SliderFormat.Fixed0, WorldDominationSettings.DefNegotiateAskMaxSilver);
        }

        private void DrawOrderedRoadOrderSettings(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Diplo_OrderedRoadOrdersHeader".Translate(), ref orderedRoadExpanded, SettingsUI.SectionHeaderColor))
                return;

            l.Label("TSA_WD_Diplo_OrderedRoadPerSegmentCosts".Translate());
            float max = GoodwillCapUtility.MaxGoodwillCap();
            s.orderedRoadPerSegmentT1 = SettingsUI.LabeledSlider(l, "TSA_WD_RoadDirt".Translate(), s.orderedRoadPerSegmentT1, 0f, max,
                "TSA_WD_Diplo_OrderedRoadPerSegmentCostsTooltip".Translate(), 0.05f, SliderFormat.Fixed2, WorldDominationSettings.DefOrderedRoadPerSegmentRateT1);
            s.orderedRoadPerSegmentT2 = SettingsUI.LabeledSlider(l, "TSA_WD_RoadStone".Translate(), s.orderedRoadPerSegmentT2, 0f, max,
                "TSA_WD_Diplo_OrderedRoadPerSegmentCostsTooltip".Translate(), 0.05f, SliderFormat.Fixed2, WorldDominationSettings.DefOrderedRoadPerSegmentRateT2);
            s.orderedRoadPerSegmentT3 = SettingsUI.LabeledSlider(l, "TSA_WD_RoadAsphalt".Translate(), s.orderedRoadPerSegmentT3, 0f, max,
                "TSA_WD_Diplo_OrderedRoadPerSegmentCostsTooltip".Translate(), 0.05f, SliderFormat.Fixed2, WorldDominationSettings.DefOrderedRoadPerSegmentRateT3);
            s.orderedTraderGoodwillCost = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Diplo_OrderedTraderCost".Translate(), s.orderedTraderGoodwillCost, 0f, max,
                "TSA_WD_Diplo_OrderedTraderCostTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefOrderedTraderGoodwillCost);
        }

        private void DrawGoodwillCostRow(Listing_Standard l, string label, ref int t1, ref int t2, ref int t3, ref int t4, string tooltip, int defaultT1, int defaultT2, int defaultT3, int defaultT4)
        {
            l.Label(label);
            float max = GoodwillCapUtility.MaxGoodwillCap();
            t1 = (int)SettingsUI.LabeledSlider(l, "T1", t1, 0f, max, tooltip, 1f, SliderFormat.Fixed0, defaultT1);
            t2 = (int)SettingsUI.LabeledSlider(l, "T2", t2, 0f, max, tooltip, 1f, SliderFormat.Fixed0, defaultT2);
            t3 = (int)SettingsUI.LabeledSlider(l, "T3", t3, 0f, max, tooltip, 1f, SliderFormat.Fixed0, defaultT3);
            t4 = (int)SettingsUI.LabeledSlider(l, "T4", t4, 0f, max, tooltip, 1f, SliderFormat.Fixed0, defaultT4);
            l.Gap(8f);
        }
    }
}
