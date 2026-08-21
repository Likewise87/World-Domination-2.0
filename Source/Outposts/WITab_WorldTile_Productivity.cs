using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// World / tile inspect tab. Two-column layout aligned with
    /// <see cref="Dialog_OutpostProduction"/>: left metrics box + selectable panels,
    /// right breakdown / species list for the selected panel.
    /// </summary>
    public class WITab_WorldTile_Productivity : WITab
    {
        private enum LeftPanel
        {
            Fertility,
            Animals,
            Fish,
            Mining,
            AnimalsHere,
            FishHere
        }

        private struct MetricData
        {
            public string Label;
            public int TotalPct;
            public float BaseScore;
            public string Tip;
            public string MutatorLines;
            public string UpgradeLines;
        }

        private struct SpeciesRow
        {
            public string Label;
            public string SearchText;
            public string YieldLine;
            public Texture2D Icon;
            public Color? IconColor;
        }

        private struct TileProductivityUiCache
        {
            public int unityFrame;
            public int tileId;
            public int prodOutpostId;
            public float fertUpg, huntUpg, fishUpg, mineUpg;

            public MetricData Fert, Animals, Fish, Mining;
            public List<SpeciesRow> Wildlife;
            public List<SpeciesRow> FishSpecies;
            public string FishEmptyReason;
        }

        private TileProductivityUiCache cache;
        private LeftPanel selectedPanel = LeftPanel.Fertility;
        private Vector2 rightScrollPos;
        private string speciesSearchFilter = "";

        private const float TabContentContract = 10f;
        private const float HeadlineRowHeight = 30f;
        private const float HeadlineSeparatorY = 32f;
        private const float TabHeaderConsumedHeight = 38f;
        private const float GapAfterHeadline = 10f;

        private const float ColGap = 16f;
        private const float LeftFrac = 0.42f;
        private const float LeftMinW = 260f;
        private const float PanelRowH = 30f;
        private const float PanelPad = 4f;
        private const float SpeciesIcon = 26f;
        private const float SpeciesNameH = 26f;
        private const float SpeciesFormulaH = 18f;
        private const float DetailRowH = 24f;
        private const float TotalBoxPad = 6f;
        private const float SearchBarH = 28f;
        private const float SearchGap = 6f;

        public WITab_WorldTile_Productivity()
        {
            size = new Vector2(860f, 540f);
            labelKey = "TSA_WD_TileProductivity_Tab";
        }

        public override bool IsVisible
        {
            get
            {
                var grid = Find.WorldGrid;
                if (grid == null) return false;
                int t = ResolveTileId();
                return t >= 0 && t < grid.TilesCount;
            }
        }

        private int ResolveTileId()
        {
            if (SelObject != null)
                return SelObject.Tile;
            return Find.WorldSelector?.SelectedTile ?? -1;
        }

        private WorldObject_WD_Outpost ResolveProductivityOutpost()
        {
            if (SelObject is WorldObject_WD_Outpost o) return o;
            int t = ResolveTileId();
            if (t < 0 || Find.WorldObjects == null) return null;
            foreach (WorldObject wo in Find.WorldObjects.ObjectsAt(t))
                if (wo is WorldObject_WD_Outpost wd) return wd;
            return null;
        }

        private static string CombineModifiers(string mutatorLines, string upgradeLines)
        {
            // Kept for tooltip builders elsewhere; WITab stores mutators/upgrades separately.
            bool hasMut = !string.IsNullOrEmpty(mutatorLines);
            bool hasUpg = !string.IsNullOrEmpty(upgradeLines);
            if (!hasMut && !hasUpg)
                return "TSA_WD_ProductivityTooltip_NoMutators".Translate().ToString();
            var sb = new StringBuilder();
            if (hasMut) sb.Append(mutatorLines.TrimEnd());
            if (hasUpg)
            {
                if (hasMut) sb.AppendLine();
                sb.AppendLine("TSA_WD_ProductivityTooltip_OutpostUpgradesHeader".Translate());
                sb.Append(upgradeLines.TrimEnd());
            }
            return sb.ToString();
        }

        private static string NormalizeYieldLine(string multiline)
        {
            if (string.IsNullOrEmpty(multiline)) return "";
            return multiline.Replace("\r\n", "\n").Trim();
        }

        private static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
            return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static float SpeciesRowHeight(SpeciesRow row)
        {
            int formulaLines = string.IsNullOrEmpty(row.YieldLine) ? 0 : SplitLines(row.YieldLine).Length;
            float h = SpeciesNameH;
            if (formulaLines > 0)
                h += Outpost_Dialog_UI.ListRowFormulaTopPadding + formulaLines * SpeciesFormulaH;
            return Mathf.Max(SpeciesNameH + 4f, h);
        }

        private static Color PctColor(int pct)
        {
            if (pct <= 30) return Color.red;
            if (pct <= 60) return Color.yellow;
            return Color.green;
        }

        private static string Pct(float f)
            => "TSA_WD_TileProductivity_BasePct".Translate(Mathf.RoundToInt(f * 100f)).ToString();

        private static string PctInt(int pct)
            => "TSA_WD_TileProductivity_BasePct".Translate(pct).ToString();

        private static string AppendModifierLine(string existing, string line)
        {
            if (string.IsNullOrEmpty(line)) return existing ?? "";
            if (string.IsNullOrEmpty(existing)) return line.TrimEnd();
            return existing.TrimEnd() + "\n" + line.TrimEnd();
        }

        private static void LabelAnchored(Rect rect, string text, TextAnchor anchor)
        {
            TextAnchor prev = Text.Anchor;
            Text.Anchor = anchor;
            Widgets.Label(rect, text);
            Text.Anchor = prev;
        }

        private void EnsureCache(int tile, Tile tileInfo, WorldObject_WD_Outpost prodOutpost,
            float fertUpg, float huntUpg, float fishUpg, float mineUpg)
        {
            int opId = prodOutpost?.ID ?? -1;
            if (cache.unityFrame == Time.frameCount
                && cache.tileId == tile
                && cache.prodOutpostId == opId
                && Mathf.Approximately(cache.fertUpg, fertUpg)
                && Mathf.Approximately(cache.huntUpg, huntUpg)
                && Mathf.Approximately(cache.fishUpg, fishUpg)
                && Mathf.Approximately(cache.mineUpg, mineUpg)
                && cache.Wildlife != null)
                return;

            string fertUpgLines = prodOutpost != null
                ? WorldTileProductivity.BuildOutpostUpgradeProductivityLines(prodOutpost, d => d.tileFertilityBonus) : "";
            string huntUpgLines = prodOutpost != null
                ? WorldTileProductivity.BuildOutpostUpgradeProductivityLines(prodOutpost, d => d.tileAnimalAbundanceBonus) : "";
            string fishUpgLines = prodOutpost != null
                ? WorldTileProductivity.BuildOutpostUpgradeProductivityLines(prodOutpost, d => d.tileFishAbundanceBonus) : "";
            string mineUpgLines = prodOutpost != null
                ? WorldTileProductivity.BuildOutpostUpgradeProductivityLines(prodOutpost, d => d.tileMiningBonus) : "";

            string mutF = WorldTileProductivity.GetMutatorLinesForProductivity(tileInfo, WorldTileProductivity.MutatorFarmingScoreOffsets);
            string mutH = WorldTileProductivity.GetMutatorLinesForProductivity(tileInfo, WorldTileProductivity.MutatorHuntingScoreOffsets);
            string mutFish = WorldTileProductivity.GetMutatorLinesForProductivity(tileInfo, WorldTileProductivity.MutatorFishingScoreOffsets);
            string mutM = WorldTileProductivity.GetMutatorLinesForProductivity(tileInfo, WorldTileProductivity.MutatorMiningScoreOffsets);

            string pollutionLine = WorldTileProductivity.GetPollutionEcologyModifierLine(tile);
            if (!string.IsNullOrEmpty(pollutionLine))
            {
                mutF = AppendModifierLine(mutF, pollutionLine);
                mutH = AppendModifierLine(mutH, pollutionLine);
                mutFish = AppendModifierLine(mutFish, pollutionLine);
            }

            float huntFactor = WorldTileProductivity.GetHuntingScore(tile, huntUpg);
            float fishFactor = WorldTileProductivity.GetFishingScore(tile, fishUpg);
            BiomeDef biome = WorldTileInfo.GetBiome(tile);

            var wildlife = new List<SpeciesRow>();
            foreach (HuntingAnimalOption opt in Outpost_Hunting.GetHuntingAnimalOptionsForTile(tile))
            {
                if (opt.Kind == null) continue;
                string yield = NormalizeYieldLine(Outpost_Hunting.GetHuntingPerSkillAtTileSummary(opt.Kind, huntFactor, biome));
                wildlife.Add(new SpeciesRow
                {
                    Label = opt.Kind.LabelCap,
                    SearchText = opt.Kind.LabelCap,
                    YieldLine = yield,
                    Icon = opt.Kind.race?.uiIcon,
                    IconColor = opt.Kind.race?.graphicData?.color
                });
            }

            var fishSpecies = new List<SpeciesRow>();
            string fishEmpty = null;
            var grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount)
                fishEmpty = "TSA_WD_TileProductivity_FishNone".Translate().ToString();
            else if (!tileInfo.IsCoastal || tileInfo.WaterCovered)
                fishEmpty = "TSA_WD_TileProductivity_FishNotCoastal".Translate().ToString();
            else
            {
                foreach (FishingFishOption opt in Outpost_Fishing.GetFishingFishOptionsForTile(tile))
                {
                    if (opt.Fish == null) continue;
                    string rarity = opt.IsUncommon
                        ? "TSA_WD_TileProductivity_FishUncommon".Translate().ToString()
                        : "TSA_WD_TileProductivity_FishCommon".Translate().ToString();
                    string label = opt.Fish.LabelCap + " (" + rarity + ")";
                    fishSpecies.Add(new SpeciesRow
                    {
                        Label = label,
                        SearchText = opt.Fish.LabelCap + " " + rarity,
                        YieldLine = NormalizeYieldLine(Outpost_Fishing.GetFishPerSkillAtTileSummary(opt.Fish, tile, fishFactor)),
                        Icon = opt.Fish.uiIcon,
                        IconColor = opt.Fish.graphicData?.color
                    });
                }
                if (fishSpecies.Count == 0)
                    fishEmpty = "TSA_WD_TileProductivity_FishNone".Translate().ToString();
            }

            cache = new TileProductivityUiCache
            {
                unityFrame = Time.frameCount,
                tileId = tile,
                prodOutpostId = opId,
                fertUpg = fertUpg,
                huntUpg = huntUpg,
                fishUpg = fishUpg,
                mineUpg = mineUpg,
                Fert = MakeMetric(
                    "TSA_WD_Biome_ColFertility".Translate(),
                    WorldTileProductivity.GetFarmingFertilityScore(tile, fertUpg),
                    WorldTileProductivity.GetFarmingBaseScore(tile),
                    WorldTileProductivity.GetFarmingFertilityTooltipText(tile, fertUpg, fertUpgLines),
                    mutF, fertUpgLines),
                Animals = MakeMetric(
                    "TSA_WD_Biome_ColAnimals".Translate(),
                    huntFactor,
                    WorldTileProductivity.GetHuntingBaseScore(tile),
                    WorldTileProductivity.GetHuntingScoreTooltipText(tile, huntUpg, huntUpgLines),
                    mutH, huntUpgLines),
                Fish = MakeMetric(
                    "TSA_WD_Biome_ColFish".Translate(),
                    fishFactor,
                    WorldTileProductivity.GetFishingBaseScore(tile),
                    WorldTileProductivity.GetFishingScoreTooltipText(tile, fishUpg, fishUpgLines),
                    mutFish, fishUpgLines),
                Mining = MakeMetric(
                    "TSA_WD_Production_MiningEfficiency".Translate(),
                    WorldTileProductivity.GetMiningOutputMultiplier(tile, mineUpg),
                    WorldTileProductivity.GetMiningBaseScore(tile),
                    WorldTileProductivity.GetMiningEfficiencyTooltipText(tile, mineUpg, mineUpgLines),
                    mutM, mineUpgLines),
                Wildlife = wildlife,
                FishSpecies = fishSpecies,
                FishEmptyReason = fishEmpty
            };
        }

        private static MetricData MakeMetric(string label, float total, float baseScore, string tip, string mutators, string upgrades)
        {
            return new MetricData
            {
                Label = label,
                TotalPct = Mathf.RoundToInt(total * 100f),
                BaseScore = baseScore,
                Tip = tip,
                MutatorLines = mutators ?? "",
                UpgradeLines = upgrades ?? ""
            };
        }

        protected override void FillTab()
        {
            int tile = ResolveTileId();
            var grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount) return;
            Tile tileInfo = grid[tile];

            Rect content = new Rect(0f, 0f, size.x, size.y).ContractedBy(TabContentContract);
            Text.Font = GameFont.Medium;
            LabelAnchored(new Rect(content.x, content.y, content.width, HeadlineRowHeight),
                "TSA_WD_TileProductivity_Headline".Translate(), TextAnchor.MiddleLeft);
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(content.x, content.y + HeadlineSeparatorY, content.width);

            float bodyTop = content.y + TabHeaderConsumedHeight + GapAfterHeadline;
            Rect body = new Rect(content.x, bodyTop, content.width, content.yMax - bodyTop);

            if (tileInfo.WaterCovered)
            {
                GUI.color = new Color(0.78f, 0.78f, 0.78f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(body.x, body.y, body.width, 24f),
                    "TSA_WD_ProductivityTooltip_WaterTile".Translate().ToString());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            WorldObject_WD_Outpost prodOutpost = ResolveProductivityOutpost();
            EnsureCache(tile, tileInfo, prodOutpost,
                prodOutpost?.GetBuiltUpgradeTileFertilityBonus() ?? 0f,
                prodOutpost?.GetBuiltUpgradeTileAnimalAbundanceBonus() ?? 0f,
                prodOutpost?.GetBuiltUpgradeTileFishAbundanceBonus() ?? 0f,
                prodOutpost?.GetBuiltUpgradeTileMiningBonus() ?? 0f);

            float leftW = Mathf.Max(LeftMinW, body.width * LeftFrac);
            Rect leftArea = new Rect(body.x, body.y, leftW, body.height);
            Rect rightArea = new Rect(body.x + leftW + ColGap, body.y, body.width - leftW - ColGap, body.height);
            Widgets.DrawLineVertical(body.x + leftW + ColGap * 0.5f, body.y, body.height);

            DrawLeftColumn(leftArea);
            DrawRightColumn(rightArea);

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawLeftColumn(Rect left)
        {
            float y = left.y;
            float w = left.width;

            // Metrics overview box (production-style menu section)
            const float lineH = Outpost_Dialog_UI.OutcomeLineH;
            const float pad = Outpost_Dialog_UI.OutcomeBoxPad;
            float boxH = pad * 2f + lineH * 4f;
            Outpost_Dialog_UI.DrawOutcomeBox(new Rect(left.x, y, w, boxH));
            float cy = y + pad;
            float ix = left.x + pad;
            float iw = w - pad * 2f;
            DrawMetricSummaryLine(ix, cy, iw, cache.Fert); cy += lineH;
            DrawMetricSummaryLine(ix, cy, iw, cache.Animals); cy += lineH;
            DrawMetricSummaryLine(ix, cy, iw, cache.Fish); cy += lineH;
            DrawMetricSummaryLine(ix, cy, iw, cache.Mining);
            y += boxH + Outpost_Dialog_UI.OutcomeBoxGap;

            GUI.color = Outpost_Dialog_UI.CycleTimerColor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(left.x, y, w, 22f), "TSA_WD_TileProductivity_ChoosePanel".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            y += 24f;

            y = DrawNavRow(left.x, y, w, LeftPanel.Fertility, cache.Fert.Label, PctInt(cache.Fert.TotalPct), PctColor(cache.Fert.TotalPct), 0);
            y = DrawNavRow(left.x, y, w, LeftPanel.Animals, cache.Animals.Label, PctInt(cache.Animals.TotalPct), PctColor(cache.Animals.TotalPct), 1);
            y = DrawNavRow(left.x, y, w, LeftPanel.Fish, cache.Fish.Label, PctInt(cache.Fish.TotalPct), PctColor(cache.Fish.TotalPct), 2);
            y = DrawNavRow(left.x, y, w, LeftPanel.Mining, cache.Mining.Label, PctInt(cache.Mining.TotalPct), PctColor(cache.Mining.TotalPct), 3);
            y += 6f;
            y = DrawNavRow(left.x, y, w, LeftPanel.AnimalsHere,
                "TSA_WD_TileProductivity_WildlifeHeader".Translate().ToString(),
                (cache.Wildlife?.Count ?? 0).ToString(),
                Outpost_Dialog_UI.CycleTimerColor, 4);
            DrawNavRow(left.x, y, w, LeftPanel.FishHere,
                "TSA_WD_TileProductivity_FishHeader".Translate().ToString(),
                (cache.FishSpecies?.Count ?? 0).ToString(),
                Outpost_Dialog_UI.CycleTimerColor, 5);
        }

        private static void DrawMetricSummaryLine(float x, float y, float w, MetricData m)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(x, y, w * 0.62f, Outpost_Dialog_UI.OutcomeLineH), m.Label);
            GUI.color = PctColor(m.TotalPct);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(x + w * 0.62f, y, w * 0.38f, Outpost_Dialog_UI.OutcomeLineH), PctInt(m.TotalPct));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(new Rect(x, y, w, Outpost_Dialog_UI.OutcomeLineH), m.Tip);
        }

        private float DrawNavRow(float x, float y, float w, LeftPanel panel, string label, string trailing, Color trailingColor, int zebraIndex)
        {
            Rect row = new Rect(x, y, w, PanelRowH);
            bool selected = selectedPanel == panel;
            if (zebraIndex % 2 == 0) Widgets.DrawHighlight(row);
            Outpost_Dialog_UI.DrawSelectedRowTint(row, selected);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(x + 8f, y, w * 0.65f - 8f, PanelRowH), label);
            GUI.color = trailingColor;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(x + w * 0.65f, y, w * 0.35f - 8f, PanelRowH), trailing);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            Outpost_Dialog_UI.FinishSelectableListRow(row, selected);
            if (Widgets.ButtonInvisible(row))
            {
                if (selectedPanel != panel)
                {
                    rightScrollPos = Vector2.zero;
                    speciesSearchFilter = "";
                }
                selectedPanel = panel;
            }
            return y + PanelRowH + PanelPad;
        }

        private void DrawRightColumn(Rect right)
        {
            float y = right.y;
            string header = GetRightHeader();
            GUI.color = Outpost_Dialog_UI.CycleTimerColor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(right.x, y, right.width, 22f), header);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            y += 24f;

            bool speciesPanel = selectedPanel == LeftPanel.AnimalsHere || selectedPanel == LeftPanel.FishHere;
            if (speciesPanel)
            {
                string oldFilter = speciesSearchFilter;
                Rect searchRect = new Rect(right.x, y, right.width - 16f, SearchBarH);
                speciesSearchFilter = Widgets.TextField(searchRect, speciesSearchFilter);
                if (speciesSearchFilter != oldFilter)
                    rightScrollPos = Vector2.zero;
                if (string.IsNullOrEmpty(speciesSearchFilter))
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.4f);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Text.Font = GameFont.Tiny;
                    string ph = "TSA_WD_Production_SearchPlaceholder".Translate().ToString();
                    if (ph.Contains("TSA_WD_")) ph = "Filter by name…";
                    Widgets.Label(searchRect, ph);
                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                }
                y += SearchBarH + SearchGap;
            }

            Rect scrollOuter = new Rect(right.x, y, right.width, right.yMax - y);
            float contentH = MeasureRightContentHeight();
            Rect view = new Rect(0f, 0f, right.width - 16f, Mathf.Max(contentH, scrollOuter.height));
            Widgets.BeginScrollView(scrollOuter, ref rightScrollPos, view);

            float sy = 0f;
            float sw = view.width;
            switch (selectedPanel)
            {
                case LeftPanel.Fertility:
                    sy = DrawMetricDetail(0f, sy, sw, cache.Fert);
                    break;
                case LeftPanel.Animals:
                    sy = DrawMetricDetail(0f, sy, sw, cache.Animals);
                    break;
                case LeftPanel.Fish:
                    sy = DrawMetricDetail(0f, sy, sw, cache.Fish);
                    break;
                case LeftPanel.Mining:
                    sy = DrawMetricDetail(0f, sy, sw, cache.Mining);
                    break;
                case LeftPanel.AnimalsHere:
                    sy = DrawSpeciesList(0f, sy, sw, cache.Wildlife,
                        "TSA_WD_TileProductivity_WildlifeNone".Translate().ToString());
                    break;
                case LeftPanel.FishHere:
                    sy = DrawSpeciesList(0f, sy, sw, cache.FishSpecies,
                        cache.FishEmptyReason ?? "TSA_WD_TileProductivity_FishNone".Translate().ToString());
                    break;
            }

            Widgets.EndScrollView();
        }

        private string GetRightHeader()
        {
            switch (selectedPanel)
            {
                case LeftPanel.Fertility: return cache.Fert.Label;
                case LeftPanel.Animals: return cache.Animals.Label;
                case LeftPanel.Fish: return cache.Fish.Label;
                case LeftPanel.Mining: return cache.Mining.Label;
                case LeftPanel.AnimalsHere: return "TSA_WD_TileProductivity_WildlifeHeader".Translate().ToString();
                case LeftPanel.FishHere: return "TSA_WD_TileProductivity_FishHeader".Translate().ToString();
                default: return "TSA_WD_TileProductivity_Breakdown".Translate().ToString();
            }
        }

        private bool SpeciesMatchesSearch(SpeciesRow row)
        {
            if (string.IsNullOrEmpty(speciesSearchFilter)) return true;
            string hay = row.SearchText ?? row.Label ?? "";
            return hay.IndexOf(speciesSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private float MeasureRightContentHeight()
        {
            switch (selectedPanel)
            {
                case LeftPanel.AnimalsHere:
                case LeftPanel.FishHere:
                {
                    List<SpeciesRow> rows = selectedPanel == LeftPanel.AnimalsHere ? cache.Wildlife : cache.FishSpecies;
                    if (rows == null || rows.Count == 0) return SpeciesNameH + 8f;
                    float h = 8f;
                    int visible = 0;
                    for (int i = 0; i < rows.Count; i++)
                    {
                        if (!SpeciesMatchesSearch(rows[i])) continue;
                        h += SpeciesRowHeight(rows[i]) + PanelPad;
                        visible++;
                    }
                    if (visible == 0) h += SpeciesNameH;
                    return h;
                }
                default:
                {
                    MetricData m = GetSelectedMetric();
                    int mut = SplitLines(m.MutatorLines).Length;
                    int upg = SplitLines(m.UpgradeLines).Length;
                    // total box + base + modifiers header + mut lines + upgrades header + upg lines
                    float h = TotalBoxPad * 2f + DetailRowH + 6f;
                    h += DetailRowH; // base
                    h += DetailRowH; // tile modifiers label
                    h += Mathf.Max(1, mut) * DetailRowH;
                    h += DetailRowH; // upgrades label
                    if (upg > 0) h += upg * DetailRowH;
                    return h + 8f;
                }
            }
        }

        private MetricData GetSelectedMetric()
        {
            switch (selectedPanel)
            {
                case LeftPanel.Animals: return cache.Animals;
                case LeftPanel.Fish: return cache.Fish;
                case LeftPanel.Mining: return cache.Mining;
                default: return cache.Fert;
            }
        }

        private static float DrawMetricDetail(float x, float y, float w, MetricData m)
        {
            float boxH = TotalBoxPad * 2f + DetailRowH;
            Rect box = new Rect(x, y, w, boxH);
            Outpost_Dialog_UI.DrawOutcomeBox(box);
            GUI.color = Outpost_Dialog_UI.CycleTimerColor;
            Widgets.DrawBox(box, 1);
            GUI.color = Color.white;

            Rect totalInner = box.ContractedBy(TotalBoxPad);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(totalInner.x, totalInner.y, totalInner.width * 0.55f, totalInner.height),
                "TSA_WD_TileProductivity_TotalPct".Translate(m.TotalPct).ToString());
            GUI.color = PctColor(m.TotalPct);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(totalInner.x + totalInner.width * 0.55f, totalInner.y, totalInner.width * 0.45f, totalInner.height),
                PctInt(m.TotalPct));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            if (!string.IsNullOrEmpty(m.Tip))
                TooltipHandler.TipRegion(box, m.Tip);
            y += boxH + 6f;

            int zebra = 0;
            y = DrawDetailKeyValue(x, y, w, zebra++,
                "TSA_WD_TileProductivity_BaseForTile".Translate().ToString(),
                Pct(m.BaseScore));

            string[] muts = SplitLines(m.MutatorLines);
            y = DrawDetailKeyValue(x, y, w, zebra++,
                "TSA_WD_TileProductivity_TileModifiers".Translate().ToString(),
                muts.Length == 0 ? "TSA_WD_ProductivityTooltip_NoMutators".Translate().ToString() : "");
            for (int i = 0; i < muts.Length; i++)
                y = DrawDetailLine(x, y, w, zebra++, muts[i]);

            string[] upgs = SplitLines(m.UpgradeLines);
            y = DrawDetailKeyValue(x, y, w, zebra++,
                "TSA_WD_ProductivityTooltip_OutpostUpgradesHeader".Translate().ToString(),
                "");
            for (int i = 0; i < upgs.Length; i++)
                y = DrawDetailLine(x, y, w, zebra++, upgs[i]);

            return y;
        }

        private static float DrawDetailKeyValue(float x, float y, float w, int index, string label, string value)
        {
            Rect row = new Rect(x, y, w, DetailRowH);
            if (index % 2 == 0) Widgets.DrawHighlight(row);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(x + 6f, y, w * 0.55f - 6f, DetailRowH), label);
            if (!string.IsNullOrEmpty(value))
            {
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(x + w * 0.55f, y, w * 0.45f - 6f, DetailRowH), value);
            }
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            return y + DetailRowH;
        }

        private static float DrawDetailLine(float x, float y, float w, int index, string text)
        {
            Rect row = new Rect(x, y, w, DetailRowH);
            if (index % 2 == 0) Widgets.DrawHighlight(row);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(x + 10f, y, w - 16f, DetailRowH), text);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            return y + DetailRowH;
        }

        private float DrawSpeciesList(float x, float y, float w, List<SpeciesRow> rows, string emptyText)
        {
            if (rows == null || rows.Count == 0)
            {
                Rect empty = new Rect(x, y, w, SpeciesNameH);
                Widgets.DrawHighlight(empty);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Outpost_Dialog_UI.TheoreticalLabelColor;
                Widgets.Label(new Rect(x + 8f, y, w - 16f, SpeciesNameH), emptyText);
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                return y + SpeciesNameH;
            }

            int visibleIndex = 0;
            bool any = false;
            for (int i = 0; i < rows.Count; i++)
            {
                if (!SpeciesMatchesSearch(rows[i])) continue;
                any = true;
                SpeciesRow row = rows[i];
                float rowH = SpeciesRowHeight(row);
                Rect r = new Rect(x, y, w, rowH);
                if (visibleIndex % 2 == 0) Widgets.DrawHighlight(r);

                float iconX = x + 6f;
                if (row.Icon != null)
                {
                    Rect iconRect = new Rect(iconX, y + (SpeciesNameH - SpeciesIcon) * 0.5f, SpeciesIcon, SpeciesIcon);
                    Color prev = GUI.color;
                    if (row.IconColor.HasValue) GUI.color = row.IconColor.Value;
                    Widgets.DrawTextureFitted(iconRect, row.Icon, 1f);
                    GUI.color = prev;
                }
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(iconX + SpeciesIcon + 8f, y, w - SpeciesIcon - 22f, SpeciesNameH), row.Label);
                Text.Anchor = TextAnchor.UpperLeft;

                if (!string.IsNullOrEmpty(row.YieldLine))
                {
                    float fy = y + SpeciesNameH + Outpost_Dialog_UI.ListRowFormulaTopPadding;
                    Text.Font = GameFont.Tiny;
                    GUI.color = Color.gray;
                    string[] lines = SplitLines(row.YieldLine);
                    for (int li = 0; li < lines.Length; li++)
                    {
                        Widgets.Label(new Rect(iconX + SpeciesIcon + 8f, fy, w - SpeciesIcon - 22f, SpeciesFormulaH), lines[li]);
                        fy += SpeciesFormulaH;
                    }
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                }

                y += rowH + PanelPad;
                visibleIndex++;
            }

            if (!any)
            {
                Rect empty = new Rect(x, y, w, SpeciesNameH);
                Widgets.DrawHighlight(empty);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Outpost_Dialog_UI.TheoreticalLabelColor;
                Widgets.Label(new Rect(x + 8f, y, w - 16f, SpeciesNameH),
                    "TSA_WD_TileProductivity_SearchNoMatch".Translate().ToString());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                y += SpeciesNameH;
            }
            return y;
        }
    }
}
