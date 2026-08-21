using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    // Added Weight format
    public enum SliderFormat { Fixed0, Fixed1, Fixed2, Multiplier, Percent, PercentDecimal, Weight }

    public static class SettingsUI
    {
        public const float StandardGap = 12f;
        /// <summary>Height of one menu row (button + description) in the main settings hub. Fits ~3 lines of description text.</summary>
        public const float MenuRowHeight = 60f;
        /// <summary>Fraction of row width used for the button column (rest is description).</summary>
        public const float MenuButtonColumnFraction = 0.38f;
        /// <summary>Reserve this many pixels at the top of Window content so scrollable content never sits under the close button.</summary>
        public const float ReserveTopForCloseButton = 38f;
        /// <summary>Reserve this many pixels at the bottom of Window content so content is not drawn under the auto-generated close button at the bottom.</summary>
        public const float ReserveBottomForCloseButton = 44f;
        /// <summary>Height used when drawing our own window title (one line, same style as main settings). Use with DrawWindowTitle; then content starts below this.</summary>
        public const float WindowTitleHeight = 28f;

        /// <summary>Shared label for per-page reset buttons on settings sub-menus opened from the main hub.</summary>
        public static string ResetPageToDefaultsLabel => "TSA_WD_ResetPageToDefaults".Translate();

        /// <summary>Shared cyan used by collapsible section headers across settings dialogs.</summary>
        public static readonly Color SectionHeaderColor = new Color(0.55f, 0.85f, 1f);

        public static float LabeledSlider(Listing_Standard l, string label, float val, float min, float max, string tooltip = null, float step = 0.1f, SliderFormat format = SliderFormat.Fixed1, float? defaultValue = null, SliderFormat? defaultFormat = null)
        {
            l.Gap(2f);
            Rect r = l.GetRect(24f);
            if (!tooltip.NullOrEmpty()) TooltipHandler.TipRegion(r, TooltipWithDefault(tooltip, defaultValue, defaultFormat ?? format));

            string displaySuffix = GetFormattedValue(val, format);
            Widgets.Label(r.LeftPart(0.5f), $"{label}: {displaySuffix.Colorize(Color.cyan)}");
            return Widgets.HorizontalSlider(r.RightPart(0.5f), val, min, max, false, null, null, null, step);
        }

        /// <summary>Label on its own row (Small, 24px), slider on the next row. Optional left/right captions on the slider.</summary>
        public static float StackedSlider(
            Listing_Standard l,
            string label,
            float val,
            float min,
            float max,
            string tooltip = null,
            float step = 1f,
            SliderFormat format = SliderFormat.Fixed0,
            float? defaultValue = null,
            string leftCaption = null,
            string rightCaption = null)
        {
            l.Gap(2f);
            Rect labelRect = l.GetRect(24f);
            string displaySuffix = GetFormattedValue(val, format);
            Widgets.Label(labelRect, $"{label}: {displaySuffix.Colorize(Color.cyan)}");

            Rect sliderRect = l.GetRect(22f);
            string tip = tooltip.NullOrEmpty() ? null : TooltipWithDefault(tooltip, defaultValue, format);
            if (!tip.NullOrEmpty())
            {
                TooltipHandler.TipRegion(labelRect, tip);
                TooltipHandler.TipRegion(sliderRect, tip);
            }

            return Widgets.HorizontalSlider(sliderRect, val, min, max, false, null, leftCaption, rightCaption, step);
        }

        public static float WeightSlider(Listing_Standard l, string label, float val, float totalPool, float min, float max, string tooltip = null, float? defaultValue = null, bool showPercent = true)
        {
            l.Gap(2f);
            Rect r = l.GetRect(24f);
            if (!tooltip.NullOrEmpty()) TooltipHandler.TipRegion(r, TooltipWithDefault(tooltip, defaultValue, SliderFormat.Fixed0));

            string displaySuffix;
            if (showPercent && totalPool > 0f)
            {
                float pct = val / totalPool;
                displaySuffix = $"{val:F0} ({pct:P1})";
            }
            else
            {
                displaySuffix = $"{val:F0}";
            }

            Widgets.Label(r.LeftPart(0.5f), $"{label}: {displaySuffix.Colorize(Color.cyan)}");
            return Mathf.Round(Widgets.HorizontalSlider(r.RightPart(0.5f), val, min, max, false, null, null, null, 1f));
        }

        public static void MultiColumnSlider(Listing_Standard l, string[] labels, float[] values, Vector2 minMax, string[] tooltips = null, float step = 0.1f, SliderFormat format = SliderFormat.Fixed1, float height = 44f, float[] defaultValues = null, SliderFormat? defaultFormat = null)
        {
            l.Gap(4f);
            Rect rowRect = l.GetRect(height);
            int count = labels.Length;
            float colWidth = rowRect.width / count;

            for (int i = 0; i < count; i++)
            {
                if (labels[i].NullOrEmpty())
                    continue;

                Rect colRect = new Rect(rowRect.x + (colWidth * i), rowRect.y, colWidth - 10f, rowRect.height);
                if (tooltips != null && i < tooltips.Length && !tooltips[i].NullOrEmpty())
                {
                    float? defaultValue = defaultValues != null && i < defaultValues.Length ? defaultValues[i] : (float?)null;
                    TooltipHandler.TipRegion(colRect, TooltipWithDefault(tooltips[i], defaultValue, defaultFormat ?? format));
                }

                string suffix = GetFormattedValue(values[i], format);
                Widgets.Label(colRect.TopPart(0.6f), $"<b>{labels[i]}</b>: {suffix.Colorize(Color.cyan)}");
                values[i] = Widgets.HorizontalSlider(colRect.BottomPart(0.4f), values[i], minMax.x, minMax.y, false, null, null, null, step);
            }
        }

        public static void DrawHeader(Listing_Standard l, string label, Color? color = null)
        {
            l.Gap(StandardGap);
            Text.Font = GameFont.Small;
            string colorHex = color.HasValue ? ColorUtility.ToHtmlStringRGBA(color.Value) : "FFFFFF";
            string styledLabel = $"<size=15><b><color=#{colorHex}>{label}</color></b></size>";
            // size=15 bold needs a full Small line box; Listing.Label CalcHeight often undersizes and crops descenders.
            float h = Mathf.Max(24f, Text.CalcHeight(styledLabel, l.ColumnWidth) + 2f);
            TextAnchor prev = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(l.GetRect(h), styledLabel);
            Text.Anchor = prev;
            l.GapLine(4f);
            l.Gap(StandardGap);
        }

        /// <summary>
        /// Large clickable section title. Returns whether the section body should be drawn.
        /// </summary>
        public static bool DrawCollapsibleHeader(Listing_Standard l, string label, ref bool expanded, Color? color = null, string tip = null)
        {
            l.Gap(StandardGap);
            Rect r = l.GetRect(26f);
            Widgets.DrawHighlightIfMouseover(r);
            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(r, tip);
            if (Widgets.ButtonInvisible(r))
                expanded = !expanded;

            Color c = color ?? Color.white;
            string colorHex = ColorUtility.ToHtmlStringRGBA(c);
            string arrow = expanded ? "▼" : "▶";
            Text.Font = GameFont.Small;
            Widgets.Label(r, $"<b><color=#{colorHex}>{arrow}  {label}</color></b>");
            if (expanded)
            {
                l.GapLine(6f);
                l.Gap(4f);
            }
            else
                l.Gap(4f);
            return expanded;
        }

        /// <summary>Per-menu top bar: red Reset (left) + optional second left button + Expand all / Collapse all (right).</summary>
        public static void DrawMenuTopBar(Listing_Standard l, string resetLabel, Action onReset, Action expandAll, Action collapseAll,
            string secondaryLeftLabel = null, Action onSecondaryLeft = null, string secondaryLeftTip = null)
        {
            if (onReset == null) return;
            l.Gap(4f);
            Rect row = l.GetRect(30f);
            float gap = 16f;
            float sideBtnW = 110f;
            float leftPairBtnW = 220f; // Reset / Update Notes: twice Expand/Collapse width
            float rightPairW = sideBtnW * 2f + 8f;
            bool hasSecondary = !secondaryLeftLabel.NullOrEmpty() && onSecondaryLeft != null;
            float leftBudget = row.width - rightPairW - gap - 8f;

            float leftBtnW = leftPairBtnW;
            float resetW;
            if (hasSecondary)
            {
                float needed = leftPairBtnW * 2f + 8f;
                if (leftBudget < needed)
                    leftBtnW = Mathf.Max(120f, (leftBudget - 8f) / 2f);
                resetW = leftBtnW;
            }
            else
            {
                resetW = Mathf.Min(250f, leftBudget);
                if (resetW < 120f) resetW = Mathf.Max(100f, row.width * 0.35f);
            }

            Rect resetRect = new Rect(row.x, row.y, resetW, row.height);
            Rect secondaryRect = hasSecondary
                ? new Rect(resetRect.xMax + 8f, row.y, leftBtnW, row.height)
                : default;
            Rect collapseRect = new Rect(row.xMax - sideBtnW, row.y, sideBtnW, row.height);
            Rect expandRect = new Rect(collapseRect.x - 8f - sideBtnW, row.y, sideBtnW, row.height);

            Color prev = GUI.color;
            GUI.color = new Color(1f, 0.5f, 0.5f);
            if (Widgets.ButtonText(resetRect, resetLabel))
                onReset();
            GUI.color = prev;

            if (hasSecondary)
            {
                if (!secondaryLeftTip.NullOrEmpty())
                    TooltipHandler.TipRegion(secondaryRect, secondaryLeftTip);
                if (Widgets.ButtonText(secondaryRect, secondaryLeftLabel))
                    onSecondaryLeft();
            }

            if (expandAll != null && Widgets.ButtonText(expandRect, "TSA_WD_Settings_ExpandAll".Translate()))
                expandAll();
            if (collapseAll != null && Widgets.ButtonText(collapseRect, "TSA_WD_Settings_CollapseAll".Translate()))
                collapseAll();

            l.Gap(6f);
        }

        /// <summary>Obsolete: use <see cref="DrawMenuTopBar"/>.</summary>
        public static void DrawMenuResetButton(Listing_Standard l, string label, Action onReset)
        {
            DrawMenuTopBar(l, label, onReset, null, null);
        }

        /// <summary>
        /// Three columns: optional pack label + dropdown + optional Tiny gray status | Tiny gray description | Apply.
        /// Pass empty <paramref name="packLabel"/> / <paramref name="currentlyAppliedLine"/> to omit those lines.
        /// Button height matches <see cref="DrawMenuRow"/> (<see cref="MenuRowHeight"/> − 24). Row grows with description.
        /// </summary>
        public static void DrawSettingPresetRow<T>(
            Listing_Standard l,
            string packLabel,
            T pendingSelected,
            string currentlyAppliedLine,
            Func<T, string> labelFor,
            Func<T, string> descFor,
            Action<T> onSelect,
            Action onApply,
            string tip = null) where T : struct, Enum
        {
            l.Gap(4f);
            bool showPack = !packLabel.NullOrEmpty();
            bool showStatus = !currentlyAppliedLine.NullOrEmpty();

            float applyW = 90f;
            float gap = 8f;
            float col1 = l.ColumnWidth * 0.28f;
            float col2w = l.ColumnWidth - col1 - gap - applyW - gap;
            // Same button height as DrawMenuRow (MenuRowHeight − 24).
            float btnH = MenuRowHeight - 24f;
            const float pad = 4f;

            Text.Font = GameFont.Small;
            string packStyled = showPack ? $"<size=15><b><color=#FFFFFF>{packLabel}</color></b></size>" : "";
            float packH = showPack ? Mathf.Max(24f, Text.CalcHeight(packStyled, col1) + 2f) : 0f;
            Text.Font = GameFont.Tiny;
            float statusH = showStatus ? Mathf.Max(15f, Text.LineHeight) : 0f;

            string desc = descFor != null ? descFor(pendingSelected) : "";
            Text.Font = GameFont.Small;
            Text.WordWrap = true;
            float descH = Mathf.Max(24f, Text.CalcHeight(desc, Mathf.Max(1f, col2w)));
            float bandH = Mathf.Max(btnH, descH);
            float rowH;
            if (!showPack && !showStatus)
                rowH = Mathf.Max(MenuRowHeight, bandH + 8f);
            else
                rowH = (showPack ? packH + pad : 0f) + bandH + (showStatus ? pad + statusH : 0f);

            Rect row = l.GetRect(rowH);
            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(row, tip);

            float col3x = row.xMax - applyW;
            float col2x = row.x + col1 + gap;
            Rect selectCol = new Rect(row.x, row.y, col1, row.height);

            float btnBandTop = showPack ? selectCol.y + packH + pad : selectCol.y;
            float btnBandBottom = showStatus ? selectCol.yMax - statusH - pad : selectCol.yMax;
            float btnY = btnBandTop + (btnBandBottom - btnBandTop - btnH) / 2f;
            Rect dropRect = new Rect(selectCol.x, btnY, selectCol.width, btnH);
            Rect applyRect = new Rect(col3x, btnY, applyW, btnH);

            TextAnchor prevAnchor = Text.Anchor;
            bool prevWrap = Text.WordWrap;
            Color prevColor = GUI.color;

            if (showPack)
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = Color.white;
                Widgets.Label(new Rect(selectCol.x, selectCol.y, selectCol.width, packH), packStyled);
            }

            Text.Font = GameFont.Small;
            string pendingLabel = labelFor != null ? labelFor(pendingSelected) : pendingSelected.ToString();
            if (Widgets.ButtonText(dropRect, pendingLabel))
            {
                var opts = new List<FloatMenuOption>();
                foreach (T value in Enum.GetValues(typeof(T)))
                {
                    T captured = value;
                    string optLabel = labelFor != null ? labelFor(captured) : captured.ToString();
                    opts.Add(new FloatMenuOption(optLabel, () => onSelect?.Invoke(captured)));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }

            if (showStatus)
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = Color.gray;
                Widgets.Label(
                    new Rect(selectCol.x, selectCol.yMax - statusH, selectCol.width, statusH),
                    currentlyAppliedLine);
            }

            Text.Font = GameFont.Small;
            Text.WordWrap = true;
            float descY = btnBandTop + (btnBandBottom - btnBandTop - descH) / 2f;
            descY = Mathf.Clamp(descY, row.y, row.yMax - descH);
            Rect descRect = new Rect(col2x, descY, col2w, descH);
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(descRect, desc);

            Text.Font = GameFont.Small;
            Text.Anchor = prevAnchor;
            Text.WordWrap = prevWrap;
            GUI.color = prevColor;

            if (Widgets.ButtonText(applyRect, "TSA_WD_SettingsPreset_Apply".Translate()))
                onApply?.Invoke();
        }

        /// <summary>Height used by <see cref="DrawSettingPresetRow"/> for a desc-only row (no pack/status), matching menu button size.</summary>
        public static float EstimateSettingPresetRowHeight(string description, float columnWidth)
        {
            float applyW = 90f;
            float gap = 8f;
            float col1 = columnWidth * 0.28f;
            float col2w = columnWidth - col1 - gap - applyW - gap;
            float btnH = MenuRowHeight - 24f;
            Text.Font = GameFont.Small;
            Text.WordWrap = true;
            float descH = Mathf.Max(24f, Text.CalcHeight(description ?? "", Mathf.Max(1f, col2w)));
            return 4f + Mathf.Max(MenuRowHeight, Mathf.Max(btnH, descH) + 8f);
        }

        /// <summary>Enum dropdown that invokes <paramref name="setAndApply"/> when a new value is chosen.</summary>
        public static void EnumDropdownApply<T>(
            Listing_Standard l,
            string label,
            T current,
            Action<T> setAndApply,
            Func<T, string> labelFor,
            string tooltip = null) where T : struct, Enum
        {
            if (setAndApply == null) return;
            l.Gap(2f);
            Rect r = l.GetRect(28f);
            if (!tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(r, tooltip);

            string currentLabel = labelFor != null ? labelFor(current) : current.ToString();
            Widgets.Label(r.LeftPart(0.45f), $"{label}: {currentLabel.Colorize(Color.cyan)}");
            if (Widgets.ButtonText(r.RightPart(0.55f), currentLabel))
            {
                var opts = new List<FloatMenuOption>();
                foreach (T value in Enum.GetValues(typeof(T)))
                {
                    T captured = value;
                    string optLabel = labelFor != null ? labelFor(captured) : captured.ToString();
                    opts.Add(new FloatMenuOption(optLabel, () => setAndApply(captured)));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
        }

        /// <summary>Draw window title at top of inRect (same size/style as main settings headers), return Rect for content below. Reserve bottom for close button. Call when optionalTitle = null.</summary>
        public static Rect DrawWindowTitle(Rect inRect, string title)
        {
            Text.Font = GameFont.Small;
            string styledLabel = $"<size=15><b><color=#FFFFFF>{title}</color></b></size>";
            Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width, WindowTitleHeight - 4f);
            Widgets.Label(titleRect, styledLabel);
            return new Rect(inRect.x, inRect.y + WindowTitleHeight, inRect.width, inRect.height - WindowTitleHeight - ReserveBottomForCloseButton);
        }

        /// <summary>Checkbox row consistent with other settings UI (gap, rect height, tooltip). Optional rowHeight centers the 24px control vertically (e.g. 38f for main menu).</summary>
        public static void DrawCheckbox(Listing_Standard l, string label, ref bool value, string tooltip = null, float? rowHeight = null, bool? defaultValue = null)
        {
            l.Gap(2f);
            float h = rowHeight ?? 24f;
            Rect r = l.GetRect(h);
            if (!tooltip.NullOrEmpty()) TooltipHandler.TipRegion(r, TooltipWithDefault(tooltip, defaultValue));
            Rect inner = h > 24f ? new Rect(r.x, r.y + (h - 24f) / 2f, r.width, 24f) : r;
            Widgets.CheckboxLabeled(inner, label, ref value);
        }

        /// <summary>One row: optional zebra stripe, button (left column), description text (right column).</summary>
        public static void DrawMenuRow(Listing_Standard l, int rowIndex, string buttonLabel, string descriptionTranslated, Action onOpen)
        {
            l.Gap(2f);
            Rect rowRect = l.GetRect(MenuRowHeight);
            bool zebra = (rowIndex % 2) == 1;
            if (zebra)
            {
                Color prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.08f);
                GUI.DrawTexture(rowRect, BaseContent.WhiteTex);
                GUI.color = prev;
            }
            float gap = 8f;
            float btnPaddingLeft = 10f;
            float btnW = rowRect.width * MenuButtonColumnFraction - gap - btnPaddingLeft;
            float btnH = rowRect.height - 24f;
            Rect btnRect = new Rect(rowRect.x + btnPaddingLeft, rowRect.y + (rowRect.height - btnH) / 2f, btnW, btnH);
            Rect descRect = new Rect(rowRect.x + rowRect.width * MenuButtonColumnFraction + gap, rowRect.y, rowRect.width * (1f - MenuButtonColumnFraction) - gap, rowRect.height);
            if (Widgets.ButtonText(btnRect, buttonLabel))
                onOpen?.Invoke();
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = true;
            Widgets.Label(descRect, descriptionTranslated);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
        }

        public static string TooltipWithDefault(string tooltip, float? defaultValue, SliderFormat format = SliderFormat.Fixed1)
        {
            if (!defaultValue.HasValue) return tooltip;
            return AppendDefaultLine(tooltip, GetFormattedValue(defaultValue.Value, format));
        }

        public static string TooltipWithDefault(string tooltip, bool? defaultValue)
        {
            if (!defaultValue.HasValue) return tooltip;
            return AppendDefaultLine(tooltip, defaultValue.Value ? "TSA_WD_Settings_DefaultOn".Translate().ToString() : "TSA_WD_Settings_DefaultOff".Translate().ToString());
        }

        public static string TooltipWithDefault(string tooltip, string defaultValue)
        {
            if (defaultValue.NullOrEmpty()) return tooltip;
            return AppendDefaultLine(tooltip, defaultValue);
        }

        private static string AppendDefaultLine(string tooltip, string defaultValue)
        {
            string defaultLine = "TSA_WD_Settings_DefaultValue".Translate(defaultValue).ToString();
            if (tooltip.NullOrEmpty()) return defaultLine;
            return tooltip + "\n" + defaultLine;
        }

        public static string FormatDefault(float val, SliderFormat format = SliderFormat.Fixed1)
        {
            return GetFormattedValue(val, format);
        }

        /// <summary>TechLevel picker (Neolithic…Archotech).</summary>
        public static void TechLevelDropdown(
            Listing_Standard l,
            string label,
            TechLevel current,
            Action<TechLevel> set,
            string tooltip = null,
            TechLevel? defaultValue = null)
        {
            if (set == null) return;
            l.Gap(2f);
            Rect r = l.GetRect(24f);
            string currentLabel = current.ToString();
            if (!tooltip.NullOrEmpty())
            {
                string tip = defaultValue.HasValue
                    ? TooltipWithDefault(tooltip, defaultValue.Value.ToString())
                    : tooltip;
                TooltipHandler.TipRegion(r, tip);
            }

            Widgets.Label(r.LeftPart(0.55f), $"{label}: {currentLabel.Colorize(Color.cyan)}");
            if (Widgets.ButtonText(r.RightPart(0.45f), currentLabel))
            {
                var opts = new List<FloatMenuOption>();
                for (TechLevel t = TechLevel.Neolithic; t <= TechLevel.Archotech; t++)
                {
                    TechLevel captured = t;
                    opts.Add(new FloatMenuOption(captured.ToString(), () => set(captured)));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
        }

        private static string GetFormattedValue(float val, SliderFormat format)
        {
            switch (format)
            {
                case SliderFormat.Fixed0: return val.ToString("F0");
                case SliderFormat.Fixed1: return val.ToString("F1");
                case SliderFormat.Fixed2: return val.ToString("F2");
                case SliderFormat.Multiplier: return val.ToString("F2") + "x";
                case SliderFormat.Percent: return (val * 100f).ToString("F0") + " %";
                case SliderFormat.PercentDecimal: return (val * 100f).ToString("F1") + " %"; 
                default: return val.ToString();
            }
        }
    }
}