using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>Shared raid UI: power boxes, efficiency/win chance, breakdown scrolls. Single point of truth for attempt/resolution/outpost preview.</summary>
    [StaticConstructorOnStartup]
    public static class RaidUIUtils
    {
        private static readonly Color BoxBgColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
        private const float BarFillAlpha = 0.9f;
        private static readonly Color ForecastWinGreen = new Color(0.28f, 0.78f, 0.32f);
        private static readonly Color ForecastLossRed = new Color(0.82f, 0.18f, 0.16f);
        private static Texture2D cachedWinBarTex;
        internal static Texture2D CachedWinBarTexture
        {
            get
            {
                if (cachedWinBarTex == null) cachedWinBarTex = SolidColorMaterials.NewSolidColorTexture(ForecastWinGreen);
                return cachedWinBarTex;
            }
        }

        private static Texture2D cachedDefeatBarTex;
        public static Texture2D GetBarTexture(bool victory)
        {
            if (victory) return CachedWinBarTexture;
            if (cachedDefeatBarTex == null) cachedDefeatBarTex = SolidColorMaterials.NewSolidColorTexture(ColorLibrary.RedReadable);
            return cachedDefeatBarTex;
        }

        public static void DrawRaidPowerBoxes(Rect rect, float atkPower, float defPower, string atkLabelKey = "TSA_WD_AttackerPower", string defLabelKey = "TSA_WD_DefenderPower")
        {
            Rect atkRect = rect.LeftHalf().ContractedBy(5f);
            Rect defRect = rect.RightHalf().ContractedBy(5f);
            Widgets.DrawBoxSolid(atkRect, BoxBgColor);
            Widgets.DrawBoxSolid(defRect, BoxBgColor);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(atkRect.TopHalf(), atkLabelKey.Translate());
            Text.Font = GameFont.Medium;
            Widgets.Label(atkRect.BottomHalf(), atkPower.ToString("F0"));
            Text.Font = GameFont.Small;
            Widgets.Label(defRect.TopHalf(), defLabelKey.Translate());
            Text.Font = GameFont.Medium;
            Widgets.Label(defRect.BottomHalf(), defPower.ToString("F0"));
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private static Color WithBarFillAlpha(Color color) => new Color(color.r, color.g, color.b, BarFillAlpha);

        private static void DrawFilledBarSegment(ref float x, Rect rect, float share, Color color, string tooltip = null)
        {
            if (share <= 0.001f) return;
            float w = rect.width * share;
            Rect seg = new Rect(x, rect.y, w, rect.height);
            Widgets.DrawBoxSolid(seg, WithBarFillAlpha(color));
            if (tooltip != null)
                TooltipHandler.TipRegion(seg, tooltip);
            x += w;
        }

        public static void DrawWinChanceBar(Rect rect, float winChance, string winTooltip = null, string lossTooltip = null)
        {
            winChance = Mathf.Clamp01(winChance);
            float x = rect.x;
            DrawFilledBarSegment(ref x, rect, winChance, ForecastWinGreen, winTooltip);
            DrawFilledBarSegment(ref x, rect, 1f - winChance, ForecastLossRed, lossTooltip);
        }

        public static void DrawRaidEfficiencyAndWinChance(Listing_Standard listing, float efficiency, float winChance, bool showEfficiency, bool showBar, Color? efficiencyColor = null)
        {
            if (showEfficiency)
            {
                Color effColor = efficiencyColor ?? (efficiency > 0.8f ? Color.green : (efficiency > 0.5f ? Color.yellow : Color.red));
                GUI.color = effColor;
                listing.Label("TSA_WD_PredictedEfficiency".Translate() + ": " + efficiency.ToStringPercent());
                GUI.color = Color.white;
            }
            listing.Label("TSA_WD_WinChance".Translate() + ": " + winChance.ToStringPercent());
            if (showBar)
            {
                Rect barRect = listing.GetRect(22f);
                DrawWinChanceBar(barRect, winChance);
            }
        }

        public static void DrawRaidBreakdownScrolls(Rect rect, List<string> atkDetails, List<string> defDetails, ref Vector2 scrollAtk, ref Vector2 scrollDef)
        {
            DrawDetailScroll(rect.LeftHalf().ContractedBy(2f), ref scrollAtk, atkDetails);
            DrawDetailScroll(rect.RightHalf().ContractedBy(2f), ref scrollDef, defDetails);
        }

        /// <summary>
        /// Embassy-style force lists: faction icon, name, compact +strength. Optional click-to-toggle on attacker allies.
        /// Falls back to string scrolls when both structured lists are empty.
        /// </summary>
        public static void DrawRaidForceBreakdownScrolls(
            Rect rect,
            List<RaidForceRow> atkRows,
            List<RaidForceRow> defRows,
            List<string> atkFallback,
            List<string> defFallback,
            ref Vector2 scrollAtk,
            ref Vector2 scrollDef,
            System.Action<RaidForceRow> onToggleAlly = null,
            System.Func<bool> areAllAttackerAlliesSelected = null,
            System.Action onToggleSelectAllAttackerAllies = null)
        {
            bool useStructured = (atkRows != null && atkRows.Count > 0) || (defRows != null && defRows.Count > 0);
            if (!useStructured)
            {
                DrawRaidBreakdownScrolls(rect, atkFallback, defFallback, ref scrollAtk, ref scrollDef);
                return;
            }

            // Blue selected tint + supporting-allies header only on the attacker (left) column when toggles are live.
            bool interactiveAtk = onToggleAlly != null;
            DrawForceRowScroll(
                rect.LeftHalf().ContractedBy(2f),
                ref scrollAtk,
                atkRows,
                onToggleAlly,
                applySelectedTint: true,
                showSupportingAlliesSection: interactiveAtk,
                areAllAttackerAlliesSelected,
                onToggleSelectAllAttackerAllies);
            DrawForceRowScroll(
                rect.RightHalf().ContractedBy(2f),
                ref scrollDef,
                defRows,
                null,
                applySelectedTint: false,
                showSupportingAlliesSection: false,
                null,
                null);
        }

        public const float RaidForceRowHeight = 28f;
        public const float RaidForceIconSize = 28f;
        private const float RaidForceStrengthRightPad = 5f;
        private const float RaidForceSectionHeaderH = 24f;
        private const float RaidForceSeparatorGap = 8f;

        private static void DrawForceRowScroll(
            Rect rect,
            ref Vector2 scroll,
            List<RaidForceRow> rows,
            System.Action<RaidForceRow> onToggleAlly,
            bool applySelectedTint,
            bool showSupportingAlliesSection,
            System.Func<bool> areAllAlliesSelected,
            System.Action onToggleSelectAllAllies)
        {
            Widgets.DrawMenuSection(rect);
            if (rows == null || rows.Count == 0)
            {
                Text.Font = GameFont.Tiny;
                Widgets.Label(rect.ContractedBy(6f), "(" + "None".Translate() + ")");
                Text.Font = GameFont.Small;
                return;
            }

            float width = rect.width - 16f;
            int allyCount = 0;
            if (showSupportingAlliesSection)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i] != null && !rows[i].IsPrimary)
                        allyCount++;
                }
            }

            bool useSection = showSupportingAlliesSection && allyCount > 0;
            float contentH = rows.Count * RaidForceRowHeight + 8f;
            if (useSection)
                contentH += RaidForceSeparatorGap * 2f + RaidForceSectionHeaderH;

            Rect viewRect = new Rect(0f, 0f, width, contentH);
            Widgets.BeginScrollView(rect, ref scroll, viewRect);
            float y = 4f;

            if (!useSection)
            {
                for (int i = 0; i < rows.Count; i++)
                    y = DrawForceRow(y, width, rows[i], onToggleAlly, applySelectedTint);
            }
            else
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i] != null && rows[i].IsPrimary)
                        y = DrawForceRow(y, width, rows[i], onToggleAlly, applySelectedTint);
                }

                y += RaidForceSeparatorGap * 0.35f;
                Widgets.DrawLineHorizontal(4f, y, width - 8f);
                y += RaidForceSeparatorGap * 0.65f;

                y = DrawSupportingAlliesHeader(y, width, allyCount, areAllAlliesSelected, onToggleSelectAllAllies);

                y += RaidForceSeparatorGap * 0.35f;
                Widgets.DrawLineHorizontal(4f, y, width - 8f);
                y += RaidForceSeparatorGap * 0.65f;

                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i] != null && !rows[i].IsPrimary)
                        y = DrawForceRow(y, width, rows[i], onToggleAlly, applySelectedTint);
                }
            }

            Widgets.EndScrollView();
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private static float DrawSupportingAlliesHeader(
            float y,
            float width,
            int allyCount,
            System.Func<bool> areAllAlliesSelected,
            System.Action onToggleSelectAllAllies)
        {
            Rect headerRect = new Rect(0f, y, width, RaidForceSectionHeaderH);
            if (Mouse.IsOver(headerRect))
                Widgets.DrawHighlight(headerRect);

            // Same column as DrawForceRow faction icons (outer at x=4, size RaidForceIconSize).
            const float iconColX = 4f;
            const float box = 18f;
            float cx = iconColX + (RaidForceIconSize - box) * 0.5f;
            float cy = y + (RaidForceSectionHeaderH - box) * 0.5f;
            bool allSelected = areAllAlliesSelected != null && areAllAlliesSelected();
            Widgets.CheckboxDraw(cx, cy, allSelected, allyCount == 0, box);

            // Same left edge as settlement name labels in DrawForceRow.
            float nameX = RaidForceIconSize + 10f;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.gray;
            Rect labelRect = new Rect(nameX, y, Mathf.Max(1f, width - nameX - 4f), RaidForceSectionHeaderH);
            Widgets.Label(labelRect, "TSA_WD_RaidForce_SupportingAllies".Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            TooltipHandler.TipRegion(headerRect, "TSA_WD_RaidForce_SelectAllAlliesTip".Translate());
            if (allyCount > 0 && onToggleSelectAllAllies != null && Widgets.ButtonInvisible(headerRect))
            {
                onToggleSelectAllAllies();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            return y + RaidForceSectionHeaderH;
        }

        private static float DrawForceRow(float y, float width, RaidForceRow row, System.Action<RaidForceRow> onToggleAlly, bool applySelectedTint)
        {
            if (row == null) return y + RaidForceRowHeight;
            Rect rowRect = new Rect(0f, y, width, RaidForceRowHeight);
            bool included = row.Included;
            bool dimmed = !included;

            if (applySelectedTint && included && (row.CanToggle || row.IsPrimary))
                Outpost_Dialog_UI.DrawSelectedRowTint(rowRect, true);
            if (Mouse.IsOver(rowRect))
                Widgets.DrawHighlight(rowRect);

            Rect iconOuter = new Rect(4f, y + (RaidForceRowHeight - RaidForceIconSize) * 0.5f, RaidForceIconSize, RaidForceIconSize);
            Rect iconInner = new Rect(iconOuter.x + 2f, iconOuter.y + 2f, iconOuter.width - 4f, iconOuter.height - 4f);
            if (dimmed) GUI.color = new Color(1f, 1f, 1f, 0.45f);
            WorldDomination_UIUtils.DrawFactionIconWithColor(iconInner, row.Faction);

            const float strengthW = 54f;
            float nameX = RaidForceIconSize + 10f;
            Rect nameRect = new Rect(nameX, y, width - nameX - strengthW - RaidForceStrengthRightPad - 4f, RaidForceRowHeight);
            Rect strengthRect = new Rect(width - strengthW - RaidForceStrengthRightPad, y, strengthW, RaidForceRowHeight);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = dimmed ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
            Widgets.Label(nameRect, row.Label ?? "?");

            Text.Anchor = TextAnchor.MiddleRight;
            string strengthText = "+" + row.DisplayStrength.ToString("F0");
            GUI.color = dimmed ? new Color(0.55f, 0.75f, 0.75f, 0.55f) : Color.cyan;
            Widgets.Label(strengthRect, strengthText);

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            if (!row.Tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(rowRect, row.Tooltip);

            if (row.CanToggle && onToggleAlly != null && Widgets.ButtonInvisible(rowRect))
                onToggleAlly(row);

            return y + RaidForceRowHeight;
        }

        public static string FormatMarginOutcome(bool attackerWon, BattleMarginTier tier)
        {
            string key = attackerWon
                ? (tier == BattleMarginTier.Decisive ? "TSA_WD_Margin_AttWinDecisive"
                    : tier == BattleMarginTier.Normal ? "TSA_WD_Margin_AttWinNormal" : "TSA_WD_Margin_AttWinClose")
                : (tier == BattleMarginTier.Decisive ? "TSA_WD_Margin_DefWinDecisive"
                    : tier == BattleMarginTier.Normal ? "TSA_WD_Margin_DefWinNormal" : "TSA_WD_Margin_DefWinClose");
            return key.Translate();
        }

        public static bool IsPlayerOutpostDefense(SpreadLogEntry entry)
        {
            if (entry?.targetB == null || !entry.targetB.IsValid) return false;
            WorldObject wo = entry.targetB.WorldObject;
            return wo is WorldObject_WD_Outpost outpost && outpost.Faction == Faction.OfPlayer;
        }

        public static BattleMarginTier GetAttSeverityTier(SpreadLogEntry entry)
        {
            if (entry == null) return BattleMarginTier.Normal;
            return entry.attSeverityTier != BattleMarginTier.Normal || entry.defCoalitionSeverityTier != BattleMarginTier.Normal
                ? entry.attSeverityTier : entry.marginTier;
        }

        public static string FormatResolutionMarginLine(BattleMarginTier tier, bool attackerSide, bool sideWon, float lossPct, bool useVictoryLabel = false)
        {
            string label = FormatMarginTierLabel(tier, attackerSide, sideWon, useVictoryLabel);
            return "TSA_WD_RaidResolution_MarginLoss".Translate(label, (lossPct * 100f).ToString("F0"));
        }

        public static string FormatRaidOutcomeHeadline(SpreadLogEntry entry)
        {
            if (entry == null) return string.Empty;
            bool defenderPerspective = IsPlayerOutpostDefense(entry);
            bool attackerWon = entry.victory;
            if (defenderPerspective)
                return attackerWon ? "TSA_WD_RaidResolution_DefenderDefeat".Translate() : "TSA_WD_RaidResolution_DefenderVictory".Translate();
            return attackerWon ? "TSA_WD_RaidResolution_AttackerVictory".Translate() : "TSA_WD_RaidResolution_AttackerDefeat".Translate();
        }

        public static Color GetMarginTierColor(BattleMarginTier tier, bool isWinOutcome)
        {
            if (isWinOutcome)
            {
                if (tier == BattleMarginTier.Close) return new Color(0.98f, 0.82f, 0.18f);
                if (tier == BattleMarginTier.Normal) return new Color(0.72f, 0.86f, 0.22f);
                return ForecastWinGreen;
            }
            if (tier == BattleMarginTier.Close) return new Color(1f, 0.58f, 0.12f);
            if (tier == BattleMarginTier.Normal) return new Color(0.95f, 0.38f, 0.14f);
            return ForecastLossRed;
        }

        public static Color GetMarginColor(bool attackerWon, BattleMarginTier tier)
        {
            return GetMarginTierColor(tier, attackerWon);
        }

        public static void DrawMarginTierChip(Rect rect, BattleMarginTier tier, bool attackerWon)
        {
            Color bg = GetMarginColor(attackerWon, tier);
            Widgets.DrawBoxSolid(rect, new Color(bg.r, bg.g, bg.b, 0.35f));
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            string abbrev = tier == BattleMarginTier.Decisive ? "D" : tier == BattleMarginTier.Normal ? "N" : "C";
            Widgets.Label(rect, abbrev);
            TooltipHandler.TipRegion(rect, FormatMarginOutcome(attackerWon, tier));
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        public static string FormatMarginTierLabel(BattleMarginTier tier, bool attackerSide, bool isWin, bool useVictoryLabel = false)
        {
            string margin = tier == BattleMarginTier.Decisive ? "Decisive"
                : tier == BattleMarginTier.Normal ? "Normal" : "Close";
            string who = attackerSide ? "Attacker" : "Defender";
            string outcome = isWin ? (useVictoryLabel ? "Victory" : "Win") : "Loss";
            return ("TSA_WD_Raid_" + margin + who + outcome).Translate();
        }

        /// <summary>
        /// Full conditional-outcome breakdown for one side of the win/loss bar (hover tooltip).
        /// Shares are conditional on that branch (sum to 100% within win or within loss).
        /// </summary>
        private static string FormatForecastBranchTooltip(
            float branchLikelihood,
            string sectionHeader,
            RaidMarginShares shares,
            WorldDominationSettings seth,
            bool attackerSide,
            bool isWinOutcome)
        {
            if (seth == null) seth = WorldDominationMod.settings;
            shares.Normalize();

            var sb = new System.Text.StringBuilder(256);
            sb.AppendLine("TSA_WD_WinChance".Translate() + ": " + branchLikelihood.ToStringPercent());
            sb.AppendLine();
            sb.AppendLine("--- " + sectionHeader + " ---");
            sb.AppendLine();
            AppendForecastTierTooltipLines(sb, BattleMarginTier.Decisive, shares.decisive, seth, attackerSide, isWinOutcome);
            AppendForecastTierTooltipLines(sb, BattleMarginTier.Normal, shares.normal, seth, attackerSide, isWinOutcome);
            AppendForecastTierTooltipLines(sb, BattleMarginTier.Close, shares.close, seth, attackerSide, isWinOutcome);
            return sb.ToString().TrimEnd();
        }

        private static void AppendForecastTierTooltipLines(
            System.Text.StringBuilder sb,
            BattleMarginTier tier,
            float share,
            WorldDominationSettings seth,
            bool attackerSide,
            bool isWinOutcome)
        {
            if (share <= 0.0005f) return;
            float loss = attackerSide
                ? seth.GetAttCasualtyLoss(tier, isWinOutcome)
                : seth.GetDefCoalitionCasualtyLoss(tier, isWinOutcome);
            sb.AppendLine(FormatMarginTierLabel(tier, attackerSide, isWinOutcome) + ": " + (share * 100f).ToString("F0") + " %");
            sb.AppendLine("TSA_WD_RaidForecast_SegmentStrengthLoss".Translate((loss * 100f).ToString("F0")));
            sb.AppendLine();
        }

        public static void DrawRaidForecast(Listing_Standard listing, RaidOutcomeForecast forecast, float ratio, bool defenderPerspective, string relativeStrengthSuffix = null)
        {
            WorldDominationSettings seth = WorldDominationMod.settings;
            float displayWin = defenderPerspective ? (1f - forecast.winChance) : forecast.winChance;
            float displayLoss = 1f - displayWin;

            string winLabel = "TSA_WD_WinChance".Translate() + ": " + displayWin.ToStringPercent();
            if (!relativeStrengthSuffix.NullOrEmpty())
                winLabel += " (" + relativeStrengthSuffix + ")";
            listing.Label(winLabel);
            Rect winBar = listing.GetRect(22f);

            string winTip;
            string lossTip;
            if (defenderPerspective)
            {
                winTip = FormatForecastBranchTooltip(displayWin, "TSA_WD_RaidForecast_DefWinType".Translate(),
                    forecast.attLossDefCoalition, seth, attackerSide: false, isWinOutcome: true);
                lossTip = FormatForecastBranchTooltip(displayLoss, "TSA_WD_RaidForecast_DefLossType".Translate(),
                    forecast.attWinDefCoalition, seth, attackerSide: false, isWinOutcome: false);
            }
            else
            {
                winTip = FormatForecastBranchTooltip(displayWin, "TSA_WD_RaidForecast_WinType".Translate(),
                    forecast.attWinAttSeverity, seth, attackerSide: true, isWinOutcome: true);
                lossTip = FormatForecastBranchTooltip(displayLoss, "TSA_WD_RaidForecast_LossType".Translate(),
                    forecast.attLossAttSeverity, seth, attackerSide: true, isWinOutcome: false);
            }

            DrawWinChanceBar(winBar, displayWin, winTip, lossTip);
        }
        public static string FormatRaidLogSuffix(SpreadLogEntry entry)
        {
            if (entry == null || !entry.isRaid) return null;
            if (entry.isAttempt)
                return " (" + entry.winChance.ToStringPercent() + " " + "TSA_WD_WinChance".Translate() + ")";

            if (entry.isAborted) return null;

            bool defenderPerspective = IsPlayerOutpostDefense(entry);
            bool attackerWon = entry.victory;
            BattleMarginTier attTier = GetAttSeverityTier(entry);
            BattleMarginTier defTier = entry.defCoalitionSeverityTier;

            if (defenderPerspective)
                return " " + FormatMarginTierLabel(defTier, false, !attackerWon);
            return " " + FormatMarginTierLabel(attTier, true, attackerWon);
        }

        /// <summary>Draws a scrollable list of detail lines. Each line may be "display" or "display|tooltip"; tooltip shown on mouseover.</summary>
        public static void DrawDetailScroll(Rect rect, ref Vector2 scroll, List<string> lines)
        {
            Widgets.DrawMenuSection(rect);
            Text.Font = GameFont.Tiny;
            float width = rect.width - 20f;
            if (lines == null || lines.Count == 0) lines = new List<string> { "(" + "None".Translate() + ")" };
            float totalHeight = 0f;
            foreach (var line in lines)
            {
                int idx = line.IndexOf(Raid_ReinforcementLogic.DetailTooltipDelimiter);
                string display = idx >= 0 ? line.Substring(0, idx) : line;
                totalHeight += Text.CalcHeight(display, width) + 4f;
            }
            Rect viewRect = new Rect(0, 0, width, totalHeight + 10f);
            Widgets.BeginScrollView(rect, ref scroll, viewRect);
            float curY = 5f;
            Text.Anchor = TextAnchor.UpperLeft;
            foreach (var line in lines)
            {
                int idx = line.IndexOf(Raid_ReinforcementLogic.DetailTooltipDelimiter);
                string display = idx >= 0 ? line.Substring(0, idx) : line;
                string tooltip = idx >= 0 ? line.Substring(idx + 1) : null;
                float height = Text.CalcHeight(display, width);
                Rect lineRect = new Rect(5f, curY, width, height);
                if (tooltip != null)
                {
                    Widgets.DrawHighlightIfMouseover(lineRect);
                    TooltipHandler.TipRegion(lineRect, tooltip);
                }
                Widgets.Label(lineRect, display);
                curY += height + 4f;
            }
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.EndScrollView();
        }
    }

    [StaticConstructorOnStartup]
    public static class WorldDomination_UIUtils
    {
        private static PlanetLayer cachedDefaultLayer;
        private static bool defaultLayerResolved;

        /// <summary>Cached first PlanetLayer from WorldGrid — avoids repeated .First().Value LINQ calls.</summary>
        public static PlanetLayer GetDefaultPlanetLayer()
        {
            if (!defaultLayerResolved || cachedDefaultLayer == null)
            {
                var layers = Find.WorldGrid?.PlanetLayers;
                if (layers != null)
                {
                    foreach (var kv in layers)
                    {
                        cachedDefaultLayer = kv.Value;
                        break;
                    }
                }
                defaultLayerResolved = true;
            }
            return cachedDefaultLayer;
        }

        /// <summary>Call on game load or world change to reset cached layer.</summary>
        public static void ResetPlanetLayerCache()
        {
            cachedDefaultLayer = null;
            defaultLayerResolved = false;
        }

        /// <summary>Shared label style matching SpreadLogEntry / Action Log rows.</summary>
        public static string FormatWorldObjectLabelLikeActionLog(WorldObject obj)
        {
            if (obj == null || obj.Destroyed) return "---";
            var comp = obj.GetComponent<CompViralSpread>();
            string typeLabel = "";
            if (obj is WorldObject_Traveler)
                typeLabel = " (Expedition Force)";
            else if (obj is WorldObject_WD_Outpost)
                typeLabel = " (Outpost)";
            else if (obj is Settlement)
                typeLabel = comp != null ? $" ({comp.tier})" : " (Town)";
            return $"{obj.LabelCap}{typeLabel} ({obj.Faction?.Name ?? "No Faction"})";
        }

        /// <summary>Format ticks to "Day N, Xh" label (shared across Action Log and Dashboard).</summary>
        public static string FormatTicksAsDay(int ticks)
        {
            if (ticks <= 0) return "---";
            int day = (ticks / 60000) + 1;
            int hour = (ticks % 60000) / 2500;
            return $"Day {day}, {hour}h";
        }

        private static readonly List<Pawn> humanlikeScratch = new List<Pawn>(16);

        /// <summary>Fill buffer with humanlike, alive pawns from caravan. Clears buffer first. Returns the buffer for chaining.</summary>
        public static List<Pawn> GetHumanlikeColonists(Caravan caravan, List<Pawn> buffer = null)
        {
            buffer ??= humanlikeScratch;
            buffer.Clear();
            var reading = caravan?.PawnsListForReading;
            if (reading == null) return buffer;
            for (int i = 0; i < reading.Count; i++)
            {
                var p = reading[i];
                if (p != null && !p.Dead && p.RaceProps?.Humanlike == true)
                    buffer.Add(p);
            }
            return buffer;
        }

        private static Texture2D unknownWorldTargetPlaceholderIcon;

        /// <summary>Vanilla-style placeholder when a target is a raw tile (no faction/world object icon).</summary>
        public static Texture2D UnknownWorldTargetPlaceholderIcon
        {
            get
            {
                if (unknownWorldTargetPlaceholderIcon == null)
                {
                    unknownWorldTargetPlaceholderIcon = ContentFinder<Texture2D>.Get("UI/Icons/QuestionMark", false)
                        ?? TexButton.Info;
                }
                return unknownWorldTargetPlaceholderIcon;
            }
        }

        /// <summary>True if a faction icon was drawn for this target (tile-only targets return false).</summary>
        public static bool TryDrawFactionIconForTarget(Rect rect, GlobalTargetInfo target, out Faction faction)
        {
            faction = null;
            if (target.HasWorldObject && target.WorldObject != null)
                faction = target.WorldObject.Faction;
            else if (target.HasThing && target.Thing != null)
                faction = target.Thing.Faction;
            if (faction != null && faction.def?.FactionIcon != null)
            {
                DrawFactionIconWithColor(rect, faction);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Draws a faction icon tinted with the faction's custom color.
        /// Extracts faction automatically from GlobalTargetInfo.
        /// </summary>
        public static void DrawFactionIconWithColor(Rect rect, GlobalTargetInfo target) =>
            TryDrawFactionIconForTarget(rect, target, out _);

        /// <summary>
        /// Draws a faction icon tinted with the faction's custom color.
        /// Directly accepts a Faction object.
        /// </summary>
        public static void DrawFactionIconWithColor(Rect rect, Faction faction)
        {
            if (faction?.def?.FactionIcon != null)
            {
                // GUI.DrawTexture with the color parameter is the most reliable way to tint
                // It bypasses the button logic that often resets GUI.color
                GUI.DrawTexture(rect, faction.def.FactionIcon, ScaleMode.ScaleToFit, true, 0f, faction.Color, 0f, 0f);
            }
        }

        /// <summary>
        /// Diplomacy goodwill / relation colors vs the player (Hostile red, Neutral cyan-blue, Ally green).
        /// Player faction stays cyan for list identity.
        /// </summary>
        public static Color ColorForRelationWithPlayer(Faction faction)
        {
            if (faction == null) return Color.white;
            if (faction.IsPlayer) return Color.cyan;
            FactionRelationKind kind = WorldActions_Utils.SafeRelationKindWith(faction, Faction.OfPlayerSilentFail);
            return kind.GetColor();
        }

        private static readonly Dictionary<string, Texture2D> TravelerMissionIconByPath = new Dictionary<string, Texture2D>();

        /// <summary>Mission icon from traveler def XML; cached by path so list UIs do not hit ContentFinder every row each frame.</summary>
        public static Texture2D CachedTravelerMissionIcon(WorldObject_Traveler t)
        {
            string path = t?.ResolveIconTexturePath();
            if (string.IsNullOrEmpty(path)) return null;
            if (TravelerMissionIconByPath.TryGetValue(path, out Texture2D tex) && tex != null) return tex;
            tex = ContentFinder<Texture2D>.Get(path, false);
            if (tex != null) TravelerMissionIconByPath[path] = tex;
            return tex;
        }

        /// <summary>Close WD overlay windows and escape the World Domination main tab so the world map is visible.</summary>
        public static void DismissWorldDominationUiForWorldMap()
        {
            WdNavWindows.CloseAllNavWindows(escapeMainTab: true);
        }

        /// <summary>Jump to a world object, select it, and dismiss WD UI covering the map.</summary>
        public static void JumpToWorldObjectOnMap(WorldObject wo)
        {
            if (wo == null) return;
            CameraJumper.TryJumpAndSelect(wo);
            DismissWorldDominationUiForWorldMap();
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        public const float RosterIconBtnSize = 30f;
        public const float SlateNavIconSize = 26f;
        public const float SlateNavIconPad = 8f;
        public const float SlateNavIconTextGap = 6f;

        private static readonly Color SlateNavFill = new Color(0.16f, 0.18f, 0.22f, 0.92f);
        private static readonly Color SlateNavHover = new Color(0.22f, 0.26f, 0.32f, 0.96f);
        private static readonly Color SlateNavPress = new Color(0.12f, 0.14f, 0.17f, 0.96f);
        private static readonly Color SlateNavOutline = new Color(0.55f, 0.62f, 0.72f, 0.42f);
        private static readonly Color SlateNavOutlineHover = new Color(0.78f, 0.84f, 0.92f, 0.72f);

        public static Texture2D RosterResetViewIcon =>
            rosterResetViewIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/ResetView", false);
        public static Texture2D RosterColumnPickerIcon =>
            rosterColumnPickerIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/ColumnPicker", false);
        public static Texture2D RosterHighlightIcon =>
            rosterHighlightIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/Highlight", false);
        public static Texture2D RosterTransferIcon =>
            rosterTransferIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/Transfer", false);
        public static Texture2D RosterSmartIcon =>
            rosterSmartIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/Smart", false);
        public static Texture2D RosterEstablishOutpostIcon =>
            rosterEstablishOutpostIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/EstablishOutpost", false)
                ?? ContentFinder<Texture2D>.Get("UI/Commands/Settle", false);
        public static Texture2D RosterKickOutIcon =>
            rosterKickOutIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/KickOut", false);

        private static Texture2D rosterResetViewIcon;
        private static Texture2D rosterColumnPickerIcon;
        private static Texture2D rosterHighlightIcon;
        private static Texture2D rosterTransferIcon;
        private static Texture2D rosterSmartIcon;
        private static Texture2D rosterEstablishOutpostIcon;
        private static Texture2D rosterKickOutIcon;

        /// <summary>Icon-only button; tooltip is the former button label. Optional <paramref name="iconTint"/> tints the icon.</summary>
        public static bool ButtonIconOnly(Rect rect, Texture2D icon, string tooltip, Color? iconTint = null)
        {
            Texture2D tex = icon ?? BaseContent.BadTex;
            bool clicked = iconTint.HasValue
                ? Widgets.ButtonImage(rect, tex, iconTint.Value)
                : Widgets.ButtonImage(rect, tex);
            if (!tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(rect, tooltip);
            return clicked;
        }

        public static float TitleRestoreButtonReserve => RosterIconBtnSize + 8f;

        /// <summary>Places the roster Restore default view icon at the right of a title band.</summary>
        public static void DrawTitleRestoreDefaultView(float titleBandWidth, float titleBandH, Action onRestore)
        {
            DrawTitleRestoreDefaultViewAt(titleBandWidth - RosterIconBtnSize, titleBandH, onRestore);
        }

        /// <summary>Places the roster Restore default view icon at <paramref name="x"/> in the title band.</summary>
        public static void DrawTitleRestoreDefaultViewAt(float x, float titleBandH, Action onRestore)
        {
            float btn = RosterIconBtnSize;
            float y = Mathf.Max(0f, (titleBandH - btn) * 0.5f);
            Rect restoreBtn = new Rect(x, y, btn, btn);
            if (ButtonIconOnly(restoreBtn, RosterResetViewIcon, "TSA_WD_AllPlayerPawns_RestoreDefault".Translate()))
            {
                onRestore?.Invoke();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }

        /// <summary>Dashboard-style slate icon+label button. Optional <paramref name="iconSize"/> overrides the default 26px icon. Optional <paramref name="iconTint"/> tints the icon (Launch_Raid-style assets that are already colorized should pass white / omit). Optional <paramref name="fill"/> replaces the default slate background.</summary>
        public static bool ButtonTextWithIcon(
            Rect rect,
            Texture2D icon,
            string label,
            float iconSize = -1f,
            Color? iconTint = null,
            bool centerContents = false,
            Color? fill = null)
        {
            bool mouseOver = Mouse.IsOver(rect);
            bool pressed = mouseOver && Input.GetMouseButton(0) && GUI.enabled;
            Color baseFill = fill ?? SlateNavFill;
            Color hover = fill.HasValue ? Color.Lerp(baseFill, Color.white, 0.14f) : SlateNavHover;
            Color press = fill.HasValue ? Color.Lerp(baseFill, Color.black, 0.18f) : SlateNavPress;
            Color bg = !GUI.enabled
                ? new Color(baseFill.r, baseFill.g, baseFill.b, 0.45f)
                : pressed ? press : mouseOver ? hover : baseFill;
            Widgets.DrawBoxSolid(rect, bg);
            GUI.color = mouseOver && GUI.enabled ? SlateNavOutlineHover : SlateNavOutline;
            Widgets.DrawBox(rect, 1);
            GUI.color = GUI.enabled ? Color.white : new Color(1f, 1f, 1f, 0.4f);

            float drawIcon = iconSize > 0f ? iconSize : Mathf.Min(SlateNavIconSize, rect.height - 8f);
            float textLeft = rect.x + SlateNavIconPad;
            string text = label ?? "";
            float textW = Text.CalcSize(text).x;
            float contentW = textW + (icon != null && drawIcon > 4f ? drawIcon + SlateNavIconTextGap : 0f);
            if (centerContents)
                textLeft = Mathf.Max(rect.x + SlateNavIconPad, rect.x + (rect.width - contentW) * 0.5f);
            if (icon != null && drawIcon > 4f)
            {
                Rect iconRect = new Rect(
                    textLeft,
                    rect.y + (rect.height - drawIcon) * 0.5f,
                    drawIcon,
                    drawIcon);
                Color tint = iconTint ?? Color.white;
                if (!GUI.enabled)
                    tint = new Color(tint.r, tint.g, tint.b, tint.a * 0.4f);
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true, 0f, tint, 0f, 0f);
                textLeft = iconRect.xMax + SlateNavIconTextGap;
            }

            Text.Font = GameFont.Small;
            TextAnchor prev = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            float labelW = Mathf.Max(0f, rect.xMax - textLeft - 4f);
            Widgets.Label(new Rect(textLeft, rect.y, labelW, rect.height), text.Truncate(labelW));
            Text.Anchor = prev;
            GUI.color = Color.white;

            return GUI.enabled && Widgets.ButtonInvisible(rect);
        }
    }
}