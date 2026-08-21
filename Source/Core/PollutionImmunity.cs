using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Biotech waster pirates thrive in pollution; WD pollution combat skips them when the setting is on.</summary>
    public static class PollutionImmunity
    {
        public const string WasterPirateFactionDefName = "PirateWaster";

        public static bool IsImmune(Faction faction)
        {
            if (faction?.def == null || faction.def.defName != WasterPirateFactionDefName)
                return false;
            return WorldDominationMod.settings?.wasterPollutionImmunityEnabled
                ?? WorldDominationSettings.DefWasterPollutionImmunityEnabled;
        }

        public static bool IsImmune(WorldObject worldObject)
        {
            return worldObject != null && IsImmune(worldObject.Faction);
        }
    }
}
