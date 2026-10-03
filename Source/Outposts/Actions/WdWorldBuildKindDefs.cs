using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>XML-authored road-block kind (cost, research, min Construction). Looked up by <see cref="kind"/>.</summary>
    public class WdRoadBlockKindDef : Def
    {
        public RoadBlockKind kind = RoadBlockKind.Normal;
        public int minCumulativeConstructionSkill = -1;
        public List<ResearchProjectDef> researchPrerequisites = new List<ResearchProjectDef>();
        public List<OutpostUpgradeCostEntry> cost = new List<OutpostUpgradeCostEntry>();
    }

    /// <summary>XML-authored spike-trap kind (cost, research, min Construction). Looked up by <see cref="kind"/>.</summary>
    public class WdSpikeTrapKindDef : Def
    {
        public SpikeTrapKind kind = SpikeTrapKind.Spike;
        public int minCumulativeConstructionSkill = -1;
        public List<ResearchProjectDef> researchPrerequisites = new List<ResearchProjectDef>();
        public List<OutpostUpgradeCostEntry> cost = new List<OutpostUpgradeCostEntry>();
    }

    /// <summary>Resolve WorldBuild kind defs by scribed enum (no save coupling to defName).</summary>
    public static class WdWorldBuildKindDefResolver
    {
        private static Dictionary<RoadBlockKind, WdRoadBlockKindDef> roadBlockByKind;
        private static Dictionary<SpikeTrapKind, WdSpikeTrapKindDef> spikeTrapByKind;

        public static void ClearCache()
        {
            roadBlockByKind = null;
            spikeTrapByKind = null;
        }

        public static WdRoadBlockKindDef GetRoadBlock(RoadBlockKind kind)
        {
            EnsureRoadBlocks();
            return roadBlockByKind.TryGetValue(kind, out WdRoadBlockKindDef def) ? def : null;
        }

        public static WdSpikeTrapKindDef GetSpikeTrap(SpikeTrapKind kind)
        {
            EnsureSpikeTraps();
            return spikeTrapByKind.TryGetValue(kind, out WdSpikeTrapKindDef def) ? def : null;
        }

        private static void EnsureRoadBlocks()
        {
            if (roadBlockByKind != null) return;
            roadBlockByKind = new Dictionary<RoadBlockKind, WdRoadBlockKindDef>();
            var list = DefDatabase<WdRoadBlockKindDef>.AllDefsListForReading;
            for (int i = 0; i < list.Count; i++)
            {
                WdRoadBlockKindDef d = list[i];
                if (d == null) continue;
                roadBlockByKind[d.kind] = d;
            }
        }

        private static void EnsureSpikeTraps()
        {
            if (spikeTrapByKind != null) return;
            spikeTrapByKind = new Dictionary<SpikeTrapKind, WdSpikeTrapKindDef>();
            var list = DefDatabase<WdSpikeTrapKindDef>.AllDefsListForReading;
            for (int i = 0; i < list.Count; i++)
            {
                WdSpikeTrapKindDef d = list[i];
                if (d == null) continue;
                spikeTrapByKind[d.kind] = d;
            }
        }
    }
}
