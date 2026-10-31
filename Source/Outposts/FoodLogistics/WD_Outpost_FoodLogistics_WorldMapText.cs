using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Event = UnityEngine.Event;
using EventType = UnityEngine.EventType;

namespace TSA_WorldDomination
{
    public class WorldLogisticsVisualizer : WorldComponent
    {
        private WorldComponent_LogisticsManager cachedManager;
        private static readonly Color ShadowColor = new Color(0, 0, 0, 0.7f);
        /// <summary>Constant "raidable" label; translated once instead of per outpost per frame.</summary>
        private static string cachedRaidableLabel;
        /// <summary>Supply lines (F food, T tooling, P power) cached per outpost for a short bucket of ticks.</summary>
        private readonly Dictionary<int, SupplyLabelCache> supplyLabelCache = new Dictionary<int, SupplyLabelCache>();
        private readonly Dictionary<int, StrengthLabelCache> strengthLabelCache = new Dictionary<int, StrengthLabelCache>();

        private sealed class SupplyLabelCache
        {
            public int RefreshBucket = -1;
            public readonly List<string> Lines = new List<string>(3);
            public readonly List<Color> Colors = new List<Color>(3);
        }

        private struct StrengthLabelCache
        {
            public int RefreshBucket;
            public string StrengthLabel;
            public Color StrengthColor;
            public string RecoveryLabel;
            public Color RecoveryColor;
        }

        public WorldLogisticsVisualizer(World world) : base(world) { }

        public override void WorldComponentOnGUI()
        {
            base.WorldComponentOnGUI();

            if (Current.ProgramState != ProgramState.Playing || !WorldRendererUtility.WorldRendered) return;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceLayerSelected()) return;

            WD_OutpostWorldMapLabelMode mode = WorldComponent_WDVisualizerToggle.OutpostWorldMapLabelMode;
            if (mode == WD_OutpostWorldMapLabelMode.Off) return;
            if (mode == WD_OutpostWorldMapLabelMode.Supply && !AnySupplyActive())
                return;

            if (Event.current.type != EventType.Repaint) return;
            // Stricter than tier labels (0.25); still scales up on small worlds.
            if (WD_WorldMapZoomUtil.IsZoomedTooFarOut(0.08f)) return;

            var nodes = GetLogisticsNodes();
            if (nodes == null) return;

            float bottomStripHeight = 80f;

            for (int i = 0; i < nodes.Count; i++)
            {
                if (!(nodes[i].Obj is WorldObject_WD_Outpost wo)) continue;
                if (wo.Faction == null || !wo.Faction.IsPlayer) continue;
                if (!WorldObjectSelectionUtility.VisibleToCameraNow(wo)) continue;

                Vector2 screenPos = WorldObjectSelectionUtility.ScreenPos(wo);
                float yOffset = -45f;

                switch (mode)
                {
                    case WD_OutpostWorldMapLabelMode.Name:
                        DrawOutpostNameLabel(wo, screenPos, yOffset, bottomStripHeight);
                        break;
                    case WD_OutpostWorldMapLabelMode.Supply:
                        DrawSupplyLabels(wo, nodes[i].Logi, screenPos, yOffset, bottomStripHeight);
                        break;
                    case WD_OutpostWorldMapLabelMode.Strength:
                    case WD_OutpostWorldMapLabelMode.RaidCooldown:
                    {
                        CompViralSpread spread = wo.GetComponent<CompViralSpread>();
                        if (spread == null) continue;
                        if (mode == WD_OutpostWorldMapLabelMode.Strength)
                            DrawStrengthLabels(wo.ID, spread, screenPos, yOffset, bottomStripHeight);
                        else
                            DrawRaidCooldownLabel(spread, screenPos, yOffset, bottomStripHeight);
                        break;
                    }
                }
            }
        }

        private IReadOnlyList<(WorldObject Obj, CompOutpostLogistics Logi)> GetLogisticsNodes()
        {
            if (cachedManager == null)
                cachedManager = Find.World.GetComponent<WorldComponent_LogisticsManager>();
            return cachedManager?.GetCachedPlayerLogisticsNodes();
        }

        private static bool AnySupplyActive() =>
            WorldDominationMod.settings == null
            || WorldDominationMod.settings.foodLogisticsActive
            || OutpostTooling.Enabled
            || OutpostPowerGrid.Enabled;

        private static Color StockColor(float pct) =>
            pct <= 0.12f ? Color.red
            : pct >= 0.70f ? Color.green
            : (pct >= 0.13f && pct <= 0.19f) ? Color.yellow
            : Color.white;

        private static string SignedF1(float v) => (v >= 0f ? "+" : "") + v.ToString("F1");

