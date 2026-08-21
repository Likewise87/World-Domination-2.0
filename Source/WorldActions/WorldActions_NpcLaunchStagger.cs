using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Cosmetic stagger for multi-caravan NPC fortify / road launches (8–20s between departures).
    /// Not scribed; mid-gap save drops remaining queued spawns (strength already reserved is refunded on fail only).
    /// </summary>
    internal static class WorldActions_NpcLaunchStagger
    {
        private const int GapMinTicks = 480; // 8s at 1x
        private const int GapMaxTicks = 1200; // 20s at 1x

        private struct PendingFortify
        {
            public int dueTick;
            public Settlement origin;
            public int destTile;
            public float cost;
            public bool placeTrap;
            public SpikeTrapKind trapKind;
            public RoadBlockKind blockKind;
        }

        private struct PendingRoad
        {
            public int dueTick;
            public WorldObject origin;
            public int destTile;
            public SettlementTier tier;
            public List<int> pathTilesDestFirst;
            public float cost;
        }

        private static readonly List<PendingFortify> fortifyQueue = new List<PendingFortify>();
        private static readonly List<PendingRoad> roadQueue = new List<PendingRoad>();

        public static int NextGapTicks() => Rand.RangeInclusive(GapMinTicks, GapMaxTicks);

        public static void EnqueueFortify(
            int dueTick,
            Settlement origin,
            int destTile,
            float cost,
            bool placeTrap,
            SpikeTrapKind trapKind,
            RoadBlockKind blockKind)
        {
            fortifyQueue.Add(new PendingFortify
            {
                dueTick = dueTick,
                origin = origin,
                destTile = destTile,
                cost = cost,
                placeTrap = placeTrap,
                trapKind = trapKind,
                blockKind = blockKind
            });
        }

        public static void EnqueueRoad(
            int dueTick,
            WorldObject origin,
            int destTile,
            SettlementTier tier,
            List<int> pathTilesDestFirst,
            float cost)
        {
            List<int> pathCopy = null;
            if (pathTilesDestFirst != null && pathTilesDestFirst.Count > 0)
                pathCopy = new List<int>(pathTilesDestFirst);

            roadQueue.Add(new PendingRoad
            {
                dueTick = dueTick,
                origin = origin,
                destTile = destTile,
                tier = tier,
                pathTilesDestFirst = pathCopy,
                cost = cost
            });
        }

        public static void Tick()
        {
            if (fortifyQueue.Count == 0 && roadQueue.Count == 0) return;
            int now = Find.TickManager.TicksGame;

            for (int i = fortifyQueue.Count - 1; i >= 0; i--)
            {
                PendingFortify p = fortifyQueue[i];
                if (p.dueTick > now) continue;
                fortifyQueue.RemoveAt(i);

                if (p.origin == null || p.origin.Destroyed)
                    continue;

                if (!WorldActions_NpcFortify.SpawnFortifyTravelerPrepaid(
                        p.origin, p.destTile, p.cost, p.placeTrap, p.trapKind, p.blockKind))
                {
                    RefundStrength(p.origin, p.cost);
                    continue;
                }

                Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry(
                    "TSA_WD_Log_Fortify".Translate(p.origin.LabelCap, p.destTile.ToString()),
                    p.origin,
                    p.destTile));
            }

            for (int i = roadQueue.Count - 1; i >= 0; i--)
            {
                PendingRoad p = roadQueue[i];
                if (p.dueTick > now) continue;
                roadQueue.RemoveAt(i);

                if (p.origin == null || p.origin.Destroyed)
                    continue;

                if (!WorldActions_Roads.SpawnRoadTravelerPrepaid(
                        p.origin, p.destTile, p.tier, p.pathTilesDestFirst, p.cost))
                {
                    RefundStrength(p.origin, p.cost);
                }
            }
        }

        private static void RefundStrength(WorldObject origin, float cost)
        {
            var comp = origin?.GetComponent<CompViralSpread>();
            if (comp == null || cost <= 0f) return;
            comp.AddStrength(cost);
        }
    }
}
