using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Tooling value per item (ratios: wood 1, steel 2, plasteel 3 by default). Edit the XML defaults, or add any item
    /// (for example a pickaxe worth 50 wood). Overrides are stored in <see cref="WorldDominationSettings.toolingValueOverrides"/>.
    /// </summary>
    public class Dialog_ToolingValues : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;

        public override Vector2 InitialSize => new Vector2(850f, 750f);

        public Dialog_ToolingValues()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_ToolingValues_Title".Translate();
            optionalTitle = null;
        }

        public override void PreClose()
        {
            base.PreClose();
            OutpostTooling.MarkDirty();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            float contentWidth = contentRect.width - 24f;
            var s = WorldDominationMod.settings;

            var rows = BuildRows(s);
            float height = 200f + rows.Count * 62f;
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, height);
            Widgets.BeginScrollView(contentRect, ref scrollPosition, scrollViewRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(scrollViewRect);

            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetToolingValues(), () => { }, () => { });
            l.Label("TSA_WD_ToolingValues_Intro".Translate());
            l.Gap(SettingsUI.StandardGap);

            if (l.ButtonText("TSA_WD_ToolingValues_Add".Translate()))
                Find.WindowStack.Add(new Dialog_ToolingItemPicker());
            l.Gap(SettingsUI.StandardGap);

            string removeKey = null;
            foreach (ToolingRow row in rows)
            {
                string label = row.def != null ? row.def.LabelCap.ToString() : row.defName;
                float current = row.current;
                float newValue = SettingsUI.LabeledSlider(l, label, current, 0f, 100f,
                    "TSA_WD_ToolingValues_RowTooltip".Translate(), 0.5f, SliderFormat.Fixed1, row.xmlDefault ?? 0f);
                newValue = Mathf.Round(newValue * 2f) / 2f;
                if (Mathf.Abs(newValue - current) > 0.0001f)
                {
                    if (s.toolingValueOverrides == null) s.toolingValueOverrides = new Dictionary<string, float>();
                    s.toolingValueOverrides[row.defName] = newValue;
                }

                if (row.xmlDefault.HasValue)
                {
                    if (s.toolingValueOverrides != null && s.toolingValueOverrides.ContainsKey(row.defName)
                        && l.ButtonText("TSA_WD_ToolingValues_ResetItem".Translate(label, row.xmlDefault.Value.ToString("F1"))))
                        removeKey = row.defName;
                }
                else if (l.ButtonText("TSA_WD_ToolingValues_RemoveItem".Translate(label)))
                {
                    removeKey = row.defName;
                }
                l.Gap(4f);
            }

            if (removeKey != null)
            {
                s.toolingValueOverrides?.Remove(removeKey);
                if (s.toolingValueOverrides != null && s.toolingValueOverrides.Count == 0)
                    s.toolingValueOverrides = null;
            }

            l.End();
            Widgets.EndScrollView();
        }

        private struct ToolingRow
        {
            public string defName;
            public ThingDef def;
            public float current;
            public float? xmlDefault;
        }

        private static List<ToolingRow> BuildRows(WorldDominationSettings s)
        {
            var rows = new List<ToolingRow>();
            var seen = new HashSet<string>();

            foreach (WdToolingValueDef d in DefDatabase<WdToolingValueDef>.AllDefsListForReading)
            {
                if (d.thingDef == null || !seen.Add(d.thingDef.defName)) continue;
                rows.Add(new ToolingRow
                {
                    defName = d.thingDef.defName,
                    def = d.thingDef,
                    xmlDefault = d.value,
                    current = OutpostTooling.GetValue(d.thingDef)
                });
            }

            if (s.toolingValueOverrides != null)
            {
                foreach (KeyValuePair<string, float> kv in s.toolingValueOverrides)
                {
                    if (!seen.Add(kv.Key)) continue;
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(kv.Key);
                    if (def == null) continue;
                    rows.Add(new ToolingRow { defName = kv.Key, def = def, xmlDefault = null, current = kv.Value });
                }
            }

            rows.Sort((a, b) =>
            {
                int cmp = b.current.CompareTo(a.current);
                return cmp != 0 ? cmp : string.CompareOrdinal(a.defName, b.defName);
            });
            return rows;
        }
    }

    /// <summary>Searchable list of items to add to the tooling values (starts at value 1, adjust afterwards).</summary>
    public class Dialog_ToolingItemPicker : Window
    {
        private Vector2 scrollPosition;
        private string filter = "";
        private List<ThingDef> candidates;

        public override Vector2 InitialSize => new Vector2(520f, 640f);

        public Dialog_ToolingItemPicker()
        {
            doCloseX = true;
            forcePause = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (candidates == null)
                candidates = BuildCandidates();

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "TSA_WD_ToolingValues_PickerTitle".Translate());
            Text.Font = GameFont.Small;

            filter = Widgets.TextField(new Rect(inRect.x, inRect.y + 38f, inRect.width, 28f), filter);

            Rect listRect = new Rect(inRect.x, inRect.y + 72f, inRect.width, inRect.height - 72f);
            List<ThingDef> shown = string.IsNullOrWhiteSpace(filter)
                ? candidates
                : candidates.Where(d => d.LabelCap.ToString().IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0
                    || d.defName.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            const float rowH = 28f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, shown.Count * rowH);
            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);
            for (int i = 0; i < shown.Count; i++)
            {
                ThingDef def = shown[i];
                Rect row = new Rect(0f, i * rowH, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawLightHighlight(row);
                Widgets.ThingIcon(new Rect(row.x + 2f, row.y + 2f, rowH - 4f, rowH - 4f), def);
                Widgets.Label(new Rect(row.x + rowH + 6f, row.y + 3f, row.width - rowH - 10f, rowH), def.LabelCap);
                if (Widgets.ButtonInvisible(row))
                {
                    var s = WorldDominationMod.settings;
                    if (s.toolingValueOverrides == null) s.toolingValueOverrides = new Dictionary<string, float>();
                    s.toolingValueOverrides[def.defName] = 1f;
                    OutpostTooling.MarkDirty();
                    Close();
                    return;
                }
            }
            Widgets.EndScrollView();
        }

        private static List<ThingDef> BuildCandidates()
        {
            var s = WorldDominationMod.settings;
            return DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.category == ThingCategory.Item
                    && d.EverHaulable
                    && !d.IsCorpse
                    && !string.IsNullOrEmpty(d.label)
                    && OutpostTooling.GetXmlValue(d) == null
                    && (s.toolingValueOverrides == null || !s.toolingValueOverrides.ContainsKey(d.defName)))
                .OrderBy(d => d.LabelCap.ToString())
                .ToList();
        }
    }
}
