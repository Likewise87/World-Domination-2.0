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

        public static WorldObject_Traveler LaunchMassRelocationTraveler(Settlement source, int destTile, WorldComponent_SpreadManager manager)
        {
            if (source == null || source.Destroyed) return null;
            var comp = source.GetComponent<CompViralSpread>();
            if (comp == null) return null;

            SettlementTier tier = comp.tier;
            float off = comp.offensiveStrength;
            float def = comp.defensiveStrength;
            Faction faction = source.Faction;
            int originTile = source.Tile.tileId;
            string label = source.LabelCap;

            SuppressLossNotifyBeforePack(source);
            source.Destroy();
            Outpost_EstablishmentRequirements.InvalidateNearbyCountCache();

            WorldObject_Traveler traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(
                DefDatabase<WorldObjectDef>.GetNamed("TSA_WD_Traveler_MassRelocation"));
            traveler.Tile = originTile;
            traveler.SetFaction(faction);
            traveler.mission = TravelerMission.MassRelocation;
            traveler.travelerStrength = Mathf.Max(10f, off);
            traveler.initialStrength = traveler.travelerStrength;
            traveler.projectedArrivalStrength = traveler.travelerStrength;
            traveler.packUpRequiresRefound = true;
            traveler.massRelocationDestTile = destTile;
            traveler.massRelocationTier = tier;
            traveler.massRelocationDefensiveStrength = def;
            traveler.originObject = null;
            traveler.packUpOriginLabel = label;

            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(destTile, traveler));

            manager?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_ForwardAssault_PackUpVanguard".Translate(label, destTile),
                traveler, null));
            WDVerbose.Msg($"ForwardAssault MassRelocation launch from={label} dest={destTile} tier={tier} str={traveler.travelerStrength:F0}");
            return traveler;
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
            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();
            int dest = traveler.Tile.tileId;

            WorldObject blocker = FindBlockerAt(dest);
            if (blocker != null)
            {
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
                WDVerbose.Msg(
                    $"ForwardAssault MassRelocation dest={dest} not founded (invalid or blocker pad) — redirect/refound");
                int redirect = WdSettlementClusterUtility.FindNearestValidSettlementTile(dest, traveler.Faction);
                if (redirect >= 0)
                {
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
