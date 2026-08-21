#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>Inspector tab: virtual pawns in a clear table. Vanilla PortraitsCache.Get (headgear + clothes on), name, skills, strength, select-for-removal + bulk remove. Click opens Dialog_InfoCard.</summary>
    public class WITab_Outpost_Pawns : WITab
    {
        private Vector2 scrollPosition;
        private float scrollViewHeight = 120f;
        /// <summary>Tab body left X when drawing the fixed table header (aligns with scroll list).</summary>
        private float bodyXForHeader;

        private const float BottomBarHeight = 38f;
        private const float ColPortrait = 40f;
        private const float ColPawnType = 96f;
        private const float ColName = 120f;
        private const float ColStar = 56f;
        private const float ColResistance = 69f;
        private const float ColTraits = PawnRosterTraitFilter.ColWidth;
        private const float ColXenotype = 100f;
        private const float ColPsycasts = 110f;
        private const float ColAge = 44f;
        private const float ColSkill = 56f;
        private const float ColRelevantXp = 72f;
        private const float ColConstruction = 70f;
        private const float ColStrength = 72f;
        private const float ColDailyFood = 68f;
        private const float ColHurt = 56f;
        private const float ColSelect = 36f;
        private const float ColReorder = 48f;
        private const float RowHeight = 40f;
        /// <summary>Tall enough for three Tiny trait lines without cropping.</summary>
        private const float TallRowHeight = 58f;
        private const float PrisonerRowHeight = 54f;
        private const float GroupHeaderHeight = 28f;
        private const float HeaderHeight = 36f;
        private const float ToolbarHeight = 48f;
        private const float TransferBtnWidth = 200f;
        private const float SelectedLabelWidth = 130f;
        private const float ToolbarBtnGap = 10f;
        private const float ColSkillPad = 12f;
        private const PawnRosterColumnWindow ColWindow = PawnRosterColumnWindow.OutpostPawns;
        /// <summary>Inset so Transfer does not overlap the inspect-pane close X.</summary>
        private const float TransferBtnRightInset = 24f;
        private const float TabHeaderOffsetY = 0f;
        private static readonly Vector2 PortraitSize = new Vector2(36f, 36f);
        private static readonly Color RecruitingRowTint = new Color(1f, 0.55f, 0.15f, 0.21f);

        private OutpostPawnTableSortColumn sortColumn = OutpostPawnTableSortColumn.Default;
        private bool sortAscending = true;
        private bool useDefaultGrouping = true;
        private PlayerPawnTypeFilter pawnTypeFilter = PlayerPawnTypeFilter.All;
        private string pawnSearchTerm = "";
        private OutpostPawnStarFilter starFilter = OutpostPawnStarFilter.All;
        private string xenotypeFilter = "";
        private string psycastFilter = "";

        private static string hdrNoPawns, hdrName, hdrAge, hdrMelee, hdrStrength, hdrSortTip,
            hdrSelectColumnTip, hdrXpSuffix, hdrRelevantXp, hdrHunt,
            hdrSlaveRemoveBlockedTip, hdrConstructionRoadTip, hdrDailyFood, hdrHurt, hdrHurtTip,
            hdrTransfer, hdrTransferTip, hdrStarTip, hdrResistance, hdrTraits;

        private static bool headerLabelsReady;

        /// <summary>ThingIDs selected for bulk removal (persists while the tab is open).</summary>
        private readonly HashSet<string> selectedForRemovalThingIds = new HashSet<string>();

        private float lastScrollViewportHeight = 400f;

        private const int CacheRefreshInterval = 60;
        private int lastCacheTick = -1;
        private static bool cacheInvalidated;
        private WorldObject_WD_Outpost cachedOutpost = null!;
        private OutpostPawnTableSortColumn cachedSortCol;
        private bool cachedSortAsc;
        private bool cachedUseDefaultGrouping;
        private PlayerPawnTypeFilter cachedTypeFilter = PlayerPawnTypeFilter.All;
        private string cachedNameFilter = "";
        private OutpostPawnStarFilter cachedStarFilter;
        private string cachedXenotypeFilter = "";
        private string cachedPsycastFilter = "";
        private int cachedPrisonerCount = -1;
        private List<CachedPawnRow> cachedRows = null!;
        private List<SkillDef> cachedRelevantDefs = null!;
        private string cachedRelHeader = null!;
        private string cachedXpTooltip = null!;
        private string cachedXpHeaderLabel = null!;
        /// <summary>Academy: effective teaching skill for cache invalidation (selection / cycle lock).</summary>
        private string cachedAcademyTeachingSkillKey = null!;

        /// <summary>Force next draw to rebuild rows (capture / let-go / recruit from other UIs).</summary>
        public static void InvalidateCache() => cacheInvalidated = true;

        private enum OutpostPawnRowKind
        {
            Occupant,
            StoredTransport,
            StoredMechanoid,
            Shuttle,
            Prisoner,
            GroupHeader
        }

        private class CachedPawnRow
        {
            public Pawn pawn = null!;
            public Building_PassengerShuttle shuttle = null!;
            public VirtualPawnSummary summary = null!;
            public OutpostPawnRowKind rowKind;
            public PlayerPawnSortCategory sortCategory;
            public string typeLabel = null!;
            /// <summary>Ideology slave row — tinted in the pawns table.</summary>
            public bool isSlave;
            public string portraitKey = null!;
            public string nameLabel = null!;
            public string ageLabel = null!;
            public string shootingLabel = null!;
            public string meleeLabel = null!;
            public string relevantSkillLabel = null!;
            public string xpProgressLabel = null!;
            public string constructionLabel = null!;
            public string strengthLabel = null!;
            public string dailyFoodLabel = null!;
            public string resistanceLabel = null!;
            public string resistanceTip = null!;
            public float resistanceValue;
            public string traitsDisplay = null!;
            public string traitsTip = null!;
            public string xenotypeDisplay = null!;
            public string xenotypeTip = null!;
            public string psycastsDisplay = null!;
            public string psycastsTip = null!;
            public bool needsHealing;
            /// <summary>Outpost prisoner currently occupying a recruit slot.</summary>
            public bool isBeingRecruited;
            public int prisonerIndex;
            public int prisonerCount;
            /// <summary>Animals/vehicles/shuttles show dashes for skill/age columns.</summary>
            public bool sparseSkills;
            public bool isStarred;
            public bool isGroupHeader;
            public string groupHeaderLabel = null!;
        }

        private static void EnsureHeaderLabels()
        {
            if (headerLabelsReady) return;
            hdrNoPawns = "TSA_WD_NoPawns".Translate();
            hdrName = "TSA_WD_PawnCol_PawnName".Translate();
            hdrAge = "TSA_WD_PawnCol_Age".Translate();
            hdrMelee = "TSA_WD_PawnCol_Melee".Translate();
            hdrStrength = "TSA_WD_Outpost_Strength".Translate();
            hdrSortTip = "TSA_WD_PawnCol_SortTooltip".Translate();
            hdrSelectColumnTip = "TSA_WD_PawnCol_SelectColumnTip".Translate();
            hdrSlaveRemoveBlockedTip = "TSA_WD_Pawns_RemoveSlaveAccompanimentRequiredTip".Translate();
            hdrXpSuffix = "TSA_WD_PawnCol_XpSuffix".Translate();
            hdrRelevantXp = "TSA_WD_PawnCol_RelevantXpHeader".Translate();
            hdrHunt = "TSA_WD_PawnCol_Hunt".Translate();
            hdrConstructionRoadTip = "TSA_WD_PawnCol_ConstructionRoadTip".Translate();
            hdrDailyFood = "TSA_WD_PawnCol_DailyFood".Translate();
            hdrHurt = "TSA_WD_PawnCol_Hurt".Translate();
            hdrHurtTip = "TSA_WD_PawnCol_HurtTip".Translate();
            hdrTransfer = "TSA_WD_AllPlayerPawns_Transfer".Translate();
            hdrTransferTip = "TSA_WD_AllPlayerPawns_TransferTip".Translate();
            hdrStarTip = "TSA_WD_AllPlayerPawns_StarTip".Translate();
            hdrResistance = "TSA_WD_Prisoners_ColResistance".Translate();
            hdrTraits = "TSA_WD_Prisoners_ColTraits".Translate();
            headerLabelsReady = true;
        }

        private static void DrawHurtCell(ref float x, float curY, bool needsHealing, float rowHeight)
        {
            Rect cell = new Rect(x, curY, ColHurt, rowHeight);
            if (needsHealing)
            {
                OutpostHealthUIUtils.DrawBleedingIconCentered(cell, rowHeight, hdrHurtTip);
            }
            x += ColHurt;
        }

        private static float GetRowHeight(CachedPawnRow row)
        {
            if (row != null && row.rowKind == OutpostPawnRowKind.Prisoner)
                return PrisonerRowHeight;
            if (ColOn(PawnRosterColumnIds.Traits) || ColOn(PawnRosterColumnIds.Psycasts))
                return TallRowHeight;
            return RowHeight;
        }

        private static void LabelAnchored(Rect rect, string text, TextAnchor anchor)
        {
            TextAnchor prev = Text.Anchor;
            Text.Anchor = anchor;
            Widgets.Label(rect, text);
            Text.Anchor = prev;
        }

        private static string BuildPortraitCacheKey(Pawn p, VirtualPawnSummary v)
        {
            return PawnPortraitUIUtils.BuildCacheKey(p, v);
        }

        /// <summary>Vanilla PortraitsCache.Get via shared helper (do not cache the Texture — pooled RTs).</summary>
        private static Texture GetPortraitFor(Pawn pawn, string key)
        {
            return PawnPortraitUIUtils.GetPortrait(pawn, PortraitSize, key)!;
        }

        private static Texture GetStoredTransportIcon(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed) return null!;
            if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(pawn))
                return VehicleFrameworkOutpostDissolveCompat.TryGetVehicleIcon(pawn) ?? pawn.def?.uiIcon ?? null!;
            if (pawn.RaceProps?.Humanlike == true)
                return GetPortraitFor(pawn, BuildPortraitCacheKey(pawn, VirtualPawnSummary.FromPawn(pawn)));
            return pawn.def?.uiIcon ?? pawn.kindDef?.race?.uiIcon ?? null!;
        }

        public WITab_Outpost_Pawns()
        {
            size = new Vector2(1280f, 560f);
            labelKey = "TSA_WD_Outpost_Pawns";
        }

        public WorldObject_WD_Outpost SelOutpost => SelObject as WorldObject_WD_Outpost ?? null!;

        public override bool IsVisible => SelOutpost != null && SelOutpost.Faction == Faction.OfPlayer;

        private static bool ColOn(string id) => PlayerPawnRosterUtility.ColVisible(ColWindow, id);

        private void OnColumnsChanged()
        {
            if (!IsCurrentSortColumnVisible())
            {
                useDefaultGrouping = true;
                sortColumn = OutpostPawnTableSortColumn.Default;
                sortAscending = true;
            }
            lastCacheTick = -1;
        }

        private bool IsCurrentSortColumnVisible()
        {
            switch (sortColumn)
            {
                case OutpostPawnTableSortColumn.Traits:
                    return ColOn(PawnRosterColumnIds.Traits);
                case OutpostPawnTableSortColumn.Xenotype:
                    return ColOn(PawnRosterColumnIds.Xenotype);
                case OutpostPawnTableSortColumn.Psycasts:
                    return ColOn(PawnRosterColumnIds.Psycasts);
                case OutpostPawnTableSortColumn.Hurt:
                    return ColOn(PawnRosterColumnIds.Hurt);
                case OutpostPawnTableSortColumn.RelevantCombined:
                case OutpostPawnTableSortColumn.RelevantXp:
                    return ColOn(PawnRosterColumnIds.Relevant);
                case OutpostPawnTableSortColumn.Plants:
                    return ColOn(PawnRosterColumnIds.FullSkill(SkillDefOf.Plants));
                case OutpostPawnTableSortColumn.Animals:
                    return ColOn(PawnRosterColumnIds.FullSkill(SkillDefOf.Animals));
                case OutpostPawnTableSortColumn.Social:
                    return ColOn(PawnRosterColumnIds.FullSkill(SkillDefOf.Social));
                case OutpostPawnTableSortColumn.Mining:
                    return ColOn(PawnRosterColumnIds.FullSkill(SkillDefOf.Mining));
                case OutpostPawnTableSortColumn.Crafting:
                    return ColOn(PawnRosterColumnIds.FullSkill(SkillDefOf.Crafting));
                case OutpostPawnTableSortColumn.Intellectual:
                    return ColOn(PawnRosterColumnIds.FullSkill(SkillDefOf.Intellectual));
                case OutpostPawnTableSortColumn.Cooking:
                    return ColOn(PawnRosterColumnIds.FullSkill(SkillDefOf.Cooking));
                case OutpostPawnTableSortColumn.Medicine:
                    return ColOn(PawnRosterColumnIds.FullSkill(SkillDefOf.Medicine));
                case OutpostPawnTableSortColumn.Artistic:
                    return ColOn(PawnRosterColumnIds.FullSkill(SkillDefOf.Artistic));
                default:
                    return true;
            }
        }

        private void RestoreDefaultView()
        {
            useDefaultGrouping = true;
            sortColumn = OutpostPawnTableSortColumn.Default;
            sortAscending = true;
            pawnTypeFilter = PlayerPawnTypeFilter.All;
            pawnSearchTerm = "";
            starFilter = OutpostPawnStarFilter.All;
            xenotypeFilter = "";
            psycastFilter = "";
            scrollPosition = Vector2.zero;
            lastCacheTick = -1;
            PlayerPawnRosterUtility.ResetSkillDisplayOptions(ColWindow);
            WorldComponent_PawnRosterColumnPrefs.Get()?.ResetToDefaults(ColWindow);
            PawnRosterTraitFilter.Clear();
            PawnRosterHeaderFilter.CloseDropdown();
        }

        /// <summary>Fixed columns excluding the flexible Name column.</summary>
        private float ComputeFixedColumnsWidth(bool hasRelevant)
        {
            float w = 0f;
            if (ColOn(PawnRosterColumnIds.Portrait)) w += ColPortrait;
            if (ColOn(PawnRosterColumnIds.Type)) w += ColPawnType;
            if (ColOn(PawnRosterColumnIds.Star)) w += ColStar;
            if (ColOn(PawnRosterColumnIds.Select)) w += ColSelect;
            if (ColOn(PawnRosterColumnIds.Reorder)) w += ColReorder;
            if (ColOn(PawnRosterColumnIds.Resistance)) w += ColResistance;
            if (ColOn(PawnRosterColumnIds.Traits)) w += ColTraits;
            if (ColOn(PawnRosterColumnIds.Xenotype)) w += ColXenotype;
            if (ColOn(PawnRosterColumnIds.Psycasts)) w += ColPsycasts;
            if (ColOn(PawnRosterColumnIds.Age)) w += ColAge;
            if (ColOn(PawnRosterColumnIds.Shooting)) w += ColSkill;
            if (ColOn(PawnRosterColumnIds.Melee)) w += ColSkill;
            if (ColOn(PawnRosterColumnIds.Strength)) w += ColStrength;
            if (hasRelevant && ColOn(PawnRosterColumnIds.Relevant))
                w += ColSkill + ColRelevantXp;
            if (ColOn(PawnRosterColumnIds.Construction)) w += ColConstruction;
            if (ColOn(PawnRosterColumnIds.DailyFood)) w += ColDailyFood;
            if (ColOn(PawnRosterColumnIds.Hurt)) w += ColHurt;
            if (PlayerPawnRosterUtility.AnyFullSkillColumnVisible(ColWindow))
            {
                w += ColSkillPad;
                SkillDef[] skills = PlayerPawnRosterUtility.AllSkillColumns;
                for (int i = 0; i < skills.Length; i++)
                {
                    if (skills[i] == SkillDefOf.Shooting || skills[i] == SkillDefOf.Melee)
                        continue;
                    if (ColOn(PawnRosterColumnIds.FullSkill(skills[i])))
                        w += ColSkill;
                }
            }
            return w;
        }

        private float colNameWidth = ColName;

        private void UpdateFlexibleNameWidth(float availableInnerWidth, bool hasRelevant)
        {
            if (!ColOn(PawnRosterColumnIds.Name))
            {
                colNameWidth = 0f;
                return;
            }
            float fixedW = ComputeFixedColumnsWidth(hasRelevant);
            colNameWidth = Mathf.Max(ColName, availableInnerWidth - fixedW);
        }

        private float ComputeTotalTableWidth(bool hasRelevant) =>
            ComputeFixedColumnsWidth(hasRelevant) + (ColOn(PawnRosterColumnIds.Name) ? colNameWidth : 0f);

        private void RebuildRowCacheIfNeeded()
        {
            var outpost = SelOutpost;
            if (outpost?.Occupants == null) return;

            string academyTeachingKey = "";
            if (Outpost_Production_Utils.IsAcademyOutpost(outpost.def))
            {
                var teach = Outpost_Academy.GetSkillForCurrentCycle(outpost) ?? outpost.SelectedAcademySkill;
                academyTeachingKey = teach?.defName ?? "";
            }

            string nameFilter = pawnSearchTerm ?? "";
            int tick = Find.TickManager.TicksGame;
            int prisonerCount = outpost.Prisoners?.Count ?? 0;
            bool dirty = cachedRows == null
                || cachedOutpost != outpost
                || cachedSortCol != sortColumn
                || cachedSortAsc != sortAscending
                || cachedUseDefaultGrouping != useDefaultGrouping
                || cachedTypeFilter != pawnTypeFilter
                || !string.Equals(cachedNameFilter, nameFilter, StringComparison.Ordinal)
                || cachedStarFilter != starFilter
                || !string.Equals(cachedXenotypeFilter, xenotypeFilter, StringComparison.Ordinal)
                || !string.Equals(cachedPsycastFilter, psycastFilter, StringComparison.Ordinal)
                || cachedPrisonerCount != prisonerCount
                || cacheInvalidated
                || tick - lastCacheTick >= CacheRefreshInterval
                || !string.Equals(cachedAcademyTeachingSkillKey, academyTeachingKey, StringComparison.Ordinal);
            if (!dirty) return;

            cacheInvalidated = false;
            cachedAcademyTeachingSkillKey = academyTeachingKey;
            cachedTypeFilter = pawnTypeFilter;
            cachedNameFilter = nameFilter;
            cachedStarFilter = starFilter;
            cachedXenotypeFilter = xenotypeFilter ?? "";
            cachedPsycastFilter = psycastFilter ?? "";
            cachedPrisonerCount = prisonerCount;

            if (cachedOutpost != null && cachedOutpost != outpost)
                selectedForRemovalThingIds.Clear();

            lastCacheTick = tick;
            cachedOutpost = outpost;

            cachedRelevantDefs = WorldObject_WD_Outpost.GetRelevantSkillDefsForPawnsTab(outpost);

            if ((sortColumn == OutpostPawnTableSortColumn.RelevantCombined
                    || sortColumn == OutpostPawnTableSortColumn.RelevantXp)
                && cachedRelevantDefs.Count == 0)
            {
                sortColumn = OutpostPawnTableSortColumn.Default;
                useDefaultGrouping = true;
            }

            cachedSortCol = sortColumn;
            cachedSortAsc = sortAscending;
            cachedUseDefaultGrouping = useDefaultGrouping;

            if (cachedRelevantDefs.Count == 1)
                cachedRelHeader = cachedRelevantDefs[0].LabelCap;
            else if (cachedRelevantDefs.Count == 2
                && cachedRelevantDefs.Contains(SkillDefOf.Shooting)
                && cachedRelevantDefs.Contains(SkillDefOf.Animals))
                cachedRelHeader = hdrHunt;
            else if (cachedRelevantDefs.Count > 0)
                cachedRelHeader = JoinSkillLabels(cachedRelevantDefs);
            else
                cachedRelHeader = null!;

            if (cachedRelevantDefs.Count > 0)
            {
                string skillName = cachedRelevantDefs.Count == 1
                    ? Outpost_Production_Utils.SkillLabelCap(cachedRelevantDefs[0])
                    : JoinSkillLabels(cachedRelevantDefs);
                cachedXpTooltip = "TSA_WD_PawnCol_RelevantXpTooltip".Translate(skillName);
                if (cachedXpTooltip.Contains("TSA_WD_"))
                    cachedXpTooltip = "XP toward next level in: " + skillName;

                cachedXpHeaderLabel = cachedRelevantDefs.Count == 1
                    ? cachedRelevantDefs[0].LabelCap + " " + hdrXpSuffix
                    : hdrRelevantXp;
            }

            if (cachedRows == null)
                cachedRows = new List<CachedPawnRow>(32);
            else
                cachedRows.Clear();

            bool hasRelevant = cachedRelevantDefs.Count > 0;
            float foodPerPawn = WorldDominationMod.settings?.foodConsumptionPerPawn ?? WorldDominationSettings.DefFoodConsumptionPerPawn;
            string nameFilterLower = string.IsNullOrEmpty(nameFilter) ? null : nameFilter.ToLowerInvariant();

            var pawns = outpost.Occupants;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null) continue;
                CachedPawnRow row = BuildOccupantRow(p, hasRelevant, foodPerPawn);
                if (PassesFilters(row, nameFilterLower))
                    cachedRows.Add(row);
            }

            List<Pawn> stored = outpost.StoredAnimalsAndVehicles;
            if (stored != null)
            {
                for (int i = 0; i < stored.Count; i++)
                {
                    Pawn p = stored[i];
                    if (p == null || p.Destroyed || p.Dead) continue;
                    CachedPawnRow row = BuildStoredTransportRow(p);
                    if (PassesFilters(row, nameFilterLower))
                        cachedRows.Add(row);
                }
            }

            List<Pawn> mechs = outpost.StoredMechanoids;
            if (mechs != null)
            {
                for (int i = 0; i < mechs.Count; i++)
                {
                    Pawn p = mechs[i];
                    if (p == null || p.Destroyed || p.Dead) continue;
                    CachedPawnRow row = BuildMechanoidRow(p, hasRelevant);
                    if (PassesFilters(row, nameFilterLower))
                        cachedRows.Add(row);
                }
            }

            if (ModsConfig.OdysseyActive)
            {
                List<Thing> shuttles = outpost.StoredPassengerShuttles;
                if (shuttles != null)
                {
                    for (int i = 0; i < shuttles.Count; i++)
                    {
                        if (shuttles[i] is Building_PassengerShuttle shuttle && !shuttle.Destroyed)
                        {
                            CachedPawnRow row = BuildShuttleRow(shuttle);
                            if (PassesFilters(row, nameFilterLower))
                                cachedRows.Add(row);
                        }
                    }
                }
            }

            if (cachedRows.Count > 1)
                SortCachedRows(outpost);

            AppendPrisonerRows(outpost, hasRelevant, foodPerPawn, nameFilterLower);
            ApplyTraitFilterToCachedRows();
        }

        private void ApplyTraitFilterToCachedRows()
        {
            if (cachedRows == null || !PawnRosterTraitFilter.FilterApplies(ColWindow))
                return;
            cachedRows.RemoveAll(r =>
                !r.isGroupHeader && (r.pawn == null || !PawnRosterTraitFilter.Matches(r.pawn)));
            for (int i = cachedRows.Count - 1; i >= 0; i--)
            {
                if (!cachedRows[i].isGroupHeader) continue;
                bool hasBody = i + 1 < cachedRows.Count && !cachedRows[i + 1].isGroupHeader;
                if (!hasBody)
                    cachedRows.RemoveAt(i);
            }
        }

        private void AppendPrisonerRows(
            WorldObject_WD_Outpost outpost,
            bool hasRelevant,
            float foodPerPawn,
            string nameFilterLower)
        {
            List<Pawn> captives = outpost.Prisoners;
            if (captives == null || captives.Count == 0) return;

            var prisonerRows = new List<CachedPawnRow>(captives.Count);
            for (int i = 0; i < captives.Count; i++)
            {
                Pawn p = captives[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                CachedPawnRow row = BuildPrisonerRow(p, hasRelevant, foodPerPawn, i, captives.Count);
                if (PassesFilters(row, nameFilterLower))
                    prisonerRows.Add(row);
            }
            if (prisonerRows.Count == 0) return;

            cachedRows.Add(new CachedPawnRow
            {
                rowKind = OutpostPawnRowKind.GroupHeader,
                isGroupHeader = true,
                groupHeaderLabel = "TSA_WD_Prisoners_GroupOutpostHeader".Translate(
                    OutpostPrisonerResistanceScaling.GetConcurrentRecruitSlots(outpost).ToString()),
                nameLabel = "",
                typeLabel = "",
                resistanceValue = -1f,
                resistanceLabel = "",
                traitsDisplay = "",
                traitsTip = ""
            });
            cachedRows.AddRange(prisonerRows);
        }

        private bool PassesFilters(
            CachedPawnRow row,
            string nameFilterLower,
            bool applyTypeFilter = true,
            bool applyStarFilter = true,
            bool applyXenotypeFilter = true,
            bool applyPsycastFilter = true)
        {
            if (row != null && row.isGroupHeader) return true;
            if (applyTypeFilter
                && ColOn(PawnRosterColumnIds.Type)
                && pawnTypeFilter != PlayerPawnTypeFilter.All
                && row.sortCategory != PlayerPawnRosterUtility.ToSortCategory(pawnTypeFilter))
                return false;
            if (ColOn(PawnRosterColumnIds.Name)
                && nameFilterLower != null
                && (row.nameLabel == null || !row.nameLabel.ToLowerInvariant().Contains(nameFilterLower)))
                return false;
            if (applyStarFilter && ColOn(PawnRosterColumnIds.Star))
            {
                if (starFilter == OutpostPawnStarFilter.Starred && !row.isStarred)
                    return false;
                if (starFilter == OutpostPawnStarFilter.NotStarred && row.isStarred)
                    return false;
            }
            if (applyXenotypeFilter
                && ColOn(PawnRosterColumnIds.Xenotype)
                && !xenotypeFilter.NullOrEmpty()
                && !PawnRosterTraitFilter.MatchesXenotype(row.pawn, xenotypeFilter))
                return false;
            if (applyPsycastFilter
                && ColOn(PawnRosterColumnIds.Psycasts)
                && !psycastFilter.NullOrEmpty()
                && !PawnRosterTraitFilter.MatchesPsycast(row.pawn, psycastFilter))
                return false;
            return true;
        }

        private void ForEachFilterCountRow(
            Action<CachedPawnRow> consider,
            bool applyTypeFilter,
            bool applyStarFilter,
            bool applyXenotypeFilter,
            bool applyPsycastFilter = true)
        {
            var outpost = SelOutpost;
            if (outpost?.Occupants == null || consider == null) return;

            bool hasRelevant = cachedRelevantDefs != null && cachedRelevantDefs.Count > 0;
            float foodPerPawn = WorldDominationMod.settings?.foodConsumptionPerPawn ?? WorldDominationSettings.DefFoodConsumptionPerPawn;
            string nameFilterLower = string.IsNullOrEmpty(pawnSearchTerm) ? null : pawnSearchTerm.ToLowerInvariant();
            bool traitFilter = PawnRosterTraitFilter.FilterApplies(ColWindow);

            void tryAdd(CachedPawnRow row)
            {
                if (row == null || row.isGroupHeader) return;
                if (!PassesFilters(row, nameFilterLower, applyTypeFilter, applyStarFilter, applyXenotypeFilter, applyPsycastFilter))
                    return;
                if (traitFilter && (row.pawn == null || !PawnRosterTraitFilter.Matches(row.pawn))) return;
                consider(row);
            }

            var pawns = outpost.Occupants;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null) continue;
                tryAdd(BuildOccupantRow(p, hasRelevant, foodPerPawn));
            }

            List<Pawn> stored = outpost.StoredAnimalsAndVehicles;
            if (stored != null)
            {
                for (int i = 0; i < stored.Count; i++)
                {
                    Pawn p = stored[i];
                    if (p == null || p.Destroyed || p.Dead) continue;
                    tryAdd(BuildStoredTransportRow(p));
                }
            }

            List<Pawn> mechs = outpost.StoredMechanoids;
            if (mechs != null)
            {
                for (int i = 0; i < mechs.Count; i++)
                {
                    Pawn p = mechs[i];
                    if (p == null || p.Destroyed || p.Dead) continue;
                    tryAdd(BuildMechanoidRow(p, hasRelevant));
                }
            }

            if (ModsConfig.OdysseyActive)
            {
                List<Thing> shuttles = outpost.StoredPassengerShuttles;
                if (shuttles != null)
                {
                    for (int i = 0; i < shuttles.Count; i++)
                    {
                        if (shuttles[i] is Building_PassengerShuttle shuttle && !shuttle.Destroyed)
                            tryAdd(BuildShuttleRow(shuttle));
                    }
                }
            }

            List<Pawn> captives = outpost.Prisoners;
            if (captives != null)
            {
                for (int i = 0; i < captives.Count; i++)
                {
                    Pawn p = captives[i];
                    if (p == null || p.Destroyed || p.Dead) continue;
                    tryAdd(BuildPrisonerRow(p, hasRelevant, foodPerPawn, i, captives.Count));
                }
            }
        }

        private List<PlayerPawnSortCategory> TypePopulationForFilterDialog()
        {
            var cats = new List<PlayerPawnSortCategory>();
            ForEachFilterCountRow(
                row => cats.Add(row.sortCategory),
                applyTypeFilter: false,
                applyStarFilter: true,
                applyXenotypeFilter: true);
            return cats;
        }

        private List<bool> StarPopulationForFilterDialog()
        {
            var flags = new List<bool>();
            ForEachFilterCountRow(
                row => flags.Add(row.isStarred),
                applyTypeFilter: true,
                applyStarFilter: false,
                applyXenotypeFilter: true);
            return flags;
        }

        private List<string> XenotypePopulationForFilterDialog()
        {
            var keys = new List<string>();
            ForEachFilterCountRow(
                row => keys.Add(PawnRosterHeaderFilter.XenotypeKey(row.pawn)),
                applyTypeFilter: true,
                applyStarFilter: true,
                applyXenotypeFilter: false);
            return keys;
        }

        private List<List<string>> PsycastPopulationForFilterDialog()
        {
            var lists = new List<List<string>>();
            ForEachFilterCountRow(
                row => lists.Add(PawnRosterHeaderFilter.PsycastKeysOnPawn(row.pawn)),
                applyTypeFilter: true,
                applyStarFilter: true,
                applyXenotypeFilter: true,
                applyPsycastFilter: false);
            return lists;
        }

        private CachedPawnRow BuildOccupantRow(Pawn p, bool hasRelevant, float foodPerPawn)
        {
            var v = VirtualPawnSummary.FromPawn(p);
            var cat = PlayerPawnRosterUtility.ClassifyPawn(p, PlayerPawnOutpostRole.Occupant);
            var row = new CachedPawnRow
            {
                pawn = p,
                summary = v,
                rowKind = OutpostPawnRowKind.Occupant,
                sortCategory = cat,
                typeLabel = PlayerPawnRosterUtility.GetPawnTypeLabel(cat),
                isSlave = OutpostPawnIdeologyUtil.IsSlaveHumanlike(p),
                sparseSkills = false
            };
            row.portraitKey = BuildPortraitCacheKey(p, v);
            row.nameLabel = p.Name?.ToStringFull ?? p.Label ?? "—";
            row.isStarred = WorldComponent_PlayerPawnFavorites.Get()?.IsStarred(p.ThingID) == true;
            row.constructionLabel = "—";
            row.resistanceLabel = "—";
            row.resistanceValue = -1f;
            row.traitsDisplay = "—";
            row.traitsTip = "";
            row.xenotypeDisplay = "—";
            row.xenotypeTip = "";
            row.psycastsDisplay = "—";
            row.psycastsTip = "";
            if (p.RaceProps?.Humanlike == true)
            {
                PrisonerRosterUtility.FormatTraits(p, out row.traitsDisplay, out row.traitsTip);
                PawnRosterTraitFilter.FormatXenotype(p, out row.xenotypeDisplay, out row.xenotypeTip);
                PawnRosterTraitFilter.FormatPsycasts(p, out row.psycastsDisplay, out row.psycastsTip);
            }
            if (v != null)
            {
                string ageLabel = "—";
                try
                {
                    if (p.ageTracker != null)
                        ageLabel = p.ageTracker.AgeBiologicalYears.ToString();
                }
                catch
                {
                    ageLabel = v.biologicalAgeYears.ToString("F0");
                }
                row.ageLabel = ageLabel ?? "—";
                row.shootingLabel = v.shooting.ToString();
                row.meleeLabel = v.melee.ToString();
                row.constructionLabel = v.construction.ToString();
                row.strengthLabel = v.CombatStrength.ToString("F0");
                row.dailyFoodLabel = OutpostPawnClassificationUtil.ConsumesVirtualFood(p)
                    ? foodPerPawn.ToString("F1")
                    : "0";
                row.needsHealing = Outpost_OccupantProgression.OccupantShowsHurtIcon(p);
                if (hasRelevant)
                {
                    float relVal = 0f;
                    for (int ri = 0; ri < cachedRelevantDefs.Count; ri++)
                    {
                        var sd = cachedRelevantDefs[ri];
                        if (sd != null) relVal += v.GetSkill(sd);
                    }
                    row.relevantSkillLabel = relVal.ToString("F0");
                    row.xpProgressLabel = VirtualPawnSummary.FormatRelevantSkillsXpProgress(p, cachedRelevantDefs);
                }
            }
            return row;
        }

        private CachedPawnRow BuildPrisonerRow(Pawn p, bool hasRelevant, float foodPerPawn, int prisonerIndex, int prisonerCount)
        {
            var v = VirtualPawnSummary.FromPawn(p);
            bool recruiting = OutpostPrisonerUtility.IsCurrentlyBeingRecruited(SelOutpost, p);
            var row = new CachedPawnRow
            {
                pawn = p,
                summary = v,
                rowKind = OutpostPawnRowKind.Prisoner,
                sortCategory = PlayerPawnSortCategory.Human,
                typeLabel = "TSA_WD_PawnType_Prisoner".Translate(),
                isSlave = false,
                sparseSkills = false,
                isBeingRecruited = recruiting,
                prisonerIndex = prisonerIndex,
                prisonerCount = prisonerCount
            };
            row.portraitKey = BuildPortraitCacheKey(p, v);
            row.nameLabel = p.Name?.ToStringFull ?? p.Label ?? "—";
            row.isStarred = WorldComponent_PlayerPawnFavorites.Get()?.IsStarred(p.ThingID) == true;
            row.constructionLabel = "—";
            row.resistanceValue = p.guest?.resistance ?? 0f;
            float daily = recruiting ? OutpostPrisonerResistanceScaling.GetDailyDrop(SelOutpost) : 0f;
            row.resistanceLabel = OutpostPrisonerResistanceScaling.FormatRateLabel(row.resistanceValue, daily);
            row.resistanceTip = OutpostPrisonerResistanceScaling.BuildTooltip(SelOutpost);
            PrisonerRosterUtility.FormatTraits(p, out row.traitsDisplay, out row.traitsTip);
            PawnRosterTraitFilter.FormatXenotype(p, out row.xenotypeDisplay, out row.xenotypeTip);
            PawnRosterTraitFilter.FormatPsycasts(p, out row.psycastsDisplay, out row.psycastsTip);
            if (v != null)
            {
                string ageLabel = "—";
                try
                {
                    if (p.ageTracker != null)
                        ageLabel = p.ageTracker.AgeBiologicalYears.ToString();
                }
                catch
                {
                    ageLabel = v.biologicalAgeYears.ToString("F0");
                }
                row.ageLabel = ageLabel ?? "—";
                row.shootingLabel = v.shooting.ToString();
                row.meleeLabel = v.melee.ToString();
                row.constructionLabel = v.construction.ToString();
                row.strengthLabel = "0";
                row.dailyFoodLabel = OutpostPawnClassificationUtil.ConsumesVirtualFood(p)
                    ? foodPerPawn.ToString("F1")
                    : "0";
                row.needsHealing = Outpost_OccupantProgression.OccupantShowsHurtIcon(p);
                if (hasRelevant)
                {
                    float relVal = 0f;
                    for (int ri = 0; ri < cachedRelevantDefs.Count; ri++)
                    {
                        var sd = cachedRelevantDefs[ri];
                        if (sd != null) relVal += v.GetSkill(sd);
                    }
                    row.relevantSkillLabel = relVal.ToString("F0");
                    row.xpProgressLabel = VirtualPawnSummary.FormatRelevantSkillsXpProgress(p, cachedRelevantDefs);
                }
            }
            return row;
        }

        private static CachedPawnRow BuildStoredTransportRow(Pawn p)
        {
            var cat = PlayerPawnRosterUtility.ClassifyPawn(p, PlayerPawnOutpostRole.StoredTransport);
            return new CachedPawnRow
            {
                pawn = p,
                rowKind = OutpostPawnRowKind.StoredTransport,
                sortCategory = cat,
                typeLabel = PlayerPawnRosterUtility.GetPawnTypeLabel(cat),
                nameLabel = p.LabelCap ?? p.Label ?? "—",
                isStarred = WorldComponent_PlayerPawnFavorites.Get()?.IsStarred(p.ThingID) == true,
                ageLabel = "—",
                shootingLabel = "—",
                meleeLabel = "—",
                relevantSkillLabel = "—",
                xpProgressLabel = "—",
                constructionLabel = "—",
                strengthLabel = WorldObject_WD_Outpost.GetStoredTransportCombatStrength(p).ToString("F0"),
                dailyFoodLabel = "0",
                resistanceLabel = "—",
                resistanceValue = -1f,
                traitsDisplay = "—",
                traitsTip = "",
                sparseSkills = true,
                needsHealing = false
            };
        }

        private CachedPawnRow BuildMechanoidRow(Pawn p, bool hasRelevant)
        {
            var v = VirtualPawnSummary.FromPawn(p);
            var cat = PlayerPawnRosterUtility.ClassifyPawn(p, PlayerPawnOutpostRole.StoredMechanoid);
            var row = new CachedPawnRow
            {
                pawn = p,
                summary = v,
                rowKind = OutpostPawnRowKind.StoredMechanoid,
                sortCategory = cat,
                typeLabel = PlayerPawnRosterUtility.GetPawnTypeLabel(cat),
                nameLabel = p.LabelCap ?? p.Label ?? "—",
                isStarred = WorldComponent_PlayerPawnFavorites.Get()?.IsStarred(p.ThingID) == true,
                ageLabel = "—",
                dailyFoodLabel = "0",
                resistanceLabel = "—",
                resistanceValue = -1f,
                traitsDisplay = "—",
                traitsTip = "",
                sparseSkills = false,
                needsHealing = false
            };
            if (v != null)
            {
                row.shootingLabel = v.shooting.ToString();
                row.meleeLabel = v.melee.ToString();
                row.constructionLabel = v.construction.ToString();
                row.strengthLabel = v.CombatStrength.ToString("F0");
                if (hasRelevant)
                {
                    float relVal = 0f;
                    for (int ri = 0; ri < cachedRelevantDefs.Count; ri++)
                    {
                        var sd = cachedRelevantDefs[ri];
                        if (sd != null) relVal += v.GetSkill(sd);
                    }
                    row.relevantSkillLabel = relVal.ToString("F0");
                    row.xpProgressLabel = "—";
                }
            }
            else
            {
                row.shootingLabel = "—";
                row.meleeLabel = "—";
                row.constructionLabel = "—";
                row.strengthLabel = "0";
                row.relevantSkillLabel = "—";
                row.xpProgressLabel = "—";
            }
            return row;
        }

        private static CachedPawnRow BuildShuttleRow(Building_PassengerShuttle shuttle)
        {
            return new CachedPawnRow
            {
                shuttle = shuttle,
                rowKind = OutpostPawnRowKind.Shuttle,
                sortCategory = PlayerPawnSortCategory.Vehicle,
                typeLabel = PlayerPawnRosterUtility.GetPawnTypeLabel(PlayerPawnSortCategory.Vehicle),
                nameLabel = shuttle.LabelCap ?? shuttle.Label ?? "—",
                ageLabel = "—",
                shootingLabel = "—",
                meleeLabel = "—",
                relevantSkillLabel = "—",
                xpProgressLabel = "—",
                constructionLabel = "—",
                strengthLabel = "0",
                dailyFoodLabel = "0",
                resistanceLabel = "—",
                resistanceValue = -1f,
                traitsDisplay = "—",
                traitsTip = "",
                sparseSkills = true,
                needsHealing = false
            };
        }

        private void SortCachedRows(WorldObject_WD_Outpost outpost)
        {
            if (useDefaultGrouping || sortColumn == OutpostPawnTableSortColumn.Default)
            {
                cachedRows.Sort((a, b) =>
                {
                    int catCmp = ((int)a.sortCategory).CompareTo((int)b.sortCategory);
                    if (catCmp != 0) return catCmp;
                    return string.Compare(a.nameLabel, b.nameLabel, StringComparison.OrdinalIgnoreCase);
                });
                return;
            }

            if (sortColumn == OutpostPawnTableSortColumn.PawnType)
            {
                cachedRows.Sort((a, b) =>
                {
                    int cmp = string.Compare(a.typeLabel, b.typeLabel, StringComparison.OrdinalIgnoreCase);
                    if (cmp == 0)
                        cmp = string.Compare(a.nameLabel, b.nameLabel, StringComparison.OrdinalIgnoreCase);
                    return sortAscending ? cmp : -cmp;
                });
                return;
            }

            if (sortColumn == OutpostPawnTableSortColumn.Starred)
            {
                cachedRows.Sort((a, b) =>
                {
                    int cmp = (a.isStarred ? 1 : 0).CompareTo(b.isStarred ? 1 : 0);
                    if (cmp == 0)
                        cmp = string.Compare(a.nameLabel, b.nameLabel, StringComparison.OrdinalIgnoreCase);
                    return sortAscending ? cmp : -cmp;
                });
                return;
            }

            if (sortColumn == OutpostPawnTableSortColumn.Resistance)
            {
                cachedRows.Sort((a, b) =>
                {
                    int cmp = a.resistanceValue.CompareTo(b.resistanceValue);
                    if (cmp == 0)
                        cmp = string.Compare(a.nameLabel, b.nameLabel, StringComparison.OrdinalIgnoreCase);
                    return sortAscending ? cmp : -cmp;
                });
                return;
            }

            if (sortColumn == OutpostPawnTableSortColumn.Traits)
            {
                cachedRows.Sort((a, b) =>
                {
                    int cmp = string.Compare(a.traitsTip, b.traitsTip, StringComparison.OrdinalIgnoreCase);
                    if (cmp == 0)
                        cmp = string.Compare(a.nameLabel, b.nameLabel, StringComparison.OrdinalIgnoreCase);
                    return sortAscending ? cmp : -cmp;
                });
                return;
            }

            if (sortColumn == OutpostPawnTableSortColumn.Xenotype)
            {
                cachedRows.Sort((a, b) =>
                {
                    int cmp = string.Compare(a.xenotypeDisplay, b.xenotypeDisplay, StringComparison.OrdinalIgnoreCase);
                    if (cmp == 0)
                        cmp = string.Compare(a.nameLabel, b.nameLabel, StringComparison.OrdinalIgnoreCase);
                    return sortAscending ? cmp : -cmp;
                });
                return;
            }

            if (sortColumn == OutpostPawnTableSortColumn.Psycasts)
            {
                cachedRows.Sort((a, b) =>
                {
                    int cmp = string.Compare(a.psycastsDisplay, b.psycastsDisplay, StringComparison.OrdinalIgnoreCase);
                    if (cmp == 0)
                        cmp = string.Compare(a.nameLabel, b.nameLabel, StringComparison.OrdinalIgnoreCase);
                    return sortAscending ? cmp : -cmp;
                });
                return;
            }

            if (sortColumn == OutpostPawnTableSortColumn.Hurt)
            {
                cachedRows.Sort((a, b) =>
                {
                    int cmp = (a.needsHealing ? 1 : 0).CompareTo(b.needsHealing ? 1 : 0);
                    if (cmp == 0)
                        cmp = string.Compare(a.nameLabel, b.nameLabel, StringComparison.OrdinalIgnoreCase);
                    return sortAscending ? cmp : -cmp;
                });
                return;
            }

            if (sortColumn == OutpostPawnTableSortColumn.RelevantXp)
            {
                List<SkillDef> rel = cachedRelevantDefs;
                cachedRows.Sort((a, b) =>
                {
                    int cmp = VirtualPawnSummary.RelevantXpSortKey(a.pawn, rel)
                        .CompareTo(VirtualPawnSummary.RelevantXpSortKey(b.pawn, rel));
                    if (cmp == 0)
                        cmp = string.Compare(a.nameLabel, b.nameLabel, StringComparison.OrdinalIgnoreCase);
                    return sortAscending ? cmp : -cmp;
                });
                return;
            }

            OutpostPawnTableSortColumn sc = sortColumn;
            bool sa = sortAscending;
            cachedRows.Sort((a, b) =>
            {
                // Sparse rows (animals/vehicles/shuttles) sort by name when comparing skill-like columns.
                if (a.summary == null && b.summary == null)
                    return string.Compare(a.nameLabel, b.nameLabel, StringComparison.OrdinalIgnoreCase) * (sa ? 1 : -1);
                if (a.summary == null) return sa ? 1 : -1;
                if (b.summary == null) return sa ? -1 : 1;
                return VirtualPawnSummary.CompareForOutpostTableSort(a.summary, b.summary, sc, outpost, sa);
            });
        }

        protected override void FillTab()
        {
            if (SelOutpost?.Occupants == null) return;

            // First ESC closes an open header dropdown; remaining ESC is handled by world-inspect ESC patch.
            if (PawnRosterHeaderFilter.TryCloseDropdownOnCancel())
                return;
            WdWindowEsc.TryDefocusOnCancel();

            EnsureHeaderLabels();
            PruneSelectionToCurrentOccupants();
            PawnRosterPaintSelect.BeginFrame(this);

            Rect body = new Rect(0f, TabHeaderOffsetY, size.x, size.y - TabHeaderOffsetY).ContractedBy(10f);
            bool showRrBar = SelOutpost.IsRapidResponseOutpost;
            float bottomReserve = showRrBar ? BottomBarHeight : 0f;
            Rect bottomBar = new Rect(body.x, body.yMax - BottomBarHeight, body.width, BottomBarHeight);
            Rect content = new Rect(body.x, body.y, body.width, body.height - bottomReserve - (showRrBar ? 6f : 0f));

            // Match All Player Pawns: 0-based layout inside the content rect.
            GUI.BeginGroup(content);

            float tableInnerWidth = content.width - 16f;
            if (tableInnerWidth < 50f) tableInnerWidth = content.width;
            bool hasRelevantPreview = cachedRelevantDefs != null && cachedRelevantDefs.Count > 0;
            UpdateFlexibleNameWidth(tableInnerWidth, hasRelevantPreview);

            Text.Font = GameFont.Medium;
            string headline = OutpostTranslationUtil.TabHeadline(SelOutpost, "TSA_WD_Outpost_Pawns");

            int selectedCount = selectedForRemovalThingIds.Count;
            bool anyTransferableSelected = HasTransferableSelection();
            bool onlyPrisonersSelected = HasPrisonerSelection() && !anyTransferableSelected;

            Rect transferBtn = new Rect(content.width - TransferBtnWidth - TransferBtnRightInset, 4f, TransferBtnWidth, 30f);
            Text.Font = GameFont.Small;
            string selectedLabel = "TSA_WD_AllPlayerPawns_Selected".Translate(selectedCount.ToString());
            float selectedW = Mathf.Max(Text.CalcSize(selectedLabel).x + 8f, 80f);
            Text.Anchor = TextAnchor.MiddleRight;
            Rect selectedRect = new Rect(transferBtn.x - ToolbarBtnGap - selectedW, 6f, selectedW, 28f);
            Widgets.Label(selectedRect, selectedLabel);
            Text.Anchor = TextAnchor.UpperLeft;

            float viewControlsLeft = PlayerPawnRosterUtility.DrawRosterViewControls(
                4f,
                30f,
                selectedRect.x - ToolbarBtnGap,
                ColWindow,
                RestoreDefaultView,
                () => Find.WindowStack.Add(new Dialog_PawnRosterColumns(ColWindow, OnColumnsChanged)));

            Text.Font = GameFont.Medium;
            float headlineW = Mathf.Max(80f, viewControlsLeft - 8f);
            Widgets.Label(new Rect(0f, 0f, headlineW, 32f), headline);

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(0f, 32f, headlineW, 16f),
                "TSA_WD_Outpost_Pawns_ExpertsHint".Translate());
            GUI.color = Color.white;

            if (onlyPrisonersSelected)
            {
                TooltipHandler.TipRegion(transferBtn, "TSA_WD_Prisoners_LetGoSelectedTip".Translate());
                Color prev = GUI.color;
                GUI.color = new Color(0.95f, 0.35f, 0.35f);
                if (Widgets.ButtonText(transferBtn, "TSA_WD_Prisoners_LetGoSelected".Translate()))
                {
                    ConfirmLetGoSelectedPrisoners();
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
                GUI.color = prev;
            }
            else
            {
                TooltipHandler.TipRegion(transferBtn, hdrTransferTip);
                GUI.enabled = anyTransferableSelected;
                if (WorldDomination_UIUtils.ButtonTextWithIcon(
                    transferBtn,
                    WorldDomination_UIUtils.RosterTransferIcon,
                    hdrTransfer))
                {
                    var selected = PlayerPawnRosterUtility.BuildTransferEntriesForOutpost(SelOutpost, selectedForRemovalThingIds);
                    Find.WindowStack.Add(new Dialog_MovePawnToLocation(selected, () =>
                    {
                        selectedForRemovalThingIds.Clear();
                        lastCacheTick = -1;
                    }, offerExitHere: true));
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
                GUI.enabled = true;
            }

            float headerTop = ToolbarHeight + 4f;
            RebuildRowCacheIfNeeded();
            hasRelevantPreview = cachedRelevantDefs != null && cachedRelevantDefs.Count > 0;
            UpdateFlexibleNameWidth(tableInnerWidth, hasRelevantPreview);
            float totalTableWidth = ComputeTotalTableWidth(hasRelevantPreview);

            DrawHorizontallyScrolledSection(
                new Rect(0f, headerTop, content.width, HeaderHeight),
                scrollPosition.x,
                totalTableWidth,
                x =>
                {
                    bodyXForHeader = x;
                    float headerCurY = 0f;
                    DoTableHeader(ref headerCurY);
                });
            Widgets.DrawLineHorizontal(0f, headerTop + HeaderHeight, content.width);

            float listTopY = headerTop + HeaderHeight + 4f;
            Rect listScrollArea = new Rect(0f, listTopY, content.width, content.height - listTopY);
            lastScrollViewportHeight = listScrollArea.height;

            float viewHeight = Mathf.Max(scrollViewHeight, 80f);
            if (viewHeight < listScrollArea.height)
                viewHeight = listScrollArea.height;
            Rect rowViewRect = new Rect(0f, 0f, Mathf.Max(totalTableWidth, tableInnerWidth), viewHeight);

            Widgets.BeginScrollView(listScrollArea, ref scrollPosition, rowViewRect);

            float curY = 0f;
            Text.Font = GameFont.Tiny;
            if (cachedRows == null || cachedRows.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(0f, curY, tableInnerWidth, 24f), hdrNoPawns);
                GUI.color = Color.white;
                curY += 28f;
            }
            else
            {
                for (int i = 0; i < cachedRows.Count; i++)
                    DoPawnRow(ref curY, rowViewRect, cachedRows[i], i % 2 == 0);
            }

            if (Event.current.type == EventType.Layout)
                scrollViewHeight = curY + 8f;

            Widgets.EndScrollView();
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.EndGroup();
            PawnRosterHeaderFilter.DrawDropdownIfOpen();

            if (showRrBar)
            {
                Rect dispatchBtn = new Rect(
                    bottomBar.xMax - TransferBtnWidth - TransferBtnRightInset,
                    bottomBar.y + 4f,
                    TransferBtnWidth,
                    30f);
                DrawRapidResponseDispatchButton(SelOutpost, dispatchBtn, Color.white);
            }
        }

        private bool HasTransferableSelection()
        {
            if (selectedForRemovalThingIds.Count == 0 || SelOutpost == null) return false;
            var outpost = SelOutpost;
            var occ = outpost.Occupants;
            if (occ != null)
            {
                for (int i = 0; i < occ.Count; i++)
                {
                    Pawn p = occ[i];
                    if (p?.ThingID != null && selectedForRemovalThingIds.Contains(p.ThingID))
                        return true;
                }
            }
            var stored = outpost.StoredAnimalsAndVehicles;
            if (stored != null)
            {
                for (int i = 0; i < stored.Count; i++)
                {
                    Pawn p = stored[i];
                    if (p?.ThingID != null && !p.Destroyed && !p.Dead && selectedForRemovalThingIds.Contains(p.ThingID))
                        return true;
                }
            }
            var mechs = outpost.StoredMechanoids;
            if (mechs != null)
            {
                for (int i = 0; i < mechs.Count; i++)
                {
                    Pawn p = mechs[i];
                    if (p?.ThingID != null && !p.Destroyed && !p.Dead && selectedForRemovalThingIds.Contains(p.ThingID))
                        return true;
                }
            }
            var shuttles = outpost.StoredPassengerShuttles;
            if (shuttles != null)
            {
                for (int i = 0; i < shuttles.Count; i++)
                {
                    Thing t = shuttles[i];
                    if (t?.ThingID != null && !t.Destroyed && selectedForRemovalThingIds.Contains(t.ThingID))
                        return true;
                }
            }
            return false;
        }

        private bool HasPrisonerSelection()
        {
            if (selectedForRemovalThingIds.Count == 0 || SelOutpost == null) return false;
            List<Pawn> captives = SelOutpost.Prisoners;
            if (captives == null || captives.Count == 0) return false;
            for (int i = 0; i < captives.Count; i++)
            {
                Pawn p = captives[i];
                if (p?.ThingID != null && selectedForRemovalThingIds.Contains(p.ThingID))
                    return true;
            }
            return false;
        }

        private void ClearNonPrisonerSelection()
        {
            if (selectedForRemovalThingIds.Count == 0 || SelOutpost == null) return;
            var keep = new HashSet<string>();
            List<Pawn> captives = SelOutpost.Prisoners;
            if (captives != null)
            {
                for (int i = 0; i < captives.Count; i++)
                {
                    Pawn p = captives[i];
                    if (p?.ThingID != null && selectedForRemovalThingIds.Contains(p.ThingID))
                        keep.Add(p.ThingID);
                }
            }
            selectedForRemovalThingIds.Clear();
            foreach (string id in keep)
                selectedForRemovalThingIds.Add(id);
        }

        private void ClearPrisonerSelection()
        {
            if (selectedForRemovalThingIds.Count == 0 || SelOutpost == null) return;
            List<Pawn> captives = SelOutpost.Prisoners;
            if (captives == null) return;
            for (int i = 0; i < captives.Count; i++)
            {
                Pawn p = captives[i];
                if (p?.ThingID != null)
                    selectedForRemovalThingIds.Remove(p.ThingID);
            }
        }

        private void ConfirmLetGoSelectedPrisoners()
        {
            if (SelOutpost == null || selectedForRemovalThingIds.Count == 0) return;
            var toRelease = new List<Pawn>();
            List<Pawn> captives = SelOutpost.Prisoners;
            if (captives == null) return;
            for (int i = 0; i < captives.Count; i++)
            {
                Pawn p = captives[i];
                if (p?.ThingID != null && selectedForRemovalThingIds.Contains(p.ThingID))
                    toRelease.Add(p);
            }
            if (toRelease.Count == 0) return;

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "TSA_WD_Prisoners_LetGoConfirm".Translate(),
                () =>
                {
                    for (int i = 0; i < toRelease.Count; i++)
                        SelOutpost?.LetGoPrisoner(toRelease[i]);
                    selectedForRemovalThingIds.Clear();
                    lastCacheTick = -1;
                },
                destructive: true));
        }

        private static void DrawHorizontallyScrolledSection(Rect viewport, float scrollX, float contentWidth, Action<float> draw)
        {
            GUI.BeginGroup(viewport);
            draw(-scrollX);
            GUI.EndGroup();
        }

        private void PruneSelectionToCurrentOccupants()
        {
            var outpost = SelOutpost;
            var occ = outpost?.Occupants;
            if (occ == null || selectedForRemovalThingIds.Count == 0) return;

            var keep = new HashSet<string>();
            for (int i = 0; i < occ.Count; i++)
            {
                Pawn p = occ[i];
                if (p?.ThingID != null) keep.Add(p.ThingID);
            }
            var stored = outpost.StoredAnimalsAndVehicles;
            if (stored != null)
            {
                for (int i = 0; i < stored.Count; i++)
                {
                    Pawn p = stored[i];
                    if (p?.ThingID != null && !p.Destroyed && !p.Dead) keep.Add(p.ThingID);
                }
            }
            var mechs = outpost.StoredMechanoids;
            if (mechs != null)
            {
                for (int i = 0; i < mechs.Count; i++)
                {
                    Pawn p = mechs[i];
                    if (p?.ThingID != null && !p.Destroyed && !p.Dead) keep.Add(p.ThingID);
                }
            }
            var shuttles = outpost.StoredPassengerShuttles;
            if (shuttles != null)
            {
                for (int i = 0; i < shuttles.Count; i++)
                {
                    Thing t = shuttles[i];
                    if (t?.ThingID != null && !t.Destroyed) keep.Add(t.ThingID);
                }
            }
            var captives = outpost.Prisoners;
            if (captives != null)
            {
                for (int i = 0; i < captives.Count; i++)
                {
                    Pawn p = captives[i];
                    if (p?.ThingID != null && !p.Destroyed && !p.Dead) keep.Add(p.ThingID);
                }
            }

            var drop = new List<string>();
            foreach (string id in selectedForRemovalThingIds)
            {
                if (!keep.Contains(id))
                    drop.Add(id);
            }
            for (int i = 0; i < drop.Count; i++)
                selectedForRemovalThingIds.Remove(drop[i]);
        }

        private void DrawRapidResponseDispatchButton(WorldObject_WD_Outpost outpost, Rect btn, Color normalColor)
        {
            var selectedPawns = GetSelectedOccupants(outpost);
            bool hasStoredSelected = SelectedIncludesStoredTransport(outpost) || SelectedIncludesMechanoids(outpost);
            bool researched = RapidResponseUtility.TransportPodsResearched();
            bool hasPawns = selectedPawns.Count > 0;
            bool selectionAllowed = hasPawns && !hasStoredSelected && OutpostPawnIdeologyUtil.BulkRemovalSelectionIsAllowed(outpost, selectedPawns);
            bool leavesGarrison = outpost.Occupants != null && selectedPawns.Count < outpost.Occupants.Count;
            bool enabled = researched && selectionAllowed && leavesGarrison && !HasPrisonerSelection()
                && !outpost.ManualDefenseActive;

            string tip;
            if (outpost.ManualDefenseActive) tip = "TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate();
            else if (!researched) tip = "TSA_WD_RapidResponse_DropPodsNeedResearch".Translate();
            else if (HasPrisonerSelection()) tip = "TSA_WD_RapidResponse_DropPodsPrisonersSelected".Translate();
            else if (!hasPawns) tip = "TSA_WD_RapidResponse_DropPodsSelectPawns".Translate();
            else if (hasStoredSelected) tip = "TSA_WD_RapidResponse_DropPodsNoStoredTransport".Translate();
            else if (!leavesGarrison) tip = "TSA_WD_RapidResponse_DropPodsLeavePawn".Translate();
            else if (!selectionAllowed) tip = hdrSlaveRemoveBlockedTip;
            else tip = "TSA_WD_RapidResponse_DropPodsDesc".Translate();

            TooltipHandler.TipRegion(btn, tip);
            if (!enabled)
                GUI.color = new Color(0.45f, 0.45f, 0.45f, 1f);
            GUI.enabled = enabled;
            if (Widgets.ButtonText(btn, "TSA_WD_RapidResponse_DropPods".Translate(selectedPawns.Count.ToString())))
            {
                CloseTab();
                StartRapidResponseDropPodTargeting(outpost, selectedPawns);
                selectedForRemovalThingIds.Clear();
                lastCacheTick = -1;
            }
            GUI.enabled = true;
            GUI.color = normalColor;
        }

        private List<Pawn> GetSelectedOccupants(WorldObject_WD_Outpost outpost)
        {
            var list = new List<Pawn>();
            var occ = outpost?.Occupants;
            if (occ == null || selectedForRemovalThingIds.Count == 0) return list;
            for (int i = 0; i < occ.Count; i++)
            {
                Pawn p = occ[i];
                if (p?.ThingID != null && selectedForRemovalThingIds.Contains(p.ThingID))
                    list.Add(p);
            }
            return list;
        }

        private bool SelectedIncludesStoredTransport(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || selectedForRemovalThingIds.Count == 0) return false;
            var stored = outpost.StoredAnimalsAndVehicles;
            if (stored != null)
            {
                for (int i = 0; i < stored.Count; i++)
                {
                    Pawn p = stored[i];
                    if (p?.ThingID != null && selectedForRemovalThingIds.Contains(p.ThingID))
                        return true;
                }
            }
            var shuttles = outpost.StoredPassengerShuttles;
            if (shuttles != null)
            {
                for (int i = 0; i < shuttles.Count; i++)
                {
                    Thing t = shuttles[i];
                    if (t?.ThingID != null && selectedForRemovalThingIds.Contains(t.ThingID))
                        return true;
                }
            }
            return false;
        }

        private bool SelectedIncludesMechanoids(WorldObject_WD_Outpost outpost)
        {
            var mechs = outpost?.StoredMechanoids;
            if (mechs == null || selectedForRemovalThingIds.Count == 0) return false;
            for (int i = 0; i < mechs.Count; i++)
            {
                Pawn p = mechs[i];
                if (p?.ThingID != null && selectedForRemovalThingIds.Contains(p.ThingID))
                    return true;
            }
            return false;
        }

        private static void StartRapidResponseDropPodTargeting(WorldObject_WD_Outpost outpost, List<Pawn> selectedPawns)
        {
            if (outpost == null || selectedPawns == null || selectedPawns.Count == 0) return;
            if (outpost.ManualDefenseActive)
            {
                Messages.Message("TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            CameraJumper.TryJump(outpost.Tile);
            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    if (!IsValidRapidResponseDropPodDestination(outpost, target)) return false;
                    WorldObject wo = target.WorldObject;
                    int destTile = wo != null && !wo.Destroyed ? wo.Tile.tileId : target.Tile.tileId;
                    Action launch = () =>
                    {
                        if (IsValidRapidResponseDropPodWorldObjectTarget(outpost, wo))
                            DispatchSelectedPawnsToTarget(outpost, selectedPawns, wo);
                        else
                            DispatchSelectedPawnsToTile(outpost, selectedPawns, destTile);
                    };

                    if (TryConfirmDropPodDespiteHostileAa(outpost.Tile.tileId, destTile, launch))
                        return true;

                    launch();
                    return true;
                },
                true, null, false,
                () =>
                {
                    WD_RadiusOverlayMode.DrawOrFill(
                        outpost,
                        RapidResponseUtility.GetDropPodRangeTiles(),
                        OutpostCoverageFillKind.Purple,
                        WorldOverlayLineMaterials.RecruitTradingRadiusRing);
                },
                null,
                target => IsValidRapidResponseDropPodDestination(outpost, target));
        }

        /// <summary>
        /// If hostile T4 flak threatens this flight, show a confirmation and run <paramref name="onConfirm"/> only if accepted.
        /// Returns true when a dialog was shown (caller should not launch immediately).
        /// </summary>
        private static bool TryConfirmDropPodDespiteHostileAa(int originTile, int destTile, Action onConfirm)
        {
            var threats = new List<Settlement>();
            if (!AntiAirFireUtils.TryGetHostileSettlementAaThreatsForDropPodFlight(originTile, destTile, threats)
                || threats.Count == 0)
                return false;

            string names = threats[0].LabelCap;
            for (int i = 1; i < threats.Count; i++)
                names += ", " + threats[i].LabelCap;

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "TSA_WD_RapidResponse_DropPodsAaWarning".Translate(names),
                onConfirm,
                destructive: true));
            return true;
        }

        private static bool IsValidRapidResponseDropPodDestination(WorldObject_WD_Outpost source, GlobalTargetInfo target)
        {
            if (source == null || source.Destroyed || !target.IsValid || target.Tile < 0) return false;
            if (!WithinRapidResponseDropPodRange(source, target.Tile.tileId)) return false;

            if (target.HasWorldObject && IsValidRapidResponseDropPodWorldObjectTarget(source, target.WorldObject))
                return true;

            return IsPassableRapidResponseDropPodTile(source, target.Tile.tileId);
        }

        /// <summary>Special arrival targets: hostile traveler/caravan clash, player colony map, player outpost, clash maps.</summary>
        private static bool IsValidRapidResponseDropPodWorldObjectTarget(WorldObject_WD_Outpost source, WorldObject wo)
        {
            if (source == null || source.Destroyed || wo == null || wo.Destroyed) return false;
            if (!WithinRapidResponseDropPodRange(source, wo.Tile.tileId)) return false;

            if (wo is WorldObject_Traveler traveler)
                return traveler.Faction != null
                    && traveler.mission != TravelerMission.MortarStrike
                    && traveler.mission != TravelerMission.AntiAirStrike
                    && traveler.mission != TravelerMission.RapidResponseIntercept
                    && traveler.mission != TravelerMission.RapidResponseDropPod
                    && WorldActions_Utils.SafeHostileTo(traveler.Faction, Faction.OfPlayer);

            if (wo is Caravan caravan)
                return caravan.Faction != null
                    && WorldActions_Utils.SafeHostileTo(caravan.Faction, Faction.OfPlayer)
                    && RapidResponseUtility.MapAtTile(caravan.Tile) != null;

            if (wo is Settlement settlement)
                return settlement.Faction == Faction.OfPlayer && settlement.HasMap;

            if (wo is WorldObject_WD_Outpost outpost)
                return outpost != source && outpost.Faction == Faction.OfPlayer;

            if (wo is MapParent mapParent && mapParent.HasMap)
                return RapidResponseUtility.IsCaravanClashMap(mapParent.Map);

            return false;
        }

        private static bool IsPassableRapidResponseDropPodTile(WorldObject_WD_Outpost source, int tileId)
        {
            if (source == null || tileId < 0 || !Find.WorldGrid.InBounds(tileId)) return false;
            if (tileId == source.Tile.tileId) return false;

            PlanetTile pTile = PlanetSurfaceWorldActions.PlanetTileForWdTravel(tileId, source);
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(pTile)) return false;
            if (Find.World.Impassable(pTile)) return false;
            if (Find.WorldGrid[tileId].WaterCovered) return false;
            return true;
        }

        private static bool WithinRapidResponseDropPodRange(WorldObject_WD_Outpost source, int tileId)
        {
            if (source == null || tileId < 0) return false;
            float range = RapidResponseUtility.GetDropPodRangeTiles();
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            float dist = manager != null
                ? WorldActions_Utils.GetDistance(source.Tile, tileId, manager)
                : Find.WorldGrid.ApproxDistanceInTiles(source.Tile, tileId);
            return dist <= range;
        }

        private static List<Pawn> RemoveSelectedPawnsForDropPod(WorldObject_WD_Outpost outpost, List<Pawn> selectedPawns)
        {
            var removed = new List<Pawn>(selectedPawns.Count);
            for (int i = 0; i < selectedPawns.Count; i++)
            {
                Pawn p = selectedPawns[i];
                if (p == null || !outpost.Occupants.Contains(p)) continue;
                Pawn r = outpost.RemovePawn(p);
                if (r != null && !r.Destroyed && !r.Dead)
                    removed.Add(r);
            }
            return removed;
        }

        private static void RestorePawnsToOutpost(WorldObject_WD_Outpost outpost, List<Pawn> removed)
        {
            if (outpost == null || removed == null) return;
            for (int i = 0; i < removed.Count; i++)
            {
                Pawn p = removed[i];
                if (p != null && !p.Destroyed && !p.Dead)
                    outpost.AddPawn(p, null!);
            }
        }

        private static void DispatchSelectedPawnsToTarget(WorldObject_WD_Outpost outpost, List<Pawn> selectedPawns, WorldObject target)
        {
            var removed = RemoveSelectedPawnsForDropPod(outpost, selectedPawns);
            if (removed.Count == 0) return;

            var traveler = WorldActions_Traveler.SpawnRapidResponseDropPodTraveler(outpost, target, removed);
            if (traveler == null)
            {
                RestorePawnsToOutpost(outpost, removed);
                Messages.Message("TSA_WD_RapidResponse_DropPodsAborted".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_RapidResponseDropPodsLaunched".Translate(outpost.LabelCap, target.LabelCap, removed.Count.ToString()),
                outpost,
                target));
            Messages.Message(
                "TSA_WD_RapidResponse_DropPodsLaunched".Translate(removed.Count.ToString(), target.LabelCap),
                MessageTypeDefOf.TaskCompletion,
                false);
        }

        private static void DispatchSelectedPawnsToTile(WorldObject_WD_Outpost outpost, List<Pawn> selectedPawns, int tileId)
        {
            var removed = RemoveSelectedPawnsForDropPod(outpost, selectedPawns);
            if (removed.Count == 0) return;

            var traveler = WorldActions_Traveler.SpawnRapidResponseDropPodTraveler(outpost, tileId, removed);
            if (traveler == null)
            {
                RestorePawnsToOutpost(outpost, removed);
                Messages.Message("TSA_WD_RapidResponse_DropPodsAborted".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            string destLabel = "#" + tileId;
            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_RapidResponseDropPodsLaunched".Translate(outpost.LabelCap, destLabel, removed.Count.ToString()),
                outpost,
                null));
            Messages.Message(
                "TSA_WD_RapidResponse_DropPodsLaunched".Translate(removed.Count.ToString(), destLabel),
                MessageTypeDefOf.TaskCompletion,
                false);
        }

        private void DoTableHeader(ref float curY)
        {
            float x = bodyXForHeader;
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;

            if (ColOn(PawnRosterColumnIds.Portrait))
                x += ColPortrait;
            if (ColOn(PawnRosterColumnIds.Type))
            {
                PawnRosterHeaderFilter.DrawFilterableHeader(
                    ref x, curY, ColPawnType, HeaderHeight,
                    "",
                    !useDefaultGrouping && sortColumn == OutpostPawnTableSortColumn.PawnType,
                    sortAscending,
                    TextAnchor.MiddleCenter,
                    pawnTypeFilter != PlayerPawnTypeFilter.All,
                    "TSA_WD_FilterByType".Translate(),
                    icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                        icon,
                        "TSA_WD_FilterByType".Translate(),
                        PawnRosterHeaderFilter.TypeChoices(pawnTypeFilter, f =>
                        {
                            pawnTypeFilter = f;
                            lastCacheTick = -1;
                        }, TypePopulationForFilterDialog())),
                    () => ToggleSort(OutpostPawnTableSortColumn.PawnType));
            }
            if (ColOn(PawnRosterColumnIds.Name))
            {
                PawnRosterHeaderFilter.DrawFilterableHeader(
                    ref x, curY, colNameWidth, HeaderHeight,
                    hdrName,
                    !useDefaultGrouping && sortColumn == OutpostPawnTableSortColumn.Name,
                    sortAscending,
                    TextAnchor.MiddleLeft,
                    !pawnSearchTerm.NullOrEmpty(),
                    "TSA_WD_AllPlayerPawns_SearchName".Translate(),
                    icon => PawnRosterHeaderFilter.OpenTextDropdown(
                        icon,
                        "TSA_WD_FilterByPawnName".Translate(),
                        "TSA_WD_AllPlayerPawns_SearchName".Translate(),
                        () => pawnSearchTerm,
                        v => { pawnSearchTerm = v; lastCacheTick = -1; },
                        () => { pawnSearchTerm = ""; lastCacheTick = -1; }),
                    () => ToggleSort(OutpostPawnTableSortColumn.Name));
            }
            if (ColOn(PawnRosterColumnIds.Star))
            {
                PawnRosterHeaderFilter.DrawFilterableHeader(
                    ref x, curY, ColStar, HeaderHeight,
                    "",
                    !useDefaultGrouping && sortColumn == OutpostPawnTableSortColumn.Starred,
                    sortAscending,
                    TextAnchor.MiddleCenter,
                    starFilter != OutpostPawnStarFilter.All,
                    hdrStarTip,
                    icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                        icon,
                        "TSA_WD_FilterByStar".Translate(),
                        PawnRosterHeaderFilter.OutpostStarChoices(starFilter, f =>
                        {
                            starFilter = f;
                            lastCacheTick = -1;
                        }, StarPopulationForFilterDialog())),
                    () => ToggleSort(OutpostPawnTableSortColumn.Starred));
            }
            if (ColOn(PawnRosterColumnIds.Select))
                DrawSelectHeader(ref x, curY);
            if (ColOn(PawnRosterColumnIds.Reorder))
                DrawHeaderLabel(ref x, curY, ColReorder, "", true);
            if (ColOn(PawnRosterColumnIds.Resistance))
                DrawSortableHeader(ref x, curY, ColResistance, hdrResistance, OutpostPawnTableSortColumn.Resistance, true);
            if (ColOn(PawnRosterColumnIds.Traits))
            {
                PawnRosterTraitFilter.DrawTraitsHeader(
                    ref x, curY, ColTraits, HeaderHeight,
                    hdrTraits,
                    !useDefaultGrouping && sortColumn == OutpostPawnTableSortColumn.Traits,
                    sortAscending,
                    TextAnchor.MiddleCenter,
                    () => ToggleSort(OutpostPawnTableSortColumn.Traits));
            }
            if (ColOn(PawnRosterColumnIds.Xenotype))
            {
                PawnRosterHeaderFilter.DrawFilterableHeader(
                    ref x, curY, ColXenotype, HeaderHeight,
                    "TSA_WD_PawnRoster_ColXenotype".Translate(),
                    !useDefaultGrouping && sortColumn == OutpostPawnTableSortColumn.Xenotype,
                    sortAscending,
                    TextAnchor.MiddleCenter,
                    !xenotypeFilter.NullOrEmpty(),
                    "TSA_WD_FilterByXenotype".Translate(),
                    icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                        icon,
                        "TSA_WD_FilterByXenotype".Translate(),
                        PawnRosterHeaderFilter.XenotypeChoices(xenotypeFilter, v =>
                        {
                            xenotypeFilter = v ?? "";
                            lastCacheTick = -1;
                        }, XenotypePopulationForFilterDialog())),
                    () => ToggleSort(OutpostPawnTableSortColumn.Xenotype));
            }
            if (ColOn(PawnRosterColumnIds.Psycasts))
            {
                PawnRosterHeaderFilter.DrawFilterableHeader(
                    ref x, curY, ColPsycasts, HeaderHeight,
                    "TSA_WD_PawnRoster_ColPsycasts".Translate(),
                    !useDefaultGrouping && sortColumn == OutpostPawnTableSortColumn.Psycasts,
                    sortAscending,
                    TextAnchor.MiddleCenter,
                    !psycastFilter.NullOrEmpty(),
                    "TSA_WD_FilterByPsycast".Translate(),
                    icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                        icon,
                        "TSA_WD_FilterByPsycast".Translate(),
                        PawnRosterHeaderFilter.PsycastChoices(psycastFilter, v =>
                        {
                            psycastFilter = v ?? "";
                            lastCacheTick = -1;
                        }, PsycastPopulationForFilterDialog())),
                    () => ToggleSort(OutpostPawnTableSortColumn.Psycasts));
            }
            if (ColOn(PawnRosterColumnIds.Age))
                DrawSortableHeader(ref x, curY, ColAge, hdrAge, OutpostPawnTableSortColumn.Age, true);
            if (ColOn(PawnRosterColumnIds.Shooting))
                DrawSortableHeader(ref x, curY, ColSkill, SkillDefOf.Shooting.LabelCap, OutpostPawnTableSortColumn.Shooting, true);
            if (ColOn(PawnRosterColumnIds.Melee))
                DrawSortableHeader(ref x, curY, ColSkill, hdrMelee, OutpostPawnTableSortColumn.Melee, true);
            if (ColOn(PawnRosterColumnIds.Strength))
                DrawSortableHeader(ref x, curY, ColStrength, hdrStrength, OutpostPawnTableSortColumn.Strength, true);
            if (cachedRelevantDefs != null && cachedRelevantDefs.Count > 0 && ColOn(PawnRosterColumnIds.Relevant))
            {
                DrawSortableHeader(ref x, curY, ColSkill, (cachedRelHeader ?? "").Truncate(ColSkill), OutpostPawnTableSortColumn.RelevantCombined, true);
                DrawRelevantXpHeader(ref x, curY);
            }
            if (ColOn(PawnRosterColumnIds.Construction))
                DrawSortableHeader(ref x, curY, ColConstruction, SkillDefOf.Construction.LabelCap.Truncate(ColConstruction), OutpostPawnTableSortColumn.Construction, true, hdrConstructionRoadTip);
            if (ColOn(PawnRosterColumnIds.DailyFood))
                DrawSortableMultilineHeader(ref x, curY, ColDailyFood, hdrDailyFood, OutpostPawnTableSortColumn.DailyFood);
            if (ColOn(PawnRosterColumnIds.Hurt))
                DrawSortableHeader(ref x, curY, ColHurt, hdrHurt, OutpostPawnTableSortColumn.Hurt, true, hdrHurtTip);

            if (PlayerPawnRosterUtility.AnyFullSkillColumnVisible(ColWindow))
            {
                PlayerPawnRosterUtility.DrawSkillBlockSeparator(ref x, curY, HeaderHeight);
                SkillDef[] skills = PlayerPawnRosterUtility.AllSkillColumns;
                for (int i = 0; i < skills.Length; i++)
                {
                    if (skills[i] == SkillDefOf.Shooting || skills[i] == SkillDefOf.Melee)
                        continue;
                    if (!ColOn(PawnRosterColumnIds.FullSkill(skills[i]))) continue;
                    OutpostPawnTableSortColumn skillSort = VirtualPawnSummary.SortColumnForSkillDef(skills[i]);
                    DrawSortableHeader(ref x, curY, ColSkill, skills[i].LabelCap, skillSort, true);
                }
            }

            GUI.color = Color.white;
            curY += HeaderHeight;
        }

        private void DrawSelectHeader(ref float x, float curY)
        {
            Rect selHdr = new Rect(x, curY, ColSelect, HeaderHeight);
            if (Mouse.IsOver(selHdr)) Widgets.DrawHighlight(selHdr);

            bool allSelected = AreAllVisibleOccupantsSelectedForRemoval();
            float box = 18f;
            float cx = selHdr.x + (ColSelect - box) * 0.5f;
            float cy = selHdr.y + (HeaderHeight - box) * 0.5f;
            int selectableCount = CountSelectableVisibleOccupantRows();
            Widgets.CheckboxDraw(cx, cy, allSelected, selectableCount == 0, box);

            TooltipHandler.TipRegion(selHdr, hdrSelectColumnTip);
            if (selectableCount > 0 && Widgets.ButtonInvisible(selHdr))
            {
                ToggleSelectAllVisibleOccupantsForRemoval();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            x += ColSelect;
        }

        private float GetSelectColumnX()
        {
            float x = 0f;
            if (ColOn(PawnRosterColumnIds.Portrait)) x += ColPortrait;
            if (ColOn(PawnRosterColumnIds.Type)) x += ColPawnType;
            if (ColOn(PawnRosterColumnIds.Name)) x += colNameWidth;
            if (ColOn(PawnRosterColumnIds.Star)) x += ColStar;
            return x;
        }

        private float GetReorderColumnX() =>
            GetSelectColumnX() + (ColOn(PawnRosterColumnIds.Select) ? ColSelect : 0f);

        private void DrawPrisonerGroupSelectHeader(float curY)
        {
            float selectX = GetSelectColumnX();
            Rect selHdr = new Rect(selectX, curY, ColSelect, GroupHeaderHeight);
            if (Mouse.IsOver(selHdr)) Widgets.DrawHighlight(selHdr);

            bool allSelected = AreAllVisiblePrisonersSelected();
            float box = 18f;
            float cx = selHdr.x + (ColSelect - box) * 0.5f;
            float cy = selHdr.y + (GroupHeaderHeight - box) * 0.5f;
            int prisonerCount = CountVisiblePrisonerRows();
            Widgets.CheckboxDraw(cx, cy, allSelected, prisonerCount == 0, box);

            TooltipHandler.TipRegion(selHdr, "TSA_WD_Prisoners_SelectColumnTip".Translate());
            if (prisonerCount > 0 && Widgets.ButtonInvisible(selHdr))
            {
                ToggleSelectAllVisiblePrisoners();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }

        private static string GetRowSelectThingId(CachedPawnRow row)
        {
            if (row == null || row.isGroupHeader) return null;
            return row.rowKind == OutpostPawnRowKind.Shuttle
                ? row.shuttle?.ThingID
                : row.pawn?.ThingID;
        }

        private bool CanInteractSelectRow(CachedPawnRow row, string tid)
        {
            if (row == null || tid.NullOrEmpty()) return false;
            if (row.isGroupHeader) return false;

            // Prisoners and transferable pawns are mutually exclusive selection modes.
            bool prisonerMode = IsPrisonerSelectionMode();

            if (row.rowKind == OutpostPawnRowKind.Prisoner)
            {
                // Always allow selecting prisoners (starts/switches to prisoner mode).
                // Already-selected prisoners stay clickable to deselect.
                return true;
            }

            if (prisonerMode)
            {
                // Switching away from prisoners: only an occupant click starts transfer mode.
                if (row.rowKind != OutpostPawnRowKind.Occupant) return false;
                return OutpostPawnIdeologyUtil.BulkRemovalSelectionIsAllowedWithExtra(
                    SelOutpost,
                    new HashSet<string>(),
                    row.pawn);
            }

            bool sel = selectedForRemovalThingIds.Contains(tid);
            if (row.rowKind == OutpostPawnRowKind.Occupant)
            {
                return OutpostPawnIdeologyUtil.BulkRemovalSelectionIsAllowedWithExtra(
                    SelOutpost,
                    selectedForRemovalThingIds,
                    row.pawn);
            }
            return sel || SelectedRemovalIncludesOccupant();
        }

        private bool IsPrisonerSelectionMode() =>
            HasPrisonerSelection() && !HasTransferableSelection();

        private int CountSelectableVisibleOccupantRows()
        {
            if (cachedRows == null || cachedRows.Count == 0) return 0;
            int n = 0;
            for (int i = 0; i < cachedRows.Count; i++)
            {
                CachedPawnRow row = cachedRows[i];
                if (row.rowKind == OutpostPawnRowKind.Prisoner) continue;
                string tid = GetRowSelectThingId(row);
                if (!tid.NullOrEmpty())
                    n++;
            }
            return n;
        }

        private int CountVisiblePrisonerRows()
        {
            if (cachedRows == null || cachedRows.Count == 0) return 0;
            int n = 0;
            for (int i = 0; i < cachedRows.Count; i++)
            {
                if (cachedRows[i].rowKind == OutpostPawnRowKind.Prisoner)
                    n++;
            }
            return n;
        }

        private bool AreAllVisibleOccupantsSelectedForRemoval()
        {
            if (cachedRows == null || cachedRows.Count == 0) return false;
            bool any = false;
            for (int i = 0; i < cachedRows.Count; i++)
            {
                CachedPawnRow row = cachedRows[i];
                if (row.rowKind == OutpostPawnRowKind.Prisoner) continue;
                string tid = GetRowSelectThingId(row);
                if (tid.NullOrEmpty()) continue;
                any = true;
                if (!selectedForRemovalThingIds.Contains(tid))
                    return false;
            }
            return any;
        }

        private bool AreAllVisiblePrisonersSelected()
        {
            if (cachedRows == null || cachedRows.Count == 0) return false;
            bool any = false;
            for (int i = 0; i < cachedRows.Count; i++)
            {
                CachedPawnRow row = cachedRows[i];
                if (row.rowKind != OutpostPawnRowKind.Prisoner) continue;
                string tid = GetRowSelectThingId(row);
                if (tid.NullOrEmpty()) continue;
                any = true;
                if (!selectedForRemovalThingIds.Contains(tid))
                    return false;
            }
            return any;
        }

        private void ToggleSelectAllVisibleOccupantsForRemoval()
        {
            if (cachedRows == null || cachedRows.Count == 0) return;
            if (AreAllVisibleOccupantsSelectedForRemoval())
            {
                for (int i = 0; i < cachedRows.Count; i++)
                {
                    CachedPawnRow row = cachedRows[i];
                    if (row.rowKind == OutpostPawnRowKind.Prisoner) continue;
                    string tid = GetRowSelectThingId(row);
                    if (!tid.NullOrEmpty())
                        selectedForRemovalThingIds.Remove(tid);
                }
                return;
            }

            ClearPrisonerSelection();
            // Occupants first so vehicles/animals/shuttles can unlock after an occupant is selected.
            for (int i = 0; i < cachedRows.Count; i++)
            {
                CachedPawnRow row = cachedRows[i];
                if (row.rowKind != OutpostPawnRowKind.Occupant) continue;
                string tid = GetRowSelectThingId(row);
                if (tid.NullOrEmpty()) continue;
                if (CanInteractSelectRow(row, tid))
                    selectedForRemovalThingIds.Add(tid);
            }
            for (int i = 0; i < cachedRows.Count; i++)
            {
                CachedPawnRow row = cachedRows[i];
                if (row.rowKind == OutpostPawnRowKind.Occupant || row.rowKind == OutpostPawnRowKind.Prisoner) continue;
                string tid = GetRowSelectThingId(row);
                if (tid.NullOrEmpty()) continue;
                if (CanInteractSelectRow(row, tid))
                    selectedForRemovalThingIds.Add(tid);
            }
        }

        private void ToggleSelectAllVisiblePrisoners()
        {
            if (cachedRows == null || cachedRows.Count == 0) return;
            if (AreAllVisiblePrisonersSelected())
            {
                for (int i = 0; i < cachedRows.Count; i++)
                {
                    CachedPawnRow row = cachedRows[i];
                    if (row.rowKind != OutpostPawnRowKind.Prisoner) continue;
                    string tid = GetRowSelectThingId(row);
                    if (!tid.NullOrEmpty())
                        selectedForRemovalThingIds.Remove(tid);
                }
                return;
            }

            ClearNonPrisonerSelection();
            for (int i = 0; i < cachedRows.Count; i++)
            {
                CachedPawnRow row = cachedRows[i];
                if (row.rowKind != OutpostPawnRowKind.Prisoner) continue;
                string tid = GetRowSelectThingId(row);
                if (!tid.NullOrEmpty())
                    selectedForRemovalThingIds.Add(tid);
            }
        }

        private void ToggleSort(OutpostPawnTableSortColumn col)
        {
            useDefaultGrouping = false;
            if (sortColumn == col)
                sortAscending = !sortAscending;
            else
            {
                sortColumn = col;
                sortAscending = true;
            }
            lastCacheTick = -1;
        }

        private void DrawRelevantXpHeader(ref float x, float curY)
        {
            DrawSortableHeader(ref x, curY, ColRelevantXp, cachedXpHeaderLabel ?? "", OutpostPawnTableSortColumn.RelevantXp, true, cachedXpTooltip ?? hdrSortTip);
        }

        private void DrawSortableMultilineHeader(ref float x, float curY, float width, string label, OutpostPawnTableSortColumn column)
        {
            Rect r = new Rect(x, curY, width, HeaderHeight);
            if (Mouse.IsOver(r)) Widgets.DrawHighlight(r);
            string arrow = (!useDefaultGrouping && sortColumn == column) ? (sortAscending ? " ▲" : " ▼") : "";
            string[] lines = (label ?? "").Split('\n');
            if (lines.Length == 0) lines = new[] { "" };
            if (lines.Length == 1) lines = new[] { lines[0], "" };

            Text.Font = GameFont.Tiny;
            // Two Tiny lines need enough vertical room; HeaderHeight 36 avoids top cropping.
            float lineH = 16f;
            float blockH = lineH * 2f;
            float startY = curY + Mathf.Max(1f, (HeaderHeight - blockH) * 0.5f);
            Text.Anchor = TextAnchor.MiddleCenter;

            string line1 = lines[0];
            string line2 = lines[1] + arrow;
            if (Text.CalcSize(line2).x > width - 2f)
                line2 = lines[1].Truncate(Mathf.Max(8f, width - 18f)) + arrow;

            Widgets.Label(new Rect(x, startY, width, lineH), line1);
            Widgets.Label(new Rect(x, startY + lineH, width, lineH), line2);
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(r, hdrSortTip);
            if (Widgets.ButtonInvisible(r))
                ToggleSort(column);
            x += width;
        }

        private void DrawSortableHeader(ref float x, float curY, float width, string label, OutpostPawnTableSortColumn column, bool centered, string tipOverride = null!)
        {
            Rect r = new Rect(x, curY, width, HeaderHeight);
            if (Mouse.IsOver(r)) Widgets.DrawHighlight(r);
            string arrow = (!useDefaultGrouping && sortColumn == column) ? (sortAscending ? " ▲" : " ▼") : "";
            string fullText = (label ?? "") + arrow;
            if (Text.CalcSize(fullText).x > width - 2f)
                fullText = (label ?? "").Truncate(width - 18f) + arrow;
            Text.Anchor = centered ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            Widgets.Label(r, fullText);
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(r, tipOverride ?? hdrSortTip);
            if (Widgets.ButtonInvisible(r))
                ToggleSort(column);
            x += width;
        }

        private static void DrawHeaderLabel(ref float x, float curY, float width, string label, bool centered)
        {
            Rect r = new Rect(x, curY, width, HeaderHeight);
            Text.Anchor = centered ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            Widgets.Label(r, (label ?? "").Truncate(width - 2f));
            Text.Anchor = TextAnchor.UpperLeft;
            x += width;
        }

        private void DoPawnRow(ref float curY, Rect viewRect, CachedPawnRow row, bool zebra)
        {
            if (row != null && row.isGroupHeader)
            {
                float visibleY = scrollPosition.y - GroupHeaderHeight;
                float visibleYMax = scrollPosition.y + lastScrollViewportHeight;
                if (curY >= visibleY && curY < visibleYMax)
                {
                    GUI.color = Color.white;
                    Widgets.DrawLineHorizontal(0f, curY, viewRect.width);
                    Widgets.DrawLineHorizontal(0f, curY + GroupHeaderHeight - 1f, viewRect.width);
                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.MiddleLeft;
                    GUI.color = Color.yellow;

                    float titleLeft = 8f;
                    float titleRight = GetReorderColumnX() - 4f;
                    float titleW = Mathf.Max(80f, titleRight - titleLeft);
                    Rect headerRect = new Rect(titleLeft, curY, titleW, GroupHeaderHeight);
                    string headerText = row.groupHeaderLabel
                        ?? "TSA_WD_Prisoners_GroupOutpostHeader".Translate(
                            OutpostPrisonerResistanceScaling.GetConcurrentRecruitSlots(SelOutpost).ToString());
                    Widgets.Label(headerRect, headerText.Truncate(titleW - 2f));
                    TooltipHandler.TipRegion(headerRect, "TSA_WD_Prisoners_GroupOutpostTip".Translate());

                    GUI.color = Color.white;
                    Text.Anchor = TextAnchor.UpperLeft;
                    Text.Font = GameFont.Tiny;
                    DrawPrisonerGroupSelectHeader(curY);
                }
                curY += GroupHeaderHeight;
                return;
            }

            try
            {
                float rowH = GetRowHeight(row);
                float visibleY = scrollPosition.y - rowH;
                float visibleYMax = scrollPosition.y + lastScrollViewportHeight;
                bool visible = curY >= visibleY && curY < visibleYMax;
                if (row == null || SelOutpost == null || (row.pawn == null && row.shuttle == null))
                {
                    curY += rowH;
                    return;
                }

                if (visible)
                {
                    Rect rowRect = new Rect(0f, curY, viewRect.width, rowH);
                    if (zebra) Widgets.DrawHighlight(rowRect);
                    if (Mouse.IsOver(rowRect)) Widgets.DrawLightHighlight(rowRect);
                    if (row.isSlave && row.pawn != null)
                    {
                        Color nameTint = PawnNameColorUtility.PawnNameColorOf(row.pawn);
                        Color rowBg = new Color(
                            Mathf.Clamp01(nameTint.r * 0.28f + 0.08f),
                            Mathf.Clamp01(nameTint.g * 0.28f + 0.06f),
                            Mathf.Clamp01(nameTint.b * 0.12f + 0.02f),
                            0.21f);
                        Widgets.DrawBoxSolid(rowRect, rowBg);
                    }
                    else if (row.isBeingRecruited)
                    {
                        Widgets.DrawBoxSolid(rowRect, RecruitingRowTint);
                    }

                    Color prevGui = GUI.color;
                    if (row.isSlave && row.pawn != null)
                        GUI.color = PawnNameColorUtility.PawnNameColorOf(row.pawn);

                    Text.Font = GameFont.Tiny;
                    float x = 0f;
                    if (ColOn(PawnRosterColumnIds.Portrait))
                    {
                        Rect cell = new Rect(x, curY, ColPortrait, rowH);
                        Texture portrait = GetRowPortrait(row);
                        Rect portraitRect = new Rect(cell.x + (cell.width - PortraitSize.x) / 2f, curY + (rowH - PortraitSize.y) / 2f, PortraitSize.x, PortraitSize.y);
                        if (portrait != null)
                            GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit);
                        else
                            Widgets.DrawBoxSolid(portraitRect, new Color(0.3f, 0.3f, 0.35f, 1f));

                        if (Widgets.ButtonInvisible(cell))
                            OpenRowInfoCard(row);
                        x += ColPortrait;
                    }

                    if (ColOn(PawnRosterColumnIds.Type))
                    {
                        Text.Anchor = TextAnchor.MiddleCenter;
                        Widgets.Label(new Rect(x, curY, ColPawnType, rowH), (row.typeLabel ?? "—").Truncate(ColPawnType - 4f));
                        x += ColPawnType;
                    }

                    if (ColOn(PawnRosterColumnIds.Name))
                    {
                        Rect cell = new Rect(x, curY, colNameWidth, rowH);
                        Text.Anchor = TextAnchor.MiddleLeft;
                        Widgets.Label(cell, (row.nameLabel ?? "—").Truncate(colNameWidth - 4f));
                        Text.Anchor = TextAnchor.UpperLeft;
                        if (Widgets.ButtonInvisible(cell))
                            OpenRowInfoCard(row);
                        x += colNameWidth;
                    }

                    if (ColOn(PawnRosterColumnIds.Star))
                        DrawRowStarCell(ref x, curY, row, rowH);
                    if (ColOn(PawnRosterColumnIds.Select))
                        DrawRowSelectCheckbox(ref x, curY, row, rowH);
                    if (ColOn(PawnRosterColumnIds.Reorder))
                        DrawPrisonerQueueButtons(ref x, curY, row, rowH);

                    Text.Anchor = TextAnchor.MiddleCenter;
                    Text.Font = GameFont.Tiny;
                    if (ColOn(PawnRosterColumnIds.Resistance))
                    {
                        Rect cell = new Rect(x, curY, ColResistance, rowH);
                        Widgets.Label(cell, (row.resistanceLabel ?? "—").Truncate(ColResistance - 2f));
                        if (!string.IsNullOrEmpty(row.resistanceTip))
                            TooltipHandler.TipRegion(cell, row.resistanceTip);
                        Text.Font = GameFont.Tiny;
                        x += ColResistance;
                    }

                    if (ColOn(PawnRosterColumnIds.Traits))
                    {
                        Rect traitsRect = new Rect(x + 2f, curY + 2f, ColTraits - 4f, rowH - 4f);
                        PrisonerRosterUtility.DrawTraitsCell(traitsRect, row.traitsDisplay, row.traitsTip);
                        Text.Font = GameFont.Tiny;
                        x += ColTraits;
                    }

                    if (ColOn(PawnRosterColumnIds.Xenotype))
                    {
                        Rect cell = new Rect(x + 2f, curY + 2f, ColXenotype - 4f, rowH - 4f);
                        PrisonerRosterUtility.DrawTraitsCell(cell, row.xenotypeDisplay, row.xenotypeTip);
                        Text.Font = GameFont.Tiny;
                        x += ColXenotype;
                    }

                    if (ColOn(PawnRosterColumnIds.Psycasts))
                    {
                        Rect cell = new Rect(x + 2f, curY + 2f, ColPsycasts - 4f, rowH - 4f);
                        PrisonerRosterUtility.DrawTraitsCell(cell, row.psycastsDisplay, row.psycastsTip);
                        Text.Font = GameFont.Tiny;
                        x += ColPsycasts;
                    }

                    if (ColOn(PawnRosterColumnIds.Age))
                    {
                        Text.Anchor = TextAnchor.MiddleCenter;
                        Widgets.Label(new Rect(x, curY, ColAge, rowH), (row.ageLabel ?? "—").Truncate(ColAge - 2f));
                        x += ColAge;
                    }

                    if (ColOn(PawnRosterColumnIds.Shooting))
                    {
                        DrawOutpostSkillCell(ref x, curY, rowH, row, SkillDefOf.Shooting, row.shootingLabel);
                    }
                    if (ColOn(PawnRosterColumnIds.Melee))
                    {
                        DrawOutpostSkillCell(ref x, curY, rowH, row, SkillDefOf.Melee, row.meleeLabel);
                    }

                    if (ColOn(PawnRosterColumnIds.Strength))
                    {
                        Widgets.Label(new Rect(x, curY, ColStrength, rowH), row.strengthLabel ?? "—");
                        x += ColStrength;
                    }

                    if (cachedRelevantDefs != null && cachedRelevantDefs.Count > 0 && SelOutpost?.def != null
                        && ColOn(PawnRosterColumnIds.Relevant))
                    {
                        Widgets.Label(new Rect(x, curY, ColSkill, rowH), row.relevantSkillLabel ?? "—");
                        x += ColSkill;
                        Rect cell = new Rect(x, curY, ColRelevantXp, rowH);
                        GameFont prev = Text.Font;
                        if (cachedRelevantDefs.Count > 1) Text.Font = GameFont.Tiny;
                        Widgets.Label(cell, (row.xpProgressLabel ?? "").Truncate(ColRelevantXp));
                        Text.Font = prev;
                        x += ColRelevantXp;
                    }

                    if (ColOn(PawnRosterColumnIds.Construction))
                    {
                        Widgets.Label(new Rect(x, curY, ColConstruction, rowH), row.constructionLabel ?? "—");
                        x += ColConstruction;
                    }

                    if (ColOn(PawnRosterColumnIds.DailyFood))
                    {
                        Widgets.Label(new Rect(x, curY, ColDailyFood, rowH), row.dailyFoodLabel ?? "—");
                        x += ColDailyFood;
                    }
                    if (ColOn(PawnRosterColumnIds.Hurt))
                        DrawHurtCell(ref x, curY, row.needsHealing, rowH);

                    if (PlayerPawnRosterUtility.AnyFullSkillColumnVisible(ColWindow))
                    {
                        PlayerPawnRosterUtility.DrawSkillBlockSeparator(ref x, curY, rowH);
                        SkillDef[] skills = PlayerPawnRosterUtility.AllSkillColumns;
                        int best = 0;
                        if (row.summary != null && !row.sparseSkills)
                        {
                            for (int si = 0; si < skills.Length; si++)
                                best = Mathf.Max(best, Mathf.RoundToInt(row.summary.GetSkill(skills[si])));
                        }
                        for (int si = 0; si < skills.Length; si++)
                        {
                            if (skills[si] == SkillDefOf.Shooting || skills[si] == SkillDefOf.Melee)
                                continue;
                            if (!ColOn(PawnRosterColumnIds.FullSkill(skills[si]))) continue;
                            int level = (row.summary != null && !row.sparseSkills)
                                ? Mathf.RoundToInt(row.summary.GetSkill(skills[si]))
                                : 0;
                            string fallback = row.sparseSkills || row.summary == null ? "—" : level.ToString();
                            DrawOutpostSkillCell(ref x, curY, rowH, row, skills[si], fallback, best);
                        }
                    }
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = prevGui;

                    if (row.isSlave)
                    {
                        Rect slaveRowTipRect = new Rect(0f, curY, x, rowH);
                        TooltipHandler.TipRegion(slaveRowTipRect, "TSA_WD_PawnRow_SlaveRowTip".Translate());
                    }
                }
            }
            finally
            {
                Text.Anchor = TextAnchor.UpperLeft;
            }

            curY += GetRowHeight(row);
        }

        private void DrawOutpostSkillCell(ref float x, float curY, float rowH, CachedPawnRow row, SkillDef skill, string fallbackLabel, int bestLevel = -1)
        {
            Rect cell = new Rect(x, curY, ColSkill, rowH);
            if (row.pawn != null && !row.sparseSkills && skill != null)
            {
                int level = row.summary != null
                    ? Mathf.RoundToInt(row.summary.GetSkill(skill))
                    : 0;
                if (bestLevel < 0)
                {
                    bestLevel = 0;
                    if (row.summary != null)
                    {
                        SkillDef[] skills = PlayerPawnRosterUtility.AllSkillColumns;
                        for (int i = 0; i < skills.Length; i++)
                            bestLevel = Mathf.Max(bestLevel, Mathf.RoundToInt(row.summary.GetSkill(skills[i])));
                    }
                }
                bool isBest = bestLevel > 0 && level == bestLevel;
                PlayerPawnRosterUtility.DrawSkillLevelWithPassion(cell, row.pawn, skill, level, isBest, ColWindow);
            }
            else
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(cell, fallbackLabel ?? "—");
            }
            x += ColSkill;
        }

        private Texture GetRowPortrait(CachedPawnRow row)
        {
            if (row.rowKind == OutpostPawnRowKind.Shuttle)
                return row.shuttle?.def?.uiIcon;
            if ((row.rowKind == OutpostPawnRowKind.Occupant || row.rowKind == OutpostPawnRowKind.Prisoner)
                && row.pawn != null)
            {
                Texture portrait = GetPortraitFor(row.pawn, row.portraitKey);
                if (portrait != null) return portrait;
            }
            return GetStoredTransportIcon(row.pawn);
        }

        private void OpenRowInfoCard(CachedPawnRow row)
        {
            if (row.rowKind == OutpostPawnRowKind.Shuttle)
                OpenVanillaInfoCard(row.shuttle);
            else
                OpenVanillaInfoCard(row.pawn);
        }

        private void DrawRowStarCell(ref float x, float curY, CachedPawnRow row, float rowHeight)
        {
            Rect starCell = new Rect(x, curY, ColStar, rowHeight);
            bool canStar = row.rowKind != OutpostPawnRowKind.Shuttle && row.pawn?.ThingID != null;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Medium;
            if (canStar)
            {
                GUI.color = row.isStarred ? new Color(1f, 0.85f, 0.2f) : new Color(0.55f, 0.55f, 0.55f, 0.7f);
                Widgets.Label(starCell, row.isStarred ? "★" : "☆");
            }
            else
            {
                GUI.color = new Color(0.4f, 0.4f, 0.4f, 0.35f);
                Widgets.Label(starCell, "☆");
            }
            GUI.color = Color.white;
            Text.Font = GameFont.Tiny;
            TooltipHandler.TipRegion(starCell, hdrStarTip);
            if (canStar && Widgets.ButtonInvisible(starCell))
            {
                WorldComponent_PlayerPawnFavorites.Get()?.Toggle(row.pawn.ThingID);
                row.isStarred = !row.isStarred;
                SoundDefOf.Click.PlayOneShotOnCamera();
                lastCacheTick = -1;
            }
            x += ColStar;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawRowSelectCheckbox(ref float x, float curY, CachedPawnRow row, float rowHeight)
        {
            TextAnchor prevAnchor = Text.Anchor;
            if (row.isGroupHeader)
            {
                x += ColSelect;
                Text.Anchor = prevAnchor;
                return;
            }

            string tid = row.rowKind == OutpostPawnRowKind.Shuttle
                ? row.shuttle?.ThingID
                : row.pawn?.ThingID;

            Rect selectColRect = new Rect(x, curY, ColSelect, rowHeight);
            if (tid != null)
            {
                bool wasSelected = selectedForRemovalThingIds.Contains(tid);
                bool canInteract = CanInteractSelectRow(row, tid);

                float cx = x + (ColSelect - 24f) * 0.5f;
                float cy = curY + (rowHeight - 24f) * 0.5f;

                if (!wasSelected && !canInteract && row.rowKind != OutpostPawnRowKind.Prisoner)
                    TooltipHandler.TipRegion(selectColRect, hdrSlaveRemoveBlockedTip);

                bool nowSelected = PawnRosterPaintSelect.Draw(
                    this, selectColRect, cx, cy, 24f, tid, selectedForRemovalThingIds, canInteract);

                // Enforce exclusive modes: prisoners XOR colonists/vehicles/animals/mechs/shuttles.
                if (nowSelected && !wasSelected)
                {
                    if (row.rowKind == OutpostPawnRowKind.Prisoner)
                        ClearNonPrisonerSelection();
                    else
                        ClearPrisonerSelection();
                }
            }

            // Always consume the column width so subsequent cells stay aligned with headers.
            x += ColSelect;
            Text.Anchor = prevAnchor;
        }

        private void DrawPrisonerQueueButtons(ref float x, float curY, CachedPawnRow row, float rowHeight)
        {
            Rect col = new Rect(x, curY, ColReorder, rowHeight);
            if (row != null && row.rowKind == OutpostPawnRowKind.Prisoner && row.pawn != null)
            {
                const float btn = 20f;
                const float gap = 2f;
                float gridW = btn * 2f + gap;
                float gridH = btn * 2f + gap;
                float gx = col.x + (ColReorder - gridW) * 0.5f;
                float gy = curY + (rowHeight - gridH) * 0.5f;
                bool canUp = row.prisonerIndex > 0;
                bool canDown = row.prisonerIndex < row.prisonerCount - 1;
                DrawQueueButton(new Rect(gx, gy, btn, btn), TexButton.ReorderUp, canUp,
                    "TSA_WD_Prisoners_QueueTopTip".Translate(),
                    () =>
                    {
                        if (OutpostPrisonerUtility.TryMovePrisonerToExtreme(SelOutpost, row.pawn, true))
                            AfterPrisonerQueueChanged();
                    }, doubled: true);
                DrawQueueButton(new Rect(gx + btn + gap, gy, btn, btn), TexButton.ReorderUp, canUp,
                    "TSA_WD_Prisoners_QueueUpTip".Translate(),
                    () =>
                    {
                        if (OutpostPrisonerUtility.TryMovePrisoner(SelOutpost, row.pawn, -1))
                            AfterPrisonerQueueChanged();
                    });
                DrawQueueButton(new Rect(gx, gy + btn + gap, btn, btn), TexButton.ReorderDown, canDown,
                    "TSA_WD_Prisoners_QueueBottomTip".Translate(),
                    () =>
                    {
                        if (OutpostPrisonerUtility.TryMovePrisonerToExtreme(SelOutpost, row.pawn, false))
                            AfterPrisonerQueueChanged();
                    }, doubled: true);
                DrawQueueButton(new Rect(gx + btn + gap, gy + btn + gap, btn, btn), TexButton.ReorderDown, canDown,
                    "TSA_WD_Prisoners_QueueDownTip".Translate(),
                    () =>
                    {
                        if (OutpostPrisonerUtility.TryMovePrisoner(SelOutpost, row.pawn, 1))
                            AfterPrisonerQueueChanged();
                    });
            }
            x += ColReorder;
        }

        private static void DrawQueueButton(Rect rect, Texture2D tex, bool enabled, string tip, Action action, bool doubled = false)
        {
            if (!string.IsNullOrEmpty(tip))
                TooltipHandler.TipRegion(rect, tip);
            if (Mouse.IsOver(rect))
                Widgets.DrawHighlight(rect);
            Texture2D icon = tex ?? BaseContent.BadTex;
            Color prev = GUI.color;
            GUI.color = enabled ? Color.white : new Color(1f, 1f, 1f, 0.28f);
            if (doubled)
            {
                float h = rect.height * 0.52f;
                GUI.DrawTexture(new Rect(rect.x, rect.y + 1f, rect.width, h), icon, ScaleMode.ScaleToFit);
                GUI.DrawTexture(new Rect(rect.x, rect.yMax - h - 1f, rect.width, h), icon, ScaleMode.ScaleToFit);
            }
            else
            {
                GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit);
            }
            GUI.color = prev;
            if (Widgets.ButtonInvisible(rect) && enabled)
            {
                action?.Invoke();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }

        private void AfterPrisonerQueueChanged()
        {
            lastCacheTick = -1;
            InvalidateCache();
            Window_Prisoners.InvalidateCache();
        }

        private bool SelectedRemovalIncludesOccupant()
        {
            var occ = SelOutpost?.Occupants;
            if (occ == null || selectedForRemovalThingIds.Count == 0) return false;
            for (int i = 0; i < occ.Count; i++)
            {
                Pawn pawn = occ[i];
                if (pawn?.ThingID != null && selectedForRemovalThingIds.Contains(pawn.ThingID))
                    return true;
            }
            return false;
        }

        private static string JoinSkillLabels(List<SkillDef> defs)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < defs.Count; i++)
            {
                if (i > 0) sb.Append('/');
                sb.Append(defs[i].LabelCap);
            }
            return sb.ToString();
        }

        private void OpenVanillaInfoCard(Pawn pawn)
        {
            if (pawn == null) return;
            Find.WindowStack.Add(new Dialog_InfoCard(pawn));
        }

        private static void OpenVanillaInfoCard(Thing thing)
        {
            if (thing == null) return;
            Find.WindowStack.Add(new Dialog_InfoCard(thing));
        }
    }
}
