using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Shared IMGUI helpers for <see cref="WITab_OutpostAndSettlement_Stats"/> and related previews.</summary>
    public static class OutpostTabStatsUi
    {
        public const float TabHeaderConsumedHeight = 38f;
        /// <summary>Width of the vertical scrollbar strip. Content view width = outer − this (flush table, no dead gap).</summary>
        public const float ScrollbarRightPadding = 16f;
        /// <summary>Padding between right-aligned values and the row's right edge (inside the table, before the scrollbar).</summary>
        private const float ContentRightInset = 12f;
        private const float SectionGap = 12f;
        private const float SectionColumnGap = 16f;
        private const float SectionHeaderHeight = 30f;
        private const float RowHeight = 24f;

        /// <summary>
        /// Rebuild <paramref name="into"/> with sections/rows matching <paramref name="needleLower"/>
        /// (empty needle copies all sections by reference). Matching is case-insensitive on section title,
        /// row label, or row value.
        /// </summary>
        public static void FillFilteredSections(
            IList<OutpostStatsSection> source,
            string needleLower,
            List<OutpostStatsSection> into)
        {
            into?.Clear();
            if (into == null || source == null) return;

            bool filter = !string.IsNullOrEmpty(needleLower);
            for (int i = 0; i < source.Count; i++)
            {
                OutpostStatsSection section = source[i];
                if (section == null) continue;

                if (!filter)
                {
                    into.Add(section);
                    continue;
                }

                bool titleMatch = !string.IsNullOrEmpty(section.Title)
                    && section.Title.ToLowerInvariant().Contains(needleLower);
                if (titleMatch)
                {
                    into.Add(section);
                    continue;
                }

                if (section.Rows == null || section.Rows.Count == 0)
                    continue;

                OutpostStatsSection filtered = null;
                for (int r = 0; r < section.Rows.Count; r++)
                {
                    OutpostStatRow row = section.Rows[r];
                    if (row == null) continue;
                    if (!RowMatches(row, needleLower)) continue;
                    filtered ??= new OutpostStatsSection
                    {
                        Title = section.Title,
                        FullWidth = section.FullWidth
                    };
                    filtered.Rows.Add(row);
                }
                if (filtered != null)
                    into.Add(filtered);
            }
        }

        private static bool RowMatches(OutpostStatRow row, string needleLower)
        {
            if (!string.IsNullOrEmpty(row.Label)
                && row.Label.ToLowerInvariant().Contains(needleLower))
                return true;
            if (!string.IsNullOrEmpty(row.Value)
                && row.Value.ToLowerInvariant().Contains(needleLower))
                return true;
            return false;
        }

        public static void DrawHeadline(Rect body, string headline)
        {
            Text.Font = GameFont.Medium;
            LabelAnchored(new Rect(body.x, body.y, body.width, 30f), headline, TextAnchor.MiddleLeft);
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(body.x, body.y + 32f, body.width);
        }

        public static float DrawSectionHeader(float x, float y, float width, string title)
        {
            Text.Font = GameFont.Small;
            GUI.color = Widgets.SeparatorLabelColor;
            Widgets.Label(new Rect(x, y, width, SectionHeaderHeight), title);
            GUI.color = Color.white;
            float lineY = y + SectionHeaderHeight;
            Widgets.DrawLineHorizontal(x, lineY, width);
            return lineY + 4f;
        }

        public static float MeasureKeyValueRowsHeight(float width, OutpostStatsSection section, bool truncateText = true)
        {
            if (section?.Rows == null) return 0f;
            float h = 0f;
            for (int i = 0; i < section.Rows.Count; i++)
                h += GetRowHeight(width, section.Rows[i], truncateText);
            return h;
        }

        private const float RowIconSize = 18f;
        private const float RowIconGap = 4f;

        public static float DrawKeyValueRows(
            float x, float y, float width, OutpostStatsSection section,
            bool zebraStriping = true, bool truncateText = true)
        {
            if (section?.Rows == null) return y;
            const float labelFrac = 0.48f;
            // Zebra / hit area spans the full content width (meets the scrollbar). Value text keeps ContentRightInset padding.
            float rowW = width;
            bool prevWrap = Text.WordWrap;
            for (int i = 0; i < section.Rows.Count; i++)
            {
                OutpostStatRow row = section.Rows[i];
                float labelW = rowW * labelFrac;
                float iconPad = MeasureLabelIconPad(row);
                Rect labelRect = new Rect(x + 4f + iconPad, y, Mathf.Max(8f, labelW - 8f - iconPad), RowHeight);
                Rect valueRect = new Rect(x + labelW, y, Mathf.Max(8f, rowW - labelW - ContentRightInset), RowHeight);
                bool wrap = !truncateText || row.WrapValue;
                float rowH = GetRowHeight(rowW, row, truncateText);
                if (wrap)
                {
                    labelRect.height = rowH;
                    valueRect.height = rowH;
                }

                Rect rowRect = new Rect(x, y, rowW, rowH);
                if (zebraStriping && i % 2 == 0) Widgets.DrawHighlight(rowRect);

                DrawRowLabelIcons(x + 4f, y, rowH, row);

                Text.Anchor = wrap ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
                bool statusBanner = row.LabelColor.HasValue && string.IsNullOrWhiteSpace(row.Value);
                Color prev = GUI.color;
                if (row.LabelColor.HasValue) GUI.color = row.LabelColor.Value;
                if (statusBanner)
                {
                    // Full-width yellow (or other) status line; skip empty value column / em-dash fallback.
                    Rect bannerRect = new Rect(x + 4f + iconPad, y, Mathf.Max(8f, rowW - ContentRightInset - 4f - iconPad), rowH);
                    if (wrap)
                    {
                        Text.WordWrap = true;
                        Widgets.Label(bannerRect, row.Label ?? "");
                    }
                    else
                        Widgets.Label(bannerRect, (row.Label ?? "").Truncate(bannerRect.width));
                }
                else
                {
                    if (wrap)
                    {
                        Text.WordWrap = true;
                        Widgets.Label(labelRect, row.Label ?? "");
                    }
                    else
                        Widgets.Label(labelRect, (row.Label ?? "").Truncate(labelRect.width));
                    GUI.color = prev;
                    if (row.ValueColor.HasValue) GUI.color = row.ValueColor.Value;
                    if (wrap)
                    {
                        Text.WordWrap = true;
                        Text.Anchor = TextAnchor.UpperRight;
                        Widgets.Label(valueRect, row.Value ?? "—");
                    }
                    else
                    {
                        Text.Anchor = TextAnchor.MiddleRight;
                        Widgets.Label(valueRect, (row.Value ?? "—").Truncate(valueRect.width));
                    }
                }
                GUI.color = prev;
                Text.Anchor = TextAnchor.UpperLeft;
                if (!string.IsNullOrEmpty(row.Tooltip))
                    TooltipHandler.TipRegion(rowRect, row.Tooltip);
                y += rowH;
            }
            Text.WordWrap = prevWrap;
            return y;
        }

        private static float MeasureLabelIconPad(OutpostStatRow row)
        {
            if (row == null) return 0f;
            float pad = 0f;
            if (row.LabelIcon != null)
                pad += RowIconSize + RowIconGap;
            if (row.LabelFaction?.def?.FactionIcon != null)
                pad += RowIconSize + RowIconGap;
            return pad;
        }

        private static void DrawRowLabelIcons(float x, float y, float rowH, OutpostStatRow row)
        {
            if (row == null) return;
            float ix = x;
            float iy = y + (rowH - RowIconSize) * 0.5f;
            if (row.LabelIcon != null)
            {
                Rect r = new Rect(ix, iy, RowIconSize, RowIconSize);
                GUI.DrawTexture(r, row.LabelIcon, ScaleMode.ScaleToFit, true, 0f, row.LabelIconColor, 0f, 0f);
                ix += RowIconSize + RowIconGap;
            }
            if (row.LabelFaction?.def?.FactionIcon != null)
            {
                Rect r = new Rect(ix, iy, RowIconSize, RowIconSize);
                WorldDomination_UIUtils.DrawFactionIconWithColor(r, row.LabelFaction);
            }
        }

        private static float GetRowHeight(float rowW, OutpostStatRow row, bool truncateText = true)
        {
            if (row == null) return RowHeight;
            if (truncateText && !row.WrapValue)
                return RowHeight;

            const float labelFrac = 0.48f;
            float labelW = rowW * labelFrac - 8f - MeasureLabelIconPad(row);
            float valueW = rowW - rowW * labelFrac - ContentRightInset;
            bool prevWrap = Text.WordWrap;
            Text.WordWrap = true;
            float labelH = Text.CalcHeight(row.Label ?? "", labelW);
            float valueH = Text.CalcHeight(row.Value ?? "—", valueW);
            Text.WordWrap = prevWrap;
            return Mathf.Max(RowHeight, labelH, valueH);
        }

        /// <summary>Blue selected tint + white outline (ordered road/trader preview cost box).</summary>
        public static float DrawHighlightedKeyValueSection(float x, float y, float width, OutpostStatsSection section)
        {
            const float boxPad = 6f;
            float contentW = width - boxPad * 2f;
            float contentH = MeasureKeyValueRowsHeight(contentW, section);
            Rect box = new Rect(x, y, width, contentH + boxPad * 2f);
            Outpost_Dialog_UI.DrawSelectedRowTint(box, true);
            GUI.color = Color.white;
            Widgets.DrawBox(box, 1);
            DrawKeyValueRows(x + boxPad, y + boxPad, contentW, section, zebraStriping: false);
            return box.yMax;
        }

        public static float DrawSectionColumn(
            float x, float y, float width, OutpostStatsSection section, bool truncateText = true)
        {
            if (section == null) return y;
            if (!string.IsNullOrEmpty(section.Title))
                y = DrawSectionHeader(x, y, width, section.Title);
            return DrawKeyValueRows(x, y, width, section, truncateText: truncateText);
        }

        /// <summary>Sections with <see cref="OutpostStatsSection.FullWidth"/> span the row; others are paired in two columns.</summary>
        public static float DrawStatsLayout(float x, float y, float width, IList<OutpostStatsSection> sections)
        {
            if (sections == null || sections.Count == 0) return y;

            int i = 0;
            while (i < sections.Count)
            {
                if (i > 0) y += SectionGap;

                if (sections[i].FullWidth)
                {
                    y = DrawSectionColumn(x, y, width, sections[i]);
                    i++;
                    continue;
                }

                float colW = (width - SectionColumnGap) * 0.5f;
                float rowStart = y;
                float leftEnd = DrawSectionColumn(x, rowStart, colW, sections[i]);
                float rightEnd = rowStart;
                i++;
                if (i < sections.Count && !sections[i].FullWidth)
                {
                    rightEnd = DrawSectionColumn(x + colW + SectionColumnGap, rowStart, colW, sections[i]);
                    i++;
                }
                y = Mathf.Max(leftEnd, rightEnd);
            }

            return y;
        }

        public static float MeasureSectionHeight(OutpostStatsSection section)
        {
            if (section?.Rows == null)
                return string.IsNullOrEmpty(section?.Title) ? 0f : SectionHeaderHeight + 4f;
            float h = string.IsNullOrEmpty(section.Title) ? 0f : SectionHeaderHeight + 4f;
            return h + section.Rows.Count * RowHeight;
        }

        public static float MeasureContentHeight(OutpostStatsSnapshot snap, float width) =>
            MeasureContentHeight(snap?.Sections, width);

        public static float MeasureContentHeight(IList<OutpostStatsSection> sections, float width)
        {
            if (sections == null || sections.Count == 0) return 200f;

            float h = 0f;
            int i = 0;
            while (i < sections.Count)
            {
                if (i > 0) h += SectionGap;

                if (sections[i].FullWidth)
                {
                    h += MeasureSectionHeight(sections[i]);
                    i++;
                    continue;
                }

                float leftH = MeasureSectionHeight(sections[i]);
                float rightH = 0f;
                i++;
                if (i < sections.Count && !sections[i].FullWidth)
                {
                    rightH = MeasureSectionHeight(sections[i]);
                    i++;
                }
                h += Mathf.Max(leftH, rightH);
            }

            return h + 16f;
        }

        private static void LabelAnchored(Rect rect, string text, TextAnchor anchor)
        {
            TextAnchor prev = Text.Anchor;
            Text.Anchor = anchor;
            Widgets.Label(rect, text);
            Text.Anchor = prev;
        }
    }
}
