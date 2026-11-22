using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Inspect Stats tab for player WD outposts, NPC settlements, AT turrets, and player map colonies.
    /// Sub-panes: Main Stats / Intel (Intel when settlement-intel gate is configured).
    /// Player map colonies: Intel only (no Main Stats).
    /// </summary>
    public class WITab_OutpostAndSettlement_Stats : WITab
    {
        private enum StatsPane : byte { Main = 0, Intel = 1 }

        private Vector2 scrollPosition;
        private Vector2 intelScrollPosition;
        private float scrollViewHeight = 400f;

        private const int CacheRefreshInterval = 2500;
        private const float SearchBarH = 28f;
        private const float SearchBarGap = 6f;
        private const float TabsToHeadlineGap = 12f;

        private StatsPane statsPane;
        private WorldObject paneDefaultedFor;
        private readonly List<TabRecord> statsPaneTabs = new List<TabRecord>(2);

        private int lastCacheTick = -1;
        private WorldObject cachedWorldObject;
        private int cachedFingerprint = int.MinValue;
        private OutpostStatsSnapshot cachedSnapshot;

        private int lastIntelCacheTick = -1;
        private WorldObject cachedIntelWorldObject;
        private int cachedIntelFingerprint = int.MinValue;
        private OutpostStatsSnapshot cachedIntelSnapshot;

        private string searchFilter = "";
        private string searchNeedleLower = "";
        private readonly List<OutpostStatsSection> filteredSections = new List<OutpostStatsSection>(16);
        private OutpostStatsSnapshot filterSourceSnap;
        private string filterSourceNeedle = "";

        private static string s_paneMain;
        private static string s_paneIntel;

        public WITab_OutpostAndSettlement_Stats()
        {
            size = new Vector2(820f, 560f);
            labelKey = "TSA_WD_OutpostStats_TabLabel";
        }

        public WorldObject SelWorldObject => SelObject;

        public override bool IsVisible
        {
            get
            {
                WorldObject wo = SelObject;
                if (wo == null) return false;
                // Player map colony: Intel-only when settlement intel is configured (no CompViralSpread required).
                if (IsPlayerMapColony(wo))
                    return WorldComponent_SettlementIntel.IsGateConfigured();
                if (wo is WorldObject_AT_Turret)
                    return WorldComponent_SettlementIntel.IsGateConfigured();

                if (wo.GetComponent<CompViralSpread>() == null)
                    return false;
                if (wo is WorldObject_WD_Outpost outpost)
                    return outpost.Faction == Faction.OfPlayer;
                return true;
            }
        }

        private static bool IsPlayerMapColony(WorldObject wo) =>
            wo is Settlement settlement && settlement.Faction?.IsPlayer == true && settlement.HasMap;

        private bool ShowIntelPane(WorldObject wo)
        {
            if (!WorldComponent_SettlementIntel.IsGateConfigured() || wo == null)
                return false;
            if (IsPlayerMapColony(wo) || wo is WorldObject_AT_Turret)
                return true;
            if (wo.GetComponent<CompViralSpread>() == null)
                return false;
            return wo is Settlement || wo is WorldObject_WD_Outpost;
        }

        protected override void UpdateSize()
        {
            base.UpdateSize();
            // Colony / AT: tab chrome reads "Intel"; outposts/NPC settlements keep Stats.
            WorldObject wo = SelWorldObject;
            if (wo != null && (IsPlayerMapColony(wo) || wo is WorldObject_AT_Turret))
                labelKey = "TSA_WD_SettlementIntel_TabLabel";
            else
                labelKey = "TSA_WD_OutpostStats_TabLabel";
        }

        /// <summary>Colony map settlements only show Intel (no Main Stats combat/production pane).</summary>
        private static bool IntelOnlyNoMainTabs(WorldObject wo) => IsPlayerMapColony(wo);

        private float MainHeaderConsumedHeight =>
            OutpostTabStatsUi.TabHeaderConsumedHeight + SearchBarH + SearchBarGap;

        private static void EnsurePaneLabels()
        {
            if (s_paneMain != null) return;
            s_paneMain = "TSA_WD_StatsPane_Main".Translate().ToString();
            if (s_paneMain.Contains("TSA_WD_StatsPane_Main")) s_paneMain = "Main Stats";
            s_paneIntel = "TSA_WD_StatsPane_Intel".Translate().ToString();
            if (s_paneIntel.Contains("TSA_WD_StatsPane_Intel")) s_paneIntel = "Intel";
        }

        protected override void FillTab()
        {
            WorldObject worldObject = SelWorldObject;
            if (worldObject == null) return;

            bool intelOn = ShowIntelPane(worldObject);
            bool intelOnly = intelOn && IntelOnlyNoMainTabs(worldObject);
            if (!intelOn)
                statsPane = StatsPane.Main;
            else if (intelOnly)
                statsPane = StatsPane.Intel;
            else if (worldObject != paneDefaultedFor)
            {
                paneDefaultedFor = worldObject;
                // AT has no useful Main snapshot; open on Intel. Settlements/outposts stay on Main.
                statsPane = worldObject is WorldObject_AT_Turret ? StatsPane.Intel : StatsPane.Main;
            }

            size = worldObject is WorldObject_AT_Turret || IsPlayerMapColony(worldObject)
                ? new Vector2(500f, 500f)
                : new Vector2(820f, 560f);

            Rect outer = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
            Rect body = outer;
            // Main+Intel tabs only when both panes exist; AT / player colony skip straight to Intel.
            if (intelOn && !intelOnly)
            {
                body.yMin += TabDrawer.TabHeight;
                DrawStatsPaneTabs(body);
                body.yMin += TabsToHeadlineGap;
            }

            if (intelOn && statsPane == StatsPane.Intel)
            {
                FillIntelPane(body, worldObject);
                return;
            }

            FillMainPane(body, worldObject);
        }

        private void DrawStatsPaneTabs(Rect contentRect)
        {
            EnsurePaneLabels();
            statsPaneTabs.Clear();
            statsPaneTabs.Add(new TabRecord(
                s_paneMain,
                () => statsPane = StatsPane.Main,
                statsPane == StatsPane.Main));
            statsPaneTabs.Add(new TabRecord(
                s_paneIntel,
                () => statsPane = StatsPane.Intel,
                statsPane == StatsPane.Intel));
            TabDrawer.DrawTabs(contentRect, statsPaneTabs);
        }

        private void FillMainPane(Rect body, WorldObject worldObject)
        {
            OutpostTabStatsUi.DrawHeadline(body, BuildHeadline(worldObject));

            if (worldObject is WorldObject_AT_Turret)
            {
                EnsureSnapshotFresh(worldObject);
                // Empty main snapshot for AT: leave a short note under the headline.
                Rect note = new Rect(
                    body.x,
                    body.y + OutpostTabStatsUi.TabHeaderConsumedHeight,
                    body.width,
                    40f);
                Text.Font = GameFont.Small;
                Widgets.Label(note, "TSA_WD_StatsPane_MainEmptyAt".Translate().ToString());
                return;
            }

            EnsureSnapshotFresh(worldObject);
            OutpostStatsSnapshot snap = cachedSnapshot;
            if (snap == null) return;

            float searchY = body.y + OutpostTabStatsUi.TabHeaderConsumedHeight;
            DrawSearchBar(new Rect(body.x, searchY, body.width - OutpostTabStatsUi.ScrollbarRightPadding, SearchBarH));

            EnsureFilteredSections(snap);
            IList<OutpostStatsSection> sections = filteredSections;

            Rect scrollOuter = new Rect(
                body.x,
                body.y + MainHeaderConsumedHeight,
                body.width,
                body.height - MainHeaderConsumedHeight);
            float contentWidth = scrollOuter.width - OutpostTabStatsUi.ScrollbarRightPadding;

            float bannerExtra = 0f;
            if (worldObject is WorldObject_WD_Outpost wdBanner)
            {
                float rawBanner = OutpostSkillScaling.GetBannerRawSkill(wdBanner);
                if (OutpostSkillScaling.IsDiminished(rawBanner, wdBanner.def))
                {
                    Text.Font = GameFont.Small;
                    string probe = OutpostSkillScaling.IsAtOrAboveHardCap(rawBanner)
                        ? "TSA_WD_SkillScaling_BannerHardCap".Translate(rawBanner.ToString("F0"), OutpostSkillScaling.ToEffective(rawBanner, wdBanner.def).ToString("F0")).ToString()
                        : "TSA_WD_SkillScaling_BannerSoft".Translate(rawBanner.ToString("F0"), OutpostSkillScaling.ToEffective(rawBanner, wdBanner.def).ToString("F0")).ToString();
                    bannerExtra = Mathf.Max(24f, Text.CalcHeight(probe, contentWidth - 12f)) + 12f + 6f;
                }
            }

            scrollViewHeight = OutpostTabStatsUi.MeasureContentHeight(sections, contentWidth) + bannerExtra;
            if (scrollViewHeight < scrollOuter.height)
                scrollViewHeight = scrollOuter.height;
            Rect viewRect = new Rect(0f, 0f, contentWidth, scrollViewHeight);

            Widgets.BeginScrollView(scrollOuter, ref scrollPosition, viewRect);

            float drawY = 0f;
            if (worldObject is WorldObject_WD_Outpost wdDraw)
                drawY = Outpost_Dialog_UI.DrawSkillDiminishingReturnsBanner(0f, drawY, contentWidth, wdDraw);
            OutpostTabStatsUi.DrawStatsLayout(0f, drawY, contentWidth, sections);

            Widgets.EndScrollView();
        }

        private void FillIntelPane(Rect body, WorldObject worldObject)
        {
            OutpostTabStatsUi.DrawHeadline(body, BuildIntelHeadline(worldObject));

            EnsureIntelSnapshotFresh(worldObject);
            OutpostStatsSnapshot snap = cachedIntelSnapshot;
            if (snap == null) return;

            const float headerH = OutpostTabStatsUi.TabHeaderConsumedHeight;
            Rect scrollOuter = new Rect(body.x, body.y + headerH, body.width, body.height - headerH);
            float contentWidth = scrollOuter.width - OutpostTabStatsUi.ScrollbarRightPadding;

            float h = OutpostTabStatsUi.MeasureContentHeight(snap.Sections, contentWidth);
            if (h < scrollOuter.height)
                h = scrollOuter.height;
            Rect viewRect = new Rect(0f, 0f, contentWidth, h);

            Widgets.BeginScrollView(scrollOuter, ref intelScrollPosition, viewRect);
            OutpostTabStatsUi.DrawStatsLayout(0f, 0f, contentWidth, snap.Sections);
            Widgets.EndScrollView();
        }

        private void DrawSearchBar(Rect searchFieldRect)
        {
            string oldFilter = searchFilter;
            searchFilter = Widgets.TextField(searchFieldRect, searchFilter ?? "");
            if (searchFilter != oldFilter)
            {
                searchNeedleLower = string.IsNullOrEmpty(searchFilter)
                    ? ""
                    : searchFilter.Trim().ToLowerInvariant();
                scrollPosition = Vector2.zero;
                filterSourceSnap = null;
            }

            if (string.IsNullOrEmpty(searchFilter))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                Widgets.Label(searchFieldRect, "TSA_WD_OutpostStats_SearchPlaceholder".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
        }

        private void EnsureFilteredSections(OutpostStatsSnapshot snap)
        {
            if (snap == null)
            {
                filteredSections.Clear();
                filterSourceSnap = null;
                return;
            }

            if (filterSourceSnap == snap && filterSourceNeedle == searchNeedleLower)
                return;

            filterSourceSnap = snap;
            filterSourceNeedle = searchNeedleLower ?? "";
            OutpostTabStatsUi.FillFilteredSections(snap.Sections, filterSourceNeedle, filteredSections);
        }

        private static string BuildHeadline(WorldObject worldObject)
        {
            if (worldObject is WorldObject_WD_Outpost outpost)
                return OutpostTranslationUtil.TabHeadline(outpost, "TSA_WD_OutpostStats_TabLabel");
            if (worldObject is WorldObject_AT_Turret)
                return "TSA_WD_WorldObjectStats_Headline".Translate(worldObject.LabelCap).ToString();
            return "TSA_WD_WorldObjectStats_Headline".Translate(worldObject.LabelCap).ToString();
        }

        private static string BuildIntelHeadline(WorldObject worldObject)
        {
            if (worldObject is WorldObject_WD_Outpost outpost)
                return OutpostTranslationUtil.TabHeadline(outpost, "TSA_WD_SettlementIntel_TabLabel");
            if (worldObject is WorldObject_AT_Turret)
                return "TSA_WD_ATIntel_Headline".Translate(worldObject.LabelCap).ToString();
            return "TSA_WD_SettlementIntel_Headline".Translate(worldObject.LabelCap).ToString();
        }

        private void EnsureSnapshotFresh(WorldObject worldObject)
        {
            int tick = Find.TickManager.TicksGame;
            int fp = ComputeFingerprint(worldObject);
            bool dirty = cachedWorldObject != worldObject
                || cachedSnapshot == null
                || tick - lastCacheTick >= CacheRefreshInterval
                || fp != cachedFingerprint;
            if (!dirty) return;

            lastCacheTick = tick;
            cachedWorldObject = worldObject;
            cachedFingerprint = fp;
            cachedSnapshot = OutpostStatsSnapshot.Build(worldObject);
            filterSourceSnap = null;
        }

        private void EnsureIntelSnapshotFresh(WorldObject worldObject)
        {
            int tick = Find.TickManager.TicksGame;
            int fp = ComputeIntelFingerprint(worldObject);
            if (cachedIntelSnapshot != null
                && cachedIntelWorldObject == worldObject
                && cachedIntelFingerprint == fp
                && tick - lastIntelCacheTick < CacheRefreshInterval)
                return;

            lastIntelCacheTick = tick;
            cachedIntelWorldObject = worldObject;
            cachedIntelFingerprint = fp;
            cachedIntelSnapshot = SettlementIntelSnapshot.Build(worldObject);
        }

        private static int BucketFloat(float v, float scale = 1f) => Mathf.RoundToInt(v * scale);

        /// <summary>
        /// Fingerprint for cooldown rows: exact end tick (Ready→CD is immediate) plus remaining buckets
        /// so F1 day countdowns refresh without waiting for <see cref="CacheRefreshInterval"/>.
        /// </summary>
        private static int CooldownFingerprint(int endTick)
        {
            int now = Find.TickManager.TicksGame;
            if (endTick <= 0 || now >= endTick) return 0;
            return endTick ^ (((endTick - now) / 250) * 397);
        }

        private static int ComputeIntelFingerprint(WorldObject worldObject)
        {
            if (worldObject == null) return 0;
            unchecked
            {
                int h = worldObject.ID;
                var intel = WorldComponent_SettlementIntel.Get();
                h = h * 397 + (intel?.Version ?? 0);
                h = h * 397 + (WorldComponent_SettlementIntel.IsTracking() ? 1 : 0);
                h = h * 397 + (WorldComponent_SettlementIntel.IsActive() ? 1 : 0);
                h = h * 397 + (int)(WorldDominationMod.settings?.gateSettlementIntel ?? WdThreatStageGate.Never);
                var mgr = Find.World?.GetComponent<WorldComponent_SpreadManager>();
                if (mgr != null)
                    h = h * 397 + (int)mgr.cachedEscalationStage;
                CompViralSpread comp = worldObject.GetComponent<CompViralSpread>();
                if (comp != null)
                    h = h * 397 + (int)comp.tier;
                h = h * 397 + (Find.TickManager.TicksGame / 2500);
                h = h * 397 + WorldObject_Traveler.LiveTravelers.Count;
                h = h * 397 + (Find.WorldObjects?.Caravans?.Count ?? 0);
                return h;
            }
        }

        private static int ComputeFingerprint(WorldObject worldObject)
        {
            if (worldObject == null) return 0;
            unchecked
            {
                int h = worldObject.ID;
                h = h * 397 + (WorldComponent_AdvancedBuildings.Get()?.Version ?? 0);
                h = h * 397 + ReinforcementNeighborCache.Generation;

                if (worldObject is WorldObject_WD_Outpost outpost)
                {
                    h = h * 397 + outpost.PawnCount;
                    h = h * 397 + outpost.WorkerPawnCount;
                    h = h * 397 + (outpost.Prisoners?.Count ?? 0);
                    h = h * 397 + outpost.CountOccupantsConsumingFood();
                    h = h * 397 + BucketFloat(outpost.TotalConstructionSkill());
                    h = h * 397 + BucketFloat(outpost.GetSkillSumRaw(SkillDefOf.Social));
                    h = h * 397 + outpost.ProductionTicksLeft;
                    h = h * 397 + outpost.StoredMechanoidPawnCount;
                    h = h * 397 + outpost.StoredTransportPawnCount;
                    CompOutpostLogistics logi = outpost.GetComponent<CompOutpostLogistics>();
                    if (logi != null)
                        h = h * 397 + BucketFloat(logi.currentFood, 0.1f);
                    WorldComponent_LogisticsManager mgr = Find.World?.GetComponent<WorldComponent_LogisticsManager>();
                    if (mgr != null)
                        h = h * 397 + mgr.LogisticsNetDisplayGeneration;
                    CompViralSpread comp = outpost.GetComponent<CompViralSpread>();
                    if (comp != null)
                    {
                        h = h * 397 + BucketFloat(comp.offensiveStrength);
                        h = h * 397 + BucketFloat(comp.defensiveStrength);
                        h = h * 397 + CooldownFingerprint(comp.raidCooldownTick);
                        h = h * 397 + CooldownFingerprint(comp.defenseCooldownTick);
                        h = h * 397 + CooldownFingerprint(comp.expansionCooldownTick);
                        h = h * 397 + CooldownFingerprint(comp.roadCooldownTick);
                        h = h * 397 + CooldownFingerprint(comp.traderCooldownTick);
                        h = h * 397 + CooldownFingerprint(comp.incidentCooldownTick);
                        h = h * 397 + CooldownFingerprint(comp.fortifyCooldownTick);
                        h = h * 397 + comp.roadTargetTile;
                        h = h * 397 + (int)comp.selectedRoadTier;
                        h = h * 397 + BucketFloat(comp.roadProgress, 10f);
                        h = h * 397 + (comp.roadBlockPlannedTiles?.Count ?? 0);
                        h = h * 397 + BucketFloat(comp.roadBlockProgress, 10f);
                        h = h * 397 + (comp.spikeTrapPlannedTiles?.Count ?? 0);
                        h = h * 397 + BucketFloat(comp.spikeTrapProgress, 10f);
                        h = h * 397 + (comp.atTurretPlannedTiles?.Count ?? 0);
                        h = h * 397 + BucketFloat(comp.atTurretProgress, 10f);
                        h = h * 397 + (int)comp.selectedAtTurretTier;
                        h = h * 397 + AtTurretUtility.CountTurretsBuiltBySite(outpost);
                        h = h * 397 + AtTurretUtility.CountInFlightTurretCrewsFrom(outpost);
                    }
                    h = h * 397 + (outpost.GetExpertThingId(OutpostExpertRole.Strategist) ?? "").GetHashCode();
                    h = h * 397 + (outpost.GetExpertThingId(OutpostExpertRole.Entertainer) ?? "").GetHashCode();
                    h = h * 397 + (outpost.GetExpertThingId(OutpostExpertRole.Cook) ?? "").GetHashCode();
                    h = h * 397 + (outpost.GetExpertThingId(OutpostExpertRole.Doctor) ?? "").GetHashCode();
                    h = h * 397 + (outpost.GetExpertThingId(OutpostExpertRole.Engineer) ?? "").GetHashCode();
                    h = h * 397 + (outpost.GetExpertThingId(OutpostExpertRole.Recruiter) ?? "").GetHashCode();
                    WorldDominationSettings settings = WorldDominationMod.settings;
                    if (settings != null)
                    {
                        h = h * 397 + BucketFloat(settings.expertStrategistMaxBonusPct, 100f);
                        h = h * 397 + BucketFloat(settings.expertEntertainerMaxBonusPct, 100f);
                        h = h * 397 + BucketFloat(settings.expertCookMaxBonusPct, 100f);
                        h = h * 397 + BucketFloat(settings.expertDoctorMaxBonusPct, 100f);
                        h = h * 397 + BucketFloat(settings.expertEngineerMaxBonusPct, 100f);
                        h = h * 397 + BucketFloat(settings.expertEngineerConstructionRadiusMaxBonusPct, 100f);
                        h = h * 397 + BucketFloat(settings.expertRecruiterMaxBonusPct, 100f);
                        h = h * 397 + settings.expertReferenceSkillLevel;
                        h = h * 397 + BucketFloat(settings.raidAllyRadius);
                        h = h * 397 + (settings.enableMidGameAllyRadiusScaling ? 1 : 0);
                        h = h * 397 + BucketFloat(settings.midGameAllyRadiusBonusPct, 100f);
                        h = h * 397 + (settings.enableLateGameAllyRadiusScaling ? 1 : 0);
                        h = h * 397 + BucketFloat(settings.lateGameAllyRadiusBonusPct, 100f);
                        h = h * 397 + (settings.enableLateGameScaling ? 1 : 0);
                        h = h * 397 + BucketFloat(settings.mortarHitChance0To50PctRange, 100f);
                        h = h * 397 + BucketFloat(settings.mortarHitChance51To75PctRange, 100f);
                        h = h * 397 + BucketFloat(settings.mortarHitChance76To100PctRange, 100f);
                        h = h * 397 + BucketFloat(settings.antiAirHitChance0To50PctRange, 100f);
                        h = h * 397 + BucketFloat(settings.antiAirHitChance51To75PctRange, 100f);
                        h = h * 397 + BucketFloat(settings.antiAirHitChance76To100PctRange, 100f);
                        h = h * 397 + BucketFloat(settings.antiAirVsMortarHitChance, 100f);
                        h = h * 397 + settings.atTurretPlayerPerSiteMax;
                    }
                    h = h * 397 + BucketFloat(outpost.GetBuiltUpgradeAllyPullRadiusBonus());
                    h = h * 397 + BucketFloat(outpost.GetBuiltUpgradePrisonerRecruitSpeedBonus(), 100f);
                    h = h * 397 + (outpost.HasBuiltUnwaveringRecruitUnlock() ? 1 : 0);
                    h = h * 397 + OutpostPowerGrid.PowerStateGeneration;
                    h = h * 397 + (OutpostPowerGrid.Enabled ? 1 : 0);
                    h = h * 397 + (OutpostPowerGrid.IsPowered(outpost) ? 1 : 0);
                    h = h * 397 + BucketFloat(OutpostPowerGrid.GetPowerDemandWatts(outpost), 0.1f);
                    h = h * 397 + BucketFloat(OutpostPowerGrid.GetSuppliedWatts(outpost), 0.1f);
                }
                else
                {
                    CompViralSpread comp = worldObject.GetComponent<CompViralSpread>();
                    if (comp != null)
                    {
                        h = h * 397 + BucketFloat(comp.offensiveStrength);
                        h = h * 397 + BucketFloat(comp.defensiveStrength);
                        h = h * 397 + CooldownFingerprint(comp.raidCooldownTick);
                        h = h * 397 + CooldownFingerprint(comp.defenseCooldownTick);
                        h = h * 397 + CooldownFingerprint(comp.expansionCooldownTick);
                        h = h * 397 + CooldownFingerprint(comp.roadCooldownTick);
                        h = h * 397 + CooldownFingerprint(comp.traderCooldownTick);
                        h = h * 397 + CooldownFingerprint(comp.ambushCooldownTick);
                        h = h * 397 + CooldownFingerprint(comp.incidentCooldownTick);
                        h = h * 397 + CooldownFingerprint(comp.fortifyCooldownTick);
                        h = h * 397 + CooldownFingerprint(comp.NpcDecontamAssessCooldownEndTick);
                        h = h * 397 + (int)comp.tier;
                        h = h * 397 + comp.actionsTakenToday;
                        h = h * 397 + comp.lastActionDay;
                    }
                    WorldDominationSettings settings = WorldDominationMod.settings;
                    if (settings != null)
                    {
                        h = h * 397 + BucketFloat(settings.raidAllyRadius);
                        h = h * 397 + (settings.enableMidGameAllyRadiusScaling ? 1 : 0);
                        h = h * 397 + BucketFloat(settings.midGameAllyRadiusBonusPct, 100f);
                        h = h * 397 + (settings.enableLateGameAllyRadiusScaling ? 1 : 0);
                        h = h * 397 + BucketFloat(settings.lateGameAllyRadiusBonusPct, 100f);
                        h = h * 397 + (settings.enableLateGameScaling ? 1 : 0);
                        h = h * 397 + BucketFloat(settings.npcMortarHitChance0To50PctRange, 100f);
                        h = h * 397 + BucketFloat(settings.npcMortarHitChance51To75PctRange, 100f);
                        h = h * 397 + BucketFloat(settings.npcMortarHitChance76To100PctRange, 100f);
                        h = h * 397 + BucketFloat(settings.npcAntiAirHitChance0To50PctRange, 100f);
                        h = h * 397 + BucketFloat(settings.npcAntiAirHitChance51To75PctRange, 100f);
                        h = h * 397 + BucketFloat(settings.npcAntiAirHitChance76To100PctRange, 100f);
                        h = h * 397 + BucketFloat(settings.npcAntiAirVsMortarHitChance, 100f);
                        h = h * 397 + (settings.gateThreatAmbush != WdThreatStageGate.Never ? 1 : 0);
                        h = h * 397 + (int)settings.settlementAmbushMinTier;
                    }
                }

                var spreadMgr = Find.World?.GetComponent<WorldComponent_SpreadManager>();
                if (spreadMgr != null)
                    h = h * 397 + (int)spreadMgr.cachedEscalationStage;

                return h;
            }
        }
    }
}
