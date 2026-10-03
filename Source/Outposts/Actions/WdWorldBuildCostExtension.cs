using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Build-gate data on WorldObjectDefs (AT turrets): material cost, research, min Construction.
    /// </summary>
    public class WdWorldBuildCostExtension : DefModExtension
    {
        public List<OutpostUpgradeCostEntry> cost = new List<OutpostUpgradeCostEntry>();
        public List<ResearchProjectDef> researchPrerequisites = new List<ResearchProjectDef>();
        /// <summary>When &gt;= 0, overrides settings-based min Construction for this build.</summary>
        public int minCumulativeConstructionSkill = -1;
    }
}
