using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Stub material cost entry for player world builds (roads, fortifications, AT).
    /// Deduction (colony map + warehouses anywhere) will mirror outpost upgrades later; lists are empty for now.
    /// </summary>
    public class ColonyWorldBuildCostEntry
    {
        public string thingDefName;
        public int count = 1;
    }

    /// <summary>
    /// Research + construction gates and tip formatting for player Build menu options.
    /// </summary>
    public static class ColonyWorldBuildRequirements
    {
        public const string ResearchMachining = "Machining";
        public const string ResearchMicroelectronics = "MicroelectronicsBasics";
        public const string ResearchFabrication = "Fabrication";

        public static ResearchProjectDef GetRequiredResearchForRoad(SettlementTier tier)
        {
            if (tier == SettlementTier.T3 || tier == SettlementTier.T4)
                return FindResearch(ResearchMicroelectronics);
            if (tier == SettlementTier.T2)
                return FindResearch(ResearchMachining);
            return null;
        }

        public static ResearchProjectDef GetRequiredResearchForRoadBlock(RoadBlockKind kind)
        {
            if (kind == RoadBlockKind.Heavy)
                return FindResearch(ResearchMicroelectronics);
            if (kind == RoadBlockKind.Normal)
                return FindResearch(ResearchMachining);
            return null;
        }

        public static ResearchProjectDef GetRequiredResearchForSpikeTrap(SpikeTrapKind kind)
        {
            if (kind == SpikeTrapKind.Caltrops)
                return FindResearch(ResearchMachining);
            return null;
        }

        public static ResearchProjectDef GetRequiredResearchForAtTurret(AtTurretTier tier)
        {
            if (tier == AtTurretTier.Heavy)
                return FindResearch(ResearchFabrication);
            if (tier == AtTurretTier.Medium)
                return FindResearch(ResearchMicroelectronics);
            if (tier == AtTurretTier.Light)
                return FindResearch(ResearchMachining);
            return null;
        }

        /// <summary>Material costs for this build (empty until goods costs are enabled).</summary>
        public static List<ColonyWorldBuildCostEntry> GetMaterialCostsForRoad(SettlementTier tier) => EmptyCosts;

        public static List<ColonyWorldBuildCostEntry> GetMaterialCostsForRoadBlock(RoadBlockKind kind) => EmptyCosts;

        public static List<ColonyWorldBuildCostEntry> GetMaterialCostsForSpikeTrap(SpikeTrapKind kind) => EmptyCosts;

        public static List<ColonyWorldBuildCostEntry> GetMaterialCostsForAtTurret(AtTurretTier tier) => EmptyCosts;

        private static readonly List<ColonyWorldBuildCostEntry> EmptyCosts = new List<ColonyWorldBuildCostEntry>();

        public static bool IsResearchMet(ResearchProjectDef project)
        {
            if (project == null) return true;
            return project.IsFinished;
        }

        public static bool MeetsConstruction(float currentSkill, int minConstruction) =>
            currentSkill >= minConstruction;

        public static bool MeetsRoadRequirements(WorldObject actor, SettlementTier tier)
        {
            float skill = ColonyWorldBuildUtility.GetActorConstructionSkillRaw(actor);
            int minC = WorldActions_Roads.GetMinConstructionToBuildRoad(tier);
            return MeetsConstruction(skill, minC) && IsResearchMet(GetRequiredResearchForRoad(tier));
        }

        public static bool MeetsRoadBlockRequirements(WorldObject actor, RoadBlockKind kind)
        {
            float skill = ColonyWorldBuildUtility.GetActorConstructionSkillRaw(actor);
            int minC = WorldActions_RoadBlocks.GetMinConstruction(kind);
            return MeetsConstruction(skill, minC) && IsResearchMet(GetRequiredResearchForRoadBlock(kind));
        }

        public static bool MeetsSpikeTrapRequirements(WorldObject actor, SpikeTrapKind kind)
        {
            float skill = ColonyWorldBuildUtility.GetActorConstructionSkillRaw(actor);
            int minC = WorldActions_SpikeTraps.GetMinConstruction(kind);
            return MeetsConstruction(skill, minC) && IsResearchMet(GetRequiredResearchForSpikeTrap(kind));
        }

        public static bool MeetsAtTurretRequirements(WorldObject actor, AtTurretTier tier)
        {
            float skill = ColonyWorldBuildUtility.GetActorConstructionSkillRaw(actor);
            int minC = WorldActions_AtTurrets.GetMinConstruction(tier);
            return MeetsConstruction(skill, minC) && IsResearchMet(GetRequiredResearchForAtTurret(tier));
        }

        /// <summary>
        /// Grey the option if unmet; keep the normal label. Append requirements (construction, research, materials) to the tooltip.
        /// </summary>
        public static void ApplyGate(
            FloatMenuOption opt,
            float currentConstruction,
            int minConstruction,
            ResearchProjectDef requiredResearch,
            List<ColonyWorldBuildCostEntry> materialCosts)
        {
            if (opt == null) return;

            string existingTip = opt.tooltip.HasValue ? (opt.tooltip.Value.text ?? string.Empty) : string.Empty;
            string reqBlock = FormatRequirementsBlock(currentConstruction, minConstruction, requiredResearch, materialCosts);
            if (!string.IsNullOrEmpty(existingTip))
                opt.tooltip = existingTip + "\n\n" + reqBlock;
            else
                opt.tooltip = reqBlock;

            bool unmet = !MeetsConstruction(currentConstruction, minConstruction) || !IsResearchMet(requiredResearch);
            if (unmet)
                opt.Disabled = true;
        }

        public static string FormatRequirementsBlock(
            float currentConstruction,
            int minConstruction,
            ResearchProjectDef requiredResearch,
            List<ColonyWorldBuildCostEntry> materialCosts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("TSA_WD_BuildReq_Header".Translate());
            sb.AppendLine("TSA_WD_BuildReq_Construction".Translate(
                currentConstruction.ToString("F0"),
                minConstruction.ToString()));

            if (requiredResearch == null)
                sb.AppendLine("TSA_WD_BuildReq_ResearchNone".Translate());
            else if (requiredResearch.IsFinished)
                sb.AppendLine("TSA_WD_BuildReq_ResearchDone".Translate(requiredResearch.LabelCap));
            else
                sb.AppendLine("TSA_WD_BuildReq_ResearchMissing".Translate(requiredResearch.LabelCap));

            if (materialCosts == null || materialCosts.Count == 0)
                sb.Append("TSA_WD_BuildReq_MaterialsNone".Translate());
            else
            {
                sb.AppendLine("TSA_WD_BuildReq_MaterialsHeader".Translate());
                for (int i = 0; i < materialCosts.Count; i++)
                {
                    ColonyWorldBuildCostEntry e = materialCosts[i];
                    if (e == null || string.IsNullOrEmpty(e.thingDefName)) continue;
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(e.thingDefName);
                    string name = def != null ? def.LabelCap : e.thingDefName;
                    sb.AppendLine("TSA_WD_BuildReq_MaterialLine".Translate(name, e.count.ToString()));
                }
            }

            return sb.ToString().TrimEnd();
        }

        private static ResearchProjectDef FindResearch(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return null;
            return DefDatabase<ResearchProjectDef>.GetNamedSilentFail(defName);
        }
    }
}
