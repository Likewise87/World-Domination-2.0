using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_DailyActionsSettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;
        private bool tiersExpanded = true;
        private bool weightsExpanded;
        private bool fortifyExpanded = true;
        private bool capsExpanded;
        private bool cooldownsExpanded;

        public override Vector2 InitialSize => new Vector2(850f, 750f);

        public Dialog_DailyActionsSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnDailyActions".Translate();
            optionalTitle = null;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            float contentWidth = contentRect.width - 24f;
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 3200f);

            Widgets.BeginScrollView(contentRect, ref scrollPosition, scrollViewRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(scrollViewRect);
            var s = WorldDominationMod.settings;
            bool advanced = s.showAdvancedSettings;
            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetDailyActions(),
                () => { tiersExpanded = weightsExpanded = fortifyExpanded = capsExpanded = cooldownsExpanded = true; },
                () => { tiersExpanded = weightsExpanded = fortifyExpanded = capsExpanded = cooldownsExpanded = false; });

            // 1. TIER CONTRIBUTIONS (Economic Shares) - core
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Daily_HeaderTiers".Translate(), ref tiersExpanded, SettingsUI.SectionHeaderColor))
            {

            float[] tierVals = { s.tier1Share, s.tier2Share, s.tier3Share, s.tier4Share };
            string[] tierLabels = {
                "TSA_WD_Tier1".Translate().ToString(),
                "TSA_WD_Tier2".Translate().ToString(),
                "TSA_WD_Tier3".Translate().ToString(),
                "TSA_WD_Tier4".Translate().ToString()
            };
            string[] tierTips = {
                "TSA_WD_Daily_Tier1Tip".Translate().ToString(),
                "TSA_WD_Daily_Tier2Tip".Translate().ToString(),
                "TSA_WD_Daily_Tier3Tip".Translate().ToString(),
                "TSA_WD_Daily_Tier4Tip".Translate().ToString()
            };

            SettingsUI.MultiColumnSlider(l, tierLabels, tierVals, new Vector2(0.05f, 2.5f), tierTips, 0.01f, SliderFormat.Fixed2, 38f,
                new[] { WorldDominationSettings.DefTier1Share, WorldDominationSettings.DefTier2Share, WorldDominationSettings.DefTier3Share, WorldDominationSettings.DefTier4Share });

            s.tier1Share = tierVals[0];
            s.tier2Share = tierVals[1];
            s.tier3Share = tierVals[2];
            s.tier4Share = tierVals[3];
            }
            // 2. ACTION LIKELIHOOD WEIGHTS - core (prioritized, always visible)
            l.Gap(12f);
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Daily_HeaderWeights".Translate(), ref weightsExpanded, SettingsUI.SectionHeaderColor))
            {
            float displayPool = s.WeightPercentDisplayPool;
            s.weightRaid = SettingsUI.WeightSlider(l, "TSA_WD_Daily_WeightRaid".Translate(), s.weightRaid, displayPool, 0f, 400f, "TSA_WD_Daily_WeightRaidTip".Translate(), WorldDominationSettings.DefWeightRaid);
            s.weightMinorIncident = SettingsUI.WeightSlider(l, "TSA_WD_Daily_WeightMinor".Translate(), s.weightMinorIncident, displayPool, 0f, 400f, "TSA_WD_Daily_WeightMinorTip".Translate(), WorldDominationSettings.DefWeightMinorIncident);
            s.weightMajorIncident = SettingsUI.WeightSlider(l, "TSA_WD_Daily_WeightMajor".Translate(), s.weightMajorIncident, displayPool, 0f, 400f, "TSA_WD_Daily_WeightMajorTip".Translate(), WorldDominationSettings.DefWeightMajorIncident);
            s.weightBuildRoad = SettingsUI.WeightSlider(l, "TSA_WD_Daily_WeightBuildRoad".Translate(), s.weightBuildRoad, displayPool, 0f, 400f, "TSA_WD_Daily_WeightBuildRoadTip".Translate(), WorldDominationSettings.DefWeightBuildRoad);
            s.weightTrader = SettingsUI.WeightSlider(l, "TSA_WD_Daily_WeightTrader".Translate(), s.weightTrader, displayPool, 0f, 400f, "TSA_WD_Daily_WeightTraderTip".Translate(), WorldDominationSettings.DefWeightTrader);
            s.weightFortify = SettingsUI.WeightSlider(l, "TSA_WD_Daily_WeightFortify".Translate(), s.weightFortify, displayPool, 0f, 400f, "TSA_WD_Daily_WeightFortifyTip".Translate(), WorldDominationSettings.DefWeightFortify);

            l.Gap(8f);
            SettingsUI.DrawHeader(l, "TSA_WD_Daily_HeaderNearCapActions".Translate());
            bool showDevelopPct = s.includeDevelopWeightInPercentDisplay;
            s.weightGrow = SettingsUI.WeightSlider(l, "TSA_WD_Daily_WeightGrow".Translate(), s.weightGrow, displayPool, 0f, 400f,
                "TSA_WD_Daily_WeightGrowTip".Translate(), WorldDominationSettings.DefWeightGrow, showPercent: showDevelopPct);
            SettingsUI.DrawCheckbox(l, "TSA_WD_Daily_IncludeDevelopInPercent".Translate(),
                ref s.includeDevelopWeightInPercentDisplay,
                "TSA_WD_Daily_IncludeDevelopInPercentTip".Translate(),
                defaultValue: WorldDominationSettings.DefIncludeDevelopWeightInPercentDisplay);
            }

            // 3. NPC FORTIFY - core
            l.Gap(12f);
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Daily_HeaderFortify".Translate(), ref fortifyExpanded, SettingsUI.SectionHeaderColor))
            {
                s.fortifyMinTilesFromSelf = (int)SettingsUI.LabeledSlider(l,
                    "TSA_WD_Fortify_MinFromSelf".Translate(),
                    s.fortifyMinTilesFromSelf, 1f, 20f,
                    "TSA_WD_Fortify_MinFromSelfTip".Translate(),
                    1f, SliderFormat.Fixed0, WorldDominationSettings.DefFortifyMinTilesFromSelf);
                s.fortifyMinTilesFromOtherSettlement = (int)SettingsUI.LabeledSlider(l,
                    "TSA_WD_Fortify_MinFromOther".Translate(),
                    s.fortifyMinTilesFromOtherSettlement, 0f, 20f,
                    "TSA_WD_Fortify_MinFromOtherTip".Translate(),
                    1f, SliderFormat.Fixed0, WorldDominationSettings.DefFortifyMinTilesFromOtherSettlement);
                s.fortifyMaxTilesFromSelf = (int)SettingsUI.LabeledSlider(l,
                    "TSA_WD_Fortify_MaxFromSelf".Translate(),
                    s.fortifyMaxTilesFromSelf, 2f, 30f,
                    "TSA_WD_Fortify_MaxFromSelfTip".Translate(),
                    1f, SliderFormat.Fixed0, WorldDominationSettings.DefFortifyMaxTilesFromSelf);
                if (s.fortifyMaxTilesFromSelf < s.fortifyMinTilesFromSelf)
                    s.fortifyMaxTilesFromSelf = s.fortifyMinTilesFromSelf;

                s.fortifyMaxTravelTiles = (int)SettingsUI.LabeledSlider(l,
                    "TSA_WD_Fortify_MaxTravel".Translate(),
                    s.fortifyMaxTravelTiles, 5f, 80f,
                    "TSA_WD_Fortify_MaxTravelTip".Translate(),
                    1f, SliderFormat.Fixed0, WorldDominationSettings.DefFortifyMaxTravelTiles);

                s.fortifyTerritoryLinkMaxTiles = (int)SettingsUI.LabeledSlider(l,
                    "TSA_WD_Fortify_TerritoryLink".Translate(),
                    s.fortifyTerritoryLinkMaxTiles, 10f, 80f,
                    "TSA_WD_Fortify_TerritoryLinkTip".Translate(),
                    1f, SliderFormat.Fixed0, WorldDominationSettings.DefFortifyTerritoryLinkMaxTiles);

                s.fortifyTravelerStrength = SettingsUI.LabeledSlider(l,
                    "TSA_WD_Fortify_TravelerStrength".Translate(),
                    s.fortifyTravelerStrength, 10f, 200f,
                    "TSA_WD_Fortify_TravelerStrengthTip".Translate(),
                    5f, SliderFormat.Fixed0, WorldDominationSettings.DefFortifyTravelerStrength);

                SettingsUI.DrawCheckbox(l, "TSA_WD_Fortify_ClearOnBuilderLoss".Translate(),
                    ref s.fortifyClearOnBuilderLoss,
                    "TSA_WD_Fortify_ClearOnBuilderLossTip".Translate(),
                    defaultValue: WorldDominationSettings.DefFortifyClearOnBuilderLoss);

                SettingsUI.DrawCheckbox(l, "TSA_WD_Fortify_EnableBlacklist".Translate(),
                    ref s.enableFortifyBlacklist,
                    "TSA_WD_Fortify_EnableBlacklistTip".Translate(),
                    defaultValue: WorldDominationSettings.DefEnableFortifyBlacklist);
                if (s.enableFortifyBlacklist)
                {
                    SettingsUI.DrawCheckbox(l, "TSA_WD_Fortify_BlacklistApplyNeutral".Translate(),
                        ref s.fortifyBlacklistApplyToNeutral,
                        "TSA_WD_Fortify_BlacklistApplyNeutralTip".Translate(),
                        defaultValue: WorldDominationSettings.DefFortifyBlacklistApplyToNeutral);
                }

                l.Gap(8f);
                SettingsUI.DrawHeader(l, "TSA_WD_Fortify_HeaderTypeChances".Translate());
                s.fortifyChanceRoadBlock = SettingsUI.LabeledSlider(l,
                    "TSA_WD_Fortify_ChanceRoadBlock".Translate(),
                    s.fortifyChanceRoadBlock, 0f, 1f,
                    "TSA_WD_Fortify_ChanceRoadBlockTip".Translate(),
                    0.01f, SliderFormat.Percent, WorldDominationSettings.DefFortifyChanceRoadBlock);
                s.fortifyChanceTrap = SettingsUI.LabeledSlider(l,
                    "TSA_WD_Fortify_ChanceTrap".Translate(),
                    s.fortifyChanceTrap, 0f, 1f,
                    "TSA_WD_Fortify_ChanceTrapTip".Translate(),
                    0.01f, SliderFormat.Percent, WorldDominationSettings.DefFortifyChanceTrap);
                s.fortifyChanceTurret = SettingsUI.LabeledSlider(l,
                    "TSA_WD_Fortify_ChanceTurret".Translate(),
                    s.fortifyChanceTurret, 0f, 1f,
                    "TSA_WD_Fortify_ChanceTurretTip".Translate(),
                    0.01f, SliderFormat.Percent, WorldDominationSettings.DefFortifyChanceTurret);

                l.Gap(8f);
                SettingsUI.DrawHeader(l, "TSA_WD_Fortify_HeaderMultiCaravan".Translate());
                s.fortifyMultiT1ChanceOf2 = SettingsUI.LabeledSlider(l,
                    "TSA_WD_Fortify_MultiT1ChanceOf2".Translate(),
                    s.fortifyMultiT1ChanceOf2, 0f, 1f,
                    "TSA_WD_Fortify_MultiT1ChanceOf2Tip".Translate(),
                    0.01f, SliderFormat.Percent, WorldDominationSettings.DefFortifyMultiT1ChanceOf2);
                s.fortifyMultiT2ChanceOf2 = SettingsUI.LabeledSlider(l,
                    "TSA_WD_Fortify_MultiT2ChanceOf2".Translate(),
                    s.fortifyMultiT2ChanceOf2, 0f, 1f,
                    "TSA_WD_Fortify_MultiT2ChanceOf2Tip".Translate(),
                    0.01f, SliderFormat.Percent, WorldDominationSettings.DefFortifyMultiT2ChanceOf2);
                s.fortifyMultiT3ChanceOf2 = SettingsUI.LabeledSlider(l,
                    "TSA_WD_Fortify_MultiT3ChanceOf2".Translate(),
                    s.fortifyMultiT3ChanceOf2, 0f, 1f,
                    "TSA_WD_Fortify_MultiT3ChanceOf2Tip".Translate(),
                    0.01f, SliderFormat.Percent, WorldDominationSettings.DefFortifyMultiT3ChanceOf2);
                s.fortifyMultiT4ChanceOf3 = SettingsUI.LabeledSlider(l,
                    "TSA_WD_Fortify_MultiT4ChanceOf3".Translate(),
                    s.fortifyMultiT4ChanceOf3, 0f, 1f,
                    "TSA_WD_Fortify_MultiT4ChanceOf3Tip".Translate(),
                    0.01f, SliderFormat.Percent, WorldDominationSettings.DefFortifyMultiT4ChanceOf3);

                l.Gap(8f);
                SettingsUI.DrawHeader(l, "TSA_WD_Fortify_HeaderAtTurrets".Translate());

                float[] atCaps = { s.atTurretMaxT1, s.atTurretMaxT2, s.atTurretMaxT3, s.atTurretMaxT4 };
                SettingsUI.MultiColumnSlider(l,
                    new[]
                    {
                        "TSA_WD_Fortify_AtTurretMaxT1".Translate().ToString(),
                        "TSA_WD_Fortify_AtTurretMaxT2".Translate().ToString(),
                        "TSA_WD_Fortify_AtTurretMaxT3".Translate().ToString(),
                        "TSA_WD_Fortify_AtTurretMaxT4".Translate().ToString()
                    },
                    atCaps, new Vector2(0f, 8f),
                    new[]
                    {
                        "TSA_WD_Fortify_AtTurretMaxTip".Translate().ToString(),
                        "TSA_WD_Fortify_AtTurretMaxTip".Translate().ToString(),
                        "TSA_WD_Fortify_AtTurretMaxTip".Translate().ToString(),
                        "TSA_WD_Fortify_AtTurretMaxTip".Translate().ToString()
                    },
                    1f, SliderFormat.Fixed0, 38f,
                    new float[]
                    {
                        WorldDominationSettings.DefAtTurretMaxT1,
                        WorldDominationSettings.DefAtTurretMaxT2,
                        WorldDominationSettings.DefAtTurretMaxT3,
                        WorldDominationSettings.DefAtTurretMaxT4
                    });
                s.atTurretMaxT1 = (int)atCaps[0];
                s.atTurretMaxT2 = (int)atCaps[1];
                s.atTurretMaxT3 = (int)atCaps[2];
                s.atTurretMaxT4 = (int)atCaps[3];
            }

            // 4. ACTION CAPS + COOLDOWNS - advanced only
            if (advanced)
            {
                l.Gap(12f);
                if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Daily_HeaderActionCaps".Translate(), ref capsExpanded, SettingsUI.SectionHeaderColor))
                {

                float[] capVals = { s.tier1MaxActions, s.tier2MaxActions, s.tier3MaxActions, s.tier4MaxActions };
                string[] capLabels = {
                    "TSA_WD_CapT1".Translate().ToString(),
                    "TSA_WD_CapT2".Translate().ToString(),
                    "TSA_WD_CapT3".Translate().ToString(),
                    "TSA_WD_CapT4".Translate().ToString()
                };
                string[] capTips = {
                    "TSA_WD_Daily_CapTip".Translate().ToString(),
                    "TSA_WD_Daily_CapTip".Translate().ToString(),
                    "TSA_WD_Daily_CapTip".Translate().ToString(),
                    "TSA_WD_Daily_CapTip".Translate().ToString()
                };

                SettingsUI.MultiColumnSlider(l, capLabels, capVals, new Vector2(1f, 5f), capTips, 1f, SliderFormat.Fixed0, 38f,
                    new float[] { WorldDominationSettings.DefCapT1, WorldDominationSettings.DefCapT2, WorldDominationSettings.DefCapT3, WorldDominationSettings.DefCapT4 });

                s.tier1MaxActions = (int)capVals[0];
                s.tier2MaxActions = (int)capVals[1];
                s.tier3MaxActions = (int)capVals[2];
                s.tier4MaxActions = (int)capVals[3];
                }
                l.Gap(12f);
                if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Daily_HeaderCooldowns".Translate(), ref cooldownsExpanded, SettingsUI.SectionHeaderColor))
                {

                float[] cdRow1 = { s.cooldownGrowDays, s.cooldownExpandDays };
                SettingsUI.MultiColumnSlider(l,
                    new[] { "TSA_WD_Daily_CdGrow".Translate().ToString(), "TSA_WD_Daily_CdExpand".Translate().ToString() },
                    cdRow1, new Vector2(0f, 15f),
                    new[] { "TSA_WD_Daily_CdGrowTip".Translate().ToString(), "TSA_WD_Daily_CdExpandTip".Translate().ToString() },
                    0.1f, SliderFormat.Fixed1, 38f,
                    new[] { WorldDominationSettings.DefCdGrowDays, WorldDominationSettings.DefCdExpandDays });

                s.cooldownGrowDays = cdRow1[0];
                s.cooldownExpandDays = cdRow1[1];

                float[] cdRow2 = { s.cooldownRaidDays, s.cooldownBeingRaidedDays };
                SettingsUI.MultiColumnSlider(l,
                    new[] { "TSA_WD_Daily_CdRaid".Translate().ToString(), "TSA_WD_Daily_CdBeingRaided".Translate().ToString() },
                    cdRow2, new Vector2(0f, 10f),
                    new[] { "TSA_WD_Daily_CdRaidTip".Translate().ToString(), "TSA_WD_Daily_CdBeingRaidedTip".Translate().ToString() },
                    0.1f, SliderFormat.Fixed1, 38f,
                    new[] { WorldDominationSettings.DefCdRaidDays, WorldDominationSettings.DefCdBeingRaidedDays });

                s.cooldownRaidDays = cdRow2[0];
                s.cooldownBeingRaidedDays = cdRow2[1];

                l.Gap(2f);
                float[] cdRow3 = { s.cooldownIncidentDays, s.cooldownTraderDays };
                SettingsUI.MultiColumnSlider(l,
                    new[] { "TSA_WD_Daily_CdIncident".Translate().ToString(), "TSA_WD_Daily_CdTrader".Translate().ToString() },
                    cdRow3, new Vector2(0f, 10f),
                    new[] { "TSA_WD_Daily_CdIncidentTip".Translate().ToString(), "TSA_WD_Daily_CdTraderTip".Translate().ToString() },
                    0.1f, SliderFormat.Fixed1, 38f,
                    new[] { WorldDominationSettings.DefCdIncidentDays, WorldDominationSettings.DefCdTraderDays });

                s.cooldownIncidentDays = cdRow3[0];
                s.cooldownTraderDays = cdRow3[1];

                l.Gap(2f);
                float[] cdRow4 = { s.cooldownFortifyDays, 0f };
                SettingsUI.MultiColumnSlider(l,
                    new[] { "TSA_WD_Daily_CdFortify".Translate().ToString(), "" },
                    cdRow4, new Vector2(0f, 15f),
                    new[] { "TSA_WD_Daily_CdFortifyTip".Translate().ToString(), "" },
                    0.1f, SliderFormat.Fixed1, 38f,
                    new[] { WorldDominationSettings.DefCdFortifyDays, 0f });
                s.cooldownFortifyDays = cdRow4[0];
                }
            }

            l.End();
            Widgets.EndScrollView();
        }
    }
}
