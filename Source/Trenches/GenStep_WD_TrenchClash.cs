using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Linear trench corridor for outpost/colony trench intercepts: dirt ditch, berms, sandbag/barricade rails.
    /// Optionally places Oomby <c>Tr_TrenchDirt</c> when that mod is loaded.
    /// </summary>
    public class GenStep_WD_TrenchClash : GenStep
    {
        public const int CorridorWidth = 7;
        public override int SeedPart => 913847221;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (map == null) return;

            TerrainDef soil = TerrainDefOf.Soil ?? TerrainDefOf.Sand;
            TerrainDef packed = DefDatabase<TerrainDef>.GetNamedSilentFail("PackedDirt") ?? soil;
            IntVec3 center = map.Center;
            var layout = map.GetComponent<MapComponent_WdTrenchClashLayout>();
            if (layout == null)
            {
                layout = new MapComponent_WdTrenchClashLayout(map);
                map.components.Add(layout);
            }

            // Horizontal corridor through map center.
            int halfW = CorridorWidth / 2;
            var corridor = new List<IntVec3>();
            for (int x = 0; x < map.Size.x; x++)
            {
                for (int z = center.z - halfW; z <= center.z + halfW; z++)
                {
                    IntVec3 c = new IntVec3(x, 0, z);
                    if (!c.InBounds(map)) continue;
                    bool edge = z == center.z - halfW || z == center.z + halfW;
                    map.terrainGrid.SetTerrain(c, edge ? packed : soil);
                    corridor.Add(c);
                }
            }

            layout.CorridorCells = corridor;
            layout.PlayerEnd = new IntVec3(SpawnInset(map), 0, center.z);
            layout.HostileEnd = new IntVec3(map.Size.x - 1 - SpawnInset(map), 0, center.z);

            PlaceRailFortifications(map, center.z - halfW);
            PlaceRailFortifications(map, center.z + halfW);
            TryPlaceOombyTrenches(map, center.z);
        }

        private static int SpawnInset(Map map) => Mathf.Clamp(map.Size.x / 8, 4, 12);

        private static void PlaceRailFortifications(Map map, int z)
        {
            ThingDef sandbags = ThingDefOf.Sandbags
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("Sandbags");
            ThingDef barricade = ThingDefOf.Barricade
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("Barricade");
            for (int x = 2; x < map.Size.x - 2; x++)
            {
                if (x % 5 == 0) continue; // gaps
                IntVec3 c = new IntVec3(x, 0, z);
                if (!c.InBounds(map) || c.GetFirstBuilding(map) != null) continue;
                ThingDef def = (x % 3 == 0 && barricade != null) ? barricade : sandbags;
                if (def == null) continue;
                try
                {
                    GenSpawn.Spawn(ThingMaker.MakeThing(def), c, map);
                }
                catch
                {
                    // ignore place failures
                }
            }
        }

        private static void TryPlaceOombyTrenches(Map map, int centerZ)
        {
            ThingDef trench = DefDatabase<ThingDef>.GetNamedSilentFail("Tr_TrenchDirt");
            if (trench == null) return;
            for (int x = 4; x < map.Size.x - 4; x += 2)
            {
                IntVec3 c = new IntVec3(x, 0, centerZ);
                if (!c.InBounds(map) || c.GetFirstBuilding(map) != null) continue;
                try
                {
                    GenSpawn.Spawn(ThingMaker.MakeThing(trench), c, map);
                }
                catch
                {
                }
            }
        }
    }

    public class MapComponent_WdTrenchClashLayout : MapComponent
    {
        public List<IntVec3> CorridorCells = new List<IntVec3>();
        public IntVec3 PlayerEnd;
        public IntVec3 HostileEnd;

        public MapComponent_WdTrenchClashLayout(Map map) : base(map) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref CorridorCells, "corridorCells", LookMode.Value);
            Scribe_Values.Look(ref PlayerEnd, "playerEnd");
            Scribe_Values.Look(ref HostileEnd, "hostileEnd");
            if (CorridorCells == null) CorridorCells = new List<IntVec3>();
        }
    }
}
