using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;
using System.Text;
using Verse.Sound;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public class Window_OutpostOverview : Window
    {
        private Vector2 scrollPos;
        // Adjusted for Full Screen
        public override Vector2 InitialSize => new Vector2(UI.screenWidth, UI.screenHeight);

        private const float HeaderHeight = 30f;

        // Sorting & Filtering state
        private string sortColumn = "Name";
        private bool sortAscending = true;
        private static string typeFilter = "";
        private static string nameSearchTerm = "";

        // Performance Caching
        private int lastUpdateTick = -9999;
        private const int UpdateIntervalTicks = 300; // 300 ticks = 5 seconds
        /// <summary>Multiplier on <see cref="GameFont.Tiny"/> line height for the 3-line strength column (tighter than equal thirds of row).</summary>
        private const float StrengthColumnLineHeightFactor = 0.88f;
        private List<OutpostEntry> cachedList = new List<OutpostEntry>();

        private static float StrengthColumnLineHeight()
        {
            return Text.LineHeightOf(GameFont.Tiny) * StrengthColumnLineHeightFactor;
        }

        /// <summary>Call when production or logistics change (e.g. from production dialog or logistics tab) so the overview refreshes immediately if open.</summary>
        public static void InvalidateCache()
        {
            _cacheInvalidated = true;
        }
        private static bool _cacheInvalidated;

        private static bool s_overviewStringsInit;
        private static string s_ovTitle;
        private static string s_hdrName, s_hdrDist, s_hdrPawns, s_hdrNonhumanPawns, s_hdrFood, s_hdrStrength, s_hdrUpgrades, s_hdrRoad, s_hdrStatus, s_hdrProduces, s_hdrOutput, s_hdrExperts;
        private static string s_tipHdrName, s_tipHdrDist, s_tipHdrNonhumanPawns, s_tipHdrFood, s_tipHdrRoad, s_tipHdrStatus, s_tipHdrOutput;
        private static string s_tipNonhumanMechanoids, s_tipNonhumanAnimals, s_tipNonhumanVehicles;
        private static string s_ovRenameTip, s_ovStrengthTip, s_ovNone;
        private static string s_skillDrLabel;

        // Simple helper class for caching
        private class OutpostEntry
        {
            public WorldObject_WD_Outpost Outpost;
            public CompViralSpread Comp;
            public int Distance;
            public string ProdSummary;
            public string ProductionTimeStr;
            public bool IsProductionPaused;  // from outpost.GetProductionPauseReason; when true, show Produces and Timer in yellow
            public int PawnCount;
            public int PrisonerCount;
            public int InjuredPawnCount;
            public string InjuredPawnsTooltip;
            public string PawnsColumnTooltip;
            public int MechanoidCount;
            public int AnimalCount;
            public int VehicleCount;
            public int NonhumanPawnCount;
            public int TicksLeft;
            public float FoodNet;
            public float FoodCurrent;
            public List<string> UpgradeLabels = new List<string>();
            public float RowHeight = 60f;
            public float CachedStrength;
            /// <summary>Populated on cache rebuild so draw does not call defense getters / Translate every GUI frame.</summary>
            public bool HasStrengthCache;
            public bool StrengthLine1Cyan;
            public string StrengthLine1;
            public string StrengthLine2;
            public string StrengthLine3;
            public string StrengthTooltip;
            public string DistanceStr;
            public string PawnCountStr;
            public string NonhumanPawnCountStr;
            public string NonhumanTooltip;
            public string FoodDisplayColorized;
            public string StrengthLine1Colorized;
            public List<string> UpgradeLabelsColorized;
            public string UpgradesMoreStr;
            public string UpgradeTooltip;
            public string RoadProgressStr;
            public string RaidStatusColorized;
            public string RaidTooltip;
            public string DefStatusColorized;
            public string DefTooltip;
            public string JumpTooltip;
            public string ProdLabel;
            public string ProdTooltip;
            public bool HasSkillDiminishingReturns;
            public string SkillDrTooltip;
            public string SkillDrLabel;
            public int ExpertsAssigned;
            public int ExpertsMax;
            public string ExpertsCountStr;
            public string ExpertsCountTooltip;
            public List<OutpostExpertRole> AssignedExpertRoles = new List<OutpostExpertRole>();
            public List<string> AssignedExpertTooltips = new List<string>();
        }

        private static Texture2D overviewRoadBarFillTex;

        private static Texture2D OverviewRoadBarFillTexture
        {
            get
            {
                if (overviewRoadBarFillTex == null)
                    overviewRoadBarFillTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.4f, 0.6f, 0.9f));
                return overviewRoadBarFillTex;
            }
        }

        public Window_OutpostOverview()
        {
            this.doCloseX = true;
            this.draggable = false; // Disabled for full screen
            this.preventCameraMotion = false;
            this.forcePause = false; // Pausing while in full screen overview is standard
            this.closeOnCancel = true;
        }

        public override void PostClose()
        {
            base.PostClose();
            PawnRosterHeaderFilter.CloseDropdown();
            WdWindowEsc.ClearTextFocusOnClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            WdNavWindows.ProcessHotkeys();
            if (!IsOpen) return;
            if (PawnRosterHeaderFilter.TryCloseDropdownOnCancel())
                return;
            if (WdWindowEsc.TryCloseOnCancel(this))
                return;

            if (!s_overviewStringsInit)
            {
                s_overviewStringsInit = true;
                s_ovTitle = "TSA_WD_OutpostManager_Title".Translate();
                s_hdrName = "TSA_WD_OutpostOverview_HdrName".Translate();
                s_hdrDist = "TSA_WD_OutpostOverview_HdrDist".Translate();
                s_hdrPawns = "TSA_WD_Outpost_Pawns".Translate();
                s_hdrNonhumanPawns = "TSA_WD_OutpostOverview_HdrNonhuman".Translate();
                s_hdrFood = "TSA_WD_OutpostOverview_HdrFood".Translate();
                s_hdrStrength = "TSA_WD_Outpost_Strength".Translate();
                s_hdrUpgrades = "TSA_WD_Outpost_Upgrades".Translate();
                s_hdrRoad = "TSA_WD_OutpostOverview_HdrProject".Translate();
                s_hdrStatus = "TSA_WD_OutpostOverview_HdrCooldown".Translate();
                s_hdrProduces = "TSA_WD_Outpost_Produces".Translate();
                s_hdrOutput = "TSA_WD_OutpostOverview_HdrDelivery".Translate();
                s_hdrExperts = "TSA_WD_Outpost_Experts".Translate();
                s_tipHdrName = "TSA_WD_Outpost_Name".Translate();
                s_tipHdrDist = "TSA_WD_Outpost_Dist".Translate();
                s_tipHdrNonhumanPawns = "TSA_WD_Outpost_NonhumanPawns".Translate();
                s_tipHdrFood = "TSA_WD_Outpost_FoodStatus".Translate();
                s_tipHdrRoad = "TSA_WD_Outpost_Road".Translate();
                s_tipHdrStatus = "TSA_WD_Outpost_Status".Translate();
                s_tipHdrOutput = "TSA_WD_Outpost_Output".Translate();
                s_ovRenameTip = "TSA_WD_Outpost_ClickToRenameTooltip".Translate();
                s_ovStrengthTip = "TSA_WD_OutpostOverview_StrengthTooltip".Translate();
                s_ovNone = "TSA_WD_None".Translate();
                s_tipNonhumanMechanoids = "TSA_WD_OutpostOverview_NonhumanMechanoidsTip".Translate();
                s_tipNonhumanAnimals = "TSA_WD_OutpostOverview_NonhumanAnimalsTip".Translate();
                s_tipNonhumanVehicles = "TSA_WD_OutpostOverview_NonhumanVehiclesTip".Translate();
                s_skillDrLabel = "TSA_WD_SkillScaling_OverviewLabel".Translate();
            }
            if (_cacheInvalidated) { lastUpdateTick = -9999; _cacheInvalidated = false; }
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 35f), s_ovTitle);

            // --- REBALANCED COLUMN WIDTHS ---
            float colIcon = 60f;
            float colPadding = 20f;
            float colName = 160f;
            float colDist = 58f;
            float colPawns = 55f;
            float colNonhumanPawns = 85f;
            float colFood = 76f;
            float colStrength = 89f;
            float colUpgrades = 175f;
            float colRoad = 110f;
            float colCooldown = 100f;
            float colWhat = 175f;
            float colWhen = 130f;
            float colExperts = 100f;
            float contentWidth = colIcon + colPadding
                + colName + colDist + colPawns + colNonhumanPawns + colExperts + colFood
                + colStrength + colUpgrades + colRoad + colCooldown
                + colWhat + colWhen;
            contentWidth = Mathf.Max(contentWidth, inRect.width - 16f);

            // --- HEADERS (sticky vertically; X synced to body horizontal scroll) ---
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect hRect = new Rect(0, 40f, inRect.width, HeaderHeight);
            float curX = colIcon + colPadding - scrollPos.x;

            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, hRect.y, colName, hRect.height,
                s_hdrName,
                sortColumn == "Name", sortAscending,
                TextAnchor.MiddleCenter,
                !nameSearchTerm.NullOrEmpty(),
                "TSA_WD_FilterByName".Translate(),
                icon => PawnRosterHeaderFilter.OpenTextDropdown(
                    icon,
                    "TSA_WD_FilterByName".Translate(),
                    "TSA_WD_FilterByName".Translate(),
                    () => nameSearchTerm,
                    v => { nameSearchTerm = v ?? ""; lastUpdateTick = -9999; },
                    () => { nameSearchTerm = ""; lastUpdateTick = -9999; }),
                () => SetSort("Name"));
            DrawHeader(ref curX, colDist, s_hdrDist, "Dist", hRect, s_tipHdrDist);
            DrawHeader(ref curX, colPawns, s_hdrPawns, "Pawns", hRect);
            DrawHeader(ref curX, colNonhumanPawns, s_hdrNonhumanPawns, "NonhumanPawns", hRect, s_tipHdrNonhumanPawns);
            DrawHeader(ref curX, colExperts, s_hdrExperts, "Experts", hRect);
            DrawHeader(ref curX, colFood, s_hdrFood, "Food", hRect, s_tipHdrFood);
            DrawHeader(ref curX, colStrength, s_hdrStrength, "Strength", hRect);
            DrawHeader(ref curX, colUpgrades, s_hdrUpgrades, "Upgrades", hRect);
            DrawHeader(ref curX, colRoad, s_hdrRoad, "Road", hRect, s_tipHdrRoad);
            DrawHeader(ref curX, colCooldown, s_hdrStatus, "Status", hRect, s_tipHdrStatus);
            DrawHeader(ref curX, colWhat, s_hdrProduces, "Produces", hRect);
            DrawHeader(ref curX, colWhen, s_hdrOutput, "Timer", hRect, s_tipHdrOutput);

            GUI.color = Color.white;
            Widgets.DrawLineHorizontal(0, hRect.yMax, inRect.width);

            // Type filter stays on the icon column (drawn last so H-scroll does not cover it).
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            float typeHdrX = 0f;
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref typeHdrX, hRect.y, colIcon, hRect.height,
                "",
                false, sortAscending,
                TextAnchor.MiddleCenter,
                !typeFilter.NullOrEmpty(),
                "TSA_WD_FilterByType".Translate(),
                icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                    icon,
                    "TSA_WD_FilterByType".Translate(),
                    PawnRosterHeaderFilter.OutpostTypeChoices(typeFilter, v =>
                    {
                        typeFilter = v ?? "";
                        lastUpdateTick = -9999;
                    }, CollectPlayerOutpostTypeDefNames())),
                null);
            GUI.color = Color.white;

            // --- DATA GATHERING (CACHED EVERY 5 SECONDS) ---
            if (Find.TickManager.TicksGame >= lastUpdateTick + UpdateIntervalTicks || cachedList == null)
            {
                var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();
                var logi = Find.World.GetComponent<WorldComponent_LogisticsManager>();
                int playerTile = -1;
                var settlements = Find.WorldObjects.Settlements;
                for (int si = 0; si < settlements.Count; si++)
                {
                    if (settlements[si].Faction == Faction.OfPlayer) { playerTile = settlements[si].Tile; break; }
                }

                string nameLower = string.IsNullOrEmpty(nameSearchTerm) ? null : nameSearchTerm.ToLowerInvariant();
                cachedList.Clear();
                var allWo = Find.WorldObjects.AllWorldObjects;
                for (int wi = 0; wi < allWo.Count; wi++)
                {
                    if (!(allWo[wi] is WorldObject_WD_Outpost o) || o.Faction != Faction.OfPlayer) continue;
                    if (!typeFilter.NullOrEmpty() && (o.def == null || o.def.defName != typeFilter)) continue;
                    if (nameLower != null && !((string)o.LabelCap).ToLowerInvariant().Contains(nameLower)) continue;

                    var lComp = o.GetComponent<CompOutpostLogistics>();
                    float net = logi != null ? logi.GetLogisticsNetDailyForOutpost(o) : 0f;
                    bool isPaused = o.IsResearchOutpost
                        ? !Outpost_Research.CanResearchNow(o, out _)
                        : !o.GetProductionPauseReason(out _);
                    var upgrades = GetBuiltUpgradeLabels(o);
                    var comp = o.GetComponent<CompViralSpread>();
                    bool hasStr = comp != null;
                    bool strCyan = false;
                    string s1 = null, s2 = null, s3 = null;
                    string strengthTooltip = null;
                    int mechanoidCount = o.StoredMechanoidPawnCount;
                    int animalCount = 0;
                    int vehicleCount = 0;
                    List<Pawn> storedTransport = o.StoredAnimalsAndVehicles;
                    for (int si = 0; si < storedTransport.Count; si++)
                    {
                        Pawn sp = storedTransport[si];
                        if (sp == null || sp.Destroyed || sp.Dead) continue;
                        if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(sp))
                            vehicleCount++;
                        else
                            animalCount++;
                    }
                    int nonhumanPawnCount = mechanoidCount + animalCount + vehicleCount;
                    if (hasStr)
                    {
                        float offCur = comp.offensiveStrength;
                        float defCur = comp.defensiveStrength;
                        float offMax = comp.GetMaxOffensiveStrength();
                        float defMax = comp.GetBaseDefensiveStrength();
                        float totCur = offCur + defCur;
                        float totMax = offMax + defMax;
                        strCyan = totCur >= totMax - 0.01f;
                        s1 = string.Concat(totCur.ToString("F0"), "/", totMax.ToString("F0"));
                        string dRec = "+" + comp.GetInspectDailyDefensiveRecovery().ToString("F0");
                        string oRec = "+" + comp.GetInspectDailyOffensiveRecovery().ToString("F0");
                        s2 = string.Concat(offCur.ToString("F0"), "/", offMax.ToString("F0"), " Offensive Strength");
                        s3 = string.Concat(defCur.ToString("F0"), "/", defMax.ToString("F0"), " Defensive Strength");
                        strengthTooltip = string.Join("\n",
                            s1 + " Strength",
                            s2,
                            s3,
                            oRec + " Daily Offensive Strength gain",
                            dRec + " Daily Defensive Strength gain");
                        string offExpertLines = OutpostExpertUtility.BuildExpertMutatorLines(o, ExpertEffect.OffensiveRecovery);
                        if (!string.IsNullOrEmpty(offExpertLines))
                            strengthTooltip += "\n\n" + "TSA_WD_Experts_MutatorHeader".Translate() + "\n" + offExpertLines;
                        string defExpertLines = OutpostExpertUtility.BuildExpertMutatorLines(o, ExpertEffect.DefensiveRecovery);
                        if (!string.IsNullOrEmpty(defExpertLines))
                            strengthTooltip += (string.IsNullOrEmpty(offExpertLines) ? "\n\n" + "TSA_WD_Experts_MutatorHeader".Translate() + "\n" : "\n") + defExpertLines;
                        if (mechanoidCount > 0)
                            strengthTooltip += "\n" + "TSA_WD_StoredMechanoids_Inspect".Translate(mechanoidCount);
                    }
                    int dist = (playerTile != -1) ? WorldActions_Utils.GetDistance(o.Tile, playerTile, manager) : 999;
                    int pawnCount = o.PawnCount;
                    int prisonerCount = o.Prisoners?.Count ?? 0;
                    int injuredPawnCount = Outpost_OccupantProgression.CountOccupantsShowingHurtIcon(o.Occupants);
                    string injuredPawnsTooltip = injuredPawnCount > 0
                        ? "TSA_WD_OutpostOverview_InjuredPawnsTip".Translate(injuredPawnCount).ToString()
                        : null;
                    string pawnsColumnTooltip = prisonerCount > 0
                        ? "TSA_WD_OutpostOverview_PawnsColumnTip".Translate().ToString()
                        : null;
                    if (!string.IsNullOrEmpty(injuredPawnsTooltip))
                    {
                        pawnsColumnTooltip = string.IsNullOrEmpty(pawnsColumnTooltip)
                            ? injuredPawnsTooltip
                            : pawnsColumnTooltip + "\n\n" + injuredPawnsTooltip;
                    }
                    float foodCurrent = lComp?.currentFood ?? 0f;
                    float maxFood = CompOutpostLogistics.GetEffectiveMaxFoodFor(o);
                    string netSign = net >= 0 ? "+" : "";
                    Color fCol = net > 0.1f ? Color.green : (net < -0.1f ? Color.red : Color.yellow);
                    string foodDisplayColorized = $"{netSign}{net:F1}\n({foodCurrent:F0}/{maxFood:F0})".Colorize(fCol);
                    Color c1 = strCyan ? Color.cyan : Color.white;
                    string s1Colorized = s1?.Colorize(c1);
                    var upgradeLabelsColorized = new List<string>(upgrades.Count);
                    for (int ui = 0; ui < upgrades.Count; ui++)
                        upgradeLabelsColorized.Add(upgrades[ui].Colorize(Color.green));
                    string upgradesMoreStr = null;
                    if (upgrades.Count > 4)
                        upgradesMoreStr = "TSA_WD_OutpostOverview_UpgradesMore".Translate(upgrades.Count - 4).ToString();
                    string upgradeTooltip = upgrades.Count > 0 ? string.Join("\n", upgrades) : null;
                    string roadProgressStr = null;
                    if (comp != null && comp.roadTargetTile != -1)
                    {
                        string insufficient = comp.GetInsufficientStrengthConstructionMessage();
                        roadProgressStr = insufficient ?? $"{(Mathf.Min(1f, comp.roadProgress) * 100f).ToString("F0")}%";
                    }
                    else if (comp != null && WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp))
                    {
                        string insufficient = comp.GetInsufficientStrengthConstructionMessage();
                        roadProgressStr = insufficient ?? $"{(Mathf.Min(1f, comp.roadBlockProgress) * 100f).ToString("F0")}%";
                    }
                    else if (comp != null && WorldActions_SpikeTraps.HasActiveSpikeTrapProject(comp))
                    {
                        string insufficient = comp.GetInsufficientStrengthConstructionMessage();
                        roadProgressStr = insufficient ?? $"{(Mathf.Min(1f, comp.spikeTrapProgress) * 100f).ToString("F0")}%";
                    }
                    else if (comp != null && WorldActions_Decontamination.HasActiveDecontaminationProject(comp))
                    {
                        string insufficient = comp.GetInsufficientStrengthConstructionMessage();
                        roadProgressStr = insufficient ?? $"{(Mathf.Min(1f, comp.decontamProgress) * 100f).ToString("F0")}%";
                    }
                    string raidStatusColorized = null;
                    string raidTooltipStr = null;
                    string defStatusColorized = null;
                    string defTooltipStr = null;
                    if (comp != null)
                    {
                        bool raidOnCd = comp.IsRaidOnCooldown;
                        if (raidOnCd)
                        {
                            float raidDays = Mathf.Max(0, (comp.raidCooldownTick - Find.TickManager.TicksGame) / 60000f);
                            string raidDaysStr = raidDays.ToString("F1");
                            raidStatusColorized = "TSA_WD_OutpostOverview_CooldownDays".Translate(raidDaysStr).Colorize(Color.yellow);
                            raidTooltipStr = "TSA_WD_OutpostOverview_Tip_RaidCooldown".Translate(raidDaysStr);
                        }
                        else
                        {
                            raidStatusColorized = "TSA_WD_Status_RaidReady".Translate().Colorize(Color.green);
                            raidTooltipStr = "TSA_WD_OutpostOverview_Tip_CanRaid".Translate();
                        }
                        bool shielded = comp.IsDefenseOnCooldown;
                        if (shielded)
                        {
                            float defDays = Mathf.Max(0, (comp.defenseCooldownTick - Find.TickManager.TicksGame) / 60000f);
                            string defDaysStr = defDays.ToString("F1");
                            defStatusColorized = "TSA_WD_OutpostOverview_CooldownDays".Translate(defDaysStr).Colorize(Color.green);
                            defTooltipStr = "TSA_WD_OutpostOverview_Tip_RaidProtected".Translate(defDaysStr);
                        }
                        else
                        {
                            defStatusColorized = "TSA_WD_Status_DefVulnerable".Translate().Colorize(CompViralSpread.RaidVulnerableColor);
                            defTooltipStr = "TSA_WD_OutpostOverview_Tip_CanBeRaided".Translate();
                        }
                    }
                    string jumpTooltip = "TSA_WD_JumpToOutpost".Translate(o.LabelCap);
                    string prodSummary = o.GetProductionLineForOverview();
                    string prodLabel = string.IsNullOrEmpty(prodSummary)
                        ? (string)"TSA_WD_Outpost_Inspect_ProducingNone".Translate()
                        : prodSummary;
                    string prodTooltip = Outpost_Production.GetProductionTooltip(o);
                    if (string.IsNullOrEmpty(prodTooltip) || prodTooltip.Contains("TSA_WD_"))
                        prodTooltip = null;
                    float skillDrRaw = OutpostSkillScaling.GetBannerRawSkill(o);
                    bool hasSkillDr = OutpostSkillScaling.IsDiminished(skillDrRaw);
                    string skillDrTip = hasSkillDr ? OutpostSkillScaling.BuildBandBreakdownTip(skillDrRaw) : null;
                    int expertsAssigned = OutpostExpertUtility.GetAssignedExpertCount(o);
                    int expertsMax = OutpostExpertUtility.GetMaxExpertSlots(o);
                    string expertsCountStr = expertsAssigned + "/" + expertsMax;
                    string expertsCountTooltip = "TSA_WD_OutpostOverview_ExpertsAssignedTip"
                        .Translate(expertsAssigned, expertsMax).ToString();
                    var assignedExpertRoles = new List<OutpostExpertRole>();
                    var assignedExpertTooltips = new List<string>();
                    foreach (OutpostExpertRole role in Enum.GetValues(typeof(OutpostExpertRole)))
                    {
                        if (!OutpostExpertUtility.IsRoleAvailableForOutpost(o, role)) continue;
                        Pawn expert = o.GetAssignedExpert(role);
                        if (expert == null) continue;
                        assignedExpertRoles.Add(role);
                        assignedExpertTooltips.Add(
                            OutpostExpertUtility.BuildAssignedExpertIconTooltip(o, role, expert));
                    }
                    float rowH = ComputeOverviewRowHeight(upgrades.Count);
                    if (hasSkillDr)
                        rowH = Mathf.Max(rowH, 72f);
                    cachedList.Add(new OutpostEntry
                    {
                        Outpost = o,
                        Comp = comp,
                        Distance = dist,
                        ProdSummary = prodSummary,
                        ProductionTimeStr = o.GetProductionTimeLeftForOverview(),
                        IsProductionPaused = isPaused,
                        PawnCount = pawnCount,
                        PrisonerCount = prisonerCount,
                        InjuredPawnCount = injuredPawnCount,
                        InjuredPawnsTooltip = injuredPawnsTooltip,
                        PawnsColumnTooltip = pawnsColumnTooltip,
                        MechanoidCount = mechanoidCount,
                        AnimalCount = animalCount,
                        VehicleCount = vehicleCount,
                        NonhumanPawnCount = nonhumanPawnCount,
                        TicksLeft = o.ProductionTicksLeft,
                        FoodNet = net,
                        FoodCurrent = foodCurrent,
                        UpgradeLabels = upgrades,
                        RowHeight = rowH,
                        CachedStrength = comp != null ? comp.GetTotalLocalDefensePower() : 0f,
                        HasStrengthCache = hasStr,
                        StrengthLine1Cyan = strCyan,
                        StrengthLine1 = s1,
                        StrengthLine2 = s2,
                        StrengthLine3 = s3,
                        StrengthTooltip = strengthTooltip,
                        DistanceStr = dist.ToString(),
                        PawnCountStr = pawnCount.ToString(),
                        NonhumanPawnCountStr = nonhumanPawnCount.ToString(),
                        NonhumanTooltip = BuildNonhumanPawnTooltip(mechanoidCount, animalCount, vehicleCount),
                        FoodDisplayColorized = foodDisplayColorized,
                        StrengthLine1Colorized = s1Colorized,
                        UpgradeLabelsColorized = upgradeLabelsColorized,
                        UpgradesMoreStr = upgradesMoreStr,
                        UpgradeTooltip = upgradeTooltip,
                        RoadProgressStr = roadProgressStr,
                        RaidStatusColorized = raidStatusColorized,
                        RaidTooltip = raidTooltipStr,
                        DefStatusColorized = defStatusColorized,
                        DefTooltip = defTooltipStr,
                        JumpTooltip = jumpTooltip,
                        ProdLabel = prodLabel,
                        ProdTooltip = prodTooltip,
                        HasSkillDiminishingReturns = hasSkillDr,
                        SkillDrTooltip = skillDrTip,
                        SkillDrLabel = s_skillDrLabel,
                        ExpertsAssigned = expertsAssigned,
                        ExpertsMax = expertsMax,
                        ExpertsCountStr = expertsCountStr,
                        ExpertsCountTooltip = expertsCountTooltip,
                        AssignedExpertRoles = assignedExpertRoles,
                        AssignedExpertTooltips = assignedExpertTooltips
                    });
                }

                SortCachedList();
                lastUpdateTick = Find.TickManager.TicksGame;
            }

            // --- SCROLL VIEW (vertical + horizontal when columns exceed viewport) ---
            float totalScrollHeight = 0f;
            for (int j = 0; j < cachedList.Count; j++)
                totalScrollHeight += cachedList[j].RowHeight;
            Rect scrollOuter = new Rect(0, hRect.yMax + 5f, inRect.width, inRect.height - (hRect.yMax + 5f) - 30f);
            Rect viewRect = new Rect(0, 0, contentWidth, Mathf.Max(totalScrollHeight, scrollOuter.height - 1f));
            Widgets.BeginScrollView(scrollOuter, ref scrollPos, viewRect);

            string cachedRenameTooltip = s_ovRenameTip;
            string cachedStrengthTooltip = s_ovStrengthTip;
            string cachedNoneLabel = s_ovNone;
            float rowY = 0f;
            for (int i = 0; i < cachedList.Count; i++)
            {
                var entry = cachedList[i];
                var outpost = entry.Outpost;
                var comp = entry.Comp;
                float rowHeight = entry.RowHeight;
                Rect row = new Rect(0, rowY, viewRect.width, rowHeight);
                rowY += rowHeight;

                if (i % 2 == 0) Widgets.DrawHighlight(row);
                if (Mouse.IsOver(row)) Widgets.DrawLightHighlight(row);

                // 1. Icon & Jump Logic
                float typeIconSz = 40f;
                float typeIconX = row.x + (colIcon - typeIconSz) * 0.5f;
                float iconY = row.y + Mathf.Max(7f, (row.height - typeIconSz) * 0.5f);
                Rect iconRect = new Rect(typeIconX, iconY, typeIconSz, typeIconSz);
                if (outpost.def != null)
                {
                    GUI.color = outpost.Faction.Color;
                    GUI.DrawTexture(iconRect, outpost.def.ExpandingIconTexture);
                    GUI.color = Color.white;
                    if (Widgets.ButtonInvisible(iconRect))
                    {
                        CameraJumper.TryJump(outpost);
                        Find.WorldSelector.ClearSelection();
                        Find.WorldSelector.Select(outpost);
                        if (Find.MainTabsRoot.OpenTab != null) Find.MainTabsRoot.EscapeCurrentTab();
                        this.Close();
                    }
                    TooltipHandler.TipRegion(iconRect, entry.JumpTooltip);
                }

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                float rX = colIcon + colPadding;

                // 2. Name (click to rename)
                Rect nameRect = new Rect(rX, row.y, colName, rowHeight);
                Widgets.Label(nameRect, outpost.LabelCap);
                TooltipHandler.TipRegion(nameRect, cachedRenameTooltip);
                if (Widgets.ButtonInvisible(nameRect))
                {
                    Find.WindowStack.Add(new Dialog_RenameOutpost(outpost));
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
                rX += colName;

                // 3. Distance
                Widgets.Label(new Rect(rX, row.y, colDist, rowHeight), entry.DistanceStr);
                rX += colDist;

                // 4. Pawns (white = all; yellow second line = prisoners when any)
                Rect pawnsRect = new Rect(rX, row.y, colPawns, rowHeight);
                float iconSz = entry.InjuredPawnCount > 0 ? OutpostHealthUIUtils.BleedingIconSize : 0f;
                Rect pawnsLabelRect = new Rect(pawnsRect.x, pawnsRect.y, pawnsRect.width - iconSz, rowHeight);
                if (entry.PrisonerCount > 0)
                {
                    string pawnsText = entry.PawnCountStr + "\n"
                        + entry.PrisonerCount.ToString().Colorize(Color.yellow);
                    Widgets.Label(pawnsLabelRect, pawnsText);
                }
                else
                {
                    Widgets.Label(pawnsLabelRect, entry.PawnCountStr);
                }
                if (entry.InjuredPawnCount > 0)
                {
                    Texture2D bleedIcon = OutpostHealthUIUtils.GetBleedingIcon();
                    if (bleedIcon != null)
                    {
                        Rect bleedIconRect = new Rect(pawnsRect.xMax - iconSz, pawnsRect.y + (rowHeight - iconSz) * 0.5f, iconSz, iconSz);
                        GUI.DrawTexture(bleedIconRect, bleedIcon, ScaleMode.ScaleToFit);
                    }
                }
                if (!string.IsNullOrEmpty(entry.PawnsColumnTooltip))
                    TooltipHandler.TipRegion(pawnsRect, entry.PawnsColumnTooltip);
                rX += colPawns;

                // 4b. Nonhuman pawns (sum; breakdown on tooltip)
                Rect nonhumanRect = new Rect(rX, row.y, colNonhumanPawns, rowHeight);
                Widgets.Label(nonhumanRect, entry.NonhumanPawnCountStr);
                if (!string.IsNullOrEmpty(entry.NonhumanTooltip))
                    TooltipHandler.TipRegion(nonhumanRect, entry.NonhumanTooltip);
                rX += colNonhumanPawns;

                // 4c. Experts (count / max, then assigned role icons)
                Rect expertsCol = new Rect(rX, row.y, colExperts, rowHeight);
                int iconCount = entry.AssignedExpertRoles?.Count ?? 0;
                if (iconCount <= 0)
                {
                    Widgets.Label(expertsCol, entry.ExpertsCountStr);
                    if (!string.IsNullOrEmpty(entry.ExpertsCountTooltip))
                        TooltipHandler.TipRegion(expertsCol, entry.ExpertsCountTooltip);
                }
                else
                {
                    const float expertsGapReduce = 4f;
                    const float expertsPadBottom = 2f;
                    float expertsHalf = rowHeight * 0.5f;
                    Rect expertsCountRect = new Rect(expertsCol.x, expertsCol.y, expertsCol.width, expertsHalf);
                    Widgets.Label(expertsCountRect, entry.ExpertsCountStr);
                    if (!string.IsNullOrEmpty(entry.ExpertsCountTooltip))
                        TooltipHandler.TipRegion(expertsCountRect, entry.ExpertsCountTooltip);

                    Rect expertsIconsRect = new Rect(expertsCol.x, expertsCol.y + expertsHalf, expertsCol.width, expertsHalf);
                    const int maxExpertIconSlots = 6;
                    const float expertIconSz = 16f;
                    float expertIconGap = maxExpertIconSlots > 1
                        ? Mathf.Max(0f, (expertsIconsRect.width - maxExpertIconSlots * expertIconSz) / (maxExpertIconSlots - 1))
                        : 0f;
                    float iconsTotalW = iconCount * expertIconSz + (iconCount - 1) * expertIconGap;
                    float iconX = expertsIconsRect.x + Mathf.Max(0f, (expertsIconsRect.width - iconsTotalW) * 0.5f);
                    float expertIconY = expertsIconsRect.y
                        + Mathf.Max(0f, (expertsIconsRect.height - expertIconSz) * 0.5f)
                        - expertsGapReduce;
                    expertIconY = Mathf.Min(expertIconY, expertsCol.yMax - expertsPadBottom - expertIconSz);
                    expertIconY = Mathf.Max(expertIconY, expertsCol.y);
                    Color prevIconColor = GUI.color;
                    GUI.color = Color.white;
                    for (int ei = 0; ei < iconCount; ei++)
                    {
                        Rect iconR = new Rect(iconX, expertIconY, expertIconSz, expertIconSz);
                        Texture2D tex = OutpostExpertRoleIcons.Get(entry.AssignedExpertRoles[ei]);
                        if (tex != null)
                            GUI.DrawTexture(iconR, tex);
                        if (ei < entry.AssignedExpertTooltips.Count
                            && !string.IsNullOrEmpty(entry.AssignedExpertTooltips[ei]))
                            TooltipHandler.TipRegion(iconR, entry.AssignedExpertTooltips[ei]);
                        iconX += expertIconSz + expertIconGap;
                    }
                    GUI.color = prevIconColor;
                }
                rX += colExperts;

                // 5. Food Status
                Widgets.Label(new Rect(rX, row.y, colFood, rowHeight), entry.FoodDisplayColorized);
                rX += colFood;

                // 6. Strength (compact; detailed split on tooltip)
                Rect strColRect = new Rect(rX, row.y, colStrength, rowHeight);
                if (!entry.HasStrengthCache)
                {
                    Widgets.Label(strColRect, "-");
                }
                else
                {
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(strColRect, entry.StrengthLine1Colorized);
                    TooltipHandler.TipRegion(strColRect, entry.StrengthTooltip ?? cachedStrengthTooltip);
                }
                Text.Anchor = TextAnchor.MiddleCenter;
                rX += colStrength;

                // 6b. Upgrades (built tiers, green; row height grows with count)
                Rect upRect = new Rect(rX, row.y, colUpgrades, rowHeight);
                if (entry.UpgradeLabels == null || entry.UpgradeLabels.Count == 0)
                {
                    GUI.color = Color.gray;
                    Widgets.Label(upRect, cachedNoneLabel);
                    GUI.color = Color.white;
                }
                else
                {
                    Text.Anchor = TextAnchor.UpperCenter;
                    float lineH = Text.LineHeightOf(GameFont.Tiny);
                    const int maxShow = 4;
                    float uy = upRect.y + 2f;
                    for (int ui = 0; ui < entry.UpgradeLabelsColorized.Count && ui < maxShow; ui++)
                    {
                        float lh = Mathf.Min(lineH, Text.CalcHeight(entry.UpgradeLabels[ui], upRect.width));
                        Widgets.Label(new Rect(upRect.x, uy, upRect.width, lh), entry.UpgradeLabelsColorized[ui]);
                        uy += lh;
                    }
                    if (entry.UpgradesMoreStr != null)
                    {
                        Widgets.Label(new Rect(upRect.x, uy, upRect.width, lineH), entry.UpgradesMoreStr);
                    }
                    if (entry.UpgradeTooltip != null)
                        TooltipHandler.TipRegion(upRect, entry.UpgradeTooltip);
                    Text.Anchor = TextAnchor.MiddleCenter;
                }
                rX += colUpgrades;

                // 7. Construction Project (road / road block / spike trap)
                Rect roadRect = new Rect(rX, row.y, colRoad, rowHeight);
                if (comp != null && (comp.roadTargetTile != -1
                    || WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp)
                    || WorldActions_SpikeTraps.HasActiveSpikeTrapProject(comp)
                    || WorldActions_Decontamination.HasActiveDecontaminationProject(comp)))
                {
                    string insufficient = comp.GetInsufficientStrengthConstructionMessage();
                    if (insufficient != null)
                    {
                        GUI.color = Color.yellow;
                        Widgets.Label(roadRect, entry.RoadProgressStr ?? insufficient);
                        GUI.color = Color.white;
                        TooltipHandler.TipRegion(roadRect, insufficient);
                    }
                    else if (comp.roadTargetTile != -1)
                    {
                        Widgets.Label(new Rect(roadRect.x, roadRect.y + 6f, colRoad, 16f),
                            comp.roadIsClearing
                                ? "TSA_WD_OutpostOverview_ProjectRoadClear".Translate()
                                : "TSA_WD_OutpostOverview_ProjectRoad".Translate());
                        Rect barRect = new Rect(roadRect.x + 5, roadRect.y + 28, colRoad - 10, 15);
                        Widgets.FillableBar(barRect, Mathf.Clamp01(comp.roadProgress), OverviewRoadBarFillTexture);
                        Widgets.Label(barRect, entry.RoadProgressStr);
                        TooltipHandler.TipRegion(roadRect,
                            comp.GetActiveRoadProjectLabel() + ": " + (comp.roadTargetName ?? ""));
                    }
                    else if (WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp))
                    {
                        Widgets.Label(new Rect(roadRect.x, roadRect.y + 6f, colRoad, 16f), comp.GetActiveRoadBlockProjectLabel());
                        Rect barRect = new Rect(roadRect.x + 5, roadRect.y + 28, colRoad - 10, 15);
                        Widgets.FillableBar(barRect, Mathf.Clamp01(comp.roadBlockProgress), OverviewRoadBarFillTexture);
                        Widgets.Label(barRect, entry.RoadProgressStr);
                        TooltipHandler.TipRegion(roadRect,
                            comp.GetActiveRoadBlockProjectLabel() + ": " + (comp.roadBlockTargetName ?? ""));
                    }
                    else if (WorldActions_SpikeTraps.HasActiveSpikeTrapProject(comp))
                    {
                        Widgets.Label(new Rect(roadRect.x, roadRect.y + 6f, colRoad, 16f), comp.GetActiveSpikeTrapProjectLabel());
                        Rect barRect = new Rect(roadRect.x + 5, roadRect.y + 28, colRoad - 10, 15);
                        Widgets.FillableBar(barRect, Mathf.Clamp01(comp.spikeTrapProgress), OverviewRoadBarFillTexture);
                        Widgets.Label(barRect, entry.RoadProgressStr);
                        TooltipHandler.TipRegion(roadRect,
                            comp.GetActiveSpikeTrapProjectLabel() + ": " + (comp.spikeTrapTargetName ?? ""));
                    }
                    else if (WorldActions_Decontamination.HasActiveDecontaminationProject(comp))
                    {
                        Widgets.Label(new Rect(roadRect.x, roadRect.y + 6f, colRoad, 16f), "TSA_WD_OutpostOverview_ProjectDecontamination".Translate());
                        Rect barRect = new Rect(roadRect.x + 5, roadRect.y + 28, colRoad - 10, 15);
                        Widgets.FillableBar(barRect, Mathf.Clamp01(comp.decontamProgress), OverviewRoadBarFillTexture);
                        Widgets.Label(barRect, entry.RoadProgressStr);
                        TooltipHandler.TipRegion(roadRect,
                            "TSA_WD_Inspect_DecontaminationBuild".Translate() + ": " + (comp.decontamTargetName ?? ""));
                    }
                }
                else
                {
                    GUI.color = Color.gray;
                    Widgets.Label(roadRect, cachedNoneLabel);
                    GUI.color = Color.white;
                }
                rX += colRoad;

                // 8. Cooldown Status (SURGICAL: Corrected Logic & Translation Key Mapping)
                Rect statusRect = new Rect(rX, row.y, colCooldown, rowHeight);
                if (comp != null)
                {
                    Rect topHalf = new Rect(statusRect.x, statusRect.y, statusRect.width, statusRect.height / 2f).ContractedBy(2f);
                    Rect bottomHalf = new Rect(statusRect.x, statusRect.y + (statusRect.height / 2f), statusRect.width, statusRect.height / 2f).ContractedBy(2f);

                    Widgets.Label(topHalf, entry.RaidStatusColorized);
                    TooltipHandler.TipRegion(topHalf, entry.RaidTooltip);

                    Widgets.Label(bottomHalf, entry.DefStatusColorized);
                    TooltipHandler.TipRegion(bottomHalf, entry.DefTooltip);
                }
                rX += colCooldown;


                // 9. Produces (same as inspect pane); yellow when production is paused (from outpost flag)
                // Optional second line: Diminishing Returns when skill scaling applies.
                Rect prodRect = new Rect(rX, row.y, colWhat, rowHeight);
                if (entry.HasSkillDiminishingReturns)
                {
                    float half = rowHeight * 0.5f;
                    Rect prodTop = new Rect(prodRect.x, prodRect.y, prodRect.width, half);
                    Rect prodBot = new Rect(prodRect.x, prodRect.y + half, prodRect.width, half);
                    if (entry.IsProductionPaused) GUI.color = Color.yellow;
                    Widgets.Label(prodTop, entry.ProdLabel);
                    GUI.color = Color.yellow;
                    Widgets.Label(prodBot, entry.SkillDrLabel ?? s_skillDrLabel);
                    GUI.color = Color.white;
                }
                else
                {
                    if (entry.IsProductionPaused) GUI.color = Color.yellow;
                    Widgets.Label(prodRect, entry.ProdLabel);
                    if (entry.IsProductionPaused) GUI.color = Color.white;
                }
                string prodTip = entry.ProdTooltip;
                if (entry.HasSkillDiminishingReturns && !string.IsNullOrEmpty(entry.SkillDrTooltip))
                    prodTip = string.IsNullOrEmpty(prodTip) ? entry.SkillDrTooltip : prodTip + "\n\n" + entry.SkillDrTooltip;
                if (!string.IsNullOrEmpty(prodTip))
                    TooltipHandler.TipRegion(prodRect, prodTip);
                rX += colWhat;

                // 10. Timer (same as inspect pane: days left, Paused, or Delayed); yellow when production is paused (same flag)
                string timeLabel = entry.ProductionTimeStr ?? "-";
                if (entry.IsProductionPaused) GUI.color = Color.yellow;
                Widgets.Label(new Rect(rX, row.y, colWhen, rowHeight), timeLabel);
                if (entry.IsProductionPaused) GUI.color = Color.white;
                rX += colWhen;

                // SURGICAL: Reset anchor to UpperLeft for the next iteration's early columns
                Text.Anchor = TextAnchor.UpperLeft;
            }
            Widgets.EndScrollView();
            Text.Anchor = TextAnchor.UpperLeft;
            PawnRosterHeaderFilter.DrawDropdownIfOpen();
        }

        private string BuildNonhumanPawnTooltip(int mechanoids, int animals, int vehicles)
        {
            return string.Join("\n",
                s_tipNonhumanMechanoids + ": " + mechanoids,
                s_tipNonhumanAnimals + ": " + animals,
                s_tipNonhumanVehicles + ": " + vehicles);
        }

        private static List<string> CollectPlayerOutpostTypeDefNames()
        {
            var list = new List<string>();
            string nameLower = string.IsNullOrEmpty(nameSearchTerm) ? null : nameSearchTerm.ToLowerInvariant();
            var allWo = Find.WorldObjects.AllWorldObjects;
            for (int wi = 0; wi < allWo.Count; wi++)
            {
                if (!(allWo[wi] is WorldObject_WD_Outpost o) || o.Faction != Faction.OfPlayer) continue;
                if (nameLower != null && !((string)o.LabelCap).ToLowerInvariant().Contains(nameLower)) continue;
                list.Add(o.def?.defName ?? "");
            }
            return list;
        }

        private void DrawHeader(ref float curX, float width, string label, string tag, Rect hRect, string tip = null)
        {
            float x = curX;
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, hRect.y, width, hRect.height,
                label, sortColumn == tag, sortAscending,
                TextAnchor.MiddleCenter, false, null, null,
                () => SetSort(tag));
            if (!string.IsNullOrEmpty(tip))
                TooltipHandler.TipRegion(new Rect(x, hRect.y, width, hRect.height), tip);
        }

        private void SetSort(string col)
        {
            if (sortColumn == col) sortAscending = !sortAscending;
            else { sortColumn = col; sortAscending = true; }
            lastUpdateTick = -9999; // Force refresh data to apply new sort
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private void SortCachedList()
        {
            cachedList.Sort((a, b) =>
            {
                int cmp = sortColumn switch
                {
                    "Name" => string.Compare(a.Outpost.LabelCap, b.Outpost.LabelCap, StringComparison.OrdinalIgnoreCase),
                    "Dist" => a.Distance.CompareTo(b.Distance),
                    "Pawns" => a.PawnCount.CompareTo(b.PawnCount),
                    "NonhumanPawns" => a.NonhumanPawnCount.CompareTo(b.NonhumanPawnCount),
                    "Food" => a.FoodNet.CompareTo(b.FoodNet),
                    "Strength" => a.CachedStrength.CompareTo(b.CachedStrength),
                    "Upgrades" => (a.UpgradeLabels?.Count ?? 0).CompareTo(b.UpgradeLabels?.Count ?? 0),
                    "Road" => GetConstructionProjectProgress(a.Comp).CompareTo(GetConstructionProjectProgress(b.Comp)),
                    "Status" => (a.Comp?.IsRaidOnCooldown ?? false).CompareTo(b.Comp?.IsRaidOnCooldown ?? false),
                    "Produces" => string.Compare(a.ProdSummary, b.ProdSummary, StringComparison.OrdinalIgnoreCase),
                    "Timer" => a.TicksLeft.CompareTo(b.TicksLeft),
                    "Experts" => a.ExpertsAssigned.CompareTo(b.ExpertsAssigned),
                    _ => string.Compare(a.Outpost.LabelCap, b.Outpost.LabelCap, StringComparison.OrdinalIgnoreCase)
                };
                return sortAscending ? cmp : -cmp;
            });
        }

        private static float GetConstructionProjectProgress(CompViralSpread comp)
        {
            if (comp == null) return 0f;
            if (comp.roadTargetTile != -1) return Mathf.Min(1f, comp.roadProgress);
            if (WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp)) return Mathf.Min(1f, comp.roadBlockProgress);
            if (WorldActions_SpikeTraps.HasActiveSpikeTrapProject(comp)) return Mathf.Min(1f, comp.spikeTrapProgress);
            if (WorldActions_Decontamination.HasActiveDecontaminationProject(comp)) return Mathf.Min(1f, comp.decontamProgress);
            return 0f;
        }

        private static List<string> GetBuiltUpgradeLabels(WorldObject_WD_Outpost o)
        {
            var list = new List<string>();
            if (o?.BuiltUpgradeLevels == null) return list;
            foreach (var kv in o.BuiltUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                list.Add(def != null ? def.LabelCap : kv.Key);
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        private static float ComputeOverviewRowHeight(int upgradeLabelCount)
        {
            const float minH = 60f;
            float lineTiny = Text.LineHeightOf(GameFont.Tiny);
            float strBlock = 3f * (lineTiny * StrengthColumnLineHeightFactor) + 6f;
            int upLines = upgradeLabelCount <= 0 ? 1 : Mathf.Min(upgradeLabelCount, 4) + (upgradeLabelCount > 4 ? 1 : 0);
            float upBlock = 6f + upLines * lineTiny;
            return Mathf.Max(minH, strBlock, upBlock);
        }

        private int GetRoadTotalSteps(int start, int dest)
        {
            if (dest == -1) return 0;
            var layer = WorldDomination_UIUtils.GetDefaultPlanetLayer();
            using (WorldPathing pathing = new WorldPathing(layer))
            {
                using (WorldPath path = pathing.FindPath(new PlanetTile(start, layer), new PlanetTile(dest, layer), null))
                    return (path != null && path.Found) ? path.NodesReversed.Count - 1 : 0;
            }
        }

        private int GetRoadRemainingSteps(int start, int dest)
        {
            if (dest == -1) return 0;
            var layer = WorldDomination_UIUtils.GetDefaultPlanetLayer();
            using (WorldPathing pathing = new WorldPathing(layer))
            {
                using (WorldPath path = pathing.FindPath(new PlanetTile(start, layer), new PlanetTile(dest, layer), null))
                {
                    if (path == null || !path.Found) return 0;
                    var nodes = path.NodesReversed;
                    int unpaved = 0;
                    for (int i = nodes.Count - 1; i > 0; i--) { if (!HasRoadLink(nodes[i], nodes[i - 1])) unpaved++; }
                    return unpaved;
                }
            }
        }

        private bool HasRoadLink(int tileA, int tileB)
        {
            var tile = Find.WorldGrid[tileA] as SurfaceTile;
            if (tile?.potentialRoads == null) return false;
            var roads = tile.potentialRoads;
            for (int i = 0; i < roads.Count; i++)
            {
                if (roads[i].neighbor.tileId == tileB) return true;
            }
            return false;
        }
    }

    /// <summary>Simple dialog to rename an outpost. Used from the Outpost Overview when clicking the name.</summary>
    public class Dialog_RenameOutpost : Window
    {
        private readonly WorldObject_WD_Outpost outpost;
        private string curName;

        private const float ButtonGap = 12f;
        private const float SidePad = 4f;

        public override Vector2 InitialSize => new Vector2(320f, 160f);

        public Dialog_RenameOutpost(WorldObject_WD_Outpost outpost)
        {
            this.outpost = outpost;
            curName = outpost.Name ?? "";
            doCloseButton = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            float buttonsH = CloseButSize.y + 12f;
            Rect body = new Rect(0f, 0f, inRect.width, inRect.height - buttonsH);

            string title = "TSA_WD_Outpost_RenameDialogTitle".Translate().ToString();
            if (title.Contains("TSA_WD_")) title = "Rename outpost";
            Text.Font = GameFont.Small;
            float y = 0f;
            Widgets.Label(new Rect(SidePad, y, body.width - SidePad * 2f, 24f), title);
            y += 28f;
            curName = Widgets.TextField(new Rect(SidePad, y, body.width - SidePad * 2f, 28f), curName);

            float btnW = CloseButSize.x;
            float btnH = CloseButSize.y;
            float pairW = btnW * 2f + ButtonGap;
            float startX = (inRect.width - pairW) * 0.5f;
            float by = inRect.height - btnH;

            if (Widgets.ButtonText(new Rect(startX, by, btnW, btnH), "Close".Translate()))
                Close();

            string acceptLabel = "TSA_WD_Outpost_RenameAccept".Translate().ToString();
            if (acceptLabel.Contains("TSA_WD_")) acceptLabel = "Accept";
            if (Widgets.ButtonText(new Rect(startX + btnW + ButtonGap, by, btnW, btnH), acceptLabel))
                ApplyAndClose();
        }

        private void ApplyAndClose()
        {
            string trimmed = curName?.Trim();
            outpost.Name = string.IsNullOrEmpty(trimmed) ? null : trimmed;
            Window_OutpostOverview.InvalidateCache();
            Close();
        }
    }
}