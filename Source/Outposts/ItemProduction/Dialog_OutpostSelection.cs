using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;

namespace TSA_WorldDomination
{
    /// <summary>Built once at startup; avoids filtering AllDefs every GUI frame while the selection dialog is open. Sorted by outpost tier (ascending), then defName.</summary>
    [StaticConstructorOnStartup]
    internal static class WD_OutpostSelectionCachedDefs
    {
        private static readonly HashSet<string> AbstractOutpostDefNames = new HashSet<string> { "TSA_WD_Outpost", "TSA_WD_Outpost_SimpleProduction" };
        public static readonly List<WorldObjectDef> List;

        static WD_OutpostSelectionCachedDefs()
        {
            var all = DefDatabase<WorldObjectDef>.AllDefsListForReading;
            List = new List<WorldObjectDef>();
            for (int i = 0; i < all.Count; i++)
            {
                var d = all[i];
                if (d.worldObjectClass != null
                    && typeof(WorldObject_WD_Outpost).IsAssignableFrom(d.worldObjectClass)
                    && !AbstractOutpostDefNames.Contains(d.defName))
                    List.Add(d);
            }

            List.Sort((a, b) =>
            {
                int c = WorldObject_WD_Outpost.GetOutpostTier(a).CompareTo(WorldObject_WD_Outpost.GetOutpostTier(b));
                if (c != 0) return c;
                return string.CompareOrdinal(a.defName, b.defName);
            });
        }
    }

    /// <summary>Window to select outpost type and establish at current caravan tile. Uses Outpost_EstablishmentRequirements for min distance, tile, cost.</summary>
    [StaticConstructorOnStartup]
    public class Dialog_OutpostSelection : Window
    {
        private int tile;
        private string originalName;
        private int ruinsId;
        private SettlementTier tier;
        private ConquestOpportunityContext conquestContext;
        private bool conquestChoiceConsumed;
        private Caravan fromCaravan;
        private readonly List<PlayerPawnRosterEntry> remoteEstablishEntries;
        private readonly MapParent remoteEstablishSource;
        private bool remoteRetargetOnClose;
        private List<Pawn> cachedRemotePawns;
        private static readonly List<Pawn> EmptyPawnListForCost = new List<Pawn>();
        private Vector2 rightScrollPos;
        private Vector2 leftDetailScrollPos;
        private string outpostSearchFilter = "";
        private string selectedOutpostDefName;
        private EstablishmentTabFilter establishmentFilter = EstablishmentTabFilter.All;
        /// <summary>Read-only reference: same layout as caravan establishment but no Establish action (world map toolbar).</summary>
        private readonly bool requirementsPreviewOnly;
        /// <summary>Tile known; pawns chosen next in <see cref="Window_RemoteEstablishPawns"/>.</summary>
        private readonly bool tileFirstRemoteEstablish;

        private enum EstablishmentTabFilter
        {
            All,
            Buildable
        }

        private bool cacheBuilt;
        private bool cachedTileValid;
        private string cachedHeadline;
        private string cachedBiomeName;
        private string cachedTerrainVal;
        private string cachedFertPctLabel;
        private string cachedAnimPctLabel;
        private string cachedFishPctLabel;
        private string cachedMinePctLabel;
        private int cachedFertPct;
        private int cachedAnimPct;
        private int cachedFishPct;
        private int cachedMinePct;
        private string cachedBiomeTooltip;
        private string cachedTerrainTooltip;
        private string cachedFertTip;
        private string cachedHuntTip;
        private string cachedFishTip;
        private string cachedMiningTip;
        private string cachedConquestHeader;
        private string cachedInfoOnlyTip;
        private string cachedColName;
        private string cachedColTerrain;
        private string cachedColFertility;
        private string cachedColAnimals;
        private string cachedColFish;
        private string cachedColMining;
        private string cachedCostLabel;
        private string cachedNoCostLabel;
        private string cachedInfoOnlyLabel;
        private string cachedEstablishLabel;
        private string cachedBackLabel;
        private string cachedSearchPlaceholder;
        private string cachedChooseHeader;
        private string cachedSelectedDetailHeader;
        private string cachedNoSelection;
        private string cachedSkillsHeader;
        private string cachedRequirementsHeader;
        private string cachedTileProximityBlockedHint;
        private string cachedTileProximityBlockedTip;
        private EstablishmentRowCache[] cachedRows;

        private bool IsRemoteEstablish =>
            remoteEstablishEntries != null && remoteEstablishEntries.Count > 0 && remoteEstablishSource != null;

        private bool IsTileFirstRemoteEstablish => tileFirstRemoteEstablish;

        /// <summary>Pawn-backed remote send or tile-first remote (colony warehouses for cost).</summary>
        private bool IsAnyRemoteEstablishPath => IsRemoteEstablish || IsTileFirstRemoteEstablish;

        public override Vector2 InitialSize => new Vector2(960f, 620f);

