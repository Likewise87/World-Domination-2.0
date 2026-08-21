namespace TSA_WorldDomination
{
    /// <summary>Persisted spike-trap type. Spike = 0 for legacy saves without a kind field.</summary>
    public enum SpikeTrapKind : byte
    {
        Spike = 0,
        Caltrops = 1
    }

    public static class SpikeTrapKindUtil
    {
        public static int Rank(SpikeTrapKind kind)
        {
            switch (kind)
            {
                case SpikeTrapKind.Spike: return 1;
                case SpikeTrapKind.Caltrops: return 2;
                default: return -1;
            }
        }

        public static bool CanUpgradeTo(SpikeTrapKind existing, SpikeTrapKind selected)
        {
            int from = Rank(existing);
            int to = Rank(selected);
            return from >= 0 && to > from;
        }

        public static SettlementTier WorkBaselineTier(SpikeTrapKind kind)
        {
            return kind == SpikeTrapKind.Caltrops ? SettlementTier.T2 : SettlementTier.T1;
        }

        public static string TexturePath(SpikeTrapKind kind)
        {
            return kind == SpikeTrapKind.Caltrops
                ? "WorldObjects/Caltrops"
                : "WorldObjects/WorldSpikeTrap";
        }

        public static string LabelKey(SpikeTrapKind kind)
        {
            return kind == SpikeTrapKind.Caltrops
                ? "TSA_WD_SpikeTrapCaltrops"
                : "TSA_WD_SpikeTrapSpike";
        }
    }
}