        private void DrawSupplyLabels(
            WorldObject_WD_Outpost wo,
            CompOutpostLogistics logi,
            Vector2 screenPos,
            float yOffset,
            float bottomStripHeight)
        {
            int bucket = Find.TickManager.TicksGame / 60;
            if (!supplyLabelCache.TryGetValue(wo.ID, out SupplyLabelCache cache))
            {
                cache = new SupplyLabelCache();
                supplyLabelCache[wo.ID] = cache;
            }
            if (cache.RefreshBucket != bucket)
            {
                cache.RefreshBucket = bucket;
                cache.Lines.Clear();
                cache.Colors.Clear();
                BuildFoodLine(wo, logi, cache);
                BuildToolingLine(wo, cache);
                BuildPowerLine(wo, cache);
            }
            if (cache.Lines.Count == 0) return;
            DrawMultiLineLabel(screenPos, yOffset, bottomStripHeight, cache.Lines, cache.Colors);
        }

        private void BuildFoodLine(WorldObject_WD_Outpost wo, CompOutpostLogistics logi, SupplyLabelCache cache)
        {
            if (logi == null || cachedManager == null) return;
            if (WorldDominationMod.settings != null && !WorldDominationMod.settings.foodLogisticsActive) return;
            float maxFood = CompOutpostLogistics.GetEffectiveMaxFoodFor(wo);
            float net = cachedManager.GetLogisticsNetDailyForOutpost(wo);
            float pct = maxFood > 0.001f ? logi.currentFood / maxFood : 0f;
            cache.Lines.Add("F " + Mathf.RoundToInt(logi.currentFood) + "/" + Mathf.RoundToInt(maxFood) + " " + SignedF1(net));
            cache.Colors.Add(net < -0.05f && pct < 0.7f ? Color.red : StockColor(pct));
        }

        private static void BuildToolingLine(WorldObject_WD_Outpost wo, SupplyLabelCache cache)
        {
            if (!OutpostTooling.Enabled) return;
            if (OutpostTooling.IsGenerator(wo))
            {
                float output = OutpostTooling.GetGeneratorOutput(wo);
                cache.Lines.Add("T " + OutpostTooling.GetGeneratorUsed(wo).ToString("F0") + "/" + output.ToString("F0"));
                cache.Colors.Add(output > 0.01f ? Color.cyan : Color.red);
                return;
            }
            if (!OutpostTooling.RequiresTooling(wo)) return;
            float max = OutpostTooling.MaxPerOutpost;
            float net = OutpostTooling.GetNetPerDay(wo);
            cache.Lines.Add("T " + Mathf.RoundToInt(wo.currentTooling) + "/" + Mathf.RoundToInt(max) + " " + SignedF1(net));
            cache.Colors.Add(OutpostTooling.IsTooled(wo) ? StockColor(wo.currentTooling / max) : Color.red);
        }

        private static void BuildPowerLine(WorldObject_WD_Outpost wo, SupplyLabelCache cache)
        {
            if (!OutpostPowerGrid.Enabled) return;
            if (wo.IsPowerPlantOutpost)
            {
                float capacity = Outpost_PowerPlant.GetRemotePowerWatts(wo);
                if (wo.PowerRole == PowerOutputRole.SupplyOutposts)
                    cache.Lines.Add("P " + OutpostPowerGrid.GetPlantUsedWatts(wo).ToString("F0") + "/" + capacity.ToString("F0"));
                else
                    cache.Lines.Add("P " + capacity.ToString("F0"));
                cache.Colors.Add(Color.cyan);
                return;
            }
            if (!OutpostPowerGrid.RequiresPower(wo)) return;
            OutpostPowerGrid.TryGetStatus(wo, out OutpostPowerStatus status);
            cache.Lines.Add("P " + status.supplied.ToString("F0") + "/" + status.demand.ToString("F0"));
            cache.Colors.Add(OutpostPowerGrid.IsPowered(wo) ? Color.green : Color.red);
        }

        private static void DrawMultiLineLabel(
            Vector2 screenPos,
            float yOffset,
            float bottomStripHeight,
            List<string> lines,
            List<Color> colors)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;

