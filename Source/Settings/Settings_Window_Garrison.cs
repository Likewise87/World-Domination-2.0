using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_GarrisonSettings : Window
    {
        private readonly string windowTitle;
        private bool tribalExpanded = true;
        private bool genericExpanded = true;
        private bool scalingExpanded = true;

        public override Vector2 InitialSize => new Vector2(850f, 750f);

        public Dialog_GarrisonSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnGarrisonSettings".Translate();
            optionalTitle = null;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            Listing_Standard l = new Listing_Standard();
            l.Begin(contentRect);
            var s = WorldDominationMod.settings;

            Text.Font = GameFont.Medium;
            l.Label("TSA_WD_Garrison_Title".Translate());
            Text.Font = GameFont.Small;
            l.Gap(10f);

            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetGarrisons(),
                () => { tribalExpanded = genericExpanded = scalingExpanded = true; },
                () => { tribalExpanded = genericExpanded = scalingExpanded = false; });
            l.CheckboxLabeled("TSA_WD_Garrison_AllowBaseGeneration".Translate(), ref s.allowWdSettlementBaseGeneration,
                SettingsUI.TooltipWithDefault("TSA_WD_Garrison_AllowBaseGenerationTooltip".Translate(), WorldDominationSettings.DefAllowWdSettlementBaseGeneration));
            l.Gap(12f);

            // --- TRIBAL SECTION ---
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_HeaderTribalGarrisons".Translate(), ref tribalExpanded, SettingsUI.SectionHeaderColor))
            {

            s.kcsgMultTribalT1 = SettingsUI.LabeledSlider(l, "TSA_WD_Garrison_T1".Translate(), s.kcsgMultTribalT1, 0.1f, 10f,
                "TSA_WD_Garrison_Tooltip".Translate(), 0.1f, SliderFormat.Multiplier, WorldDominationSettings.DefKcsgMultTribalT1);

            s.kcsgMultTribalT2 = SettingsUI.LabeledSlider(l, "TSA_WD_Garrison_T2".Translate(), s.kcsgMultTribalT2, 0.1f, 10f,
                "TSA_WD_Garrison_Tooltip".Translate(), 0.1f, SliderFormat.Multiplier, WorldDominationSettings.DefKcsgMultTribalT2);

            s.kcsgMultTribalT3 = SettingsUI.LabeledSlider(l, "TSA_WD_Garrison_T3".Translate(), s.kcsgMultTribalT3, 0.1f, 10f,
                "TSA_WD_Garrison_Tooltip".Translate(), 0.1f, SliderFormat.Multiplier, WorldDominationSettings.DefKcsgMultTribalT3);

            s.kcsgMultTribalT4 = SettingsUI.LabeledSlider(l, "TSA_WD_Garrison_T4".Translate(), s.kcsgMultTribalT4, 0.1f, 10f,
                "TSA_WD_Garrison_Tooltip".Translate(), 0.1f, SliderFormat.Multiplier, WorldDominationSettings.DefKcsgMultTribalT4);
            }

            l.Gap(12f);

            // --- GENERIC SECTION ---
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_HeaderGenericGarrisons".Translate(), ref genericExpanded, SettingsUI.SectionHeaderColor))
            {

            s.kcsgMultGenericT1 = SettingsUI.LabeledSlider(l, "TSA_WD_Garrison_T1".Translate(), s.kcsgMultGenericT1, 0.1f, 10f,
                "TSA_WD_Garrison_Tooltip".Translate(), 0.1f, SliderFormat.Multiplier, WorldDominationSettings.DefKcsgMultGenericT1);

            s.kcsgMultGenericT2 = SettingsUI.LabeledSlider(l, "TSA_WD_Garrison_T2".Translate(), s.kcsgMultGenericT2, 0.1f, 10f,
                "TSA_WD_Garrison_Tooltip".Translate(), 0.1f, SliderFormat.Multiplier, WorldDominationSettings.DefKcsgMultGenericT2);

            s.kcsgMultGenericT3 = SettingsUI.LabeledSlider(l, "TSA_WD_Garrison_T3".Translate(), s.kcsgMultGenericT3, 0.1f, 10f,
                "TSA_WD_Garrison_Tooltip".Translate(), 0.1f, SliderFormat.Multiplier, WorldDominationSettings.DefKcsgMultGenericT3);

            s.kcsgMultGenericT4 = SettingsUI.LabeledSlider(l, "TSA_WD_Garrison_T4".Translate(), s.kcsgMultGenericT4, 0.1f, 10f,
                "TSA_WD_Garrison_Tooltip".Translate(), 0.1f, SliderFormat.Multiplier, WorldDominationSettings.DefKcsgMultGenericT4);
            }
            l.Gap(12f);

            // --- DYNAMIC SCALING (by current offensive strength) ---
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_HeaderDynamicGarrison".Translate(), ref scalingExpanded, SettingsUI.SectionHeaderColor))
            {

            s.garrisonOffensiveStrengthMinScale = SettingsUI.LabeledSlider(l, "TSA_WD_Garrison_MinScale".Translate(), s.garrisonOffensiveStrengthMinScale, 0f, 1f,
                "TSA_WD_Garrison_MinScaleTooltip".Translate(), 0.05f, SliderFormat.PercentDecimal, WorldDominationSettings.DefGarrisonOffensiveStrengthMinScale);
            }

            l.End();
        }
    }
}
