using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_PollutionSettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;

        public override Vector2 InitialSize => new Vector2(850f, 700f);

        public Dialog_PollutionSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnPollution".Translate();
            optionalTitle = null;
        }

        private static Dialog_PollutionSettings embeddedHost;

        public static void DrawEmbedded(Rect inRect)
        {
            if (embeddedHost == null) embeddedHost = new Dialog_PollutionSettings();
            embeddedHost.DrawBody(inRect);
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            DrawBody(contentRect);
        }

        private void DrawBody(Rect contentRect)
        {
            if (WdSettingsSearch.IsCollecting)
            {
                Listing_Standard l = new Listing_Standard();
                l.Begin(contentRect);
                DrawListing(l);
                l.End();
                return;
            }

            float contentWidth = SettingsUI.ScrollContentWidth(contentRect.width);
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 1600f);
            SettingsUI.ApplyPendingHighlightScroll(ref scrollPosition, contentRect.height, scrollViewRect.height);
            Widgets.BeginScrollView(contentRect, ref scrollPosition, scrollViewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(scrollViewRect);
            DrawListing(listing);
            listing.End();
            Widgets.EndScrollView();
        }

        private void DrawListing(Listing_Standard l)
        {
            var s = WorldDominationMod.settings;
            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetPollution(),
                () => { }, () => { });

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
    }
}
