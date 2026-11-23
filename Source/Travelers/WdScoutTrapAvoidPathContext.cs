using System;
using RimWorld;
using UnityEngine;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Scoped FindPath bias for hostile spike-trap tiles. Scout / nimble scouts use the scout
    /// penalty; other WD travelers and player caravans use the caravan trap penalty. Pathfinding
    /// only; does not make traps impassable.
    /// </summary>
    public static class WdScoutTrapAvoidPathContext
    {
        [ThreadStatic] private static int depth;
        [ThreadStatic] private static Faction faction;
        [ThreadStatic] private static float penalty;

        public static bool Active => depth > 0;

        public static Faction MoverFaction => faction;

        /// <summary>Flat difficulty active for this FindPath (pathfinding bias only).</summary>
        public static float HostileTrapFlatPenalty => depth > 0 ? penalty : 0f;

        public static bool IsScoutStyle(WorldObject_Traveler traveler) =>
            traveler != null
            && (traveler.mission == TravelerMission.Scout || traveler.isNimbleScout);

        /// <summary>Legacy name: scout / nimble scout still always scope when launching path.</summary>
        public static bool ShouldAvoidFor(WorldObject_Traveler traveler) => IsScoutStyle(traveler);

        public static Scope TryActivateForTraveler(WorldObject_Traveler traveler)
        {
            if (traveler?.Faction == null) return null;
            var s = WorldDominationMod.settings;
            if (s == null) return null;
            bool player = traveler.Faction.IsPlayer;
            if (player)
            {
                if (!s.playerCaravansAvoidHostileSettlements) return null;
            }
            else if (!s.npcCaravansAvoidHostileSettlements)
                return null;

            float p = IsScoutStyle(traveler)
                ? s.scoutTrapPathAvoidPenalty
                : s.caravanTrapPathAvoidPenalty;
            p = Mathf.Clamp(p, 0f, 10f);
            if (p <= 0f && !IsScoutStyle(traveler)) return null;
            return Activate(traveler.Faction, p);
        }

        public static Scope TryActivateForPlayerCaravan(Faction faction)
        {
            if (faction == null || !faction.IsPlayer) return null;
            var s = WorldDominationMod.settings;
            if (s == null || !s.playerPawnCaravanPathfindingAdjustments) return null;
            float p = Mathf.Clamp(s.caravanTrapPathAvoidPenalty, 0f, 10f);
            if (p <= 0f) return null;
            return Activate(faction, p);
        }

        public static Scope Activate(Faction moverFaction, float flatPenalty) =>
            new Scope(moverFaction, Mathf.Clamp(flatPenalty, 0f, 10f));

        /// <summary>Scout path dry-run helper (uses scout setting).</summary>
        public static Scope Activate(Faction moverFaction)
        {
            var s = WorldDominationMod.settings;
            float p = s != null ? s.scoutTrapPathAvoidPenalty : WorldDominationSettings.DefScoutTrapPathAvoidPenalty;
            return Activate(moverFaction, p);
        }

        public sealed class Scope : IDisposable
        {
            private readonly Faction previousFaction;
            private readonly float previousPenalty;
            private bool disposed;

            public Scope(Faction moverFaction, float flatPenalty)
            {
                previousFaction = faction;
                previousPenalty = penalty;
                faction = moverFaction;
                penalty = flatPenalty;
                depth++;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                depth = Math.Max(0, depth - 1);
                if (depth == 0)
                {
                    faction = null;
                    penalty = 0f;
                }
                else
                {
                    faction = previousFaction;
                    penalty = previousPenalty;
                }
            }
        }
    }
}
