using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Pollution-adapted factions (vanilla waster pirates, Det Buzzers, similar):
    /// damage/path skip when the Experimental setting is on; NPC auto-decontam always skips.
    /// </summary>
    public static class PollutionImmunity
    {
        public const string WasterPirateFactionDefName = "PirateWaster";
        public const string PollutionRushGeneDefName = "PollutionRush";

        private static readonly Dictionary<FactionDef, bool> adaptedFactionDefCache = new Dictionary<FactionDef, bool>();
        private static GeneDef cachedPollutionRushGene;
        private static bool pollutionRushGeneResolved;

        /// <summary>
        /// Exact Biotech waster-pirate id, or faction whose highest-chance xenotype carries PollutionRush.
        /// Exact PirateWaster stays required under VRE-Waster (strips PollutionRush from Waster).
        /// </summary>
        public static bool IsPollutionAdaptedFaction(Faction faction)
        {
            if (faction?.def == null) return false;
            return IsPollutionAdaptedFactionDef(faction.def);
        }

        public static bool IsPollutionAdaptedFaction(WorldObject worldObject)
            => worldObject != null && IsPollutionAdaptedFaction(worldObject.Faction);

        /// <summary>Alias for decontam/call-site clarity; same as <see cref="IsPollutionAdaptedFaction"/>.</summary>
        public static bool IsWasterPirateFaction(Faction faction)
            => IsPollutionAdaptedFaction(faction);

        public static bool IsWasterPirateFaction(WorldObject worldObject)
            => IsPollutionAdaptedFaction(worldObject);

        /// <summary>Skip traveler exit / site daily pollution damage when the Experimental setting is on.</summary>
        public static bool IsImmune(Faction faction)
        {
            if (!IsPollutionAdaptedFaction(faction))
                return false;
            return WorldDominationMod.settings?.wasterPollutionImmunityEnabled
                ?? WorldDominationSettings.DefWasterPollutionImmunityEnabled;
        }

        public static bool IsImmune(WorldObject worldObject)
        {
            return worldObject != null && IsImmune(worldObject.Faction);
        }

        private static bool IsPollutionAdaptedFactionDef(FactionDef def)
        {
            if (def == null) return false;
            if (adaptedFactionDefCache.TryGetValue(def, out bool cached))
                return cached;

            bool adapted = def.defName == WasterPirateFactionDefName
                || MajorityXenotypeHasPollutionRush(def);
            adaptedFactionDefCache[def] = adapted;
            return adapted;
        }

        private static bool MajorityXenotypeHasPollutionRush(FactionDef def)
        {
            GeneDef rush = PollutionRushGene();
            if (rush == null) return false;

            XenotypeSet set = def.xenotypeSet;
            if (set == null || set.Count == 0) return false;

            XenotypeDef best = null;
            float bestChance = -1f;
            for (int i = 0; i < set.Count; i++)
            {
                XenotypeChance entry = set[i];
                if (entry.xenotype == null) continue;
                if (entry.chance > bestChance)
                {
                    bestChance = entry.chance;
                    best = entry.xenotype;
                }
            }

            return XenotypeHasGene(best, rush);
        }

        private static bool XenotypeHasGene(XenotypeDef xenotype, GeneDef gene)
        {
            if (xenotype?.genes == null || gene == null) return false;
            for (int i = 0; i < xenotype.genes.Count; i++)
            {
                if (xenotype.genes[i] == gene)
                    return true;
            }
            return false;
        }

        private static GeneDef PollutionRushGene()
        {
            if (pollutionRushGeneResolved)
                return cachedPollutionRushGene;
            pollutionRushGeneResolved = true;
            cachedPollutionRushGene = DefDatabase<GeneDef>.GetNamedSilentFail(PollutionRushGeneDefName);
            return cachedPollutionRushGene;
        }
    }
}