        /// <param name="fromCaravan">If set, caravan pawns are converted to virtual pawns and removed from the caravan; otherwise pawns are generated.</param>
        /// <param name="requirementsPreviewOnly">If true, shows costs and requirements for planning only; <paramref name="fromCaravan"/> must be null.</param>
        /// <param name="remoteEstablishEntries">Colony roster selection for remote establish send (AllPlayerPawns).</param>
        /// <param name="tileFirstRemoteEstablish">Tile selected first; Establish opens the colony pawn picker instead of launching.</param>
        public Dialog_OutpostSelection(
            int tile,
            string name,
            int ruinsId,
            SettlementTier tier,
            ConquestOpportunityContext conquestContext,
            Caravan fromCaravan = null,
            bool requirementsPreviewOnly = false,
            List<PlayerPawnRosterEntry> remoteEstablishEntries = null,
            MapParent remoteEstablishSource = null,
            bool tileFirstRemoteEstablish = false)
        {
            this.tile = tile;
            originalName = name;
            this.ruinsId = ruinsId;
            this.tier = tier;
            this.conquestContext = conquestContext;
            this.fromCaravan = fromCaravan;
            this.requirementsPreviewOnly = requirementsPreviewOnly;
            this.remoteEstablishEntries = remoteEstablishEntries;
            this.remoteEstablishSource = remoteEstablishSource;
            this.tileFirstRemoteEstablish = tileFirstRemoteEstablish;
            doCloseButton = conquestContext == null && !IsRemoteEstablish;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void PostClose()
        {
            base.PostClose();
            if (conquestContext != null && !conquestChoiceConsumed)
                conquestContext.ReopenMenuIfActive();

            if (requirementsPreviewOnly)
                EnsureRequirementsPreviewTargetingAfterDialogClosed();

            if (IsRemoteEstablish)
            {
                if (remoteRetargetOnClose)
                    RemoteOutpostEstablishSession.RestartTargetingWithPending();
                else
                    RemoteOutpostEstablishSession.Clear();
            }
        }

        public override void Close(bool doCloseSound = true)
        {
            if (requirementsPreviewOnly)
            {
                suppressEstablishmentPreviewEnd = true;
                base.Close(doCloseSound);
                suppressEstablishmentPreviewEnd = false;
                EnsureRequirementsPreviewTargetingAfterDialogClosed();
                return;
            }

            base.Close(doCloseSound);
        }

        private void RebuildStringCache()
        {
            if (cacheBuilt) return;
            cacheBuilt = true;

            var grid = Find.WorldGrid;
            cachedTileValid = grid != null && tile >= 0 && tile < grid.TilesCount;

            cachedHeadline = requirementsPreviewOnly
                ? "TSA_WD_RequirementsPreview_Headline".Translate().ToString()
                : IsTileFirstRemoteEstablish
                    ? "TSA_WD_TileFirstEstablish_Headline".Translate().ToString()
                    : IsRemoteEstablish
                        ? "TSA_WD_RemoteEstablish_Headline".Translate().ToString()
                        : (!originalName.NullOrEmpty()
                            ? (conquestContext != null && conquestContext.fromSettlementBuy
                                ? "TSA_WD_SelectOutpostType_Purchased".Translate(originalName).ToString()
                                : "TSA_WD_SelectOutpostType_Ruins".Translate(originalName).ToString())
                            : "TSA_WD_SelectOutpostType".Translate().ToString());

            if (IsRemoteEstablish)
                cachedRemotePawns = RemoteOutpostEstablishUtility.CollectPawns(remoteEstablishEntries);

            if (cachedTileValid)
            {
                cachedBiomeName = WorldTileInfo.GetBiomeLabel(tile).CapitalizeFirst();
                Hilliness hill = WorldTileInfo.GetHilliness(tile);
                cachedTerrainVal = GetTerrainLabel(hill);
                int fertPct = Mathf.RoundToInt(WorldTileProductivity.GetFarmingFertilityScore(tile) * 100f);
                int animPct = Mathf.RoundToInt(WorldTileProductivity.GetHuntingScore(tile) * 100f);
                int fishPct = Mathf.RoundToInt(WorldTileProductivity.GetFishingScore(tile) * 100f);
                int minePct = Mathf.RoundToInt(WorldTileProductivity.GetMiningOutputMultiplier(tile) * 100f);
                cachedFertPct = fertPct;
                cachedAnimPct = animPct;
                cachedFishPct = fishPct;
                cachedMinePct = minePct;
                cachedFertPctLabel = "TSA_WD_Biome_FertilityPercent".Translate(fertPct).ToString();
                cachedAnimPctLabel = "TSA_WD_Biome_AnimalsPercent".Translate(animPct).ToString();
                cachedFishPctLabel = "TSA_WD_Biome_FishPercent".Translate(fishPct).ToString();
                cachedMinePctLabel = "TSA_WD_Biome_MiningPercent".Translate(minePct).ToString();
                cachedBiomeTooltip = "TSA_WD_Biome_Tooltip_Biome".Translate().ToString();
                cachedTerrainTooltip = "TSA_WD_Biome_Tooltip_Terrain".Translate().ToString();
                cachedFertTip = WorldTileProductivity.GetFarmingFertilityTooltipText(tile);
                cachedHuntTip = WorldTileProductivity.GetHuntingScoreTooltipText(tile);
                cachedFishTip = WorldTileProductivity.GetFishingScoreTooltipText(tile);
                cachedMiningTip = WorldTileProductivity.GetMiningEfficiencyTooltipText(tile);
            }

            cachedTileProximityBlockedHint = null;
            cachedTileProximityBlockedTip = null;
            if (requirementsPreviewOnly && cachedTileValid
                && Outpost_EstablishmentRequirements.IsTileBlockedByMinDistanceCached(tile))
            {
                cachedTileProximityBlockedHint = "TSA_WD_RequirementsPreview_TileBlockedProximity".Translate().ToString();
                Outpost_EstablishmentRequirements.MeetsMinDistanceOnly(tile, out string blockReason);
                cachedTileProximityBlockedTip = blockReason;
            }

            if (fromCaravan == null && !requirementsPreviewOnly && !IsAnyRemoteEstablishPath)
                cachedConquestHeader = "TSA_WD_ConquestDialog_Header".Translate((int)tier + 1).ToString();

            string skillTooltipNormal = "TSA_WD_CumSkill_Tooltip".Translate().ToString();
            string skillTooltipDeferred = "TSA_WD_TileFirstEstablish_CheckedWhenPawns".Translate().ToString();
            cachedInfoOnlyTip = "TSA_WD_RequirementsPreview_InfoOnlyTip".Translate().ToString();
            cachedColName = "TSA_WD_Biome_ColName".Translate().ToString();
            cachedColTerrain = "TSA_WD_Biome_ColTerrain".Translate().ToString();
            cachedColFertility = "TSA_WD_Biome_ColFertility".Translate().ToString();
            cachedColAnimals = "TSA_WD_Biome_ColAnimals".Translate().ToString();
            cachedColFish = "TSA_WD_Biome_ColFish".Translate().ToString();
            cachedColMining = "TSA_WD_Biome_ColMining".Translate().ToString();
            cachedCostLabel = "TSA_WD_CostToEstablish".Translate().ToString();
            cachedNoCostLabel = "TSA_WD_NoCostConquest".Translate().ToString();
            cachedInfoOnlyLabel = "TSA_WD_RequirementsPreview_InfoOnly".Translate().ToString();
            cachedEstablishLabel = IsTileFirstRemoteEstablish
                ? "TSA_WD_TileFirstEstablish_ChooseColonists".Translate().ToString()
                : IsRemoteEstablish
                    ? "TSA_WD_RemoteEstablish_Establish".Translate().ToString()
                    : "TSA_WD_Select_Establish".Translate().ToString();
            cachedBackLabel = "TSA_WD_Conquest_Back".Translate().ToString();
            cachedSearchPlaceholder = "TSA_WD_OutpostUpgrades_SearchPlaceholder".Translate().ToString();
            if (cachedSearchPlaceholder.Contains("TSA_WD_")) cachedSearchPlaceholder = "Filter by name...";
            cachedChooseHeader = OutpostTranslationUtil.Key("TSA_WD_OutpostEstablish_ChooseHeader");
            cachedSelectedDetailHeader = OutpostTranslationUtil.Key("TSA_WD_OutpostEstablish_SelectedDetail");
            cachedNoSelection = OutpostTranslationUtil.Key("TSA_WD_OutpostEstablish_NoSelection");
            cachedSkillsHeader = OutpostTranslationUtil.Key("TSA_WD_OutpostEstablish_Skills");
            cachedRequirementsHeader = OutpostTranslationUtil.Key("TSA_WD_OutpostEstablish_Requirements");

            var defs = WD_OutpostSelectionCachedDefs.List;
            cachedRows = new EstablishmentRowCache[defs.Count];

            bool showCostColumn = fromCaravan != null || requirementsPreviewOnly || IsAnyRemoteEstablishPath;
            Map remoteMap = null;
            List<WorldObject_WD_Outpost> remoteWarehouses = null;
            if (IsRemoteEstablish)
            {
                remoteMap = remoteEstablishSource.Map;
                remoteWarehouses = ColonyWarehouseStockUtility.GetAllWarehouses();
            }
            else if (IsTileFirstRemoteEstablish)
            {
                remoteMap = Outpost_PowerPlant.GetPlayerColonyMap();
                remoteWarehouses = ColonyWarehouseStockUtility.GetAllWarehouses();
            }

            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                var row = new EstablishmentRowCache();

                row.displaySkills = GetDisplaySkillDefs(def);

                if (row.displaySkills.Count > 0)
                {
                    if (fromCaravan != null)
                    {
                        var lines = new List<string>();
                        foreach (var skill in row.displaySkills)
                        {
                            int sum = GetCumulativeSkillForCaravan(fromCaravan, skill);
                            lines.Add("TSA_WD_CumCaravanSkill".Translate(skill.LabelCap, sum).ToString());
                        }
                        row.skillLine = string.Join("\n", lines);
                        row.skillTooltip = skillTooltipNormal;
                    }
                    else if (IsRemoteEstablish)
                    {
                        var lines = new List<string>();
                        foreach (var skill in row.displaySkills)
                        {
                            int sum = RemoteOutpostEstablishUtility.GetCumulativeSkill(cachedRemotePawns, skill);
                            lines.Add("TSA_WD_CumCaravanSkill".Translate(skill.LabelCap, sum).ToString());
                        }
                        row.skillLine = string.Join("\n", lines);
                        row.skillTooltip = skillTooltipNormal;
                    }
                    else if (requirementsPreviewOnly || IsTileFirstRemoteEstablish)
                    {
                        var linesPv = new List<string>();
                        foreach (var skill in row.displaySkills)
                            linesPv.Add("TSA_WD_CumCaravanSkill".Translate(skill.LabelCap, 0).ToString());
                        row.skillLine = string.Join("\n", linesPv);
                        row.skillTooltip = IsTileFirstRemoteEstablish ? skillTooltipDeferred : skillTooltipNormal;
                    }
                    else
                    {
                        var lines = new List<string>();
                        for (int si = 0; si < row.displaySkills.Count; si++)
                            lines.Add(row.displaySkills[si].LabelCap);
                        row.skillLine = string.Join("\n", lines);
                        row.skillTooltip = skillTooltipNormal;
                    }
                }

                row.outpostTooltip = Outpost_Establishment_UI.GetOutpostTypeTooltip(def);

                if (showCostColumn)
                {
                    var cost = Outpost_EstablishmentRequirements.GetCost(def);
                    var costList = new List<EstablishmentCostItem>();
                    foreach (var c in cost)
                    {
                        if (c?.thingDef == null) continue;
                        bool costWaived = !Outpost_EstablishmentRequirements.EnforceCost;
                        bool colorByAvailability = !costWaived && (fromCaravan != null || IsAnyRemoteEstablishPath);
                        bool met = true;
                        int have = 0;
                        if (!costWaived && fromCaravan != null)
                        {
                            have = Outpost_EstablishmentRequirements.CountThingOnCaravan(fromCaravan, c.thingDef);
                            met = have >= c.count;
                        }
                        else if (!costWaived && IsRemoteEstablish)
                        {
                            have = ColonyWarehouseStockUtility.CountAvailable(remoteMap, remoteWarehouses, c.thingDef, cachedRemotePawns);
                            met = have >= c.count;
                        }
                        else if (!costWaived && IsTileFirstRemoteEstablish)
                        {
                            have = ColonyWarehouseStockUtility.CountAvailable(remoteMap, remoteWarehouses, c.thingDef, EmptyPawnListForCost);
                            met = have >= c.count;
                        }
                        string tip = costWaived
                            ? "TSA_WD_Req_WaivedInSettings".Translate().ToString()
                            : (colorByAvailability
                                ? c.thingDef.LabelCap + ": " + have + "/" + c.count
                                : c.thingDef.LabelCap + ": " + c.count);
                        costList.Add(new EstablishmentCostItem
                        {
                            thingDef = c.thingDef,
                            countLabel = "x" + c.count,
                            tooltipLabel = tip,
                            met = costWaived || met,
                            colorByAvailability = colorByAvailability,
                            waived = costWaived
                        });
                    }
                    row.costItems = costList.ToArray();
                }

                row.reqApplies = new bool[9];
                row.reqs = new EstablishmentReqLine[9];
                row.reqCount = 0;
                for (int li = 1; li <= 9; li++)
                {
                    bool extraCheck = li != 7 || (fromCaravan != null || requirementsPreviewOnly || IsAnyRemoteEstablishPath);
                    if (extraCheck && RequirementApplies(def, li, fromCaravan, requirementsPreviewOnly, IsAnyRemoteEstablishPath))
                    {
                        row.reqApplies[li - 1] = true;
                        row.reqCount++;
                        row.reqs[li - 1] = BuildReqLine(def, li);
                    }
                }

                cachedRows[i] = row;
            }
        }

