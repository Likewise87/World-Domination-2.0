using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Shared chrome for offensive-strength withdraw / defense deploy pickers (GiftDeal-aligned, no fill bars).</summary>
    internal static class OutpostStrengthBudgetUi
    {
        public const float RowHeight = 36f;
        public const float DeployRowHeight = 60f;
        public const float RowIconSize = 28f;
        public const float DeployPortraitSize = 44f;
        public const float HeaderHeight = 30f;
        public const float ColIcon = 40f;
        public const float ColIconDeploy = 52f;
        public const float ColStrength = 72f;
        /// <summary>Wider strength column on the defense deploy picker only.</summary>
        public const float ColStrengthDeploy = 102f;
        public const float ColGear = 220f;
        public const float ColShoot = 52f;
        public const float ColMelee = 52f;
        public const float ColHealth = 58f;
        public const float ColAction = 120f;
        public const float BottomH = 48f;
        public const float BoxLineH = 24f;
        public const float MeterH = 16f;
        public const float MeterGap = 6f;

        public static readonly Color SelectedTint = new Color(0.45f, 0.85f, 0.5f);
        public static readonly Color LostTint = new Color(0.95f, 0.45f, 0.4f);
        public static readonly Color BannerAmber = new Color(1f, 0.75f, 0.35f);
        /// <summary>Matches MainTabWindow_WorldDomination status / nav chrome fill.</summary>
        private static readonly Color CarbonBoxFill = new Color(1f, 1f, 1f, 0.04f);
        /// <summary>Matches MainTabWindow_WorldDomination nav button outline.</summary>
        private static readonly Color CarbonBoxBorder = new Color(0.55f, 0.62f, 0.72f, 0.42f);

        public static float DeployFixedColumnsWidth =>
            ColIconDeploy + ColGear + ColShoot + ColMelee + ColHealth + ColStrengthDeploy + ColAction;

        public static void LabelAnchored(Rect rect, string text, TextAnchor anchor)
        {
            TextAnchor prev = Text.Anchor;
            Text.Anchor = anchor;
            Widgets.Label(rect, text);
            Text.Anchor = prev;
        }

        /// <summary>Framed meter box (text only). Returns y below the box.</summary>
        public static float DrawMeterBox(float x, float y, float width, string meterLine, Color meterColor, string? tipLine = null, string tipKey = "TSA_WD_StrengthBudget_MeterTip")
        {
            const float boxPad = Outpost_Dialog_UI.OutcomeBoxPad;
            bool hasTip = !tipLine.NullOrEmpty();
            float innerH = BoxLineH + (hasTip ? BoxLineH : 0f);
            float boxH = boxPad * 2f + innerH;
            Rect boxRect = new Rect(x, y, width, boxH);
            Widgets.DrawBoxSolid(boxRect, CarbonBoxFill);
            Color prev = GUI.color;
            GUI.color = CarbonBoxBorder;
            Widgets.DrawBox(boxRect, 1);
            GUI.color = prev;

            float cy = y + boxPad;
            float ix = x + boxPad;
            float iw = width - boxPad * 2f;

            GUI.color = meterColor;
            LabelAnchored(new Rect(ix, cy, iw, BoxLineH), meterLine, TextAnchor.MiddleCenter);
            GUI.color = prev;
            TooltipHandler.TipRegion(new Rect(ix, cy, iw, BoxLineH), tipKey.Translate());
            cy += BoxLineH;

            if (hasTip)
            {
                GUI.color = ColorLibrary.RedReadable;
                Text.Font = GameFont.Tiny;
                LabelAnchored(new Rect(ix, cy, iw, BoxLineH), tipLine, TextAnchor.MiddleCenter);
                Text.Font = GameFont.Small;
                GUI.color = prev;
            }

            return y + boxH;
        }

        /// <summary>
        /// Withdraw-only meter: current offense sits at 2/3 bar width so over-budget fill has room.
        /// Excess line (if any) is drawn under the bar inside the box.
        /// </summary>
        public static float DrawWithdrawMeterBox(float x, float y, float width, float used, float available, string? tipLine = null)
        {
            const float boxPad = Outpost_Dialog_UI.OutcomeBoxPad;
            const float barInset = 4f;
            const float barH = 20f;
            bool hasTip = !tipLine.NullOrEmpty();
            float innerH = barH + (hasTip ? MeterGap + BoxLineH : 0f);
            float boxH = boxPad * 2f + innerH;
            Rect boxRect = new Rect(x, y, width, boxH);
            Widgets.DrawBoxSolid(boxRect, CarbonBoxFill);
            Color prev = GUI.color;
            GUI.color = CarbonBoxBorder;
            Widgets.DrawBox(boxRect, 1);
            GUI.color = prev;

            float cy = y + boxPad;
            float meterX = x + barInset;
            float meterW = Mathf.Max(8f, width - barInset * 2f);

            float effectiveLimit = Mathf.Max(0f, OutpostStrengthBudget.GetWithdrawEffectiveLimit(available));
            float tolerance = Mathf.Max(0f, effectiveLimit - Mathf.Max(0f, available));
            // Keep the current-offense target near two-thirds of the bar, not jammed against the right edge.
            float barMax = available > 0f ? available / (2f / 3f) : 1f;
            float fillT = barMax > 0f ? Mathf.Clamp01(used / barMax) : 0f;
            Rect meterBg = new Rect(meterX, cy, meterW, barH);
            Widgets.DrawBoxSolid(meterBg, new Color(0.15f, 0.15f, 0.15f));
            Color fillColor = used <= available
                ? ColorLibrary.LightGreen
                : (used <= effectiveLimit ? BannerAmber : ColorLibrary.RedReadable);
            Widgets.DrawBoxSolid(new Rect(meterBg.x, meterBg.y, meterBg.width * fillT, meterBg.height), fillColor);
            float rawMarkerT = barMax > 0f ? Mathf.Clamp01(available / barMax) : 0f;
            float markerX = meterBg.x + meterBg.width * rawMarkerT;
            Widgets.DrawBoxSolid(new Rect(markerX - 1f, meterBg.y - 2f, 2f, meterBg.height + 4f), Color.white);
            TooltipHandler.TipRegion(
                meterBg,
                "TSA_WD_StrengthBudget_WithdrawMeterTip".Translate(
                    available.ToString("F0"),
                    effectiveLimit.ToString("F0"),
                    tolerance.ToString("F0")));
            cy += barH;

            if (hasTip)
            {
                cy += MeterGap;
                float ix = x + boxPad;
                float iw = width - boxPad * 2f;
                GUI.color = ColorLibrary.RedReadable;
                Text.Font = GameFont.Tiny;
                LabelAnchored(new Rect(ix, cy, iw, BoxLineH), tipLine, TextAnchor.MiddleCenter);
                Text.Font = GameFont.Small;
                GUI.color = prev;
            }

            return y + boxH;
        }

        public static void DrawTableHeader(Rect hRect, float actionColW, bool includeGear = false)
        {
            float colIcon = includeGear ? ColIconDeploy : ColIcon;
            float gearW = includeGear ? ColGear : 0f;
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            float curX = hRect.x + colIcon;
            float nameW = Mathf.Max(80f, hRect.width - colIcon - ColStrength - gearW - actionColW);
            LabelAnchored(new Rect(curX, hRect.y, nameW, hRect.height),
                "TSA_WD_StrengthBudget_ColName".Translate(), TextAnchor.MiddleLeft);
            curX += nameW;
            if (includeGear)
            {
                LabelAnchored(new Rect(curX, hRect.y, ColGear, hRect.height),
                    "TSA_WD_StrengthBudget_ColGear".Translate(), TextAnchor.MiddleLeft);
                curX += ColGear;
            }
            LabelAnchored(new Rect(curX, hRect.y, ColStrength, hRect.height),
                "TSA_WD_StrengthBudget_ColStrength".Translate(), TextAnchor.MiddleCenter);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        public static void DrawDeployTableHeader(Rect hRect)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            float curX = hRect.x + ColIconDeploy;
            float nameW = Mathf.Max(80f, hRect.width - DeployFixedColumnsWidth);
            LabelAnchored(new Rect(curX, hRect.y, nameW, hRect.height),
                "TSA_WD_StrengthBudget_ColName".Translate(), TextAnchor.MiddleLeft);
            curX += nameW;
            LabelAnchored(new Rect(curX, hRect.y, ColGear, hRect.height),
                "TSA_WD_StrengthBudget_ColGear".Translate(), TextAnchor.MiddleLeft);
            curX += ColGear;
            LabelAnchored(new Rect(curX, hRect.y, ColShoot, hRect.height),
                "TSA_WD_StrengthBudget_ColShooting".Translate(), TextAnchor.MiddleCenter);
            curX += ColShoot;
            LabelAnchored(new Rect(curX, hRect.y, ColMelee, hRect.height),
                "TSA_WD_StrengthBudget_ColMelee".Translate(), TextAnchor.MiddleCenter);
            curX += ColMelee;
            LabelAnchored(new Rect(curX, hRect.y, ColHealth, hRect.height),
                "TSA_WD_StrengthBudget_ColHealth".Translate(), TextAnchor.MiddleCenter);
            curX += ColHealth;
            LabelAnchored(new Rect(curX, hRect.y, ColStrengthDeploy, hRect.height),
                "TSA_WD_StrengthBudget_ColStrength".Translate(), TextAnchor.MiddleCenter);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        public static void DrawPawnPortrait(Rect iconRect, Pawn pawn)
        {
            if (pawn == null) return;
            Texture portrait = PawnPortraitUIUtils.GetPortrait(pawn, new Vector2(iconRect.width, iconRect.height));
            if (portrait != null)
                GUI.DrawTexture(iconRect, portrait, ScaleMode.ScaleToFit);
            else
                Widgets.ThingIcon(iconRect, pawn);
        }

        public static void DrawRowHover(Rect rowRect)
        {
            if (Mouse.IsOver(rowRect)) Widgets.DrawLightHighlight(rowRect);
        }

        /// <summary>Thin 1px selected outline (lighter than standard list selection box).</summary>
        public static void FinishDeploySelectedRow(Rect rowRect, bool isSelected)
        {
            DrawRowHover(rowRect);
            if (!isSelected) return;
            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Widgets.DrawLineHorizontal(rowRect.x, rowRect.y, rowRect.width);
            Widgets.DrawLineHorizontal(rowRect.x, rowRect.yMax - 1f, rowRect.width);
            Widgets.DrawLineVertical(rowRect.x, rowRect.y, rowRect.height);
            Widgets.DrawLineVertical(rowRect.xMax - 1f, rowRect.y, rowRect.height);
            GUI.color = prev;
        }

        public static int GetSkillLevel(Pawn pawn, SkillDef skill)
        {
            if (pawn?.skills == null || skill == null) return 0;
            return pawn.skills.GetSkill(skill)?.Level ?? 0;
        }

        public static float GetHealthPercent(Pawn pawn)
        {
            if (pawn?.health?.summaryHealth == null) return 0f;
            return Mathf.Clamp01(pawn.health.summaryHealth.SummaryHealthPercent) * 100f;
        }

        public static string GetRelevantCombatSkillLine(Pawn pawn)
        {
            ThingWithComps primary = pawn?.equipment?.Primary;
            if (primary == null)
                return "TSA_WD_OutpostDefense_Unarmed".Translate();

            SkillDef skill = primary.def != null && primary.def.IsRangedWeapon
                ? SkillDefOf.Shooting
                : SkillDefOf.Melee;
            int level = GetSkillLevel(pawn, skill);
            return skill.LabelCap + ": " + level;
        }

        /// <summary>Draws equipment + apparel icons. Returns true if an item info card was opened.</summary>
        public static bool DrawEquippedItemIcons(Rect rect, Pawn pawn)
        {
            const float iconSize = 26f;
            const float gap = 3f;
            float x = rect.x;
            float y = rect.y + Mathf.Max(0f, (rect.height - iconSize) / 2f);
            int drawn = 0;
            bool clicked = false;

            if (pawn?.equipment?.AllEquipmentListForReading != null)
            {
                List<ThingWithComps> equipment = pawn.equipment.AllEquipmentListForReading;
                for (int i = 0; i < equipment.Count; i++)
                {
                    if (!TryDrawThingIcon(ref x, ref y, rect, iconSize, gap, equipment[i], ref drawn, ref clicked))
                        return clicked;
                }
            }

            if (pawn?.apparel?.WornApparel != null)
            {
                List<Apparel> apparel = pawn.apparel.WornApparel;
                for (int i = 0; i < apparel.Count; i++)
                {
                    if (!TryDrawThingIcon(ref x, ref y, rect, iconSize, gap, apparel[i], ref drawn, ref clicked))
                        return clicked;
                }
            }

            if (drawn == 0)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                LabelAnchored(rect, "TSA_WD_OutpostDefense_NoEquipmentIcons".Translate(), TextAnchor.MiddleLeft);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }

            return clicked;
        }

        private static bool TryDrawThingIcon(ref float x, ref float y, Rect bounds, float iconSize, float gap, Thing thing, ref int drawn, ref bool clicked)
        {
            if (thing == null || thing.Destroyed) return true;
            if (x + iconSize > bounds.xMax)
            {
                x = bounds.x;
                y += iconSize + gap;
            }
            if (y + iconSize > bounds.yMax)
            {
                LabelAnchored(new Rect(x, bounds.y, bounds.xMax - x, bounds.height), "...", TextAnchor.MiddleLeft);
                return false;
            }

            Rect icon = new Rect(x, y, iconSize, iconSize);
            Widgets.ThingIcon(icon, thing);
            TooltipHandler.TipRegion(icon, thing.LabelCap);
            if (Widgets.ButtonInvisible(icon))
            {
                Find.WindowStack.Add(new Dialog_InfoCard(thing));
                clicked = true;
            }
            x += iconSize + gap;
            drawn++;
            return true;
        }
    }
}
