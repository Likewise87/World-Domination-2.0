using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// AT Turret auto-fire configure dialog (mission mask, raid filters, range).
    /// Same fields as the mortar tab of <see cref="Dialog_OutpostArtilleryConfigure"/>; no anti-air.
    /// </summary>
    public class Dialog_AtTurretConfigure : Window
    {
        private readonly WorldObject_AT_Turret turret;

        private MissionMask defenseMask;
        private RaidTargetMask raidTargetMask;
        private float range;

        private static Dialog_AtTurretConfigure active;
        private static WorldObject_AT_Turret previewTurret;
        private static float previewRadius;

        private const float SectionPad = 10f;
        private const float RowH = 26f;
        private const float HeaderH = 22f;
        private const float SectionGap = 12f;
        private const float SliderH = 28f;

        public override Vector2 InitialSize => new Vector2(540f, 326f);

        public Dialog_AtTurretConfigure(WorldObject_AT_Turret turret)
        {
            this.turret = turret;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = true;
            doCloseX = true;
            doCloseButton = false;
            optionalTitle = null;

            defenseMask = turret.DefenseMask;
            if (!turret.DefenseActive)
                defenseMask = MissionMask.None;
            raidTargetMask = turret.DefenseRaidTargetMask;
            range = turret.EffectiveRangeTiles;
        }

        public static bool TryGetPreview(WorldObject_AT_Turret forTurret, out float radius)
        {
            radius = 0f;
            if (active == null || previewTurret == null || forTurret == null) return false;
            if (previewTurret != forTurret || previewTurret.Destroyed) return false;
            radius = previewRadius;
            return radius > 0f;
        }

        public static void Open(WorldObject_AT_Turret turret)
        {
            if (turret == null || turret.Destroyed) return;
            Find.WindowStack.Add(new Dialog_AtTurretConfigure(turret));
        }

        public override void PreOpen()
        {
            base.PreOpen();
            active = this;
            if (turret != null && !turret.Destroyed)
            {
                Find.WorldSelector.ClearSelection();
                Find.WorldSelector.Select(turret);
            }
            PushPreview();
        }

        public override void PreClose()
        {
            if (active == this)
            {
                active = null;
                previewTurret = null;
                previewRadius = 0f;
            }
            base.PreClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (turret == null || turret.Destroyed)
            {
                Close();
                return;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 30f), "TSA_WD_AT_Turret_ConfigureTitle".Translate());
            Text.Font = GameFont.Small;

            const float btnW = 120f;
            const float btnH = 28f;
            Rect content = new Rect(0f, 36f, inRect.width, inRect.height - 36f - btnH - 8f);
            DrawBody(content);

            if (Widgets.ButtonText(new Rect(0f, inRect.height - btnH, btnW, btnH), "TSA_WD_OutpostRange_Reset".Translate()))
                Reset();
            if (Widgets.ButtonText(new Rect(inRect.width - btnW, inRect.height - btnH, btnW, btnH), "TSA_WD_OutpostRange_Confirm".Translate()))
                ConfirmAndClose();
        }

        private void DrawBody(Rect area)
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
            float max = turret.GetConfiguredMaxRangeTiles();
            float min = Mathf.Min(WorldObject_AT_Turret.MinRangeTiles, max);
            range = Mathf.Clamp(range, min, max);
            Rect sliderRow = new Rect(area.x, y, contentW, SliderH);
            float next = DrawLabeledSliderRow(
                sliderRow,
                "TSA_WD_OutpostRange_Title_AtTurret".Translate(),
                range, min, max, 1f);
            if (!Mathf.Approximately(next, range))
            {
                range = next;
                PushPreview();
            }
        }

        private void Reset()
        {
            defenseMask = MissionMask.Raider | MissionMask.Expansion;
            raidTargetMask = RaidTargetMask.Player | RaidTargetMask.Allies;
            range = turret.GetConfiguredMaxRangeTiles();
            PushPreview();
        }

        private void ConfirmAndClose()
        {
            if (turret == null || turret.Destroyed)
            {
                Close();
                return;
            }

            turret.SetDefenseActive(defenseMask != MissionMask.None);
            turret.SetDefenseMask(defenseMask == MissionMask.None
                ? MissionMask.Raider | MissionMask.Expansion
                : defenseMask);
            turret.SetRaidTargetMask(raidTargetMask);
            turret.SetRangeOverride(range);
            Close();
        }

        private void PushPreview()
        {
            previewTurret = turret;
            previewRadius = range;
        }

        private static void DrawSectionHeader(Rect rect, string text)
        {
            GUI.color = SettingsUI.SectionHeaderColor;
            Widgets.Label(rect, text);
            GUI.color = Color.white;
        }

        private static float DrawLabeledSliderRow(Rect row, string label, float val, float min, float max, float step)
        {
            Widgets.Label(row.LeftPart(0.5f), $"{label}: {val.ToString("F0").Colorize(Color.cyan)}");
            return Widgets.HorizontalSlider(row.RightPart(0.5f), val, min, max, false, null, null, null, step);
        }

        private void DrawMaskRow(Rect rect, string label, MissionMask bit)
        {
            bool value = (defenseMask & bit) != 0;
            Widgets.CheckboxLabeled(rect, label, ref value);
            if (value) defenseMask |= bit;
            else defenseMask &= ~bit;
        }

        private void DrawRaidRow(Rect rect, string label, RaidTargetMask bit)
        {
            bool value = (raidTargetMask & bit) != 0;
            Widgets.CheckboxLabeled(rect, label, ref value);
            if (value) raidTargetMask |= bit;
            else raidTargetMask &= ~bit;
        }
    }
}
