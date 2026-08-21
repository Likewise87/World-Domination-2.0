using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Shared checks and refund/abort paths when traveler origin or target endpoints are missing or destroyed.</summary>
    public static class TravelerEndpointUtility
    {
        public static bool IsLiveEndpoint(WorldObject wo) => wo != null && !wo.Destroyed;

        /// <summary>Attacker context for raid resolution: live origin when available, otherwise null (use <see cref="WorldObject_Traveler.Faction"/>).</summary>
        public static WorldObject GetRaidAttackerContext(WorldObject_Traveler traveler)
        {
            if (traveler == null) return null;
            return IsLiveEndpoint(traveler.originObject) ? traveler.originObject : null;
        }

        public static Faction GetRaidAttackerFaction(WorldObject_Traveler traveler)
        {
            if (traveler == null) return null;
            WorldObject origin = traveler.originObject;
            if (IsLiveEndpoint(origin) && origin.Faction != null)
                return origin.Faction;
            return traveler.Faction;
        }

        /// <summary>
        /// Returns committed strength to live contributors (DNA) or a live origin.
        /// Skips destroyed endpoints; unrefundable strength is absorbed silently.
        /// </summary>
        public static void RefundTravelerStrength(WorldObject_Traveler traveler, float survivalMultiplier)
        {
            if (traveler == null || survivalMultiplier <= 0f || traveler.travelerStrength <= 0f) return;

            if (traveler.mission == TravelerMission.RapidResponseIntercept)
            {
                RefundRapidResponseStrength(traveler, traveler.travelerStrength * survivalMultiplier);
                return;
            }

            if (UsesContributionDnaRefund(traveler))
            {
                Raid_Simulated.RefundStrength(traveler, survivalMultiplier);
                return;
            }

            if (IsLiveEndpoint(traveler.originObject))
            {
                float amount = traveler.travelerStrength * survivalMultiplier;
                traveler.originObject.GetComponent<CompViralSpread>()?.AddStrength(amount);
            }
        }

        private static bool UsesContributionDnaRefund(WorldObject_Traveler traveler)
        {
            return traveler.contributionFactors != null && traveler.contributionFactors.Count > 0;
        }

        /// <summary>
        /// Returns unspent rapid-response strength to the origin outpost.
        /// Call with surviving caravan strength after a clash, or full strength on abort.
        /// </summary>
        public static void RefundRapidResponseStrength(WorldObject_Traveler traveler, float amount)
        {
            if (traveler == null || traveler.rapidResponseStrengthRefunded) return;
            if (amount > 0f && IsLiveEndpoint(traveler.originObject))
                traveler.originObject.GetComponent<CompViralSpread>()?.AddStrengthNoTierUpgrade(amount);
            traveler.rapidResponseStrengthRefunded = true;
        }

        /// <summary>Refund strength, release raid goodwill, log, and destroy the traveler.</summary>
        public static void AbortTraveler(WorldObject_Traveler traveler, string reason, WorldComponent_SpreadManager manager = null)
        {
            if (traveler == null || traveler.Destroyed) return;

            manager ??= Find.World?.GetComponent<WorldComponent_SpreadManager>();

            RefundTravelerStrength(traveler, 1f);
            if (WorldObject_Traveler.IsRaidMission(traveler.mission))
                Raid_Simulated.RefundAlliedRaidOrderGoodwill(traveler);

            if (traveler.mission == TravelerMission.RapidResponseIntercept)
                traveler.rapidResponseStrengthRefunded = true;

            if (!string.IsNullOrEmpty(reason))
                manager?.AddLog(new SpreadLogEntry(reason, traveler, traveler.originObject));

            // Combat wipes (spike traps, pollution) zero strength before abort; keep destroyed-caravan FX.
            // Peaceful abort / refund still has strength: skip the wipe overlay.
            if (traveler.travelerStrength > 0.01f)
                traveler.suppressDestroyedWorldFx = true;

            traveler.pather?.StopDead();
            traveler.Destroy();
        }
    }
}
