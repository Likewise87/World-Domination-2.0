using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Outpost cumulative skill diminishing returns: enable, hard cap, 5 band ends + weights, live preview.</summary>
    public class Dialog_OutpostSkillScalingSettings : Window
    {
        private const float BandEndMin = 1f;
        private const float BandEndMax = 500f;
        private const float BandWeightMinPct = 10f;
        private const float BandWeightMaxPct = 100f;

        private Vector2 scrollPosition;
        private readonly string windowTitle;
        private bool bandsExpanded = true;
        private bool previewExpanded = true;
        private float previewRaw = 100f;
        private bool _bandsSanitized;

        public override Vector2 InitialSize => new Vector2(850f, 750f);

        public Dialog_OutpostSkillScalingSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnOutpostSkillScaling".Translate();
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
            OutpostSkillScaling.EnsureArrays(s);
            // One-time sanitize of loaded floats so sliders start on step boundaries (no DragSlider spam).
            if (!_bandsSanitized)
            {
                OutpostSkillScaling.NormalizeBands(s);
                _bandsSanitized = true;
            }

            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () =>
            {
                OutpostSkillScaling.ResetToDefaults(s);
                previewRaw = 100f;
            },
                () => { bandsExpanded = previewExpanded = true; },
                () => { bandsExpanded = previewExpanded = false; });

            l.Label("TSA_WD_SkillScaling_Intro".Translate());
            l.Gap(6f);

            SettingsUI.DrawCheckbox(l, "TSA_WD_SkillScaling_Enable".Translate(), ref s.enableOutpostSkillDiminishingReturns,
                "TSA_WD_SkillScaling_EnableTip".Translate(), defaultValue: OutpostSkillScaling.DefEnableDiminishingReturns);

            // Round before HorizontalSlider: off-step floats (esp. weight*100) make RimWorld play DragSlider every frame.
            float hardCapIn = Mathf.Round(s.outpostSkillHardCapRaw);
            float hardCapOut = SettingsUI.LabeledSlider(l, "TSA_WD_SkillScaling_HardCap".Translate(),
                hardCapIn, 60f, 500f, "TSA_WD_SkillScaling_HardCapTip".Translate(), 1f, SliderFormat.Fixed0,
                OutpostSkillScaling.DefHardCapRaw);
            bool bandsDirty = !Mathf.Approximately(hardCapOut, hardCapIn);
            if (bandsDirty)
                s.outpostSkillHardCapRaw = hardCapOut;

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_SkillScaling_BandsHeader".Translate(), ref bandsExpanded, SettingsUI.SectionHeaderColor))
            {
                for (int i = 0; i < OutpostSkillScaling.BandCount; i++)
                {
                    // Fixed slider ranges always. Monotonicity is enforced after edit via NormalizeBands (not by shrinking min/max).
                    // Display start is exclusive of previous end (0–60, then 61–100).
                    float start = i == 0 ? 0f : Mathf.Round(s.outpostSkillBandEnds[i - 1]) + 1f;
                    float endIn = Mathf.Clamp(Mathf.Round(s.outpostSkillBandEnds[i]), BandEndMin, BandEndMax);
                    float wIn = Mathf.Clamp(Mathf.Round(s.outpostSkillBandWeights[i] * 100f), BandWeightMinPct, BandWeightMaxPct);

                    string rangeLabel = "TSA_WD_SkillScaling_BandRange".Translate(
                        start.ToString("F0").Colorize(Color.cyan),
                        endIn.ToString("F0").Colorize(Color.cyan));
                    string effLabel = "TSA_WD_SkillScaling_BandEfficiency".Translate(
                        wIn.ToString("F0").Colorize(Color.cyan));

                    l.Gap(4f);
                    Rect row = l.GetRect(52f);
                    float gap = 12f;
                    float colW = (row.width - gap) * 0.5f;
                    Rect left = new Rect(row.x, row.y, colW, row.height);
                    Rect right = new Rect(row.x + colW + gap, row.y, colW, row.height);

                    TooltipHandler.TipRegion(left, SettingsUI.TooltipWithDefault(
                        "TSA_WD_SkillScaling_BandEndTip".Translate(),
                        OutpostSkillScaling.DefBandEnds[i], SliderFormat.Fixed0));
                    TooltipHandler.TipRegion(right, SettingsUI.TooltipWithDefault(
                        "TSA_WD_SkillScaling_BandWeightTip".Translate(),
                        OutpostSkillScaling.DefBandWeights[i] * 100f, SliderFormat.Fixed0));

                    Text.Font = GameFont.Small;
                    Widgets.Label(left.TopPartPixels(24f), rangeLabel);
                    Widgets.Label(right.TopPartPixels(24f), effLabel);

                    float endOut = Widgets.HorizontalSlider(left.BottomPartPixels(22f), endIn, BandEndMin, BandEndMax, false, null, null, null, 1f);
                    float wOut = Widgets.HorizontalSlider(right.BottomPartPixels(22f), wIn, BandWeightMinPct, BandWeightMaxPct, false, null, null, null, 1f);

                    if (!Mathf.Approximately(endOut, endIn))
                    {
                        s.outpostSkillBandEnds[i] = endOut;
                        bandsDirty = true;
                    }
                    if (!Mathf.Approximately(wOut, wIn))
                    {
                        s.outpostSkillBandWeights[i] = wOut / 100f;
                        bandsDirty = true;
                    }
                }
                if (bandsDirty)
                    OutpostSkillScaling.NormalizeBands(s);
            }

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_SkillScaling_PreviewHeader".Translate(), ref previewExpanded, SettingsUI.SectionHeaderColor))
            {
                float previewIn = Mathf.Round(previewRaw);
                float previewOut = SettingsUI.LabeledSlider(l, "TSA_WD_SkillScaling_PreviewRaw".Translate(),
                    previewIn, 0f, 400f, null, 1f, SliderFormat.Fixed0, 100f);
                if (!Mathf.Approximately(previewOut, previewIn))
                    previewRaw = previewOut;
                float eff = OutpostSkillScaling.ToEffective(previewRaw);
                Text.Font = GameFont.Small;
                l.Label("TSA_WD_SkillScaling_PreviewResult".Translate(eff.ToString("F0")));
                l.Gap(4f);
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                l.Label(OutpostSkillScaling.BuildBandBreakdownTip(previewRaw));
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }

            l.End();
            Widgets.EndScrollView();
        }
    }
}
