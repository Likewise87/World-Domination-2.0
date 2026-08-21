using System;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    [Flags]
    public enum RaidTargetMask
    {
        None      = 0,
        Player    = 1 << 0,
        Allies    = 1 << 1,
        OtherNpcs = 1 << 2,
    }

    /// <summary>Player mortar-outpost AA filters: which airborne kinds auto-flak may engage.</summary>
    [Flags]
    public enum AntiAirKindMask
    {
        None         = 0,
        MortarShells = 1 << 0,
        DropPods     = 1 << 1,
        All          = MortarShells | DropPods,
    }

    /// <summary>
    /// Mission types a defensive interceptor can be configured to target. Flags so defensive toggles
    /// can combine arbitrary subsets (e.g. Raider|Expansion), while "All" is the convenient default.
    /// Mapping from <see cref="TravelerMission"/> lives in <see cref="InterceptionMissionMaskUtils"/>.
    /// </summary>
    [Flags]
    public enum MissionMask
    {
        None      = 0,
        Raider    = 1 << 0,
        Expansion = 1 << 1,
        Road      = 1 << 2,
        Trader    = 1 << 3,
        Fortify   = 1 << 4,
        All       = Raider | Expansion | Road | Trader | Fortify
    }

    /// <summary>
    /// Anything that can intercept a hostile <see cref="WorldObject_Traveler"/> passing through its range
    /// (mortar outpost, tier-4 settlement, future physical interception caravans).
    /// Scanned centrally by <see cref="WorldComponent_InterceptionScheduler"/> — interceptors never loop
    /// the world themselves; they only answer "can I fire now" and "fire on this target".
    /// </summary>
    public interface IDefensiveInterceptor
    {
        /// <summary>World object backing this interceptor (for hostility/faction/destroyed checks).</summary>
        WorldObject Self { get; }

        /// <summary>Planet tile the interceptor shoots from; used for range checks.</summary>
        PlanetTile InterceptorTile { get; }

        /// <summary>Faction of the interceptor (never fires at allies/itself).</summary>
        Faction InterceptorFaction { get; }

        /// <summary>Max engagement range in tiles (from settings/def).</summary>
        float InterceptorRange { get; }

        /// <summary>Subset of mission types this interceptor wants to engage this tick.</summary>
        MissionMask InterceptorMissionMask { get; }

        /// <summary>Cheap cooldown / readiness check (shared mortar cooldown, defense toggle off, skill==0, etc.).</summary>
        bool InterceptorCanFireNow();

        /// <summary>Whether this interceptor may engage player-faction travelers (and, in fallback, player outposts).
        /// Player mortar outposts return false (they never shoot the player); NPC T4 settlements return true only
        /// while the late-game modifier is active.</summary>
        bool InterceptorCanTargetPlayer { get; }

        /// <summary>Commit a shot at the given traveler. Distance already resolved by scheduler; interceptor
        /// decides hit/miss and performs the actual fire (e.g. spawning a mortar shell traveler).</summary>
        void InterceptorFire(WorldObject_Traveler target, float approxTileDist);

        /// <summary>Called by the scheduler on a scan where no traveler target was found while the interceptor is ready.
        /// Used by NPC T4 settlements to fire at a nearby static target after an idle window; most implementers no-op.</summary>
        void InterceptorNoTargetFire();
    }

    public static class InterceptionMissionMaskUtils
    {
        /// <summary>Maps a traveler mission to its <see cref="MissionMask"/> bit; returns <see cref="MissionMask.None"/>
        /// for missions that should never be intercepted (outpost deliveries, upgrades, mortar shells themselves).</summary>
        public static MissionMask MaskFor(TravelerMission mission)
        {
            switch (mission)
            {
                case TravelerMission.Raid:         return MissionMask.Raider;
                case TravelerMission.RaidDropPod:  return MissionMask.Raider;
                case TravelerMission.RapidResponseDropPod: return MissionMask.Raider;
                case TravelerMission.DebugRaidTransit: return MissionMask.Raider;
                case TravelerMission.RapidResponseIntercept: return MissionMask.None;
                case TravelerMission.Expansion:    return MissionMask.Expansion;
                case TravelerMission.RoadBuilding: return MissionMask.Road;
                case TravelerMission.RoadBlock:    return MissionMask.Road;
                case TravelerMission.SpikeTrap:    return MissionMask.Road;
                case TravelerMission.Decontamination: return MissionMask.Road;
                case TravelerMission.NpcFortify: return MissionMask.Fortify;
                case TravelerMission.NpcAtTurret: return MissionMask.Fortify;
                case TravelerMission.Trader:       return MissionMask.Trader;
                default:                           return MissionMask.None;
            }
        }

        public static bool Matches(TravelerMission mission, MissionMask mask)
        {
            MissionMask bit = MaskFor(mission);
            return bit != MissionMask.None && (mask & bit) != 0;
        }
    }
}