        private static bool OutpostDefMatchesSearch(WorldObjectDef def, string filter)
        {
            string q = filter?.Trim();
            if (string.IsNullOrEmpty(q)) return true;
            return TokenMatches(def?.LabelCap, q)
                || TokenMatches(def?.label, q)
                || TokenMatches(def?.defName, q);
        }

        private static bool TokenMatches(string haystack, string needle)
        {
            return !haystack.NullOrEmpty()
                && !needle.NullOrEmpty()
                && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private EstablishmentReqLine BuildReqLine(WorldObjectDef def, int lineIndex)
        {
            var line = new EstablishmentReqLine();

            if (requirementsPreviewOnly && !cachedTileValid && (lineIndex == 2 || lineIndex == 3 || lineIndex == 4 || lineIndex == 6 || lineIndex == 8))
            {
                line.text = "TSA_WD_RequirementsPreview_TileReqPending".Translate().ToString();
                line.tooltip = "TSA_WD_RequirementsPreview_TileReqPendingTip".Translate().ToString();
                line.met = true;
                line.useFixedColor = true;
                line.color = new Color(0.65f, 0.65f, 0.65f);
                return line;
            }

            switch (lineIndex)
            {
                case 1:
                    if (IsTileFirstRemoteEstablish || (requirementsPreviewOnly && fromCaravan == null && !IsRemoteEstablish))
                    {
                        var extPv = def?.GetModExtension<OutpostDefExtension>();
                        var skillSetsPv = extPv?.MinCumulativeSkill;
                        if (skillSetsPv == null || skillSetsPv.Count == 0 || !HasAnySkillRequirement(skillSetsPv))
                        {
                            line.text = "TSA_WD_Req_CumSkill_None".Translate().ToString();
                            line.tooltip = "TSA_WD_Req_Tooltip_CumSkillNone".Translate().ToString();
                        }
                        else
                        {
                            var partsPv = new List<string>();
                            foreach (var set in skillSetsPv)
                            {
                                if (set == null) continue;
                                foreach (var kv in set.GetRequirements())
                                {
                                    if (kv.Key == null || kv.Value <= 0) continue;
                                    partsPv.Add(kv.Key.LabelCap + " " + 0 + "/" + kv.Value);
                                }
                            }
                            line.text = partsPv.Count > 0 ? string.Join(", ", partsPv) : "-";
                            line.tooltip = IsTileFirstRemoteEstablish
                                ? "TSA_WD_TileFirstEstablish_CheckedWhenPawns".Translate().ToString()
                                : "TSA_WD_Req_Tooltip_CumSkillUnmet".Translate().ToString();
                        }
                        line.met = true;
                        line.useFixedColor = true;
                        line.color = new Color(0.75f, 0.75f, 0.75f);
                        ApplyEstablishmentFlagWaiver(def, lineIndex, ref line);
                        return line;
                    }
                    var ext = def?.GetModExtension<OutpostDefExtension>();
                    var skillSets = ext?.MinCumulativeSkill;
                    if (skillSets == null || skillSets.Count == 0 || !HasAnySkillRequirement(skillSets))
                    {
                        line.text = "TSA_WD_Req_CumSkill_None".Translate().ToString();
                        line.met = true;
                        line.tooltip = "TSA_WD_Req_Tooltip_CumSkillNone".Translate().ToString();
                    }
                    else
                    {
                        var parts = new List<string>();
                        bool allMet = true;
                        foreach (var set in skillSets)
                        {
                            if (set == null) continue;
                            foreach (var kv in set.GetRequirements())
                            {
                                if (kv.Key == null || kv.Value <= 0) continue;
                                int have = fromCaravan != null
                                    ? Outpost_EstablishmentRequirements.GetCumulativeCaravanSkillForSkill(fromCaravan, kv.Key)
                                    : (IsRemoteEstablish
                                        ? RemoteOutpostEstablishUtility.GetCumulativeSkill(cachedRemotePawns, kv.Key)
                                        : 0);
                                bool skillMet = have >= kv.Value;
                                if (!skillMet) allMet = false;
                                parts.Add(kv.Key.LabelCap + " " + have + "/" + kv.Value);
                            }
                        }
                        line.text = parts.Count > 0 ? string.Join(", ", parts) : "â€”";
                        line.met = parts.Count == 0 || allMet;
                        line.tooltip = line.met ? "TSA_WD_Req_Tooltip_CumSkillMet".Translate().ToString() : "TSA_WD_Req_Tooltip_CumSkillUnmet".Translate().ToString();
                    }
                    break;
                case 2:
                {
                    int fertPct = Mathf.RoundToInt(WorldTileProductivity.GetFarmingFertilityScore(tile) * 100f);
                    int minFert = def?.GetModExtension<OutpostDefExtension>()?.minFertilityPercent ?? 30;
                    line.text = "TSA_WD_Req_FertilityMin".Translate(fertPct, minFert).ToString();
                    bool waterCovered = Find.WorldGrid != null && tile >= 0 && tile < Find.WorldGrid.TilesCount && Find.WorldGrid[tile].WaterCovered;
                    line.met = !waterCovered && fertPct >= minFert;
                    line.tooltip = waterCovered ? "TSA_WD_Req_Tooltip_FertilityWater".Translate().ToString() : (fertPct >= minFert ? "TSA_WD_Req_Tooltip_FertilityMet".Translate().ToString() : "TSA_WD_Req_Tooltip_FertilityBelow".Translate(minFert).ToString());
                    break;
                }
                case 3:
                {
                    string biomeLabel = WorldTileInfo.GetBiomeLabel(tile).CapitalizeFirst();
                    line.text = "TSA_WD_Req_Biome".Translate(biomeLabel).ToString();
                    line.met = Outpost_EstablishmentRequirements.BiomeAllowedForOutpost(tile, def, out string biomeReason);
                    line.tooltip = line.met ? "TSA_WD_Req_Tooltip_BiomeMet".Translate(def.label).ToString() : (biomeReason ?? "TSA_WD_Req_Tooltip_BiomeNotAllowed".Translate().ToString());
                    break;
                }
                case 4:
                {
                    int miningPct = Mathf.RoundToInt(WorldTileProductivity.GetMiningOutputMultiplier(tile) * 100f);
                    line.text = "TSA_WD_Req_MiningPotential".Translate(miningPct).ToString();
                    line.met = Outpost_EstablishmentRequirements.TileSatisfiesOutpostType(tile, def, out string terrainReason);
                    line.tooltip = line.met ? "TSA_WD_Req_Tooltip_MiningMet".Translate().ToString() : (terrainReason ?? "TSA_WD_Req_Tooltip_MiningNot".Translate().ToString());
                    break;
                }
                case 5:
                {
                    var projects = Outpost_EstablishmentRequirements.GetRequiredResearchProjects(def);
                    bool researchMet = Outpost_EstablishmentRequirements.ResearchRequirementsMet(def, out string researchReason);
                    line.met = researchMet;
                    if (projects.Count == 0)
                    {
                        line.text = "TSA_WD_Req_ResearchNone".Translate().ToString();
                        line.tooltip = "TSA_WD_Req_Tooltip_ResearchNone".Translate().ToString();
                    }
                    else
                    {
                        line.text = researchMet ? "TSA_WD_Req_ResearchOK".Translate().ToString() : "TSA_WD_Req_ResearchMissing".Translate().ToString();
                        line.tooltip = researchMet ? "TSA_WD_Req_Tooltip_ResearchMet".Translate().ToString() : (researchReason ?? "TSA_WD_Req_Tooltip_ResearchRequired".Translate().ToString());
                    }
                    break;
                }
                case 6:
                {
                    string dn = def?.defName?.ToLowerInvariant() ?? "";
                    if (dn.Contains("fishing"))
                    {
                        int fishPct = Mathf.RoundToInt(WorldTileProductivity.GetFishingScore(tile) * 100f);
                        int minFish = def?.GetModExtension<OutpostDefExtension>()?.minFishAbundancePercent ?? 30;
                        line.text = "TSA_WD_Req_FishingMin".Translate(fishPct, minFish).ToString();
                        bool tileOk = Find.WorldGrid != null && tile >= 0 && tile < Find.WorldGrid.TilesCount
                            && !Find.WorldGrid[tile].WaterCovered;
                        bool coastalOk = tileOk && Find.WorldGrid[tile].IsCoastal;
                        bool hasFish = coastalOk && Outpost_Fishing.HasAnySaltwaterFish(tile);
                        line.met = hasFish && fishPct >= minFish;
                        if (line.met)
                            line.tooltip = "TSA_WD_Req_Tooltip_FishingMet".Translate().ToString();
                        else if (!coastalOk)
                            line.tooltip = "TSA_WD_Req_Tooltip_FishingNotCoastal".Translate().ToString();
                        else if (!hasFish)
                            line.tooltip = "TSA_WD_Req_Tooltip_FishingNone".Translate().ToString();
                        else
                            line.tooltip = "TSA_WD_Req_Tooltip_FishingBelow".Translate(minFish).ToString();
                    }
                    else
                    {
                        int huntPct = Mathf.RoundToInt(WorldTileProductivity.GetHuntingScore(tile) * 100f);
                        int minAnim = def?.GetModExtension<OutpostDefExtension>()?.minAnimalAbundancePercent ?? 30;
                        line.text = "TSA_WD_Req_HuntingMin".Translate(huntPct, minAnim).ToString();
                        bool hasAnimals = Find.WorldGrid != null && tile >= 0 && tile < Find.WorldGrid.TilesCount && !Find.WorldGrid[tile].WaterCovered
                            && WorldTileInfo.GetBiome(tile)?.animalDensity > 0f;
                        line.met = hasAnimals && huntPct >= minAnim;
                        line.tooltip = line.met ? "TSA_WD_Req_Tooltip_HuntingMet".Translate().ToString() : (huntPct < minAnim ? "TSA_WD_Req_Tooltip_HuntingBelow".Translate(minAnim).ToString() : "TSA_WD_Req_Tooltip_HuntingNone".Translate().ToString());
                    }
                    break;
                }
                case 7:
                {
                    if (IsTileFirstRemoteEstablish || (requirementsPreviewOnly && fromCaravan == null && !IsRemoteEstablish))
                    {
                        int minPawnsPv = Outpost_EstablishmentRequirements.GetMinPawnsToFound(def);
                        line.text = "TSA_WD_Req_MinPawns".Translate(0, minPawnsPv).ToString();
                        line.tooltip = IsTileFirstRemoteEstablish
                            ? "TSA_WD_TileFirstEstablish_CheckedWhenPawns".Translate().ToString()
                            : "TSA_WD_Req_Tooltip_MinPawnsUnmet".Translate(minPawnsPv, 0).ToString();
                        line.met = true;
                        line.useFixedColor = true;
                        line.color = new Color(0.75f, 0.75f, 0.75f);
                        ApplyEstablishmentFlagWaiver(def, lineIndex, ref line);
                        return line;
                    }
                    int minPawns = Outpost_EstablishmentRequirements.GetMinPawnsToFound(def);
                    int pawnCount = 0;
                    if (fromCaravan?.PawnsListForReading != null)
                    {
                        var cpawns = fromCaravan.PawnsListForReading;
                        for (int pi = 0; pi < cpawns.Count; pi++)
                            if (cpawns[pi]?.RaceProps?.Humanlike == true && !cpawns[pi].Dead) pawnCount++;
                    }
                    else if (IsRemoteEstablish)
                        pawnCount = RemoteOutpostEstablishUtility.CountHumanlikes(cachedRemotePawns);
                    line.text = "TSA_WD_Req_MinPawns".Translate(pawnCount, minPawns).ToString();
                    line.met = pawnCount >= minPawns;
                    line.tooltip = line.met ? "TSA_WD_Req_Tooltip_MinPawnsMet".Translate().ToString() : "TSA_WD_Req_Tooltip_MinPawnsUnmet".Translate(minPawns, pawnCount).ToString();
                    break;
                }
                case 8:
                {
                    var extNearby = def?.GetModExtension<OutpostDefExtension>();
                    int radius = Mathf.Max(0, extNearby?.minNearbyRadiusTiles ?? 0);
                    int minNearby = extNearby?.minNearbySettlementsOrOutposts ?? 0;
                    int countNearby = Outpost_EstablishmentRequirements.CountNearbySettlementsOrOutposts(tile, radius);
                    line.met = Outpost_EstablishmentRequirements.MeetsMinNearbySettlements(tile, def, out string nearbyReason);
                    line.text = "TSA_WD_Req_MinNearby".Translate(countNearby, minNearby, radius).ToString();
                    if (line.text.Contains("TSA_WD_Req_")) line.text = "Settlements in radius: " + countNearby + " / " + minNearby + " (" + radius + " tiles)";
                    line.tooltip = line.met ? "TSA_WD_Req_Tooltip_MinNearbyMet".Translate(minNearby, radius).ToString() : (nearbyReason ?? "TSA_WD_Req_Tooltip_MinNearbyUnmet".Translate(minNearby, radius, countNearby).ToString());
                    if (line.tooltip.Contains("TSA_WD_Req_")) line.tooltip = line.met ? "At least " + minNearby + " settlements or outposts from other factions (neutral or allied) within " + radius + " tiles." : "Need at least " + minNearby + " within " + radius + " tiles. Found: " + countNearby;
                    break;
                }
                case 9:
                {
                    int maxAllowedTier = (int)tier + 1;
                    int outpostTierVal = WorldObject_WD_Outpost.GetOutpostTier(def);
                    line.met = outpostTierVal <= maxAllowedTier;
                    line.text = "TSA_WD_Req_ConquestTier".Translate(outpostTierVal, maxAllowedTier).ToString();
                    line.tooltip = line.met ? "TSA_WD_Req_Tooltip_ConquestTierMet".Translate(maxAllowedTier).ToString() : "TSA_WD_Req_Tooltip_ConquestTierUnmet".Translate(outpostTierVal, maxAllowedTier).ToString();
                    break;
                }
            }
            ApplyEstablishmentFlagWaiver(def, lineIndex, ref line);
            return line;
        }

        private static void MarkReqWaived(ref EstablishmentReqLine line, string tip)
        {
            line.met = true;
            line.useFixedColor = true;
            line.color = new Color(0.75f, 0.75f, 0.75f);
            line.tooltip = tip;
        }

        private void ApplyEstablishmentFlagWaiver(WorldObjectDef def, int lineIndex, ref EstablishmentReqLine line)
        {
            string settingsTip = "TSA_WD_Req_WaivedInSettings".Translate();
            switch (lineIndex)
            {
                case 1:
                    if (!Outpost_EstablishmentRequirements.EnforceMinSkill)
                        MarkReqWaived(ref line, settingsTip);
                    break;
                case 2:
                    if (!Outpost_EstablishmentRequirements.EnforceFertility)
                        MarkReqWaived(ref line, settingsTip);
                    break;
                case 3:
                    if (!Outpost_EstablishmentRequirements.EnforceBiome)
                        MarkReqWaived(ref line, settingsTip);
                    break;
                case 4:
                    if (!Outpost_EstablishmentRequirements.EnforceMiningTerrain)
                        MarkReqWaived(ref line, settingsTip);
                    break;
                case 5:
                    bool conquest = fromCaravan == null && !requirementsPreviewOnly && !IsAnyRemoteEstablishPath;
                    if (conquest)
                        MarkReqWaived(ref line, "TSA_WD_Req_WaivedConquestResearch".Translate());
                    else if (!Outpost_EstablishmentRequirements.EnforceResearch)
                        MarkReqWaived(ref line, settingsTip);
                    break;
                case 6:
                {
                    string dn = def?.defName?.ToLowerInvariant() ?? "";
                    if (dn.Contains("fishing"))
                    {
                        bool coastalOk = Find.WorldGrid != null && tile >= 0 && tile < Find.WorldGrid.TilesCount
                            && !Find.WorldGrid[tile].WaterCovered
                            && Find.WorldGrid[tile].IsCoastal;
                        if (coastalOk && !Outpost_EstablishmentRequirements.EnforceFishAbundance)
                            MarkReqWaived(ref line, settingsTip);
                    }
                    else if (!Outpost_EstablishmentRequirements.EnforceAnimalAbundance)
                        MarkReqWaived(ref line, settingsTip);
                    break;
                }
                case 7:
                    if (!Outpost_EstablishmentRequirements.EnforceMinPawns)
                        MarkReqWaived(ref line, settingsTip);
                    break;
                case 8:
                    if (!Outpost_EstablishmentRequirements.EnforceNearbySettlements)
                        MarkReqWaived(ref line, settingsTip);
                    break;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (!cacheBuilt)
                RebuildStringCache();

            var defs = WD_OutpostSelectionCachedDefs.List;
            if (defs.Count == 0)
            {
                Widgets.Label(new Rect(0, 0, inRect.width, 40f), "TSA_WD_SelectOutpost_NoDefs".Translate());
                return;
            }

            Rect body = inRect.ContractedBy(10f);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(body.x, body.y, body.width, 30f), cachedHeadline);
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(body.x, body.y + 32f, body.width);

            const float ColGap = 18f;
            const float bottomReserveClose = 44f;
            bool showBackButton = (conquestContext != null || IsRemoteEstablish) && !requirementsPreviewOnly;
            float bottomReserve = doCloseButton ? bottomReserveClose : (showBackButton ? CloseButSize.y + 8f : 0f);

            float columnsTop = body.y + 38f;
            float columnsBottom = body.yMax - bottomReserve;
            float leftW = Mathf.Max(260f, body.width * 0.42f);
            Rect leftArea = new Rect(body.x, columnsTop, leftW, columnsBottom - columnsTop);
            Rect rightArea = new Rect(body.x + leftW + ColGap, columnsTop, body.xMax - (body.x + leftW + ColGap), columnsBottom - columnsTop);
            Widgets.DrawLineVertical(body.x + leftW + ColGap * 0.5f, columnsTop, columnsBottom - columnsTop);

            EnsureDefaultSelection(defs);
            DrawLeftColumn(leftArea, defs);
            DrawRightColumn(rightArea, defs);

            if (showBackButton)
            {
                Rect backRect = new Rect(body.x, body.yMax - CloseButSize.y, CloseButSize.x, CloseButSize.y);
                if (Widgets.ButtonText(backRect, cachedBackLabel))
                {
                    if (IsRemoteEstablish)
                        remoteRetargetOnClose = true;
                    Close();
                }
            }
        }

        private void DrawLeftColumn(Rect leftArea, List<WorldObjectDef> defs)
        {
            float lx = leftArea.x;
            float lw = leftArea.width;
            float ly = leftArea.y;

            string invalidHint = requirementsPreviewOnly
                ? "TSA_WD_RequirementsPreview_SelectTileHint".Translate().ToString()
                : "TSA_WD_RequirementsPreview_InvalidTile".Translate().ToString();

            ly = Outpost_Establishment_UI.DrawTileDetailsBox(
                lx, ly, lw, cachedTileValid, requirementsPreviewOnly,
                invalidHint,
                cachedBiomeName, cachedTerrainVal, cachedFertPct, cachedAnimPct, cachedFishPct, cachedMinePct,
                cachedFertPctLabel, cachedAnimPctLabel, cachedFishPctLabel, cachedMinePctLabel,
                cachedBiomeTooltip, cachedTerrainTooltip, cachedFertTip, cachedHuntTip, cachedFishTip, cachedMiningTip,
                cachedColName, cachedColTerrain, cachedColFertility, cachedColAnimals, cachedColFish, cachedColMining,
                cachedTileProximityBlockedHint, cachedTileProximityBlockedTip);
            ly += Outpost_Dialog_UI.OutcomeBoxGap;

            if (fromCaravan == null && !requirementsPreviewOnly && !IsAnyRemoteEstablishPath)
            {
                GUI.color = new Color(0.9f, 0.85f, 0.6f);
                float conquestHeaderH = Mathf.Max(Outpost_Dialog_UI.YieldLineH, Text.CalcHeight(cachedConquestHeader, lw));
                Widgets.Label(new Rect(lx, ly, lw, conquestHeaderH), cachedConquestHeader);
                GUI.color = Color.white;
                ly += conquestHeaderH + 4f;
            }

            Widgets.DrawLineHorizontal(lx, ly, lw);
            ly += 8f;

            GUI.color = Outpost_Dialog_UI.TheoreticalLabelColor;
            Widgets.Label(new Rect(lx, ly, lw, Outpost_Dialog_UI.OutcomeLineH), cachedSelectedDetailHeader);
            GUI.color = Color.white;
            ly += Outpost_Dialog_UI.OutcomeLineH + 4f;

            int selectedIdx = FindDefIndex(selectedOutpostDefName);
            bool showCostColumn = fromCaravan != null || requirementsPreviewOnly || IsAnyRemoteEstablishPath;
            float detailContentH = selectedIdx >= 0
                ? Outpost_Establishment_UI.MeasureSelectedDetailHeight(
                    defs[selectedIdx], cachedRows[selectedIdx], showCostColumn,
                    !string.IsNullOrEmpty(Outpost_Establishment_UI.GetOutpostDescription(defs[selectedIdx])), requirementsPreviewOnly, lw - 16f)
                : Outpost_Dialog_UI.OutcomeLineH;

            float detailScrollH = leftArea.yMax - ly - 4f;
            Rect detailOuter = new Rect(lx, ly, lw, detailScrollH);
            Rect detailView = new Rect(0f, 0f, lw - 16f, Mathf.Max(detailContentH, detailScrollH));
            Widgets.BeginScrollView(detailOuter, ref leftDetailScrollPos, detailView);

            if (selectedIdx >= 0)
            {
                var def = defs[selectedIdx];
                var row = cachedRows[selectedIdx];
                bool canEstablish = false;
                string blockReason = null;
                if (!requirementsPreviewOnly)
                {
                    if (IsTileFirstRemoteEstablish)
                        canEstablish = CanTileFirstEstablish(def, out blockReason);
                    else if (IsRemoteEstablish)
                        canEstablish = RemoteOutpostEstablishUtility.CanEstablishAtRemote(
                            tile, def, cachedRemotePawns, remoteEstablishSource?.Map, out blockReason);
                    else
                        canEstablish = fromCaravan != null
                            ? Outpost_EstablishmentRequirements.CanEstablishAt(tile, def, fromCaravan, out blockReason)
                            : Outpost_EstablishmentRequirements.CanEstablishAtForConquest(tile, def, tier, out blockReason);
                }

                Outpost_Establishment_UI.DrawSelectedOutpostDetail(
                    0f, 0f, detailView.width, def, row, showCostColumn, requirementsPreviewOnly,
                    cachedCostLabel, cachedNoCostLabel, cachedSkillsHeader, cachedRequirementsHeader,
                    cachedEstablishLabel,
                    canEstablish, blockReason,
                    () =>
                    {
                        if (!canEstablish) return;
                        if (IsTileFirstRemoteEstablish)
                        {
                            Find.WindowStack.Add(new Window_RemoteEstablishPawns(tile, def));
                            Close();
                            return;
                        }
                        if (IsRemoteEstablish)
                        {
                            RemoteOutpostEstablishUtility.LaunchAfterOptionalCarryConfirm(
                                tile, def, remoteEstablishSource, remoteEstablishEntries,
                                onSuccess: () => Close(),
                                onFail: fail => Messages.Message(
                                    fail ?? "TSA_WD_RemoteEstablish_Failed".Translate(),
                                    MessageTypeDefOf.RejectInput, false),
                                onCancel: () =>
                                {
                                    // Back to All Player Pawns so the player can change who is sent.
                                    Close();
                                    Find.WindowStack.Add(new Window_AllPlayerPawns());
                                });
                            return;
                        }
                        FinalizeOutpost(def);
                        Close();
                    });
            }
            else
            {
                Widgets.Label(new Rect(0f, 0f, detailView.width, Outpost_Dialog_UI.OutcomeLineH), cachedNoSelection);
            }

            Widgets.EndScrollView();
        }

        private void DrawRightColumn(Rect rightArea, List<WorldObjectDef> defs)
        {
            float y = rightArea.y;
            const float itemSearchBarH = 28f;
            const float SearchRowGap = 6f;
            const float FilterButtonWidth = 150f;
            const float IconPadding = 8f;
            const float IconColW = 56f;

            GUI.color = new Color(0.75f, 0.82f, 1f);
            Widgets.Label(new Rect(rightArea.x, y, rightArea.width, Outpost_Upgrade_UI.RightColHeaderH), cachedChooseHeader);
            GUI.color = Color.white;
            y += Outpost_Upgrade_UI.RightColHeaderH + 2f;

            Rect filterRect = new Rect(rightArea.xMax - FilterButtonWidth, y, FilterButtonWidth, itemSearchBarH);
            Rect searchRect = new Rect(rightArea.x, y, rightArea.width - FilterButtonWidth - 8f, itemSearchBarH);

            string oldSearch = outpostSearchFilter;
            outpostSearchFilter = Widgets.TextField(searchRect, outpostSearchFilter);
            if (outpostSearchFilter != oldSearch)
                rightScrollPos = Vector2.zero;

            if (string.IsNullOrEmpty(outpostSearchFilter))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                Widgets.Label(searchRect, cachedSearchPlaceholder);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            if (Widgets.ButtonText(filterRect, GetFilterLabel(establishmentFilter)))
            {
                var opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption(OutpostTranslationUtil.Key("TSA_WD_OutpostEstablish_FilterAll"),
                        () => SetFilter(EstablishmentTabFilter.All)),
                    new FloatMenuOption(OutpostTranslationUtil.Key("TSA_WD_OutpostEstablish_FilterBuildable"),
                        () => SetFilter(EstablishmentTabFilter.Buildable)),
                };
                Find.WindowStack.Add(new FloatMenu(opts));
            }

            y += itemSearchBarH + SearchRowGap;

            float scrollHeight = 8f;
            int lastTier = -1;
            for (int i = 0; i < defs.Count; i++)
            {
                WorldObjectDef def = defs[i];
                if (!RowPassesFilters(def, i)) continue;
                int tierVal = WorldObject_WD_Outpost.GetOutpostTier(def);
                if (tierVal != lastTier)
                {
                    scrollHeight += Outpost_Establishment_UI.TierHeaderH;
                    lastTier = tierVal;
                }
                scrollHeight += Outpost_Upgrade_UI.CompactRowHeight + Outpost_Upgrade_UI.CompactRowPadding;
            }

            Rect scrollOuter = new Rect(rightArea.x, y, rightArea.width, rightArea.yMax - y);
            Rect viewRect = new Rect(0f, 0f, rightArea.width - 16f, Mathf.Max(scrollHeight, 1f));
            Widgets.BeginScrollView(scrollOuter, ref rightScrollPos, viewRect);

            float curY = 0f;
            lastTier = -1;
            int visibleRow = 0;
            for (int idx = 0; idx < defs.Count; idx++)
            {
                WorldObjectDef def = defs[idx];
                if (!RowPassesFilters(def, idx)) continue;

                int tierVal = WorldObject_WD_Outpost.GetOutpostTier(def);
                if (tierVal != lastTier)
                {
                    Outpost_Establishment_UI.DrawTierHeader(new Rect(0f, curY, viewRect.width, Outpost_Establishment_UI.TierHeaderH), tierVal);
                    curY += Outpost_Establishment_UI.TierHeaderH;
                    lastTier = tierVal;
                }

                float rowH = Outpost_Upgrade_UI.CompactRowHeight + Outpost_Upgrade_UI.CompactRowPadding;
                Rect rowRect = new Rect(0f, curY, viewRect.width, rowH);
                if (visibleRow % 2 == 0) Widgets.DrawHighlight(rowRect);
                bool isSelected = def.defName == selectedOutpostDefName;
                bool buildable = IsBuildableOutpost(def, idx);
                Outpost_Dialog_UI.DrawUnmetRequirementsRowTint(rowRect, !buildable);
                Outpost_Dialog_UI.DrawSelectedRowTint(rowRect, isSelected);

                float rowContentY = curY + (rowRect.height - Outpost_Upgrade_UI.CompactRowHeight) * 0.5f;
                Texture2D icon = def.ExpandingIconTexture;
                Rect iconRect = new Rect(IconPadding, rowContentY + (Outpost_Upgrade_UI.CompactRowHeight - Outpost_Upgrade_UI.CompactRowIconSize) * 0.5f,
                    Outpost_Upgrade_UI.CompactRowIconSize, Outpost_Upgrade_UI.CompactRowIconSize);
                if (icon != null)
                {
                    GUI.color = Color.cyan;
                    Widgets.DrawTextureFitted(iconRect, icon, 1f);
                    GUI.color = Color.white;
                }

                Rect labelRect = new Rect(IconColW, rowContentY, viewRect.width - IconColW - 8f, Outpost_Upgrade_UI.CompactRowHeight);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(labelRect, def.LabelCap);
                Text.Anchor = TextAnchor.UpperLeft;

                Outpost_Dialog_UI.FinishSelectableListRow(rowRect, isSelected);
                if (Widgets.ButtonInvisible(rowRect))
                    selectedOutpostDefName = def.defName;
                if (!string.IsNullOrEmpty(cachedRows[idx].outpostTooltip))
                    TooltipHandler.TipRegion(rowRect, cachedRows[idx].outpostTooltip);

                curY += rowH;
                visibleRow++;
            }

            Widgets.EndScrollView();
        }

