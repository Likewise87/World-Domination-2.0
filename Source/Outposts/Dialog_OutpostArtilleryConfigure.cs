using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public enum ArtilleryConfigureTab
    {
        Mortar,
        AntiAir
    }

    /// <summary>Tabbed mortar / anti-air configure dialog. Confirm commits both tabs; Reset only the active tab.</summary>
    public class Dialog_OutpostArtilleryConfigure : Window
    {
        private readonly WorldObject_WD_Outpost outpost;
        private readonly bool hasAntiAir;
        private ArtilleryConfigureTab tab;

        private MissionMask mortarMask;
        private RaidTargetMask mortarRaidMask;
        private float mortarRange;

        private bool aaEnabled;
        private AntiAirKindMask aaKindMask;
        private float aaRange;

        private static Dialog_OutpostArtilleryConfigure active;
        private static WorldObject_WD_Outpost previewOutpost;
        private static ArtilleryConfigureTab previewTab;
        private static float previewMortarRadius;
        private static float previewAaRadius;
        private static bool previewHasAntiAir;

        private const float SectionPad = 10f;
        private const float RowH = 26f;
        private const float HeaderH = 22f;
        private const float SectionGap = 12f;
        private const float SliderH = 28f;
        private const float TabH = 30f;

        public override Vector2 InitialSize => new Vector2(540f, hasAntiAir ? 366f : 326f);

        public Dialog_OutpostArtilleryConfigure(WorldObject_WD_Outpost outpost)
        {
            this.outpost = outpost;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = true;
            doCloseX = true;
            doCloseButton = false;
            optionalTitle = null;

            hasAntiAir = AntiAirFireUtils.HasAntiAirUpgrade(outpost);
            tab = ArtilleryConfigureTab.Mortar;

            mortarMask = outpost.MortarDefenseMask;
            if (!outpost.MortarDefenseActive)
                mortarMask = MissionMask.None;
            mortarRaidMask = outpost.MortarRaidTargetMask;
            mortarRange = MortarFireUtils.GetPlayerMortarMaxRangeTiles(outpost);

            aaEnabled = outpost.AntiAirDefenseActive;
            aaKindMask = outpost.AntiAirTargetKinds;
            aaRange = AntiAirFireUtils.GetPlayerAntiAirMaxRangeTiles(outpost);
        }

        public static bool TryGetPreview(
            WorldObject_WD_Outpost forOutpost,
            out ArtilleryConfigureTab tab,
            out float mortarRadius,
            out float aaRadius,
            out bool hasAa)
        {
            tab = default;
            mortarRadius = 0f;
            aaRadius = 0f;
            hasAa = false;
            if (active == null || previewOutpost == null || forOutpost == null) return false;
            if (previewOutpost != forOutpost || previewOutpost.Destroyed) return false;
            tab = previewTab;
            mortarRadius = previewMortarRadius;
            aaRadius = previewAaRadius;
            hasAa = previewHasAntiAir;
            return mortarRadius > 0f || (hasAa && aaRadius > 0f);
        }

        public static void Open(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.Destroyed) return;
            Find.WindowStack.Add(new Dialog_OutpostArtilleryConfigure(outpost));
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
                previewMortarRadius = 0f;
                previewAaRadius = 0f;
                previewHasAntiAir = false;
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

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 30f), "TSA_WD_Artillery_ConfigureTitle".Translate());
            Text.Font = GameFont.Small;

            float y = 36f;
            if (hasAntiAir)
            {
                float tabW = (inRect.width - 8f) / 2f;
                Rect mortarTabRect = new Rect(0f, y, tabW, TabH);
                Rect aaTabRect = new Rect(tabW + 8f, y, tabW, TabH);
                if (DrawSelectedTabButton(mortarTabRect, "TSA_WD_Artillery_Tab_Mortar".Translate(), tab == ArtilleryConfigureTab.Mortar))
                {
                    tab = ArtilleryConfigureTab.Mortar;
                    PushPreview();
                }
                if (DrawSelectedTabButton(aaTabRect, "TSA_WD_Artillery_Tab_AntiAir".Translate(), tab == ArtilleryConfigureTab.AntiAir))
                {
                    tab = ArtilleryConfigureTab.AntiAir;
                    PushPreview();
                }
                y += TabH + 4f;
                Widgets.DrawLineHorizontal(0f, y, inRect.width);
                y += 8f;
            }

            const float btnW = 120f;
            const float btnH = 28f;
            Rect content = new Rect(0f, y, inRect.width, inRect.height - y - btnH - 8f);

            if (tab == ArtilleryConfigureTab.Mortar)
                DrawMortarTab(content);
            else
                DrawAntiAirTab(content);

            if (Widgets.ButtonText(new Rect(0f, inRect.height - btnH, btnW, btnH), "TSA_WD_OutpostRange_Reset".Translate()))
                ResetActiveTab();
            if (Widgets.ButtonText(new Rect(inRect.width - btnW, inRect.height - btnH, btnW, btnH), "TSA_WD_OutpostRange_Confirm".Translate()))
                ConfirmAndClose();
        }

        private void DrawMortarTab(Rect area)
        {
            float contentW = area.width;
            float colGap = 10f;
            float colW = (contentW - colGap) / 2f;
            float topH = SectionPad * 2f + HeaderH + 5f * RowH;

            Rect leftBox = new Rect(area.x, area.y, colW, topH);
            Rect rightBox = new Rect(area.x + colW + colGap, area.y, colW, topH);
            Outpost_Dialog_UI.DrawOutcomeBox(leftBox);
            Outpost_Dialog_UI.DrawOutcomeBox(rightBox);

            float lx = leftBox.x + SectionPad;
            float ly = leftBox.y + SectionPad;
            float innerW = leftBox.width - SectionPad * 2f;
            DrawSectionHeader(new Rect(lx, ly, innerW, HeaderH), "TSA_WD_RapidResponse_CaravansToTarget".Translate());
            ly += HeaderH;
            DrawMaskRow(new Rect(lx, ly, innerW, RowH), "TSA_WD_Mortar_AutoAttack_Menu_Raider".Translate(), MissionMask.Raider);
            ly += RowH;
            DrawMaskRow(new Rect(lx, ly, innerW, RowH), "TSA_WD_Mortar_AutoAttack_Menu_Expansion".Translate(), MissionMask.Expansion);
            ly += RowH;
            DrawMaskRow(new Rect(lx, ly, innerW, RowH), "TSA_WD_Mortar_AutoAttack_Menu_Road".Translate(), MissionMask.Road);
            ly += RowH;
            DrawMaskRow(new Rect(lx, ly, innerW, RowH), "TSA_WD_Mortar_AutoAttack_Menu_Trader".Translate(), MissionMask.Trader);
            ly += RowH;
            DrawMaskRow(new Rect(lx, ly, innerW, RowH), "TSA_WD_Mortar_AutoAttack_Menu_Fortify".Translate(), MissionMask.Fortify);

            float rx = rightBox.x + SectionPad;
            float ry = rightBox.y + SectionPad;
            float rightInnerW = rightBox.width - SectionPad * 2f;
            DrawSectionHeader(new Rect(rx, ry, rightInnerW, HeaderH), "TSA_WD_RapidResponse_WhichRaidersToIntercept".Translate());
            ry += HeaderH;
            DrawRaidRow(new Rect(rx, ry, rightInnerW, RowH), "TSA_WD_RapidResponse_RaidTarget_Player".Translate(), RaidTargetMask.Player);
            ry += RowH;
            DrawRaidRow(new Rect(rx, ry, rightInnerW, RowH), "TSA_WD_RapidResponse_RaidTarget_Allies".Translate(), RaidTargetMask.Allies);
            ry += RowH;
            DrawRaidRow(new Rect(rx, ry, rightInnerW, RowH), "TSA_WD_RapidResponse_RaidTarget_OtherNpcs".Translate(), RaidTargetMask.OtherNpcs);

            float y = area.y + topH + SectionGap;
            float max = MortarFireUtils.GetPlayerMortarConfiguredMaxRangeTiles(outpost);
            float min = Mathf.Min(Dialog_OutpostRangeAdjust.MinTiles, max);
            mortarRange = Mathf.Clamp(mortarRange, min, max);
            Rect mortarSliderRow = new Rect(area.x, y, contentW, SliderH);
            float next = DrawLabeledSliderRow(
                mortarSliderRow,
                "TSA_WD_OutpostRange_Title_Mortar".Translate(),
                mortarRange, min, max, 1f);
            TipArtilleryRangeIncludesStrategist(mortarSliderRow, max);
            if (!Mathf.Approximately(next, mortarRange))
            {
                mortarRange = next;
                PushPreview();
            }
        }

        private void DrawAntiAirTab(Rect area)
        {
            float contentW = area.width;
            float y = area.y;

            bool enabled = aaEnabled;
            Widgets.CheckboxLabeled(new Rect(area.x, y, contentW, RowH), "TSA_WD_AntiAir_Auto_Toggle".Translate(), ref enabled);
            aaEnabled = enabled;
            y += RowH + 6f;

            float boxH = SectionPad * 2f + HeaderH + 2f * RowH;
            Rect kindBox = new Rect(area.x, y, contentW, boxH);
            Outpost_Dialog_UI.DrawOutcomeBox(kindBox);
            float kx = kindBox.x + SectionPad;
            float ky = kindBox.y + SectionPad;
            float kw = kindBox.width - SectionPad * 2f;
            DrawSectionHeader(new Rect(kx, ky, kw, HeaderH), "TSA_WD_Artillery_AaTargets".Translate());
            ky += HeaderH;
            DrawAaKindRow(new Rect(kx, ky, kw, RowH), "TSA_WD_Artillery_AaTarget_MortarShells".Translate(), AntiAirKindMask.MortarShells);
            ky += RowH;
            DrawAaKindRow(new Rect(kx, ky, kw, RowH), "TSA_WD_Artillery_AaTarget_DropPods".Translate(), AntiAirKindMask.DropPods);

            y += boxH + SectionGap;
            float max = AntiAirFireUtils.GetPlayerAntiAirConfiguredMaxRangeTiles(outpost);
            float min = Mathf.Min(Dialog_OutpostRangeAdjust.MinTiles, max);
            aaRange = Mathf.Clamp(aaRange, min, max);
            Rect aaSliderRow = new Rect(area.x, y, contentW, SliderH);
            float next = DrawLabeledSliderRow(
                aaSliderRow,
                "TSA_WD_OutpostRange_Title_Flak".Translate(),
                aaRange, min, max, 1f);
            TipArtilleryRangeIncludesStrategist(aaSliderRow, max);
            if (!Mathf.Approximately(next, aaRange))
            {
                aaRange = next;
                PushPreview();
            }
        }

        private void TipArtilleryRangeIncludesStrategist(Rect row, float maxTiles)
        {
            float bonus = OutpostExpertUtility.GetStrategistAttackRangeBonusFraction(outpost);
            if (bonus <= 0f) return;
            string tip = OutpostExpertUtility.BuildExpertContributionTooltip(
                outpost, OutpostExpertRole.Strategist, bonus, ExpertEffect.MortarAntiAirRange);
            if (string.IsNullOrEmpty(tip)) return;
            tip += "\n\n" + "TSA_WD_Artillery_RangeMaxIncludesStrategist".Translate(
                maxTiles.ToString("F0"),
                Mathf.RoundToInt(bonus * 100f).ToString());
            TooltipHandler.TipRegion(row, tip);
        }

        private void ResetActiveTab()
        {
            if (tab == ArtilleryConfigureTab.Mortar)
            {
                mortarMask = MissionMask.Raider | MissionMask.Expansion;
                mortarRaidMask = RaidTargetMask.Player | RaidTargetMask.Allies;
                mortarRange = MortarFireUtils.GetPlayerMortarConfiguredMaxRangeTiles(outpost);
            }
            else
            {
                aaEnabled = true;
                aaKindMask = AntiAirKindMask.All;
                aaRange = AntiAirFireUtils.GetPlayerAntiAirConfiguredMaxRangeTiles(outpost);
            }
            PushPreview();
        }

        private void ConfirmAndClose()
        {
            if (outpost == null || outpost.Destroyed)
            {
                Close();
                return;
            }

            outpost.SetMortarDefenseActive(mortarMask != MissionMask.None);
            outpost.SetMortarDefenseMask(mortarMask == MissionMask.None ? MissionMask.Raider | MissionMask.Expansion : mortarMask);
            outpost.SetMortarRaidTargetMask(mortarRaidMask);
            outpost.SetMortarRangeOverride(mortarRange);

            if (hasAntiAir)
            {
                outpost.SetAntiAirDefenseActive(aaEnabled && aaKindMask != AntiAirKindMask.None);
                outpost.SetAntiAirKindMask(aaKindMask == AntiAirKindMask.None ? AntiAirKindMask.All : aaKindMask);
                outpost.SetAntiAirRangeOverride(aaRange);
            }
            Close();
        }

        private void PushPreview()
        {
            previewOutpost = outpost;
            previewTab = tab;
            previewMortarRadius = mortarRange;
            previewAaRadius = aaRange;
            previewHasAntiAir = hasAntiAir;
        }

        private static void DrawSectionHeader(Rect rect, string text)
        {
            GUI.color = SettingsUI.SectionHeaderColor;
            Widgets.Label(rect, text);
            GUI.color = Color.white;
        }

        /// <summary>
        /// Tab button: solid button background for both states; selected uses the same blue tint + white border
        /// as production list rows; unselected keeps a thinner, dimmer border.
        /// </summary>
        private static bool DrawSelectedTabButton(Rect rect, string label, bool selected)
        {
            // Base fill so both states read as clickable tabs.
            Widgets.DrawBoxSolid(rect, new Color(0.16f, 0.16f, 0.18f, 0.92f));
            Outpost_Dialog_UI.DrawSelectedRowTint(rect, selected);

            if (selected)
            {
                GUI.color = Color.white;
                Widgets.DrawBox(rect, 2);
            }
            else
            {
                GUI.color = new Color(1f, 1f, 1f, 0.28f);
                Widgets.DrawBox(rect, 1);
            }
            GUI.color = Color.white;

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            if (Mouse.IsOver(rect))
                Widgets.DrawHighlight(rect);
            return !selected && Widgets.ButtonInvisible(rect);
        }

        private static float DrawLabeledSliderRow(Rect row, string label, float val, float min, float max, float step)
        {
            Widgets.Label(row.LeftPart(0.5f), $"{label}: {val.ToString("F0").Colorize(Color.cyan)}");
            return Widgets.HorizontalSlider(row.RightPart(0.5f), val, min, max, false, null, null, null, step);
        }

        private void DrawMaskRow(Rect rect, string label, MissionMask bit)
        {
            bool value = (mortarMask & bit) != 0;
            Widgets.CheckboxLabeled(rect, label, ref value);
            if (value) mortarMask |= bit;
            else mortarMask &= ~bit;
        }

        private void DrawRaidRow(Rect rect, string label, RaidTargetMask bit)
        {
            bool value = (mortarRaidMask & bit) != 0;
            Widgets.CheckboxLabeled(rect, label, ref value);
            if (value) mortarRaidMask |= bit;
            else mortarRaidMask &= ~bit;
        }

        private void DrawAaKindRow(Rect rect, string label, AntiAirKindMask bit)
        {
            bool value = (aaKindMask & bit) != 0;
            Widgets.CheckboxLabeled(rect, label, ref value);
            if (value) aaKindMask |= bit;
            else aaKindMask &= ~bit;
        }
    }
}
