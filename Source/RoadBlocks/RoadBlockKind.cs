namespace TSA_WorldDomination
{
    /// <summary>
    /// Persisted road-block type. Value 0 is Normal (player-facing label: Medium; legacy saves).
    /// Gate is stored/API-ready but not placed from UI.
    /// </summary>
    public enum RoadBlockKind : byte
    {
        /// <summary>Medium road block in UI (enum/save name remains Normal, value 0).</summary>
        Normal = 0,
        Gate = 1,
        Light = 2,
        Heavy = 3
    }

    public static class RoadBlockKindUtil
    {
        /// <summary>Upgrade rank; higher may replace lower. Gate is not upgradable.</summary>
        public static int Rank(RoadBlockKind kind)
        {
            switch (kind)
            {
                case RoadBlockKind.Light: return 1;
                case RoadBlockKind.Normal: return 2;
                case RoadBlockKind.Heavy: return 3;
                default: return -1;
            }
        }

        public static bool CanUpgradeTo(RoadBlockKind existing, RoadBlockKind selected)
        {
            int from = Rank(existing);
            int to = Rank(selected);
            return from >= 0 && to > from;
        }

        public static bool IsPlaceableFromUi(RoadBlockKind kind)
        {
            return kind == RoadBlockKind.Light || kind == RoadBlockKind.Normal || kind == RoadBlockKind.Heavy;
        }

        public static SettlementTier WorkBaselineTier(RoadBlockKind kind)
        {
            switch (kind)
            {
                case RoadBlockKind.Heavy: return SettlementTier.T3;
                case RoadBlockKind.Normal: return SettlementTier.T2;
                default: return SettlementTier.T1;
            }
        }

        public static string TexturePath(RoadBlockKind kind)
        {
            switch (kind)
            {
                case RoadBlockKind.Light: return "WorldObjects/RoadBlock_Light_Colorized";
                case RoadBlockKind.Heavy: return "WorldObjects/RoadBlock_Heavy";
                // Medium (persisted as Normal). Keep RoadBlock_Colorized as a spare asset on disk.
                default: return "WorldObjects/RoadBlock_Medium_Colorized";
            }
        }

        public static string LabelKey(RoadBlockKind kind)
        {
            switch (kind)
            {
                case RoadBlockKind.Light: return "TSA_WD_RoadBlockLight";
                case RoadBlockKind.Heavy: return "TSA_WD_RoadBlockHeavy";
                case RoadBlockKind.Gate: return "TSA_WD_RoadBlockGate";
                // Translation key kept for compatibility; EN/ES/ZH display "Medium".
                default: return "TSA_WD_RoadBlockNormal";
            }
        }
    }
}
