using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Growth and expansion settings. Scrollable (fixed height like Dialog_FoodSettings).</summary>
    public class Dialog_GrowthSettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;
        private bool growthExpanded = true;
        private bool expansionExpanded;
        private bool defensiveExpanded;
        private bool incidentsExpanded;

        public override Vector2 InitialSize => new Vector2(850f, 750f);

        public Dialog_GrowthSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnGrowthExpand".Translate();
            optionalTitle = null;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            float contentWidth = contentRect.width - 24f;
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 2400f);

            Widgets.BeginScrollView(contentRect, ref scrollPosition, scrollViewRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(scrollViewRect);
            var s = WorldDominationMod.settings;
            bool advanced = s.showAdvancedSettings;
            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetGrowth(),
                () => { growthExpanded = expansionExpanded = defensiveExpanded = incidentsExpanded = true; },
                () => { growthExpanded = expansionExpanded = defensiveExpanded = incidentsExpanded = false; });

            // ================= SECTION 1: GROWTH (core) =================
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Growth_HeaderGrowth".Translate(), ref growthExpanded, SettingsUI.SectionHeaderColor))
            {

            s.maxSettlements = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Growth_MaxSettlements".Translate(), (float)s.maxSettlements, 30f, 1250f,
                "TSA_WD_Growth_MaxSettlementsTooltip".Translate(), 1.0f, SliderFormat.Fixed0, WorldDominationSettings.DefMaxSettlements);

            float[] growthVals = { s.passiveGrowthT1, s.passiveGrowthT2, s.passiveGrowthT3, s.passiveGrowthT4 };
            SettingsUI.MultiColumnSlider(l,
                new[] {
                    "TSA_WD_Growth_PassiveT1".Translate().ToString(),
                    "TSA_WD_Growth_PassiveT2".Translate().ToString(),
                    "TSA_WD_Growth_PassiveT3".Translate().ToString(),
                    "TSA_WD_Growth_PassiveT4".Translate().ToString()
                },
                growthVals, new Vector2(0f, 300f),
                new[] {
                    "TSA_WD_Growth_PassiveTierTip".Translate().ToString(),
                    "TSA_WD_Growth_PassiveTierTip".Translate().ToString(),
                    "TSA_WD_Growth_PassiveTierTip".Translate().ToString(),
                    "TSA_WD_Growth_PassiveTierTip".Translate().ToString()
                },
                5f, SliderFormat.Fixed0, 38f,
                new[] {
                    WorldDominationSettings.DefPassiveGrowthT1,
                    WorldDominationSettings.DefPassiveGrowthT2,
                    WorldDominationSettings.DefPassiveGrowthT3,
                    WorldDominationSettings.DefPassiveGrowthT4
                });
            s.passiveGrowthT1 = growthVals[0];
            s.passiveGrowthT2 = growthVals[1];
            s.passiveGrowthT3 = growthVals[2];
            s.passiveGrowthT4 = growthVals[3];
            }
            // ================= SECTION: EXPANSION & LOCAL DENSITY =================
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Growth_HeaderExpansion".Translate(), ref expansionExpanded, SettingsUI.SectionHeaderColor))
            {

            s.expandMinRadius = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Growth_MinRadius".Translate(), (float)s.expandMinRadius, 2f, 20f,
                "TSA_WD_Growth_MinRadiusTooltip".Translate(), 1.0f, SliderFormat.Fixed0, WorldDominationSettings.DefExpandMinRad);

            s.expandMaxRadius = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Growth_MaxRadius".Translate(), (float)s.expandMaxRadius, 5f, 80f,
                "TSA_WD_Growth_MaxRadiusTooltip".Translate(), 1.0f, SliderFormat.Fixed0, WorldDominationSettings.DefExpandMaxRad);

            if (s.expandMaxRadius < s.expandMinRadius) s.expandMaxRadius = s.expandMinRadius;

            s.localMaxT1 = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Growth_LocalMaxT1".Translate(), (float)s.localMaxT1, 1f, 25f,
                "TSA_WD_Growth_LocalMaxT1Tooltip".Translate(), 1.0f, SliderFormat.Fixed0, WorldDominationSettings.DefLocalMaxT1);

            s.localMaxT2 = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Growth_LocalMaxT2".Translate(), (float)s.localMaxT2, 1f, 25f,
                "TSA_WD_Growth_LocalMaxT2Tooltip".Translate(), 1.0f, SliderFormat.Fixed0, WorldDominationSettings.DefLocalMaxT2);

            s.localMaxT3 = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Growth_LocalMaxT3".Translate(), (float)s.localMaxT3, 1f, 25f,
                "TSA_WD_Growth_LocalMaxT3Tooltip".Translate(), 1.0f, SliderFormat.Fixed0, WorldDominationSettings.DefLocalMaxT3);

            s.localMaxT4 = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Growth_LocalMaxT4".Translate(), (float)s.localMaxT4, 1f, 25f,
                "TSA_WD_Growth_LocalMaxT4Tooltip".Translate(), 1.0f, SliderFormat.Fixed0, WorldDominationSettings.DefLocalMaxT4);

            s.sameTierNeighborsToUpgradeT1 = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Growth_SameTierUpgradeT1".Translate(), (float)s.sameTierNeighborsToUpgradeT1, 0f, 5f,
                "TSA_WD_Growth_SameTierUpgradeT1Tooltip".Translate(), 1.0f, SliderFormat.Fixed0, WorldDominationSettings.DefSameTierNeighborsToUpgradeT1);
            s.sameTierNeighborsToUpgradeT2 = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Growth_SameTierUpgradeT2".Translate(), (float)s.sameTierNeighborsToUpgradeT2, 0f, 5f,
                "TSA_WD_Growth_SameTierUpgradeT2Tooltip".Translate(), 1.0f, SliderFormat.Fixed0, WorldDominationSettings.DefSameTierNeighborsToUpgradeT2);
            s.sameTierNeighborsToUpgradeT3 = (int)SettingsUI.LabeledSlider(l, "TSA_WD_Growth_SameTierUpgradeT3".Translate(), (float)s.sameTierNeighborsToUpgradeT3, 0f, 5f,
                "TSA_WD_Growth_SameTierUpgradeT3Tooltip".Translate(), 1.0f, SliderFormat.Fixed0, WorldDominationSettings.DefSameTierNeighborsToUpgradeT3);
            }
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Growth_HeaderDefensiveBaselines".Translate(), ref defensiveExpanded, SettingsUI.SectionHeaderColor))
            {
            s.tier1BaseDefensiveStrength = SettingsUI.LabeledSlider(l, "TSA_WD_Growth_T1DefensiveStrength".Translate(), s.tier1BaseDefensiveStrength, 0f, 1000f,
                "TSA_WD_Growth_T1DefensiveStrengthTooltip".Translate(), 10f, SliderFormat.Fixed0, WorldDominationSettings.DefTier1BaseDefensiveStrength);
            s.tier2BaseDefensiveStrength = SettingsUI.LabeledSlider(l, "TSA_WD_Growth_T2DefensiveStrength".Translate(), s.tier2BaseDefensiveStrength, 0f, 1500f,
                "TSA_WD_Growth_T2DefensiveStrengthTooltip".Translate(), 10f, SliderFormat.Fixed0, WorldDominationSettings.DefTier2BaseDefensiveStrength);
            s.tier3BaseDefensiveStrength = SettingsUI.LabeledSlider(l, "TSA_WD_Growth_T3DefensiveStrength".Translate(), s.tier3BaseDefensiveStrength, 0f, 2000f,
                "TSA_WD_Growth_T3DefensiveStrengthTooltip".Translate(), 10f, SliderFormat.Fixed0, WorldDominationSettings.DefTier3BaseDefensiveStrength);
            s.tier4BaseDefensiveStrength = SettingsUI.LabeledSlider(l, "TSA_WD_Growth_T4DefensiveStrength".Translate(), s.tier4BaseDefensiveStrength, 0f, 3000f,
                "TSA_WD_Growth_T4DefensiveStrengthTooltip".Translate(), 10f, SliderFormat.Fixed0, WorldDominationSettings.DefTier4BaseDefensiveStrength);
            }

            if (advanced)
            {
            // ================= SECTION: INCIDENTS (advanced) =================
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Growth_HeaderIncidents".Translate(), ref incidentsExpanded, SettingsUI.SectionHeaderColor))
            {

            s.minorIncidentSeverity = SettingsUI.LabeledSlider(l, "TSA_WD_Growth_MinorInc".Translate(), s.minorIncidentSeverity, 10f, 500f,
                "TSA_WD_Growth_MinorIncTooltip".Translate(), 5.0f, SliderFormat.Fixed0, WorldDominationSettings.DefMinorIncSev);

            s.majorIncidentSeverity = SettingsUI.LabeledSlider(l, "TSA_WD_Growth_MajorInc".Translate(), s.majorIncidentSeverity, 100f, 1500f,
                "TSA_WD_Growth_MajorIncTooltip".Translate(), 10.0f, SliderFormat.Fixed0, WorldDominationSettings.DefMajorIncSev);
            }
            }

            l.End();
            Widgets.EndScrollView();
        }
    }
}
