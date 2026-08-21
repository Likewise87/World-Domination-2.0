using System;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Fallback heuristics for orbit-like maps and defs. WD scope uses <see cref="WorldActions_Utils.IsWdSurfaceTile"/> first.</summary>
    public static class SpaceMapGuard
    {
        /// <summary>Core game uses RimWorld.OrbitLayer for space; avoid hard reference via type name.</summary>
        public static bool IsOrbitLayer(PlanetLayer layer)
        {
            return layer != null && layer.GetType().Name == "OrbitLayer";
        }

        public static bool IsSpaceLike(Map map)
        {
            if (map == null) return false;
            string biome = map.Biome?.defName ?? string.Empty;
            if (biome.IndexOf("space", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (biome.IndexOf("asteroid", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (biome.IndexOf("vacuum", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            string parent = map.Parent?.def?.defName ?? string.Empty;
            if (parent.IndexOf("Space", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (parent.IndexOf("Asteroid", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (parent.IndexOf("Odyssey", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (parent.IndexOf("Ship", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (parent.IndexOf("Grav", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            if (map.Tile < 0) return true;
            return false;
        }

        public static bool IsSpaceLike(WorldObject obj)
        {
            if (obj == null) return false;

            if (obj is Settlement s && s.Map != null) return IsSpaceLike(s.Map);

            string defName = obj.def?.defName ?? string.Empty;
            bool isSpaceDef = defName.IndexOf("Space", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              defName.IndexOf("Asteroid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              defName.IndexOf("Odyssey", StringComparison.OrdinalIgnoreCase) >= 0;

            return isSpaceDef || obj.Tile < 0;
        }
    }
}
