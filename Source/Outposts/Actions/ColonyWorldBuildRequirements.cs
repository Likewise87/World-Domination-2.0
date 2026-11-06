using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
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
        public const string ResearchMedievalEngineering = "DankPyon_Engineering";
        public const string ResearchSmithing = "Smithing";

        private static readonly List<OutpostUpgradeCostEntry> EmptyCosts = ColonyWorldBuildMaterials.EmptyCostList;

        /// <summary>
        /// When Medieval mode is active, Machining world-build gates resolve to MO Engineering
        /// (or vanilla Smithing if Engineering is missing). Does not mutate Def XML.
        /// </summary>
        public static ResearchProjectDef EffectiveResearch(ResearchProjectDef project)
        {
            if (!WdMedievalMode.Active || project == null) return project;
            if (project.defName != ResearchMachining) return project;
            return FindResearch(ResearchMedievalEngineering)
                ?? FindResearch(ResearchSmithing)
                ?? project;
        }

        public static ResearchProjectDef GetRequiredResearchForRoad(SettlementTier tier)
        {
            WdRoadTierDef def = WdBiomeTableResolver.GetRoadTierDef(tier);
            if (def?.researchPrerequisites != null)
            {
                for (int i = 0; i < def.researchPrerequisites.Count; i++)
                {
                    ResearchProjectDef project = def.researchPrerequisites[i];
                    if (project != null) return EffectiveResearch(project);
                }
            }

            if (tier == SettlementTier.T3 || tier == SettlementTier.T4)
                return EffectiveResearch(FindResearch(ResearchMicroelectronics));
            if (tier == SettlementTier.T2)
                return EffectiveResearch(FindResearch(ResearchMachining));
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
                        return EffectiveResearch(def.researchPrerequisites[i]);
                }
            }

            if (kind == RoadBlockKind.Heavy)
                return EffectiveResearch(FindResearch(ResearchMicroelectronics));
            if (kind == RoadBlockKind.Normal)
                return EffectiveResearch(FindResearch(ResearchMachining));
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
                        return EffectiveResearch(def.researchPrerequisites[i]);
                }
            }

            if (kind == SpikeTrapKind.Caltrops)
                return EffectiveResearch(FindResearch(ResearchMachining));
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
                        return EffectiveResearch(ext.researchPrerequisites[i]);
                }
            }

            if (tier == AtTurretTier.Heavy)
                return EffectiveResearch(FindResearch(ResearchFabrication));
            if (tier == AtTurretTier.Medium)
                return EffectiveResearch(FindResearch(ResearchMicroelectronics));
            if (tier == AtTurretTier.Light)
                return EffectiveResearch(FindResearch(ResearchMachining));
            return null;
        }

        /// <summary>Unscaled Def costs (may alias Def XML lists — never mutate).</summary>
        public static List<OutpostUpgradeCostEntry> GetMaterialCostsForRoadUnscaled(SettlementTier tier)
        {
            WdRoadTierDef def = WdBiomeTableResolver.GetRoadTierDef(tier);
            if (def?.cost == null || def.cost.Count == 0) return EmptyCosts;
            return def.cost;
        }

        public static List<OutpostUpgradeCostEntry> GetMaterialCostsForRoadBlockUnscaled(RoadBlockKind kind)
        {
            WdRoadBlockKindDef def = WdWorldBuildKindDefResolver.GetRoadBlock(kind);
            if (def?.cost == null || def.cost.Count == 0) return EmptyCosts;
            return def.cost;
        }

        public static List<OutpostUpgradeCostEntry> GetMaterialCostsForSpikeTrapUnscaled(SpikeTrapKind kind)
        {
            WdSpikeTrapKindDef def = WdWorldBuildKindDefResolver.GetSpikeTrap(kind);
            if (def?.cost == null || def.cost.Count == 0) return EmptyCosts;
            return def.cost;
        }

        public static List<OutpostUpgradeCostEntry> GetMaterialCostsForAtTurretUnscaled(AtTurretTier tier)
        {
            WdWorldBuildCostExtension ext = GetAtTurretBuildExtension(tier);
            if (ext?.cost == null || ext.cost.Count == 0) return EmptyCosts;
            return ext.cost;
        }

        /// <summary>
        /// World-build materials with Building Projects material mult applied.
        /// At 100%: returns the unscaled list by reference (no alloc). Otherwise clones; never mutates Defs.
        /// </summary>
        public static List<OutpostUpgradeCostEntry> ApplyWorldBuildMaterialScale(List<OutpostUpgradeCostEntry> baseCosts)
        {
            var s = WorldDominationMod.settings;
            float mult = s != null ? s.worldBuildMaterialCostMult : 1f;
            if (mult <= 0f) return EmptyCosts;
            if (baseCosts == null || baseCosts.Count == 0) return EmptyCosts;
            if (s == null || Mathf.Approximately(mult, 1f)) return baseCosts;

            var scaled = new List<OutpostUpgradeCostEntry>(baseCosts.Count);
            for (int i = 0; i < baseCosts.Count; i++)
            {
                OutpostUpgradeCostEntry e = baseCosts[i];
                if (e == null) continue;
                int count = s.ScaleWorldBuildMaterialCount(e.count);
                if (count <= 0) continue;
                scaled.Add(new OutpostUpgradeCostEntry
                {
                    thingDef = e.thingDef,
                    count = count,
                    costMode = e.costMode
                });
            }
            return scaled.Count > 0 ? scaled : EmptyCosts;
        }

        public static List<OutpostUpgradeCostEntry> GetMaterialCostsForRoad(SettlementTier tier) =>
            ApplyWorldBuildMaterialScale(GetMaterialCostsForRoadUnscaled(tier));

        public static List<OutpostUpgradeCostEntry> GetMaterialCostsForRoadBlock(RoadBlockKind kind) =>
            ApplyWorldBuildMaterialScale(GetMaterialCostsForRoadBlockUnscaled(kind));

        public static List<OutpostUpgradeCostEntry> GetMaterialCostsForSpikeTrap(SpikeTrapKind kind) =>
            ApplyWorldBuildMaterialScale(GetMaterialCostsForSpikeTrapUnscaled(kind));

        public static List<OutpostUpgradeCostEntry> GetMaterialCostsForAtTurret(AtTurretTier tier) =>
            ApplyWorldBuildMaterialScale(GetMaterialCostsForAtTurretUnscaled(tier));

        /// <summary>True when this actor pays world-build materials (player outpost or colony build site).</summary>
        public static bool ActorPaysWorldBuildMaterials(WorldObject actor)
        {
            if (actor == null) return false;
            return actor is WorldObject_WD_Outpost
                || ColonyWorldBuildUtility.IsPlayerColonyBuildActor(actor);
        }

        /// <summary>
        /// Afford check without allocating at non-1 material mult (iterates unscaled base + scaled need).
        /// </summary>
        public static bool HasWorldBuildMaterials(List<OutpostUpgradeCostEntry> baseCosts)
        {
            var s = WorldDominationMod.settings;
            float mult = s != null ? s.worldBuildMaterialCostMult : 1f;
            if (mult <= 0f) return true;
            if (baseCosts == null || baseCosts.Count == 0) return true;
            if (s == null || Mathf.Approximately(mult, 1f))
                return ColonyWorldBuildMaterials.HasMaterialCosts(baseCosts);

            Map colonyMap = ColonyWorldBuildMaterials.GetColonyMap();
            var warehouses = ColonyWorldBuildMaterials.GetContributingWarehouses();
            if (colonyMap == null && (warehouses == null || warehouses.Count == 0))
                return false;

            for (int i = 0; i < baseCosts.Count; i++)
            {
                OutpostUpgradeCostEntry e = baseCosts[i];
                if (e == null || e.count <= 0) continue;
                if (e.thingDef == null && !ColonyWorldBuildMaterials.IsAnyStoneBlocksCost(e)) continue;
                int need = s.ScaleWorldBuildMaterialCount(e.count);
                if (need <= 0) continue;
                int have = ColonyWorldBuildMaterials.CountHaveForCost(e);
                if (have < need) return false;
            }
            return true;
        }

        public static bool HasMaterialCostsForRoad(SettlementTier tier) =>
            HasWorldBuildMaterials(GetMaterialCostsForRoadUnscaled(tier));

        public static bool HasMaterialCostsForRoadBlock(RoadBlockKind kind) =>
            HasWorldBuildMaterials(GetMaterialCostsForRoadBlockUnscaled(kind));

        public static bool HasMaterialCostsForSpikeTrap(SpikeTrapKind kind) =>
            HasWorldBuildMaterials(GetMaterialCostsForSpikeTrapUnscaled(kind));

        public static bool HasMaterialCostsForAtTurret(AtTurretTier tier) =>
            HasWorldBuildMaterials(GetMaterialCostsForAtTurretUnscaled(tier));

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
        /// On success, stores a cost clone on the traveler for abort refunds.
        /// </summary>
        public static bool TryFinalizeMaterialsOrAbort(
            WorldObject origin,
            WorldObject_Traveler traveler,
            float strengthCost,
            List<OutpostUpgradeCostEntry> costs)
        {
            if (!ActorPaysWorldBuildMaterials(origin)) return true;
            if (costs == null || costs.Count == 0) return true;
            if (ColonyWorldBuildMaterials.TryDeductMaterialCosts(costs, out _))
            {
                traveler?.StoreConstructionMaterialCosts(ColonyWorldBuildMaterials.CloneCosts(costs));
                return true;
            }

            var comp = origin?.GetComponent<CompViralSpread>();
            if (traveler != null && !traveler.Destroyed)
                traveler.Destroy();
            if (comp != null && strengthCost > 0f)
                WorldActions_Utils.RefundExpeditionStrength(comp, strengthCost);
            return false;
        }

        public static bool IsWorldBuildConstructionMission(TravelerMission mission) =>
            mission == TravelerMission.RoadBuilding
            || mission == TravelerMission.RoadBlock
            || mission == TravelerMission.SpikeTrap
            || mission == TravelerMission.AtTurret
            || mission == TravelerMission.BridgeBuilding;

        /// <summary>
        /// Clean abort: refund strength once; refund materials if the paid work unit was never applied.
        /// </summary>
        public static void RefundConstructionAbort(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.constructionAbortRefunded) return;
            traveler.constructionAbortRefunded = true;
            TravelerEndpointUtility.RefundTravelerStrength(traveler, 1f);
            TryRefundUnappliedConstructionMaterials(traveler);
        }

        /// <summary>Arrival no-op / destroy without abort: return materials if work never applied.</summary>
        public static void TryRefundUnappliedConstructionMaterials(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.constructionMaterialsRefunded) return;
            if (traveler.constructionWorkApplied) return;
            if (traveler.constructionMaterialCosts == null || traveler.constructionMaterialCosts.Count == 0)
                return;
            traveler.constructionMaterialsRefunded = true;
            ColonyWorldBuildMaterials.TryRefundMaterialCosts(traveler.constructionMaterialCosts);
            traveler.constructionMaterialCosts = null;
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
            if (!WdMedievalMode.IsRoadTierAllowed(tier)) return false;

            float skill = ColonyWorldBuildUtility.GetActorConstructionSkillRaw(actor);
            int minC = WorldActions_Roads.GetMinConstructionToBuildRoad(tier);
            if (!MeetsConstruction(skill, minC)) return false;

            WdRoadTierDef def = WdBiomeTableResolver.GetRoadTierDef(tier);
            if (def?.researchPrerequisites != null && def.researchPrerequisites.Count > 0)
            {
                for (int i = 0; i < def.researchPrerequisites.Count; i++)
                {
                    if (!IsResearchMet(EffectiveResearch(def.researchPrerequisites[i]))) return false;
                }
                return true;
            }

            return IsResearchMet(GetRequiredResearchForRoad(tier));
        }

        public static bool MeetsRoadBlockRequirements(WorldObject actor, RoadBlockKind kind)
        {
            if (!WdMedievalMode.IsRoadBlockKindAllowed(kind)) return false;

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

        private const float CostIconSize = 18f;
        private const float CostIconGap = 4f;
        private const float CostCountMinW = 18f;

        private struct CostIconSnap
        {
            public ThingDef iconDef;
            public int need;
            public bool shortStock;
        }

        /// <summary>
        /// Grey the option if unmet; keep the normal label. Append requirements (construction, research, materials) to the tooltip.
        /// When materials are non-empty, also draws count + ThingIcon on the float-menu row (stock snapshotted once).
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

            AttachMaterialCostIcons(opt, materialCosts);
        }

        private static void AttachMaterialCostIcons(FloatMenuOption opt, List<OutpostUpgradeCostEntry> materialCosts)
        {
            if (opt == null || materialCosts == null || materialCosts.Count == 0)
                return;

            var snaps = new List<CostIconSnap>(materialCosts.Count);
            Text.Font = GameFont.Tiny;
            float totalW = 0f;
            for (int i = 0; i < materialCosts.Count; i++)
            {
                OutpostUpgradeCostEntry e = materialCosts[i];
                if (e == null || e.count <= 0) continue;
                ThingDef iconDef = ColonyWorldBuildMaterials.GetCostIconThingDef(e);
                if (iconDef == null) continue;
                int have = ColonyWorldBuildMaterials.CountHaveForCost(e);
                var snap = new CostIconSnap
                {
                    iconDef = iconDef,
                    need = e.count,
                    shortStock = have < e.count
                };
                snaps.Add(snap);
                float numW = Mathf.Max(CostCountMinW, Text.CalcSize(snap.need.ToString()).x);
                totalW += numW + CostIconSize + CostIconGap;
            }
            Text.Font = GameFont.Small;
            if (snaps.Count == 0) return;

            opt.extraPartWidth = totalW;
            opt.extraPartOnGUI = rect =>
            {
                DrawMaterialCostIcons(rect, snaps);
                return false;
            };
        }

        private static void DrawMaterialCostIcons(Rect rect, List<CostIconSnap> snaps)
        {
            if (snaps == null || snaps.Count == 0) return;
            float x = rect.x;
            float midY = rect.y + rect.height * 0.5f;
            Text.Font = GameFont.Tiny;
            for (int i = 0; i < snaps.Count; i++)
            {
                CostIconSnap s = snaps[i];
                string num = s.need.ToString();
                float numW = Mathf.Max(CostCountMinW, Text.CalcSize(num).x);
                Color prev = GUI.color;
                GUI.color = s.shortStock ? new Color(1f, 0.55f, 0.35f) : Color.white;
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(x, rect.y, numW, rect.height), num);
                Text.Anchor = TextAnchor.UpperLeft;
                Rect iconRect = new Rect(x + numW, midY - CostIconSize * 0.5f, CostIconSize, CostIconSize);
                if (s.iconDef != null)
                    Widgets.ThingIcon(iconRect, s.iconDef);
                GUI.color = prev;
                x += numW + CostIconSize + CostIconGap;
            }
            Text.Font = GameFont.Small;
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
