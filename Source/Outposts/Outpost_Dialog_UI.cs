using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Shared layout helpers for outpost production / recruiting / trading dialogs.</summary>
    internal static class Outpost_Dialog_UI
    {
        public const float OutcomeLineH = 26f;
        public const float OutcomeBoxPad = 8f;
        public const float OutcomeBoxGap = 10f;
        public const float YieldLineH = 24f;
        public const float OutcomeValueIndent = 10f;
        public const float AfterSnapshotGap = 12f;

        /// <summary>Medium-font headline row (e.g. "Academy Teaching"); 26px clipped descenders.</summary>
        public const float DialogTitleHeight = 30f;
        public const float DialogTitleRowAdvance = 32f;
        public const float DialogHeaderSlotHeight = 26f;
        public const float DialogHeaderSlotTopInset = 2f;

        /// <summary>Matches outpost production timer cadence; avoids rescanning every GUI frame.</summary>
        public const int LiveRefreshIntervalTicks = 2500;

        public const float PauseHeaderHeight = 20f;
        public const float PauseReasonLineHeight = 21f;
        public const float AfterPauseBannerGap = 4f;

        /// <summary>Right-side selectable list rows: item name line height.</summary>
        public const float ListRowNameHeight = 26f;
        /// <summary>Right-side selectable list rows: formula / quantity line height (Tiny font).</summary>
        public const float ListRowFormulaLineHeight = 18f;
        /// <summary>Negative offset pulls formula line toward name (−6 ≈ 3px tighter than −3).</summary>
        public const float ListRowFormulaTopPadding = -6f;
        /// <summary>Vertical space reserved below the name for the formula line.</summary>
        public const float ListRowFormulaBlockHeight = 17f;

        public static readonly Color CycleTimerColor = new Color(0.75f, 0.82f, 1f);
        public static readonly Color TheoreticalLabelColor = new Color(0.72f, 0.72f, 0.72f);
        public static readonly Color OutcomeValueColor = new Color(0.4f, 0.8f, 1f);
        public static readonly Color RowBgSelected = new Color(0.4f, 0.8f, 1f, 0.12f);
        public static readonly Color RowBgRequirementsUnmet = new Color(0.95f, 0.45f, 0.45f, 0.16f);

        /// <summary>Blue tint behind a selected list row (draw before row content).</summary>
        public static void DrawSelectedRowTint(Rect rowRect, bool isSelected)
        {
            if (!isSelected) return;
            GUI.color = RowBgSelected;
            GUI.DrawTexture(rowRect, BaseContent.WhiteTex);
            GUI.color = Color.white;
        }

        /// <summary>Light red tint when a row has unmet selection requirements (draw after zebra, before selected tint).</summary>
        public static void DrawUnmetRequirementsRowTint(Rect rowRect, bool requirementsUnmet)
        {
            if (!requirementsUnmet) return;
            GUI.color = RowBgRequirementsUnmet;
            GUI.DrawTexture(rowRect, BaseContent.WhiteTex);
            GUI.color = Color.white;
        }

        /// <summary>Hover highlight and white border for a selected list row (draw after row content).</summary>
        public static void FinishSelectableListRow(Rect rowRect, bool isSelected)
        {
            if (Mouse.IsOver(rowRect)) Widgets.DrawHighlight(rowRect);
            if (!isSelected) return;
            GUI.color = Color.white;
            Widgets.DrawBox(rowRect, 1);
            GUI.color = Color.white;
        }

        public static void DrawOutcomeBox(Rect rect) => Widgets.DrawMenuSection(rect);

        /// <summary>Yellow production-paused header + bullet reasons. Returns y below the block (unchanged if not paused).</summary>
        public static float DrawProductionPauseBanner(float x, float y, float width, WorldObject_WD_Outpost outpost)
        {
            var pauseReasons = outpost?.GetProductionPauseReasons();
            if (pauseReasons == null || pauseReasons.Count == 0)
                return y;

            GUI.color = Color.yellow;
            Widgets.Label(new Rect(x, y, width, PauseHeaderHeight), OutpostTranslationUtil.Key("TSA_WD_Production_PausedHeader"));
            y += PauseHeaderHeight;
            for (int i = 0; i < pauseReasons.Count; i++)
            {
                string r = pauseReasons[i];
                if (!string.IsNullOrEmpty(r))
                    Widgets.Label(new Rect(x + 12f, y, width - 12f, PauseReasonLineHeight), "• " + r);
                y += PauseReasonLineHeight;
            }
            GUI.color = Color.white;
            return y + 6f;
        }

        public static readonly Color SkillDrBoxYellow = new Color(0.25f, 0.2f, 0.05f, 0.42f);
        public static readonly Color SkillDrBoxRed = new Color(0.35f, 0.08f, 0.08f, 0.45f);

        /// <summary>
        /// Fat warning box when cumulative skill is past the first full band. Yellow in soft bands, red at hard cap.
        /// Returns y below the box (unchanged if not shown).
        /// </summary>
        public static float DrawSkillDiminishingReturnsBanner(float x, float y, float width, WorldObject_WD_Outpost outpost)
        {
            float raw = OutpostSkillScaling.GetBannerRawSkill(outpost);
            return DrawSkillDiminishingReturnsBanner(x, y, width, raw);
        }

        public static float DrawSkillDiminishingReturnsBanner(float x, float y, float width, float rawSkill)
        {
            if (!OutpostSkillScaling.IsDiminished(rawSkill))
                return y;

            float eff = OutpostSkillScaling.ToEffective(rawSkill);
            bool hard = OutpostSkillScaling.IsAtOrAboveHardCap(rawSkill);
            string text = hard
                ? "TSA_WD_SkillScaling_BannerHardCap".Translate(rawSkill.ToString("F0"), eff.ToString("F0")).ToString()
                : "TSA_WD_SkillScaling_BannerSoft".Translate(rawSkill.ToString("F0"), eff.ToString("F0")).ToString();

            Text.Font = GameFont.Small;
            float textH = Mathf.Max(24f, Text.CalcHeight(text, width - 12f));
            float boxH = textH + 12f;
            Rect boxRect = new Rect(x, y, width, boxH);
            Widgets.DrawBoxSolid(boxRect, hard ? SkillDrBoxRed : SkillDrBoxYellow);
            Widgets.DrawBox(boxRect);
            GUI.color = hard ? new Color(1f, 0.45f, 0.45f) : Color.yellow;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(boxRect.ContractedBy(6f), text);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            TooltipHandler.TipRegion(boxRect, OutpostSkillScaling.BuildBandBreakdownTip(rawSkill));
            return y + boxH + 6f;
        }

        public static Color NearbyCountColor(int count)
        {
            if (count == 0) return Color.red;
            if (count <= 2) return Color.yellow;
            return Color.green;
        }

        public static string FormatNearbyHeaderLabel(int count)
            => OutpostTranslationUtil.Key("TSA_WD_Biome_ColTradingNearby") + ": " + count;

        public static float MeasureYieldLinesHeight(List<ThingDefCountClass> items, string fallbackText)
        {
            if (items != null && items.Count > 0)
            {
                int n = 0;
                for (int i = 0; i < items.Count; i++)
                    if (items[i]?.thingDef != null) n++;
                return n > 0 ? n * YieldLineH : YieldLineH;
            }
            return MeasureTextLinesHeight(fallbackText);
        }

        public static float MeasureTextLinesHeight(string text)
        {
            if (string.IsNullOrEmpty(text)) return YieldLineH;
            return text.Split('\n').Length * YieldLineH;
        }

        /// <summary>Item rows or fallback text at production yield styling (Small font, 24px rows).</summary>
        public static float DrawOutcomeLines(float x, float y, float w, List<ThingDefCountClass> items, string fallbackText, Color color)
        {
            Text.Font = GameFont.Small;
            if (items != null && items.Count > 0)
            {
                foreach (var tc in items)
                {
                    if (tc?.thingDef == null) continue;
                    GUI.color = color;
                    Widgets.Label(new Rect(x, y, w, YieldLineH), tc.count + " " + tc.thingDef.LabelCap);
                    GUI.color = Color.white;
                    y += YieldLineH;
                }
                return y;
            }
            return DrawTextOutcomeLines(x, y, w, fallbackText, color);
        }

        /// <summary>Single- or multi-line outcome text at production yield styling.</summary>
        public static float DrawTextOutcomeLines(float x, float y, float w, string text, Color color)
        {
            Text.Font = GameFont.Small;
            string display = string.IsNullOrEmpty(text) ? "—" : text;
            foreach (var part in display.Split('\n'))
            {
                GUI.color = color;
                Widgets.Label(new Rect(x, y, w, YieldLineH), part);
                GUI.color = Color.white;
                y += YieldLineH;
            }
            return y;
        }
    }

    /// <summary>Throttled refresh of pause state and nearby settlement data while a dialog is open.</summary>
    internal sealed class OutpostDialogNearbyMonitor
    {
        private int lastRefreshTick = -1;
        private int lastWorldObjectCount = -1;

        public int NearbyCount { get; private set; }

        /// <summary>Recomputes pause cache and nearby count at most once per interval, or immediately when the world object count changes.</summary>
        public bool TryRefresh(WorldObject_WD_Outpost outpost, Action onPartnersChanged = null)
        {
            if (outpost == null) return false;

            int tick = Find.TickManager.TicksGame;
            int worldObjectCount = Find.WorldObjects.AllWorldObjects.Count;
            if (lastRefreshTick >= 0
                && tick - lastRefreshTick < Outpost_Dialog_UI.LiveRefreshIntervalTicks
                && worldObjectCount == lastWorldObjectCount)
                return false;

            lastRefreshTick = tick;
            lastWorldObjectCount = worldObjectCount;

            Outpost_EstablishmentRequirements.InvalidateNearbyCountCache();
            Outpost_Trading.InvalidateTradingRadiusProbeCache(outpost);
            outpost.RecomputeProductionRequirementCache();
            NearbyCount = Outpost_Trading.GetNearbySettlementCount(outpost);
            onPartnersChanged?.Invoke();
            return true;
        }

        public void ForceRefresh(WorldObject_WD_Outpost outpost, Action onPartnersChanged = null)
        {
            lastRefreshTick = -1;
            lastWorldObjectCount = -1;
            TryRefresh(outpost, onPartnersChanged);
        }
    }
}
