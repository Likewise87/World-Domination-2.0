using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Shared pack-up helpers for Vanguard forward assault (and shared FA host picking used by Invasion).
    /// Vanguard destroys sources and mass-relocates; Invasion reuses the same pick gates but launches
    /// coordinated raids without abandoning homes (see <see cref="WorldActions_Invasion"/>).
    /// </summary>
    public static class WorldActions_PackUp
    {
        public const int CountMin = 5;
        public const int CountMax = 7;
        /// <summary>Minimum settlements that must remain after a Vanguard pack-up (also FA eligibility for Invasion hosts).</summary>
        public const int HomelandReserve = 8;

        public static void ClearForwardAssaultCooldown(WorldComponent_SpreadManager manager)
        {
            if (manager == null) return;
            manager.forwardAssaultCooldownTick = -1;
            var seth = WorldDominationMod.settings;
            if (seth != null && seth.useSharedSpecialEventCooldown)
                manager.specialWorldEventCooldownTick = -1;
        }

        public static bool TryPickStrongHostileFaction(
            WorldComponent_SpreadManager manager,
            DailyWorldSnapshot snapshot,
            WorldDominationSettings seth,
            out Faction faction,
            out List<Settlement> sources)
        {
            faction = null;
            sources = null;
            var stats = snapshot?.WorldPowerStats?.FactionStats;
            if (stats == null || stats.Count == 0) return false;

            float topPct = Mathf.Clamp(seth.forwardAssaultTopPct, 0.05f, 1f);
            int take = Mathf.Max(1, Mathf.CeilToInt(stats.Count * topPct));
            take = Mathf.Min(take, stats.Count);

            float minDist = Mathf.Max(1f, seth.forwardAssaultMinDistanceFromPlayer);
            var candidates = new List<(Faction f, List<Settlement> sites, float str)>();

            for (int i = 0; i < take; i++)
            {
                Faction f = stats[i]?.faction;
                if (f == null || f.IsPlayer || f.defeated || !WorldActions_Utils.IsWdParticipant(f)) continue;
                if (!WorldActions_Utils.SafeHostileTo(f, Faction.OfPlayer)) continue;
                if (WorldActions_Revolt.IsFactionOnComebackCooldown(manager, f)) continue;

                List<Settlement> all = null;
                if (snapshot?.SettlementsByFaction != null)
                    snapshot.SettlementsByFaction.TryGetValue(f, out all);
                if (all == null || all.Count < 1) continue;

                var far = new List<Settlement>();
                int validCount = 0;
                for (int s = 0; s < all.Count; s++)
                {
                    Settlement set = all[s];
                    if (!DailyWorldSnapshot.IsSettlementStillValid(set)) continue;
                    if (WorldActions_Utils.IsSettlementProtected(set)) continue;
                    validCount++;
                    float d = MinDistanceToPlayer(set.Tile.tileId, manager);
                    if (d < minDist) continue;
                    far.Add(set);
                }

                // After packing CountMin..CountMax, at least HomelandReserve settlements must stay active.
                if (validCount < HomelandReserve + CountMin) continue;
                int maxTake = Mathf.Min(CountMax, far.Count, validCount - HomelandReserve);
                if (maxTake < CountMin) continue;

                far.Sort((a, b) => SourcePickWeight(b).CompareTo(SourcePickWeight(a)));
                var picked = new List<Settlement>();
                for (int s = 0; s < far.Count && picked.Count < maxTake; s++)
                    picked.Add(far[s]);
                // Prefer filling with T2/T3 — already sorted by weight
                if (picked.Count < CountMin) continue;

                float totalOff = 0f;
                for (int s = 0; s < picked.Count; s++)
                    totalOff += picked[s].GetComponent<CompViralSpread>()?.offensiveStrength ?? 0f;
                candidates.Add((f, picked, totalOff));
            }

            if (candidates.Count == 0)
            {
                WDVerbose.Msg("ForwardAssault pick abort reason=no-strong-far-foe");
                return false;
            }

            candidates.Sort((a, b) => b.str.CompareTo(a.str));
            faction = candidates[0].f;
            sources = candidates[0].sites;
            // Trim to CountMin..CountMax randomly preferring more when available
            int want = Mathf.Clamp(Rand.RangeInclusive(CountMin, CountMax), CountMin, sources.Count);
            if (sources.Count > want)
                sources = sources.GetRange(0, want);
            WDVerbose.Msg($"ForwardAssault pick faction={faction.Name} sources={sources.Count}");
            return true;
        }

        /// <summary>
        /// Debug forced sources: the clicked seed plus the faction's other sites by pick weight, up to the pack cap.
        /// Skips distance / homeland / min settlement-count gates used by <see cref="TryPickStrongHostileFaction"/>.
        /// </summary>
        public static List<Settlement> BuildDebugForcedSources(Faction faction, Settlement seed, DailyWorldSnapshot snapshot)
        {
            var picked = new List<Settlement> { seed };
            List<Settlement> all = null;
            snapshot?.SettlementsByFaction?.TryGetValue(faction, out all);
            if (all != null)
            {
                var rest = new List<Settlement>();
                for (int i = 0; i < all.Count; i++)
                {
                    Settlement s = all[i];
                    if (s == null || s == seed || !DailyWorldSnapshot.IsSettlementStillValid(s)) continue;
                    if (WorldActions_Utils.IsSettlementProtected(s)) continue;
                    rest.Add(s);
                }
                rest.Sort((a, b) => SourcePickWeight(b).CompareTo(SourcePickWeight(a)));
                for (int i = 0; i < rest.Count && picked.Count < CountMax; i++)
                    picked.Add(rest[i]);
            }
            return picked;
        }

        /// <summary>
        /// Dev pick: any hostile WD participant with ≥1 valid settlement. Skips top-% / distance / homeland / count gates.
        /// Prefers the strongest faction by summed settlement pick weight.
        /// </summary>
        public static bool TryPickAnyHostileFactionForDebug(
            DailyWorldSnapshot snapshot,
            out Faction faction,
            out List<Settlement> sources)
        {
            faction = null;
            sources = null;
            var byFaction = snapshot?.SettlementsByFaction;
            if (byFaction == null) return false;

            Faction bestF = null;
            List<Settlement> bestSources = null;
            float bestStr = float.MinValue;

            foreach (var kv in byFaction)
            {
                Faction f = kv.Key;
                if (f == null || f.IsPlayer || f.defeated || !WorldActions_Utils.IsWdParticipant(f)) continue;
                if (!WorldActions_Utils.SafeHostileTo(f, Faction.OfPlayer)) continue;

                Settlement seed = null;
                float seedW = float.MinValue;
                List<Settlement> all = kv.Value;
                if (all == null) continue;
                for (int i = 0; i < all.Count; i++)
                {
                    Settlement s = all[i];
                    if (!DailyWorldSnapshot.IsSettlementStillValid(s)) continue;
                    if (WorldActions_Utils.IsSettlementProtected(s)) continue;
                    float w = SourcePickWeight(s);
                    if (seed == null || w > seedW)
                    {
                        seed = s;
                        seedW = w;
                    }
                }
                if (seed == null) continue;

                List<Settlement> picked = BuildDebugForcedSources(f, seed, snapshot);
                if (picked == null || picked.Count < 1) continue;

                float total = 0f;
                for (int i = 0; i < picked.Count; i++)
                    total += picked[i].GetComponent<CompViralSpread>()?.offensiveStrength ?? 0f;
                if (bestF == null || total > bestStr)
                {
                    bestF = f;
                    bestSources = picked;
                    bestStr = total;
                }
            }

            if (bestF == null || bestSources == null) return false;
            faction = bestF;
            sources = bestSources;
            WDVerbose.Msg($"ForwardAssault debug-pick faction={faction.Name} sources={sources.Count}");
            return true;
        }

        /// <summary>Source preference for pack-up: tier bias × offensive strength (matches caravan launch).</summary>
        public static float SourcePickWeight(Settlement s)
        {
            var c = s?.GetComponent<CompViralSpread>();
            if (c == null) return 0f;
            float tierW = c.tier switch
            {
                SettlementTier.T2 => 4f,
                SettlementTier.T3 => 4f,
                SettlementTier.T4 => 1.5f,
                _ => 1f,
            };
            return tierW * (10f + c.offensiveStrength);
        }

        public static float MinDistanceToPlayer(int tileId, WorldComponent_SpreadManager manager)
        {
            float best = float.MaxValue;
            var settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s?.Faction?.IsPlayer != true) continue;
                float d = WorldActions_Utils.GetDistance(tileId, s.Tile.tileId, manager);
                if (d < best) best = d;
            }
            return best;
        }

        public static Settlement FindNearestPlayerSettlement(int fromTile, WorldComponent_SpreadManager manager)
        {
            Settlement best = null;
            float bestDist = float.MaxValue;
            var settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s?.Faction?.IsPlayer != true) continue;
                float d = fromTile < 0 ? 0f : WorldActions_Utils.GetDistance(fromTile, s.Tile.tileId, manager);
                if (best == null || d < bestDist)
                {
                    best = s;
                    bestDist = d;
                }
            }
            return best;
        }

        public static List<WorldObject> CollectDistinctPlayerTargets()
        {
            var list = new List<WorldObject>();
            var settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s?.Faction?.IsPlayer == true && s.HasMap)
                    list.Add(s);
            }
            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            if (outposts != null)
            {
                for (int i = 0; i < outposts.Count; i++)
                {
                    WorldObject_WD_Outpost o = outposts[i];
                    if (o != null && !o.Destroyed)
                        list.Add(o);
                }
            }
            return list;
        }

        public static WorldObject PickNearestPlayerTarget(int fromTile, IList<WorldObject> targets)
        {
            if (targets == null || targets.Count < 1) return null;
            WorldObject best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < targets.Count; i++)
            {
                WorldObject t = targets[i];
                if (t == null || t.Destroyed) continue;
                float dist = fromTile < 0
                    ? 0f
                    : Find.WorldGrid.ApproxDistanceInTiles(fromTile, t.Tile.tileId);
                if (best == null || dist < bestDist)
                {
                    best = t;
                    bestDist = dist;
                }
            }
            return best;
        }

        /// <summary>
        /// Greedy unique 1:1 assign: prefer gate-pass then shorter distance. Each source and target used at most once.
        /// </summary>
        public static List<(Settlement src, WorldObject tgt, float dist, bool ratioOk)> AssignSourcesToTargetsGreedy(
            IList<Settlement> sources,
            IList<WorldObject> targets,
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth,
            int maxAssignments = -1)
        {
            var result = new List<(Settlement src, WorldObject tgt, float dist, bool ratioOk)>();
            if (sources == null || targets == null || sources.Count < 1 || targets.Count < 1)
                return result;

            int cap = maxAssignments < 0
                ? Mathf.Min(sources.Count, targets.Count, CountMax)
                : Mathf.Min(maxAssignments, sources.Count, targets.Count, CountMax);
            if (cap < 1) return result;

            var lookup = WorldActions_Utils.GetWorldObjectsWithCompByFaction();
            var pairs = new List<(Settlement src, WorldObject tgt, float dist, bool ratioOk)>();
            for (int s = 0; s < sources.Count; s++)
            {
                Settlement src = sources[s];
                if (src == null || src.Destroyed) continue;
                for (int t = 0; t < targets.Count; t++)
                {
                    WorldObject tgt = targets[t];
                    if (tgt == null || tgt.Destroyed) continue;
                    float dist = Find.WorldGrid.ApproxDistanceInTiles(src.Tile.tileId, tgt.Tile.tileId);
                    var kind = RaidLaunchGate.ClassifyTarget(tgt);
                    var gate = RaidLaunchGate.Evaluate(src, tgt, kind, null, lookup, manager, seth);
                    pairs.Add((src, tgt, dist, gate.passed));
                }
            }

            pairs.Sort((a, b) =>
            {
                int c = b.ratioOk.CompareTo(a.ratioOk);
                if (c != 0) return c;
                return a.dist.CompareTo(b.dist);
            });

            var usedSrc = new HashSet<int>();
            var usedTgt = new HashSet<int>();
            for (int i = 0; i < pairs.Count && result.Count < cap; i++)
            {
                var p = pairs[i];
                if (usedSrc.Contains(p.src.ID) || usedTgt.Contains(p.tgt.ID)) continue;
                usedSrc.Add(p.src.ID);
                usedTgt.Add(p.tgt.ID);
                result.Add(p);
            }
            return result;
        }

        /// <summary>
        /// Assign every host to a player target. Prefer unique 1:1 when targets exist; leftover hosts
        /// reuse the nearest target (Invasion multi-raid when hosts outnumber holdings).
        /// </summary>
        public static List<(Settlement src, WorldObject tgt, float dist, bool ratioOk)> AssignEveryHostToPlayerTargets(
            IList<Settlement> sources,
            IList<WorldObject> targets,
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth)
        {
            var result = new List<(Settlement src, WorldObject tgt, float dist, bool ratioOk)>();
            if (sources == null || targets == null || sources.Count < 1 || targets.Count < 1)
                return result;

            var unique = AssignSourcesToTargetsGreedy(sources, targets, manager, seth, maxAssignments: CountMax);
            var usedSrc = new HashSet<int>();
            for (int i = 0; i < unique.Count; i++)
            {
                result.Add(unique[i]);
                usedSrc.Add(unique[i].src.ID);
            }

            var lookup = WorldActions_Utils.GetWorldObjectsWithCompByFaction();
            for (int s = 0; s < sources.Count; s++)
            {
                Settlement src = sources[s];
                if (src == null || src.Destroyed || usedSrc.Contains(src.ID)) continue;
                WorldObject tgt = PickNearestPlayerTarget(src.Tile.tileId, targets);
                if (tgt == null || tgt.Destroyed) continue;
                float dist = Find.WorldGrid.ApproxDistanceInTiles(src.Tile.tileId, tgt.Tile.tileId);
                var kind = RaidLaunchGate.ClassifyTarget(tgt);
                var gate = RaidLaunchGate.Evaluate(src, tgt, kind, null, lookup, manager, seth);
                result.Add((src, tgt, dist, gate.passed));
                usedSrc.Add(src.ID);
            }
            return result;
        }

        /// <summary>
        /// Destroy one home and spawn a MassRelocation traveler on that tile.
        /// <paramref name="massRelocationDestTile"/> is always the dig-in; <paramref name="pathDestTile"/> may be a local rally.
        /// </summary>
        public static WorldObject_Traveler LaunchMassRelocationTraveler(
            Settlement source,
            int massRelocationDestTile,
            WorldComponent_SpreadManager manager,
            int groupId = 0,
            int pathDestTile = -1)
        {
            if (source == null || source.Destroyed || massRelocationDestTile < 0) return null;
            var comp = source.GetComponent<CompViralSpread>();
            if (comp == null) return null;

            Faction faction = source.Faction;
            int originTile = source.Tile.tileId;
            string label = source.LabelCap;
            SettlementTier tier = comp.tier;
            float off = Mathf.Max(0f, comp.offensiveStrength);
            float def = Mathf.Max(0f, comp.defensiveStrength);

            SuppressLossNotifyBeforePack(source);
            source.Destroy();
            Outpost_EstablishmentRequirements.InvalidateNearbyCountCache();
            if (faction == null || originTile < 0) return null;

            int pathTo = pathDestTile >= 0 ? pathDestTile : massRelocationDestTile;

            WorldObject_Traveler traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(
                DefDatabase<WorldObjectDef>.GetNamed("TSA_WD_Traveler_MassRelocation"));
            traveler.Tile = originTile;
            traveler.SetFaction(faction);
            traveler.mission = TravelerMission.MassRelocation;
            traveler.travelerStrength = Mathf.Max(10f, off);
            traveler.initialStrength = traveler.travelerStrength;
            traveler.projectedArrivalStrength = traveler.travelerStrength;
            traveler.packUpRequiresRefound = true;
            traveler.massRelocationDestTile = massRelocationDestTile;
            traveler.massRelocationTier = tier;
            traveler.massRelocationDefensiveStrength = def;
            traveler.vanguardGroupId = groupId;
            traveler.originObject = null;
            traveler.packUpOriginLabel = label;

            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(pathTo, traveler));

            manager?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_ForwardAssault_PackUpVanguard".Translate(label, massRelocationDestTile),
                traveler, null));
            WDVerbose.Msg(
                $"ForwardAssault MassRelocation pack origin={originTile} path={pathTo} digIn={massRelocationDestTile} tier={tier} str={traveler.travelerStrength:F0} group={groupId}");
            return traveler;
        }

        /// <summary>
        /// Pack an AssaultRally geographic assembly: solo marches to dig-in; multi-source rallies locally then marches.
        /// </summary>
        public static List<WorldObject_Traveler> LaunchVanguardAssembly(
            IList<Settlement> assembly,
            int digInTile,
            int groupId,
            WorldComponent_SpreadManager manager)
        {
            var launched = new List<WorldObject_Traveler>();
            if (assembly == null || digInTile < 0) return launched;

            var live = new List<Settlement>();
            for (int i = 0; i < assembly.Count; i++)
            {
                Settlement s = assembly[i];
                if (s != null && !s.Destroyed)
                    live.Add(s);
            }
            if (live.Count < 1) return launched;

            if (live.Count == 1)
            {
                WorldObject_Traveler solo = LaunchMassRelocationTraveler(live[0], digInTile, manager, groupId);
                if (solo != null)
                    launched.Add(solo);
                return launched;
            }

            int centroid = WorldActions_AssaultRally.ComputeCentroidTile(live);
            int rallyTile = WorldActions_AssaultRally.PickRallyTile(live, centroid);
            if (rallyTile < 0)
            {
                WDVerbose.Msg("ForwardAssault Vanguard assembly abort reason=no-rally-tile — solo dig-in");
                for (int i = 0; i < live.Count; i++)
                {
                    WorldObject_Traveler t = LaunchMassRelocationTraveler(live[i], digInTile, manager, groupId);
                    if (t != null)
                        launched.Add(t);
                }
                return launched;
            }

            var rallyMembers = new List<WorldObject_Traveler>();
            for (int i = 0; i < live.Count; i++)
            {
                Settlement src = live[i];
                if (WorldActions_AssaultRally.CanReachRallyInCap(src.Tile.tileId, rallyTile))
                {
                    WorldObject_Traveler t = LaunchMassRelocationTraveler(
                        src, digInTile, manager, groupId, pathDestTile: rallyTile);
                    if (t == null) continue;
                    launched.Add(t);
                    rallyMembers.Add(t);
                }
                else
                {
                    WorldObject_Traveler t = LaunchMassRelocationTraveler(src, digInTile, manager, groupId);
                    if (t != null)
                        launched.Add(t);
                }
            }

            if (rallyMembers.Count >= 2)
            {
                int expected = rallyMembers.Count;
                for (int i = 0; i < rallyMembers.Count; i++)
                    rallyMembers[i].desperationExpectedCount = expected;
            }
            else if (rallyMembers.Count == 1)
            {
                // Alone at rally — skip wait and march straight to dig-in.
                WorldObject_Traveler alone = rallyMembers[0];
                alone.desperationExpectedCount = 0;
                alone.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(digInTile, alone));
            }

            return launched;
        }

        /// <summary>True while a multi-source Vanguard column is still in local rally (not yet marching to dig-in).</summary>
        public static bool IsVanguardRallyPhase(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return false;
            if (traveler.mission != TravelerMission.MassRelocation) return false;
            if (traveler.vanguardGroupId <= 0) return false;
            if (traveler.desperationExpectedCount <= 1) return false;
            return true;
        }

        public static void ExecuteVanguardRallyArrival(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return;
            if (!IsVanguardRallyPhase(traveler)) return;

            WorldObject_Traveler host = FindVanguardRallyHost(traveler.vanguardGroupId, exclude: traveler);
            if (host == null)
            {
                traveler.desperationIsHost = true;
                traveler.desperationArrivedCount = 1;
                traveler.desperationWaitUntilTick = Find.TickManager.TicksGame
                    + CompViralSpread.CooldownTicksFromDays(WorldActions_AssaultRally.RallyWaitDays);
                traveler.pather?.StopDead();
                WDVerbose.Msg(
                    $"ForwardAssault Vanguard rally host wait group={traveler.vanguardGroupId} expected={traveler.desperationExpectedCount} until={traveler.desperationWaitUntilTick}");
                AbsorbSameTileVanguardOrphans(traveler);
                TryFinalizeVanguardRallyIfReady(traveler);
                return;
            }

            AbsorbMassRelocationIntoHost(host, traveler);
            AbsorbSameTileVanguardOrphans(host);
            TryFinalizeVanguardRallyIfReady(host);
        }

        public static void TickVanguardRallyHost(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return;
            if (traveler.mission != TravelerMission.MassRelocation) return;
            if (!traveler.desperationIsHost || traveler.vanguardGroupId <= 0) return;
            if (traveler.pather != null && traveler.pather.moving) return;

            AbsorbSameTileVanguardOrphans(traveler);
            TryFinalizeVanguardRallyIfReady(traveler);
        }

        private static void AbsorbSameTileVanguardOrphans(WorldObject_Traveler host)
        {
            if (host == null || host.Destroyed || !host.desperationIsHost) return;
            if (host.vanguardGroupId <= 0) return;

            int tileId = host.Tile.tileId;
            var objs = Find.WorldObjects.AllWorldObjects;
            for (int i = objs.Count - 1; i >= 0; i--)
            {
                if (objs[i] is not WorldObject_Traveler t || t.Destroyed || t == host) continue;
                if (t.vanguardGroupId != host.vanguardGroupId) continue;
                if (t.mission != TravelerMission.MassRelocation) continue;
                if (t.desperationIsHost) continue;
                if (t.Tile.tileId != tileId) continue;
                AbsorbMassRelocationIntoHost(host, t);
            }
        }

        private static void AbsorbMassRelocationIntoHost(WorldObject_Traveler host, WorldObject_Traveler joiner)
        {
            if (host == null || joiner == null || host.Destroyed || joiner.Destroyed) return;

            host.travelerStrength = Mathf.Max(10f, host.travelerStrength + Mathf.Max(0f, joiner.travelerStrength));
            host.initialStrength += Mathf.Max(0f, joiner.initialStrength);
            host.projectedArrivalStrength = host.travelerStrength;
            host.massRelocationDefensiveStrength =
                Mathf.Max(0f, host.massRelocationDefensiveStrength) + Mathf.Max(0f, joiner.massRelocationDefensiveStrength);
            if (joiner.massRelocationTier > host.massRelocationTier)
                host.massRelocationTier = joiner.massRelocationTier;
            if (host.massRelocationDestTile < 0 && joiner.massRelocationDestTile >= 0)
                host.massRelocationDestTile = joiner.massRelocationDestTile;
            host.desperationArrivedCount = Mathf.Max(1, host.desperationArrivedCount) + 1;

            joiner.suppressDestroyedWorldFx = true;
            joiner.Destroy();
            WDVerbose.Msg(
                $"ForwardAssault Vanguard rally absorb group={host.vanguardGroupId} arrived={host.desperationArrivedCount}/{host.desperationExpectedCount} str={host.travelerStrength:F0}");
        }

        private static void TryFinalizeVanguardRallyIfReady(WorldObject_Traveler host)
        {
            if (host == null || host.Destroyed || !host.desperationIsHost) return;
            if (host.mission != TravelerMission.MassRelocation) return;
            if (host.vanguardGroupId <= 0) return;

            int now = Find.TickManager.TicksGame;
            bool allIn = host.desperationExpectedCount > 0
                && host.desperationArrivedCount >= host.desperationExpectedCount;
            bool timedOut = host.desperationWaitUntilTick > 0 && now >= host.desperationWaitUntilTick;
            if (!allIn && !timedOut) return;

            FinalizeVanguardRallyHost(host);
        }

        private static void FinalizeVanguardRallyHost(WorldObject_Traveler host)
        {
            int digIn = host.massRelocationDestTile;
            if (digIn < 0)
            {
                WDVerbose.Msg($"ForwardAssault Vanguard rally abort no-digIn group={host.vanguardGroupId}");
                TryRefoundFromTraveler(host);
                host.suppressDestroyedWorldFx = true;
                host.Destroy();
                return;
            }

            host.desperationIsHost = false;
            host.desperationExpectedCount = 0;
            host.desperationArrivedCount = 0;
            host.desperationWaitUntilTick = -1;
            host.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(digIn, host));

            WDVerbose.Msg(
                $"ForwardAssault Vanguard rally → digIn group={host.vanguardGroupId} dest={digIn} str={host.travelerStrength:F0}");

            PromoteVanguardRallyStragglers(host, digIn);
        }

        private static void PromoteVanguardRallyStragglers(WorldObject_Traveler host, int digIn)
        {
            if (host == null || host.vanguardGroupId <= 0 || digIn < 0) return;
            int groupId = host.vanguardGroupId;
            var objs = Find.WorldObjects.AllWorldObjects;
            for (int i = objs.Count - 1; i >= 0; i--)
            {
                if (objs[i] is not WorldObject_Traveler t || t.Destroyed || t == host) continue;
                if (t.vanguardGroupId != groupId) continue;
                if (t.mission != TravelerMission.MassRelocation) continue;

                t.desperationIsHost = false;
                t.desperationExpectedCount = 0;
                t.desperationArrivedCount = 0;
                t.desperationWaitUntilTick = -1;
                t.massRelocationDestTile = digIn;
                t.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(digIn, t));
                WDVerbose.Msg($"ForwardAssault Vanguard straggler→digIn group={groupId} label={t.Label}");
            }
        }

        private static WorldObject_Traveler FindVanguardRallyHost(int groupId, WorldObject_Traveler exclude)
        {
            if (groupId <= 0) return null;
            var objs = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < objs.Count; i++)
            {
                if (objs[i] is not WorldObject_Traveler t || t.Destroyed) continue;
                if (t == exclude) continue;
                if (t.vanguardGroupId != groupId) continue;
                if (t.mission != TravelerMission.MassRelocation) continue;
                if (!t.desperationIsHost) continue;
                return t;
            }
            return null;
        }

        /// <summary>Found a settlement from a pack-up traveler. Pass <paramref name="subType"/> null/empty for neutral remounts (e.g. Turtle).</summary>
        public static Settlement SpawnVanguardSettlement(WorldObject_Traveler traveler, int tileId, string subType = "Vanguard")
        {
            if (traveler == null || tileId < 0) return null;
            if (Find.WorldObjects.AnySettlementAt(tileId)
                || Outpost_EstablishmentRequirements.TileHasActiveCamp(tileId)
                || !TileFinder.IsValidTileForNewSettlement(tileId))
                return null;

            Settlement newS = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            newS.Tile = tileId;
            newS.SetFaction(traveler.Faction);
            newS.Name = SettlementNameGenerator.GenerateSettlementName(newS);
            Find.WorldObjects.Add(newS);
            Outpost_EstablishmentRequirements.InvalidateNearbyCountCache();

            var comp = newS.GetComponent<CompViralSpread>();
            if (comp != null)
            {
                SettlementTier tier = traveler.massRelocationTier;
                if (tier < SettlementTier.T1 || tier > SettlementTier.T4)
                    tier = SettlementTier.T1;
                comp.SetState(tier);
                if (!subType.NullOrEmpty())
                    comp.subType = subType;
                comp.offensiveStrength = Mathf.Max(10f, traveler.travelerStrength);
                if (traveler.massRelocationDefensiveStrength > 0f)
                    comp.defensiveStrength = traveler.massRelocationDefensiveStrength;
                else
                    comp.defensiveStrength = comp.GetBaseDefensiveStrength();
            }

            return newS;
        }

        /// <summary>Absorb traveler strength into an existing same-faction Vanguard settlement at/near tile.</summary>
        public static bool TryAbsorbIntoNearbyVanguardSettlement(WorldObject_Traveler traveler, int tileId)
        {
            if (traveler == null || traveler.Destroyed || tileId < 0 || traveler.Faction == null) return false;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return false;

            Settlement best = null;
            float bestDist = float.MaxValue;
            var settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s == null || s.Destroyed || s.Faction != traveler.Faction) continue;
                var comp = s.GetComponent<CompViralSpread>();
                if (comp == null || comp.subType != "Vanguard") continue;
                float d = grid.ApproxDistanceInTiles(tileId, s.Tile.tileId);
                if (d > 1.01f) continue;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = s;
                }
            }

            if (best == null) return false;
            var host = best.GetComponent<CompViralSpread>();
            if (host == null) return false;

            host.offensiveStrength = Mathf.Max(10f, host.offensiveStrength + Mathf.Max(0f, traveler.travelerStrength));
            if (traveler.massRelocationDefensiveStrength > 0f)
                host.defensiveStrength = Mathf.Max(0f, host.defensiveStrength) + traveler.massRelocationDefensiveStrength;
            if (traveler.massRelocationTier > host.tier)
                host.SetState(traveler.massRelocationTier);

            WDVerbose.Msg(
                $"ForwardAssault Vanguard absorb into={best.Label} +str={traveler.travelerStrength:F0} total={host.offensiveStrength:F0}");
            return true;
        }

        /// <summary>Required when pack-up destroyed the home — remount strength as a settlement near the traveler.</summary>
        public static bool TryRefoundFromTraveler(WorldObject_Traveler traveler, string subType = "Vanguard")
        {
            if (traveler == null || traveler.Destroyed || traveler.travelerStrength <= 0f) return false;
            if (traveler.Faction == null) return false;

            int seed = traveler.Tile.tileId;
            if (seed < 0 && traveler.massRelocationDestTile >= 0)
                seed = traveler.massRelocationDestTile;

            int tile = WdSettlementClusterUtility.FindNearestValidSettlementTile(seed, traveler.Faction);
            if (tile < 0)
            {
                WDVerbose.Msg($"PackUp refound FAIL traveler={traveler.Label} no-tile");
                return false;
            }

            Settlement s = SpawnVanguardSettlement(traveler, tile, subType);
            if (s == null)
            {
                WDVerbose.Msg($"PackUp refound FAIL traveler={traveler.Label} spawn");
                return false;
            }

            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();
            manager?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_ForwardAssault_Refound".Translate(traveler.Label, s.LabelCap),
                traveler, s));
            WDVerbose.Msg($"PackUp refound ok traveler={traveler.Label} settlement={s.Label} tile={tile} subType={subType ?? "none"}");
            return true;
        }

        public static void ExecuteMassRelocationArrival(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return;

            // Multi-source columns arrive at local rally first; dig-in founding only after finalize.
            if (IsVanguardRallyPhase(traveler))
            {
                ExecuteVanguardRallyArrival(traveler);
                return;
            }

            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();
            int dest = traveler.Tile.tileId;

            WorldObject blocker = FindBlockerAt(dest);
            if (blocker != null)
            {
                if (TryAbsorbIntoNearbyVanguardSettlement(traveler, dest))
                {
                    manager?.AddLog(new SpreadLogEntry(
                        "TSA_WD_Log_ForwardAssault_VanguardAbsorbed".Translate(traveler.Label),
                        traveler, blocker));
                    traveler.Destroy();
                    return;
                }

                if (TargetOfOpportunityUtility.TryConvertPackUpToRaid(traveler, blocker))
                {
                    manager?.AddLog(new SpreadLogEntry(
                        "TSA_WD_Log_ForwardAssault_BlockedRaid".Translate(traveler.Label, blocker.Label),
                        traveler, blocker));
                    WDVerbose.Msg($"ForwardAssault MassRelocation blocked→raid tgt={blocker.Label}");
                    if (traveler.Tile.tileId == blocker.Tile.tileId
                        && !traveler.Destroyed
                        && traveler.mission == TravelerMission.Raid)
                    {
                        var raidManager = Find.World.GetComponent<WorldComponent_SpreadManager>();
                        bool marauding = Raid_Simulated.ExecuteTravelerRaid(traveler, raidManager);
                        if (!marauding && traveler != null && !traveler.Destroyed)
                            traveler.Destroy();
                    }
                    return;
                }

                int redirect = WdSettlementClusterUtility.FindNearestValidSettlementTile(dest, traveler.Faction);
                if (redirect >= 0 && redirect != dest)
                {
                    traveler.massRelocationDestTile = redirect;
                    traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(redirect, traveler));
                    WDVerbose.Msg($"ForwardAssault MassRelocation redirect dest={redirect}");
                    return;
                }

                TryRefoundFromTraveler(traveler);
                traveler.Destroy();
                return;
            }

            if (!TileFinder.IsValidTileForNewSettlement(dest)
                || !WdSettlementClusterUtility.CanFoundVanguardAt(dest, traveler.Faction))
            {
                if (TryAbsorbIntoNearbyVanguardSettlement(traveler, dest))
                {
                    manager?.AddLog(new SpreadLogEntry(
                        "TSA_WD_Log_ForwardAssault_VanguardAbsorbed".Translate(traveler.Label),
                        traveler, null));
                    traveler.Destroy();
                    return;
                }

                WDVerbose.Msg(
                    $"ForwardAssault MassRelocation dest={dest} not founded (invalid or blocker pad) — redirect/refound");
                int redirect = WdSettlementClusterUtility.FindNearestValidSettlementTile(dest, traveler.Faction);
                if (redirect >= 0)
                {
                    if (TryAbsorbIntoNearbyVanguardSettlement(traveler, redirect))
                    {
                        manager?.AddLog(new SpreadLogEntry(
                            "TSA_WD_Log_ForwardAssault_VanguardAbsorbed".Translate(traveler.Label),
                            traveler, null));
                        traveler.Destroy();
                        return;
                    }

                    Settlement relocated = SpawnVanguardSettlement(traveler, redirect);
                    if (relocated != null)
                    {
                        manager?.AddLog(new SpreadLogEntry(
                            "TSA_WD_Log_ForwardAssault_VanguardFounded".Translate(relocated.LabelCap),
                            traveler, relocated));
                        traveler.Destroy();
                        return;
                    }
                }
                TryRefoundFromTraveler(traveler);
                traveler.Destroy();
                return;
            }

            if (TryAbsorbIntoNearbyVanguardSettlement(traveler, dest))
            {
                manager?.AddLog(new SpreadLogEntry(
                    "TSA_WD_Log_ForwardAssault_VanguardAbsorbed".Translate(traveler.Label),
                    traveler, null));
                traveler.Destroy();
                return;
            }

            Settlement founded = SpawnVanguardSettlement(traveler, dest);
            if (founded != null)
            {
                manager?.AddLog(new SpreadLogEntry(
                    "TSA_WD_Log_ForwardAssault_VanguardFounded".Translate(founded.LabelCap),
                    traveler, founded));
                WDVerbose.Msg($"ForwardAssault Vanguard founded {founded.Label} tile={dest}");
            }
            else
            {
                TryRefoundFromTraveler(traveler);
            }
            traveler.Destroy();
        }

        private static void SuppressLossNotifyBeforePack(Settlement source)
        {
            WorldActions_DesperationRaid.SuppressLossNotify(source);
        }

        private static WorldObject FindBlockerAt(int tileId)
        {
            if (tileId < 0) return null;
            foreach (WorldObject wo in Find.WorldObjects.ObjectsAt(tileId))
            {
                if (wo == null || wo.Destroyed) continue;
                if (wo is Settlement || wo is WorldObject_WD_Outpost)
                    return wo;
            }
            return null;
        }
    }
}
