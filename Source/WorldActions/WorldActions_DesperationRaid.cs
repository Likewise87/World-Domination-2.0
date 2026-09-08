using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Strategy on Settlement Loss: on NPC settlement conquest loss, an eligible threatened cluster
    /// either Turtles (ally-radius consolidate / fortify-in-place) or packs for a desperation raid.
    /// Shared per-faction CD on <c>desperationRaidCooldownByFaction</c>. Stage gate is player-controlled
    /// (not difficulty presets). Cluster membership: connected component among V's remaining sites
    /// with edge ≤ <see cref="ClusterEdgeTiles"/>.
    /// </summary>
    public static class WorldActions_DesperationRaid
    {
        public const int ClusterEdgeTiles = WorldActions_Turtle.ClusterEdgeTiles;
        public const float WeakNearBandTiles = 40f;

        private static readonly HashSet<int> suppressLossNotifyIds = new HashSet<int>();
        private static readonly HashSet<int> notifiedLossIds = new HashSet<int>();

        private static readonly List<Settlement> tmpFactionSettlements = new List<Settlement>();
        private static readonly List<Settlement> tmpCluster = new List<Settlement>();
        private static readonly Queue<Settlement> tmpBfs = new Queue<Settlement>();
        private static readonly HashSet<int> tmpSeen = new HashSet<int>();

        /// <summary>Skip loss bus for voluntary Destroy/Remove (FA pack-up, revolt, buy, desperation pack).</summary>
        public static void SuppressLossNotify(Settlement settlement)
        {
            if (settlement != null)
                suppressLossNotifyIds.Add(settlement.ID);
        }

        /// <summary>Call before Destroy/Remove of an NPC settlement on the include list.</summary>
        public static void NotifyNpcSettlementLost(Faction victim, int tile, Faction takerOrNull, int settlementId = -1)
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            if (victim == null || victim.IsPlayer || victim.defeated) return;
            if (settlementId >= 0)
            {
                if (suppressLossNotifyIds.Remove(settlementId)) return;
                if (!notifiedLossIds.Add(settlementId)) return;
            }

            try
            {
                TryTriggerAfterLoss(victim, tile, takerOrNull, settlementId);
            }
            catch (Exception e)
            {
                Log.Error($"[TSA WD] DesperationRaid NotifyNpcSettlementLost failed: {e}");
            }
        }

        public static void NotifyNpcSettlementLost(Settlement lost, Faction takerOrNull)
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            if (lost == null || lost.Faction == null || lost.Faction.IsPlayer) return;
            NotifyNpcSettlementLost(lost.Faction, lost.Tile.tileId, takerOrNull, lost.ID);
        }

        public static bool IsDesperationRaidTraveler(WorldObject_Traveler t) =>
            t != null && t.isDesperationRaid;

        public static SettlementTier TierFromHostStrength(float strength)
        {
            int idx = WorldStatsUtils.TierIndexFromWorldStrengthTotal(strength);
            if (idx >= 4) return SettlementTier.T4;
            if (idx >= 3) return SettlementTier.T3;
            if (idx >= 2) return SettlementTier.T2;
            return SettlementTier.T1;
        }

        public static void OnDesperationVictory(WorldObject_Traveler traveler, int tile, WorldObject builtBySiteOrNull)
        {
            if (traveler == null || !traveler.isDesperationRaid || tile < 0) return;
            Faction faction = traveler.Faction;
            if (faction == null) return;

            Settlement builtBySettlement = Find.WorldObjects.SettlementAt(tile);
            SettlementTier kitTier;
            var settleComp = builtBySettlement?.GetComponent<CompViralSpread>();
            if (settleComp != null)
                kitTier = settleComp.tier;
            else
            {
                float hostStr = Mathf.Max(traveler.travelerStrength, traveler.initialStrength);
                kitTier = TierFromHostStrength(hostStr);
            }

            WorldActions_FortifyKit.TryPlaceFortifyKit(tile, faction, kitTier, builtBySettlement, builtBySiteOrNull);
        }

        public static void TryPlaceFortifyKit(
            int centerTile,
            Faction faction,
            SettlementTier kitTier,
            Settlement builtBySettlement,
            WorldObject builtBySite)
        {
            WorldActions_FortifyKit.TryPlaceFortifyKit(centerTile, faction, kitTier, builtBySettlement, builtBySite);
        }

        /// <summary>Thin wrappers — rally absorb/tick lives on <see cref="WorldActions_AssaultRally"/>.</summary>
        public static void ExecuteDesperationRallyArrival(WorldObject_Traveler traveler) =>
            WorldActions_AssaultRally.ExecuteRallyArrival(traveler);

        public static void TickDesperationHost(WorldObject_Traveler traveler) =>
            WorldActions_AssaultRally.TickRallyHost(traveler);

        /// <summary>Used by AssaultRally when a desperation host's endpoint died.</summary>
        public static WorldObject RetargetAggressorSitePublic(Faction victim, int fromTile, int aggressorLoadId) =>
            RetargetAggressorSite(victim, fromTile, aggressorLoadId, Find.World.GetComponent<WorldComponent_SpreadManager>());

        /// <summary>
        /// Dev tool: destroy <paramref name="seed"/> (simulated loss) and force-pack its connected
        /// surrounding cluster into a desperation raid at the player. Skips enable/chance/weak/CD and
        /// connected-cluster size gates (falls back to all remaining sites).
        /// </summary>
        public static bool DebugForceSimulatingLoss(Settlement seed, out string message)
        {
            message = null;
            if (seed == null || seed.Destroyed)
            {
                message = "no settlement";
                return false;
            }

            Faction victim = seed.Faction;
            if (victim == null || victim.IsPlayer || victim.defeated)
            {
                message = "click an NPC settlement";
                return false;
            }

            if (!WorldActions_Utils.IsWdParticipant(victim))
            {
                message = $"{victim.Name} is not a WD participant";
                return false;
            }

            int lostTile = seed.Tile.tileId;
            string lostLabel = seed.LabelCap;
            SuppressLossNotify(seed);
            seed.Destroy();
            Outpost_EstablishmentRequirements.InvalidateNearbyCountCache();

            if (!TryTriggerAfterLossCore(victim, lostTile, Faction.OfPlayer, forceDebug: true, excludeSettlementId: -1, out string fail))
            {
                message = $"destroyed {lostLabel}; pack failed ({fail})";
                return false;
            }

            message = $"destroyed {lostLabel}; forced desperation pack for {victim.Name}";
            return true;
        }

        private static void TryTriggerAfterLoss(Faction victim, int lostTile, Faction takerOrNull, int excludeSettlementId)
        {
            TryTriggerAfterLossCore(victim, lostTile, takerOrNull, forceDebug: false, excludeSettlementId, out _);
        }

        private static bool TryTriggerAfterLossCore(
            Faction victim, int lostTile, Faction takerOrNull, bool forceDebug, int excludeSettlementId, out string failReason)
        {
            failReason = null;
            var seth = WorldDominationMod.settings;
            if (seth == null)
            {
                failReason = "no settings";
                return false;
            }

            if (!WorldActions_Utils.IsWdParticipant(victim))
            {
                failReason = "not WD participant";
                return false;
            }

            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null)
            {
                failReason = "no spread manager";
                return false;
            }

            // Strategy on Settlement Loss stage gate (Def Always). Not driven by difficulty presets.
            if (!forceDebug && !WdEscalation.PassesGate(seth.gateThreatDesperation, manager))
            {
                failReason = "strategy stage gate";
                WDVerbose.Msg($"SettlementLossStrategy skip reason=stage-gate gate={seth.gateThreatDesperation}");
                return false;
            }

            if (forceDebug)
            {
                manager.desperationRaidCooldownByFaction?.Remove(victim.loadID);
                manager.factionComebackCooldownByFaction?.Remove(victim.loadID);
            }
            else if (IsFactionOnDesperationCooldown(manager, victim, seth))
            {
                failReason = "cooldown";
                WDVerbose.Msg($"SettlementLossStrategy skip reason=cooldown faction={victim.Name}");
                return false;
            }
            else if (WorldActions_Revolt.IsFactionOnComebackCooldown(manager, victim))
            {
                failReason = "comeback cooldown";
                WDVerbose.Msg($"SettlementLossStrategy skip reason=comeback-cd faction={victim.Name}");
                return false;
            }

            // Never pack the lost site itself (Remove Prefix / pre-Destroy notify still list it).
            CollectFactionSettlements(victim, tmpFactionSettlements, excludeSettlementId, lostTile);
            if (tmpFactionSettlements.Count < 1)
            {
                failReason = "no remaining settlements";
                WDVerbose.Msg($"SettlementLossStrategy skip reason=no-remaining faction={victim.Name}");
                return false;
            }

            BuildConnectedCluster(victim, lostTile, tmpFactionSettlements, tmpCluster);
            if (tmpCluster.Count < 1)
            {
                if (forceDebug && tmpFactionSettlements.Count > 0)
                {
                    tmpCluster.Clear();
                    tmpCluster.AddRange(tmpFactionSettlements);
                    WDVerbose.Msg($"SettlementLossStrategy debug fallback pack-all-remaining n={tmpCluster.Count} faction={victim.Name}");
                }
                else
                {
                    failReason = "empty cluster (no surrounding sites within edge)";
                    WDVerbose.Msg($"SettlementLossStrategy skip reason=empty-cluster faction={victim.Name}");
                    return false;
                }
            }

            int minCluster = Mathf.Max(2, seth.turtleMinClusterSize);
            if (!forceDebug && tmpCluster.Count < minCluster)
            {
                failReason = "cluster too small";
                WDVerbose.Msg($"SettlementLossStrategy skip reason=min-cluster n={tmpCluster.Count} need={minCluster} faction={victim.Name}");
                return false;
            }

            if (!forceDebug)
            {
                float ownStr = WorldActions_Turtle.SumClusterOffensePublic(tmpCluster);
                float hostileStr = WorldActions_Turtle.SumHostileThreatToClusterPublic(tmpCluster, victim);
                float ratio = hostileStr / Mathf.Max(1f, ownStr);
                float minRatio = Mathf.Max(0f, seth.turtlePressureRatio);
                if (ratio < minRatio)
                {
                    failReason = "low pressure";
                    WDVerbose.Msg(
                        $"SettlementLossStrategy skip reason=pressure ratio={ratio:F2} need={minRatio:F2} faction={victim.Name}");
                    return false;
                }

                // Soft fire gate: eligible but Strategy does not always act. Fail does not stamp CD.
                float fireChance = Mathf.Clamp01(seth.settlementLossStrategyFireChance);
                if (Rand.Value >= fireChance)
                {
                    failReason = "fire chance";
                    WDVerbose.Msg(
                        $"SettlementLossStrategy skip reason=fire-chance p={fireChance:F2} faction={victim.Name}");
                    return false;
                }
            }

            // Debug force always takes the desperation raid branch.
            bool chooseDesperation = forceDebug;
            if (!forceDebug)
            {
                float pDesperation = ComputeDesperationChance(tmpFactionSettlements, seth);
                chooseDesperation = Rand.Value < pDesperation;
                WDVerbose.Msg(
                    $"SettlementLossStrategy fork faction={victim.Name} pDesperation={pDesperation:F3} choose={(chooseDesperation ? "desperation" : "turtle")}");
            }

            if (!chooseDesperation)
            {
                if (!WorldActions_Turtle.TryTriggerReactiveFromLoss(manager, seth, victim, tmpCluster, forceDebug))
                {
                    failReason = "reactive turtle failed";
                    WDVerbose.Msg($"SettlementLossStrategy abort turtle-failed faction={victim.Name}");
                    return false;
                }
                StampFactionStrategyCooldown(manager, victim, seth);
                failReason = null;
                return true;
            }

            return LaunchDesperationRaid(victim, takerOrNull, forceDebug, manager, seth, out failReason);
        }

        /// <summary>Equal-share bands × desperation likelihood slider. Healthy empires → 0.</summary>
        private static float ComputeDesperationChance(List<Settlement> remaining, WorldDominationSettings seth)
        {
            float victimStr = 0f;
            for (int i = 0; i < remaining.Count; i++)
                victimStr += remaining[i].GetComponent<CompViralSpread>()?.GetTotalLocalDefensePower() ?? 0f;

            SumLivingNpcWorldDefense(out float npcWorld, out int n);
            float relative = WorldStatsUtils.RelativeToNpcEqualShare(victimStr, npcWorld, n);

            float dyingRel = WorldDominationSettings.DefSettlementLossStrategyDyingRelative;
            float midRel = WorldDominationSettings.DefSettlementLossStrategyMidRelative;
            float baseChance;
            if (relative < dyingRel)
                baseChance = WorldDominationSettings.DefSettlementLossStrategyDyingBaseChance;
            else if (relative < midRel)
                baseChance = WorldDominationSettings.DefSettlementLossStrategyMidBaseChance;
            else
                baseChance = 0f;

            float likelihood = Mathf.Clamp01(seth.desperationChanceOnLoss);
            float p = baseChance * likelihood;
            WDVerbose.Msg(
                $"SettlementLossStrategy relative={relative:F2} n={n} victimStr={victimStr:F0} npcWorld={npcWorld:F0} base={baseChance:F2} likelihood={likelihood:F2} p={p:F3}");
            return p;
        }

        private static void SumLivingNpcWorldDefense(out float npcWorld, out int n)
        {
            npcWorld = 0f;
            n = 0;
            var byFaction = new Dictionary<Faction, float>();
            var settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s?.Faction == null || s.Faction.IsPlayer || s.Faction.defeated) continue;
                if (!WorldActions_Utils.IsWdParticipant(s.Faction)) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;
                float str = s.GetComponent<CompViralSpread>()?.GetTotalLocalDefensePower() ?? 0f;
                if (!byFaction.TryGetValue(s.Faction, out float cur))
                    byFaction[s.Faction] = str;
                else
                    byFaction[s.Faction] = cur + str;
            }

            foreach (var kv in byFaction)
            {
                if (kv.Value <= 0.01f) continue;
                npcWorld += kv.Value;
                n++;
            }
        }

        private static bool LaunchDesperationRaid(
            Faction victim,
            Faction takerOrNull,
            bool forceDebug,
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth,
            out string failReason)
        {
            failReason = null;

            int centroid = WorldActions_AssaultRally.ComputeCentroidTile(tmpCluster);
            Faction aggressor = ResolveAggressor(victim, takerOrNull, centroid, manager);
            if (aggressor == null)
            {
                failReason = "no aggressor";
                WDVerbose.Msg($"DesperationRaid skip reason=no-aggressor faction={victim.Name}");
                return false;
            }

            WorldObject target = PickAggressorTarget(aggressor, centroid, tmpCluster, manager, seth);
            if (target == null)
            {
                failReason = $"no target for aggressor {aggressor.Name}";
                WDVerbose.Msg($"DesperationRaid skip reason=no-target aggressor={aggressor.Name}");
                return false;
            }

            var opts = new WorldActions_AssaultRally.AssaultRallyOptions
            {
                isDesperationRaid = true,
                aggressorFactionLoadId = aggressor.loadID,
                packRallyLogKey = "TSA_WD_Log_DesperationRaid_PackRally",
                packSoloRaidLogKey = "TSA_WD_Log_DesperationRaid_PackRaid",
                launchRaidLogKey = "TSA_WD_Log_DesperationRaid_LaunchRaid"
            };

            var launch = WorldActions_AssaultRally.LaunchAssemblies(
                tmpCluster,
                _ => target,
                opts,
                manager);

            if (launch.travelersLaunched < 1)
            {
                failReason = "zero launched (path/rally)";
                WDVerbose.Msg($"DesperationRaid abort zero-launched faction={victim.Name}");
                return false;
            }

            StampFactionStrategyCooldown(manager, victim, seth);

            string msg = "TSA_WD_Log_DesperationRaid_PackUp".Translate(victim.Name, launch.settlementsPacked, target.Label);
            manager.AddLog(new SpreadLogEntry(msg, target, null));
            WDVerbose.Msg(
                $"DesperationRaid launched faction={victim.Name} n={launch.settlementsPacked} columns={launch.columnsLaunched} tgt={target.Label} forceDebug={forceDebug}");

            if (seth.notifyDesperationRaid || forceDebug)
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_Letter_DesperationRaid_Label".Translate(victim.Name),
                    "TSA_WD_Letter_DesperationRaid_Text".Translate(target.Label),
                    LetterDefOf.ThreatBig,
                    new LookTargets(target));
            }

            failReason = null;
            return true;
        }

        private static void CollectFactionSettlements(
            Faction faction,
            List<Settlement> into,
            int excludeSettlementId = -1,
            int excludeTile = -1)
        {
            into.Clear();
            var settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s == null || s.Destroyed || s.Faction != faction) continue;
                if (excludeSettlementId >= 0 && s.ID == excludeSettlementId) continue;
                // Loss notify often runs before Remove finishes; lost site is still listed at lostTile.
                if (excludeTile >= 0 && s.Tile.tileId == excludeTile) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;
                if (s.GetComponent<CompViralSpread>() == null) continue;
                into.Add(s);
            }
        }

        /// <summary>Connected component: BFS among V sites with edge distance ≤ ClusterEdgeTiles, seeded by sites within that of lostTile.</summary>
        private static void BuildConnectedCluster(Faction faction, int lostTile, List<Settlement> all, List<Settlement> into)
        {
            into.Clear();
            tmpSeen.Clear();
            tmpBfs.Clear();

            for (int i = 0; i < all.Count; i++)
            {
                Settlement s = all[i];
                float d = Find.WorldGrid.ApproxDistanceInTiles(lostTile, s.Tile.tileId);
                if (d > ClusterEdgeTiles) continue;
                tmpBfs.Enqueue(s);
                tmpSeen.Add(s.ID);
                into.Add(s);
            }

            while (tmpBfs.Count > 0)
            {
                Settlement cur = tmpBfs.Dequeue();
                for (int i = 0; i < all.Count; i++)
                {
                    Settlement s = all[i];
                    if (tmpSeen.Contains(s.ID)) continue;
                    float d = Find.WorldGrid.ApproxDistanceInTiles(cur.Tile.tileId, s.Tile.tileId);
                    if (d > ClusterEdgeTiles) continue;
                    tmpSeen.Add(s.ID);
                    tmpBfs.Enqueue(s);
                    into.Add(s);
                }
            }
        }

        private static Faction ResolveAggressor(Faction victim, Faction taker, int centroid, WorldComponent_SpreadManager manager)
        {
            if (taker != null && !taker.defeated
                && WorldActions_Utils.SafeHostileTo(victim, taker)
                && AggressorHasTarget(taker))
                return taker;

            Faction best = null;
            float bestScore = float.MinValue;
            float bestStr = float.MinValue;
            float bestDist = float.MaxValue;

            var factions = Find.FactionManager?.AllFactionsListForReading;
            if (factions == null) return null;
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                if (f == null || f == victim || f.defeated || f.def.hidden) continue;
                if (!WorldActions_Utils.SafeHostileTo(victim, f)) continue;
                if (!AggressorHasTarget(f)) continue;

                float score = 0f;
                float str = 0f;
                float nearest = float.MaxValue;
                ScoreAggressorSites(f, centroid, ref score, ref str, ref nearest);

                if (score > bestScore
                    || (Mathf.Approximately(score, bestScore) && str > bestStr)
                    || (Mathf.Approximately(score, bestScore) && Mathf.Approximately(str, bestStr) && nearest < bestDist))
                {
                    best = f;
                    bestScore = score;
                    bestStr = str;
                    bestDist = nearest;
                }
            }
            return best;
        }

        private static bool AggressorHasTarget(Faction f)
        {
            if (f.IsPlayer)
            {
                var settlements = Find.WorldObjects.Settlements;
                for (int i = 0; i < settlements.Count; i++)
                {
                    Settlement s = settlements[i];
                    if (s?.Faction?.IsPlayer == true) return true;
                }
                IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
                return outposts != null && outposts.Count > 0;
            }

            var list = Find.WorldObjects.Settlements;
            for (int i = 0; i < list.Count; i++)
            {
                Settlement s = list[i];
                if (s?.Faction == f && PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s))
                    return true;
            }
            return false;
        }

        private static void ScoreAggressorSites(Faction f, int centroid, ref float score, ref float str, ref float nearest)
        {
            if (f.IsPlayer)
            {
                var settlements = Find.WorldObjects.Settlements;
                for (int i = 0; i < settlements.Count; i++)
                {
                    Settlement s = settlements[i];
                    if (s?.Faction?.IsPlayer == true)
                        ConsiderAggressorSite(s, centroid, ref score, ref str, ref nearest);
                }
                IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
                if (outposts != null)
                {
                    for (int i = 0; i < outposts.Count; i++)
                        ConsiderAggressorSite(outposts[i], centroid, ref score, ref str, ref nearest);
                }
                return;
            }

            var list = Find.WorldObjects.Settlements;
            for (int i = 0; i < list.Count; i++)
            {
                Settlement s = list[i];
                if (s?.Faction != f) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;
                ConsiderAggressorSite(s, centroid, ref score, ref str, ref nearest);
            }
        }

        private static void ConsiderAggressorSite(WorldObject wo, int centroid, ref float score, ref float str, ref float nearest)
        {
            if (wo == null || wo.Destroyed) return;
            float d = centroid >= 0
                ? Find.WorldGrid.ApproxDistanceInTiles(centroid, wo.Tile.tileId)
                : 999f;
            if (d < nearest) nearest = d;
            score += d <= 40f ? 3f : 1f;
            str += wo.GetComponent<CompViralSpread>()?.GetTotalLocalDefensePower() ?? 0f;
        }

        private static WorldObject PickAggressorTarget(
            Faction aggressor,
            int centroid,
            List<Settlement> cluster,
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth)
        {
            var candidates = new List<WorldObject>();
            CollectAggressorSites(aggressor, candidates);
            if (candidates.Count == 0) return null;

            float clusterStr = 0f;
            for (int i = 0; i < cluster.Count; i++)
                clusterStr += cluster[i].GetComponent<CompViralSpread>()?.offensiveStrength ?? 0f;

            WorldObject bestWeak = null;
            float bestWeakDist = float.MaxValue;
            WorldObject bestAny = null;
            float bestAnyDist = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                WorldObject tgt = candidates[i];
                // Ignore origin/target settlement raid-protection CD (HasMap / defense CD still ok to attack).
                float dist = centroid >= 0
                    ? Find.WorldGrid.ApproxDistanceInTiles(centroid, tgt.Tile.tileId)
                    : 0f;
                if (dist < bestAnyDist)
                {
                    bestAnyDist = dist;
                    bestAny = tgt;
                }

                float def = tgt.GetComponent<CompViralSpread>()?.GetTotalLocalDefensePower() ?? 0f;
                if (tgt is Settlement ps && ps.Faction?.IsPlayer == true && ps.HasMap)
                    def = RaidLaunchGate.GetColonyStorytellerDefense(tgt);

                bool weak = dist <= WeakNearBandTiles && (def <= 0.01f || clusterStr / Mathf.Max(1f, def) >= 0.5f);
                if (weak && dist < bestWeakDist)
                {
                    bestWeakDist = dist;
                    bestWeak = tgt;
                }
            }

            return bestWeak ?? bestAny;
        }

        private static void CollectAggressorSites(Faction aggressor, List<WorldObject> into)
        {
            into.Clear();
            if (aggressor.IsPlayer)
            {
                var settlements = Find.WorldObjects.Settlements;
                for (int i = 0; i < settlements.Count; i++)
                {
                    Settlement s = settlements[i];
                    if (s?.Faction?.IsPlayer == true)
                        into.Add(s);
                }
                IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
                if (outposts != null)
                {
                    for (int i = 0; i < outposts.Count; i++)
                    {
                        if (outposts[i] != null && !outposts[i].Destroyed)
                            into.Add(outposts[i]);
                    }
                }
                return;
            }

            var list = Find.WorldObjects.Settlements;
            for (int i = 0; i < list.Count; i++)
            {
                Settlement s = list[i];
                if (s?.Faction != aggressor) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;
                into.Add(s);
            }
        }

        private static WorldObject RetargetAggressorSite(Faction victim, int fromTile, int aggressorLoadId, WorldComponent_SpreadManager manager)
        {
            Faction aggressor = null;
            var factions = Find.FactionManager?.AllFactionsListForReading;
            if (factions != null)
            {
                for (int i = 0; i < factions.Count; i++)
                {
                    if (factions[i] != null && factions[i].loadID == aggressorLoadId)
                    {
                        aggressor = factions[i];
                        break;
                    }
                }
            }
            if (aggressor == null) return null;
            var seth = WorldDominationMod.settings;
            var emptyCluster = new List<Settlement>();
            return PickAggressorTarget(aggressor, fromTile, emptyCluster, manager, seth);
        }

        private static bool IsFactionOnDesperationCooldown(WorldComponent_SpreadManager manager, Faction faction, WorldDominationSettings seth)
        {
            if (manager.desperationRaidCooldownByFaction == null) return false;
            if (!manager.desperationRaidCooldownByFaction.TryGetValue(faction.loadID, out int until)) return false;
            return Find.TickManager.TicksGame < until;
        }

        /// <summary>Shared Strategy on Settlement Loss CD (Turtle or desperation outcome).</summary>
        public static void StampFactionStrategyCooldown(WorldComponent_SpreadManager manager, Faction faction, WorldDominationSettings seth)
        {
            if (manager.desperationRaidCooldownByFaction == null)
                manager.desperationRaidCooldownByFaction = new Dictionary<int, int>();
            float days = Mathf.Max(0.1f, seth.desperationCooldownDays);
            manager.desperationRaidCooldownByFaction[faction.loadID] =
                Find.TickManager.TicksGame + CompViralSpread.CooldownTicksFromDays(days);
        }

        private static void StampFactionDesperationCooldown(WorldComponent_SpreadManager manager, Faction faction, WorldDominationSettings seth) =>
            StampFactionStrategyCooldown(manager, faction, seth);
    }

    /// <summary>Safety net for vanilla CheckDefeated leave paths that Remove without an explicit Notify.</summary>
    [HarmonyPatch(typeof(WorldObjectsHolder), nameof(WorldObjectsHolder.Remove))]
    public static class Patch_DesperationRaid_OnSettlementRemoved
    {
        public static void Prefix(WorldObject o)
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            if (o is not Settlement s) return;
            if (s.Faction == null || s.Faction.IsPlayer) return;
            // Do not invent a player taker — aggressor resolution falls back when null.
            WorldActions_DesperationRaid.NotifyNpcSettlementLost(s, null);
        }
    }
}