        private void SetFilter(EstablishmentTabFilter filter)
        {
            if (establishmentFilter == filter) return;
            establishmentFilter = filter;
            rightScrollPos = Vector2.zero;
            EnsureDefaultSelection(WD_OutpostSelectionCachedDefs.List);
        }

        private static string GetFilterLabel(EstablishmentTabFilter filter)
        {
            switch (filter)
            {
                case EstablishmentTabFilter.Buildable:
                    return OutpostTranslationUtil.Key("TSA_WD_OutpostEstablish_FilterBuildable");
                default:
                    return OutpostTranslationUtil.Key("TSA_WD_OutpostEstablish_FilterAll");
            }
        }

        private bool RowPassesFilters(WorldObjectDef def, int rowIdx)
        {
            if (!OutpostDefMatchesSearch(def, outpostSearchFilter)) return false;
            if (establishmentFilter == EstablishmentTabFilter.Buildable)
                return IsBuildableOutpost(def, rowIdx);
            return true;
        }

        private bool IsBuildableOutpost(WorldObjectDef def, int rowIdx)
        {
            if (requirementsPreviewOnly)
            {
                if (!cachedTileValid) return false;
                if (Outpost_EstablishmentRequirements.IsTileBlockedByMinDistanceCached(tile)) return false;
                var row = cachedRows[rowIdx];
                for (int li = 0; li < 9; li++)
                {
                    if (!row.reqApplies[li]) continue;
                    // Preview: caravan-only lines (cum. skills, min pawns) are informational.
                    if (li == 0 || li == 6) continue;
                    if (!row.reqs[li].met) return false;
                }
                return true;
            }

            if (IsTileFirstRemoteEstablish)
                return CanTileFirstEstablish(def, out _);
            if (IsRemoteEstablish)
                return RemoteOutpostEstablishUtility.CanEstablishAtRemote(
                    tile, def, cachedRemotePawns, remoteEstablishSource?.Map, out _);
            if (fromCaravan != null)
                return Outpost_EstablishmentRequirements.CanEstablishAt(tile, def, fromCaravan, out _);
            return Outpost_EstablishmentRequirements.CanEstablishAtForConquest(tile, def, tier, out _);
        }

