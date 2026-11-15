using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>AT Turret Intel tab: Known-to rows only. Registered via 1.7/Patches.</summary>
    public class WITab_AT_Intel : WITab
    {
        private Vector2 scrollPosition;
        private float scrollViewHeight = 280f;

        private const int CacheRefreshInterval = 2500;
        private int lastCacheTick = -1;
        private WorldObject cachedWorldObject;
        private int cachedFingerprint = int.MinValue;
        private OutpostStatsSnapshot cachedSnapshot;

        public WITab_AT_Intel()
        {
            size = new Vector2(460f, 420f);
            labelKey = "TSA_WD_ATIntel_TabLabel";
        }

        public WorldObject SelWorldObject => SelObject;

        public override bool IsVisible
        {
            get
            {
                if (!WorldComponent_SettlementIntel.IsGateConfigured())
                    return false;
                return SelObject is WorldObject_AT_Turret;
            }
        }

        protected override void FillTab()
        {
            WorldObject worldObject = SelWorldObject;
            if (worldObject == null) return;

            Rect body = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
            OutpostTabStatsUi.DrawHeadline(body, "TSA_WD_ATIntel_Headline".Translate(worldObject.LabelCap).ToString());

            EnsureSnapshotFresh(worldObject);
            OutpostStatsSnapshot snap = cachedSnapshot;
            if (snap == null) return;

            const float headerH = OutpostTabStatsUi.TabHeaderConsumedHeight;
            Rect scrollOuter = new Rect(body.x, body.y + headerH, body.width, body.height - headerH);
            float contentWidth = scrollOuter.width - OutpostTabStatsUi.ScrollbarRightPadding;

            scrollViewHeight = OutpostTabStatsUi.MeasureContentHeight(snap.Sections, contentWidth);
            if (scrollViewHeight < scrollOuter.height)
                scrollViewHeight = scrollOuter.height;
            Rect viewRect = new Rect(0f, 0f, contentWidth, scrollViewHeight);

            Widgets.BeginScrollView(scrollOuter, ref scrollPosition, viewRect);
            OutpostTabStatsUi.DrawStatsLayout(0f, 0f, contentWidth, snap.Sections);
            Widgets.EndScrollView();
        }

        private void EnsureSnapshotFresh(WorldObject worldObject)
        {
            int tick = Find.TickManager.TicksGame;
            int fp = ComputeFingerprint(worldObject);
            if (cachedSnapshot != null
                && cachedWorldObject == worldObject
                && cachedFingerprint == fp
                && tick - lastCacheTick < CacheRefreshInterval)
                return;

            lastCacheTick = tick;
            cachedWorldObject = worldObject;
            cachedFingerprint = fp;
            cachedSnapshot = BuildKnownTo(worldObject);
        }

        private static int ComputeFingerprint(WorldObject worldObject)
        {
            if (worldObject == null) return 0;
            unchecked
            {
                int h = worldObject.ID;
                var intel = WorldComponent_SettlementIntel.Get();
                h = h * 397 + (intel?.Version ?? 0);
                h = h * 397 + (WorldComponent_SettlementIntel.IsTracking() ? 1 : 0);
                return h;
            }
        }

        private static OutpostStatsSnapshot BuildKnownTo(WorldObject worldObject)
        {
            var snap = new OutpostStatsSnapshot();
            var section = new OutpostStatsSection
            {
                Title = "TSA_WD_SettlementIntel_Section_KnownTo".Translate().ToString(),
                FullWidth = true
            };
            snap.Sections.Add(section);

            var intel = WorldComponent_SettlementIntel.Get();
            var factions = Find.FactionManager?.AllFactionsListForReading;
            if (factions == null)
            {
                section.Rows.Add(new OutpostStatRow
                {
                    Label = "TSA_WD_SettlementIntel_Row_None".Translate().ToString(),
                    Value = "-",
                    Tooltip = ""
                });
                return snap;
            }

            var ordered = new List<Faction>();
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                if (f == null || f.def == null || f.def.hidden || f.temporary) continue;
                ordered.Add(f);
            }
            ordered.Sort((a, b) =>
            {
                if (a.IsPlayer != b.IsPlayer) return a.IsPlayer ? -1 : 1;
                return string.CompareOrdinal(a.Name, b.Name);
            });

            for (int i = 0; i < ordered.Count; i++)
            {
                Faction f = ordered[i];
                bool knows = intel != null && intel.IsDiscovered(f, worldObject);
                string yes = "TSA_WD_SettlementIntel_Yes".Translate().ToString();
                string no = "TSA_WD_SettlementIntel_No".Translate().ToString();
                section.Rows.Add(new OutpostStatRow
                {
                    Label = f.Name,
                    Value = knows ? yes : no,
                    Tooltip = knows
                        ? "TSA_WD_SettlementIntel_KnownFactionYesTip".Translate(f.Name).ToString()
                        : "TSA_WD_SettlementIntel_KnownFactionNoTip".Translate(f.Name).ToString(),
                    LabelFaction = f
                });
            }

            return snap;
        }
    }
}
