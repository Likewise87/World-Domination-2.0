using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    public enum AdvancedBuildingCategory
    {
        Economic,
        Artillery
    }

    public enum AdvancedBuildingEffect
    {
        OffensiveMaxPct,
        RegenPct,
        AttackRangePct,
        LandSpeedPct,
        Mortar,
        AntiAir
    }

    /// <summary>
    /// NPC "advanced building project": one world object per type per settlement, placed on a tile near the owner.
    /// Economic types buff the owner; artillery types are the owner's mortar / anti-air turrets.
    /// </summary>
    public class AdvancedBuildingDef : Def
    {
        public AdvancedBuildingCategory category = AdvancedBuildingCategory.Economic;
        public AdvancedBuildingEffect effect = AdvancedBuildingEffect.OffensiveMaxPct;
        /// <summary>Pick weight per owner tier (T1..T4). Artillery entries are ignored outside the artillery rules.</summary>
        public List<float> tierWeights = new List<float>();
        public int preferredMaxRadius = 2;
        public int maxRadius = 4;
        public float travelerStrength = 50f;
        public string iconTexPath;
        public WorldObjectDef worldObjectDef;
        /// <summary>Translation key for the player-facing name.</summary>
        public string labelKey;

        public bool IsArtillery => category == AdvancedBuildingCategory.Artillery;

        public float WeightForTier(SettlementTier tier)
        {
            int i = (int)tier;
            if (tierWeights == null || i < 0 || i >= tierWeights.Count) return 0f;
            return tierWeights[i];
        }

        public string LabelText => labelKey.NullOrEmpty() ? LabelCap.ToString() : labelKey.Translate().ToString();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors())
                yield return e;
            if (worldObjectDef == null)
                yield return "worldObjectDef is null";
            if (tierWeights == null || tierWeights.Count != 4)
                yield return "tierWeights must have 4 entries (T1..T4)";
        }
    }

    [DefOf]
    public static class AdvancedBuildingDefOf
    {
        public static AdvancedBuildingDef TSA_WD_AdvBuilding_Silo;
        public static AdvancedBuildingDef TSA_WD_AdvBuilding_Hospital;
        public static AdvancedBuildingDef TSA_WD_AdvBuilding_WatchTower;
        public static AdvancedBuildingDef TSA_WD_AdvBuilding_Stable;
        public static AdvancedBuildingDef TSA_WD_AdvBuilding_MortarTurret;
        public static AdvancedBuildingDef TSA_WD_AdvBuilding_AaTurret;

        static AdvancedBuildingDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(AdvancedBuildingDefOf));
    }
}
