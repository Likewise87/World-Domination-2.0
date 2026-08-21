using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Shared IMGUI helpers for <see cref="WITab_Outpost_Stats"/>.</summary>
    public static class OutpostTabStatsUi
    {
        public const float TabHeaderConsumedHeight = 38f;
        /// <summary>Space reserved on the right of scroll content so values do not sit under the scrollbar.</summary>
        public const float ScrollbarRightPadding = 32f;
        private const float ContentRightInset = 8f;
        private const float SectionGap = 12f;
        private const float SectionColumnGap = 16f;
        private const float SectionHeaderHeight = 30f;
        private const float RowHeight = 24f;

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
            Widgets.Label(new Rect(x, y, width - ContentRightInset, SectionHeaderHeight), title);
            GUI.color = Color.white;
            float lineY = y + SectionHeaderHeight;
            Widgets.DrawLineHorizontal(x, lineY, width - ContentRightInset);
            return lineY + 4f;
        }

        public static float MeasureKeyValueRowsHeight(float width, OutpostStatsSection section)
        {
            if (section?.Rows == null) return 0f;
            float rowW = width - ContentRightInset;
            float h = 0f;
            for (int i = 0; i < section.Rows.Count; i++)
                h += GetRowHeight(rowW, section.Rows[i]);
            return h;
        }

        public static float DrawKeyValueRows(float x, float y, float width, OutpostStatsSection section, bool zebraStriping = true)
        {
            if (section?.Rows == null) return y;
            const float labelFrac = 0.48f;
            float rowW = width - ContentRightInset;
            bool prevWrap = Text.WordWrap;
            for (int i = 0; i < section.Rows.Count; i++)
            {
                OutpostStatRow row = section.Rows[i];
                float labelW = rowW * labelFrac;
                Rect labelRect = new Rect(x + 4f, y, labelW - 8f, RowHeight);
                Rect valueRect = new Rect(x + labelW, y, rowW - labelW - 4f, RowHeight);
                float rowH = GetRowHeight(rowW, row);
                if (row.WrapValue)
                {
                    labelRect.height = rowH;
                    valueRect.height = rowH;
                }

                Rect rowRect = new Rect(x, y, rowW, rowH);
                if (zebraStriping && i % 2 == 0) Widgets.DrawHighlight(rowRect);

                Text.Anchor = row.WrapValue ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
                if (row.WrapValue)
                {
                    Text.WordWrap = true;
                    Widgets.Label(labelRect, row.Label ?? "");
                }
                else
                    Widgets.Label(labelRect, (row.Label ?? "").Truncate(labelRect.width));

                Color prev = GUI.color;
                if (row.ValueColor.HasValue) GUI.color = row.ValueColor.Value;
                if (row.WrapValue)
                {
                    Text.WordWrap = true;
                    Widgets.Label(valueRect, row.Value ?? "—");
                }
                else
                {
                    Text.Anchor = TextAnchor.MiddleRight;
                    Widgets.Label(valueRect, (row.Value ?? "—").Truncate(valueRect.width));
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

        private static float GetRowHeight(float rowW, OutpostStatRow row)
        {
            if (row == null || !row.WrapValue)
                return RowHeight;

            const float labelFrac = 0.48f;
            float labelW = rowW * labelFrac - 8f;
            float valueW = rowW - rowW * labelFrac - 4f;
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

        public static float DrawSectionColumn(float x, float y, float width, OutpostStatsSection section)
        {
            if (section == null) return y;
            if (!string.IsNullOrEmpty(section.Title))
                y = DrawSectionHeader(x, y, width, section.Title);
            return DrawKeyValueRows(x, y, width, section);
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
            if (section?.Rows == null) return SectionHeaderHeight + 4f;
            return SectionHeaderHeight + 4f + section.Rows.Count * RowHeight;
        }

        public static float MeasureContentHeight(OutpostStatsSnapshot snap, float width)
        {
            if (snap?.Sections == null || snap.Sections.Count == 0) return 200f;

            float h = 0f;
            int i = 0;
            while (i < snap.Sections.Count)
            {
                if (i > 0) h += SectionGap;

                if (snap.Sections[i].FullWidth)
                {
                    h += MeasureSectionHeight(snap.Sections[i]);
                    i++;
                    continue;
                }

                float leftH = MeasureSectionHeight(snap.Sections[i]);
                float rightH = 0f;
                i++;
                if (i < snap.Sections.Count && !snap.Sections[i].FullWidth)
                {
                    rightH = MeasureSectionHeight(snap.Sections[i]);
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
