using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Tile-aware NPC settlement subtype picks (Camp / Refuge / specialty gates)
    /// and layout-name aliases (Slavery → Prison SettlementLayoutDef).
    /// </summary>
    public static class NpcSettlementSubtypeUtil
    {
        public const string SubTypeCamp = "Camp";
        public const string SubTypeRefuge = "Refuge";
        public const string SubTypeFarming = "Farming";
        public const string SubTypeLogging = "Logging";
        public const string SubTypeMining = "Mining";
        public const string SubTypeProduction = "Production";
        public const string SubTypeSlavery = "Slavery";

        /// <summary>Plant-density rank floor for extreme-tile detection (barren / ice).</summary>
        public const float ExtremePlantDensityFloor = 0.1f;

        /// <summary>Farming specialty: fertility score floor (excludes desert / ice / volcanic / adverse).</summary>
        public const float FarmingFertilityFloor = 0.30f;

        /// <summary>Logging specialty: fertility score floor (excludes ice / volcanic).</summary>
        public const float LoggingFertilityFloor = 0.15f;

        /// <summary>Mining base score floor (SmallHills = 0.5).</summary>
        public const float MiningBaseScoreFloor = 0.5f;

        private static readonly List<string> pickScratch = new List<string>(8);

        /// <summary>
        /// Farming fertility is 0 and plant density is below the extreme plant floor
        /// (ice / barren desert). Mining may still be eligible separately.
        /// Unknown tiles are not extreme (world-object init often runs before Tile is set).
        /// </summary>
        public static bool IsExtremeTile(int tileId)
        {
            if (tileId < 0) return false;
            return WorldTileProductivity.GetFarmingFertilityScore(tileId) <= 0f
                && WorldTileProductivity.GetPlantDensityRank(tileId) < ExtremePlantDensityFloor;
        }

        public static bool CanFarm(int tileId) =>
            tileId >= 0 && WorldTileProductivity.GetFarmingFertilityScore(tileId) >= FarmingFertilityFloor;

        public static bool CanLog(int tileId) =>
            tileId >= 0 && WorldTileProductivity.GetFarmingFertilityScore(tileId) >= LoggingFertilityFloor;

        public static bool CanMine(int tileId) =>
            tileId >= 0 && WorldTileProductivity.GetMiningBaseScore(tileId) >= MiningBaseScoreFloor;

        /// <summary>Cheap expand-seed score: max of fertility, hunting, mining.</summary>
        public static float ExpandTileAttractiveness(int tileId)
        {
            if (tileId < 0) return 0f;
            float fert = WorldTileProductivity.GetFarmingFertilityScore(tileId);
            float hunt = WorldTileProductivity.GetHuntingScore(tileId);
            float mine = WorldTileProductivity.GetMiningOutputMultiplier(tileId);
            return Mathf.Max(fert, Mathf.Max(hunt, mine));
        }

        /// <summary>
        /// SettlementLayoutDef token for <paramref name="subType"/>.
        /// Scribed Slavery maps to Prison layout defNames.
        /// </summary>
        public static string LayoutTokenForSubtype(string subType)
        {
            if (string.IsNullOrEmpty(subType)) return subType;
            if (string.Equals(subType, SubTypeSlavery, System.StringComparison.Ordinal))
                return "Prison";
            return subType;
        }

        /// <summary>Tile-aware random specialty for NPC settlements.</summary>
        public static string PickSubtype(SettlementTier tier, int tileId)
        {
            switch (tier)
            {
                case SettlementTier.T1:
                    return PickT1(tileId);
                case SettlementTier.T2:
                    return PickT2(tileId);
                case SettlementTier.T3:
                    return "Fortress";
                case SettlementTier.T4:
                    return "Citadel";
                default:
                    return "Generic";
            }
        }

        private static string PickT1(int tileId)
        {
            pickScratch.Clear();

            // Tile unknown (early world-object init): pre-tile-aware specialty pool, no Refuge.
            if (tileId < 0)
            {
                pickScratch.Add(SubTypeFarming);
                pickScratch.Add(SubTypeLogging);
                pickScratch.Add(SubTypeMining);
                pickScratch.Add(SubTypeCamp);
                return pickScratch.RandomElement();
            }

            if (IsExtremeTile(tileId))
            {
                if (CanMine(tileId)) pickScratch.Add(SubTypeMining);
                pickScratch.Add(SubTypeRefuge);
            }
            else
            {
                if (CanFarm(tileId)) pickScratch.Add(SubTypeFarming);
                if (CanLog(tileId)) pickScratch.Add(SubTypeLogging);
                if (CanMine(tileId)) pickScratch.Add(SubTypeMining);
                pickScratch.Add(SubTypeCamp);
            }

            if (pickScratch.Count == 0)
                return SubTypeRefuge;
            return pickScratch.RandomElement();
        }

        private static string PickT2(int tileId)
        {
            pickScratch.Clear();
            if (tileId >= 0 && IsExtremeTile(tileId))
                return SubTypeRefuge;

            pickScratch.Add(SubTypeProduction);
            pickScratch.Add(SubTypeSlavery);
            pickScratch.Add(SubTypeCamp);
            return pickScratch.RandomElement();
        }
    }
}
