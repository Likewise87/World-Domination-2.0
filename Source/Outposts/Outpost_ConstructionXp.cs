using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Flat Construction event XP when an outpost-origin build traveler successfully completes work.</summary>
    public static class Outpost_ConstructionXp
    {
        public static float XpForRoadTier(SettlementTier tier)
        {
            if (tier >= SettlementTier.T3) return Outpost_OccupantProgression.EventXpBuildCompleteT3;
            if (tier == SettlementTier.T2) return Outpost_OccupantProgression.EventXpBuildCompleteT2;
            return Outpost_OccupantProgression.EventXpBuildCompleteT1;
        }

        public static float XpForRoadBlockKind(RoadBlockKind kind)
        {
            return XpForRoadTier(RoadBlockKindUtil.WorkBaselineTier(kind));
        }

        public static float XpForSpikeTrapKind(SpikeTrapKind kind)
        {
            return XpForRoadTier(SpikeTrapKindUtil.WorkBaselineTier(kind));
        }

        public static float XpForAtTurretTier(AtTurretTier tier)
        {
            switch (tier)
            {
                case AtTurretTier.Heavy:
                    return Outpost_OccupantProgression.EventXpBuildCompleteT3;
                case AtTurretTier.Medium:
                    return Outpost_OccupantProgression.EventXpBuildCompleteT2;
                default:
                    return Outpost_OccupantProgression.EventXpBuildCompleteT1;
            }
        }

        /// <summary>Grant Construction XP to the launching player outpost after successful build work.</summary>
        public static void TryGrant(WorldObject_Traveler traveler, float xpAmount)
        {
            if (traveler == null || xpAmount <= 0f) return;
            WorldObject_WD_Outpost outpost = Outpost_OccupantProgression.OutpostFromTravelerOrigin(traveler);
            if (outpost == null || outpost.Faction != Faction.OfPlayer) return;
            Outpost_OccupantProgression.ApplySkillXp(outpost, xpAmount, SkillDefOf.Construction);
        }
    }
}
