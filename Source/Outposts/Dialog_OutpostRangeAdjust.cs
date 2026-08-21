using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public enum OutpostRangeAdjustMode
    {
        RapidResponse
    }

    /// <summary>Rapid Response defense-mission configure dialog with live world-map ring preview. Commits on Confirm.</summary>
    public class Dialog_OutpostRangeAdjust : Window
    {
        /// <summary>Lowest tiles players may set for RR auto / mortar auto / flak adjust.</summary>
        public const float MinTiles = 5f;
        private readonly WorldObject_WD_Outpost outpost;
        private float sliderValue;
        private MissionMask rrTargetMask;
        private RaidTargetMask rrRaidTargetMask;
        private float rrMinStrengthRatio;
        private float rrMaxStrengthRatio;

        private static Dialog_OutpostRangeAdjust active;
        private static WorldObject_WD_Outpost previewOutpost;
        private static float previewRadius;

        private const float RrSectionPad = 10f;
        private const float RrRowH = 26f;
        private const float RrHeaderH = 22f;
        private const float RrSectionGap = 12f;
        private const float RrSliderH = 28f;

        public override Vector2 InitialSize => new Vector2(540f, 401f);

        public Dialog_OutpostRangeAdjust(WorldObject_WD_Outpost outpost)
        {
            this.outpost = outpost;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = true;
            doCloseX = true;
            doCloseButton = false;
            optionalTitle = null;
            sliderValue = RapidResponseUtility.GetRangeTiles(outpost);

            if (outpost != null)
            {
                rrTargetMask = outpost.RapidResponseMask;
                if (!outpost.RapidResponseActive)
                    rrTargetMask = MissionMask.None;
                rrRaidTargetMask = outpost.RapidResponseRaidTargetMask;
                rrMinStrengthRatio = outpost.RapidResponseMinStrengthRatio;
                rrMaxStrengthRatio = outpost.RapidResponseMaxStrengthRatio;
            }
        }

        public static bool TryGetPreview(WorldObject_WD_Outpost forOutpost, out OutpostRangeAdjustMode mode, out float radius)
        {
            mode = OutpostRangeAdjustMode.RapidResponse;
            radius = 0f;
            if (active == null || previewOutpost == null || forOutpost == null) return false;
            if (previewOutpost != forOutpost || previewOutpost.Destroyed) return false;
            radius = previewRadius;
            return radius > 0f;
        }

        public override void PreOpen()
        {
            base.PreOpen();
            active = this;
            if (outpost != null && !outpost.Destroyed)
            {
                Find.WorldSelector.ClearSelection();
                Find.WorldSelector.Select(outpost);
            }
            PushPreview();
        }

        public override void PreClose()
        {
            if (active == this)
            {
                active = null;
                previewOutpost = null;
                previewRadius = 0f;
            }
            base.PreClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (outpost == null || outpost.Destroyed)
            {
                Close();
                return;
            }

            float max = RapidResponseUtility.GetConfiguredMaxRangeTiles();
            float min = Mathf.Min(MinTiles, max);
            sliderValue = Mathf.Clamp(sliderValue, min, max);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 30f), "TSA_WD_RapidResponse_ConfigureDefense".Translate());
            Text.Font = GameFont.Small;

            float y = 36f;
            const float btnW = 120f;
            const float btnH = 28f;

            float contentW = inRect.width;
            float colGap = 10f;
            float colW = (contentW - colGap) / 2f;
            float leftRows = 5f;
            float rightRows = 3f;
            float topH = RrSectionPad * 2f + RrHeaderH + Mathf.Max(leftRows, rightRows) * RrRowH;

            Rect leftBox = new Rect(0f, y, colW, topH);
            Rect rightBox = new Rect(colW + colGap, y, colW, topH);
            Outpost_Dialog_UI.DrawOutcomeBox(leftBox);
            Outpost_Dialog_UI.DrawOutcomeBox(rightBox);

            float lx = leftBox.x + RrSectionPad;
            float ly = leftBox.y + RrSectionPad;
            float innerW = leftBox.width - RrSectionPad * 2f;
            DrawSectionHeader(new Rect(lx, ly, innerW, RrHeaderH), "TSA_WD_RapidResponse_CaravansToTarget".Translate(), "TSA_WD_RapidResponse_CaravansToTargetTip".Translate());
            ly += RrHeaderH;
            DrawMaskCheckboxRow(new Rect(lx, ly, innerW, RrRowH), "TSA_WD_Mortar_AutoAttack_Menu_Raider".Translate(), MissionMask.Raider, "TSA_WD_RapidResponse_Tip_TargetRaiders".Translate());
            ly += RrRowH;
            DrawMaskCheckboxRow(new Rect(lx, ly, innerW, RrRowH), "TSA_WD_Mortar_AutoAttack_Menu_Expansion".Translate(), MissionMask.Expansion, "TSA_WD_RapidResponse_Tip_TargetExpansion".Translate());
            ly += RrRowH;
            DrawMaskCheckboxRow(new Rect(lx, ly, innerW, RrRowH), "TSA_WD_Mortar_AutoAttack_Menu_Road".Translate(), MissionMask.Road, "TSA_WD_RapidResponse_Tip_TargetRoad".Translate());
            ly += RrRowH;
            DrawMaskCheckboxRow(new Rect(lx, ly, innerW, RrRowH), "TSA_WD_Mortar_AutoAttack_Menu_Trader".Translate(), MissionMask.Trader, "TSA_WD_RapidResponse_Tip_TargetTrader".Translate());
            ly += RrRowH;
            DrawMaskCheckboxRow(new Rect(lx, ly, innerW, RrRowH), "TSA_WD_Mortar_AutoAttack_Menu_Fortify".Translate(), MissionMask.Fortify, "TSA_WD_RapidResponse_Tip_TargetFortify".Translate());

            float rx = rightBox.x + RrSectionPad;
            float ry = rightBox.y + RrSectionPad;
            float rightInnerW = rightBox.width - RrSectionPad * 2f;
            DrawSectionHeader(new Rect(rx, ry, rightInnerW, RrHeaderH), "TSA_WD_RapidResponse_WhichRaidersToIntercept".Translate(), "TSA_WD_RapidResponse_WhichRaidersToInterceptTip".Translate());
            ry += RrHeaderH;
            DrawRaidTargetCheckboxRow(new Rect(rx, ry, rightInnerW, RrRowH), "TSA_WD_RapidResponse_RaidTarget_Player".Translate(), RaidTargetMask.Player, "TSA_WD_RapidResponse_Tip_RaidTargetPlayer".Translate());
            ry += RrRowH;
            DrawRaidTargetCheckboxRow(new Rect(rx, ry, rightInnerW, RrRowH), "TSA_WD_RapidResponse_RaidTarget_Allies".Translate(), RaidTargetMask.Allies, "TSA_WD_RapidResponse_Tip_RaidTargetAllies".Translate());
            ry += RrRowH;
            DrawRaidTargetCheckboxRow(new Rect(rx, ry, rightInnerW, RrRowH), "TSA_WD_RapidResponse_RaidTarget_OtherNpcs".Translate(), RaidTargetMask.OtherNpcs, "TSA_WD_RapidResponse_Tip_RaidTargetOtherNpcs".Translate());

            y += topH + RrSectionGap;

            Rect minStrengthRow = new Rect(0f, y, contentW, RrSliderH);
            rrMinStrengthRatio = DrawLabeledSliderRow(
                minStrengthRow,
                "TSA_WD_RapidResponse_MenuSection_MinStrength".Translate(),
                rrMinStrengthRatio,
                0f,
                4f,
                0.05f,
                SliderFormat.Percent,
                "TSA_WD_RapidResponse_Tip_MinStrength".Translate());
            y += RrSliderH + RrSectionGap;

            Rect maxStrengthRow = new Rect(0f, y, contentW, RrSliderH);
            rrMaxStrengthRatio = DrawLabeledSliderRow(
                maxStrengthRow,
                "TSA_WD_RapidResponse_MenuSection_MaxStrength".Translate(),
                rrMaxStrengthRatio,
                RapidResponseUtility.MinMaxStrengthRatio,
                RapidResponseUtility.MaxMaxStrengthRatio,
                0.05f,
                SliderFormat.Multiplier,
                "TSA_WD_RapidResponse_Tip_MaxStrength".Translate());
            y += RrSliderH + RrSectionGap;

            Rect rangeRow = new Rect(0f, y, contentW, RrSliderH);
            float nextRr = DrawLabeledSliderRow(
                rangeRow,
                "TSA_WD_RapidResponse_MenuSection_Range".Translate(),
                sliderValue,
                min,
                max,
                1f,
                SliderFormat.Fixed0,
                "TSA_WD_RapidResponse_Tip_Range".Translate());
            if (!Mathf.Approximately(nextRr, sliderValue))
            {
                sliderValue = nextRr;
                PushPreview();
            }

            Rect resetRect = new Rect(0f, inRect.height - btnH, btnW, btnH);
            TooltipHandler.TipRegion(resetRect, "TSA_WD_RapidResponse_Tip_Reset".Translate());
            if (Widgets.ButtonText(resetRect, "TSA_WD_OutpostRange_Reset".Translate()))
            {
                rrTargetMask = MissionMask.Raider;
                rrRaidTargetMask = RaidTargetMask.Player | RaidTargetMask.Allies;
                rrMinStrengthRatio = 0.9f;
                rrMaxStrengthRatio = RapidResponseUtility.DefaultMaxStrengthRatio;
                sliderValue = max;
                PushPreview();
            }
            Rect confirmRect = new Rect(inRect.width - btnW, inRect.height - btnH, btnW, btnH);
            TooltipHandler.TipRegion(confirmRect, "TSA_WD_RapidResponse_Tip_Confirm".Translate());
            if (Widgets.ButtonText(confirmRect, "TSA_WD_OutpostRange_Confirm".Translate()))
                ConfirmAndClose();
        }

        private void ConfirmAndClose()
        {
            if (outpost == null || outpost.Destroyed)
            {
                Close();
                return;
            }

            float max = RapidResponseUtility.GetConfiguredMaxRangeTiles();
            float min = Mathf.Min(MinTiles, max);
            float value = Mathf.Clamp(sliderValue, min, max);
            outpost.SetRapidResponseActive(rrTargetMask != MissionMask.None);
            outpost.SetRapidResponseMask(rrTargetMask == MissionMask.None ? MissionMask.Raider : rrTargetMask);
            outpost.SetRapidResponseRaidTargetMask(rrRaidTargetMask);
            outpost.SetRapidResponseMinStrengthRatio(rrMinStrengthRatio);
            outpost.SetRapidResponseMaxStrengthRatio(rrMaxStrengthRatio);
            outpost.SetRapidResponseRangeOverride(value);
            Close();
        }

        private void PushPreview()
        {
            previewOutpost = outpost;
            previewRadius = sliderValue;
        }

        private static void DrawSectionHeader(Rect rect, string text, string tip = null)
        {
            GUI.color = SettingsUI.SectionHeaderColor;
            Widgets.Label(rect, text);
            GUI.color = Color.white;
            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(rect, tip);
        }

        private static float DrawLabeledSliderRow(Rect row, string label, float val, float min, float max, float step, SliderFormat format, string tip = null)
        {
            string suffix = format switch
            {
                SliderFormat.Fixed0 => val.ToString("F0"),
                SliderFormat.Percent => (val * 100f).ToString("F0") + " %",
                SliderFormat.Multiplier => val.ToString("F2") + "x",
                _ => val.ToString("F1"),
            };
            Widgets.Label(row.LeftPart(0.5f), $"{label}: {suffix.Colorize(Color.cyan)}");
            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(row, tip);
            return Widgets.HorizontalSlider(row.RightPart(0.5f), val, min, max, false, null, null, null, step);
        }

        private void DrawMaskCheckboxRow(Rect rect, string label, MissionMask bit, string tip)
        {
            bool value = (rrTargetMask & bit) != 0;
            Widgets.CheckboxLabeled(rect, label, ref value);
            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(rect, tip);
            if (value)
                rrTargetMask |= bit;
            else
                rrTargetMask &= ~bit;
        }

        private void DrawRaidTargetCheckboxRow(Rect rect, string label, RaidTargetMask bit, string tip)
        {
            bool value = (rrRaidTargetMask & bit) != 0;
            Widgets.CheckboxLabeled(rect, label, ref value);
            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(rect, tip);
            if (value)
                rrRaidTargetMask |= bit;
            else
                rrRaidTargetMask &= ~bit;
        }

        public static void Open(WorldObject_WD_Outpost outpost, OutpostRangeAdjustMode mode = OutpostRangeAdjustMode.RapidResponse)
        {
            if (outpost == null || outpost.Destroyed) return;
            Find.WindowStack.Add(new Dialog_OutpostRangeAdjust(outpost));
        }
    }
}
