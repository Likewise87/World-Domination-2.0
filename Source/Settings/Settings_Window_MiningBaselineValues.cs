using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Mining baseline quantities: vanilla stone blocks + surface ores from MiningScatterDiscovery. Layout matches Dialog_FoodSettings (Outposts): fixed size, scroll, reset at bottom.</summary>
    public class Dialog_MiningBaselineSettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;
        private bool baselinesExpanded = true;

        public override Vector2 InitialSize => new Vector2(850f, 750f);

        public Dialog_MiningBaselineSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnMiningBaselines".Translate();
            optionalTitle = null;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            float contentWidth = contentRect.width - 24f;
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 1600f);

            Widgets.BeginScrollView(contentRect, ref scrollPosition, scrollViewRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(scrollViewRect);
            var s = WorldDominationMod.settings;

            string tooltip = "TSA_WD_MiningBaseline_Tooltip".Translate();
            if (tooltip.Contains("TSA_WD_")) tooltip = "Baseline quantity per Mining skill per cycle for this resource. Example: 10 baseline × 5 Mining Skill = 50 produced per cycle (before terrain efficiency).";

            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetMiningBaselines(),
                () => { baselinesExpanded = true; },
                () => { baselinesExpanded = false; });
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_MiningBaseline_Header".Translate(), ref baselinesExpanded, SettingsUI.SectionHeaderColor))
            {
            l.Label(tooltip);
            l.Gap(SettingsUI.StandardGap);

            // --- STONE BLOCKS ---
            SettingsUI.DrawHeader(l, "TSA_WD_MiningBaseline_Stones".Translate());
            foreach (string defName in Outpost_Baselines.VanillaStoneDefNames)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                string label = def?.LabelCap ?? defName;
                float defaultValue = WorldDominationSettings.GetDefaultMiningBaselineForDef(defName);
                float current = (s.miningBaselineMultiplierByDefName != null && s.miningBaselineMultiplierByDefName.TryGetValue(defName, out float v)) ? v : defaultValue;
                float newVal = SettingsUI.LabeledSlider(l, label, current, 1f, 100f, tooltip, 1f, SliderFormat.Fixed0, defaultValue);
                if (s.miningBaselineMultiplierByDefName == null) s.miningBaselineMultiplierByDefName = new Dictionary<string, float>();
                s.miningBaselineMultiplierByDefName[defName] = Mathf.RoundToInt(newVal);
            }
            l.Gap(SettingsUI.StandardGap);

            // --- ORES (scatter mineables + vanilla union, same set as mining production) ---
            SettingsUI.DrawHeader(l, "TSA_WD_MiningBaseline_Ores".Translate());
            foreach (ThingDef ore in MiningScatterDiscovery.GetEffectiveScatterOresOrdered())
            {
                if (ore?.defName == null) continue;
                string defName = ore.defName;
                string label = ore.LabelCap;
                float defaultValue = WorldDominationSettings.GetDefaultMiningBaselineForDef(defName);
                float current = (s.miningBaselineMultiplierByDefName != null && s.miningBaselineMultiplierByDefName.TryGetValue(defName, out float v)) ? v : defaultValue;
                float newVal = SettingsUI.LabeledSlider(l, label, current, 0.1f, 100f, tooltip, 0.1f, SliderFormat.Fixed1, defaultValue);
                if (s.miningBaselineMultiplierByDefName == null) s.miningBaselineMultiplierByDefName = new Dictionary<string, float>();
                s.miningBaselineMultiplierByDefName[defName] = newVal;
            }
            }

            l.End();
            Widgets.EndScrollView();
        }
    }
}
