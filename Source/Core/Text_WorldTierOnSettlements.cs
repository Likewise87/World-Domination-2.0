using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Event = UnityEngine.Event;
using EventType = UnityEngine.EventType;

namespace TSA_WorldDomination
{
    public class Text_WorldTierOnSettlements : WorldComponent
    {
        private List<(WorldObject Obj, CompViralSpread Comp)> tierLabelCache;
        private int tierLabelCacheTick = -999;
        private const int TierLabelCacheIntervalTicks = 300;

        public Text_WorldTierOnSettlements(World world) : base(world) { }

        public void NotifyTierLabelCacheDirty()
        {
            tierLabelCacheTick = -999;
        }

        private void EnsureTierLabelCacheFresh()
        {
            int tick = Current.ProgramState == ProgramState.Playing ? (Find.TickManager?.TicksGame ?? 0) : Time.frameCount;
            if (tierLabelCache != null && tick - tierLabelCacheTick < TierLabelCacheIntervalTicks) return;
            tierLabelCacheTick = tick;
            tierLabelCache ??= new List<(WorldObject, CompViralSpread)>(128);
            tierLabelCache.Clear();
            if (Find.WorldObjects == null) return;
            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                var wo = all[i];
                var comp = wo.GetComponent<CompViralSpread>();
                if (comp == null || comp.subType == "Excluded") continue;
                if (!WorldActions_Utils.IsWdSurfaceWorldObject(wo)) continue;
                if (wo.Faction != null && wo.Faction.IsPlayer) continue;
                if (comp.IsOutpost) continue;
                tierLabelCache.Add((wo, comp));
            }
        }

        public override void WorldComponentOnGUI()
        {
            base.WorldComponentOnGUI();

            bool isValidState = Current.ProgramState == ProgramState.Playing || Current.ProgramState == ProgramState.Entry;

            if (!isValidState || !WorldRendererUtility.WorldRendered)
                return;

            if (!WorldComponent_WDVisualizerToggle.ShowSettlementTierTexts)
                return;

            if (Event.current.type != EventType.Repaint)
                return;

            // 0.25 on a normal-sized planet; raised on small worlds (My Little Planet, etc.).
            if (WD_WorldMapZoomUtil.IsZoomedTooFarOut(0.25f))
                return;

            EnsureTierLabelCacheFresh();
            if (tierLabelCache == null || tierLabelCache.Count == 0) return;

            float bottomStripHeight = 80f;

            for (int i = 0; i < tierLabelCache.Count; i++)
            {
                var (worldObject, comp) = tierLabelCache[i];
                if (worldObject.Destroyed) continue;

                if (!WorldObjectSelectionUtility.VisibleToCameraNow(worldObject)) continue;

                Vector2 screenPos = WorldObjectSelectionUtility.ScreenPos(worldObject);

                int tierIdx = (int)comp.tier;
                string labelText = TierLabels[tierIdx];
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                if (!tierLabelWidthsCached)
                {
                    for (int t = 0; t < 4; t++) TierLabelWidths[t] = Text.CalcSize(TierLabels[t]).x;
                    tierLabelWidthsCached = true;
                }
                float textSize = TierLabelWidths[tierIdx];
                float yOffset = -32f;
                Rect rect = new Rect(screenPos.x - textSize / 2f, screenPos.y + yOffset, textSize, 20f);

                if (rect.yMax > (float)UI.screenHeight - bottomStripHeight)
                {
                    Text.Anchor = TextAnchor.UpperLeft;
                    Text.Font = GameFont.Small;
                    continue;
                }

                Color labelColor = GetColorForTier(comp.tier);
                Color originalColor = GUI.color;

                GUI.color = ShadowColor;
                Rect shadowRect = new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height);
                Widgets.Label(shadowRect, labelText);

                GUI.color = labelColor;
                Widgets.Label(rect, labelText);

                GUI.color = originalColor;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
            }
        }

        private static readonly string[] TierLabels = { "T1", "T2", "T3", "T4" };
        private static readonly float[] TierLabelWidths = new float[4];
        private static bool tierLabelWidthsCached;

        private static readonly Color TierColorT4 = new Color(0.8f, 0.2f, 1f);
        private static readonly Color TierColorT3 = new Color(1f, 0.3f, 0.3f);
        private static readonly Color TierColorT2 = new Color(1f, 0.8f, 0.2f);
        private static readonly Color ShadowColor = new Color(0, 0, 0, 0.8f);

        private static Color GetColorForTier(SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.T4: return TierColorT4;
                case SettlementTier.T3: return TierColorT3;
                case SettlementTier.T2: return TierColorT2;
                case SettlementTier.T1: return Color.white;
                default: return Color.gray;
            }
        }
    }
}