        /// <summary>Tile + research + warehouse cost for tile-first mode (pawn/skill gates deferred to Confirm).</summary>
        private bool CanTileFirstEstablish(WorldObjectDef def, out string reason)
        {
            reason = null;
            if (!Outpost_EstablishmentRequirements.CanEstablishAt(tile, def, null, out reason))
                return false;

            Map colonyMap = Outpost_PowerPlant.GetPlayerColonyMap();
            if (colonyMap == null)
            {
                reason = "TSA_WD_TileFirstEstablish_NoColony".Translate();
                return false;
            }

            if (Outpost_EstablishmentRequirements.EnforceCost)
            {
                var cost = Outpost_EstablishmentRequirements.GetCost(def);
                var warehouses = ColonyWarehouseStockUtility.GetAllWarehouses();
                if (!ColonyWarehouseStockUtility.HasCosts(colonyMap, warehouses, cost, EmptyPawnListForCost, out reason))
                    return false;
            }

            return true;
        }

        private void EnsureDefaultSelection(List<WorldObjectDef> defs)
        {
            if (!string.IsNullOrEmpty(selectedOutpostDefName))
            {
                for (int i = 0; i < defs.Count; i++)
                {
                    if (defs[i].defName != selectedOutpostDefName) continue;
                    if (RowPassesFilters(defs[i], i)) return;
                    break;
                }
            }

            for (int i = 0; i < defs.Count; i++)
            {
                if (!RowPassesFilters(defs[i], i)) continue;
                selectedOutpostDefName = defs[i].defName;
                return;
            }

            selectedOutpostDefName = null;
        }