            // Keep the last line where the two-line labels end; extra lines grow upward.
            float top = yOffset - 12f * Mathf.Max(0, lines.Count - 2);
            float bottom = screenPos.y + top + 12f * (lines.Count - 1) + 20f;
            if (bottom > (float)UI.screenHeight - bottomStripHeight)
            {
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                return;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                Rect rect = new Rect(screenPos.x - 70f, screenPos.y + top + 12f * i, 140f, 20f);
                GUI.color = ShadowColor;
                Widgets.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), lines[i]);
                GUI.color = colors[i];
                Widgets.Label(rect, lines[i]);
            }

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private void DrawStrengthLabels(
            int outpostId,
            CompViralSpread spread,
            Vector2 screenPos,
            float yOffset,
            float bottomStripHeight)
        {
            int refreshBucket = Find.TickManager.TicksGame / 60;
            if (strengthLabelCache.TryGetValue(outpostId, out StrengthLabelCache cached)
                && cached.RefreshBucket == refreshBucket)
            {
                DrawTwoLineLabel(screenPos, yOffset, bottomStripHeight,
                    cached.StrengthLabel, cached.StrengthColor, cached.RecoveryLabel, cached.RecoveryColor);
                return;
            }

            float offCur = spread.offensiveStrength;
            float defCur = spread.defensiveStrength;
            float offMax = spread.GetMaxOffensiveStrength();
            float defMax = spread.GetBaseDefensiveStrength();
            float totalCur = offCur + defCur;
            float totalMax = offMax + defMax;

            string strengthLabel = totalCur.ToString("F0") + "/" + totalMax.ToString("F0");
            float strengthPct = totalMax > 0.001f ? totalCur / totalMax : 0f;
            Color strengthColor = strengthPct <= 0.12f ? Color.red
                : strengthPct >= 0.70f ? Color.green
                : (strengthPct >= 0.13f && strengthPct <= 0.19f) ? Color.yellow
                : Color.white;

            float dailyRecovery = spread.GetInspectDailyOffensiveRecovery() + spread.GetInspectDailyDefensiveRecovery();
            string recoverySign = dailyRecovery >= 0f ? "+" : "";
            string recoveryLabel = recoverySign + dailyRecovery.ToString("F0");
            Color recoveryColor = dailyRecovery < 0f ? Color.red : (dailyRecovery > 0f ? Color.green : Color.white);

            strengthLabelCache[outpostId] = new StrengthLabelCache
            {
                RefreshBucket = refreshBucket,
                StrengthLabel = strengthLabel,
                StrengthColor = strengthColor,
                RecoveryLabel = recoveryLabel,
                RecoveryColor = recoveryColor
            };

            DrawTwoLineLabel(screenPos, yOffset, bottomStripHeight, strengthLabel, strengthColor, recoveryLabel, recoveryColor);
        }

        private const int OutpostNameMaxDisplayChars = 12;
        private const string OutpostNameEllipsis = "...";

        private static string FormatOutpostNameLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return label;
            if (label.Length <= OutpostNameMaxDisplayChars) return label;
            int prefixLen = OutpostNameMaxDisplayChars - OutpostNameEllipsis.Length;
            return label.Substring(0, prefixLen) + OutpostNameEllipsis;
        }

        private static void DrawOutpostNameLabel(
            WorldObject_WD_Outpost wo,
            Vector2 screenPos,
            float yOffset,
            float bottomStripHeight)
        {
            string label = wo?.LabelCap ?? "";
            if (string.IsNullOrEmpty(label)) return;
            DrawSingleLineLabel(screenPos, yOffset, bottomStripHeight, FormatOutpostNameLabel(label), Color.cyan);
        }

        private static void DrawRaidCooldownLabel(
            CompViralSpread spread,
            Vector2 screenPos,
            float yOffset,
            float bottomStripHeight)
        {
            string label;
            Color color;
            if (spread.IsDefenseOnCooldown)
            {
                float daysLeft = Mathf.Max(0f, (spread.defenseCooldownTick - Find.TickManager.TicksGame) / 60000f);
                label = "TSA_WD_WorldMap_RaidProtectionDays".Translate(daysLeft.ToString("F1")).ToString();
                color = Color.green;
            }
            else
            {
                label = cachedRaidableLabel ??= "TSA_WD_WorldMap_Raidable".Translate().ToString();
                color = CompViralSpread.RaidVulnerableColor;
            }

            DrawSingleLineLabel(screenPos, yOffset, bottomStripHeight, label, color);
        }

        private static void DrawTwoLineLabel(
            Vector2 screenPos,
            float yOffset,
            float bottomStripHeight,
            string topLabel,
            Color topColor,
            string bottomLabel,
            Color bottomColor)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;

            Rect topRect = new Rect(screenPos.x - 50f, screenPos.y + yOffset, 100f, 20f);
            Rect bottomRect = new Rect(screenPos.x - 50f, screenPos.y + yOffset + 12f, 100f, 20f);

            Rect combinedRect = Rect.MinMaxRect(topRect.xMin, topRect.yMin, bottomRect.xMax, bottomRect.yMax);
            if (combinedRect.yMax > (float)UI.screenHeight - bottomStripHeight)
            {
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                return;
            }

            GUI.color = ShadowColor;
            Widgets.Label(new Rect(topRect.x + 1f, topRect.y + 1f, topRect.width, topRect.height), topLabel);
            Widgets.Label(new Rect(bottomRect.x + 1f, bottomRect.y + 1f, bottomRect.width, bottomRect.height), bottomLabel);

            GUI.color = topColor;
            Widgets.Label(topRect, topLabel);
            GUI.color = bottomColor;
            Widgets.Label(bottomRect, bottomLabel);

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private static void DrawSingleLineLabel(
            Vector2 screenPos,
            float yOffset,
            float bottomStripHeight,
            string label,
            Color color)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;

            Rect rect = new Rect(screenPos.x - 50f, screenPos.y + yOffset + 12f, 100f, 20f);
            if (rect.yMax > (float)UI.screenHeight - bottomStripHeight)
            {
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                return;
            }

            GUI.color = ShadowColor;
            Widgets.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), label);
            GUI.color = color;
            Widgets.Label(rect, label);

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }
    }
}
