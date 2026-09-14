using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Vanguard forward assault: pack far hostiles with per-home visuals, geo local rally absorb,
    /// then MassRelocation columns dig in on the player front (peer distance 2).
    /// </summary>
    public static class WorldActions_Vanguard
    {
        public static bool TryLaunch(WorldComponent_SpreadManager manager, DailyWorldSnapshot snapshot, WorldDominationSettings seth)
        {
            if (!WorldActions_PackUp.TryPickStrongHostileFaction(manager, snapshot, seth, out Faction faction, out List<Settlement> sources))
                return false;
            return LaunchCore(manager, seth, faction, sources);
        }

        /// <summary>Dev: force Vanguard pack-up (skips enable/chance/escalation/CD and settlement-count gates).</summary>
        public static bool DebugForce(out string message)
        {
            message = null;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            var seth = WorldDominationMod.settings;
            if (manager == null || seth == null)
            {
                message = "no manager/settings";
                return false;
            }

            WorldActions_PackUp.ClearForwardAssaultCooldown(manager);
            DailyWorldSnapshot snapshot = DailyWorldSnapshot.Build();
            if (!WorldActions_PackUp.TryPickAnyHostileFactionForDebug(snapshot, out Faction faction, out List<Settlement> sources))
            {
                message = "Vanguard failed (no hostile WD settlement)";
                return false;
            }
            if (LaunchCore(manager, seth, faction, sources, forceDebug: true))
            {
                message = "Vanguard launched";
                return true;
            }

            message = "Vanguard failed (no player anchor or cluster reservation failed — check WDVerbose)";
            return false;
        }

        /// <summary>Dev: force a Vanguard pack-up seeded from a clicked NPC settlement.</summary>
        public static bool DebugForceFromSettlement(Settlement seed, out string message)
        {
            message = null;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            var seth = WorldDominationMod.settings;
            if (manager == null || seth == null) { message = "no manager/settings"; return false; }
            if (seed == null || seed.Destroyed || seed.Faction == null || seed.Faction.IsPlayer || seed.Faction.defeated)
            {
                message = "click an NPC settlement";
                return false;
            }

            WorldActions_PackUp.ClearForwardAssaultCooldown(manager);
            DailyWorldSnapshot snapshot = DailyWorldSnapshot.Build();
            List<Settlement> sources = WorldActions_PackUp.BuildDebugForcedSources(seed.Faction, seed, snapshot);
            if (LaunchCore(manager, seth, seed.Faction, sources, forceDebug: true))
            {
                message = $"Vanguard launched for {seed.Faction.Name}";
                return true;
            }
            message = "Vanguard failed (no player anchor or cluster reservation failed — check WDVerbose)";
            return false;
        }

        private static bool LaunchCore(
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth,
            Faction faction,
            List<Settlement> sources,
            bool forceDebug = false)
        {
            if (faction == null || sources == null || sources.Count < 1) return false;

            Settlement playerAnchor = WorldActions_PackUp.FindNearestPlayerSettlement(sources[0].Tile.tileId, manager);
            if (playerAnchor == null)
            {
                WDVerbose.Msg("ForwardAssault Vanguard abort reason=no-player-anchor");
                return false;
            }

            if (!TryFindClusterSeed(sources, seth, manager, out int seedTile, out int colonyTile, out int keepOut))
            {
                WDVerbose.Msg("ForwardAssault Vanguard abort reason=no-front-seed");
                return false;
            }

            int foundCount = ResolveFoundCount(seth, sources.Count, forceDebug);
            WDVerbose.Msg(
                $"ForwardAssault Vanguard cluster-seek need={foundCount} seed={seedTile} colony={colonyTile} keepOut={keepOut} anchor={playerAnchor.Label} faction={faction.Name}");

            if (!WdSettlementClusterUtility.TryReserveClusterNear(
                    seedTile, foundCount, faction, out List<int> tiles, colonyTile, keepOut))
            {
                WDVerbose.Msg($"ForwardAssault Vanguard abort reason=cluster-fail need={foundCount} seed={seedTile}");
                return false;
            }
            WDVerbose.Msg($"ForwardAssault Vanguard cluster reserved count={tiles.Count} tiles=[{string.Join(",", tiles)}]");

            List<List<Settlement>> assemblies = WorldActions_AssaultRally.PartitionAssemblies(sources);
            if (assemblies.Count < 1)
            {
                WDVerbose.Msg("ForwardAssault Vanguard abort reason=no-assemblies");
                return false;
            }

            assemblies.Sort((a, b) => AssemblyPickWeight(b).CompareTo(AssemblyPickWeight(a)));

            int columns = 0;
            int travelersLaunched = 0;
            var lookTargets = new List<WorldObject>();
            var digInAssigned = new bool[tiles.Count];

            for (int a = 0; a < assemblies.Count; a++)
            {
                List<Settlement> assembly = assemblies[a];
                if (assembly == null || assembly.Count < 1) continue;

                int digIn = PickDigInForAssembly(assembly, tiles, digInAssigned);
                if (digIn < 0) continue;

                int groupId = manager.AllocateVanguardGroupId();
                List<WorldObject_Traveler> launched = WorldActions_PackUp.LaunchVanguardAssembly(
                    assembly, digIn, groupId, manager);
                if (launched == null || launched.Count < 1) continue;

                columns++;
                for (int i = 0; i < launched.Count; i++)
                {
                    travelersLaunched++;
                    lookTargets.Add(launched[i]);
                }
            }

            if (columns < 1)
            {
                WDVerbose.Msg("ForwardAssault Vanguard abort after zero launches");
                return false;
            }

            WorldActions_SpecialEventCooldown.Stamp(manager, SpecialWorldEventKind.ForwardAssault);
            string msg = "TSA_WD_Log_ForwardAssault_Vanguard".Translate(faction.Name, columns);
            manager.AddLog(new SpreadLogEntry(msg, playerAnchor, null));
            WDVerbose.Msg(
                $"ForwardAssault Vanguard launched faction={faction.Name} columns={columns} travelers={travelersLaunched} assemblies={assemblies.Count}");

            LookTargets letterLook = lookTargets.Count > 0
                ? new LookTargets(lookTargets)
                : new LookTargets(playerAnchor);
            Find.LetterStack.ReceiveLetter(
                "TSA_WD_Letter_Vanguard_Label".Translate(),
                "TSA_WD_Letter_Vanguard_Text".Translate(faction.Name.Colorize(Color.cyan), columns.ToString()),
                LetterDefOf.ThreatBig,
                letterLook);
            return true;
        }

        private static float AssemblyPickWeight(List<Settlement> assembly)
        {
            if (assembly == null) return 0f;
            float w = 0f;
            for (int i = 0; i < assembly.Count; i++)
            {
                Settlement s = assembly[i];
                if (s == null || s.Destroyed) continue;
                w += WorldActions_PackUp.SourcePickWeight(s);
            }
            return w;
        }

        /// <summary>Prefer unused dig-in closest to assembly centroid; reuse tiles when assemblies exceed reserved count.</summary>
        private static int PickDigInForAssembly(List<Settlement> assembly, List<int> tiles, bool[] digInAssigned)
        {
            if (tiles == null || tiles.Count < 1 || digInAssigned == null) return -1;
            WorldGrid grid = Find.WorldGrid;
            int centroid = WorldActions_AssaultRally.ComputeCentroidTile(assembly);
            if (centroid < 0 && assembly != null)
            {
                for (int i = 0; i < assembly.Count; i++)
                {
                    if (assembly[i] != null && !assembly[i].Destroyed)
                    {
                        centroid = assembly[i].Tile.tileId;
                        break;
                    }
                }
            }

            int PickClosest(bool unusedOnly)
            {
                int bestIdx = -1;
                float bestDist = float.MaxValue;
                for (int i = 0; i < tiles.Count; i++)
                {
                    if (unusedOnly && digInAssigned[i]) continue;
                    float d = grid != null && centroid >= 0
                        ? grid.ApproxDistanceInTiles(centroid, tiles[i])
                        : i;
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestIdx = i;
                    }
                }
                return bestIdx;
            }

            int idx = PickClosest(unusedOnly: true);
            if (idx < 0)
                idx = PickClosest(unusedOnly: false);
            if (idx < 0) return -1;

            digInAssigned[idx] = true;
            return tiles[idx];
        }

        private static int ResolveFoundCount(WorldDominationSettings seth, int sourceCount, bool forceDebug)
        {
            int min = Mathf.Max(1, seth?.vanguardFoundSitesMin ?? WorldDominationSettings.DefVanguardFoundSitesMin);
            int max = Mathf.Max(min, seth?.vanguardFoundSitesMax ?? WorldDominationSettings.DefVanguardFoundSitesMax);
            int want = forceDebug
                ? Mathf.Clamp(Mathf.Min(sourceCount, max), 1, max)
                : Rand.RangeInclusive(min, max);
            return Mathf.Clamp(want, 1, Mathf.Max(1, sourceCount));
        }

        /// <summary>
        /// Prefer player outpost front facing the pack; else colony annulus toward the pack sources.
        /// </summary>
        private static bool TryFindClusterSeed(
            List<Settlement> sources,
            WorldDominationSettings seth,
            WorldComponent_SpreadManager manager,
            out int seedTile,
            out int colonyTile,
            out int keepOut)
        {
            seedTile = -1;
            colonyTile = -1;
            keepOut = Mathf.Max(0, seth?.vanguardClusterMinDistFromColony ?? WorldDominationSettings.DefVanguardClusterMinDistFromColony);
            int maxDist = Mathf.Max(keepOut, seth?.vanguardClusterMaxDistFromColony ?? WorldDominationSettings.DefVanguardClusterMaxDistFromColony);

            Settlement colony = InfluenceUtils.GetPlayerColony();
            if (colony != null && colony.Tile >= 0)
                colonyTile = colony.Tile.tileId;
            else if (sources.Count > 0)
            {
                Settlement nearest = WorldActions_PackUp.FindNearestPlayerSettlement(sources[0].Tile.tileId, manager);
                if (nearest != null)
                    colonyTile = nearest.Tile.tileId;
            }

            int towardTile = sources[0].Tile.tileId;
            List<WorldObject> playerTargets = WorldActions_PackUp.CollectDistinctPlayerTargets();
            WorldObject front = WorldActions_PackUp.PickNearestPlayerTarget(towardTile, playerTargets);

            WorldGrid grid = Find.WorldGrid;
            if (front is WorldObject_WD_Outpost outpost && !outpost.Destroyed && grid != null)
            {
                int frontTile = outpost.Tile.tileId;
                float fromColony = colonyTile >= 0 ? grid.ApproxDistanceInTiles(frontTile, colonyTile) : keepOut;
                if (fromColony >= keepOut)
                {
                    seedTile = frontTile;
                    WDVerbose.Msg($"ForwardAssault Vanguard seed=front-outpost tile={seedTile} distColony={fromColony:F0}");
                    return true;
                }
            }

            if (colonyTile < 0 || grid == null)
                return false;

            if (!TryPickAnnulusSeedToward(colonyTile, towardTile, keepOut, maxDist, out seedTile))
            {
                // Fallback: any annulus tile, then colony itself as BFS seed.
                if (!TryPickAnyAnnulusSeed(colonyTile, keepOut, maxDist, out seedTile))
                    seedTile = colonyTile;
            }

            WDVerbose.Msg($"ForwardAssault Vanguard seed=annulus tile={seedTile} colony={colonyTile} toward={towardTile}");
            return seedTile >= 0;
        }

        private static bool TryPickAnnulusSeedToward(int colonyTile, int towardTile, int minR, int maxR, out int seedTile)
        {
            seedTile = -1;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || !grid.InBounds(colonyTile)) return false;

            PlanetTile parent = new PlanetTile(colonyTile, grid[colonyTile].Layer);
            PlanetLayer layer = parent.Layer ?? PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            if (layer == null) return false;

            var candidates = new List<int>();
            layer.Filler.FloodFill(
                parent,
                pt => PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(pt),
                (PlanetTile pt, int dist) =>
                {
                    if (dist > maxR) return true;
                    if (dist >= minR && dist <= maxR)
                        candidates.Add(pt.tileId);
                    return false;
                });

            if (candidates.Count < 1) return false;

            float best = float.MaxValue;
            int bestTile = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                int t = candidates[i];
                float d = grid.ApproxDistanceInTiles(t, towardTile);
                if (d < best)
                {
                    best = d;
                    bestTile = t;
                }
            }

            seedTile = bestTile;
            return seedTile >= 0;
        }

        private static bool TryPickAnyAnnulusSeed(int colonyTile, int minR, int maxR, out int seedTile)
        {
            seedTile = -1;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || !grid.InBounds(colonyTile)) return false;

            PlanetTile parent = new PlanetTile(colonyTile, grid[colonyTile].Layer);
            PlanetLayer layer = parent.Layer ?? PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            if (layer == null) return false;

            int targetDist = Rand.RangeInclusive(minR, maxR);
            var ring = new List<int>();
            layer.Filler.FloodFill(
                parent,
                pt => PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(pt),
                (PlanetTile pt, int dist) =>
                {
                    if (dist > targetDist) return true;
                    if (dist == targetDist)
                        ring.Add(pt.tileId);
                    return false;
                });

            return ring.TryRandomElement(out seedTile);
        }
    }
}
