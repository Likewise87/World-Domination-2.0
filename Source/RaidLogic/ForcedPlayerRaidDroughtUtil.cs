using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// After a quiet stretch with no player raid contact, force one raid from the strongest
    /// eligible hostile in attack range (else a strong hostile closest past that range).
    /// </summary>
    public static class ForcedPlayerRaidDroughtUtil
    {
        private const int CheckIntervalTicks = 2500;
        private const float MinRaidStrength = RaidLaunchGate.MinColonyRaidEffectiveAtt;

        private static int lastCheckTick = -99999;

        public static void Tick(WorldComponent_SpreadManager manager)
        {
            if (manager == null) return;
            var seth = WorldDominationMod.settings;
            if (seth == null) return;

            int now = Find.TickManager.TicksGame;
            if (now - lastCheckTick < CheckIntervalTicks) return;
            lastCheckTick = now;

            PlayerWdRaidDispatchUtil.AbortTravelersTargetingNonSurfacePlayerSites();

            if (!seth.forceRaidDroughtEnabled) return;
            if (HasLivePlayerBoundRaid()) return;
            if (!manager.CanAcceptPlayerWdRaid(seth)) return;

            float quietDays = GetQuietDays(manager, now);
            int quietThreshold = Mathf.Clamp(seth.forceRaidDroughtQuietDays, 1, 30);
            if (quietDays < quietThreshold) return;

            Settlement colony = FindPlayerMapColony();
            if (colony == null) return;

            if (!TryPickHostileAttacker(colony, manager, seth, out Settlement attacker, out CompViralSpread attComp, out bool beyondRange))
                return;

            WorldActions_Raid.LaunchForcedRaidAtPlayerColony(attacker, attComp, colony, manager, beyondRange);
        }

        private static float GetQuietDays(WorldComponent_SpreadManager manager, int now)
        {
            if (manager.lastPlayerWdRaidContactTick < 0)
                return now / (float)GenDate.TicksPerDay;
            return (now - manager.lastPlayerWdRaidContactTick) / (float)GenDate.TicksPerDay;
        }

        private static bool HasLivePlayerBoundRaid()
        {
            IReadOnlyList<WorldObject_Traveler> live = WorldObject_Traveler.LiveTravelers;
            for (int i = 0; i < live.Count; i++)
            {
                WorldObject_Traveler t = live[i];
                if (t == null || t.Destroyed) continue;
                if (!PlayerWdRaidDispatchUtil.IsPlayerBoundRaidMission(t.mission)) continue;
                if (!PlayerWdRaidDispatchUtil.IsPlayerBoundRaidTarget(t.targetObject)) continue;
                return true;
            }
            return false;
        }

        private static Settlement FindPlayerMapColony()
        {
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null) return null;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s == null || s.Destroyed || !s.Spawned) continue;
                if (s.Faction?.IsPlayer != true || !s.HasMap) continue;
                if (s is WorldObject_WD_Outpost) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;
                return s;
            }
            return null;
        }

        /// <summary>
        /// Prefer max available raid strength among hostiles in their attack range.
        /// If none in range: among OOR hostiles, prefer closest past their range edge, then strongest.
        /// </summary>
        private static bool TryPickHostileAttacker(
            Settlement colony,
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth,
            out Settlement attacker,
            out CompViralSpread attComp,
            out bool beyondRange)
        {
            attacker = null;
            attComp = null;
            beyondRange = false;

            Settlement bestInRange = null;
            CompViralSpread bestInRangeComp = null;
            float bestInRangeStr = float.MinValue;
            float bestInRangeDist = float.MaxValue;

            Settlement bestOor = null;
            CompViralSpread bestOorComp = null;
            float bestOorOvershoot = float.MaxValue;
            float bestOorStr = float.MinValue;

            List<Settlement> settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s == null || s.Destroyed || !s.Spawned) continue;
                if (s.Faction == null || s.Faction.IsPlayer || s.Faction.def.hidden) continue;
                if (!WorldActions_Utils.IsWdParticipant(s.Faction)) continue;
                if (!WorldActions_Utils.SafeHostileTo(s.Faction, Faction.OfPlayer)) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;
                var comp = s.GetComponent<CompViralSpread>();
                if (comp == null) continue;
                if (manager.IsPlayerBribeCeasefireActive(s.Faction)) continue;

                float strength = WorldActions_Utils.GetAvailableRaidStrength(comp, seth);
                if (strength < MinRaidStrength) continue;

                float attackRange = SettlementAttackRangeUtil.GetNpcSettlementAttackRangeWithZeal(s, seth, manager);
                float dist = WorldActions_Utils.GetDistance(s.Tile, colony.Tile, manager);

                if (dist <= attackRange + 0.01f)
                {
                    if (strength > bestInRangeStr + 0.01f
                        || (Mathf.Abs(strength - bestInRangeStr) <= 0.01f && dist < bestInRangeDist))
                    {
                        bestInRangeStr = strength;
                        bestInRangeDist = dist;
                        bestInRange = s;
                        bestInRangeComp = comp;
                    }
                    continue;
                }

                if (!seth.forceRaidDroughtAllowBeyondRange) continue;

                float overshoot = dist - attackRange;
                if (overshoot < bestOorOvershoot - 0.01f
                    || (Mathf.Abs(overshoot - bestOorOvershoot) <= 0.01f && strength > bestOorStr + 0.01f))
                {
                    bestOorOvershoot = overshoot;
                    bestOorStr = strength;
                    bestOor = s;
                    bestOorComp = comp;
                }
            }

            if (bestInRange != null && bestInRangeComp != null)
            {
                attacker = bestInRange;
                attComp = bestInRangeComp;
                beyondRange = false;
                return true;
            }

            if (seth.forceRaidDroughtAllowBeyondRange && bestOor != null && bestOorComp != null)
            {
                attacker = bestOor;
                attComp = bestOorComp;
                beyondRange = true;
                return true;
            }

            return false;
        }
    }
}
