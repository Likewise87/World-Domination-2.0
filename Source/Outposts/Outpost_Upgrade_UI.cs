using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    internal enum UpgradeBenefitKind
    {
        Defensive,
        OffensiveRecovery,
        OccupantHeal,
        TileFertility,
        TileMining,
        TileAnimals,
        TileFish,
        MortarDamage,
        MortarHit,
        MortarCooldown,
        MortarRange,
        AntiAirUnlock,
        DecontaminationUnlock,
        ResearchEfficiency,
        ProductionEfficiency,
        WarehouseAuraBonus,
        WarehouseAuraRadius,
        RemotePower,
        RapidResponseOffense,
        AllyPullRadius,
        FoodStorageMax,
        FoodProductionFlat
    }

    internal struct AggregateBenefitLine
    {
        public string DisplayText;
        public UpgradeBenefitKind Kind;
    }

    /// <summary>Shared drawing and benefit formatting for the outpost upgrades tab.</summary>
    internal static class Outpost_Upgrade_UI
    {
        public const float DetailLineH = Outpost_Dialog_UI.OutcomeLineH;
        public const float DetailCostLineH = Outpost_Dialog_UI.YieldLineH;
        public const float DetailSectionGap = 8f;
        public const float DetailIconSize = 48f;
        public const float DetailTitleLineH = Outpost_Dialog_UI.OutcomeLineH;
        public const float DeployedStatusBoxH = 36f;
        public const float BuildButtonH = 32f;
        public const float CompactRowIconSize = 32f;
        public const float CompactRowHeight = 40f;
        public const float CompactRowPadding = 6f;
        public const float RightColHeaderH = 24f;

        private static readonly Dictionary<string, Texture2D> UpgradeTexCache = new Dictionary<string, Texture2D>();

        public static readonly Color RowBgDeployed = new Color(0.06f, 0.2f, 0.09f, 0.42f);
        public static readonly Color RowBgDim = new Color(0.12f, 0.12f, 0.12f, 0.28f);
        public static readonly Color RowBgPending = new Color(0.35f, 0.28f, 0.08f, 0.38f);
        public static readonly Color RowBgSelected = new Color(0.4f, 0.8f, 1f, 0.12f);

        public static Texture2D GetUpgradeIcon(OutpostUpgradeDef def)
        {
            if (def == null || def.imagePath.NullOrEmpty()) return null;
            if (!UpgradeTexCache.TryGetValue(def.imagePath, out Texture2D icon))
            {
                icon = ContentFinder<Texture2D>.Get(def.imagePath, false);
                UpgradeTexCache[def.imagePath] = icon;
            }
            return icon;
        }

        public static void DrawTextureTopFit(Rect outer, Texture2D tex)
        {
            if (tex == null) return;
            float tw = tex.width;
            float th = tex.height;
            if (tw <= 1e-4f || th <= 1e-4f) return;
            float scale = Mathf.Min(outer.width / tw, outer.height / th);
            float w = tw * scale;
            float h = th * scale;
            float x = outer.x + Mathf.Max(0f, (outer.width - w) * 0.5f);
            GUI.DrawTexture(new Rect(x, outer.y, w, h), tex, ScaleMode.StretchToFill, true);
        }

        /// <summary>Background tint for a compact upgrade list row (unmet buy requirements use <see cref="Outpost_Dialog_UI.DrawUnmetRequirementsRowTint"/> instead).</summary>
        public static Color? GetRowBackground(
            bool deployed,
            bool superseded,
            bool sequentialBlocked,
            bool futureTier,
            bool isPending)
        {
            if (isPending) return RowBgPending;
            if (deployed) return RowBgDeployed;
            if (superseded || sequentialBlocked || futureTier) return RowBgDim;
            return null;
        }

        public static int CountBenefitLines(OutpostUpgradeDef def)
        {
            if (def == null) return 0;
            int n = 0;
            if (def.defensiveStrengthBonus > 0f) n++;
            if (def.offensiveRecoveryBonus > 0f) n++;
            if (def.category == OutpostUpgradeCategory.Hospital && def.offensiveRecoveryBonus > 0f) n++;
            if (def.tileFertilityBonus > 0f) n++;
            if (def.tileMiningBonus > 0f) n++;
            if (def.tileAnimalAbundanceBonus > 0f) n++;
            if (def.tileFishAbundanceBonus > 0f) n++;
            if (def.mortarShellDamageBonus > 0f) n++;
            if (def.mortarHitChanceBonus > 0f) n++;
            if (def.mortarCooldownReduction > 0f) n++;
            if (def.mortarRangeBonus > 0f) n++;
            if (def.enablesAntiAir) n++;
            if (def.enablesDecontaminationCrew) n++;
            if (def.researchEfficiencyBonus > 0f) n++;
            if (def.productionEfficiencyBonus > 0f) n++;
            if (def.warehouseAuraBonus > 0f) n++;
            if (def.warehouseAuraRadiusBonus > 0f) n++;
            if (def.remotePowerWattsBonus > 0f) n++;
            if (def.rapidResponseOffensiveStrengthBonus > 0f) n++;
            if (def.allyPullRadiusBonus > 0f) n++;
            if (def.foodStorageMaxBonus > 0f) n++;
            if (def.foodProductionFlatBonus > 0f) n++;
            return n;
        }

        /// <summary>Multi-line summary of built upgrade bonuses for the left outcome box.</summary>
        public static string FormatAggregateBenefits(WorldObject_WD_Outpost outpost)
        {
            var lines = BuildAggregateBenefitLines(outpost);
            if (lines.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(lines[i].DisplayText);
            }
            return sb.ToString();
        }

        public static List<AggregateBenefitLine> BuildAggregateBenefitLines(WorldObject_WD_Outpost outpost)
        {
            var lines = new List<AggregateBenefitLine>();
            if (outpost == null) return lines;

            float defBonus = outpost.GetOutpostUpgradeDefensiveBonus();
            if (defBonus > 0.01f)
                lines.Add(MakeAggregateLine(UpgradeBenefitKind.Defensive,
                    Key("TSA_WD_OutpostUpgrades_BenefitDefensive", defBonus.ToString("F0"))));

            float offRec = outpost.GetOutpostOffensiveRecoveryMultiplierBonus();
            if (outpost.IsRapidResponseOutpost)
                offRec -= outpost.GetRapidResponseOffensiveRecoveryBonus();
            if (offRec > 1e-6f)
                lines.Add(MakeAggregateLine(UpgradeBenefitKind.OffensiveRecovery,
                    Key("TSA_WD_OutpostUpgrades_BenefitRecovery", (offRec * 100f).ToString("F0"))));

            float heal = outpost.GetHospitalOccupantHealMultiplierBonus();
            if (heal > 1e-6f)
                lines.Add(MakeAggregateLine(UpgradeBenefitKind.OccupantHeal,
                    Key("TSA_WD_OutpostUpgrades_BenefitOccupantHeal", (heal * 100f).ToString("F0"))));

            if (Outpost_Production_Utils.IsFarmingOutpost(outpost.def) || Outpost_Production_Utils.IsRanchOutpost(outpost.def))
            {
                float fert = outpost.GetBuiltUpgradeTileFertilityBonus();
                if (fert > 1e-6f)
                    lines.Add(MakeAggregateLine(UpgradeBenefitKind.TileFertility,
                        Key("TSA_WD_OutpostUpgrades_BenefitTileFertility", (fert * 100f).ToString("F0"))));
            }

            if (Outpost_Production_Utils.IsMiningOutpost(outpost.def))
            {
                float mine = outpost.GetBuiltUpgradeTileMiningBonus();
                if (mine > 1e-6f)
                    lines.Add(MakeAggregateLine(UpgradeBenefitKind.TileMining,
                        Key("TSA_WD_OutpostUpgrades_BenefitTileMining", (mine * 100f).ToString("F0"))));
            }

            if (Outpost_Production_Utils.IsHuntingOutpost(outpost.def))
            {
                float hunt = outpost.GetBuiltUpgradeTileAnimalAbundanceBonus();
                if (hunt > 1e-6f)
                    lines.Add(MakeAggregateLine(UpgradeBenefitKind.TileAnimals,
                        Key("TSA_WD_OutpostUpgrades_BenefitTileAnimals", (hunt * 100f).ToString("F0"))));
            }

            if (Outpost_Production_Utils.IsFishingOutpost(outpost.def))
            {
                float fish = outpost.GetBuiltUpgradeTileFishAbundanceBonus();
                if (fish > 1e-6f)
                    lines.Add(MakeAggregateLine(UpgradeBenefitKind.TileFish,
                        Key("TSA_WD_OutpostUpgrades_BenefitTileFish", (fish * 100f).ToString("F0"))));
            }

            if (outpost.IsMortarOutpost)
            {
                float shell = outpost.GetBuiltUpgradeMortarShellDamageBonus();
                if (shell > 1e-6f)
                    lines.Add(MakeAggregateLine(UpgradeBenefitKind.MortarDamage,
                        Key("TSA_WD_OutpostUpgrades_BenefitMortarDamage", shell.ToString("F0"))));
                float hit = outpost.GetBuiltUpgradeMortarHitChanceBonus();
                if (hit > 1e-6f)
                    lines.Add(MakeAggregateLine(UpgradeBenefitKind.MortarHit,
                        Key("TSA_WD_OutpostUpgrades_BenefitMortarHit", (hit * 100f).ToString("F0"))));
                float cd = outpost.GetBuiltUpgradeMortarCooldownReduction();
                if (cd > 1e-6f)
                    lines.Add(MakeAggregateLine(UpgradeBenefitKind.MortarCooldown,
                        Key("TSA_WD_OutpostUpgrades_BenefitMortarCooldown", (cd * 100f).ToString("F0"))));
                float range = outpost.GetBuiltUpgradeMortarRangeBonus();
                if (range > 1e-6f)
                    lines.Add(MakeAggregateLine(UpgradeBenefitKind.MortarRange,
                        Key("TSA_WD_OutpostUpgrades_BenefitMortarRange", range.ToString("F0"))));
                if (AntiAirFireUtils.HasAntiAirUpgrade(outpost))
                    lines.Add(MakeAggregateLine(UpgradeBenefitKind.AntiAirUnlock,
                        Key("TSA_WD_OutpostUpgrades_BenefitAntiAirUnlock")));
            }

            if (outpost.HasBuiltDecontaminationUnlock())
                lines.Add(MakeAggregateLine(UpgradeBenefitKind.DecontaminationUnlock,
                    Key("TSA_WD_OutpostUpgrades_BenefitDecontaminationUnlock")));

            if (outpost.IsResearchOutpost)
            {
                float res = outpost.GetResearchUpgradeEfficiencyBonus();
                if (res > 1e-6f)
                    lines.Add(MakeAggregateLine(UpgradeBenefitKind.ResearchEfficiency,
                        Key("TSA_WD_OutpostUpgrades_BenefitResearchEfficiency", (res * 100f).ToString("F0"))));
            }

            float prod = outpost.GetProductionUpgradeEfficiencyBonus();
            if (prod > 1e-6f)
                lines.Add(MakeAggregateLine(UpgradeBenefitKind.ProductionEfficiency,
                    Key("TSA_WD_OutpostUpgrades_BenefitProductionEfficiency", (prod * 100f).ToString("F0"))));

            if (Outpost_Production_Utils.IsWarehouseOutpost(outpost.def))
            {
                float auraPct = outpost.GetWarehouseAuraBonusUpgradeBonus();
                if (auraPct > 1e-6f)
                    lines.Add(MakeAggregateLine(UpgradeBenefitKind.WarehouseAuraBonus,
                        Key("TSA_WD_OutpostUpgrades_BenefitWarehouseAura", (auraPct * 100f).ToString("F0"))));
                float auraRad = outpost.GetWarehouseAuraRadiusUpgradeBonus();
                if (auraRad > 1e-6f)
                    lines.Add(MakeAggregateLine(UpgradeBenefitKind.WarehouseAuraRadius,
                        Key("TSA_WD_OutpostUpgrades_BenefitWarehouseAuraRadius", auraRad.ToString("F0"))));
            }

            if (outpost.IsPowerPlantOutpost)
            {
                float power = outpost.GetRemotePowerUpgradeBonus();
                if (power > 1e-6f)
                    lines.Add(MakeAggregateLine(UpgradeBenefitKind.RemotePower,
                        Key("TSA_WD_OutpostUpgrades_BenefitRemotePower", Outpost_PowerPlant.FormatWatts(power))));
            }

            if (outpost.IsRapidResponseOutpost)
            {
                float rr = outpost.GetBuiltUpgradeRapidResponseOffensiveStrengthBonus();
                if (rr > 1e-6f)
                    lines.Add(MakeAggregateLine(UpgradeBenefitKind.RapidResponseOffense,
                        Key("TSA_WD_OutpostUpgrades_BenefitRapidResponseOffense", (rr * 100f).ToString("F0"))));
            }

            float allyPull = outpost.GetBuiltUpgradeAllyPullRadiusBonus();
            if (allyPull > 1e-6f)
                lines.Add(MakeAggregateLine(UpgradeBenefitKind.AllyPullRadius,
                    Key("TSA_WD_OutpostUpgrades_BenefitAllyPullRadius", allyPull.ToString("F0"))));

            float storageBonus = outpost.GetBuiltUpgradeFoodStorageMaxBonus();
            if (storageBonus > 1e-6f)
                lines.Add(MakeAggregateLine(UpgradeBenefitKind.FoodStorageMax,
                    Key("TSA_WD_OutpostUpgrades_BenefitFoodStorageMax", storageBonus.ToString("F0"))));

            float flatFood = outpost.GetBuiltUpgradeFoodProductionFlatBonus();
            if (flatFood > 1e-6f)
                lines.Add(MakeAggregateLine(UpgradeBenefitKind.FoodProductionFlat,
                    Key("TSA_WD_OutpostUpgrades_BenefitFoodProductionFlat", flatFood.ToString("F0"))));

            return lines;
        }

        private static AggregateBenefitLine MakeAggregateLine(UpgradeBenefitKind kind, string display)
            => new AggregateBenefitLine { Kind = kind, DisplayText = display };

        /// <summary>Lists each built upgrade that contributes to <paramref name="kind"/>.</summary>
        public static string BuildBenefitContributorsTooltip(WorldObject_WD_Outpost outpost, UpgradeBenefitKind kind)
        {
            if (outpost?.BuiltUpgradeLevels == null || outpost.BuiltUpgradeLevels.Count == 0) return "";

            var keys = new List<string>();
            foreach (var kv in outpost.BuiltUpgradeLevels)
            {
                if (kv.Value > 0) keys.Add(kv.Key);
            }
            keys.Sort(StringComparer.OrdinalIgnoreCase);

            var sb = new StringBuilder();
            for (int i = 0; i < keys.Count; i++)
            {
                if (outpost.BuiltUpgradeLevels.TryGetValue(keys[i], out int level) && level <= 0) continue;
                OutpostUpgradeDef def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(keys[i]);
                if (def == null) continue;
                float perLevel = GetBenefitPerLevel(def, kind);
                if (Mathf.Abs(perLevel) < 1e-6f) continue;
                float total = perLevel * level;
                string signed = FormatBenefitContribution(total, kind);
                string line = Key2("TSA_WD_ProductivityTooltip_MutatorLine", def.LabelCap, signed);
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(line);
            }

            return sb.Length > 0 ? sb.ToString() : Key("TSA_WD_OutpostUpgrades_BenefitContributorsNone");
        }

        private static float GetBenefitPerLevel(OutpostUpgradeDef def, UpgradeBenefitKind kind)
        {
            if (def == null) return 0f;
            switch (kind)
            {
                case UpgradeBenefitKind.Defensive:
                    return def.defensiveStrengthBonus;
                case UpgradeBenefitKind.OffensiveRecovery:
                    return def.offensiveRecoveryBonus;
                case UpgradeBenefitKind.OccupantHeal:
                    return def.category == OutpostUpgradeCategory.Hospital ? def.offensiveRecoveryBonus : 0f;
                case UpgradeBenefitKind.TileFertility:
                    return def.tileFertilityBonus;
                case UpgradeBenefitKind.TileMining:
                    return def.tileMiningBonus;
                case UpgradeBenefitKind.TileAnimals:
                    return def.tileAnimalAbundanceBonus;
                case UpgradeBenefitKind.TileFish:
                    return def.tileFishAbundanceBonus;
                case UpgradeBenefitKind.MortarDamage:
                    return def.mortarShellDamageBonus;
                case UpgradeBenefitKind.MortarHit:
                    return def.mortarHitChanceBonus;
                case UpgradeBenefitKind.MortarCooldown:
                    return def.mortarCooldownReduction;
                case UpgradeBenefitKind.MortarRange:
                    return def.mortarRangeBonus;
                case UpgradeBenefitKind.AntiAirUnlock:
                    return def.enablesAntiAir ? 1f : 0f;
                case UpgradeBenefitKind.DecontaminationUnlock:
                    return def.enablesDecontaminationCrew ? 1f : 0f;
                case UpgradeBenefitKind.ResearchEfficiency:
                    return def.researchEfficiencyBonus;
                case UpgradeBenefitKind.ProductionEfficiency:
                    return def.productionEfficiencyBonus;
                case UpgradeBenefitKind.WarehouseAuraBonus:
                    return def.warehouseAuraBonus;
                case UpgradeBenefitKind.WarehouseAuraRadius:
                    return def.warehouseAuraRadiusBonus;
                case UpgradeBenefitKind.RemotePower:
                    return def.remotePowerWattsBonus;
                case UpgradeBenefitKind.RapidResponseOffense:
                    return def.rapidResponseOffensiveStrengthBonus;
                case UpgradeBenefitKind.AllyPullRadius:
                    return def.allyPullRadiusBonus;
                case UpgradeBenefitKind.FoodStorageMax:
                    return def.foodStorageMaxBonus;
                case UpgradeBenefitKind.FoodProductionFlat:
                    return def.foodProductionFlatBonus;
                default:
                    return 0f;
            }
        }

        private static bool IsPercentBenefitKind(UpgradeBenefitKind kind)
        {
            switch (kind)
            {
                case UpgradeBenefitKind.Defensive:
                case UpgradeBenefitKind.MortarDamage:
                case UpgradeBenefitKind.MortarRange:
                case UpgradeBenefitKind.RemotePower:
                case UpgradeBenefitKind.WarehouseAuraRadius:
                case UpgradeBenefitKind.AllyPullRadius:
                case UpgradeBenefitKind.FoodStorageMax:
                case UpgradeBenefitKind.FoodProductionFlat:
                case UpgradeBenefitKind.AntiAirUnlock:
                case UpgradeBenefitKind.DecontaminationUnlock:
                    return false;
                default:
                    return true;
            }
        }

        private static string FormatBenefitContribution(float value, UpgradeBenefitKind kind)
        {
            if (kind == UpgradeBenefitKind.RemotePower)
                return (value >= 0f ? "+" : "") + Outpost_PowerPlant.FormatWatts(value);
            if (kind == UpgradeBenefitKind.WarehouseAuraRadius)
                return (value >= 0f ? "+" : "") + value.ToString("F0") + " tiles";
            if (kind == UpgradeBenefitKind.AllyPullRadius)
                return (value >= 0f ? "+" : "") + value.ToString("F0") + " tiles";
            if (IsPercentBenefitKind(kind))
            {
                int pp = Mathf.RoundToInt(value * 100f);
                return (pp >= 0 ? "+" : "") + pp + "pp";
            }
            return (value >= 0f ? "+" : "") + value.ToString("F0");
        }

        private static string BuildSingleUpgradeBenefitTooltip(OutpostUpgradeDef def, UpgradeBenefitKind kind, int level)
        {
            float total = GetBenefitPerLevel(def, kind) * level;
            if (Mathf.Abs(total) < 1e-6f) return "";
            return Key2("TSA_WD_ProductivityTooltip_MutatorLine", def.LabelCap, FormatBenefitContribution(total, kind));
        }

        public static float MeasureAggregateBoxHeight(WorldObject_WD_Outpost outpost)
        {
            const float lineH = Outpost_Dialog_UI.OutcomeLineH;
            const float boxPad = Outpost_Dialog_UI.OutcomeBoxPad;
            int lineCount = BuildAggregateBenefitLines(outpost).Count;
            if (lineCount == 0)
                return boxPad * 2f + lineH + Outpost_Dialog_UI.YieldLineH;
            float valueH = lineCount * Outpost_Dialog_UI.YieldLineH;
            return boxPad * 2f + lineH + valueH;
        }

        /// <summary>Draws the total-benefits outcome box; returns y below the box.</summary>
        public static float DrawAggregateBenefitsBox(float x, float y, float w, WorldObject_WD_Outpost outpost)
        {
            var benefitLines = BuildAggregateBenefitLines(outpost);
            float boxH = MeasureAggregateBoxHeight(outpost);
            Outpost_Dialog_UI.DrawOutcomeBox(new Rect(x, y, w, boxH));
            float cy = y + Outpost_Dialog_UI.OutcomeBoxPad;
            float ix = x + Outpost_Dialog_UI.OutcomeBoxPad;
            float iw = w - Outpost_Dialog_UI.OutcomeBoxPad * 2f;
            const float lineH = Outpost_Dialog_UI.OutcomeLineH;
            float valueX = ix + Outpost_Dialog_UI.OutcomeValueIndent;
            float valueW = iw - Outpost_Dialog_UI.OutcomeValueIndent;

            Widgets.Label(new Rect(ix, cy, iw, lineH), Key("TSA_WD_OutpostUpgrades_TotalBenefits"));
            cy += lineH;

            if (benefitLines.Count == 0)
            {
                GUI.color = Outpost_Dialog_UI.OutcomeValueColor;
                Widgets.Label(new Rect(valueX, cy, valueW, Outpost_Dialog_UI.YieldLineH),
                    Key("TSA_WD_OutpostUpgrades_NoUpgradesDeployedYet"));
                GUI.color = Color.white;
            }
            else
            {
                Text.Font = GameFont.Small;
                for (int i = 0; i < benefitLines.Count; i++)
                {
                    AggregateBenefitLine line = benefitLines[i];
                    Rect lineRect = new Rect(valueX, cy, valueW, Outpost_Dialog_UI.YieldLineH);
                    GUI.color = Outpost_Dialog_UI.OutcomeValueColor;
                    Widgets.Label(lineRect, line.DisplayText);
                    GUI.color = Color.white;
                    string tip = BuildBenefitContributorsTooltip(outpost, line.Kind);
                    if (!string.IsNullOrEmpty(tip))
                        TooltipHandler.TipRegion(lineRect, tip);
                    cy += Outpost_Dialog_UI.YieldLineH;
                }
            }

            return y + boxH;
        }

        /// <summary>Draw benefit lines for one upgrade def; returns y below last line.</summary>
        public static float DrawBenefitLines(
            float x,
            float y,
            float w,
            OutpostUpgradeDef def,
            bool dimmed,
            bool benefitsActive,
            WorldObject_WD_Outpost outpost = null)
        {
            if (def == null || CountBenefitLines(def) == 0)
            {
                GUI.color = Color.gray;
                LabelAnchored(new Rect(x, y, w, DetailLineH), Key("TSA_WD_OutpostUpgrades_NoBenefitsListed"), TextAnchor.MiddleLeft);
                GUI.color = Color.white;
                return y + DetailLineH;
            }

            Color benefitColor = dimmed ? Color.gray : benefitsActive ? Color.green : Color.white;

            if (def.defensiveStrengthBonus > 0f)
                y = DrawBenefitLine(x, y, w,
                    Key("TSA_WD_OutpostUpgrades_BenefitDefensive", def.defensiveStrengthBonus.ToString("F0")),
                    benefitColor,
                    ResolveBenefitTooltip(outpost, def, benefitsActive, UpgradeBenefitKind.Defensive));
            if (def.offensiveRecoveryBonus > 0f)
            {
                y = DrawBenefitLine(x, y, w,
                    Key("TSA_WD_OutpostUpgrades_BenefitRecovery", (def.offensiveRecoveryBonus * 100f).ToString("F0")),
                    benefitColor,
                    ResolveBenefitTooltip(outpost, def, benefitsActive, UpgradeBenefitKind.OffensiveRecovery));
                if (def.category == OutpostUpgradeCategory.Hospital)
                    y = DrawBenefitLine(x, y, w,
                        Key("TSA_WD_OutpostUpgrades_BenefitOccupantHeal", (def.offensiveRecoveryBonus * 100f).ToString("F0")),
                        benefitColor,
                        ResolveBenefitTooltip(outpost, def, benefitsActive, UpgradeBenefitKind.OccupantHeal));
            }
            if (def.tileFertilityBonus > 0f)
                y = DrawBenefitLine(x, y, w,
                    Key("TSA_WD_OutpostUpgrades_BenefitTileFertility", (def.tileFertilityBonus * 100f).ToString("F0")),
                    benefitColor,
                    ResolveBenefitTooltip(outpost, def, benefitsActive, UpgradeBenefitKind.TileFertility));
            if (def.tileMiningBonus > 0f)
                y = DrawBenefitLine(x, y, w,
                    Key("TSA_WD_OutpostUpgrades_BenefitTileMining", (def.tileMiningBonus * 100f).ToString("F0")),
                    benefitColor,
                    ResolveBenefitTooltip(outpost, def, benefitsActive, UpgradeBenefitKind.TileMining));
            if (def.tileAnimalAbundanceBonus > 0f)
                y = DrawBenefitLine(x, y, w,
                    Key("TSA_WD_OutpostUpgrades_BenefitTileAnimals", (def.tileAnimalAbundanceBonus * 100f).ToString("F0")),
                    benefitColor,
                    ResolveBenefitTooltip(outpost, def, benefitsActive, UpgradeBenefitKind.TileAnimals));
            if (def.tileFishAbundanceBonus > 0f)
                y = DrawBenefitLine(x, y, w,
                    Key("TSA_WD_OutpostUpgrades_BenefitTileFish", (def.tileFishAbundanceBonus * 100f).ToString("F0")),
                    benefitColor,
                    ResolveBenefitTooltip(outpost, def, benefitsActive, UpgradeBenefitKind.TileFish));
            if (def.mortarShellDamageBonus > 0f)
                y = DrawBenefitLine(x, y, w,
                    Key("TSA_WD_OutpostUpgrades_BenefitMortarDamage", def.mortarShellDamageBonus.ToString("F0")),
                    benefitColor,
                    ResolveBenefitTooltip(outpost, def, benefitsActive, UpgradeBenefitKind.MortarDamage));
            if (def.mortarHitChanceBonus > 0f)
                y = DrawBenefitLine(x, y, w,
                    Key("TSA_WD_OutpostUpgrades_BenefitMortarHit", (def.mortarHitChanceBonus * 100f).ToString("F0")),
                    benefitColor,
                    ResolveBenefitTooltip(outpost, def, benefitsActive, UpgradeBenefitKind.MortarHit));
            if (def.mortarCooldownReduction > 0f)
                y = DrawBenefitLine(x, y, w,
                    Key("TSA_WD_OutpostUpgrades_BenefitMortarCooldown", (def.mortarCooldownReduction * 100f).ToString("F0")),
                    benefitColor,
                    ResolveBenefitTooltip(outpost, def, benefitsActive, UpgradeBenefitKind.MortarCooldown));
            if (def.mortarRangeBonus > 0f)
                y = DrawBenefitLine(x, y, w,
                    Key("TSA_WD_OutpostUpgrades_BenefitMortarRange", def.mortarRangeBonus.ToString("F0")),
                    benefitColor,
                    ResolveBenefitTooltip(outpost, def, benefitsActive, UpgradeBenefitKind.MortarRange));
            if (def.enablesAntiAir)
                y = DrawBenefitLine(x, y, w,
                    Key("TSA_WD_OutpostUpgrades_BenefitAntiAirUnlock"),
                    benefitColor,
                    ResolveBenefitTooltip(outpost, def, benefitsActive, UpgradeBenefitKind.AntiAirUnlock));
            if (def.enablesDecontaminationCrew)
                y = DrawBenefitLine(x, y, w,
                    Key("TSA_WD_OutpostUpgrades_BenefitDecontaminationUnlock"),
                    benefitColor,
                    ResolveBenefitTooltip(outpost, def, benefitsActive, UpgradeBenefitKind.DecontaminationUnlock));
            if (def.researchEfficiencyBonus > 0f)
                y = DrawBenefitLine(x, y, w,
                    Key("TSA_WD_OutpostUpgrades_BenefitResearchEfficiency", (def.researchEfficiencyBonus * 100f).ToString("F0")),
                    benefitColor,
                    ResolveBenefitTooltip(outpost, def, benefitsActive, UpgradeBenefitKind.ResearchEfficiency));
            if (def.productionEfficiencyBonus > 0f)
                y = DrawBenefitLine(x, y, w,
                    Key("TSA_WD_OutpostUpgrades_BenefitProductionEfficiency", (def.productionEfficiencyBonus * 100f).ToString("F0")),
                    benefitColor,
                    ResolveBenefitTooltip(outpost, def, benefitsActive, UpgradeBenefitKind.ProductionEfficiency));
            if (def.warehouseAuraBonus > 0f)
                y = DrawBenefitLine(x, y, w,
                    Key("TSA_WD_OutpostUpgrades_BenefitWarehouseAura", (def.warehouseAuraBonus * 100f).ToString("F0")),
                    benefitColor,
                    ResolveBenefitTooltip(outpost, def, benefitsActive, UpgradeBenefitKind.WarehouseAuraBonus));
            if (def.warehouseAuraRadiusBonus > 0f)
                y = DrawBenefitLine(x, y, w,
                    Key("TSA_WD_OutpostUpgrades_BenefitWarehouseAuraRadius", def.warehouseAuraRadiusBonus.ToString("F0")),
                    benefitColor,
                    ResolveBenefitTooltip(outpost, def, benefitsActive, UpgradeBenefitKind.WarehouseAuraRadius));
            if (def.remotePowerWattsBonus > 0f)
                y = DrawBenefitLine(x, y, w,
                    Key("TSA_WD_OutpostUpgrades_BenefitRemotePower", Outpost_PowerPlant.FormatWatts(def.remotePowerWattsBonus)),
                    benefitColor,
                    ResolveBenefitTooltip(outpost, def, benefitsActive, UpgradeBenefitKind.RemotePower));
            if (def.rapidResponseOffensiveStrengthBonus > 0f)
                y = DrawBenefitLine(x, y, w,
                    Key("TSA_WD_OutpostUpgrades_BenefitRapidResponseOffense", (def.rapidResponseOffensiveStrengthBonus * 100f).ToString("F0")),
                    benefitColor,
                    ResolveBenefitTooltip(outpost, def, benefitsActive, UpgradeBenefitKind.RapidResponseOffense));
            if (def.allyPullRadiusBonus > 0f)
                y = DrawBenefitLine(x, y, w,
                    Key("TSA_WD_OutpostUpgrades_BenefitAllyPullRadius", def.allyPullRadiusBonus.ToString("F0")),
                    benefitColor,
                    ResolveBenefitTooltip(outpost, def, benefitsActive, UpgradeBenefitKind.AllyPullRadius));
            if (def.foodStorageMaxBonus > 0f)
                y = DrawBenefitLine(x, y, w,
                    Key("TSA_WD_OutpostUpgrades_BenefitFoodStorageMax", def.foodStorageMaxBonus.ToString("F0")),
                    benefitColor,
                    ResolveBenefitTooltip(outpost, def, benefitsActive, UpgradeBenefitKind.FoodStorageMax));
            if (def.foodProductionFlatBonus > 0f)
                y = DrawBenefitLine(x, y, w,
                    Key("TSA_WD_OutpostUpgrades_BenefitFoodProductionFlat", def.foodProductionFlatBonus.ToString("F0")),
                    benefitColor,
                    ResolveBenefitTooltip(outpost, def, benefitsActive, UpgradeBenefitKind.FoodProductionFlat));

            return y;
        }

        private static string ResolveBenefitTooltip(
            WorldObject_WD_Outpost outpost,
            OutpostUpgradeDef def,
            bool benefitsActive,
            UpgradeBenefitKind kind)
        {
            if (benefitsActive && outpost != null)
                return BuildBenefitContributorsTooltip(outpost, kind);
            return BuildSingleUpgradeBenefitTooltip(def, kind, 1);
        }

        private static float DrawBenefitLine(float x, float y, float w, string text, Color color, string tooltip)
        {
            Rect rect = new Rect(x, y, w, DetailLineH);
            LabelAnchored(rect, text.Colorize(color), TextAnchor.MiddleLeft);
            if (!string.IsNullOrEmpty(tooltip))
                TooltipHandler.TipRegion(rect, tooltip);
            return y + DetailLineH;
        }

        public static float DrawCostSection(float x, float y, float w, OutpostUpgradeDef def, Dictionary<string, bool> availabilityByDefName, bool greyedOut)
        {
            GUI.color = Outpost_Dialog_UI.TheoreticalLabelColor;
            LabelAnchored(new Rect(x, y, w, DetailLineH), Key("TSA_WD_OutpostUpgrades_ColumnCost"), TextAnchor.MiddleLeft);
            GUI.color = Color.white;
            y += DetailLineH;

            if (def == null)
                return y;

            return DrawCostList(def.GetEffectiveCost(), availabilityByDefName, x, y, w, greyedOut);
        }

        public static float DrawResearchSection(float x, float y, float w, OutpostUpgradeDef def, bool greyedOut)
        {
            GUI.color = Outpost_Dialog_UI.TheoreticalLabelColor;
            LabelAnchored(new Rect(x, y, w, DetailLineH), Key("TSA_WD_OutpostUpgrades_ColumnResearch"), TextAnchor.MiddleLeft);
            GUI.color = Color.white;
            y += DetailLineH;

            if (def == null)
                return y;

            bool anyResearch = false;
            foreach (string research in OutpostUpgradeUtility.GetResearchRequirements(def))
            {
                anyResearch = true;
                ResearchProjectDef rp = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(research);
                bool completed = !greyedOut && (rp == null || (Find.ResearchManager != null && Find.ResearchManager.GetProgress(rp) >= rp.baseCost));
                string label = rp != null ? rp.LabelCap : research;
                Color lineColor = greyedOut ? Color.gray : (completed ? Color.green : Color.yellow);
                LabelAnchored(new Rect(x, y, w, DetailCostLineH), label.Colorize(lineColor), TextAnchor.UpperLeft);
                y += DetailCostLineH;
            }

            if (!anyResearch)
            {
                Color lineColor = greyedOut ? Color.gray : Color.green;
                LabelAnchored(new Rect(x, y, w, DetailCostLineH),
                    Key("TSA_WD_OutpostUpgrades_ResearchNone").Colorize(lineColor), TextAnchor.UpperLeft);
                y += DetailCostLineH;
            }

            return y;
        }

        private static float DrawCostList(List<OutpostUpgradeCostEntry> costs, Dictionary<string, bool> availabilityByDefName, float x, float y, float w, bool greyedOut)
        {
            if (costs == null || costs.Count == 0)
            {
                LabelAnchored(new Rect(x, y, w, DetailCostLineH),
                    Key("TSA_WD_OutpostUpgrades_NoCost").Colorize(Color.gray), TextAnchor.UpperLeft);
                return y + DetailCostLineH;
            }

            int drawn = 0;
            for (int i = 0; i < costs.Count; i++)
            {
                OutpostUpgradeCostEntry c = costs[i];
                if (c?.thingDef == null || c.count <= 0) continue;
                drawn++;
                bool ok = !greyedOut
                    && availabilityByDefName != null
                    && availabilityByDefName.TryGetValue(c.thingDef.defName, out bool v) && v;
                string costLabel = OutpostUpgradeUtility.GetCostDisplayLabel(c);
                Color lineColor = greyedOut ? Color.gray : (ok ? Color.green : Color.yellow);
                LabelAnchored(new Rect(x, y, w, DetailCostLineH),
                    ("• " + costLabel + " x" + c.count).Colorize(lineColor),
                    TextAnchor.UpperLeft);
                y += DetailCostLineH;
            }

            if (drawn == 0)
            {
                LabelAnchored(new Rect(x, y, w, DetailCostLineH),
                    Key("TSA_WD_OutpostUpgrades_NoCost").Colorize(Color.gray), TextAnchor.UpperLeft);
                y += DetailCostLineH;
            }

            return y;
        }

        /// <summary>Estimated scroll content height for the selected-upgrade detail block.</summary>
        public static float MeasureSelectedUpgradeDetailHeight(OutpostUpgradeDef def, bool hasDescription)
        {
            if (def == null) return DetailLineH;
            float h = DetailTitleLineH;
            if (hasDescription && !def.description.NullOrEmpty())
                h += Text.CalcHeight(def.description, 200f) + 6f;
            else
                h += 6f;
            h = Mathf.Max(h, DetailIconSize + 4f);
            h += DetailSectionGap;
            h += BuildButtonH + DetailSectionGap; // status / build under icon + description
            h += DetailLineH; // benefits header
            int benefitLines = Mathf.Max(1, CountBenefitLines(def));
            h += benefitLines * DetailLineH;
            h += DetailSectionGap;
            h += DetailLineH; // cost header
            int costLines = 1;
            List<OutpostUpgradeCostEntry> effectiveCost = def.GetEffectiveCost();
            if (effectiveCost != null)
            {
                costLines = 0;
                for (int i = 0; i < effectiveCost.Count; i++)
                    if (effectiveCost[i]?.thingDef != null && effectiveCost[i].count > 0) costLines++;
                if (costLines == 0) costLines = 1;
            }
            h += costLines * DetailCostLineH;
            h += DetailSectionGap;
            h += DetailLineH; // research header
            h += Mathf.Max(1, OutpostUpgradeUtility.CountResearchRequirements(def)) * DetailCostLineH;
            return h;
        }

        private static float DrawDeployedStatusBox(float x, float y, float w)
        {
            float boxH = DeployedStatusBoxH;
            Rect boxRect = new Rect(x, y, w, boxH);
            Widgets.DrawBoxSolid(boxRect, RowBgDeployed);
            Widgets.DrawBox(boxRect);
            GUI.color = Color.green;
            LabelAnchored(boxRect, Key("TSA_WD_OutpostUpgrades_UpgradeDeployed"), TextAnchor.MiddleCenter);
            GUI.color = Color.white;
            return y + boxH;
        }

        /// <summary>Draw selected-upgrade detail in the left column scroll view; returns content height used.</summary>
        public static float DrawSelectedUpgradeDetail(
            float x,
            float y,
            float w,
            WorldObject_WD_Outpost outpost,
            OutpostUpgradeDef def,
            bool deployed,
            bool superseded,
            bool sequentialBlocked,
            bool futureTier,
            bool showBuy,
            bool canBuy,
            bool isPending,
            OutpostUpgradeUtility.PurchaseCheck check,
            System.Action onBuild)
        {
            if (def == null)
            {
                LabelAnchored(new Rect(x, y, w, DetailLineH), Key("TSA_WD_OutpostUpgrades_NoSelection"), TextAnchor.MiddleLeft);
                return y + DetailLineH;
            }

            float top = y;
            Texture2D icon = GetUpgradeIcon(def);
            if (icon != null)
            {
                Rect iconRect = new Rect(x, y, DetailIconSize, DetailIconSize);
                DrawTextureTopFit(iconRect.ContractedBy(3f), icon);
            }

            float textX = x + DetailIconSize + 8f;
            float textW = w - DetailIconSize - 8f;
            LabelAnchored(new Rect(textX, y, textW, DetailTitleLineH), def.LabelCap, TextAnchor.MiddleLeft);
            y += DetailTitleLineH;
            if (!def.description.NullOrEmpty())
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                float descH = Mathf.Max(Outpost_Dialog_UI.YieldLineH, Text.CalcHeight(def.description, textW));
                Widgets.Label(new Rect(textX, y, textW, descH), def.description);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                y += descH + 6f;
            }
            else
            {
                y += 6f;
            }

            y = Mathf.Max(y, top + DetailIconSize + 4f);
            y += DetailSectionGap;

            // Action / status directly under icon + description (same pattern as establishment UI).
            if (isPending)
            {
                GUI.color = new Color(0.95f, 0.75f, 0.35f);
                LabelAnchored(new Rect(x, y, w, Outpost_Dialog_UI.OutcomeLineH), Key("TSA_WD_OutpostUpgrades_AwaitingDelivery"), TextAnchor.MiddleCenter);
                GUI.color = Color.white;
                y += Outpost_Dialog_UI.OutcomeLineH + DetailSectionGap;
            }
            else if (deployed)
            {
                y = DrawDeployedStatusBox(x, y, w) + DetailSectionGap;
            }
            else if (superseded)
            {
                GUI.color = Color.gray;
                LabelAnchored(new Rect(x, y, w, Outpost_Dialog_UI.OutcomeLineH), Key("TSA_WD_OutpostUpgrades_InferiorOption"), TextAnchor.MiddleCenter);
                GUI.color = Color.white;
                y += Outpost_Dialog_UI.OutcomeLineH + DetailSectionGap;
            }
            else if (sequentialBlocked)
            {
                LabelAnchored(new Rect(x, y, w, Outpost_Dialog_UI.OutcomeLineH), Key("TSA_WD_OutpostUpgrades_PreviousUpgradeNeeded"), TextAnchor.MiddleCenter);
                y += Outpost_Dialog_UI.OutcomeLineH + DetailSectionGap;
            }
            else if (futureTier)
            {
                GUI.color = Color.gray;
                Widgets.ButtonText(new Rect(x, y, w, BuildButtonH), Key("TSA_WD_OutpostUpgrades_ButtonMax"));
                GUI.color = Color.white;
                y += BuildButtonH + DetailSectionGap;
            }
            else if (showBuy)
            {
                GUI.enabled = canBuy;
                if (Widgets.ButtonText(new Rect(x, y, w, BuildButtonH), Key("TSA_WD_OutpostUpgrades_ButtonBuild")))
                    onBuild?.Invoke();
                GUI.enabled = true;
                if (!canBuy && !string.IsNullOrEmpty(check.reason))
                    TooltipHandler.TipRegion(new Rect(x, y, w, BuildButtonH), check.reason);
                y += BuildButtonH + DetailSectionGap;
            }

            GUI.color = Outpost_Dialog_UI.TheoreticalLabelColor;
            LabelAnchored(new Rect(x, y, w, DetailLineH), Key("TSA_WD_OutpostUpgrades_ColumnBenefits"), TextAnchor.MiddleLeft);
            GUI.color = Color.white;
            y += DetailLineH;

            bool dimBenefits = superseded || sequentialBlocked || futureTier;
            y = DrawBenefitLines(x, y, w, def, dimBenefits, deployed, outpost);
            y += DetailSectionGap;

            bool greyCostResearch = deployed || superseded || futureTier || isPending;
            y = DrawCostSection(x, y, w, def, check.costAvailableByDefName, greyCostResearch);
            y += DetailSectionGap;
            y = DrawResearchSection(x, y, w, def, greyCostResearch);

            return y;
        }

        private static void LabelAnchored(Rect rect, string text, TextAnchor anchor)
        {
            TextAnchor prev = Text.Anchor;
            Text.Anchor = anchor;
            Widgets.Label(rect, text);
            Text.Anchor = prev;
        }

        private static string Key(string translationKey) => OutpostTranslationUtil.Key(translationKey);

        private static string Key(string translationKey, string arg0) => OutpostTranslationUtil.Key(translationKey, arg0);

        private static string Key2(string translationKey, string arg0, string arg1)
            => OutpostTranslationUtil.Key(translationKey, arg0, arg1);
    }
}
