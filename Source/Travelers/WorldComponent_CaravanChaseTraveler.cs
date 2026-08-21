using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Tracks player caravans that are chasing travelers (Attack Caravan). Collision every tick; repath every 60 ticks staggered by caravan ID until within 2 tiles and not in a safe zone, then starts interception.</summary>
    public class WorldComponent_CaravanChaseTraveler : WorldComponent
    {
        private const float CollisionRadius = 0.5f;
        private const int RepathIntervalTicks = 60;

        /// <summary>While &gt; 0, Harmony patches must not drop chase (automated repath StartPath/StopDead).</summary>
        internal static int RepathSuppressionDepth;

        /// <summary>After <see cref="AddChase"/>, ignore chase-dropping in StopDead and the first StartPath postfix (vanilla StartPath calls StopDead first).</summary>
        internal static readonly HashSet<Caravan> PendingInitialChaseStartPath = new HashSet<Caravan>();

        private List<ChaseEntry> chases = new List<ChaseEntry>();
        private readonly Dictionary<int, int> lastKnownTravelerTile = new Dictionary<int, int>();

        public WorldComponent_CaravanChaseTraveler(World world) : base(world) { }

        public void AddChase(Caravan caravan, WorldObject_Traveler traveler)
        {
            if (caravan == null || traveler == null) return;
            if (chases == null) chases = new List<ChaseEntry>();
            chases.RemoveAll(e => e.caravan == caravan);
            chases.Add(new ChaseEntry { caravan = caravan, traveler = traveler });
            PendingInitialChaseStartPath.Add(caravan);
        }

        /// <summary>Stop auto-pursuit for this caravan (player cancel or new orders).</summary>
        public void RemoveChase(Caravan caravan)
        {
            if (caravan == null || chases == null) return;
            chases.RemoveAll(e => e.caravan == caravan);
            PendingInitialChaseStartPath.Remove(caravan);
            lastKnownTravelerTile.Remove(caravan.ID);
        }

        /// <summary>Cancel pursuit and halt the caravan on the spot.</summary>
        public void CancelChaseAndStopCaravan(Caravan caravan)
        {
            RemoveChase(caravan);
            caravan?.pather?.StopDead();
        }

        /// <summary>Returns the traveler this caravan is chasing, or null.</summary>
        public WorldObject_Traveler GetChaseTarget(Caravan caravan)
        {
            if (caravan == null || chases == null) return null;
            for (int i = 0; i < chases.Count; i++)
            {
                var e = chases[i];
                if (e.caravan == caravan && e.traveler != null && !e.traveler.Destroyed)
                    return e.traveler;
            }
            return null;
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            if (chases == null || chases.Count == 0) return;

            int tick = Find.TickManager.TicksGame;
            for (int i = chases.Count - 1; i >= 0; i--)
            {
                var entry = chases[i];
                if (entry.caravan == null || entry.caravan.Destroyed || entry.traveler == null || entry.traveler.Destroyed)
                {
                    chases.RemoveAt(i);
                    continue;
                }

                Caravan caravan = entry.caravan;
                WorldObject_Traveler traveler = entry.traveler;
                bool repathThisTick = (tick + caravan.ID) % RepathIntervalTicks == 0;

                // Approx gate + capped traversal: collision only matters within 2 tiles.
                if (Find.WorldGrid.ApproxDistanceInTiles(traveler.Tile, caravan.Tile) <= 2.5f
                    && Find.WorldGrid.TraversalDistanceBetween(traveler.Tile, caravan.Tile, true, 2) <= 2
                    && caravan.Faction != null && caravan.Faction.IsPlayer
                    && traveler.Faction != null && WorldActions_Utils.SafeHostileTo(traveler.Faction, caravan.Faction))
                {
                    bool travelerOnStatic = HasStaticObjectAt(traveler.Tile);
                    bool playerOnStatic = HasStaticObjectAt(caravan.Tile);
                    if (travelerOnStatic || playerOnStatic)
                    {
                        if (repathThisTick)
                            RepathCaravanTo(caravan, traveler.Tile);
                        continue;
                    }

                    float dist = UnityEngine.Vector3.Distance(traveler.DrawPos, caravan.DrawPos);
                    if (dist < CollisionRadius)
                    {
                        WD_CaravanClashUtility.StartInterceptionEncounter(caravan, traveler);
                        RemoveChase(caravan);
                        continue;
                    }
                }

                if (repathThisTick)
                {
                    int caravanId = caravan.ID;
                    int travTile = traveler.Tile;
                    if (!lastKnownTravelerTile.TryGetValue(caravanId, out int prevTile) || prevTile != travTile)
                    {
                        lastKnownTravelerTile[caravanId] = travTile;
                        RepathCaravanTo(caravan, travTile);
                    }
                }
            }
        }

        private static bool HasStaticObjectAt(int tile)
        {
            foreach (var wo in Find.WorldObjects.ObjectsAt(tile))
            {
                if (!(wo is WorldObject_Traveler) && !(wo is Caravan))
                    return true;
            }
            return false;
        }

        private static void RepathCaravanTo(Caravan caravan, int tileId)
        {
            if (caravan.Destroyed || caravan.pather == null) return;
            RepathSuppressionDepth++;
            try
            {
                caravan.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(tileId, caravan), null, false, false);
            }
            finally
            {
                RepathSuppressionDepth--;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref chases, "chases", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && chases == null)
                chases = new List<ChaseEntry>();
        }

        private class ChaseEntry : IExposable
        {
            public Caravan caravan;
            public WorldObject_Traveler traveler;

            public void ExposeData()
            {
                Scribe_References.Look(ref caravan, "caravan");
                Scribe_References.Look(ref traveler, "traveler");
            }
        }
    }
}
