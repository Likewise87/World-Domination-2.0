using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Inspect Stats tab for WD travelers (same chrome as settlement/outpost Stats).</summary>
    public class WITab_Traveler_Stats : WITab
    {
        private Vector2 scrollPosition;
        private float scrollViewHeight = 400f;

        private const int CacheRefreshInterval = 2500;
        private const float SearchBarH = 28f;
        private const float SearchBarGap = 6f;

        private int lastCacheTick = -1;
        private WorldObject_Traveler cachedTraveler;
        private OutpostStatsSnapshot cachedSnapshot;
        private bool forceRefreshRequested;

        private string searchFilter = "";
        private string searchNeedleLower = "";
        private readonly List<OutpostStatsSection> filteredSections = new List<OutpostStatsSection>(8);
        private OutpostStatsSnapshot filterSourceSnap;
        private string filterSourceNeedle = "";

        public WITab_Traveler_Stats()
        {
            size = new Vector2(820f, 560f);
            labelKey = "TSA_WD_TravelerStats_TabLabel";
        }

        public override bool IsVisible => SelObject is WorldObject_Traveler;

        private float HeaderConsumedHeight =>
            OutpostTabStatsUi.TabHeaderConsumedHeight + SearchBarH + SearchBarGap;

        protected override void FillTab()
        {
            if (!(SelObject is WorldObject_Traveler traveler) || traveler.Destroyed) return;

            Rect body = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
            OutpostTabStatsUi.DrawHeadline(body, traveler.LabelCap);

            float tableRight = body.x + body.width - OutpostTabStatsUi.ScrollbarRightPadding - 8f;
            const float refreshW = 70f;
            Rect refreshRect = new Rect(tableRight - refreshW, body.y, refreshW, 24f);
            if (Widgets.ButtonText(refreshRect, "TSA_WD_OutpostStats_Refresh".Translate()))
                forceRefreshRequested = true;
            if (Mouse.IsOver(refreshRect))
                TooltipHandler.TipRegion(refreshRect, "TSA_WD_OutpostStats_RefreshTooltip".Translate());

            EnsureSnapshotFresh(traveler);
            OutpostStatsSnapshot snap = cachedSnapshot;
            if (snap == null) return;

            float searchY = body.y + OutpostTabStatsUi.TabHeaderConsumedHeight;
            DrawSearchBar(new Rect(body.x, searchY, body.width - OutpostTabStatsUi.ScrollbarRightPadding, SearchBarH));

            EnsureFilteredSections(snap);

            Rect scrollOuter = new Rect(
                body.x,
                body.y + HeaderConsumedHeight,
                body.width,
                body.height - HeaderConsumedHeight);
            float contentWidth = scrollOuter.width - OutpostTabStatsUi.ScrollbarRightPadding;
            scrollViewHeight = OutpostTabStatsUi.MeasureContentHeight(filteredSections, contentWidth);
            if (scrollViewHeight < scrollOuter.height)
                scrollViewHeight = scrollOuter.height;
            Rect viewRect = new Rect(0f, 0f, contentWidth, scrollViewHeight);

            Widgets.BeginScrollView(scrollOuter, ref scrollPosition, viewRect);
            OutpostTabStatsUi.DrawStatsLayout(0f, 0f, contentWidth, filteredSections);
            Widgets.EndScrollView();
        }

        private void DrawSearchBar(Rect rect)
        {
            string next = Widgets.TextField(rect, searchFilter);
            if (next != searchFilter)
            {
                searchFilter = next ?? "";
                searchNeedleLower = string.IsNullOrEmpty(searchFilter)
                    ? ""
                    : searchFilter.Trim().ToLowerInvariant();
                filterSourceSnap = null;
            }
            if (string.IsNullOrEmpty(searchFilter))
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                Widgets.Label(rect, "TSA_WD_OutpostStats_SearchPlaceholder".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
        }

        private void EnsureFilteredSections(OutpostStatsSnapshot snap)
        {
            if (snap == filterSourceSnap && searchNeedleLower == filterSourceNeedle)
                return;
            filterSourceSnap = snap;
            filterSourceNeedle = searchNeedleLower;
            OutpostTabStatsUi.FillFilteredSections(snap.Sections, searchNeedleLower, filteredSections);
        }

        private void EnsureSnapshotFresh(WorldObject_Traveler traveler)
        {
            int now = Find.TickManager.TicksGame;
            bool dirty = forceRefreshRequested
                || cachedSnapshot == null
                || !ReferenceEquals(cachedTraveler, traveler)
                || now - lastCacheTick >= CacheRefreshInterval;
            if (!dirty) return;
            forceRefreshRequested = false;
            lastCacheTick = now;
            cachedTraveler = traveler;
            cachedSnapshot = TravelerStatsSnapshot.Build(traveler);
        }
    }

    /// <summary>Builds <see cref="OutpostStatsSnapshot"/> sections for a WD traveler inspect Stats tab.</summary>
    public static class TravelerStatsSnapshot
    {
        public static OutpostStatsSnapshot Build(WorldObject_Traveler traveler)
        {
            var snap = new OutpostStatsSnapshot();
            if (traveler == null) return snap;

            snap.Sections.Add(BuildRouteSection(traveler));
            snap.Sections.Add(BuildForceSection(traveler));
            snap.Sections.Add(BuildAmbushSection(traveler));
            if (ShowsPlayerRaidEconomyRows(traveler))
                snap.Sections.Add(BuildPlayerThreatSection(traveler));

            return snap;
        }

        private static OutpostStatsSection BuildRouteSection(WorldObject_Traveler traveler)
        {
            var section = new OutpostStatsSection { Title = "TSA_WD_TravelerStats_Section_Route".Translate() };
            Add(section, "TSA_WD_TravelerStats_Row_Mission", traveler.mission.ToString());
            Add(section, "TSA_WD_TravelerStats_Row_Faction", traveler.Faction?.Name ?? "?");
            Add(section, "TSA_WD_TravelerStats_Row_Origin", traveler.originObject?.LabelCap ?? "?");
            Add(section, "TSA_WD_TravelerStats_Row_Destination", traveler.targetObject?.LabelCap ?? "?");
            if (IsRaidLikeMission(traveler))
            {
                Add(section, "TSA_WD_TravelerStats_Row_RaidTargetKind",
                    ClassifyRaidTarget(traveler.targetObject));
            }
            return section;
        }

        private static OutpostStatsSection BuildForceSection(WorldObject_Traveler traveler)
        {
            var section = new OutpostStatsSection { Title = "TSA_WD_TravelerStats_Section_Force".Translate() };
            Add(section, "TSA_WD_TravelerStats_Row_Strength",
                $"{traveler.travelerStrength:F0} / {traveler.initialStrength:F0}");

            float remainingPct = traveler.initialStrength > 0.01f
                ? Mathf.Clamp01(traveler.travelerStrength / traveler.initialStrength) * 100f
                : 0f;
            Add(section, "TSA_WD_TravelerStats_Row_StrengthRemaining",
                remainingPct.ToString("F0") + "%");

            string eta = traveler.TryGetTotalExpectedTravelDays(out float days) && days > 0f
                ? days.ToString("F1")
                : "?";
            Add(section, "TSA_WD_TravelerStats_Row_EtaDays", eta);

            Add(section, "TSA_WD_TravelerStats_Row_TravelMode", FormatTravelMode(traveler));
            if (traveler.attritionResting)
            {
                Add(section, "TSA_WD_TravelerStats_Row_AttritionResting",
                    "TSA_WD_TravelerStats_Yes".Translate().ToString());
            }
            return section;
        }

        private static OutpostStatsSection BuildAmbushSection(WorldObject_Traveler traveler)
        {
            var section = new OutpostStatsSection { Title = "TSA_WD_TravelerStats_Section_Ambush".Translate() };
            var seth = WorldDominationMod.settings;
            float eff = SettlementAmbushUtility.GetEffectiveAmbushRangeTiles(
                null, traveler.travelerStrength, traveler.mission, seth);
            float fullWatch = seth?.settlementAmbushWatchRangeTiles
                ?? WorldDominationSettings.DefSettlementAmbushWatchRangeTiles;
            Add(section, "TSA_WD_TravelerStats_Row_AmbushRangeBand",
                "TSA_WD_TravelerStats_Row_AmbushRangeBandValue".Translate(
                    eff.ToString("F1"), fullWatch.ToString("F0")).ToString(),
                "TSA_WD_TravelerStats_Row_AmbushRangeBandTip".Translate().ToString());
            Add(section, "TSA_WD_TravelerStats_Row_CanBeAmbushed",
                YesNo(traveler.canBeAmbushed));
            return section;
        }

        private static OutpostStatsSection BuildPlayerThreatSection(WorldObject_Traveler traveler)
        {
            var section = new OutpostStatsSection { Title = "TSA_WD_TravelerStats_Section_PlayerThreat".Translate() };
            Add(section, "TSA_WD_TravelerStats_Row_BeyondNormalRange",
                YesNo(traveler.beyondNormalAttackRange));
            Add(section, "TSA_WD_TravelerStats_Row_CapStamped",
                YesNo(traveler.playerWdRaidCapStampTick >= 0));
            Add(section, "TSA_WD_TravelerStats_Row_Contacted",
                YesNo(traveler.playerRaidContacted));
            return section;
        }

        private static string FormatTravelMode(WorldObject_Traveler traveler)
        {
            if (traveler.mission == TravelerMission.RaidGravship)
                return "TSA_WD_TravelerStats_Mode_Gravship".Translate();
            if (traveler.mission == TravelerMission.RaidDropPod
                || traveler.mission == TravelerMission.RapidResponseDropPod
                || WD_PathFollower.IsBallisticWorldFlight(traveler))
                return "TSA_WD_TravelerStats_Mode_DropPod".Translate();
            return "TSA_WD_TravelerStats_Mode_Land".Translate();
        }

        private static string YesNo(bool value) =>
            value
                ? "TSA_WD_TravelerStats_Yes".Translate().ToString()
                : "TSA_WD_TravelerStats_No".Translate().ToString();

        private static bool IsRaidLikeMission(WorldObject_Traveler traveler)
        {
            if (traveler == null) return false;
            if (PlayerWdRaidDispatchUtil.IsPlayerBoundRaidMission(traveler.mission)) return true;
            if (traveler.mission == TravelerMission.MassRelocation) return true;
            return traveler.isInvasionRaid;
        }

        /// <summary>
        /// Beyond-normal-range and raid-cap stamp rows: only for raid / invasion / vanguard
        /// travelers whose destination is the player.
        /// </summary>
        private static bool ShowsPlayerRaidEconomyRows(WorldObject_Traveler traveler)
        {
            if (!IsRaidLikeMission(traveler)) return false;
            return PlayerWdRaidDispatchUtil.IsPlayerBoundRaidTarget(traveler.targetObject);
        }

        private static string ClassifyRaidTarget(WorldObject target)
        {
            if (target == null) return "?";
            if (target is WorldObject_WD_Outpost) return "TSA_WD_TravelerStats_RaidTarget_Outpost".Translate();
            if (target is Settlement s && s.Faction?.IsPlayer == true && s.HasMap)
                return "TSA_WD_TravelerStats_RaidTarget_Colony".Translate();
            if (target is Settlement) return "TSA_WD_TravelerStats_RaidTarget_Settlement".Translate();
            return target.LabelCap;
        }

        private static void Add(OutpostStatsSection section, string labelKey, string value, string tip = null)
        {
            section.Rows.Add(new OutpostStatRow
            {
                Label = labelKey.Translate().ToString(),
                Value = value ?? "?",
                Tooltip = tip ?? ""
            });
        }
    }
}
