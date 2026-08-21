using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Pre-commit pollution attrition (B) and route-alter detection for WD ground raids.</summary>
    public static class PollutionPathMath
    {
        public const float HeavyRepathWeight = 3.5f;

        public struct Result
        {
            public float expectedLoss;
            public bool wouldGut;
            public bool routeAltered;
            public bool damageExpected;
        }

        /// <summary>True when remaining strength after expected loss is wiped or under 5% of committed.</summary>
        public static bool WouldGutStrength(float committedStrength, float expectedLoss)
        {
            if (committedStrength <= 0.01f) return expectedLoss > 0f;
            float remaining = committedStrength - expectedLoss;
            if (remaining <= 0.01f) return true;
            return remaining < committedStrength * 0.05f;
        }

        public static float SumExpectedExitDamage(IReadOnlyList<int> leftTileIds, WorldDominationSettings s)
        {
            if (s == null || leftTileIds == null || leftTileIds.Count == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < leftTileIds.Count; i++)
                sum += s.GetPollutionExitDamage(WorldTileProductivity.GetTilePollution01(leftTileIds[i]));
            return sum;
        }

        public static float SumExpectedExitDamageFromPath(WorldObject_Traveler traveler, WorldDominationSettings s)
        {
            if (traveler?.pather == null || s == null) return 0f;
            List<int> tiles = traveler.pather.CollectPathExitTileIds();
            return SumExpectedExitDamage(tiles, s);
        }

        /// <summary>
        /// Compare pollution-aware vs blind FindPath hop counts (or node sets) to detect route alteration.
        /// Cheap secondary FindPath only when A is on and we need the banner / log flag.
        /// </summary>
        public static bool DetectRouteAltered(PlanetTile start, PlanetTile dest, Faction faction)
        {
            if (!start.Valid || !dest.Valid || start == dest) return false;
            PlanetLayer layer = start.Layer;
            if (layer?.Pather == null) return false;

            WorldPath aware;
            using (WdPollutionPathContext.Activate(1f))
                aware = layer.Pather.FindPath(start, dest, null);
            WorldPath blind = layer.Pather.FindPath(start, dest, null);

            bool altered = false;
            try
            {
                if (aware == null || !aware.Found || blind == null || !blind.Found)
                    return false;
                altered = !PathNodesMatch(aware, blind);
            }
            finally
            {
                aware?.ReleaseToPool();
                blind?.ReleaseToPool();
            }
            return altered;
        }

        private static bool PathNodesMatch(WorldPath a, WorldPath b)
        {
            var na = a.NodesReversed;
            var nb = b.NodesReversed;
            if (na == null || nb == null || na.Count != nb.Count) return false;
            for (int i = 0; i < na.Count; i++)
            {
                if (na[i].tileId != nb[i].tileId) return false;
            }
            return true;
        }

        /// <summary>Preview for raid UI (no traveler). Uses one pollution-aware FindPath when A is on.</summary>
        public static Result EvaluatePreview(
            PlanetTile start,
            PlanetTile dest,
            float committedStrength,
            Faction faction,
            TravelerMission mission,
            WorldDominationSettings s,
            bool forceRouteCompare = true)
        {
            var result = new Result();
            if (s == null || !s.travelerPollutionDamageEnabled) return result;
            if (!TravelerPollutionDamage.MissionTakesPollutionDamage(mission, faction, s)) return result;
            if (PollutionImmunity.IsImmune(faction)) return result;
            if (!start.Valid || !dest.Valid) return result;

            PlanetLayer layer = start.Layer;
            if (layer?.Pather == null) return result;

            bool useA = s.pollutionPathCostEnabled;
            WorldPath path;
            if (useA)
            {
                using (WdPollutionPathContext.Activate(1f))
                    path = layer.Pather.FindPath(start, dest, null);
            }
            else
                path = layer.Pather.FindPath(start, dest, null);

            try
            {
                if (path == null || !path.Found) return result;
                var left = new List<int>();
                var nodes = path.NodesReversed;
                // NodesReversed is dest..start; exit damage applies when leaving each tile except dest.
                for (int i = nodes.Count - 1; i >= 1; i--)
                    left.Add(nodes[i].tileId);
                result.expectedLoss = SumExpectedExitDamage(left, s);
                result.damageExpected = result.expectedLoss > 0.01f;
                result.wouldGut = WouldGutStrength(committedStrength, result.expectedLoss);
            }
            finally
            {
                path?.ReleaseToPool();
            }

            if (forceRouteCompare && useA)
                result.routeAltered = DetectRouteAltered(start, dest, faction);

            return result;
        }

        /// <summary>After StartPath: sum B on the live path; optionally note High repath already applied by caller.</summary>
        public static Result EvaluateAfterStartPath(
            WorldObject_Traveler traveler,
            WorldDominationSettings s,
            bool routeAlteredHint = false)
        {
            var result = new Result();
            if (traveler == null || s == null || !s.travelerPollutionDamageEnabled) return result;
            if (!TravelerPollutionDamage.TakesPollutionDamage(traveler)) return result;

            result.expectedLoss = SumExpectedExitDamageFromPath(traveler, s);
            result.damageExpected = result.expectedLoss > 0.01f;
            result.wouldGut = WouldGutStrength(traveler.travelerStrength, result.expectedLoss);
            result.routeAltered = routeAlteredHint;
            return result;
        }
    }
}
