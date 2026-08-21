using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Neighbor ID cache for reinforcement scans. Nuclear dirty via global generation + lazy rebuild per entry.
    /// Caches who (IDs), not how strong — callers always sum live strength from resolved objects.
    /// </summary>
    public static class ReinforcementNeighborCache
    {
        private static int generation = 1;
        private static readonly Dictionary<long, CacheEntry> entries = new Dictionary<long, CacheEntry>(256);
        private static readonly List<int> rebuildIdsScratch = new List<int>(32);
        private static readonly HashSet<int> resolveWantScratch = new HashSet<int>();
        private static readonly Dictionary<int, WorldObject> idIndex = new Dictionary<int, WorldObject>(512);
        private static int idIndexGeneration = -1;

        private struct CacheEntry
        {
            public int generation;
            public List<int> neighborIds;
        }

        public static void BumpGeneration()
        {
            generation++;
            if (generation == int.MaxValue)
            {
                generation = 1;
                entries.Clear();
                idIndex.Clear();
                idIndexGeneration = -1;
            }
        }

        /// <summary>Current nuclear-dirty generation; used by WD-faction and ID-index caches.</summary>
        public static int Generation => generation;

        /// <summary>
        /// Fill <paramref name="into"/> with live neighbor world objects for the given primary/radius/enemy context.
        /// When <paramref name="excludedFactions"/> is non-empty, skips cache (coalition filters).
        /// </summary>
        public static void FillNeighbors(
            WorldObject primary,
            WorldObject enemy,
            float radius,
            Dictionary<Faction, List<WorldObject>> lookup,
            WorldComponent_SpreadManager manager,
            List<Faction> excludedFactions,
            List<WorldObject> into)
        {
            into.Clear();
            if (primary == null || radius <= 0f) return;

            if (excludedFactions != null && excludedFactions.Count > 0)
            {
                Raid_ReinforcementLogic.ScanReinforcementsLive(primary, enemy, radius, lookup, manager, excludedFactions, into);
                return;
            }

            int radiusKey = Mathf.RoundToInt(radius);
            int enemyFacId = (enemy != null && enemy.Faction != null) ? enemy.Faction.loadID : -1;
            long key = MakeKey(primary.ID, radiusKey, enemyFacId);

            if (!entries.TryGetValue(key, out CacheEntry entry) || entry.generation != generation || entry.neighborIds == null)
            {
                rebuildIdsScratch.Clear();
                Raid_ReinforcementLogic.ScanReinforcementIdsLive(primary, enemy, radius, lookup, manager, rebuildIdsScratch);
                entry = new CacheEntry
                {
                    generation = generation,
                    neighborIds = new List<int>(rebuildIdsScratch)
                };
                entries[key] = entry;
            }

            ResolveIdsToObjects(entry.neighborIds, primary, enemy, into);
        }

        private static long MakeKey(int primaryId, int radiusKey, int enemyFactionLoadId)
        {
            unchecked
            {
                long h = primaryId;
                h = (h * 397) ^ radiusKey;
                h = (h * 397) ^ enemyFactionLoadId;
                return h;
            }
        }

        private static void EnsureIdIndex()
        {
            if (idIndexGeneration == generation) return;
            idIndex.Clear();
            List<WorldObject> list = Find.WorldObjects?.AllWorldObjects;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    WorldObject wo = list[i];
                    if (wo == null) continue;
                    idIndex[wo.ID] = wo;
                }
            }
            idIndexGeneration = generation;
        }

        private static void ResolveIdsToObjects(List<int> ids, WorldObject primary, WorldObject enemy, List<WorldObject> into)
        {
            if (ids == null || ids.Count == 0) return;

            EnsureIdIndex();
            for (int i = 0; i < ids.Count; i++)
            {
                int id = ids[i];
                if (!idIndex.TryGetValue(id, out WorldObject wo) || wo == null)
                {
                    idIndexGeneration = -1;
                    EnsureIdIndex();
                    if (!idIndex.TryGetValue(id, out wo) || wo == null)
                    {
                        ResolveIdsToObjectsFullScan(ids, primary, enemy, into);
                        return;
                    }
                }
                if (wo.Destroyed || !wo.Spawned) continue;
                if (wo == primary || wo == enemy) continue;
                into.Add(wo);
            }
        }

        private static void ResolveIdsToObjectsFullScan(List<int> ids, WorldObject primary, WorldObject enemy, List<WorldObject> into)
        {
            into.Clear();
            resolveWantScratch.Clear();
            for (int i = 0; i < ids.Count; i++)
                resolveWantScratch.Add(ids[i]);

            List<WorldObject> list = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < list.Count; i++)
            {
                WorldObject wo = list[i];
                if (wo == null || !resolveWantScratch.Contains(wo.ID)) continue;
                if (wo.Destroyed || !wo.Spawned) continue;
                if (wo == primary || wo == enemy) continue;
                into.Add(wo);
            }
        }
    }
}