        private int FindDefIndex(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return -1;
            var defs = WD_OutpostSelectionCachedDefs.List;
            for (int i = 0; i < defs.Count; i++)
                if (defs[i].defName == defName) return i;
            return -1;
        }

        private void FinalizeOutpost(WorldObjectDef outpostDef)
        {
            if (fromCaravan != null)
            {
                if (!Outpost_EstablishmentRequirements.CaravanFullyStoppedOnTileForEstablishment(fromCaravan, tile, out string stopReason))
                {
                    Messages.Message(stopReason ?? "", MessageTypeDefOf.RejectInput, false);
                    return;
                }
                var pawnListEarly = fromCaravan.PawnsListForReading;
                if (pawnListEarly == null)
                {
                    Messages.Message("TSA_WD_EstablishOutpost_CaravanInvalid".Translate(), MessageTypeDefOf.RejectInput, false);
                    return;
                }
                int humanCount = 0;
                for (int pi = 0; pi < pawnListEarly.Count; pi++)
                {
                    var p = pawnListEarly[pi];
                    if (p != null && p.RaceProps != null && p.RaceProps.Humanlike && !p.Dead) humanCount++;
                }
                if (humanCount == 0)
                {
                    Messages.Message("TSA_WD_EstablishOutpost_NoHumanlikesOnCaravan".Translate(), MessageTypeDefOf.RejectInput, false);
                    return;
                }
                if (!Outpost_EstablishmentRequirements.TryDeductCost(fromCaravan, outpostDef))
                    return;
            }

            ConquestOpportunityUtility.DestroyConquestRuinsAt(tile, ruinsId);
            Outpost_EstablishmentRequirements.DestroyVanillaAbandonedCampsAt(tile);

            var outpost = (WorldObject_WD_Outpost)WorldObjectMaker.MakeWorldObject(outpostDef);
            outpost.Tile = tile;
            outpost.SetFaction(Faction.OfPlayer);
            outpost.Name = GenerateOutpostName(outpostDef, tile);
            Find.WorldObjects.Add(outpost);
            outpost.StartProductionTimerIfNeeded();

            if (fromCaravan != null)
            {
                var pawnSource = fromCaravan.PawnsListForReading;
                var humanlike = new List<Pawn>();
                for (int pi = 0; pi < pawnSource.Count; pi++)
                {
                    var p = pawnSource[pi];
                    if (p != null && p.RaceProps != null && p.RaceProps.Humanlike && !p.Dead)
                        humanlike.Add(p);
                }
                for (int pi = 0; pi < humanlike.Count; pi++)
                {
                    if (humanlike[pi] == null || humanlike[pi].Destroyed) continue;
                    outpost.AddCaravanPawnToOutpost(humanlike[pi], fromCaravan);
                }

                // Dissolve may already call Find.WorldObjects.Remove; calling Destroy() again on a removed VF caravan
                // can spawn an empty shell caravan. Only touch founding caravan while still registered; use VF-aware teardown.
                if (VehicleFrameworkOutpostDissolveCompat.CaravanIsRegisteredOnWorld(fromCaravan))
                    outpost.TryFinishDissolveCaravanAfterFoundingIfStillPresent(fromCaravan);
                VehicleFrameworkOutpostDissolveCompat.DestroyCaravanWorldObjectAfterOutpostDissolve(fromCaravan);

                var initialLogi = outpost.GetComponent<CompOutpostLogistics>();
                if (initialLogi != null && initialLogi.currentFood <= 0.01f)
                {
                    int pawnCount = outpost.PawnCount;
                    float baseFood = 50f;
                    float perPawnFood = 20f * pawnCount;
                    float initialFood = Mathf.Max(baseFood, perPawnFood);
                    initialLogi.currentFood = Mathf.Min(initialLogi.EffectiveMaxFood, initialFood);
                }

                // VF / dissolve edge cases can leave an empty player caravan shell (generic label "Caravan") on this tile.
                VehicleFrameworkOutpostDissolveCompat.DestroyAllPlayerCaravansOnTileAfterOutpostFounding(tile);
            }
            else
            {
                // No founding caravan: generate starting workers as fully frozen pawns so they can be restored 1:1 later.
                var sethFounding = WorldDominationMod.settings;
                int count;
                int minRelevantSkill;
                if (sethFounding != null)
                {
                    count = sethFounding.GetConquestFoundingPawnCount(tier);
                    minRelevantSkill = sethFounding.GetConquestFoundingMinRelevantSkillClamped();
                }
                else
                {
                    count = tier switch
                    {
                        SettlementTier.T4 => WorldDominationSettings.DefConquestFoundingPawnsT4,
                        SettlementTier.T3 => WorldDominationSettings.DefConquestFoundingPawnsT3,
                        SettlementTier.T2 => WorldDominationSettings.DefConquestFoundingPawnsT2,
                        _ => WorldDominationSettings.DefConquestFoundingPawnsT1
                    };
                    minRelevantSkill = Mathf.Clamp(WorldDominationSettings.DefConquestFoundingMinRelevantSkill, 0, 20);
                }
                Faction conqueredFaction = conquestContext?.conqueredFaction;
                var foundingXenotypePool = new List<Outpost_Recruiting.XenotypePoolEntry>();
                var foundingPawnKindPool = new List<Outpost_Recruiting.PawnKindPoolEntry>();
                Outpost_Recruiting.BuildXenotypePoolFromFaction(conqueredFaction, foundingXenotypePool);
                Outpost_Recruiting.BuildPawnKindPoolFromFaction(conqueredFaction, foundingPawnKindPool);

                for (int i = 0; i < count; i++)
                {
                    Pawn p = null;
                    try
                    {
                        bool foundValid = false;
                        int attempts = 0;
                        while (!foundValid && attempts < 50)
                        {
                            attempts++;
                            XenotypeDef xenotype = Outpost_Recruiting.RollXenotypeFromPool(foundingXenotypePool);
                            PawnKindDef kind = Outpost_Recruiting.RollPawnKindFromPool(foundingPawnKindPool);
                            p = Outpost_Recruiting.GenerateRecruitPawn(xenotype, prioritySkill: null, pawnKind: kind);

                            if (p != null && Outpost_Recruiting.PawnCanUseAllRelevantSkills(p, outpostDef))
                                foundValid = true;
                            else if (p != null)
                            {
                                Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.Discard);
                                p = null;
                            }
                        }

                        if (p != null)
                        {
                            Outpost_Recruiting.ApplyFoundingRelevantSkillFloors(p, outpostDef, minRelevantSkill);

                            // Freeze this generated pawn as a real outpost resident (same model as caravan-added pawns).
                            // NOTE: do not discard/remove from WorldPawns here; the outpost will keep it alive.
                            outpost.AddGeneratedPawnToOutpost(p);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("TSA_WorldDomination: Outpost starting pawn generation: " + ex.Message);
                        if (p != null)
                            Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.Discard);
                    }
                }

                // Initialize outpost virtual food once on creation based on generated pawn count.
                var initialLogi = outpost.GetComponent<CompOutpostLogistics>();
                if (initialLogi != null && initialLogi.currentFood <= 0.01f)
                {
                    int pawnCount = outpost.PawnCount;
                    float baseFood = 50f;
                    float perPawnFood = 20f * pawnCount;
                    float initialFood = Mathf.Max(baseFood, perPawnFood);
                    initialLogi.currentFood = Mathf.Min(initialLogi.EffectiveMaxFood, initialFood);
                }
            }

