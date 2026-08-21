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
        /// <summary>Net line cached until <see cref="WorldComponent_LogisticsManager.LogisticsNetDisplayGeneration"/> changes; food line until displayed F0 food/max changes.</summary>
        private readonly Dictionary<int, (string netLabel, Color netColor, int netGen)> netLabelCache
            = new Dictionary<int, (string, Color, int)>();
        private readonly Dictionary<int, (string foodLabel, Color foodColor, int foodRounded, int maxFoodRounded)> foodLabelCache
            = new Dictionary<int, (string, Color, int, int)>();
        private readonly Dictionary<int, StrengthLabelCache> strengthLabelCache = new Dictionary<int, StrengthLabelCache>();

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

            WD_OutpostWorldMapLabelMode mode = WorldComponent_WDVisualizerToggle.OutpostWorldMapLabelMode;
            if (mode == WD_OutpostWorldMapLabelMode.Off) return;
            if (mode == WD_OutpostWorldMapLabelMode.Food
                && (WorldDominationMod.settings == null || !WorldDominationMod.settings.foodLogisticsActive))
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
                    case WD_OutpostWorldMapLabelMode.Food:
                        DrawFoodLabels(wo, nodes[i].Logi, screenPos, yOffset, bottomStripHeight);
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

        private void DrawFoodLabels(
            WorldObject_WD_Outpost wo,
            CompOutpostLogistics logi,
            Vector2 screenPos,
            float yOffset,
            float bottomStripHeight)
        {
            if (logi == null || cachedManager == null) return;

            float maxFood = CompOutpostLogistics.GetEffectiveMaxFoodFor(wo);
            int woId = wo.ID;
            string netLabel;
            Color netColor;
            int netDisplayGen = cachedManager.LogisticsNetDisplayGeneration;
            if (netLabelCache.TryGetValue(woId, out var netCached) && netCached.netGen == netDisplayGen)
            {
                netLabel = netCached.netLabel;
                netColor = netCached.netColor;
            }
            else
            {
                float totalNet = cachedManager.GetLogisticsNetDailyForOutpost(wo);
                string netSign = totalNet >= 0 ? "+" : "";
                netLabel = string.Concat(netSign, totalNet.ToString("F1"));
                netColor = totalNet < 0f ? Color.red : (totalNet > 0f ? Color.green : Color.white);
                netLabelCache[woId] = (netLabel, netColor, netDisplayGen);
            }

            string foodLabel;
            Color foodColor;
            int foodRounded = Mathf.RoundToInt(logi.currentFood);
            int maxFoodRounded = Mathf.RoundToInt(maxFood);
            if (foodLabelCache.TryGetValue(woId, out var foodCached)
                && foodCached.foodRounded == foodRounded
                && foodCached.maxFoodRounded == maxFoodRounded)
            {
                foodLabel = foodCached.foodLabel;
                foodColor = foodCached.foodColor;
            }
            else
            {
                foodLabel = string.Concat(foodRounded.ToString(), "/", maxFoodRounded.ToString());
                float foodPct = maxFood > 0.001f ? logi.currentFood / maxFood : 0f;
                foodColor = foodPct <= 0.12f ? Color.red
                    : foodPct >= 0.70f ? Color.green
                    : (foodPct >= 0.13f && foodPct <= 0.19f) ? Color.yellow
                    : Color.white;
                foodLabelCache[woId] = (foodLabel, foodColor, foodRounded, maxFoodRounded);
            }

            DrawTwoLineLabel(screenPos, yOffset, bottomStripHeight, netLabel, netColor, foodLabel, foodColor);
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
