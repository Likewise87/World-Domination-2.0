using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Feature C: settlements (and, via <see cref="WD_Outpost_RapidResponse"/>, player outposts) with ambush
    /// capability launch a Rapid-Response-style interceptor at a passing hostile WD traveler (any caravan-style
    /// mission except decontamination / shells / drop-pods — including hostile interceptor caravans)
    /// or a real vanilla player <see cref="Caravan"/>.
    /// Reuses <see cref="WorldComponent_SettlementWatchIndex"/> for O(1) tile lookups and
    /// <see cref="WorldActions_Traveler.SpawnRapidResponseInterceptTraveler"/> for the actual dispatch — no new
    /// per-tick scanning is added beyond the existing 20-tick caravan tile-change poll in <see cref="WD_SameTileTravelerClash"/>.
    /// </summary>
    public static class SettlementAmbushUtility
    {
        /// <summary>Max chase distance = ambush watch range × this, measured from the origin settlement.</summary>
        public const float PursuitRangeMult = 1.25f;
        /// <summary>Min (available + turret strength) / threat when the target is near or heading for an owned AT turret.</summary>
        public const float AtTurretDefenseMinRatio = 1.25f;
        /// <summary>Min available / threat when the target is near or heading for an owned advanced building.</summary>
        public const float AdvancedBuildingDefenseMinRatio = 1.0f;

        /// <summary>Strength at which ambush watch shrinks to the minimum tile band.</summary>
        public const float AmbushRangeStrengthMin = 50f;
        /// <summary>Strength at which ambush watch reaches the settlement's full watch radius.</summary>
        public const float AmbushRangeStrengthFull = 500f;
        /// <summary>Trader / Expansion use this multiplier on strength for the watch-range lerp only.</summary>
        public const float AmbushRangeTradeExpandStrengthMult = 2f;

        /// <summary>Ambush watch radius: towns with a Stable watch farther.</summary>
        public static float GetWatchRangeTiles(WorldObject settlement, WorldDominationSettings seth)
        {
            float baseR = seth?.settlementAmbushWatchRangeTiles ?? WorldDominationSettings.DefSettlementAmbushWatchRangeTiles;
            if (settlement is Settlement s && WorldComponent_AdvancedBuildings.Get()?.OwnerHasStable(s) == true)
            {
                float withStable = seth?.settlementAmbushWatchRangeWithAssetsTiles
                    ?? WorldDominationSettings.DefSettlementAmbushWatchRangeWithAssetsTiles;
                return Mathf.Max(baseR, withStable);
            }
            return baseR;
        }

        /// <summary>
        /// How far this settlement can spot <paramref name="targetStrength"/> for ambush.
        /// Trader/Expansion use 2× strength in the lerp; result is clamped to full watch.
        /// </summary>
        public static float GetEffectiveAmbushRangeTiles(
            WorldObject settlement, float targetStrength, TravelerMission? mission, WorldDominationSettings seth)
        {
            float full = GetWatchRangeTiles(settlement, seth);
            float effective = Mathf.Max(0f, targetStrength);
            if (mission == TravelerMission.Trader || mission == TravelerMission.Expansion)
                effective *= AmbushRangeTradeExpandStrengthMult;
            float t = Mathf.InverseLerp(AmbushRangeStrengthMin, AmbushRangeStrengthFull, effective);
            t = Mathf.Clamp01(t);
            return Mathf.Lerp(1f, full, t);
        }

        /// <summary>Tooltip lines for NPC settlement Stats: base watch and Stable bonus.</summary>
        public static string BuildWatchRangeStatsTooltip(Settlement settlement, WorldDominationSettings seth)
        {
            float baseR = seth?.settlementAmbushWatchRangeTiles ?? WorldDominationSettings.DefSettlementAmbushWatchRangeTiles;
            float full = GetWatchRangeTiles(settlement, seth);
            bool hasStable = settlement != null && WorldComponent_AdvancedBuildings.Get()?.OwnerHasStable(settlement) == true;
            if (hasStable)
                return "TSA_WD_OutpostStats_Row_AmbushWatchTip_Stable".Translate(
                    baseR.ToString("F0"), full.ToString("F0")).ToString();
            return "TSA_WD_OutpostStats_Row_AmbushWatchTip".Translate(full.ToString("F0")).ToString();
        }
        /// <summary>Per-target anti-dogpile (90 real-time seconds at 1x).</summary>
        public const int TargetCooldownTicks = 5400;
        /// <summary>Per-origin launch cooldown (80 real-time seconds at 1x).</summary>
        public const int OriginCooldownTicks = 4800;

        /// <summary>
        /// Ambush stage gate (<see cref="WorldDominationSettings.gateThreatAmbush"/>).
        /// Applies only when the ambush target is the player (real caravan or player-faction traveler).
        /// NPC-vs-NPC interception caravans are allowed in early game.
        /// </summary>
        private static bool PassesEscalationGateForTarget(
            Faction targetFaction, WorldComponent_SpreadManager manager, WorldDominationSettings seth)
        {
            if (seth == null) return false;
            if (targetFaction == null || !targetFaction.IsPlayer)
                return true;
            return WdEscalation.PassesGate(seth.gateThreatAmbush, manager);
        }

        /// <summary>WD-traveler half: called from <see cref="WD_PathFollower.PatherTick"/>'s tile-exit block.</summary>
        public static void TryCheckAmbush(WorldObject_Traveler traveler, int tileId)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null) return;
            if (traveler == null || traveler.Destroyed || traveler.Faction == null) return;
            if (!IsAmbushableMission(traveler.mission)) return;
            if (!traveler.canBeAmbushed) return;

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (!PassesEscalationGateForTarget(traveler.Faction, manager, seth)) return;

            var watchIndex = WorldComponent_SettlementWatchIndex.Get();
            if (watchIndex == null) return;
            if (watchIndex.IsAmbushConcurrentCapReached(seth.settlementAmbushMaxConcurrent)) return;
            if (watchIndex.IsUnderAmbushTargetCooldown(traveler)) return;

            List<WorldObject> watchers = watchIndex.GetWatchers(tileId, WatchCapability.Ambush);
            if (watchers.Count == 0) return;

            WorldObject settlement = FindBestAmbusher(
                watchers, traveler, traveler.Faction, traveler.targetObject, traveler.originObject,
                traveler.travelerStrength, traveler.mission, tileId, seth);
            if (settlement == null) return;

            // Coin flip before any strength math, per Feature C ordering.
            if (Rand.Value > seth.settlementAmbushChancePct) return;

            TryDispatch(settlement, traveler, traveler.travelerStrength, traveler.targetObject, watchIndex, seth, manager);
        }

        /// <summary>
        /// After winning a WD Ambush clash, reform creates a new caravan ID. Re-stamp target anti-dogpile
        /// so surrounding settlements cannot immediately launch another interception caravan.
        /// </summary>
        public static void NotifyCaravanReformedAfterClash(Caravan caravan)
        {
            if (caravan == null || caravan.Destroyed || caravan.Faction?.IsPlayer != true) return;
            if (WD_MapComponent_CaravanClash.FindWonClashOnTile(caravan.Tile) == null) return;
            WorldComponent_SettlementWatchIndex.Get()?.StampAmbushTarget(caravan);
        }

        /// <summary>Real-Caravan half: called from <see cref="WD_SameTileTravelerClash.TickCaravanClashDetection"/> on a detected tile change.</summary>
        public static void TryCheckAmbushForCaravan(Caravan caravan, int tileId)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null) return;
            if (caravan == null || caravan.Destroyed || caravan.Faction == null) return;
            if (!AtTurretUtility.MeetsPlayerCaravanVisibilityThreshold(caravan)) return;

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (!PassesEscalationGateForTarget(caravan.Faction, manager, seth)) return;

            var watchIndex = WorldComponent_SettlementWatchIndex.Get();
            if (watchIndex == null) return;
            if (watchIndex.IsAmbushConcurrentCapReached(seth.settlementAmbushMaxConcurrent)) return;
            if (watchIndex.IsUnderAmbushTargetCooldown(caravan)) return;

            List<WorldObject> watchers = watchIndex.GetWatchers(tileId, WatchCapability.Ambush);
            if (watchers.Count == 0) return;

            float caravanStrength = WorldComponent_SpreadManager.ComputeCaravanMortarStrengthPool(caravan);
            // Real caravans: full watch range (no strength→range shrink); visibility gate already applied above.
            WorldObject settlement = FindBestAmbusher(
                watchers, caravan, caravan.Faction, null, null,
                AmbushRangeStrengthFull, null, tileId, seth);
            if (settlement == null) return;

            var evac = WorldComponent_DropPodCrashEvac.Get();
            bool primed = evac != null && evac.IsPrimed(caravan);
            if (!primed && Rand.Value > seth.settlementAmbushChancePct) return;

            if (TryDispatch(settlement, caravan, caravanStrength, null, watchIndex, seth, manager) && primed)
                evac.Clear(caravan);
        }

        /// <summary>
        /// Ground caravan-style travelers are ambushable. Decontamination crews are excluded.
        /// Ballistic shells, AA, and Rapid Response drop-pods are not caravans.
        /// Hostile Rapid Response / ambush interceptor caravans are ambushable (interceptor-vs-interceptor).
        /// </summary>
        private static bool IsAmbushableMission(TravelerMission mission)
        {
            if (mission == TravelerMission.Decontamination) return false;
            if (mission == TravelerMission.MortarStrike
                || mission == TravelerMission.AntiAirStrike
                || mission == TravelerMission.RapidResponseDropPod)
                return false;
            return true;
        }

        /// <summary>
        /// Highest available strength among eligible watchers; ties go to the owner of a threatened asset.
        /// Raiders heading for the watcher itself are valid (only the target's origin is skipped).
        /// </summary>
        private static WorldObject FindBestAmbusher(
            List<WorldObject> watchers, WorldObject target, Faction targetFaction,
            WorldObject targetDestination, WorldObject targetOrigin,
            float targetStrength, TravelerMission? mission, int targetTileId, WorldDominationSettings seth)
        {
            SettlementTier minTier = seth?.settlementAmbushMinTier ?? WorldDominationSettings.DefSettlementAmbushMinTier;
            WorldGrid grid = Find.WorldGrid;
            WorldObject best = null;
            float bestAvail = 0f;
            bool bestOwnsAsset = false;
            for (int i = 0; i < watchers.Count; i++)
            {
                WorldObject settlement = watchers[i];
                if (settlement == null || settlement.Destroyed) continue;
                if (settlement == targetOrigin) continue;
                if (!WorldActions_Utils.CanNpcWdReactiveAction(settlement.Faction)) continue;
                if (!WorldActions_Utils.SafeHostileTo(settlement.Faction, targetFaction)) continue;
                var originComp = settlement.GetComponent<CompViralSpread>();
                if (originComp == null) continue;
                if (originComp.tier < minTier) continue;
                if (originComp.IsAmbushOnCooldown) continue;
                if (grid != null && targetTileId >= 0)
                {
                    float effRange = GetEffectiveAmbushRangeTiles(settlement, targetStrength, mission, seth);
                    float dist = grid.ApproxDistanceInTiles(settlement.Tile.tileId, targetTileId);
                    if (dist > effRange + 0.01f) continue;
                }
                float avail = WorldActions_Utils.GetAvailableRaidStrength(originComp, seth);
                if (avail <= 0f) continue;
                bool ownsAsset = FindThreatenedAsset(settlement, target, targetDestination) != null;
                bool better = best == null
                    || avail > bestAvail + 0.01f
                    || (Mathf.Abs(avail - bestAvail) <= 0.01f && ownsAsset && !bestOwnsAsset);
                if (!better) continue;
                best = settlement;
                bestAvail = avail;
                bestOwnsAsset = ownsAsset;
            }
            return best;
        }

        /// <summary>
        /// Owned AT turret or advanced building that <paramref name="target"/> is heading for or standing near
        /// (AT: within its fire range; advanced building: within the detour radius). AT turrets win ties.
        /// </summary>
        private static WorldObject FindThreatenedAsset(WorldObject settlement, WorldObject target, WorldObject targetDestination)
        {
            if (!(settlement is Settlement owner) || target == null) return null;
            if (targetDestination is WorldObject_AT_Turret destAt && !destAt.Destroyed && destAt.builtBySettlement == owner)
                return destAt;
            if (targetDestination is WorldObject_WD_AdvancedBuilding destB && !destB.Destroyed && destB.builtBySettlement == owner)
                return destB;

            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return null;
            int tile = target.Tile.tileId;
            IReadOnlyList<WorldObject_AT_Turret> ats = WorldObject_AT_Turret.LiveTurrets;
            for (int i = 0; i < ats.Count; i++)
            {
                var at = ats[i];
                if (at == null || at.Destroyed || at.builtBySettlement != owner) continue;
                if (grid.ApproxDistanceInTiles(tile, at.Tile.tileId) <= at.EffectiveRangeTiles + 0.01f) return at;
            }
            var owned = WorldComponent_AdvancedBuildings.Get()?.OwnedBy(owner);
            if (owned != null)
            {
                for (int i = 0; i < owned.Count; i++)
                {
                    var b = owned[i];
                    if (b == null || b.Destroyed) continue;
                    if (grid.ApproxDistanceInTiles(tile, b.Tile.tileId)
                        <= WorldComponent_SettlementWatchIndex.AdvancedBuildingDetourRadiusTiles + 0.01f)
                        return b;
                }
            }
            return null;
        }

        /// <summary>
        /// Min ratio for this interception: owned AT turret 1.25 (turret strength counts as support), owned advanced building 1.0,
        /// target heading for the town 1.2 with assets / <see cref="WorldDominationSettings.settlementAmbushSelfDefenseMinRatio"/> without,
        /// else <see cref="WorldDominationSettings.settlementAmbushMinStrengthRatio"/>.
        /// </summary>
        private static float ResolveMinRatio(
            WorldObject settlement, WorldObject target, WorldObject targetDestination, WorldDominationSettings seth, out float support)
        {
            support = 0f;
            WorldObject asset = FindThreatenedAsset(settlement, target, targetDestination);
            if (asset is WorldObject_AT_Turret at)
            {
                support = Mathf.Max(0f, at.strength);
                return AtTurretDefenseMinRatio;
            }
            if (asset is WorldObject_WD_AdvancedBuilding)
                return AdvancedBuildingDefenseMinRatio;
            float baseRatio = seth.settlementAmbushMinStrengthRatio;
            if (targetDestination != null && targetDestination == settlement)
            {
                bool hasAssets = settlement is Settlement s && WorldComponent_AdvancedBuildings.Get()?.OwnerHasAssets(s) == true;
                return hasAssets ? baseRatio : seth.settlementAmbushSelfDefenseMinRatio;
            }
            return baseRatio;
        }

        private static bool TryDispatch(
            WorldObject settlement, WorldObject target, float targetStrengthEstimate, WorldObject targetDestination,
            WorldComponent_SettlementWatchIndex watchIndex, WorldDominationSettings seth, WorldComponent_SpreadManager manager)
        {
            var comp = settlement.GetComponent<CompViralSpread>();
            if (comp == null) return false;
            float available = WorldActions_Utils.GetAvailableRaidStrength(comp, seth);
            if (available <= 0f) return false;

            float minRatio = ResolveMinRatio(settlement, target, targetDestination, seth, out float support);
            if (minRatio > 0f && targetStrengthEstimate > 0f && (available + support) / targetStrengthEstimate < minRatio) return false;

            float strength = RapidResponseUtility.CapSentStrength(available, targetStrengthEstimate, seth.settlementAmbushMaxStrengthRatio);
            if (strength <= 0f) return false;

            comp.strength -= strength;
            comp.CheckTierUpdate(false);
            WorldObject_Traveler response = WorldActions_Traveler.SpawnRapidResponseInterceptTraveler(settlement, target, strength);
            if (response == null)
            {
                comp.AddStrengthNoTierUpgrade(strength);
                return false;
            }

            response.isSettlementAmbushSally = true;
            watchIndex.NotifyAmbushSallySpawned();
            comp.ambushCooldownTick = Find.TickManager.TicksGame + OriginCooldownTicks;
            watchIndex.StampAmbushTarget(target);
            manager?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_SettlementAmbush".Translate(settlement.LabelCap, DescribeTargetKind(target), target.LabelCap),
                settlement, target));
            return true;
        }

        private static string DescribeTargetKind(WorldObject target)
        {
            if (target is Caravan) return "TSA_WD_TargetKind_Caravan".Translate().ToString();
            if (target is WorldObject_Traveler traveler)
            {
                switch (traveler.mission)
                {
                    case TravelerMission.Trader: return "TSA_WD_TargetKind_TraderCaravan".Translate().ToString();
                    case TravelerMission.SettlementGift: return "TSA_WD_TargetKind_GiftCaravan".Translate().ToString();
                    case TravelerMission.SettlementBribe: return "TSA_WD_TargetKind_BribeCaravan".Translate().ToString();
                    case TravelerMission.OutpostDelivery: return "TSA_WD_TargetKind_SupplyCaravan".Translate().ToString();
                    case TravelerMission.OutpostUpgrade: return "TSA_WD_TargetKind_UpgradeCaravan".Translate().ToString();
                    case TravelerMission.Raid:
                    case TravelerMission.RaidDropPod:
                    case TravelerMission.RaidGravship:
                        return "TSA_WD_TargetKind_Raid".Translate().ToString();
                    case TravelerMission.RapidResponseIntercept:
                        return "TSA_WD_TargetKind_Interceptor".Translate().ToString();
                }
            }
            return target.LabelCap.ToString();
        }
    }
}
