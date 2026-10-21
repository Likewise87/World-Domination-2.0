using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Player-bound WD raid launch flags: ambush eligibility coin-flip, raid-cap stamp/unstamp, contact for drought.
    /// </summary>
    public static class PlayerWdRaidDispatchUtil
    {
        public const float PlayerBoundRaidCanBeAmbushedChance = 0.5f;

        public static bool IsPlayerBoundRaidMission(TravelerMission mission) =>
            mission == TravelerMission.Raid
            || mission == TravelerMission.RaidDropPod
            || mission == TravelerMission.RaidGravship
            || mission == TravelerMission.DesperationRally;

        public static bool IsPlayerBoundRaidTarget(WorldObject target)
        {
            if (target == null) return false;
            if (target.Faction?.IsPlayer != true) return false;
            if (target is Settlement) return true;
            if (target is WorldObject_WD_Outpost) return true;
            return false;
        }

        /// <summary>
        /// Abort hostile travelers aimed at player colonies/outposts on non-surface layers
        /// (orbit/space). Cross-layer pathing cannot reach them.
        /// </summary>
        public static void AbortTravelersTargetingNonSurfacePlayerSites()
        {
            IReadOnlyList<WorldObject_Traveler> live = WorldObject_Traveler.LiveTravelers;
            if (live == null || live.Count == 0) return;

            for (int i = live.Count - 1; i >= 0; i--)
            {
                WorldObject_Traveler t = live[i];
                if (t == null || t.Destroyed) continue;
                if (!ShouldAbortForNonSurfacePlayerTarget(t)) continue;

                WorldObject target = t.targetObject;
                TravelerEndpointUtility.AbortTraveler(
                    t,
                    $"[TSA WD] Aborted {t.mission} at non-surface player site '{target?.LabelCap ?? "?"}' (unreachable across planet layers).",
                    refundCargo: true);
            }
        }

        private static bool ShouldAbortForNonSurfacePlayerTarget(WorldObject_Traveler traveler)
        {
            WorldObject target = traveler.targetObject;
            if (!IsPlayerBoundRaidTarget(target)) return false;
            if (PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(target)) return false;

            if (IsPlayerBoundRaidMission(traveler.mission)) return true;
            if (WorldObject_Traveler.IsRaidMission(traveler.mission)) return true;
            // Pack-up / Vanguard mass relocation can also pick CollectDistinctPlayerTargets.
            if (traveler.mission == TravelerMission.MassRelocation) return true;
            return false;
        }

        /// <summary>Roll canBeAmbushed and stamp the global player raid cap. Call once when the raid is committed.</summary>
        public static void ApplyOnPlayerBoundLaunch(WorldObject_Traveler traveler, WorldComponent_SpreadManager manager)
        {
            if (traveler == null) return;
            traveler.canBeAmbushed = Rand.Chance(PlayerBoundRaidCanBeAmbushedChance);
            if (manager != null)
                traveler.playerWdRaidCapStampTick = manager.RecordPlayerWdRaidLaunch();
        }

        /// <summary>Raid reached the player — stop unstamp-on-death and reset drought quiet timer.</summary>
        public static void NotifyPlayerContact(WorldObject_Traveler traveler)
        {
            if (traveler == null) return;
            traveler.playerRaidContacted = true;
            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.RecordPlayerWdRaidContact();
        }

        /// <summary>Attribute a combat wipe to <paramref name="killer"/> (player or NPC) before Destroy.</summary>
        public static void MarkDestroyedBy(WorldObject_Traveler traveler, Faction killer)
        {
            if (traveler == null || killer == null) return;
            traveler.destroyedByFaction = killer;
        }

        /// <summary>Attribute a wipe to environment (attrition expire, pollution) before Destroy/Abort.</summary>
        public static void MarkDestroyedByEnvironment(WorldObject_Traveler traveler)
        {
            if (traveler == null) return;
            traveler.destroyedByEnvironment = true;
        }

        /// <summary>
        /// Unstamp when mid-travel death is another NPC faction or the environment.
        /// Player kills and soft cancels leave attribution unset / player-marked → keep stamp.
        /// </summary>
        public static bool ShouldUnrecordRaidCap(WorldObject_Traveler traveler)
        {
            if (traveler == null) return false;
            if (traveler.destroyedByEnvironment) return true;
            Faction killer = traveler.destroyedByFaction;
            if (killer == null) return false;
            if (killer.IsPlayer) return false;
            if (traveler.Faction != null && killer == traveler.Faction) return false;
            return true;
        }

        /// <summary>
        /// Mid-travel death before contact: unstamp raid cap and optional letter only for NPC/env kills.
        /// Call from traveler Destroy while still alive (!Destroyed yet).
        /// </summary>
        public static void NotifyDestroyedBeforeContact(WorldObject_Traveler traveler)
        {
            if (traveler == null) return;
            if (!IsPlayerBoundRaidMission(traveler.mission)) return;
            if (traveler.playerRaidContacted) return;
            if (!IsPlayerBoundRaidTarget(traveler.targetObject)) return;
            if (!ShouldUnrecordRaidCap(traveler)) return;

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (traveler.playerWdRaidCapStampTick >= 0)
            {
                manager?.UnrecordPlayerWdRaidLaunch(traveler.playerWdRaidCapStampTick);
                traveler.playerWdRaidCapStampTick = -1;
            }

            var seth = WorldDominationMod.settings;
            if (seth == null || !seth.notifyRaidPartyDestroyed) return;
            if (Find.LetterStack == null) return;

            string attacker = traveler.Faction?.Name ?? traveler.originObject?.LabelCap ?? "?";
            string target = traveler.targetObject?.LabelCap ?? "?";
            Find.LetterStack.ReceiveLetter(
                "TSA_WD_Letter_RaidPartyDestroyed_Label".Translate(),
                "TSA_WD_Letter_RaidPartyDestroyed_Text".Translate(attacker, target),
                LetterDefOf.NeutralEvent,
                traveler.originObject ?? traveler.targetObject);
        }
    }
}
