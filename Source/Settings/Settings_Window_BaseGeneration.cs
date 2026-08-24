using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_BaseGenerationSettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;
        private bool terrainPrepExpanded = true;
        private bool rockAndSoilExpanded = true;
        private bool layoutExtrasExpanded = true;
        private bool afterGenerationExpanded = true;

        public override Vector2 InitialSize => new Vector2(850f, 700f);

        public Dialog_BaseGenerationSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnBaseGeneration".Translate();
            optionalTitle = null;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            float contentWidth = contentRect.width - 24f;
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 1200f);

            Widgets.BeginScrollView(contentRect, ref scrollPosition, scrollViewRect);
            Listing_Standard l = new Listing_Standard();
            l.Begin(scrollViewRect);

            var s = WorldDominationMod.settings;
            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetBaseGeneration(),
                () =>
                {
                    terrainPrepExpanded = rockAndSoilExpanded = layoutExtrasExpanded = afterGenerationExpanded = true;
                },
                () =>
                {
                    terrainPrepExpanded = rockAndSoilExpanded = layoutExtrasExpanded = afterGenerationExpanded = false;
                });

            l.Label("TSA_WD_BaseGen_Intro".Translate());
            l.Gap(12f);

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_BaseGen_HeaderTerrainPrep".Translate(), ref terrainPrepExpanded, SettingsUI.SectionHeaderColor))
            {
                SettingsUI.DrawCheckbox(l, "TSA_WD_BaseGen_AdaptiveTerrainPrep".Translate(),
                    ref s.kcsgAdaptiveTerrainPrep,
                    "TSA_WD_BaseGen_AdaptiveTerrainPrepTip".Translate(),
                    defaultValue: WorldDominationSettings.DefKcsgAdaptiveTerrainPrep);
                if (s.kcsgAdaptiveTerrainPrep)
                {
                    SettingsUI.DrawCheckbox(l, "TSA_WD_BaseGen_AlwaysClearRect".Translate(),
                        ref s.experimentalAlwaysClearKcsgRect,
                        "TSA_WD_BaseGen_AlwaysClearRectTip".Translate(),
                        defaultValue: WorldDominationSettings.DefExperimentalAlwaysClearKcsgRect);
                    if (!s.experimentalAlwaysClearKcsgRect)
                    {
                        s.kcsgBlockedFlattenThreshold = SettingsUI.LabeledSlider(l, "TSA_WD_BaseGen_BlockedFlattenThreshold".Translate(), s.kcsgBlockedFlattenThreshold, 0.05f, 0.75f,
                            "TSA_WD_BaseGen_BlockedFlattenThresholdTip".Translate(), 0.05f, SliderFormat.PercentDecimal, WorldDominationSettings.DefKcsgBlockedFlattenThreshold);
                    }
                    SettingsUI.DrawCheckbox(l, "TSA_WD_BaseGen_RectBlend".Translate(),
                        ref s.experimentalKcsgRectBlend,
                        "TSA_WD_BaseGen_RectBlendTip".Translate(),
                        defaultValue: WorldDominationSettings.DefExperimentalKcsgRectBlend);
                }
            }

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_BaseGen_HeaderRockAndSoil".Translate(), ref rockAndSoilExpanded, SettingsUI.SectionHeaderColor))
            {
                SettingsUI.DrawCheckbox(l, "TSA_WD_BaseGen_RemapRock".Translate(),
                    ref s.kcsgRemapRockToMapStone,
                    "TSA_WD_BaseGen_RemapRockTip".Translate(),
                    defaultValue: WorldDominationSettings.DefKcsgRemapRockToMapStone);
                SettingsUI.DrawCheckbox(l, "TSA_WD_BaseGen_RemapFarmSoil".Translate(),
                    ref s.kcsgRemapFarmSoil,
                    "TSA_WD_BaseGen_RemapFarmSoilTip".Translate(),
                    defaultValue: WorldDominationSettings.DefKcsgRemapFarmSoil);
                SettingsUI.DrawCheckbox(l, "TSA_WD_BaseGen_FertileUnderCrops".Translate(),
                    ref s.kcsgFertileUnderCrops,
                    "TSA_WD_BaseGen_FertileUnderCropsTip".Translate(),
                    defaultValue: WorldDominationSettings.DefKcsgFertileUnderCrops);
            }

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_BaseGen_HeaderLayoutExtras".Translate(), ref layoutExtrasExpanded, SettingsUI.SectionHeaderColor))
            {
                SettingsUI.DrawCheckbox(l, "TSA_WD_BaseGen_SpawnPenLivestock".Translate(),
                    ref s.kcsgSpawnPenLivestock,
                    "TSA_WD_BaseGen_SpawnPenLivestockTip".Translate(),
                    defaultValue: WorldDominationSettings.DefKcsgSpawnPenLivestock);
                SettingsUI.DrawCheckbox(l, "TSA_WD_BaseGen_RemapTrees".Translate(),
                    ref s.kcsgRemapTreesToBiome,
                    "TSA_WD_BaseGen_RemapTreesTip".Translate(),
                    defaultValue: WorldDominationSettings.DefKcsgRemapTreesToBiome);
            }

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_BaseGen_HeaderAfterGeneration".Translate(), ref afterGenerationExpanded, SettingsUI.SectionHeaderColor))
            {
                SettingsUI.DrawCheckbox(l, "TSA_WD_BaseGen_UnfogRect".Translate(),
                    ref s.kcsgUnfogSettlementRect,
                    "TSA_WD_BaseGen_UnfogRectTip".Translate(),
                    defaultValue: WorldDominationSettings.DefKcsgUnfogSettlementRect);
                SettingsUI.DrawCheckbox(l, "TSA_WD_BaseGen_FogInteriorMineables".Translate(),
                    ref s.kcsgFogInteriorMineables,
                    "TSA_WD_BaseGen_FogInteriorMineablesTip".Translate(),
                    defaultValue: WorldDominationSettings.DefKcsgFogInteriorMineables);
                SettingsUI.DrawCheckbox(l, "TSA_WD_BaseGen_ForcePower".Translate(),
                    ref s.kcsgForceSettlementPower,
                    "TSA_WD_BaseGen_ForcePowerTip".Translate(),
                    defaultValue: WorldDominationSettings.DefKcsgForceSettlementPower);
                SettingsUI.DrawCheckbox(l, "TSA_WD_BaseGen_SilenceTurrets".Translate(),
                    ref s.kcsgSilenceDefeatedTurrets,
                    "TSA_WD_BaseGen_SilenceTurretsTip".Translate(),
                    defaultValue: WorldDominationSettings.DefKcsgSilenceDefeatedTurrets);
            }

            l.End();
            Widgets.EndScrollView();
        }
    }
}
