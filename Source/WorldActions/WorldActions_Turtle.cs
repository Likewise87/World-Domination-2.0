using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Scribed state for one open turtle-consolidate group (leaves depositing into one or more hubs that then fortify).</summary>
    public class TurtleGroupState : IExposable
    {
        public int hubId = -1;
        public int factionLoadId = -1;
        public int expected;
        public int arrived;
        public int deadlineTick;
        public bool fortified;
        /// <summary>Settlement IDs that received at least one deposit (primary + dig-in overflow hubs).</summary>
        public List<int> depositHubIds = new List<int>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref hubId, "hubId", -1);
            Scribe_Values.Look(ref factionLoadId, "factionLoadId", -1);
            Scribe_Values.Look(ref expected, "expected", 0);
            Scribe_Values.Look(ref arrived, "arrived", 0);
            Scribe_Values.Look(ref deadlineTick, "deadlineTick", 0);
            Scribe_Values.Look(ref fortified, "fortified", false);
            Scribe_Collections.Look(ref depositHubIds, "depositHubIds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && depositHubIds == null)
                depositHubIds = new List<int>();
        }
    }

    /// <summary>
    /// Proactive turtle consolidate: threatened cluster packs outer leaves (T1/T2, offense ≥ <see cref="MinTurtleLeafOffense"/>)
    /// into existing dig-in hubs (primary, in-cluster T3/T4, and up to <see cref="MaxReservedT2Vessels"/> strong T2 vessels).
    /// Prefers filling to T3 cap so consolidate spreads toward multiple T3s (ally-radius reinforcements); rare last-resort
    /// overflow may use T4-cap room on an existing hub. T3/T4 do not pack as leaves. No turtle founding.
    /// Own per-faction cooldown. At most one cluster per day.
    /// Cluster membership: connected component among a faction's sites with edge ≤ <see cref="ClusterEdgeTiles"/>.
    /// </summary>
    public static class WorldActions_Turtle
    {
        public const int ClusterEdgeTiles = 20;
        /// <summary>
        /// Hostile offense for pressure counts settlements within this radius of any cluster site
        /// (tighter than membership edge so multi-site unions stay local).
        /// </summary>
        public const float ClusterThreatBandTiles = 15f;
        public const float TurtleWaitDays = 2f;
        /// <summary>Leaf/migrant offensive strength below this is not worth packing.</summary>
        public const float MinTurtleLeafOffense = 400f;
        /// <summary>Strongest in-cluster T2s reserved as dig-in vessels (excluded from the leaf pool).</summary>
        private const int MaxReservedT2Vessels = 2;
        /// <summary>Allow a leaf to dump into a nearly-full hub if waste is at most this fraction of the leaf (or <see cref="MaxAbsWaste"/>).</summary>
        private const float MaxWasteFraction = 0.2f;
        private const float MaxAbsWaste = 75f;

        private static readonly List<Settlement> tmpFactionSettlements = new List<Settlement>();
        private static readonly List<List<Settlement>> tmpClusters = new List<List<Settlement>>();
        private static readonly HashSet<int> tmpSeen = new HashSet<int>();
        private static readonly Queue<Settlement> tmpBfs = new Queue<Settlement>();
        private static readonly HashSet<Settlement> tmpExcludeDest = new HashSet<Settlement>();
        private static readonly List<Settlement> tmpDigIns = new List<Settlement>();
        private static readonly List<Settlement> tmpT2Candidates = new List<Settlement>();
        private static readonly List<Settlement> tmpDestinations = new List<Settlement>();
        private static readonly Dictionary<int, float> tmpPreferRoom = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> tmpT4Room = new Dictionary<int, float>();

        public static bool IsTurtleTraveler(WorldObject_Traveler t) =>
            t != null && t.mission == TravelerMission.TurtleConsolidate;

        /// <summary>Exposed for Strategy on Settlement Loss pressure / hub picks.</summary>
        public static Settlement PickPrimaryHubPublic(List<Settlement> cluster) => PickPrimaryHub(cluster, null);

        public static float SumClusterOffensePublic(List<Settlement> cluster) => SumClusterOffense(cluster);

        public static float SumHostileThreatToClusterPublic(List<Settlement> cluster, Faction faction) =>
            SumHostileThreatToCluster(cluster, faction);

        // ----------------------------------------------------------------------------------------
        // Reactive (Strategy on Settlement Loss → Turtle fork)
        // ----------------------------------------------------------------------------------------

        /// <summary>
        /// After a settlement loss: migrate sites outside the host's ally radius into the host, or fortify in place
        /// when every remaining site already covers the hub. Does not stamp Strategy CD (caller does).
        /// </summary>
        public static bool TryTriggerReactiveFromLoss(
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth,
            Faction faction,
            List<Settlement> cluster,
            bool forceDebug)
        {
            if (manager == null || seth == null || faction == null || cluster == null || cluster.Count < 1)
                return false;

            Settlement host = PickPrimaryHub(cluster, null);
            if (host == null || host.Destroyed) return false;

            float allyR = AllyRadiusUtil.GetEffective(host, seth, manager);
            var migrants = new List<Settlement>();
            int hostTile = host.Tile.tileId;
            for (int i = 0; i < cluster.Count; i++)
            {
                Settlement s = cluster[i];
                if (s == null || s.Destroyed || s == host) continue;
                float d = Find.WorldGrid.ApproxDistanceInTiles(hostTile, s.Tile.tileId);
                if (d > allyR)
                    migrants.Add(s);
            }

            if (migrants.Count < 1)
                return FortifyHostInPlace(manager, seth, faction, host, forceDebug);

            return LaunchReactive(manager, seth, faction, cluster, host, migrants, forceDebug);
        }

        private static bool FortifyHostInPlace(
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth,
            Faction faction,
            Settlement host,
            bool forceDebug)
        {
            if (host == null || host.Destroyed) return false;
            var comp = host.GetComponent<CompViralSpread>();
            SettlementTier kitTier = comp != null ? comp.tier : SettlementTier.T1;
            WorldActions_DesperationRaid.TryPlaceFortifyKit(host.Tile.tileId, faction, kitTier, host, null);

            manager.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_Turtle_Fortify".Translate(host.Label),
                host, null));
            WDVerbose.Msg($"Turtle reactive fortify-in-place hub={host.Label} tier={kitTier} faction={faction.Name}");

            if ((seth.notifyTurtle || forceDebug) && WD_NotifyProximity.IsWithinPlayerNotificationRadius(host.Tile))
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_Letter_Turtle_Label".Translate(faction.Name),
                    "TSA_WD_Letter_Turtle_Text".Translate(host.Label),
                    LetterDefOf.NeutralEvent,
                    new LookTargets(host));
            }

            return true;
        }

        private static bool LaunchReactive(
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth,
            Faction faction,
            List<Settlement> cluster,
            Settlement primaryHub,
            List<Settlement> migrants,
            bool forceDebug)
        {
            if (primaryHub == null || primaryHub.Destroyed) return false;

            List<(Settlement leaf, Settlement dest)> assignments = BuildReactiveMigrantAssignments(cluster, primaryHub, migrants);
            if (assignments.Count < 1)
                return FortifyHostInPlace(manager, seth, faction, primaryHub, forceDebug);

            int groupId = NextTurtleGroupId(manager);
            var group = new TurtleGroupState
            {
                hubId = primaryHub.ID,
                factionLoadId = faction.loadID,
                expected = 0,
                arrived = 0,
                deadlineTick = Find.TickManager.TicksGame + CompViralSpread.CooldownTicksFromDays(TurtleWaitDays),
                fortified = false,
                depositHubIds = new List<int>(),
            };
            manager.turtleGroups[groupId] = group;

            int expected = 0;
            var launched = new List<WorldObject_Traveler>();
            for (int i = 0; i < assignments.Count; i++)
            {
                WorldObject_Traveler t = LaunchTurtleLeaf(assignments[i].leaf, assignments[i].dest, groupId, manager);
                if (t == null) continue;
                expected++;
                launched.Add(t);
            }

            if (expected < 1)
            {
                manager.turtleGroups.Remove(groupId);
                return FortifyHostInPlace(manager, seth, faction, primaryHub, forceDebug);
            }

            group.expected = expected;
            for (int i = 0; i < launched.Count; i++)
                launched[i].turtleExpectedCount = expected;

            // Reactive Turtle does not stamp daily turtle CD — Strategy CD is stamped by the caller.

            manager.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_Turtle_PackUp".Translate(faction.Name, expected, primaryHub.Label),
                primaryHub, null));
            WDVerbose.Msg(
                $"Turtle reactive launched faction={faction.Name} leaves={expected} primary={primaryHub.Label} group={groupId}");

            if ((seth.notifyTurtle || forceDebug) && WD_NotifyProximity.IsWithinPlayerNotificationRadius(primaryHub.Tile))
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_Letter_Turtle_Label".Translate(faction.Name),
                    "TSA_WD_Letter_Turtle_Text".Translate(primaryHub.Label),
                    LetterDefOf.NeutralEvent,
                    new LookTargets(primaryHub));
            }

            return true;
        }

        /// <summary>
        /// Reactive: pack migrants outside ally radius (offense ≥ <see cref="MinTurtleLeafOffense"/>) into existing
        /// dig-in destinations (host, non-migrant T3/T4, reserved strong T2 vessels). Prefer T3 room; rare T4 overflow.
        /// No daily max-pack cap. Sites that cannot fit without bad waste are left alone.
        /// </summary>
        private static List<(Settlement leaf, Settlement dest)> BuildReactiveMigrantAssignments(
            List<Settlement> cluster, Settlement primaryHub, List<Settlement> migrants)
        {
            var result = new List<(Settlement leaf, Settlement dest)>();
            if (cluster == null || primaryHub == null || migrants == null || migrants.Count < 1)
                return result;

            tmpExcludeDest.Clear();
            for (int i = 0; i < migrants.Count; i++)
            {
                Settlement m = migrants[i];
                if (m != null) tmpExcludeDest.Add(m);
            }

            BuildDigInDestinationList(cluster, primaryHub, tmpExcludeDest, tmpDestinations);
            InitTurtleRoomMaps(tmpDestinations, tmpPreferRoom, tmpT4Room);

            int primaryTile = primaryHub.Tile.tileId;
            var leaves = new List<Settlement>();
            for (int i = 0; i < migrants.Count; i++)
            {
                Settlement leaf = migrants[i];
                if (leaf == null || leaf.Destroyed) continue;
                float off = leaf.GetComponent<CompViralSpread>()?.offensiveStrength ?? 0f;
                if (off < MinTurtleLeafOffense) continue;
                leaves.Add(leaf);
            }
            leaves.Sort((a, b) =>
                Find.WorldGrid.ApproxDistanceInTiles(primaryTile, a.Tile.tileId)
                    .CompareTo(Find.WorldGrid.ApproxDistanceInTiles(primaryTile, b.Tile.tileId)));

            AssignLeavesToDigIns(leaves, tmpDestinations, tmpPreferRoom, tmpT4Room, maxPack: int.MaxValue, result);
            return result;
        }

        // ----------------------------------------------------------------------------------------
        // Daily trigger
        // ----------------------------------------------------------------------------------------

        public static void TryTrigger(WorldComponent_SpreadManager manager, DailyWorldSnapshot snapshot)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || manager == null) return;

            // Always finalize timed-out / orphaned groups even when the feature gate is Never.
            PruneAndFinalizeGroups(manager);

            if (!WdEscalation.PassesGate(seth.gateThreatTurtle, manager))
            {
                WDVerbose.Msg($"Turtle skip reason=stage-gate gate={seth.gateThreatTurtle}");
                return;
            }

            if (Rand.Value > Mathf.Clamp01(seth.turtleChance))
            {
                WDVerbose.Msg("Turtle skip reason=chance");
                return;
            }

            try
            {
                EvaluateAndLaunchBestCluster(manager, snapshot, seth, forceDebug: false, seedFaction: null);
            }
            catch (Exception e)
            {
                Log.Error($"[TSA WD] Turtle TryTrigger failed: {e}");
            }
        }

        /// <summary>Evaluate every eligible faction's clusters and launch at most the single best-scoring one.</summary>
        private static bool EvaluateAndLaunchBestCluster(
            WorldComponent_SpreadManager manager,
            DailyWorldSnapshot snapshot,
            WorldDominationSettings seth,
            bool forceDebug,
            Faction seedFaction)
        {
            int minCluster = Mathf.Max(2, seth.turtleMinClusterSize);
            float minRatio = Mathf.Max(0f, seth.turtlePressureRatio);

            List<Settlement> bestCluster = null;
            Settlement bestHub = null;
            Faction bestFaction = null;
            float bestScore = float.MinValue;

            var byFaction = snapshot?.SettlementsByFaction;
            if (byFaction == null) return false;

            foreach (var kv in byFaction)
            {
                Faction faction = kv.Key;
                if (faction == null || faction.IsPlayer || faction.defeated) continue;
                if (seedFaction != null && faction != seedFaction) continue;
                if (!WorldActions_Utils.IsWdParticipant(faction)) continue;
                if (!forceDebug && IsFactionOnCooldown(manager, faction)) continue;
                if (!forceDebug && WorldActions_Revolt.IsFactionOnComebackCooldown(manager, faction)) continue;

                CollectFactionSettlements(faction, kv.Value, tmpFactionSettlements);
                if (tmpFactionSettlements.Count < minCluster) continue;

                BuildFactionClusters(tmpFactionSettlements, tmpClusters);
                for (int i = 0; i < tmpClusters.Count; i++)
                {
                    List<Settlement> cluster = tmpClusters[i];
                    if (cluster.Count < minCluster) continue;

                    Settlement hub = PickPrimaryHub(cluster, null);
                    if (hub == null) continue;

                    float ownStr = SumClusterOffense(cluster);
                    float hostileStr = SumHostileThreatToCluster(cluster, faction);
                    float ratio = hostileStr / Mathf.Max(1f, ownStr);
                    if (!forceDebug && ratio < minRatio) continue;

                    float score = ratio * 1000f + cluster.Count;
                    if (hub.GetComponent<CompViralSpread>()?.tier == SettlementTier.T4)
                        score += 500f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestCluster = new List<Settlement>(cluster);
                        bestHub = hub;
                        bestFaction = faction;
                    }
                }
            }

            if (bestCluster == null || bestHub == null || bestFaction == null)
            {
                WDVerbose.Msg("Turtle skip reason=no-threatened-cluster");
                return false;
            }

            return Launch(manager, seth, bestFaction, bestCluster, bestHub, forceDebug);
        }

        // ----------------------------------------------------------------------------------------
        // Launch
        // ----------------------------------------------------------------------------------------

        private static bool Launch(
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth,
            Faction faction,
            List<Settlement> cluster,
            Settlement primaryHub,
            bool forceDebug)
        {
            if (primaryHub == null || primaryHub.Destroyed) return false;

            List<(Settlement leaf, Settlement dest)> assignments = BuildLeafAssignments(cluster, primaryHub, seth);
            if (assignments.Count < 1)
            {
                WDVerbose.Msg($"Turtle abort reason=no-fit-leaves faction={faction.Name} hub={primaryHub.Label}");
                return false;
            }

            int groupId = NextTurtleGroupId(manager);
            var group = new TurtleGroupState
            {
                hubId = primaryHub.ID,
                factionLoadId = faction.loadID,
                expected = 0,
                arrived = 0,
                deadlineTick = Find.TickManager.TicksGame + CompViralSpread.CooldownTicksFromDays(TurtleWaitDays),
                fortified = false,
                depositHubIds = new List<int>(),
            };
            manager.turtleGroups[groupId] = group;

            int expected = 0;
            var launched = new List<WorldObject_Traveler>();
            for (int i = 0; i < assignments.Count; i++)
            {
                WorldObject_Traveler t = LaunchTurtleLeaf(assignments[i].leaf, assignments[i].dest, groupId, manager);
                if (t == null) continue;
                expected++;
                launched.Add(t);
            }

            if (expected < 1)
            {
                manager.turtleGroups.Remove(groupId);
                WDVerbose.Msg($"Turtle abort reason=zero-launched faction={faction.Name} hub={primaryHub.Label}");
                return false;
            }

            group.expected = expected;
            for (int i = 0; i < launched.Count; i++)
                launched[i].turtleExpectedCount = expected;

            StampFactionCooldown(manager, faction, seth);

            manager.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_Turtle_PackUp".Translate(faction.Name, expected, primaryHub.Label),
                primaryHub, null));
            WDVerbose.Msg(
                $"Turtle launched faction={faction.Name} leaves={expected} primary={primaryHub.Label} group={groupId} forceDebug={forceDebug}");

            if ((seth.notifyTurtle || forceDebug) && WD_NotifyProximity.IsWithinPlayerNotificationRadius(primaryHub.Tile))
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_Letter_Turtle_Label".Translate(faction.Name),
                    "TSA_WD_Letter_Turtle_Text".Translate(primaryHub.Label),
                    LetterDefOf.NeutralEvent,
                    new LookTargets(primaryHub));
            }

            return true;
        }

        /// <summary>
        /// Pack T1/T2 leaves (offense ≥ <see cref="MinTurtleLeafOffense"/>) into existing dig-ins: primary, T3/T4,
        /// and reserved strong T2 vessels. Prefer T3-cap room; last-resort T4-cap room on the same existing hubs.
        /// </summary>
        private static List<(Settlement leaf, Settlement dest)> BuildLeafAssignments(
            List<Settlement> cluster, Settlement primaryHub, WorldDominationSettings seth)
        {
            var result = new List<(Settlement leaf, Settlement dest)>();
            if (cluster == null || primaryHub == null) return result;

            BuildDigInDestinationList(cluster, primaryHub, excludeAsDest: null, tmpDestinations);
            InitTurtleRoomMaps(tmpDestinations, tmpPreferRoom, tmpT4Room);

            int primaryTile = primaryHub.Tile.tileId;
            var destSet = new HashSet<Settlement>(tmpDestinations);
            var leaves = new List<Settlement>();
            for (int i = 0; i < cluster.Count; i++)
            {
                Settlement s = cluster[i];
                if (s == null || s.Destroyed || s == primaryHub) continue;
                if (destSet.Contains(s)) continue; // dig-in vessels stay
                var c = s.GetComponent<CompViralSpread>();
                if (c == null) continue;
                // T3/T4 dig in — they do not pack up for turtle.
                if (c.tier >= SettlementTier.T3) continue;
                if (c.offensiveStrength < MinTurtleLeafOffense) continue;
                leaves.Add(s);
            }

            leaves.Sort((a, b) =>
                Find.WorldGrid.ApproxDistanceInTiles(primaryTile, a.Tile.tileId)
                    .CompareTo(Find.WorldGrid.ApproxDistanceInTiles(primaryTile, b.Tile.tileId)));

            int maxPack = Mathf.Max(1, seth.turtleMaxPackSettlements);
            AssignLeavesToDigIns(leaves, tmpDestinations, tmpPreferRoom, tmpT4Room, maxPack, result);
            return result;
        }

        /// <summary>
        /// Primary + in-cluster T3/T4 + up to <see cref="MaxReservedT2Vessels"/> strongest T2 vessels (not primary, not excluded).
        /// Non-primary dig-ins sorted nearest-first to the primary.
        /// </summary>
        private static void BuildDigInDestinationList(
            List<Settlement> cluster,
            Settlement primaryHub,
            HashSet<Settlement> excludeAsDest,
            List<Settlement> destinationsOut)
        {
            destinationsOut.Clear();
            if (primaryHub == null) return;
            destinationsOut.Add(primaryHub);

            tmpDigIns.Clear();
            tmpT2Candidates.Clear();
            for (int i = 0; i < cluster.Count; i++)
            {
                Settlement s = cluster[i];
                if (s == null || s.Destroyed || s == primaryHub) continue;
                if (excludeAsDest != null && excludeAsDest.Contains(s)) continue;
                var c = s.GetComponent<CompViralSpread>();
                if (c == null) continue;
                if (c.tier >= SettlementTier.T3)
                    tmpDigIns.Add(s);
                else if (c.tier == SettlementTier.T2)
                    tmpT2Candidates.Add(s);
            }

            tmpT2Candidates.Sort((a, b) =>
            {
                float oa = a.GetComponent<CompViralSpread>()?.offensiveStrength ?? 0f;
                float ob = b.GetComponent<CompViralSpread>()?.offensiveStrength ?? 0f;
                return ob.CompareTo(oa);
            });
            int reserved = 0;
            for (int i = 0; i < tmpT2Candidates.Count && reserved < MaxReservedT2Vessels; i++)
            {
                tmpDigIns.Add(tmpT2Candidates[i]);
                reserved++;
            }

            int primaryTile = primaryHub.Tile.tileId;
            tmpDigIns.Sort((a, b) =>
                Find.WorldGrid.ApproxDistanceInTiles(primaryTile, a.Tile.tileId)
                    .CompareTo(Find.WorldGrid.ApproxDistanceInTiles(primaryTile, b.Tile.tileId)));
            destinationsOut.AddRange(tmpDigIns);
        }

        private static void InitTurtleRoomMaps(
            List<Settlement> destinations,
            Dictionary<int, float> preferRoom,
            Dictionary<int, float> t4Room)
        {
            preferRoom.Clear();
            t4Room.Clear();
            for (int i = 0; i < destinations.Count; i++)
            {
                Settlement d = destinations[i];
                if (d == null) continue;
                var c = d.GetComponent<CompViralSpread>();
                preferRoom[d.ID] = OffensiveRoomPreferTurtle(c);
                t4Room[d.ID] = OffensiveRoomToT4Cap(c);
            }
        }

        private static void AssignLeavesToDigIns(
            List<Settlement> leaves,
            List<Settlement> destinations,
            Dictionary<int, float> preferRoom,
            Dictionary<int, float> t4Room,
            int maxPack,
            List<(Settlement leaf, Settlement dest)> result)
        {
            int packed = 0;
            for (int i = 0; i < leaves.Count && packed < maxPack; i++)
            {
                Settlement leaf = leaves[i];
                if (leaf == null || leaf.Destroyed) continue;
                float leafOff = Mathf.Max(10f, leaf.GetComponent<CompViralSpread>()?.offensiveStrength ?? 0f);

                Settlement dest = null;
                bool usedT4Overflow = false;
                for (int d = 0; d < destinations.Count; d++)
                {
                    Settlement cand = destinations[d];
                    if (!preferRoom.TryGetValue(cand.ID, out float room)) continue;
                    if (!FitsWithoutBadWaste(room, leafOff)) continue;
                    dest = cand;
                    break;
                }

                if (dest == null)
                {
                    for (int d = 0; d < destinations.Count; d++)
                    {
                        Settlement cand = destinations[d];
                        if (!t4Room.TryGetValue(cand.ID, out float room)) continue;
                        if (!FitsWithoutBadWaste(room, leafOff)) continue;
                        dest = cand;
                        usedT4Overflow = true;
                        break;
                    }
                }

                if (dest == null) continue;

                float roomUsed = usedT4Overflow ? t4Room[dest.ID] : preferRoom[dest.ID];
                float absorb = Mathf.Min(leafOff, roomUsed);
                ConsumeTurtleRoom(preferRoom, t4Room, dest.ID, absorb);
                result.Add((leaf, dest));
                packed++;
            }
        }

        private static void ConsumeTurtleRoom(
            Dictionary<int, float> preferRoom, Dictionary<int, float> t4Room, int destId, float absorb)
        {
            if (preferRoom.TryGetValue(destId, out float p))
                preferRoom[destId] = Mathf.Max(0f, p - absorb);
            if (t4Room.TryGetValue(destId, out float t))
                t4Room[destId] = Mathf.Max(0f, t - absorb);
        }

        /// <summary>Preferred packing room: T4 hubs fill to T4 cap; others fill to T3 cap (avoid routine promote-into-T4).</summary>
        private static float OffensiveRoomPreferTurtle(CompViralSpread c)
        {
            if (c == null) return 0f;
            SettlementTier capTier = c.tier == SettlementTier.T4 ? SettlementTier.T4 : SettlementTier.T3;
            float cap = CompViralSpread.GetStrengthRange(capTier).max;
            return Mathf.Max(0f, cap - Mathf.Max(0f, c.offensiveStrength));
        }

        private static float OffensiveRoomToT4Cap(CompViralSpread c)
        {
            if (c == null) return 0f;
            float cap = CompViralSpread.GetStrengthRange(SettlementTier.T4).max;
            return Mathf.Max(0f, cap - Mathf.Max(0f, c.offensiveStrength));
        }

        private static bool FitsWithoutBadWaste(float room, float leafOff)
        {
            if (leafOff <= 0f) return true;
            if (room >= leafOff) return true;
            if (room <= 0f) return false;
            float waste = leafOff - room;
            return waste <= Mathf.Max(MaxAbsWaste, leafOff * MaxWasteFraction);
        }

        private static WorldObject_Traveler LaunchTurtleLeaf(
            Settlement source, Settlement dest, int groupId, WorldComponent_SpreadManager manager)
        {
            if (source == null || source.Destroyed || dest == null || dest.Destroyed) return null;
            var comp = source.GetComponent<CompViralSpread>();
            if (comp == null) return null;

            SettlementTier tier = comp.tier;
            float off = comp.offensiveStrength;
            float def = comp.defensiveStrength;
            Faction faction = source.Faction;
            int originTile = source.Tile.tileId;
            string label = source.LabelCap;

            WorldActions_DesperationRaid.SuppressLossNotify(source);
            source.Destroy();
            Outpost_EstablishmentRequirements.InvalidateNearbyCountCache();

            WorldObject_Traveler traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(
                DefDatabase<WorldObjectDef>.GetNamed("TSA_WD_Traveler_Raid"));
            traveler.Tile = originTile;
            traveler.SetFaction(faction);
            traveler.mission = TravelerMission.TurtleConsolidate;
            traveler.travelerStrength = Mathf.Max(10f, off);
            traveler.initialStrength = traveler.travelerStrength;
            traveler.projectedArrivalStrength = traveler.travelerStrength;
            traveler.originObject = null;
            traveler.packUpOriginLabel = label;
            traveler.targetObject = dest;
            traveler.packUpRequiresRefound = true;
            traveler.massRelocationTier = tier;
            traveler.massRelocationDefensiveStrength = def;
            traveler.turtleGroupId = groupId;
            traveler.contributionFactors = new Dictionary<WorldObject, float>();

            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(dest.Tile, traveler));

            manager?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_Turtle_PackLeaf".Translate(label, dest.Label),
                traveler, dest));
            return traveler;
        }

        // ----------------------------------------------------------------------------------------
        // Arrival / finalize
        // ----------------------------------------------------------------------------------------

        public static void ExecuteTurtleArrival(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return;
            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();

            Settlement hub = ResolveHub(traveler, manager);
            if (hub == null || hub.Destroyed)
            {
                WorldActions_PackUp.TryRefoundFromTraveler(traveler, subType: null);
                MarkTurtleLeafResolved(traveler);
                NotifyLeafLost(traveler.turtleGroupId, manager);
                traveler.suppressDestroyedWorldFx = true;
                traveler.Destroy();
                return;
            }

            DepositIntoHub(hub, traveler, manager);
            int groupId = traveler.turtleGroupId;
            MarkTurtleLeafResolved(traveler);
            traveler.suppressDestroyedWorldFx = true;
            traveler.Destroy();

            BumpArrivedAndMaybeFinalize(groupId, manager);
        }

        private static void DepositIntoHub(Settlement hub, WorldObject_Traveler traveler, WorldComponent_SpreadManager manager)
        {
            var comp = hub?.GetComponent<CompViralSpread>();
            if (comp == null) return;

            // Deposit then regional-gated promotes (Develop/investment share localMaxT*).
            comp.DepositStrengthWithRegionalPromotes(Mathf.Max(0f, traveler.travelerStrength));
            float defMax = comp.GetBaseDefensiveStrength();
            comp.defensiveStrength = Mathf.Min(defMax, comp.defensiveStrength + Mathf.Max(0f, traveler.massRelocationDefensiveStrength));

            if (manager?.turtleGroups != null
                && manager.turtleGroups.TryGetValue(traveler.turtleGroupId, out TurtleGroupState group)
                && group != null)
            {
                if (group.depositHubIds == null) group.depositHubIds = new List<int>();
                if (!group.depositHubIds.Contains(hub.ID))
                    group.depositHubIds.Add(hub.ID);
            }
        }

        private static void BumpArrivedAndMaybeFinalize(int groupId, WorldComponent_SpreadManager manager)
        {
            if (manager?.turtleGroups == null) return;
            if (!manager.turtleGroups.TryGetValue(groupId, out TurtleGroupState group) || group == null) return;

            group.arrived++;
            MaybeFinalizeTurtleGroup(groupId, group, manager);
        }

        private static void MaybeFinalizeTurtleGroup(int groupId, TurtleGroupState group, WorldComponent_SpreadManager manager)
        {
            if (manager?.turtleGroups == null || group == null || group.fortified) return;
            int now = Find.TickManager.TicksGame;
            bool allIn = group.expected > 0 && group.arrived >= group.expected;
            bool empty = group.expected <= 0;
            bool timedOut = group.deadlineTick > 0 && now >= group.deadlineTick;
            if (!allIn && !empty && !timedOut) return;

            TryFinalizeTurtleFortify(group, manager);
            group.fortified = true;
            manager.turtleGroups.Remove(groupId);
        }

        private static void TryFinalizeTurtleFortify(TurtleGroupState group, WorldComponent_SpreadManager manager)
        {
            if (group == null) return;

            var hubs = new List<Settlement>();
            if (group.depositHubIds != null)
            {
                for (int i = 0; i < group.depositHubIds.Count; i++)
                {
                    Settlement s = FindSettlementById(group.depositHubIds[i]);
                    if (s != null && !s.Destroyed) hubs.Add(s);
                }
            }
            if (hubs.Count == 0)
            {
                Settlement primary = FindSettlementById(group.hubId);
                if (primary != null && !primary.Destroyed) hubs.Add(primary);
            }

            for (int i = 0; i < hubs.Count; i++)
            {
                Settlement hub = hubs[i];
                Faction faction = hub.Faction;
                if (faction == null) continue;

                var comp = hub.GetComponent<CompViralSpread>();
                SettlementTier kitTier = comp != null ? comp.tier : SettlementTier.T1;

                WorldActions_DesperationRaid.TryPlaceFortifyKit(hub.Tile.tileId, faction, kitTier, hub, null);

                manager?.AddLog(new SpreadLogEntry(
                    "TSA_WD_Log_Turtle_Fortify".Translate(hub.Label),
                    hub, null));
                WDVerbose.Msg($"Turtle fortify hub={hub.Label} tier={kitTier} arrived={group.arrived}/{group.expected}");
            }
        }

        /// <summary>Cancel / clash abort: deposit into the live destination, else remount the strength as a settlement.</summary>
        public static void TryAbortTurtleTraveler(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            Settlement hub = ResolveHub(traveler, manager);
            if (hub != null && !hub.Destroyed)
            {
                DepositIntoHub(hub, traveler, manager);
                MarkTurtleLeafResolved(traveler);
                BumpArrivedAndMaybeFinalize(traveler.turtleGroupId, manager);
                return;
            }

            WorldActions_PackUp.TryRefoundFromTraveler(traveler, subType: null);
            MarkTurtleLeafResolved(traveler);
            NotifyLeafLost(traveler.turtleGroupId, manager);
        }

        /// <summary>
        /// Combat / clash Destroy without a prior arrival or abort: count the leaf lost so the last migrant still fortifies.
        /// </summary>
        public static void NotifyTurtleTravelerDestroyedUnresolved(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.turtleLeafResolved) return;
            MarkTurtleLeafResolved(traveler);
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            NotifyLeafLost(traveler.turtleGroupId, manager);
            WDVerbose.Msg($"Turtle leaf destroyed unresolved group={traveler.turtleGroupId}");
        }

        private static void MarkTurtleLeafResolved(WorldObject_Traveler traveler)
        {
            if (traveler != null)
                traveler.turtleLeafResolved = true;
        }

        /// <summary>Leaf remounted without depositing — reduce expected and maybe fortify remaining hubs.</summary>
        private static void NotifyLeafLost(int groupId, WorldComponent_SpreadManager manager)
        {
            if (manager?.turtleGroups == null) return;
            if (!manager.turtleGroups.TryGetValue(groupId, out TurtleGroupState group) || group == null) return;
            group.expected = Mathf.Max(0, group.expected - 1);
            MaybeFinalizeTurtleGroup(groupId, group, manager);
        }

        // ----------------------------------------------------------------------------------------
        // Debug
        // ----------------------------------------------------------------------------------------

        /// <summary>Dev: cluster membership, hostile pressure, and Turtle / Strategy eligibility.</summary>
        public static string DebugShowClusterStats(Settlement seed)
        {
            if (seed == null || seed.Destroyed)
                return "No settlement.";

            Faction faction = seed.Faction;
            if (faction == null)
                return "Settlement has no faction.";

            var seth = WorldDominationMod.settings;
            float minRatio = seth != null ? Mathf.Max(0f, seth.turtlePressureRatio) : WorldDominationSettings.DefTurtlePressureRatio;
            int minCluster = seth != null ? Mathf.Max(2, seth.turtleMinClusterSize) : WorldDominationSettings.DefTurtleMinClusterSize;
            int thresholdPct = Mathf.RoundToInt(minRatio * 100f);

            CollectFactionSettlements(faction, null, tmpFactionSettlements);
            List<Settlement> cluster = FindClusterContaining(tmpFactionSettlements, seed);
            if (cluster == null || cluster.Count < 1)
                cluster = new List<Settlement> { seed };

            float ownStr = SumClusterOffense(cluster);

            var sb = new StringBuilder();
            sb.AppendLine("Cluster edge: " + ClusterEdgeTiles + " tiles; threat band: " + ClusterThreatBandTiles.ToString("F0") + " tiles");
            sb.AppendLine();
            sb.AppendLine("Defender (same faction settlements in this cluster)");
            for (int i = 0; i < cluster.Count; i++)
            {
                Settlement s = cluster[i];
                var c = s.GetComponent<CompViralSpread>();
                float str = c != null ? Mathf.Max(0f, c.offensiveStrength) : 0f;
                sb.AppendLine("- " + s.LabelCap + ", " + str.ToString("F0"));
            }
            sb.AppendLine("Defender total: " + ownStr.ToString("F0"));

            sb.AppendLine();
            sb.AppendLine("Attacker (enemy settlements and outposts threatening this cluster)");
            float hostileStr = AppendHostileThreatLines(sb, cluster, faction);
            sb.AppendLine("Attacker total: " + hostileStr.ToString("F0"));

            float ratio = hostileStr / Mathf.Max(1f, ownStr);
            int ratioPct = Mathf.RoundToInt(ratio * 100f);

            sb.AppendLine();
            sb.AppendLine("Strength Ratio (Attacker / Defender): " + hostileStr.ToString("F0") + " / " + ownStr.ToString("F0") + " = " + ratioPct + "%");
            sb.AppendLine();

            bool sizeOk = cluster.Count >= minCluster;
            bool pressureOk = ratio >= minRatio;
            sb.AppendLine("Cluster size: " + cluster.Count + " / " + minCluster + " required (" + (sizeOk ? "PASS" : "FAIL") + ")");
            sb.AppendLine("Pressure ratio: " + ratioPct + "% / " + thresholdPct + "% required (" + (pressureOk ? "PASS" : "FAIL") + ")");
            sb.AppendLine();
            sb.AppendLine(sizeOk && pressureOk
                ? "Qualifies for Turtle/Strategy on Settlement Loss Action."
                : "Does NOT qualify for Turtle/Strategy on Settlement Loss Action.");

            return sb.ToString();
        }

        private static float AppendHostileThreatLines(StringBuilder sb, List<Settlement> cluster, Faction faction)
        {
            int listed = 0;

            var settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s == null || s.Destroyed || s.Faction == null || s.Faction == faction) continue;
                if (s.Faction.defeated) continue;
                if (!WorldActions_Utils.SafeHostileTo(faction, s.Faction)) continue;
                if (!IsWithinThreatBandOfCluster(s.Tile.tileId, cluster)) continue;
                if (!TryGetHostileThreatStrength(s, out float str)) continue;

                listed++;
                sb.AppendLine("- " + s.LabelCap + ", " + str.ToString("F0"));
            }

            Faction player = Faction.OfPlayerSilentFail;
            if (player != null && WorldActions_Utils.SafeHostileTo(faction, player))
            {
                IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
                if (outposts != null)
                {
                    for (int i = 0; i < outposts.Count; i++)
                    {
                        WorldObject_WD_Outpost o = outposts[i];
                        if (o == null || o.Destroyed) continue;
                        if (!IsWithinThreatBandOfCluster(o.Tile.tileId, cluster)) continue;
                        var oc = o.GetComponent<CompViralSpread>();
                        float str = oc != null ? Mathf.Max(0f, oc.offensiveStrength) : 0f;
                        if (str <= 0f) continue;
                        listed++;
                        sb.AppendLine("- " + o.LabelCap + ", " + str.ToString("F0"));
                    }
                }
            }

            if (listed == 0)
                sb.AppendLine("- (none)");

            return SumHostileThreatToCluster(cluster, faction);
        }

        private static bool TryGetHostileThreatStrength(Settlement s, out float str)
        {
            str = 0f;
            if (s == null) return false;
            var c = s.GetComponent<CompViralSpread>();
            if (s.Faction != null && s.Faction.IsPlayer)
            {
                float p = c != null ? Mathf.Max(c.offensiveStrength, c.defensiveStrength) : 0f;
                str = Mathf.Max(p, 150f);
                return true;
            }
            if (c == null) return false;
            str = Mathf.Max(0f, c.offensiveStrength);
            return str > 0f;
        }

        /// <summary>Dev: force a turtle consolidate seeded from a clicked NPC settlement. Skips chance / cooldown / pressure / min-cluster gates.</summary>
        public static bool DebugForceTurtle(Settlement seed, out string message)
        {
            message = null;
            if (seed == null || seed.Destroyed)
            {
                message = "no settlement";
                return false;
            }

            Faction faction = seed.Faction;
            if (faction == null || faction.IsPlayer || faction.defeated)
            {
                message = "click an NPC settlement";
                return false;
            }

            if (!WorldActions_Utils.IsWdParticipant(faction))
            {
                message = $"{faction.Name} is not a WD participant";
                return false;
            }

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            var seth = WorldDominationMod.settings;
            if (manager == null || seth == null)
            {
                message = "no manager/settings";
                return false;
            }

            manager.turtleCooldownByFaction?.Remove(faction.loadID);
            manager.factionComebackCooldownByFaction?.Remove(faction.loadID);

            List<Settlement> all;
            if (manager.TryGetFactionSettlements(faction, out all) && all != null)
                CollectFactionSettlements(faction, all, tmpFactionSettlements);
            else
                CollectFactionSettlements(faction, null, tmpFactionSettlements);

            List<Settlement> cluster = FindClusterContaining(tmpFactionSettlements, seed);
            // Debug: ignore min-cluster size — fall back to every faction site so a lone/small group still tries.
            if (cluster == null || cluster.Count < 2)
            {
                if (tmpFactionSettlements.Count < 2)
                {
                    message = "need at least 2 faction settlements to turtle (hub + leaf)";
                    return false;
                }
                cluster = new List<Settlement>(tmpFactionSettlements);
                WDVerbose.Msg($"Turtle debug fallback pack-all-faction n={cluster.Count} seed={seed.Label}");
            }

            Settlement hub = PickPrimaryHub(cluster, seed);
            if (Launch(manager, seth, faction, cluster, hub, forceDebug: true))
            {
                message = $"forced turtle for {faction.Name}; primary {hub.LabelCap}";
                return true;
            }

            message = "turtle launch failed (no leaves ≥400 offense that fit dig-in T3/T4 room — check WDVerbose)";
            return false;
        }

        // ----------------------------------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------------------------------

        private static Settlement FindSettlementById(int id)
        {
            if (id < 0) return null;
            var settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s != null && !s.Destroyed && s.ID == id) return s;
            }
            return null;
        }

        private static Settlement ResolveHub(WorldObject_Traveler traveler, WorldComponent_SpreadManager manager)
        {
            if (traveler == null) return null;
            if (traveler.targetObject is Settlement s && !s.Destroyed)
                return s;

            if (manager?.turtleGroups != null
                && manager.turtleGroups.TryGetValue(traveler.turtleGroupId, out TurtleGroupState group)
                && group != null && group.hubId >= 0)
            {
                return FindSettlementById(group.hubId);
            }
            return null;
        }

        private static void CollectFactionSettlements(Faction faction, List<Settlement> preferred, List<Settlement> into)
        {
            into.Clear();
            IReadOnlyList<Settlement> src = preferred;
            if (src == null)
                src = Find.WorldObjects.Settlements;

            for (int i = 0; i < src.Count; i++)
            {
                Settlement s = src[i];
                if (s == null || s.Destroyed || s.Faction != faction) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;
                if (s.GetComponent<CompViralSpread>() == null) continue;
                into.Add(s);
            }
        }

        /// <summary>Partition faction sites into connected components (edge distance ≤ <see cref="ClusterEdgeTiles"/>).</summary>
        private static void BuildFactionClusters(List<Settlement> all, List<List<Settlement>> clustersInto)
        {
            clustersInto.Clear();
            tmpSeen.Clear();

            for (int i = 0; i < all.Count; i++)
            {
                Settlement seed = all[i];
                if (tmpSeen.Contains(seed.ID)) continue;

                var cluster = new List<Settlement>();
                tmpBfs.Clear();
                tmpBfs.Enqueue(seed);
                tmpSeen.Add(seed.ID);

                while (tmpBfs.Count > 0)
                {
                    Settlement cur = tmpBfs.Dequeue();
                    cluster.Add(cur);
                    for (int j = 0; j < all.Count; j++)
                    {
                        Settlement other = all[j];
                        if (tmpSeen.Contains(other.ID)) continue;
                        float d = Find.WorldGrid.ApproxDistanceInTiles(cur.Tile.tileId, other.Tile.tileId);
                        if (d > ClusterEdgeTiles) continue;
                        tmpSeen.Add(other.ID);
                        tmpBfs.Enqueue(other);
                    }
                }

                clustersInto.Add(cluster);
            }
        }

        private static List<Settlement> FindClusterContaining(List<Settlement> all, Settlement seed)
        {
            if (seed == null) return null;
            BuildFactionClusters(all, tmpClusters);
            for (int i = 0; i < tmpClusters.Count; i++)
            {
                List<Settlement> cluster = tmpClusters[i];
                for (int j = 0; j < cluster.Count; j++)
                    if (cluster[j] == seed)
                        return new List<Settlement>(cluster);
            }
            return null;
        }

        /// <summary>Primary hub prefers T4, else highest tier / strength. <paramref name="preferSeed"/> wins same-tier ties when it is a valid hub.</summary>
        private static Settlement PickPrimaryHub(List<Settlement> cluster, Settlement preferSeed)
        {
            Settlement bestT4 = null;
            Settlement bestAny = null;
            for (int i = 0; i < cluster.Count; i++)
            {
                Settlement s = cluster[i];
                if (s == null || s.Destroyed) continue;
                var c = s.GetComponent<CompViralSpread>();
                if (c == null) continue;

                if (c.tier == SettlementTier.T4)
                {
                    if (bestT4 == null || IsStrictlyBetterHub(s, bestT4))
                        bestT4 = s;
                }
                if (bestAny == null || IsStrictlyBetterHub(s, bestAny))
                    bestAny = s;
            }

            Settlement best = bestT4 ?? bestAny;
            if (preferSeed != null && cluster.Contains(preferSeed))
            {
                var pc = preferSeed.GetComponent<CompViralSpread>();
                if (pc != null && best != null)
                {
                    var bc = best.GetComponent<CompViralSpread>();
                    // Prefer seed when same tier as the chosen hub (debug click affinity).
                    if (bc != null && pc.tier == bc.tier)
                        return preferSeed;
                }
            }
            return best;
        }

        private static bool IsStrictlyBetterHub(Settlement candidate, Settlement current)
        {
            var cc = candidate.GetComponent<CompViralSpread>();
            var cur = current.GetComponent<CompViralSpread>();
            if (cc == null) return false;
            if (cur == null) return true;
            if (cc.tier != cur.tier) return cc.tier > cur.tier;
            // Prefer more room to fill when same tier (emptier T4 first).
            float roomC = OffensiveRoomToT4Cap(cc);
            float roomCur = OffensiveRoomToT4Cap(cur);
            if (!Mathf.Approximately(roomC, roomCur)) return roomC > roomCur;
            return cc.offensiveStrength > cur.offensiveStrength;
        }

        private static float SumClusterOffense(List<Settlement> cluster)
        {
            float sum = 0f;
            if (cluster == null) return sum;
            for (int i = 0; i < cluster.Count; i++)
            {
                Settlement s = cluster[i];
                if (s == null || s.Destroyed) continue;
                var c = s.GetComponent<CompViralSpread>();
                if (c != null) sum += Mathf.Max(0f, c.offensiveStrength);
            }
            return sum;
        }

        private static bool IsWithinThreatBandOfCluster(int tile, List<Settlement> cluster)
        {
            if (cluster == null) return false;
            for (int i = 0; i < cluster.Count; i++)
            {
                Settlement site = cluster[i];
                if (site == null || site.Destroyed) continue;
                if (Find.WorldGrid.ApproxDistanceInTiles(tile, site.Tile.tileId) <= ClusterThreatBandTiles)
                    return true;
            }
            return false;
        }

        private static float SumHostileThreatToCluster(List<Settlement> cluster, Faction faction)
        {
            if (cluster == null || cluster.Count < 1 || faction == null) return 0f;
            float sum = 0f;

            var settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s == null || s.Destroyed || s.Faction == null || s.Faction == faction) continue;
                if (s.Faction.defeated) continue;
                if (!WorldActions_Utils.SafeHostileTo(faction, s.Faction)) continue;
                if (!IsWithinThreatBandOfCluster(s.Tile.tileId, cluster)) continue;
                var c = s.GetComponent<CompViralSpread>();
                if (s.Faction.IsPlayer)
                {
                    // Player map comps often store 0 deployable — still count regional threat.
                    float p = c != null ? Mathf.Max(c.offensiveStrength, c.defensiveStrength) : 0f;
                    sum += Mathf.Max(p, 150f);
                }
                else if (c != null)
                {
                    sum += Mathf.Max(0f, c.offensiveStrength);
                }
            }

            Faction player = Faction.OfPlayerSilentFail;
            if (player != null && WorldActions_Utils.SafeHostileTo(faction, player))
            {
                IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
                if (outposts != null)
                {
                    for (int i = 0; i < outposts.Count; i++)
                    {
                        WorldObject_WD_Outpost o = outposts[i];
                        if (o == null || o.Destroyed) continue;
                        if (!IsWithinThreatBandOfCluster(o.Tile.tileId, cluster)) continue;
                        var oc = o.GetComponent<CompViralSpread>();
                        if (oc != null) sum += Mathf.Max(0f, oc.offensiveStrength);
                    }
                }
            }

            return sum;
        }

        private static void PruneAndFinalizeGroups(WorldComponent_SpreadManager manager)
        {
            if (manager?.turtleGroups == null || manager.turtleGroups.Count == 0) return;
            List<int> keys = null;
            foreach (var kv in manager.turtleGroups)
                (keys ??= new List<int>()).Add(kv.Key);
            if (keys == null) return;

            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < keys.Count; i++)
            {
                int id = keys[i];
                if (!manager.turtleGroups.TryGetValue(id, out TurtleGroupState g) || g == null)
                {
                    manager.turtleGroups.Remove(id);
                    continue;
                }
                if (g.fortified)
                {
                    manager.turtleGroups.Remove(id);
                    continue;
                }

                bool hubGone = g.hubId < 0 || FindSettlementById(g.hubId) == null;
                bool timedOut = g.deadlineTick > 0 && now >= g.deadlineTick;
                if (hubGone || timedOut)
                    MaybeFinalizeTurtleGroup(id, g, manager);
            }
        }

        private static int NextTurtleGroupId(WorldComponent_SpreadManager manager)
        {
            int max = 0;
            if (manager?.turtleGroups != null)
                foreach (var key in manager.turtleGroups.Keys)
                    if (key > max) max = key;
            var objs = Find.WorldObjects?.AllWorldObjects;
            if (objs != null)
            {
                for (int i = 0; i < objs.Count; i++)
                {
                    if (objs[i] is WorldObject_Traveler t
                        && t.mission == TravelerMission.TurtleConsolidate
                        && t.turtleGroupId > max)
                        max = t.turtleGroupId;
                }
            }
            return max + 1;
        }

        private static bool IsFactionOnCooldown(WorldComponent_SpreadManager manager, Faction faction)
        {
            if (manager.turtleCooldownByFaction == null) return false;
            if (!manager.turtleCooldownByFaction.TryGetValue(faction.loadID, out int until)) return false;
            return Find.TickManager.TicksGame < until;
        }

        private static void StampFactionCooldown(WorldComponent_SpreadManager manager, Faction faction, WorldDominationSettings seth)
        {
            if (manager.turtleCooldownByFaction == null)
                manager.turtleCooldownByFaction = new Dictionary<int, int>();
            float days = Mathf.Max(0.1f, seth.turtleCooldownDays);
            manager.turtleCooldownByFaction[faction.loadID] =
                Find.TickManager.TicksGame + CompViralSpread.CooldownTicksFromDays(days);
        }
    }
}
