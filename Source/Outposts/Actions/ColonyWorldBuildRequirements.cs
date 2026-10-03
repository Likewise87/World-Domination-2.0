using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Research + construction + material gates and tip formatting for player Build menu options.
    /// Material lists come from WorldBuild Defs / AT ModExtensions.
    /// </summary>
    public static class ColonyWorldBuildRequirements
    {
        public const string ResearchMachining = "Machining";
        public const string ResearchMicroelectronics = "MicroelectronicsBasics";
        public const string ResearchFabrication = "Fabrication";

        private static readonly List<OutpostUpgradeCostEntry> EmptyCosts = ColonyWorldBuildMaterials.EmptyCostList;

        public static ResearchProjectDef GetRequiredResearchForRoad(SettlementTier tier)
        {
            WdRoadTierDef def = WdBiomeTableResolver.GetRoadTierDef(tier);
            if (def?.researchPrerequisites != null)
            {
                for (int i = 0; i < def.researchPrerequisites.Count; i++)
                {
                    ResearchProjectDef project = def.researchPrerequisites[i];
                    if (project != null) return project;
                }
            }

            if (tier == SettlementTier.T3 || tier == SettlementTier.T4)
                return FindResearch(ResearchMicroelectronics);
            if (tier == SettlementTier.T2)
                return FindResearch(ResearchMachining);
            return null;
        }

        public static ResearchProjectDef GetRequiredResearchForRoadBlock(RoadBlockKind kind)
        {
            WdRoadBlockKindDef def = WdWorldBuildKindDefResolver.GetRoadBlock(kind);
            if (def?.researchPrerequisites != null)
            {
                for (int i = 0; i < def.researchPrerequisites.Count; i++)
                {
                    if (def.researchPrerequisites[i] != null)
                        return def.researchPrerequisites[i];
                }
            }

            if (kind == RoadBlockKind.Heavy)
                return FindResearch(ResearchMicroelectronics);
            if (kind == RoadBlockKind.Normal)
                return FindResearch(ResearchMachining);
            return null;
        }

        public static ResearchProjectDef GetRequiredResearchForSpikeTrap(SpikeTrapKind kind)
        {
            WdSpikeTrapKindDef def = WdWorldBuildKindDefResolver.GetSpikeTrap(kind);
            if (def?.researchPrerequisites != null)
            {
                for (int i = 0; i < def.researchPrerequisites.Count; i++)
                {
                    if (def.researchPrerequisites[i] != null)
                        return def.researchPrerequisites[i];
                }
            }

            if (kind == SpikeTrapKind.Caltrops)
                return FindResearch(ResearchMachining);
            return null;
        }

        public static ResearchProjectDef GetRequiredResearchForAtTurret(AtTurretTier tier)
        {
            WdWorldBuildCostExtension ext = GetAtTurretBuildExtension(tier);
            if (ext?.researchPrerequisites != null)
            {
                for (int i = 0; i < ext.researchPrerequisites.Count; i++)
                {
                    if (ext.researchPrerequisites[i] != null)
                        return ext.researchPrerequisites[i];
                }
            }

            if (tier == AtTurretTier.Heavy)
                return FindResearch(ResearchFabrication);
            if (tier == AtTurretTier.Medium)
                return FindResearch(ResearchMicroelectronics);
            if (tier == AtTurretTier.Light)
                return FindResearch(ResearchMachining);
            return null;
        }

        public static List<OutpostUpgradeCostEntry> GetMaterialCostsForRoad(SettlementTier tier)
        {
            WdRoadTierDef def = WdBiomeTableResolver.GetRoadTierDef(tier);
            if (def?.cost == null || def.cost.Count == 0) return EmptyCosts;
            return def.cost;
        }

        public static List<OutpostUpgradeCostEntry> GetMaterialCostsForRoadBlock(RoadBlockKind kind)
        {
            WdRoadBlockKindDef def = WdWorldBuildKindDefResolver.GetRoadBlock(kind);
            if (def?.cost == null || def.cost.Count == 0) return EmptyCosts;
            return def.cost;
        }

        public static List<OutpostUpgradeCostEntry> GetMaterialCostsForSpikeTrap(SpikeTrapKind kind)
        {
            WdSpikeTrapKindDef def = WdWorldBuildKindDefResolver.GetSpikeTrap(kind);
            if (def?.cost == null || def.cost.Count == 0) return EmptyCosts;
            return def.cost;
        }

        public static List<OutpostUpgradeCostEntry> GetMaterialCostsForAtTurret(AtTurretTier tier)
        {
            WdWorldBuildCostExtension ext = GetAtTurretBuildExtension(tier);
            if (ext?.cost == null || ext.cost.Count == 0) return EmptyCosts;
            return ext.cost;
        }

        /// <summary>True when this actor pays world-build materials (player outpost or colony build site).</summary>
        public static bool ActorPaysWorldBuildMaterials(WorldObject actor)
        {
            if (actor == null) return false;
            return actor is WorldObject_WD_Outpost
                || ColonyWorldBuildUtility.IsPlayerColonyBuildActor(actor);
        }

        public static bool HasMaterialCostsForRoad(SettlementTier tier) =>
            ColonyWorldBuildMaterials.HasMaterialCosts(GetMaterialCostsForRoad(tier));

        public static bool HasMaterialCostsForRoadBlock(RoadBlockKind kind) =>
            ColonyWorldBuildMaterials.HasMaterialCosts(GetMaterialCostsForRoadBlock(kind));

        public static bool HasMaterialCostsForSpikeTrap(SpikeTrapKind kind) =>
            ColonyWorldBuildMaterials.HasMaterialCosts(GetMaterialCostsForSpikeTrap(kind));

        public static bool HasMaterialCostsForAtTurret(AtTurretTier tier) =>
            ColonyWorldBuildMaterials.HasMaterialCosts(GetMaterialCostsForAtTurret(tier));

        public static bool TryDeductForRoad(SettlementTier tier, out string reason) =>
            ColonyWorldBuildMaterials.TryDeductMaterialCosts(GetMaterialCostsForRoad(tier), out reason);

        public static bool TryDeductForRoadBlock(RoadBlockKind kind, out string reason) =>
            ColonyWorldBuildMaterials.TryDeductMaterialCosts(GetMaterialCostsForRoadBlock(kind), out reason);

        public static bool TryDeductForSpikeTrap(SpikeTrapKind kind, out string reason) =>
            ColonyWorldBuildMaterials.TryDeductMaterialCosts(GetMaterialCostsForSpikeTrap(kind), out reason);

        public static bool TryDeductForAtTurret(AtTurretTier tier, out string reason) =>
            ColonyWorldBuildMaterials.TryDeductMaterialCosts(GetMaterialCostsForAtTurret(tier), out reason);

        /// <summary>
        /// After a player construction traveler successfully spawned: deduct materials or destroy traveler and refund strength.
        /// </summary>
        public static bool TryFinalizeMaterialsOrAbort(
            WorldObject origin,
            WorldObject_Traveler traveler,
            float strengthCost,
            List<OutpostUpgradeCostEntry> costs)
        {
            if (!ActorPaysWorldBuildMaterials(origin)) return true;
            if (costs == null || costs.Count == 0) return true;
            if (ColonyWorldBuildMaterials.TryDeductMaterialCosts(costs, out _)) return true;

            var comp = origin?.GetComponent<CompViralSpread>();
            if (traveler != null && !traveler.Destroyed)
                traveler.Destroy();
            if (comp != null && strengthCost > 0f)
                WorldActions_Utils.RefundExpeditionStrength(comp, strengthCost);
            return false;
        }

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
            if (!MeetsConstruction(skill, minC)) return false;

            WdRoadTierDef def = WdBiomeTableResolver.GetRoadTierDef(tier);
            if (def?.researchPrerequisites != null && def.researchPrerequisites.Count > 0)
            {
                for (int i = 0; i < def.researchPrerequisites.Count; i++)
                {
                    if (!IsResearchMet(def.researchPrerequisites[i])) return false;
                }
                return true;
            }

            return IsResearchMet(GetRequiredResearchForRoad(tier));
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
            List<OutpostUpgradeCostEntry> materialCosts)
        {
            if (opt == null) return;

            string existingTip = opt.tooltip.HasValue ? (opt.tooltip.Value.text ?? string.Empty) : string.Empty;
            string reqBlock = FormatRequirementsBlock(currentConstruction, minConstruction, requiredResearch, materialCosts);
            if (!string.IsNullOrEmpty(existingTip))
                opt.tooltip = existingTip + "\n\n" + reqBlock;
            else
                opt.tooltip = reqBlock;

            bool unmet = !MeetsConstruction(currentConstruction, minConstruction)
                || !IsResearchMet(requiredResearch)
                || !ColonyWorldBuildMaterials.HasMaterialCosts(materialCosts);
            if (unmet)
                opt.Disabled = true;
        }

        public static string FormatRequirementsBlock(
            float currentConstruction,
            int minConstruction,
            ResearchProjectDef requiredResearch,
            List<OutpostUpgradeCostEntry> materialCosts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("TSA_WD_BuildReq_Header".Translate());
            // {0} = required, {1} = current skill (e.g. Construction: 5 (30)).
            sb.AppendLine("TSA_WD_BuildReq_Construction".Translate(
                minConstruction.ToString(),
                currentConstruction.ToString("F0")));

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
                    OutpostUpgradeCostEntry e = materialCosts[i];
                    if (e == null || e.count <= 0) continue;
                    string name = ColonyWorldBuildMaterials.GetCostDisplayLabel(e);
                    if (string.IsNullOrEmpty(name)) continue;
                    sb.AppendLine("TSA_WD_BuildReq_MaterialLine".Translate(name, e.count.ToString()));
                }
            }

            return sb.ToString().TrimEnd();
        }

        public static WdWorldBuildCostExtension GetAtTurretBuildExtension(AtTurretTier tier)
        {
            string defName = AtTurretUtility.DefNameForTier(tier);
            WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamedSilentFail(defName);
            return def?.GetModExtension<WdWorldBuildCostExtension>();
        }

        private static ResearchProjectDef FindResearch(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return null;
            return DefDatabase<ResearchProjectDef>.GetNamedSilentFail(defName);
        }
    }
}
