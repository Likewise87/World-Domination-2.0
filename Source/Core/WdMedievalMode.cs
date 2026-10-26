using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// SSoT for Medieval mode: content <c>minTechLevel</c> filter, drop-pod UI/force-land,
    /// and floors for NPC drop-pod / mortar-AA combat tech gates.
    /// </summary>
    public static class WdMedievalMode
    {
        public const TechLevel CapTechLevel = TechLevel.Medieval;
        public const TechLevel FloorCombatTechLevel = TechLevel.Industrial;

        public static bool Active =>
            WorldDominationMod.settings?.wdMedievalMode == true;

        /// <summary>When medieval mode is off, always true. Unset/default = Neolithic = allowed.</summary>
        public static bool IsAllowed(TechLevel minTechLevel)
        {
            if (!Active) return true;
            TechLevel min = minTechLevel == TechLevel.Undefined ? TechLevel.Neolithic : minTechLevel;
            return min <= CapTechLevel;
        }

        public static bool IsOutpostAllowed(WorldObjectDef def)
        {
            if (def == null) return false;
            var ext = def.GetModExtension<OutpostDefExtension>();
            return IsAllowed(ext?.minTechLevel ?? TechLevel.Undefined);
        }

        public static bool IsUpgradeAllowed(OutpostUpgradeDef def)
        {
            if (def == null) return false;
            return IsAllowed(def.minTechLevel);
        }

        public static bool IsAdvancedBuildingAllowed(AdvancedBuildingDef def)
        {
            if (def == null) return false;
            return IsAllowed(def.minTechLevel);
        }

        public static bool IsAtTurretAllowed(AtTurretTier tier)
        {
            WdWorldBuildCostExtension ext = ColonyWorldBuildRequirements.GetAtTurretBuildExtension(tier);
            return IsAllowed(ext?.minTechLevel ?? TechLevel.Undefined);
        }

        public static bool IsAtTurretAllowed(WorldObjectDef def)
        {
            if (def == null) return false;
            var ext = def.GetModExtension<WdWorldBuildCostExtension>();
            return IsAllowed(ext?.minTechLevel ?? TechLevel.Undefined);
        }

        /// <summary>Player land-vs-pod UI and launches. False when medieval mode is on.</summary>
        public static bool PlayerDropPodsAllowed => !Active;

        public static TechLevel EffectiveDropPodRaidMinTech(WorldDominationSettings seth)
        {
            TechLevel configured = seth?.dropPodRaidMinTechLevel
                ?? WorldDominationSettings.DefDropPodRaidMinTechLevel;
            if (!Active) return configured;
            return configured > FloorCombatTechLevel ? configured : FloorCombatTechLevel;
        }

        public static TechLevel EffectiveNpcArtilleryMinTech(WorldDominationSettings seth)
        {
            TechLevel configured = seth?.npcT4MortarMinTechLevel
                ?? WorldDominationSettings.DefNpcT4MortarMinTechLevel;
            if (!Active) return configured;
            return configured > FloorCombatTechLevel ? configured : FloorCombatTechLevel;
        }

        public static string MedievalBlockedReason =>
            "TSA_WD_Medieval_Blocked".Translate();
    }
}
