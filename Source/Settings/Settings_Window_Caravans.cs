using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_CaravansSettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;
        private bool travelExpanded = true;
        private bool waterExpanded;
        private bool traderExpanded;
        private bool goodwillExpanded;
        private bool deliveryExpanded;
        private bool storytellerExpanded;

        public override Vector2 InitialSize => new Vector2(850f, 750f);

        public Dialog_CaravansSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnCaravans".Translate();
            optionalTitle = null;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            float contentWidth = contentRect.width - 24f;
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 2100f);

            Widgets.BeginScrollView(contentRect, ref scrollPosition, scrollViewRect);
            Listing_Standard l = new Listing_Standard();
            l.Begin(scrollViewRect);

            var s = WorldDominationMod.settings;
            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetCaravans(),
                () => { travelExpanded = waterExpanded = traderExpanded = goodwillExpanded = deliveryExpanded = storytellerExpanded = true; },
                () => { travelExpanded = waterExpanded = traderExpanded = goodwillExpanded = deliveryExpanded = storytellerExpanded = false; });

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Caravans_HeaderTravel".Translate(), ref travelExpanded, SettingsUI.SectionHeaderColor))
            {
            s.strengthLossPerHour = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_LossPerHour".Translate(), s.strengthLossPerHour, 0f, 0.25f,
                "TSA_WD_Raid_LossPerHourTooltip".Translate(), 0.005f, SliderFormat.PercentDecimal, WorldDominationSettings.DefStrengthLossPerHour);
            s.maxTravelPercentageStrengthLoss = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_MaxTravelStrengthLoss".Translate(), s.maxTravelPercentageStrengthLoss, 0f, 1f,
                "TSA_WD_Outpost_MaxTravelStrengthLossTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefMaxTravelPercentageStrengthLoss);
            }
            l.Gap(12f);
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Caravans_HeaderWaterTravel".Translate(), ref waterExpanded, SettingsUI.SectionHeaderColor))
            {
            SettingsUI.DrawCheckbox(l, "TSA_WD_Caravans_AllowTravelOverWater".Translate(), ref s.allowCaravansTravelOverWater,
                "TSA_WD_Caravans_AllowTravelOverWaterTip".Translate(), defaultValue: WorldDominationSettings.DefAllowCaravansTravelOverWater);
            if (s.allowCaravansTravelOverWater)
            {
                l.Gap(6f);
                SettingsUI.DrawCheckbox(l, "TSA_WD_Caravans_OnlyWaterIfNoLandPath".Translate(), ref s.onlyTravelAcrossWaterIfNoOtherWay,
                    "TSA_WD_Caravans_OnlyWaterIfNoLandPathTip".Translate(), defaultValue: WorldDominationSettings.DefOnlyTravelAcrossWaterIfNoOtherWay);
                s.travelerWaterMovementDifficulty = SettingsUI.LabeledSlider(l, "TSA_WD_Caravans_WaterMovementDifficulty".Translate(), s.travelerWaterMovementDifficulty, 0.5f, 12f,
                    "TSA_WD_Caravans_WaterMovementDifficultyTip".Translate(), 0.25f, SliderFormat.Fixed2, WorldDominationSettings.DefTravelerWaterMovementDifficulty);
                s.waterPathLandThresholdDays = SettingsUI.LabeledSlider(l, "TSA_WD_Caravans_WaterPathThreshold".Translate(), s.waterPathLandThresholdDays, 0f, 10f,
                    "TSA_WD_Caravans_WaterPathThresholdTip".Translate(), 0.25f, SliderFormat.Fixed1, WorldDominationSettings.DefWaterPathLandThresholdDays);
            }
            }
            l.Gap(12f);
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Caravans_HeaderTrader".Translate(), ref traderExpanded, SettingsUI.SectionHeaderColor))
            {
            s.traderCaravanCostStrength = SettingsUI.LabeledSlider(l, "TSA_WD_Caravans_Cost".Translate(), s.traderCaravanCostStrength, 10f, 500f,
                "TSA_WD_Caravans_CostTip".Translate(), 5f, SliderFormat.Fixed0, WorldDominationSettings.DefTraderCaravanCostStrength);
            s.traderCaravanSenderRewardStrength = SettingsUI.LabeledSlider(l, "TSA_WD_Caravans_SenderReward".Translate(), s.traderCaravanSenderRewardStrength, 20f, 800f,
                "TSA_WD_Caravans_SenderRewardTip".Translate(), 5f, SliderFormat.Fixed0, WorldDominationSettings.DefTraderCaravanSenderRewardStrength);
            s.traderCaravanReceiverRewardStrength = SettingsUI.LabeledSlider(l, "TSA_WD_Caravans_ReceiverReward".Translate(), s.traderCaravanReceiverRewardStrength, 20f, 800f,
                "TSA_WD_Caravans_ReceiverRewardTip".Translate(), 5f, SliderFormat.Fixed0, WorldDominationSettings.DefTraderCaravanReceiverRewardStrength);
            s.traderCaravanGoodwillGain = SettingsUI.LabeledSlider(l, "TSA_WD_Caravans_Goodwill".Translate(), s.traderCaravanGoodwillGain, 0f, 50f,
                "TSA_WD_Caravans_GoodwillTip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefTraderCaravanGoodwillGain);
            s.cooldownPlayerColonyTraderDays = SettingsUI.LabeledSlider(l, "TSA_WD_Caravans_PlayerColonyTraderCooldown".Translate(), s.cooldownPlayerColonyTraderDays, 0f, 60f,
                "TSA_WD_Caravans_PlayerColonyTraderCooldownTip".Translate(), 0.25f, SliderFormat.Fixed2, WorldDominationSettings.DefCooldownPlayerColonyTraderDays);
            s.traderDestinationSearchRadius = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_TraderDestSearchRadius".Translate(), s.traderDestinationSearchRadius, 5f, 200f,
                "TSA_WD_Outpost_TraderDestSearchRadiusTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefTraderDestinationSearchRadius);

            l.Gap(10f);
            SettingsUI.DrawHeader(l, "TSA_WD_Caravans_HeaderTraderTierUps".Translate());
            s.traderTierUpgradeChanceT1ToT2 = SettingsUI.LabeledSlider(l, "TSA_WD_Caravans_TierUpT1ToT2".Translate(), s.traderTierUpgradeChanceT1ToT2, 0f, 1f,
                "TSA_WD_Caravans_TierUpT1ToT2Tip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefTraderTierUpgradeChanceT1ToT2);
            s.traderTierUpgradeChanceT2ToT3 = SettingsUI.LabeledSlider(l, "TSA_WD_Caravans_TierUpT2ToT3".Translate(), s.traderTierUpgradeChanceT2ToT3, 0f, 1f,
                "TSA_WD_Caravans_TierUpT2ToT3Tip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefTraderTierUpgradeChanceT2ToT3);
            s.traderTierUpgradeChanceT3ToT4 = SettingsUI.LabeledSlider(l, "TSA_WD_Caravans_TierUpT3ToT4".Translate(), s.traderTierUpgradeChanceT3ToT4, 0f, 1f,
                "TSA_WD_Caravans_TierUpT3ToT4Tip".Translate(), 0.01f, SliderFormat.Percent, WorldDominationSettings.DefTraderTierUpgradeChanceT3ToT4);

            l.Gap(10f);
            SettingsUI.DrawHeader(l, "TSA_WD_Caravans_HeaderTraderEscort".Translate());
            s.traderEscortFloorT1 = SettingsUI.LabeledSlider(l, "TSA_WD_Caravans_EscortFloorT1".Translate(), s.traderEscortFloorT1, 10f, 500f,
                "TSA_WD_Caravans_EscortFloorT1Tip".Translate(), 5f, SliderFormat.Fixed0, WorldDominationSettings.DefTraderEscortFloorT1);
            s.traderEscortFloorT2 = SettingsUI.LabeledSlider(l, "TSA_WD_Caravans_EscortFloorT2".Translate(), s.traderEscortFloorT2, 10f, 1000f,
                "TSA_WD_Caravans_EscortFloorT2Tip".Translate(), 5f, SliderFormat.Fixed0, WorldDominationSettings.DefTraderEscortFloorT2);
            s.traderEscortFloorT3 = SettingsUI.LabeledSlider(l, "TSA_WD_Caravans_EscortFloorT3".Translate(), s.traderEscortFloorT3, 10f, 1600f,
                "TSA_WD_Caravans_EscortFloorT3Tip".Translate(), 5f, SliderFormat.Fixed0, WorldDominationSettings.DefTraderEscortFloorT3);
            s.traderEscortFloorT4 = SettingsUI.LabeledSlider(l, "TSA_WD_Caravans_EscortFloorT4".Translate(), s.traderEscortFloorT4, 10f, 2250f,
                "TSA_WD_Caravans_EscortFloorT4Tip".Translate(), 5f, SliderFormat.Fixed0, WorldDominationSettings.DefTraderEscortFloorT4);
            s.traderEscortRecentInterceptWindowDays = SettingsUI.LabeledSlider(l, "TSA_WD_Caravans_EscortRecentInterceptWindow".Translate(), s.traderEscortRecentInterceptWindowDays, 0f, 30f,
                "TSA_WD_Caravans_EscortRecentInterceptWindowTip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefTraderEscortRecentInterceptWindowDays);
            }
            l.Gap(12f);
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Growth_HeaderGoodwillFromTrade".Translate(), ref goodwillExpanded, SettingsUI.SectionHeaderColor))
            {
            l.CheckboxLabeled(
                "TSA_WD_Growth_GoodwillFromTrade".Translate(),
                ref s.goodwillFromTradeEnabled,
                SettingsUI.TooltipWithDefault("TSA_WD_Growth_GoodwillFromTradeTooltip".Translate(), WorldDominationSettings.DefGoodwillFromTradeEnabled)
            );
            s.goodwillFromTradePer1000Silver = SettingsUI.LabeledSlider(l, "TSA_WD_Growth_GoodwillPer1000Silver".Translate(), s.goodwillFromTradePer1000Silver, 0.1f, 10f,
                "TSA_WD_Growth_GoodwillPer1000SilverTooltip".Translate(), 0.1f, SliderFormat.Fixed2, WorldDominationSettings.DefGoodwillFromTradePer1000Silver);
            }
            l.Gap(12f);
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Caravans_HeaderOutpostDelivery".Translate(), ref deliveryExpanded, SettingsUI.SectionHeaderColor))
            {
            s.outpostDeliveryStrengthCost = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_DeliveryStrengthCost".Translate(), s.outpostDeliveryStrengthCost, 10f, 200f,
                "TSA_WD_Outpost_DeliveryStrengthCostTooltip".Translate(), 5f, SliderFormat.Fixed0, WorldDominationSettings.DefOutpostDeliveryStrengthCost);
            s.outpostDeliveryMinStrength = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_DeliveryMinStrength".Translate(), s.outpostDeliveryMinStrength, 50f, 300f,
                "TSA_WD_Outpost_DeliveryMinStrengthTooltip".Translate(), 10f, SliderFormat.Fixed0, WorldDominationSettings.DefOutpostDeliveryMinStrength);
            }
            l.Gap(12f);
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Caravans_HeaderStoryteller".Translate(), ref storytellerExpanded, SettingsUI.SectionHeaderColor))
            {
            l.CheckboxLabeled("TSA_WD_Caravans_BlockStorytellerTraders".Translate(), ref s.blockStorytellerTradersOnlyWD,
                SettingsUI.TooltipWithDefault("TSA_WD_Caravans_BlockStorytellerTradersTip".Translate(), WorldDominationSettings.DefBlockStorytellerTradersOnlyWD));
            }

            l.End();
            Widgets.EndScrollView();
        }
    }
}
