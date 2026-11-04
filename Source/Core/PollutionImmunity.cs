using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Biotech waster pirates thrive in pollution.
    /// Damage/path skips when the setting is on; NPC auto-decontam always skips PirateWaster.
    /// </summary>
    public static class PollutionImmunity
    {
        public const string WasterPirateFactionDefName = "PirateWaster";

        /// <summary>Exact Biotech waster-pirate faction id (not xenotype heuristics).</summary>
        public static bool IsWasterPirateFaction(Faction faction)
            => faction?.def != null && faction.def.defName == WasterPirateFactionDefName;

        public static bool IsWasterPirateFaction(WorldObject worldObject)
            => worldObject != null && IsWasterPirateFaction(worldObject.Faction);

        /// <summary>Skip traveler exit / site daily pollution damage when the Experimental setting is on.</summary>
        public static bool IsImmune(Faction faction)
        {
            if (!IsWasterPirateFaction(faction))
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
