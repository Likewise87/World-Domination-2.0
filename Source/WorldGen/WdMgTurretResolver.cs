using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Picks a turret ThingDef from <see cref="WdMgTurretPoolDef"/> by level.
    /// Filters MayRequire / missing defs in C#; falls back to Turret_MiniTurret unmanned.
    /// </summary>
    public static class WdMgTurretResolver
    {
        public const string PoolDefName = "TSA_WdMgTurretPool";
        public const string FallbackTurretDefName = "Turret_MiniTurret";

        /// <summary>Settlement T2→1, T3→2, T4→3. T1 and unknown → 0 (no pool use).</summary>
        public static int PoolLevelFromTier(SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.T2: return 1;
                case SettlementTier.T3: return 2;
                case SettlementTier.T4: return 3;
                default: return 0;
            }
        }

        public static bool TryPick(int level, out ThingDef def, out bool manned)
        {
            def = null;
            manned = false;
            if (level <= 0) return false;

            List<(ThingDef def, float weight, bool manned)> eligible = CollectEligible(level);
            if (eligible.Count > 0)
            {
                float total = 0f;
                for (int i = 0; i < eligible.Count; i++)
                    total += eligible[i].weight;
                float roll = Rand.Range(0f, total);
                for (int i = 0; i < eligible.Count; i++)
                {
                    roll -= eligible[i].weight;
                    if (roll > 0f) continue;
                    def = eligible[i].def;
                    manned = ResolveManned(eligible[i].manned, def);
                    return def != null;
                }

                var last = eligible[eligible.Count - 1];
                def = last.def;
                manned = ResolveManned(last.manned, def);
                return def != null;
            }

            def = DefDatabase<ThingDef>.GetNamedSilentFail(FallbackTurretDefName);
            manned = false;
            return def != null;
        }

        private static List<(ThingDef def, float weight, bool manned)> CollectEligible(int level)
        {
            var result = new List<(ThingDef, float, bool)>();
            WdMgTurretPoolDef pool = DefDatabase<WdMgTurretPoolDef>.GetNamedSilentFail(PoolDefName);
            WdMgTurretTier tier = pool?.GetTier(level);
            if (tier?.options == null) return result;

            for (int i = 0; i < tier.options.Count; i++)
            {
                WdMgTurretOption opt = tier.options[i];
                if (opt?.thingDef == null) continue;
                if (!PassesMayRequire(opt)) continue;
                if (opt.weight <= 0f) continue;
                result.Add((opt.thingDef, opt.weight, opt.manned));
            }
            return result;
        }

        /// <summary>XML manned only if the def is actually mannable (CompMannable).</summary>
        private static bool ResolveManned(bool xmlManned, ThingDef def) =>
            xmlManned && IsMannableDef(def);

        public static bool IsMannableDef(ThingDef def) =>
            def?.GetCompProperties<CompProperties_Mannable>() != null;

        private static bool PassesMayRequire(WdMgTurretOption option)
        {
            if (option == null) return false;
            if (!string.IsNullOrWhiteSpace(option.MayRequire))
            {
                foreach (string raw in option.MayRequire.Split(','))
                {
                    string id = raw.Trim();
                    if (string.IsNullOrEmpty(id)) continue;
                    if (!ModPackageActive(id)) return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(option.MayRequireAnyOf))
            {
                bool any = false;
                foreach (string raw in option.MayRequireAnyOf.Split(','))
                {
                    string id = raw.Trim();
                    if (string.IsNullOrEmpty(id)) continue;
                    if (ModPackageActive(id))
                    {
                        any = true;
                        break;
                    }
                }
                if (!any) return false;
            }

            return true;
        }

        private static bool ModPackageActive(string packageId)
        {
            if (ModsConfig.IsActive(packageId)) return true;
            if (!packageId.EndsWith("_steam", StringComparison.OrdinalIgnoreCase)
                && ModsConfig.IsActive(packageId + "_steam"))
                return true;
            return false;
        }
    }
}
