using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class WITab_Outpost_Stats : WITab
    {
        private Vector2 scrollPosition;
        private float scrollViewHeight = 400f;

        private const int CacheRefreshInterval = 2500;
        private int lastCacheTick = -1;
        private WorldObject cachedWorldObject;
        private int cachedFingerprint = int.MinValue;
        private OutpostStatsSnapshot cachedSnapshot;
        private bool forceRefreshRequested;

        public WITab_Outpost_Stats()
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
                if (wo?.GetComponent<CompViralSpread>() == null)
                    return false;
                if (wo is WorldObject_WD_Outpost outpost)
                    return outpost.Faction == Faction.OfPlayer;
                if (wo is Settlement settlement && settlement.Faction?.IsPlayer == true && settlement.HasMap)
                    return false;
                return true;
            }
        }

        protected override void FillTab()
        {
            WorldObject worldObject = SelWorldObject;
            if (worldObject == null) return;

            Rect body = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
            string headline = BuildHeadline(worldObject);
            OutpostTabStatsUi.DrawHeadline(body, headline);

            // Right-align with stats value columns (scrollbar pad + row inset; clears inspect-pane close X).
            float tableRight = body.x + body.width - OutpostTabStatsUi.ScrollbarRightPadding - 8f;
            const float refreshW = 70f;
            Rect refreshRect = new Rect(tableRight - refreshW, body.y, refreshW, 24f);
            if (Widgets.ButtonText(refreshRect, "TSA_WD_OutpostStats_Refresh".Translate()))
                forceRefreshRequested = true;
            if (Mouse.IsOver(refreshRect))
                TooltipHandler.TipRegion(refreshRect, "TSA_WD_OutpostStats_RefreshTooltip".Translate());

            EnsureSnapshotFresh(worldObject);
            OutpostStatsSnapshot snap = cachedSnapshot;
            if (snap == null) return;

            Rect scrollOuter = new Rect(body.x, body.y + OutpostTabStatsUi.TabHeaderConsumedHeight, body.width, body.height - OutpostTabStatsUi.TabHeaderConsumedHeight);
            float contentWidth = scrollOuter.width - OutpostTabStatsUi.ScrollbarRightPadding;

            float bannerExtra = 0f;
            float rawBanner = 0f;
            if (worldObject is WorldObject_WD_Outpost wdBanner)
            {
                rawBanner = OutpostSkillScaling.GetBannerRawSkill(wdBanner);
                if (OutpostSkillScaling.IsDiminished(rawBanner))
                {
                    Text.Font = GameFont.Small;
                    string probe = OutpostSkillScaling.IsAtOrAboveHardCap(rawBanner)
                        ? "TSA_WD_SkillScaling_BannerHardCap".Translate(rawBanner.ToString("F0"), OutpostSkillScaling.ToEffective(rawBanner).ToString("F0")).ToString()
                        : "TSA_WD_SkillScaling_BannerSoft".Translate(rawBanner.ToString("F0"), OutpostSkillScaling.ToEffective(rawBanner).ToString("F0")).ToString();
                    bannerExtra = Mathf.Max(24f, Text.CalcHeight(probe, contentWidth - 12f)) + 12f + 6f;
                }
            }

            scrollViewHeight = OutpostTabStatsUi.MeasureContentHeight(snap, contentWidth) + bannerExtra;
            if (scrollViewHeight < scrollOuter.height)
                scrollViewHeight = scrollOuter.height;
            Rect viewRect = new Rect(0f, 0f, contentWidth, scrollViewHeight);

            Widgets.BeginScrollView(scrollOuter, ref scrollPosition, viewRect);

            float drawY = 0f;
            if (worldObject is WorldObject_WD_Outpost wdDraw)
                drawY = Outpost_Dialog_UI.DrawSkillDiminishingReturnsBanner(0f, drawY, contentWidth, wdDraw);
            OutpostTabStatsUi.DrawStatsLayout(0f, drawY, contentWidth, snap.Sections);

            Widgets.EndScrollView();
        }

        private static string BuildHeadline(WorldObject worldObject)
        {
            if (worldObject is WorldObject_WD_Outpost outpost)
                return OutpostTranslationUtil.TabHeadline(outpost, "TSA_WD_OutpostStats_TabLabel");

            return "TSA_WD_WorldObjectStats_Headline".Translate(worldObject.LabelCap).ToString();
        }

        private void EnsureSnapshotFresh(WorldObject worldObject)
        {
            int tick = Find.TickManager.TicksGame;
            int fp = ComputeFingerprint(worldObject);
            bool dirty = forceRefreshRequested
                || cachedWorldObject != worldObject
                || cachedSnapshot == null
                || tick - lastCacheTick >= CacheRefreshInterval
                || fp != cachedFingerprint;
            if (!dirty) return;

            forceRefreshRequested = false;
            lastCacheTick = tick;
            cachedWorldObject = worldObject;
            cachedFingerprint = fp;
            cachedSnapshot = OutpostStatsSnapshot.Build(worldObject);
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

        private static int ComputeFingerprint(WorldObject worldObject)
        {
            if (worldObject == null) return 0;
            unchecked
            {
                int h = worldObject.ID;

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
                        h = h * 397 + (settings.experimentalSettlementAmbush ? 1 : 0);
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
