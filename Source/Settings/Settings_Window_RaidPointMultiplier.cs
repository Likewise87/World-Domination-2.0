using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_RaidPointMultiplier : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;
        private bool playerRaidsExpanded = true;
        private bool clampExpanded;

        public override Vector2 InitialSize => new Vector2(850f, 750f);
        public Dialog_RaidPointMultiplier()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnRaidMult".Translate();
            optionalTitle = null;
        }

        public override void PreClose()
        {
            base.PreClose();
            WorldDominationMod.settings?.NormalizeRaidClampFractions();
            if (Current.ProgramState != ProgramState.Playing) return;
            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.Notify_WeightsChanged();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            float contentWidth = contentRect.width - 24f;
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 2000f);

            Widgets.BeginScrollView(contentRect, ref scrollPosition, scrollViewRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(scrollViewRect);
            var s = WorldDominationMod.settings;
            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetThreat(),
                () => { playerRaidsExpanded = clampExpanded = true; },
                () => { playerRaidsExpanded = clampExpanded = false; });

            // --- World Raids on player colony (top section) ---
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Raid_HeaderPlayer".Translate(), ref playerRaidsExpanded, SettingsUI.SectionHeaderColor))
            {

            l.CheckboxLabeled("TSA_WD_Raid_AllowPlayer".Translate(), ref s.allowPlayerRaid,
                SettingsUI.TooltipWithDefault("TSA_WD_Raid_AllowPlayerTooltip".Translate(), WorldDominationSettings.DefAllowPlayerRaid));

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

            l.CheckboxLabeled("TSA_WD_Raid_AllowOutpost".Translate(), ref s.allowPlayerOutpostRaid,
                SettingsUI.TooltipWithDefault("TSA_WD_Raid_AllowOutpostTooltip".Translate(), WorldDominationSettings.DefAllowPlayerOutpostRaid));

            s.maxPlayerWdRaidsPerDay = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Raid_MaxPerDay".Translate(), s.maxPlayerWdRaidsPerDay, 1f, 10f,
                "TSA_WD_Raid_MaxPerDayTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefMaxPlayerWdRaidsPerDay));
            s.maxPlayerWdRaidsPer4Days = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Raid_MaxPer4Days".Translate(), s.maxPlayerWdRaidsPer4Days, 1f, 20f,
                "TSA_WD_Raid_MaxPer4DaysTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefMaxPlayerWdRaidsPer4Days));
            s.maxPlayerWdRaidsPer7Days = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Raid_MaxPer7Days".Translate(), s.maxPlayerWdRaidsPer7Days, 1f, 30f,
                "TSA_WD_Raid_MaxPer7DaysTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefMaxPlayerWdRaidsPer7Days));
            s.ClampPlayerWdRaidRateCaps();

            l.CheckboxLabeled(
                "TS_WD_Threat_BlockStorytellerRaids".Translate(),
                ref s.blockStorytellerRaidsOnlyWD,
                SettingsUI.TooltipWithDefault("TS_WD_Threat_BlockStorytellerRaidsTooltip".Translate(), WorldDominationSettings.DefBlockStorytellerRaidsOnlyWD)
            );

            if (s.blockStorytellerRaidsOnlyWD)
            {
                l.CheckboxLabeled(
                    "TS_WD_Threat_AllowNonWdStorytellerRaids".Translate(),
                    ref s.allowStorytellerRaidsFromNonWdFactions,
                    SettingsUI.TooltipWithDefault("TS_WD_Threat_AllowNonWdStorytellerRaidsTooltip".Translate(), WorldDominationSettings.DefAllowStorytellerRaidsFromNonWdFactions)
                );
            }
            }
            l.GapLine();
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Threat_HeaderWDClamp".Translate(), ref clampExpanded, SettingsUI.SectionHeaderColor))
            {

            l.CheckboxLabeled(
                "TS_WD_CaravanRaidAlwaysUseStrength".Translate(),
                ref s.alwaysUseStrengthAsRaidPoints,
                SettingsUI.TooltipWithDefault("TS_WD_CaravanRaidAlwaysUseStrengthTooltip".Translate(), WorldDominationSettings.DefAlwaysUseStrengthAsRaidPoints));

            l.CheckboxLabeled(
                "TS_WD_OutpostDefenseAlwaysUseStrength".Translate(),
                ref s.alwaysUseStrengthAsOutpostDefenseRaidPoints,
                SettingsUI.TooltipWithDefault("TS_WD_OutpostDefenseAlwaysUseStrengthTooltip".Translate(), WorldDominationSettings.DefAlwaysUseStrengthAsOutpostDefenseRaidPoints));

            if (!s.alwaysUseStrengthAsRaidPoints)
            {
                l.CheckboxLabeled(
                    "TS_WD_RaidClamp_ScaleWithEscalation".Translate(),
                    ref s.scaleRaidClampWithEscalation,
                    SettingsUI.TooltipWithDefault("TS_WD_RaidClamp_ScaleWithEscalationTooltip".Translate(), WorldDominationSettings.DefScaleRaidClampWithEscalation));

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
            l.End();
            Widgets.EndScrollView();
        }
    }
}
