using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace TSA_WorldDomination
{
    public enum EncounterSpawnConstraint : byte
    {
        Open = 0,
        Bridge = 1,
        Trench = 2
    }

    /// <summary>
    /// Hostile encounter spawn cells must be standable, unfogged, and able to path to player pawns.
    /// Prevents EdgeWalkIn / band picks landing in sealed rock pockets on Ambush / Encounter maps.
    /// </summary>
    public static class WdEncounterSpawnCellUtility
    {
        private const int EntryBandWidth = 12;
        private const int OpenEdgeTries = 80;
        private const int OpenNearPlayerRadius = 18;
        private const int TrenchPoolTries = 120;

        private static readonly List<IntVec3> scratchAnchors = new List<IntVec3>(16);
        private static readonly List<IntVec3> scratchReachAnchors = new List<IntVec3>(2);

        private enum CardinalEdge : byte
        {
            West, East, North, South
        }

        public static EncounterSpawnConstraint DetectConstraint(Map map)
        {
            if (map == null) return EncounterSpawnConstraint.Open;
            if (map.GetComponent<MapComponent_WdTrenchClashLayout>() != null)
                return EncounterSpawnConstraint.Trench;
            var clash = map.GetComponent<WD_MapComponent_CaravanClash>();
            if (clash != null && clash.IsBridgeClash)
                return EncounterSpawnConstraint.Bridge;
            if (map.GetComponent<MapComponent_WdBridgeClashLayout>() != null)
                return EncounterSpawnConstraint.Bridge;
            return EncounterSpawnConstraint.Open;
        }

        /// <summary>Fill <paramref name="dst"/> with spawned player-faction pawn positions.</summary>
        public static void CollectPlayerAnchors(Map map, List<IntVec3> dst)
        {
            dst.Clear();
            if (map?.mapPawns?.AllPawnsSpawned == null) return;
            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn p = spawned[i];
                if (p == null || p.Destroyed || !p.Spawned) continue;
                if (p.Faction == null || !p.Faction.IsPlayer) continue;
                dst.Add(p.Position);
            }
        }

        /// <summary>1–2 reach targets: centroid cell (if any) plus nearest real player cell.</summary>
        public static void BuildReachAnchors(Map map, List<IntVec3> allAnchors, List<IntVec3> reachOut)
        {
            reachOut.Clear();
            if (allAnchors == null || allAnchors.Count == 0) return;

            int sx = 0, sz = 0;
            for (int i = 0; i < allAnchors.Count; i++)
            {
                sx += allAnchors[i].x;
                sz += allAnchors[i].z;
            }
            var centroid = new IntVec3(sx / allAnchors.Count, 0, sz / allAnchors.Count);

            IntVec3 nearest = allAnchors[0];
            int best = nearest.DistanceToSquared(centroid);
            for (int i = 1; i < allAnchors.Count; i++)
            {
                int d = allAnchors[i].DistanceToSquared(centroid);
                if (d >= best) continue;
                best = d;
                nearest = allAnchors[i];
            }
            reachOut.Add(nearest);
            if (centroid.InBounds(map) && centroid != nearest)
                reachOut.Add(centroid);
        }

        public static bool IsValidHostileSpawn(Map map, IntVec3 cell, List<IntVec3> reachAnchors)
        {
            if (map == null || !cell.IsValid || !cell.InBounds(map)) return false;
            if (cell.Fogged(map) || !cell.Standable(map)) return false;
            if (reachAnchors == null || reachAnchors.Count == 0) return true;
            TraverseParms traverse = TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly, false);
            for (int i = 0; i < reachAnchors.Count; i++)
            {
                IntVec3 anchor = reachAnchors[i];
                if (!anchor.IsValid || !anchor.InBounds(map)) continue;
                if (map.reachability.CanReach(cell, anchor, PathEndMode.OnCell, traverse))
                    return true;
            }
            return false;
        }

        public static bool FitsConstraint(Map map, IntVec3 cell, EncounterSpawnConstraint constraint)
        {
            if (map == null || !cell.IsValid || !cell.InBounds(map)) return false;
            switch (constraint)
            {
                case EncounterSpawnConstraint.Bridge:
                    return GenStep_WD_BridgeClash.IsOnBridge(map, cell);
                case EncounterSpawnConstraint.Trench:
                    return IsInTrenchHostilePool(map, cell);
                default:
                    return true;
            }
        }

        public static bool TryValidateOrRepickHostileSpawn(
            Map map,
            IntVec3 preferred,
            out IntVec3 cell,
            out Rot4 rot,
            EncounterSpawnConstraint? constraintOverride = null)
        {
            cell = IntVec3.Invalid;
            rot = Rot4.North;
            if (map == null) return false;

            EncounterSpawnConstraint constraint = constraintOverride ?? DetectConstraint(map);
            CollectPlayerAnchors(map, scratchAnchors);
            BuildReachAnchors(map, scratchAnchors, scratchReachAnchors);

            if (preferred.IsValid
                && FitsConstraint(map, preferred, constraint)
                && IsValidHostileSpawn(map, preferred, scratchReachAnchors))
            {
                cell = preferred;
                rot = FacingTowardPlayers(cell, scratchReachAnchors, map);
                return true;
            }

            switch (constraint)
            {
                case EncounterSpawnConstraint.Bridge:
                    if (TryPickBridgeSpawn(map, scratchReachAnchors, out cell))
                    {
                        rot = FacingTowardPlayers(cell, scratchReachAnchors, map);
                        return true;
                    }
                    return false;

                case EncounterSpawnConstraint.Trench:
                    if (TryPickTrenchSpawn(map, scratchReachAnchors, out cell, out rot))
                        return true;
                    return false;

                default:
                    if (TryPickOpenSpawn(map, scratchAnchors, scratchReachAnchors, out cell))
                    {
                        rot = FacingTowardPlayers(cell, scratchReachAnchors, map);
                        return true;
                    }
                    return false;
            }
        }

        /// <summary>Apply validated spawn onto incident parms (EdgeWalkIn steering).</summary>
        public static void ApplyValidatedSpawnToParms(Map map, IncidentParms parms, IntVec3 preferred = default)
        {
            if (map == null || parms == null) return;
            IntVec3 pref = preferred.IsValid ? preferred : parms.spawnCenter;
            if (TryValidateOrRepickHostileSpawn(map, pref, out IntVec3 cell, out Rot4 rot))
            {
                parms.spawnCenter = cell;
                parms.spawnRotation = rot;
            }
        }

        /// <summary>
        /// True when at least one spawned pawn of <paramref name="faction"/> can path to a player anchor.
        /// </summary>
        public static bool FactionHostilesCanReachPlayer(Map map, Faction faction)
        {
            if (map == null || faction == null) return false;
            CollectPlayerAnchors(map, scratchAnchors);
            BuildReachAnchors(map, scratchAnchors, scratchReachAnchors);
            if (scratchReachAnchors.Count == 0) return true;

            IReadOnlyList<Pawn> spawned = map.mapPawns?.AllPawnsSpawned;
            if (spawned == null) return false;
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn p = spawned[i];
                if (p == null || p.Destroyed || !p.Spawned || p.Dead) continue;
                if (p.Faction != faction) continue;
                if (IsValidHostileSpawn(map, p.Position, scratchReachAnchors))
                    return true;
            }
            return false;
        }

        public static int CountSpawnedFactionPawns(Map map, Faction faction)
        {
            if (map == null || faction == null) return 0;
            int n = 0;
            IReadOnlyList<Pawn> spawned = map.mapPawns?.AllPawnsSpawned;
            if (spawned == null) return 0;
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn p = spawned[i];
                if (p == null || p.Destroyed || !p.Spawned) continue;
                if (p.Faction == faction) n++;
            }
            return n;
        }

        /// <summary>Destroy spawned pawns of the raid faction (used before validated manual re-spawn).</summary>
        public static void DestroyFactionPawnsOnMap(Map map, Faction faction)
        {
            if (map == null || faction == null) return;
            IReadOnlyList<Pawn> spawned = map.mapPawns?.AllPawnsSpawned;
            if (spawned == null) return;
            var doomed = new List<Pawn>();
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn p = spawned[i];
                if (p == null || p.Destroyed || !p.Spawned) continue;
                if (p.Faction == faction)
                    doomed.Add(p);
            }
            for (int i = 0; i < doomed.Count; i++)
            {
                if (!doomed[i].Destroyed)
                    doomed[i].Destroy(DestroyMode.Vanish);
            }
        }

        /// <summary>
        /// After RaidEnemy.TryExecute: if no hostiles or none can reach the player, manual-spawn with a validated cell.
        /// </summary>
        public static bool EnsureReachableRaidOrManual(
            Map map,
            Faction faction,
            float points,
            RaidStrategyDef raidStrategy,
            string letterLabel,
            bool incidentOk)
        {
            if (map == null || faction == null) return false;

            int count = CountSpawnedFactionPawns(map, faction);
            bool reachable = count > 0 && FactionHostilesCanReachPlayer(map, faction);
            if (incidentOk && reachable)
                return true;

            if (count > 0)
                DestroyFactionPawnsOnMap(map, faction);

            return WdRaidParmsUtility.TryManualAssaultSpawn(map, faction, points, raidStrategy, letterLabel);
        }

        public static bool TryFindValidatedEdgeCell(Map map, out IntVec3 cell, EncounterSpawnConstraint? constraintOverride = null)
        {
            cell = IntVec3.Invalid;
            return TryValidateOrRepickHostileSpawn(map, IntVec3.Invalid, out cell, out _, constraintOverride);
        }

        private static bool TryPickOpenSpawn(
            Map map,
            List<IntVec3> allAnchors,
            List<IntVec3> reachAnchors,
            out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            CardinalEdge playerEdge = CardinalEdge.South;
            if (allAnchors.Count > 0)
            {
                int sx = 0, sz = 0;
                for (int i = 0; i < allAnchors.Count; i++)
                {
                    sx += allAnchors[i].x;
                    sz += allAnchors[i].z;
                }
                playerEdge = DominantCardinalEdge(new IntVec3(sx / allAnchors.Count, 0, sz / allAnchors.Count), map);
            }

            CardinalEdge opposite = OppositeCardinal(playerEdge);
            if (TryFindEntryInBand(map, opposite, reachAnchors, out cell))
                return true;

            var edges = new[] { CardinalEdge.West, CardinalEdge.East, CardinalEdge.North, CardinalEdge.South };
            for (int i = 0; i < edges.Length; i++)
            {
                CardinalEdge e = edges[i];
                if (e == playerEdge || e == opposite) continue;
                if (TryFindEntryInBand(map, e, reachAnchors, out cell))
                    return true;
            }

            if (CellFinder.TryFindRandomEdgeCellWith(
                    c => IsValidHostileSpawn(map, c, reachAnchors),
                    map,
                    CellFinder.EdgeRoadChance_Hostile,
                    out cell))
                return true;

            // Last resort: near a player pawn.
            if (allAnchors.Count > 0)
            {
                IntVec3 near = allAnchors[0];
                if (CellFinder.TryFindRandomCellNear(
                        near,
                        map,
                        OpenNearPlayerRadius,
                        c => IsValidHostileSpawn(map, c, reachAnchors) && (c - near).LengthHorizontalSquared > 36,
                        out cell))
                    return true;
                if (CellFinder.TryFindRandomCellNear(
                        near,
                        map,
                        OpenNearPlayerRadius,
                        c => IsValidHostileSpawn(map, c, reachAnchors),
                        out cell))
                    return true;
            }

            return false;
        }

        private static bool TryPickBridgeSpawn(Map map, List<IntVec3> reachAnchors, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            IntVec3 east = GenStep_WD_BridgeClash.EastSpawnCell(map);
            if (GenStep_WD_BridgeClash.TryFindStandableNear(map, east, out IntVec3 nearEast)
                && IsValidHostileSpawn(map, nearEast, reachAnchors)
                && FitsConstraint(map, nearEast, EncounterSpawnConstraint.Bridge))
            {
                cell = nearEast;
                return true;
            }

            var layout = map.GetComponent<MapComponent_WdBridgeClashLayout>();
            if (layout?.BridgeCells != null && layout.BridgeCells.Count > 0)
            {
                // Prefer cells near the hostile end, then any reachable bridge cell.
                IntVec3 hostileEnd = GenStep_WD_BridgeClash.EastSpawnCell(map);
                IntVec3 best = IntVec3.Invalid;
                int bestDist = int.MaxValue;
                foreach (IntVec3 c in layout.BridgeCells)
                {
                    if (!IsValidHostileSpawn(map, c, reachAnchors)) continue;
                    int d = c.DistanceToSquared(hostileEnd);
                    if (d >= bestDist) continue;
                    bestDist = d;
                    best = c;
                }
                if (best.IsValid)
                {
                    cell = best;
                    return true;
                }
            }

            return false;
        }

        private static bool TryPickTrenchSpawn(
            Map map,
            List<IntVec3> reachAnchors,
            out IntVec3 cell,
            out Rot4 rot)
        {
            cell = IntVec3.Invalid;
            rot = Rot4.North;

            if (GenStep_WD_TrenchClash.TryGetHostileEdgeSpawn(map, out IntVec3 trenchEdge, out Rot4 trenchRot)
                && IsValidHostileSpawn(map, trenchEdge, reachAnchors))
            {
                cell = trenchEdge;
                rot = trenchRot;
                return true;
            }

            var layout = map.GetComponent<MapComponent_WdTrenchClashLayout>();
            if (layout == null) return false;

            List<IntVec3> pool = layout.CorridorCells;
            if (pool != null && pool.Count > 0)
            {
                for (int n = 0; n < TrenchPoolTries; n++)
                {
                    IntVec3 c = pool[Rand.Range(0, pool.Count)];
                    if (!IsValidHostileSpawn(map, c, reachAnchors)) continue;
                    cell = c;
                    rot = FacingTowardPlayers(cell, reachAnchors, map);
                    return true;
                }
                for (int i = 0; i < pool.Count; i++)
                {
                    IntVec3 c = pool[i];
                    if (!IsValidHostileSpawn(map, c, reachAnchors)) continue;
                    cell = c;
                    rot = FacingTowardPlayers(cell, reachAnchors, map);
                    return true;
                }
            }

            return false;
        }

        private static bool IsInTrenchHostilePool(Map map, IntVec3 cell)
        {
            var layout = map.GetComponent<MapComponent_WdTrenchClashLayout>();
            if (layout == null) return false;
            if (layout.CorridorCells != null)
            {
                for (int i = 0; i < layout.CorridorCells.Count; i++)
                    if (layout.CorridorCells[i] == cell) return true;
            }
            if (layout.HostileSpawnCell.IsValid && cell.DistanceToSquared(layout.HostileSpawnCell) <= 100)
                return true;
            return false;
        }

        private static bool TryFindEntryInBand(
            Map map,
            CardinalEdge edge,
            List<IntVec3> reachAnchors,
            out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            CellRect bandRect = CardinalBand(map, edge, EntryBandWidth);
            if (bandRect.Area <= 0) return false;

            bool InBand(IntVec3 c) => bandRect.Contains(c) && IsValidHostileSpawn(map, c, reachAnchors);
            if (RCellFinder.TryFindRandomPawnEntryCell(out cell, map, 0.35f, true, InBand))
                return true;

            for (int i = 0; i < OpenEdgeTries; i++)
            {
                IntVec3 c = bandRect.RandomCell;
                if (!IsValidHostileSpawn(map, c, reachAnchors)) continue;
                cell = c;
                return true;
            }
            return false;
        }

        private static Rot4 FacingTowardPlayers(IntVec3 from, List<IntVec3> reachAnchors, Map map)
        {
            IntVec3 toward = reachAnchors != null && reachAnchors.Count > 0 ? reachAnchors[0] : map.Center;
            return Rot4.FromAngleFlat((toward - from).ToVector3().AngleFlat());
        }

        private static CardinalEdge DominantCardinalEdge(IntVec3 c, Map map)
        {
            CellRect rect = CellRect.WholeMap(map);
            int dW = c.x - rect.minX;
            int dE = rect.maxX - c.x;
            int dS = c.z - rect.minZ;
            int dN = rect.maxZ - c.z;
            int min = Mathf.Min(Mathf.Min(dW, dE), Mathf.Min(dS, dN));
            if (min == dW) return CardinalEdge.West;
            if (min == dE) return CardinalEdge.East;
            if (min == dS) return CardinalEdge.South;
            return CardinalEdge.North;
        }

        private static CardinalEdge OppositeCardinal(CardinalEdge e)
        {
            switch (e)
            {
                case CardinalEdge.West: return CardinalEdge.East;
                case CardinalEdge.East: return CardinalEdge.West;
                case CardinalEdge.North: return CardinalEdge.South;
                default: return CardinalEdge.North;
            }
        }

        private static CellRect CardinalBand(Map map, CardinalEdge edge, int band)
        {
            CellRect rect = CellRect.WholeMap(map);
            if (rect.Area <= 0) return CellRect.Empty;
            int w = Mathf.Clamp(band, 1, Mathf.Max(rect.Width, rect.Height));
            switch (edge)
            {
                case CardinalEdge.West: return new CellRect(rect.minX, rect.minZ, w, rect.Height);
                case CardinalEdge.East: return new CellRect(rect.maxX - (w - 1), rect.minZ, w, rect.Height);
                case CardinalEdge.North: return new CellRect(rect.minX, rect.maxZ - (w - 1), rect.Width, w);
                default: return new CellRect(rect.minX, rect.minZ, rect.Width, w);
            }
        }
    }
}