            CompViralSpread.ApplyPlayerOutpostFoundingShields(outpost);

            // PostAdd notifies logistics topology; flush smart once for new producer hubs so UI matches immediately.
            var manager = Find.World.GetComponent<WorldComponent_LogisticsManager>();
            var logi = outpost.GetComponent<CompOutpostLogistics>();
            if (manager != null && logi != null && Outpost_Production_Utils.IsFoodProducerOutpost(outpost.def) && logi.mode != LogisticsMode.Manual)
                manager.CompleteSmartLogisticsRefreshNow();

            // Conquest / buy / generated-worker founding leaves the player caravan on the tile.
            // Do not auto-absorb it just because Auto-Add Arrivals is on.
            if (fromCaravan == null)
                outpost.RegisterAutoAddBlockForPlayerCaravansOnThisTile();

            if (conquestContext != null)
            {
                conquestChoiceConsumed = true;
                conquestContext.consumed = true;
            }
            Find.WorldSelector.Select(outpost);
            Dialog_NameNewOutpost.Open(outpost);
        }

        public static string GenerateOutpostNamePublic(WorldObjectDef outpostDef, int outpostTile) =>
            GenerateOutpostName(outpostDef, outpostTile);

        private static string GenerateOutpostName(WorldObjectDef outpostDef, int outpostTile)
        {
            var faction = Faction.OfPlayer;
            var nameMaker = faction?.def?.settlementNameMaker;
            if (nameMaker == null) return outpostTile.ToString();
            var existing = new List<string>();
            var allWo = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < allWo.Count; i++)
            {
                if (allWo[i] is WorldObject_WD_Outpost o && !string.IsNullOrEmpty(o.Name))
                    existing.Add(o.Name);
            }
            return NameGenerator.GenerateName(nameMaker, existing);
        }

        private static string GetOutpostTypePrefix(WorldObjectDef def)
        {
            if (def?.label == null) return "TSA_WD_Select_OutpostDefault".Translate();
            string label = def.label;
            int idx = label.IndexOf(" Outpost", StringComparison.OrdinalIgnoreCase);
            return idx > 0 ? label.Substring(0, idx).Trim() : label;
        }

        /// <summary>Sum of this skill's level across all humanlike caravan pawns (for display).</summary>
        private static int GetCumulativeSkillForCaravan(Caravan caravan, SkillDef skill)
        {
            if (caravan?.PawnsListForReading == null || skill == null) return 0;
            int sum = 0;
            foreach (var p in caravan.PawnsListForReading)
            {
                if (p?.RaceProps?.Humanlike == true && !p.Dead && p.skills != null)
                    sum += p.skills.GetSkill(skill).Level;
            }
            return sum;
        }

        private static List<SkillDef> GetDisplaySkillDefs(WorldObjectDef def)
        {
            var raw = WorldObject_WD_Outpost.GetRelevantSkillDefs(def);
            var result = new List<SkillDef>();
            for (int i = 0; i < raw.Count; i++)
                if (raw[i] != null) result.Add(raw[i]);
            return result;
        }

        /// <summary>Whether this requirement line applies to this outpost type and context (conquest vs caravan vs preview vs remote).</summary>
        private static bool RequirementApplies(WorldObjectDef def, int lineIndex, Caravan fromCaravan, bool requirementsPreviewOnly = false, bool remoteEstablish = false)
        {
            bool isConquest = fromCaravan == null && !requirementsPreviewOnly && !remoteEstablish;
            string d = def?.defName?.ToLowerInvariant() ?? "";
            var ext = def?.GetModExtension<OutpostDefExtension>();
            switch (lineIndex)
            {
                case 1: // Cum. skills: caravan / remote establishment or read-only preview (not conquest)
                    if (!(fromCaravan != null || requirementsPreviewOnly || remoteEstablish) || ext?.MinCumulativeSkill == null || ext.MinCumulativeSkill.Count == 0)
                        return false;
                    for (int si = 0; si < ext.MinCumulativeSkill.Count; si++)
                        if (ext.MinCumulativeSkill[si] != null && ext.MinCumulativeSkill[si].HasAnyRequirement()) return true;
                    return false;
                case 2: // Fertility: relevant for farming and ranch outposts
                    return d.Contains("farming") || Outpost_Production_Utils.IsRanchOutpost(def);
                case 3: // Biome: only if outpost defines allowedBiomes or disallowedBiomes in XML
                    return ext != null && ((ext.allowedBiomes != null && ext.allowedBiomes.Count > 0) || (ext.disallowedBiomes != null && ext.disallowedBiomes.Count > 0));
                case 4: // Mining potential: only for mining outposts
                    return d.Contains("mining");
                case 5: // Research: only if outpost has requiredResearchProjectDefNames in XML
                    return Outpost_EstablishmentRequirements.GetRequiredResearchProjects(def).Count > 0;
                case 6: // Hunting / fishing potential
                    return d.Contains("hunting") || d.Contains("fishing");
                case 7: // Min pawns — caravan / remote only
                    return true;
                case 8: // Min settlements in radius: only if outpost def has minNearbySettlementsOrOutposts > 0 (e.g. Recruiting, Trading)
                    return ext != null && ext.minNearbySettlementsOrOutposts > 0;
                case 9: // Conquest tier: only in conquest dialog; outpost tier must be <= conquered settlement tier
                    return isConquest;
                default:
                    return true;
            }
        }

        private static bool HasAnySkillRequirement(List<MinCumulativeSkillSet> sets)
        {
            for (int i = 0; i < sets.Count; i++)
                if (sets[i] != null && sets[i].HasAnyRequirement()) return true;
            return false;
        }

        private static string GetTerrainLabel(Hilliness hill)
        {
            switch (hill)
            {
                case Hilliness.Flat: return "TSA_WD_Terrain_Flat".Translate();
                case Hilliness.SmallHills: return "TSA_WD_Terrain_SmallHills".Translate();
                case Hilliness.LargeHills: return "TSA_WD_Terrain_LargeHills".Translate();
                case Hilliness.Mountainous: return "TSA_WD_Terrain_Mountainous".Translate();
                case Hilliness.Impassable: return "TSA_WD_Terrain_Impassable".Translate();
                default: return hill.ToString();
            }
        }

        /// <summary>Mining column label; uses translation keys TSA_WD_Biome_Mining_*.</summary>
        private static string GetMiningTerrainLabel(Hilliness hill)
        {
            string key = "TSA_WD_Biome_Mining_" + hill;
            string t = key.Translate().ToString();
            if (t != key) return t;
            return GetTerrainLabel(hill);
        }

        /// <summary>Mining column color: Green = Large hills / Mountainous, Yellow = Small hills, Red = Flat / Impassable.</summary>
        private static Color GetMiningColor(Hilliness hill)
        {
            switch (hill)
            {
                case Hilliness.LargeHills:
                case Hilliness.Mountainous:
                    return new Color(0.35f, 0.8f, 0.35f); // green
                case Hilliness.SmallHills:
                    return new Color(0.9f, 0.9f, 0.35f);  // yellow
                default:
                    return new Color(1f, 0.35f, 0.35f);   // red (Flat, Impassable)
            }
        }

        private static Texture2D cachedRequirementsPreviewMouseIcon;
        private static bool suppressEstablishmentPreviewEnd;

        public static bool IsEstablishmentPreviewOverlayActive { get; private set; }

        public static void SetEstablishmentPreviewOverlayActive(bool active)
        {
            if (IsEstablishmentPreviewOverlayActive == active) return;
            IsEstablishmentPreviewOverlayActive = active;
        }

        /// <summary>World map toolbar: enter click-a-tile mode and open a read-only requirements preview per tile.</summary>
        public static void BeginRequirementsPreviewTileSelection()
        {
            RemoteOutpostEstablishSession.Clear();
            BeginOrRestartRequirementsPreviewTargeting(showHint: true);
        }

        internal static void EnsureRequirementsPreviewTargetingAfterDialogClosed()
        {
            if (!IsEstablishmentPreviewOverlayActive) return;
            if (suppressEstablishmentPreviewEnd) return;
            if (WorldComponent_WDVisualizerToggle.IsWorldTargeterActive()) return;
            BeginOrRestartRequirementsPreviewTargeting(showHint: false);
        }

        internal static bool IsSuppressingEstablishmentPreviewEnd() => suppressEstablishmentPreviewEnd;

        private static void BeginOrRestartRequirementsPreviewTargeting(bool showHint)
        {
            if (showHint)
                Messages.Message("TSA_WD_RequirementsPreview_ClickTileHint".Translate(), MessageTypeDefOf.NeutralEvent);

            cachedRequirementsPreviewMouseIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/EstablishOutpost", false)
                ?? ContentFinder<Texture2D>.Get("UI/Commands/Settle", false);

            SetEstablishmentPreviewOverlayActive(true);

            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    if (!TryResolveRequirementsPreviewTile(target, out int tile))
                        return false;

                    // StopTargeting runs after a successful pick; suppress so the session stays active until dialog close or right-click cancel.
                    suppressEstablishmentPreviewEnd = true;
                    Find.WindowStack.Add(new Dialog_OutpostSelection(tile, "", -1, SettlementTier.T1, null!, null!, requirementsPreviewOnly: true));
                    return true;
                },
                true,
                cachedRequirementsPreviewMouseIcon,
                false,
                null,
                null,
                IsValidRequirementsPreviewTile);
        }

        private static bool TryResolveRequirementsPreviewTile(GlobalTargetInfo target, out int tile)
        {
            tile = -1;
            if (!target.IsValid || target.Tile < 0)
                return false;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(target.Tile))
                return false;
            tile = target.Tile;
            return true;
        }

        private static bool IsValidRequirementsPreviewTile(GlobalTargetInfo target)
            => TryResolveRequirementsPreviewTile(target, out _);
    }
}
