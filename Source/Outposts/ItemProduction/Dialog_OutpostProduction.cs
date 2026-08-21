using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Cached row data for production dialog (computed once on open to avoid stutter).</summary>
    public struct CachedProductionRow
    {
        public ThingDef Def;
        public string ItemLabel;
        public bool CanProduce;
        public bool IsProdItem;
        public bool IsMiningItem;
        /// <summary>True for scavenging rows; select button calls SetSelectedScavenging instead of SetSelectedProduction.</summary>
        public bool IsScavengingKind;
        public Outpost_Scavenging.ScavengingKind ScavengingKind;
        /// <summary>Use the shared question-mark placeholder instead of the ThingDef's uiIcon.</summary>
        public bool UseQuestionMarkIcon;
        public string Formula;
        /// <summary>For per-factor tooltips: "237 Rice = " (everything before first segment). Null if not used.</summary>
        public string FormulaPrefix;
        /// <summary>Segment containing "(Baseline)". Null if not used.</summary>
        public string FormulaBaselinePart;
        /// <summary>Segment containing "(Fertility)" or "(Mining Efficiency)". Null if not used.</summary>
        public string FormulaFactorPart;
        /// <summary>Segment "5 Outpost plants Skill". Null if not used.</summary>
        public string FormulaSkillPart;
        public string TooltipSkill;
        public string TooltipBaseline;
        public string TooltipEfficiency;
        public string TooltipFormula;
        public string DisabledTooltip;
        public float RowHeight;
    }

    /// <summary>Per-product data for hunting rows; formula pre-built at dialog open.</summary>
    public struct CachedHuntingProductLine
    {
        public ThingDef Product;
        public float BaselineUnitsPerSkill;
        public string BaselineTooltip;
        public int CachedCount;
        public string CachedPrefix;
        public string CachedBasePart;
        public string CachedEffPart;
        public string CachedSkillPart;
        public string CachedFormulaLine;
    }

    /// <summary>Cached row data for hunting dialog (computed once on open).</summary>
    public struct CachedHuntingRow
    {
        public HuntingAnimalOption Opt;
        public int MinAnimal;
        public bool CanHunt;
        public float RowHeight;
        public List<CachedHuntingProductLine> ProductLines;
        public string AnimalRowTooltip;
        public string DisabledTooltip;
    }

    /// <summary>Window to select what this outpost produces. All heavy calculations run once when the window opens (cached) to prevent stuttering.</summary>
    [StaticConstructorOnStartup]
    public class Dialog_OutpostProduction : Window
    {
        private readonly WorldObject_WD_Outpost outpost;
        private Vector2 scrollPosition;

        private readonly bool isHunting;
        private readonly bool isFishing;
        private readonly bool isMining;
        private readonly bool isFarming;
        private readonly bool isRanch;
        private readonly bool isTrading;
        private readonly bool isScavenging;
        private readonly bool isProductionOrTrading;
        private readonly float capacity;
        /// <summary>For farming/hunting: skill assigned to colony (from Logistics). For mining/production: same as capacity.</summary>
        private readonly float capacityForColony;
        private readonly string skillName;
        private readonly float cycleDays;
        /// <summary>World-tile fertility 0–100% for farming header and formula; matches establishment selection. 0 if not farming.</summary>
        private readonly int farmingTileFertilityPct;
        private readonly float miningTileFactor;

        private readonly List<CachedHuntingRow> cachedHuntingRows = new List<CachedHuntingRow>();
        private readonly List<CachedProductionRow> cachedProductionRows = new List<CachedProductionRow>();
        /// <summary>True when this dialog has more than five selectable rows (search bar shown).</summary>
        private readonly bool showItemSearchBar;
        private string itemSearchFilter = "";
        private readonly bool isHuntingHeader;
        private readonly int animalsPct;
        private readonly int fertilityPct;
        private readonly int miningEffPct;
        private readonly string windowTitleText;
        /// <summary>Cached on open; hunting efficiency tip is identical for every row — do not rebuild each GUI frame.</summary>
        private readonly string huntingEfficiencyTooltipCached;
        /// <summary>Cached on open; Animals skill factor tip is shared by all hunting formula lines.</summary>
        private readonly string huntingSkillFactorTooltipCached;
        private readonly string cachedBiomeStatLabel;
        private readonly string cachedBiomeStatTooltip;
        private readonly int cachedNearbyCount;
        private readonly string cachedOutputFactorSuffix;
        private readonly string cachedOutputFactorTooltip;

        private int dialogHeaderStatsTick = -1;
        private float dialogCachedTotalSkill;
        private float dialogCachedAllocatedSkill;
        private float dialogCachedAvgCap;
        private float dialogCachedSnapshotSkill;
        private List<ThingDefCountClass> dialogCachedSnapshotItems;
        private List<ThingDefCountClass> dialogCachedAvgItems;
        private int dialogCachedTradingSilver;
        private int dialogCachedTradingSilverAvg;
        private string dialogCachedAvgOutputFormula;
        private string dialogCachedSnapshotOutputFormula;

        private void EnsureDialogHeaderStatsCache()
        {
            int tick = Find.TickManager.TicksGame;
            if (tick == dialogHeaderStatsTick) return;
            dialogHeaderStatsTick = tick;
            dialogCachedTotalSkill = isScavenging ? outpost.WorkerPawnCount : outpost.GetTotalRelevantSkill();
            dialogCachedAllocatedSkill = Outpost_Production_Utils.GetSkillAssignedToPhysicalProduction(outpost);
            dialogCachedAvgCap = outpost.GetCapacityForYieldPreview();
            bool isFoodProducer = Outpost_Production_Utils.IsFoodProducerOutpost(outpost.def);
            dialogCachedSnapshotSkill = isFoodProducer ? dialogCachedAllocatedSkill : Outpost_Production.GetDeliveryDrivingCapacity(outpost);
            if (isTrading || isScavenging)
            {
                dialogCachedSnapshotItems = null;
                dialogCachedAvgItems = null;
                if (isTrading)
                {
                    dialogCachedTradingSilver = Outpost_Trading.ComputeTradingSilverForOutpost(outpost, dialogCachedSnapshotSkill);
                    dialogCachedTradingSilverAvg = Outpost_Trading.ComputeTradingSilverForOutpost(outpost, dialogCachedAvgCap);
                }
                else
                {
                    dialogCachedTradingSilver = 0;
                    dialogCachedTradingSilverAvg = 0;
                }
            }
            else
            {
                dialogCachedSnapshotItems = Outpost_Production.GetCurrentDeliveryItems(outpost, dialogCachedSnapshotSkill);
                dialogCachedAvgItems = Outpost_Production.GetCurrentDeliveryItems(outpost, dialogCachedAvgCap);
                dialogCachedTradingSilver = 0;
                dialogCachedTradingSilverAvg = 0;
            }
            dialogCachedAvgOutputFormula = Outpost_Production_Formula.BuildDeliveryFormulaTooltip(outpost, dialogCachedAvgCap, true);
            dialogCachedSnapshotOutputFormula = Outpost_Production_Formula.BuildDeliveryFormulaTooltip(outpost, dialogCachedSnapshotSkill, true);
        }

        public override Vector2 InitialSize => new Vector2(960f, 728f);

        public override void PreClose()
        {
            base.PreClose();
            Window_OutpostOverview.InvalidateCache();
        }

        public Dialog_OutpostProduction(WorldObject_WD_Outpost outpost)
        {
            this.outpost = outpost;
            doCloseButton = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = false;

            outpost.RecomputeProductionRequirementCache();

            string title = "TSA_WD_Production_Select".Translate().ToString();
            if (title.Contains("TSA_WD_")) title = "Outpost Production";
            optionalTitle = null; // we draw title + efficiency in one row in DoWindowContents
            windowTitleText = title;

            string d = outpost.def?.defName?.ToLowerInvariant() ?? "";
            isHunting = Outpost_Production_Utils.IsHuntingOutpost(outpost.def);
            isFishing = Outpost_Production_Utils.IsFishingOutpost(outpost.def);
            isMining = Outpost_Production_Utils.IsMiningOutpost(outpost.def);
            isFarming = Outpost_Production_Utils.IsFarmingOutpost(outpost.def);
            isRanch = Outpost_Production_Utils.IsRanchOutpost(outpost.def);
            isTrading = Outpost_Production_Utils.IsTradingOutpost(outpost.def);
            isScavenging = Outpost_Production_Utils.IsScavengingOutpost(outpost.def);
            isProductionOrTrading = Outpost_Production_Utils.IsProductionOrTradingOutpost(outpost.def);

            huntingEfficiencyTooltipCached = isHunting
                ? Outpost_Hunting.GetHuntingEfficiencyTooltip(outpost)
                : (isFishing ? Outpost_Fishing.GetFishingEfficiencyTooltip(outpost) : null);
            huntingSkillFactorTooltipCached = (isHunting || isFishing) ? Outpost_Production_Utils.GetSkillFactorTooltip("Animals") : null;

            capacity = isTrading ? outpost.GetTotalRelevantSkill() : (isScavenging ? outpost.WorkerPawnCount : (isMining ? outpost.TotalMiningSkill() : outpost.GetFoodProductionCapacity()));
            capacityForColony = (isFarming || isHunting || isFishing || isRanch) ? Outpost_Production_Utils.GetSkillAssignedToPhysicalProduction(outpost) : capacity;
            bool isFoodOutpost = capacity > 0.01f || isMining;
            float yieldCap = outpost.GetCapacityForYieldPreview();
            skillName = GetSkillName(outpost);
            cycleDays = Outpost_Production_Utils.GetProductionCycleDays(outpost);
            farmingTileFertilityPct = (isFarming || isRanch) ? Outpost_Production_Utils.GetFarmingFertilityPercentInt(outpost) : 0;
            miningTileFactor = isMining ? Outpost_Production_Utils.GetMiningTileProductionFactor(outpost) : 0f;

            // currentLine is computed in DoWindowContents from current colony allocation so it stays in sync

            isHuntingHeader = isHunting || isFishing;
            animalsPct = isHunting
                ? Mathf.RoundToInt(Outpost_Production_Utils.GetHuntingTileProductionFactor(outpost) * 100f)
                : (isFishing ? Mathf.RoundToInt(Outpost_Production_Utils.GetFishingTileProductionFactor(outpost) * 100f) : 0);
            fertilityPct = farmingTileFertilityPct;
            miningEffPct = isMining ? Mathf.RoundToInt(miningTileFactor * 100f) : 0;

            cachedOutputFactorSuffix = Outpost_Production_Utils.BuildProductionOutputFactorSuffix(outpost);
            cachedOutputFactorTooltip = Outpost_Production_Utils.BuildProductionOutputFactorTooltip(outpost);

            if (isHunting)
            {
                cachedBiomeStatLabel = "TSA_WD_Biome_ColAnimals".Translate() + ": " + "TSA_WD_Biome_AnimalsPercent".Translate(animalsPct);
                cachedBiomeStatTooltip = Outpost_Hunting.GetHuntingEfficiencyTooltip(outpost);
            }
            else if (isFishing)
            {
                cachedBiomeStatLabel = "TSA_WD_Biome_ColFish".Translate() + ": " + "TSA_WD_Biome_FishPercent".Translate(animalsPct);
                cachedBiomeStatTooltip = Outpost_Fishing.GetFishingEfficiencyTooltip(outpost);
            }
            else if (isFarming || isRanch)
            {
                cachedBiomeStatLabel = "TSA_WD_Biome_ColFertility".Translate() + ": " + "TSA_WD_Biome_FertilityPercent".Translate(fertilityPct);
                cachedBiomeStatTooltip = Outpost_Farming.GetFarmingEfficiencyTooltip(outpost);
            }
            else if (isMining)
            {
                cachedBiomeStatLabel = "TSA_WD_Production_MiningEfficiency".Translate() + ": " + "TSA_WD_Biome_AnimalsPercent".Translate(miningEffPct);
                cachedBiomeStatTooltip = Outpost_Mining.GetMiningEfficiencyTooltip(outpost);
            }
            else if (isTrading)
            {
                cachedNearbyCount = Outpost_Trading.GetNearbySettlementCount(outpost);
                cachedBiomeStatLabel = "TSA_WD_Biome_ColTradingNearby".Translate() + ": " + cachedNearbyCount.ToString();
                string nearbyTip = "TSA_WD_Biome_Tooltip_TradingNearby".Translate(Outpost_Trading.GetNearbyRadiusTiles(outpost)).ToString();
                if (nearbyTip.Contains("TSA_WD_")) nearbyTip = "Neutral or allied settlements and TSA outposts within the trading radius.";
                string partners = Outpost_Trading.GetNearbyTradingPartnersTooltipAppendix(outpost);
                if (!string.IsNullOrEmpty(partners))
                    nearbyTip = nearbyTip + "\n\n" + partners;
                cachedBiomeStatTooltip = nearbyTip;
            }

            const float rowPadding = 6f;
            const float iconW = 48f;
            const float nameLabelHeight = Outpost_Dialog_UI.ListRowNameHeight;
            const float formulaBlockHeight = Outpost_Dialog_UI.ListRowFormulaBlockHeight;
            const float baseRowWithFormula = nameLabelHeight + formulaBlockHeight;
            const float baseRowSimple = 40f;
            const float huntingFormulaLineH = 18f;

            if (isHunting)
            {
                float yieldCapHunt = yieldCap;
                float huntTileF = Outpost_Production_Utils.GetHuntingTileProductionFactor(outpost);
                int huntEffPct = Mathf.RoundToInt(huntTileF * 100f);
                string baseTagH = "TSA_WD_Production_Formula_Baseline".Translate().ToString();
                if (baseTagH.Contains("TSA_WD_")) baseTagH = "(Baseline)";
                string abundTag = "TSA_WD_Production_Formula_AnimalAbundance".Translate().ToString();
                if (abundTag.Contains("TSA_WD_")) abundTag = "(Animal Abundance)";
                string animalSkillTag = "TSA_WD_Production_Formula_OutpostSkill".Translate("Animals").ToString();
                if (animalSkillTag.Contains("TSA_WD_")) animalSkillTag = "Outpost Animal Skill";
                int maxAnimalsSkill = MaxVirtualPawnSkill(outpost.VirtualPawns, vp => vp.animals);

                var animalOpts = Outpost_Hunting.GetHuntingAnimalOptions(outpost);
                foreach (var opt in animalOpts)
                {
                    int minAnimal = Outpost_Baselines.GetMinAnimalSkillForAnimal(opt.Kind);
                    bool canHunt = Outpost_Baselines.OutpostCanHuntAnimal(outpost, opt.Kind);
                    var previewDel = Outpost_Hunting.BuildHuntingDeliveryItems(opt.Kind, yieldCapHunt, outpost);
                    int nFormulaLines = 1;
                    if (previewDel != null && previewDel.Count > 0) nFormulaLines = previewDel.Count;
                    else if (opt.Products != null && opt.Products.Count > 0) nFormulaLines = Mathf.Max(1, opt.Products.Count);
                    float rowH = nameLabelHeight + nFormulaLines * huntingFormulaLineH + 8f;
                    var productLines = new List<CachedHuntingProductLine>();
                    if (opt.Products != null)
                    {
                        foreach (ThingDef prod in opt.Products)
                        {
                            if (prod == null) continue;
                            float bups = Outpost_Hunting.GetHuntingBaselineUnitsPerSkillForProduct(opt.Kind, prod, outpost);
                            int rawCount = Mathf.Max(0, Mathf.RoundToInt(bups * huntTileF * yieldCapHunt));
                            int fCount = Outpost_Production_Utils.ScaleOutputStackCount(rawCount, outpost);
                            string basePart = bups.ToString("F1") + " " + prod.LabelCap + " " + baseTagH;
                            string effPart = huntEffPct + "% " + abundTag;
                            string skillPart = yieldCapHunt.ToString("F0") + " " + animalSkillTag;
                            string prefix = fCount + " " + prod.LabelCap + " = ";
                            string formulaLine = prefix + basePart + " × " + effPart + " × " + skillPart + cachedOutputFactorSuffix;
                            productLines.Add(new CachedHuntingProductLine
                            {
                                Product = prod,
                                BaselineUnitsPerSkill = bups,
                                BaselineTooltip = Outpost_Baselines.GetBaselineTooltipForProduct(prod),
                                CachedCount = fCount,
                                CachedPrefix = prefix,
                                CachedBasePart = basePart,
                                CachedEffPart = effPart,
                                CachedSkillPart = skillPart,
                                CachedFormulaLine = formulaLine
                            });
                        }
                    }

                    string disabledTip = !canHunt
                        ? FormatNeedsSkillTip(SkillDefOf.Animals?.LabelCap ?? "Animals", minAnimal, maxAnimalsSkill)
                        : null;

                    cachedHuntingRows.Add(new CachedHuntingRow
                    {
                        Opt = opt,
                        MinAnimal = minAnimal,
                        CanHunt = canHunt,
                        RowHeight = rowH,
                        ProductLines = productLines,
                        AnimalRowTooltip = Outpost_Hunting.GetHuntingAnimalRowTooltip(opt.Kind, outpost),
                        DisabledTooltip = disabledTip
                    });
                }
            }
            else if (isFishing)
            {
                float fishTileF = Outpost_Production_Utils.GetFishingTileProductionFactor(outpost);
                int fishEffPct = Mathf.RoundToInt(fishTileF * 100f);
                string baseTagF = "TSA_WD_Production_Formula_Baseline".Translate().ToString();
                if (baseTagF.Contains("TSA_WD_")) baseTagF = "(Baseline)";
                string abundTagF = "TSA_WD_Production_Formula_FishAbundance".Translate().ToString();
                if (abundTagF.Contains("TSA_WD_")) abundTagF = "(Fish Abundance)";
                string animalSkillTagF = "TSA_WD_Production_Formula_OutpostSkill".Translate("Animals").ToString();
                if (animalSkillTagF.Contains("TSA_WD_")) animalSkillTagF = "Outpost Animal Skill";
                int maxAnimalsSkillF = MaxVirtualPawnSkill(outpost.VirtualPawns, vp => vp.animals);

                foreach (var opt in Outpost_Fishing.GetFishingFishOptions(outpost))
                {
                    if (opt.Fish == null) continue;
                    int minAnimals = Outpost_Fishing.GetMinAnimalsSkillForFish(opt.Fish, outpost);
                    bool canFish = Outpost_Fishing.OutpostCanFish(outpost, opt.Fish);
                    float bups = Outpost_Fishing.GetFishBaselineUnitsPerSkill(opt.Fish, outpost);
                    int rawCount = Mathf.Max(0, Mathf.RoundToInt(bups * fishTileF * yieldCap));
                    int fCount = Outpost_Production_Utils.ScaleOutputStackCount(rawCount, outpost);
                    string prefix = fCount + " " + opt.Fish.LabelCap + " = ";
                    string basePart = bups.ToString("F1") + " " + opt.Fish.LabelCap + " " + baseTagF;
                    string effPart = fishEffPct + "% " + abundTagF;
                    string skillPart = yieldCap.ToString("F0") + " " + animalSkillTagF;
                    string formula = prefix + basePart + " × " + effPart + " × " + skillPart + cachedOutputFactorSuffix;
                    string disabledTip = !canFish
                        ? FormatNeedsSkillTip(SkillDefOf.Animals?.LabelCap ?? "Animals", minAnimals, maxAnimalsSkillF)
                        : null;
                    cachedProductionRows.Add(new CachedProductionRow
                    {
                        Def = opt.Fish,
                        ItemLabel = opt.Fish.LabelCap,
                        CanProduce = canFish,
                        IsProdItem = false,
                        IsMiningItem = false,
                        Formula = formula,
                        FormulaPrefix = prefix,
                        FormulaBaselinePart = basePart,
                        FormulaFactorPart = effPart,
                        FormulaSkillPart = skillPart,
                        TooltipSkill = huntingSkillFactorTooltipCached,
                        TooltipBaseline = Outpost_Fishing.GetFishingFishRowTooltip(opt.Fish, outpost),
                        TooltipEfficiency = huntingEfficiencyTooltipCached,
                        TooltipFormula = formula,
                        DisabledTooltip = disabledTip,
                        RowHeight = baseRowWithFormula
                    });
                }
            }
            else
            {
                ThingDef producingForCycle = outpost.GetProducingDefForCurrentCycle() ?? outpost.SelectedProductionDef;
                var options = Outpost_Production.GetProducibleOptions(outpost);
                foreach (ThingDef def in options)
                {
                    bool isProdItem = isProductionOrTrading && Outpost_Production_Utils.GetProductionOption(outpost, def) != null;
                    bool isMiningItem = isMining;
                    float rowH = (isFoodOutpost || isProdItem) ? baseRowWithFormula : baseRowSimple;

                    string itemLabel;
                    bool canProduce;
                    if (isScavenging && def == ThingDefOf.ComponentIndustrial)
                    {
                        foreach (var kind in Outpost_Scavenging.AllKinds)
                        {
                            string kindLabel = Outpost_Scavenging.GetKindLabel(kind);
                            int minPawns = Outpost_Scavenging.GetMinPawns(kind);
                            bool canSelect = Outpost_Scavenging.CanUseKind(outpost, kind);
                            string kindItemLabel = kindLabel;
                            string formulaSc = Outpost_Scavenging.GetYieldPreviewLabel(outpost, kind);
                            string disabled = null;
                            if (!canSelect)
                                disabled = FormatNeedsPawnsTip(minPawns, outpost.PawnCount);
                            cachedProductionRows.Add(new CachedProductionRow
                            {
                                Def = def,
                                ItemLabel = kindItemLabel,
                                CanProduce = canSelect,
                                IsProdItem = false,
                                IsMiningItem = false,
                                IsScavengingKind = true,
                                ScavengingKind = kind,
                                UseQuestionMarkIcon = true,
                                Formula = formulaSc,
                                FormulaPrefix = null,
                                FormulaBaselinePart = null,
                                FormulaFactorPart = null,
                                FormulaSkillPart = null,
                                TooltipSkill = Outpost_Scavenging.GetKindRequirementTooltip(kind),
                                TooltipBaseline = null,
                                TooltipEfficiency = null,
                                TooltipFormula = null,
                                DisabledTooltip = disabled,
                                RowHeight = baseRowSimple
                            });
                        }
                        continue;
                    }
                    if (isTrading && def == ThingDefOf.Silver)
                    {
                        itemLabel = "TSA_WD_Production_Trading".Translate().ToString();
                        if (itemLabel.Contains("TSA_WD_")) itemLabel = "Trading";
                        canProduce = true;
                        int silverPreview = Outpost_Trading.ComputeTradingSilverForOutpost(outpost);
                        string silverLabel = ThingDefOf.Silver?.LabelCap.ToString() ?? "Silver";
                        string formulaTr = silverPreview + " " + silverLabel;
                        cachedProductionRows.Add(new CachedProductionRow
                        {
                            Def = def,
                            ItemLabel = itemLabel,
                            CanProduce = true,
                            IsProdItem = false,
                            IsMiningItem = false,
                            Formula = formulaTr,
                            FormulaPrefix = null,
                            FormulaBaselinePart = null,
                            FormulaFactorPart = null,
                            FormulaSkillPart = null,
                            TooltipSkill = Outpost_Trading.GetDetailedMathTooltip(outpost),
                            TooltipBaseline = null,
                            TooltipEfficiency = null,
                            TooltipFormula = null,
                            RowHeight = baseRowSimple
                        });
                        continue;
                    }
                    if (isProdItem)
                    {
                        var opt = Outpost_Production_Utils.GetProductionOption(outpost, def);
                        SkillDef scaleSkillDef = Outpost_Production_Utils.GetScalingSkillDefForProduction(outpost, opt);
                        string rowSkillLabel = Outpost_Production_Utils.SkillLabelCap(scaleSkillDef);
                        if (string.IsNullOrEmpty(rowSkillLabel)) rowSkillLabel = skillName;
                        itemLabel = def.LabelCap;
                        canProduce = Outpost_Production_Utils.OutpostCanProduceItem(outpost, def);
                    }
                    else if (isMiningItem)
                    {
                        int minMining = Outpost_Baselines.GetMinMiningSkillForProduct(def);
                        itemLabel = def.LabelCap;
                        canProduce = Outpost_Baselines.OutpostCanProduceMiningItem(outpost, def);
                    }
                    else
                    {
                        int minPlants = Outpost_Baselines.GetMinPlantsSkillForCrop(def);
                        itemLabel = def.LabelCap;
                        canProduce = Outpost_Baselines.OutpostCanProduceCrop(outpost, def);
                    }

                    string formula = "";
                    string tooltipSkill = Outpost_Production_Utils.GetSkillFactorTooltip(skillName);
                    string tooltipBaseline = "";
                    string tooltipEfficiency = "";
                    string formulaPrefix = null;
                    string formulaBaselinePart = null;
                    string formulaFactorPart = null;
                    string formulaSkillPart = null;
                    string tooltipFormulaValue = "";
                    if (isFoodOutpost && !isProdItem && isMiningItem)
                    {
                        float outputPerSkill = Outpost_Mining.GetOutputPerSkillPoint(outpost, def);
                        int totalScaled = Outpost_Production_Utils.ScaleOutputStackCount(Mathf.Max(0, Mathf.RoundToInt(yieldCap * outputPerSkill)), outpost);
                        float miningBaseline = Outpost_Baselines.GetMiningBaselinePerSkill(def);
                        int effPct = Mathf.RoundToInt(miningTileFactor * 100f);
                        string baseTag = "TSA_WD_Production_Formula_Baseline".Translate().ToString();
                        if (baseTag.Contains("TSA_WD_")) baseTag = "(Baseline)";
                        string effTag = "TSA_WD_Production_Formula_MiningEfficiency".Translate().ToString();
                        if (effTag.Contains("TSA_WD_")) effTag = "(Mining Efficiency)";
                        string skillTag = "TSA_WD_Production_Formula_OutpostSkill".Translate(skillName).ToString();
                        if (skillTag.Contains("TSA_WD_")) skillTag = "Outpost " + skillName + " Skill";
                        formulaPrefix = totalScaled + " " + def.LabelCap + " = ";
                        formulaBaselinePart = miningBaseline.ToString("F1") + " " + def.LabelCap + " " + baseTag;
                        formulaFactorPart = effPct + "% " + effTag;
                        formulaSkillPart = yieldCap.ToString("F0") + " " + skillTag;
                        formula = formulaPrefix + formulaBaselinePart + " × " + formulaFactorPart + " × " + formulaSkillPart + cachedOutputFactorSuffix;
                        tooltipBaseline = Outpost_Mining.GetMiningBaselineTooltip(def);
                        tooltipEfficiency = Outpost_Mining.GetMiningEfficiencyTooltip(outpost);
                    }
                    else if (isFoodOutpost && !isProdItem)
                    {
                        float outputPerSkill = Outpost_Farming.GetOutputPerSkillPoint(outpost, def);
                        int totalScaled = Outpost_Production_Utils.ScaleOutputStackCount(Mathf.Max(0, Mathf.RoundToInt(yieldCap * outputPerSkill)), outpost);
                        float cropBaseline = Outpost_Baselines.GetCropBaselinePerSkill(def);
                        int effPct = farmingTileFertilityPct;
                        string baseTagF = "TSA_WD_Production_Formula_Baseline".Translate().ToString();
                        if (baseTagF.Contains("TSA_WD_")) baseTagF = "(Baseline)";
                        string fertTag = "TSA_WD_Production_Formula_Fertility".Translate().ToString();
                        if (fertTag.Contains("TSA_WD_")) fertTag = "(Fertility)";
                        string skillTagF = "TSA_WD_Production_Formula_OutpostSkill".Translate(skillName).ToString();
                        if (skillTagF.Contains("TSA_WD_")) skillTagF = "Outpost " + skillName + " Skill";
                        formulaPrefix = totalScaled + " " + def.LabelCap + " = ";
                        formulaBaselinePart = cropBaseline.ToString("F1") + " " + def.LabelCap + " " + baseTagF;
                        formulaFactorPart = effPct + "% " + fertTag;
                        formulaSkillPart = yieldCap.ToString("F0") + " " + skillTagF;
                        formula = formulaPrefix + formulaBaselinePart + " × " + formulaFactorPart + " × " + formulaSkillPart + cachedOutputFactorSuffix;
                        tooltipBaseline = Outpost_Farming.GetCropBaselineTooltip(def);
                        tooltipEfficiency = Outpost_Farming.GetFarmingEfficiencyTooltip(outpost);
                    }
                    else if (isProdItem)
                    {
                        var opt = Outpost_Production_Utils.GetProductionOption(outpost, def);
                        SkillDef scaleSkillDef = Outpost_Production_Utils.GetScalingSkillDefForProduction(outpost, opt);
                        string rowSkillLabel = Outpost_Production_Utils.SkillLabelCap(scaleSkillDef);
                        if (string.IsNullOrEmpty(rowSkillLabel)) rowSkillLabel = skillName;
                        float eligible = Outpost_Production_Utils.GetScalingSkillTotalForProductionPreview(outpost, opt);
                        float ranchTileFactor = isRanch ? Outpost_Production_Utils.GetRanchTileProductionFactor(outpost) : 1f;
                        int totalScaled = Outpost_Production_Utils.ScaleOutputStackCount(Mathf.RoundToInt(eligible * opt.amountPerSkillLevel * ranchTileFactor), outpost);
                        string baseTagProd = "TSA_WD_Production_Formula_Baseline".Translate().ToString();
                        if (baseTagProd.Contains("TSA_WD_")) baseTagProd = "(Baseline)";
                        string skillTagProd = "TSA_WD_Production_Formula_OutpostSkill".Translate(rowSkillLabel).ToString();
                        if (skillTagProd.Contains("TSA_WD_")) skillTagProd = "Outpost " + rowSkillLabel + " Skill";
                        formulaPrefix = totalScaled + " " + def.LabelCap + " = ";
                        formulaBaselinePart = opt.amountPerSkillLevel.ToString("F1") + " " + def.LabelCap + " " + baseTagProd;
                        formulaSkillPart = eligible.ToString("F0") + " " + skillTagProd;
                        if (isRanch)
                        {
                            int effPct = farmingTileFertilityPct;
                            string fertTag = "TSA_WD_Production_Formula_Fertility".Translate().ToString();
                            if (fertTag.Contains("TSA_WD_")) fertTag = "(Fertility)";
                            formulaFactorPart = effPct + "% " + fertTag;
                            formula = formulaPrefix + formulaBaselinePart + " × " + formulaFactorPart + " × " + formulaSkillPart + cachedOutputFactorSuffix;
                            tooltipEfficiency = Outpost_Farming.GetFarmingEfficiencyTooltip(outpost);
                        }
                        else
                        {
                            formula = formulaPrefix + formulaBaselinePart + " × " + formulaSkillPart + cachedOutputFactorSuffix;
                        }
                        tooltipSkill = Outpost_Production_Utils.GetSkillFactorTooltip(rowSkillLabel);
                        string researchCap = string.IsNullOrEmpty(opt.requiredResearch) ? "" : (DefDatabase<ResearchProjectDef>.GetNamedSilentFail(opt.requiredResearch)?.LabelCap ?? opt.requiredResearch);
                        string tooltipFormulaFull = string.IsNullOrEmpty(opt.requiredResearch)
                            ? "TSA_WD_Production_EligibleSkillTooltipNoResearch".Translate(rowSkillLabel, opt.minSkillLevel).ToString()
                            : "TSA_WD_Production_EligibleSkillTooltip".Translate(rowSkillLabel, opt.minSkillLevel, researchCap).ToString();
                        if (tooltipFormulaFull.Contains("TSA_WD_")) tooltipFormulaFull = "At least one pawn with " + rowSkillLabel + " ≥ " + opt.minSkillLevel + " required; output uses total " + rowSkillLabel + " skill of the outpost." + (string.IsNullOrEmpty(opt.requiredResearch) ? "" : "\nRequires research: " + researchCap);
                        tooltipBaseline = "TSA_WD_Production_BaselineTooltip_Xml".Translate().ToString();
                        if (tooltipBaseline.Contains("TSA_WD_Production_BaselineTooltip_Xml")) tooltipBaseline = "Baselines are defined in the outpost XML (per product).";
                        tooltipFormulaValue = tooltipFormulaFull;
                    }

                    string disabledTooltip = null;
                    if (!canProduce)
                    {
                        if (isProdItem)
                            disabledTooltip = BuildProductionOptionUnmetTooltip(outpost, def, skillName);
                        else if (isMiningItem)
                        {
                            int minMining = Outpost_Baselines.GetMinMiningSkillForProduct(def);
                            int bestMining = MaxVirtualPawnSkill(outpost.VirtualPawns, vp => vp.mining);
                            disabledTooltip = FormatNeedsSkillTip(SkillDefOf.Mining?.LabelCap ?? "Mining", minMining, bestMining);
                        }
                        else
                        {
                            int minPlants = Outpost_Baselines.GetMinPlantsSkillForCrop(def);
                            int bestPlants = MaxVirtualPawnSkill(outpost.VirtualPawns, vp => vp.plants);
                            disabledTooltip = FormatNeedsSkillTip(SkillDefOf.Plants?.LabelCap ?? "Plants", minPlants, bestPlants);
                        }
                    }

                    cachedProductionRows.Add(new CachedProductionRow
                    {
                        Def = def,
                        ItemLabel = itemLabel,
                        CanProduce = canProduce,
                        IsProdItem = isProdItem,
                        IsMiningItem = isMiningItem,
                        Formula = formula,
                        FormulaPrefix = formulaPrefix,
                        FormulaBaselinePart = formulaBaselinePart,
                        FormulaFactorPart = formulaFactorPart,
                        FormulaSkillPart = formulaSkillPart,
                        TooltipSkill = tooltipSkill,
                        TooltipBaseline = tooltipBaseline,
                        TooltipEfficiency = tooltipEfficiency,
                        TooltipFormula = tooltipFormulaValue,
                        DisabledTooltip = disabledTooltip,
                        RowHeight = rowH
                    });
                }
            }

            int selectableCount = isHunting ? cachedHuntingRows.Count : cachedProductionRows.Count;
            showItemSearchBar = selectableCount > 5;
        }

        private static bool TokenMatches(string haystack, string needleLower)
        {
            if (string.IsNullOrEmpty(needleLower)) return true;
            if (string.IsNullOrEmpty(haystack)) return false;
            return haystack.ToLowerInvariant().Contains(needleLower);
        }

        private bool ProductionRowMatchesItemSearch(CachedProductionRow row)
        {
            string q = itemSearchFilter?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(q)) return true;
            if (TokenMatches(row.ItemLabel, q)) return true;
            if (TokenMatches(row.DisabledTooltip, q)) return true;
            if (row.Def == null) return false;
            return TokenMatches(row.Def.label, q) || TokenMatches(row.Def.defName, q);
        }

        private bool HuntingRowMatchesItemSearch(CachedHuntingRow row)
        {
            string q = itemSearchFilter?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(q)) return true;
            var k = row.Opt.Kind;
            if (k != null)
            {
                if (TokenMatches(k.LabelCap, q) || TokenMatches(k.defName, q)) return true;
                if (k.race != null && (TokenMatches(k.race.label, q) || TokenMatches(k.race.defName, q))) return true;
            }
            if (TokenMatches(row.DisabledTooltip, q)) return true;
            if (row.Opt.Products == null) return false;
            foreach (var p in row.Opt.Products)
            {
                if (p == null) continue;
                if (TokenMatches(p.LabelCap, q) || TokenMatches(p.defName, q)) return true;
            }
            return false;
        }

        private static string CapitalizeFirst(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.Length == 1) return char.ToUpperInvariant(s[0]).ToString();
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        private static string CompactFormulaText(string fullFormula, string formulaPrefix)
        {
            if (!string.IsNullOrEmpty(formulaPrefix))
            {
                string compact = formulaPrefix.Trim();
                if (compact.EndsWith("=", StringComparison.Ordinal))
                    compact = compact.Substring(0, compact.Length - 1).TrimEnd();
                return compact;
            }

            if (string.IsNullOrEmpty(fullFormula)) return fullFormula;
            int equalsIdx = fullFormula.IndexOf('=');
            return equalsIdx > 0 ? fullFormula.Substring(0, equalsIdx).TrimEnd() : fullFormula;
        }

        private string BuildFormulaTooltip(string fullFormula)
        {
            if (string.IsNullOrEmpty(fullFormula)) return "";
            if (string.IsNullOrEmpty(cachedOutputFactorTooltip)) return fullFormula;
            return fullFormula + "\n\n" + cachedOutputFactorTooltip;
        }

        private static string GetSkillName(WorldObject_WD_Outpost outpost)
        {
            string skillFallback = "TSA_WD_Production_SkillFallback".Translate().ToString();
            if (skillFallback.Contains("TSA_WD_")) skillFallback = "Skill";
            if (outpost?.def == null) return skillFallback;
            if (Outpost_Production_Utils.IsScavengingOutpost(outpost.def))
            {
                string k = "TSA_WD_Outpost_RelevantStat_Pawns";
                string t = k.Translate().ToString();
                return (t == k || t.Contains("TSA_WD_")) ? "Pawns" : t;
            }
            var skills = WorldObject_WD_Outpost.GetRelevantSkillDefs(outpost.def);
            if (skills == null || skills.Count == 0) return skillFallback;
            if (skills.Count == 1) return Outpost_Production_Utils.SkillLabelCap(skills[0]);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < skills.Count; i++)
            {
                if (i > 0) sb.Append('/');
                sb.Append(Outpost_Production_Utils.SkillLabelCap(skills[i]));
            }
            return sb.ToString();
        }

        private static string GetCurrentLineText(WorldObject_WD_Outpost o)
        {
            if (o == null) return "—";
            bool locked = o.IsSelectionLockedForThisCycle;
            if (locked)
            {
                string currentName = GetProducingNameForCycle(o, true);
                string nextName = GetProducingNameForCycle(o, false);
                string curPart = "TSA_WD_Production_CurrentCycle".Translate(currentName).ToString();
                if (curPart.Contains("TSA_WD_")) curPart = "Current Cycle: " + currentName;
                if (currentName != nextName)
                {
                    string nextPart = "TSA_WD_Production_NextCycleLabel".Translate(nextName).ToString();
                    if (nextPart.Contains("TSA_WD_")) nextPart = "Next Cycle: " + nextName;
                    return curPart + ". " + nextPart;
                }
                return curPart;
            }
            string noneLabel = "TSA_WD_Production_NoneLabel".Translate().ToString();
            if (noneLabel.Contains("TSA_WD_")) noneLabel = "None";
            string currentlyFmt = "TSA_WD_Production_Currently".Translate().ToString();
            if (currentlyFmt.Contains("TSA_WD_")) currentlyFmt = "Currently: {0}";
            if (Outpost_Production_Utils.IsScavengingOutpost(o.def))
            {
                if (!o.HasSelectedScavengingKind)
                    return string.Format(currentlyFmt, noneLabel);
                string s = Outpost_Production.FormatProductionSummaryLine(o);
                return !string.IsNullOrEmpty(s) ? s : "Scavenging";
            }
            if (Outpost_Production_Utils.IsRecruitingOutpost(o.def) || Outpost_Production_Utils.IsTradingOutpost(o.def))
            {
                string s = Outpost_Production.FormatProductionSummaryLine(o);
                return !string.IsNullOrEmpty(s) ? s : (Outpost_Production_Utils.IsRecruitingOutpost(o.def) ? "Recruiting" : "Trading");
            }
            if (Outpost_Production_Utils.IsHuntingOutpost(o.def))
            {
                string s = Outpost_Hunting.FormatHuntingSummaryLine(o);
                return !string.IsNullOrEmpty(s) ? s : string.Format(currentlyFmt, o.SelectedPawnKindForHunting?.LabelCap ?? noneLabel);
            }
            if (Outpost_Production_Utils.IsFishingOutpost(o.def))
            {
                string s = Outpost_Fishing.FormatFishingSummaryLine(o);
                return !string.IsNullOrEmpty(s) ? s : string.Format(currentlyFmt, o.SelectedFishDef?.LabelCap ?? noneLabel);
            }
            if (Outpost_Production_Utils.IsFarmingOutpost(o.def))
            {
                string s = Outpost_Farming.FormatFarmingSummaryLine(o);
                return !string.IsNullOrEmpty(s) ? s : string.Format(currentlyFmt, o.SelectedProductionDef?.LabelCap ?? noneLabel);
            }
            if (Outpost_Production_Utils.IsMiningOutpost(o.def))
            {
                string s = Outpost_Mining.FormatMiningSummaryLine(o);
                return !string.IsNullOrEmpty(s) ? s : string.Format(currentlyFmt, o.SelectedProductionDef?.LabelCap ?? noneLabel);
            }
            if (Outpost_Production_Utils.IsProductionOrTradingOutpost(o.def))
            {
                string s = Outpost_Production.FormatProductionSummaryLine(o);
                return !string.IsNullOrEmpty(s) ? s : string.Format(currentlyFmt, o.SelectedProductionDef?.LabelCap ?? noneLabel);
            }
            return string.Format(currentlyFmt, o.SelectedProductionDef?.LabelCap ?? noneLabel);
        }

        private static string GetProducingNameForCycle(WorldObject_WD_Outpost o, bool forCurrentCycle)
        {
            string noneLabel = "TSA_WD_Production_NoneLabel".Translate().ToString();
            if (noneLabel.Contains("TSA_WD_")) noneLabel = "None";
            if (o == null) return "—";
            if (Outpost_Production_Utils.IsScavengingOutpost(o.def))
            {
                var sk = forCurrentCycle ? o.GetProducingScavengingKindForCurrentCycle() : o.SelectedScavengingKind;
                if (!sk.HasValue) return noneLabel;
                string sBase = "TSA_WD_Production_Scavenging".Translate().ToString();
                if (sBase.Contains("TSA_WD_")) sBase = "Scavenging";
                return sBase + " (" + Outpost_Scavenging.GetKindShortLabel(sk.Value) + ")";
            }
            if (Outpost_Production_Utils.IsRecruitingOutpost(o.def)) return "TSA_WD_Production_Recruiting".Translate().ToString();
            if (Outpost_Production_Utils.IsTradingOutpost(o.def)) return "TSA_WD_Production_Trading".Translate().ToString();
            if (Outpost_Production_Utils.IsHuntingOutpost(o.def))
            {
                var kind = forCurrentCycle ? o.GetProducingPawnKindForCurrentCycle() : o.SelectedPawnKindForHunting;
                return kind?.LabelCap ?? noneLabel;
            }
            if (Outpost_Production_Utils.IsFishingOutpost(o.def))
            {
                var fish = forCurrentCycle ? o.GetProducingFishForCurrentCycle() : o.SelectedFishDef;
                return fish?.LabelCap ?? noneLabel;
            }
            var def = forCurrentCycle ? o.GetProducingDefForCurrentCycle() : o.SelectedProductionDef;
            return def?.LabelCap ?? noneLabel;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            const float listRightMargin = 20f;
            const float tableLeftPadding = 10f;
            float contentWidth = inRect.width - listRightMargin;
            const float iconColW = 56f;
            const float iconPadding = 8f;
            const float listRowRightMargin = 8f;
            // Shared right edge for the top-right content: lines up with the left edge of the close "X".
            const float closeXLeftInset = 22f; // Widgets.CloseButtonFor: 18px button + 4px margin from the right edge
            const float rightScrollbarW = 16f;
            float rightContentRight = inRect.width - closeXLeftInset;
            float headerSlotWidth = 165f; // 10% more than 150 so "Animal Abundance: 56 %" doesn't wrap
            float slotX = rightContentRight - headerSlotWidth; // right-align the efficiency stat with the search bar

            float y = 0;
            bool showHeaderSlot = isHuntingHeader || isFarming || isRanch || isMining || isTrading;
            // Title and efficiency in one row: big title left, efficiency right
            Rect titleLeftRect = new Rect(0f, y, slotX - 8f, Outpost_Dialog_UI.DialogTitleHeight);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(titleLeftRect, windowTitleText);
            Text.Anchor = TextAnchor.UpperLeft;
            if (showHeaderSlot)
            {
                Rect slotRect = new Rect(slotX, y + Outpost_Dialog_UI.DialogHeaderSlotTopInset, headerSlotWidth, Outpost_Dialog_UI.DialogHeaderSlotHeight);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleRight;
                if (isHuntingHeader)
                {
                    Color abundanceCol = animalsPct <= 30 ? Color.red : (animalsPct <= 60 ? Color.yellow : Color.green);
                    GUI.color = abundanceCol;
                }
                else if (isFarming || isRanch)
                {
                    Color fertilityCol = fertilityPct <= 30 ? Color.red : (fertilityPct <= 60 ? Color.yellow : Color.green);
                    GUI.color = fertilityCol;
                }
                else if (isMining)
                {
                    Color miningCol = miningEffPct <= 30 ? Color.red : (miningEffPct <= 60 ? Color.yellow : Color.green);
                    GUI.color = miningCol;
                }
                else if (isTrading)
                {
                    Color nearbyCol = cachedNearbyCount == 0 ? Color.red : (cachedNearbyCount <= 2 ? Color.yellow : Color.green);
                    GUI.color = nearbyCol;
                }
                Widgets.Label(slotRect, cachedBiomeStatLabel);
                TooltipHandler.TipRegion(slotRect, cachedBiomeStatTooltip);
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }
            y += Outpost_Dialog_UI.DialogTitleRowAdvance;
            string outpostName = outpost.Name ?? outpost.Label;
            string typeLabel = outpost.def?.label ?? "Outpost";
            string subTitle = (outpostName + " (" + typeLabel + ")").Truncate(contentWidth);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            const float subTitleHeight = 24f; // 24 keeps descenders (e.g. the 'g' in "Scavenging") from being clipped
            Rect subTitleRect = new Rect(0f, y, contentWidth, subTitleHeight);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(subTitleRect, subTitle);
            Text.Anchor = TextAnchor.UpperLeft;
            y += subTitleHeight + 4f;

            // Production paused: header + bullet list (one line per reason; layout adjusts for multiple reasons)
            var pauseReasons = outpost.GetProductionPauseReasons();
            const float pauseHeaderHeight = 20f;  // 2px more than 18 so "Production paused" isn't cut off
            const float pauseReasonLineHeight = 21f;  // 5px more per reason so descenders aren't cropped
            if (pauseReasons != null && pauseReasons.Count > 0)
            {
                GUI.color = Color.yellow;
                string header = "TSA_WD_Production_PausedHeader".Translate().ToString();
                if (header.Contains("TSA_WD_")) header = "Production paused:";
                float pauseH = pauseHeaderHeight + pauseReasons.Count * pauseReasonLineHeight;
                Widgets.Label(new Rect(0f, y, contentWidth, pauseHeaderHeight), header);
                y += pauseHeaderHeight;
                foreach (string r in pauseReasons)
                {
                    if (!string.IsNullOrEmpty(r))
                        Widgets.Label(new Rect(12f, y, contentWidth - 12f, pauseReasonLineHeight), "• " + r);
                    y += pauseReasonLineHeight;
                }
                GUI.color = Color.white;
                y += 6f;
            }

            y = Outpost_Dialog_UI.DrawSkillDiminishingReturnsBanner(0f, y, contentWidth, outpost);
            y += 4f;   // less space over table

            // ===== LEFT COLUMN: simple production breakdown =====
            EnsureDialogHeaderStatsCache();
            float snapshotSkill = dialogCachedSnapshotSkill;
            float avgSkill = dialogCachedAvgCap;
            string skillCap = CapitalizeFirst(skillName);

            const float bottomReserve = 44f;
            const float colGap = 18f;
            float columnsTop = y;
            float columnsBottom = inRect.height - bottomReserve;
            float leftW = Mathf.Max(260f, contentWidth * 0.42f);
            Rect leftArea = new Rect(0f, columnsTop, leftW, columnsBottom - columnsTop);
            // Extend the right column so its content (zebra rows) lines up with the left edge of the top-right close "X".
            // The 16px scrollbar sits to the right of that line, below the X, so there is no overlap.
            float rightColRight = rightContentRight + rightScrollbarW;
            Rect rightArea = new Rect(leftW + colGap, columnsTop, rightColRight - (leftW + colGap), columnsBottom - columnsTop);
            Widgets.DrawLineVertical(leftW + colGap * 0.5f, columnsTop, columnsBottom - columnsTop);

            float lx = leftArea.x;
            float lw = leftArea.width;
            float ly = leftArea.y;
            Text.Anchor = TextAnchor.MiddleLeft;
            const float lineH = Outpost_Dialog_UI.OutcomeLineH;
            const float boxPad = Outpost_Dialog_UI.OutcomeBoxPad;

            // ===== Outcome box: timer, cycle average, expected delivery =====
            float cycleDaysLeft = outpost.ProductionTicksLeftForDisplay / 60000f;
            string avgFallback = GetNonItemYieldText(false);
            float avgYieldH = Outpost_Dialog_UI.MeasureYieldLinesHeight(dialogCachedAvgItems, avgFallback);
            float boxH = boxPad * 2f + (lineH + 2f) + lineH + lineH + avgYieldH;
            Outpost_Dialog_UI.DrawOutcomeBox(new Rect(lx, ly, lw, boxH));
            float cy = ly + boxPad;
            float ix = lx + boxPad;
            float iw = lw - boxPad * 2f;

            GUI.color = Outpost_Dialog_UI.CycleTimerColor;
            string cycleEndsLine = Tr("TSA_WD_Production_Info_CycleEndsIn", "Production cycle ends in " + cycleDaysLeft.ToString("F1") + " days", cycleDaysLeft.ToString("F1"));
            Rect cycleEndsRect = new Rect(ix, cy, iw, lineH);
            Widgets.Label(cycleEndsRect, cycleEndsLine);
            TooltipHandler.TipRegion(cycleEndsRect, Tr("TSA_WD_Production_Info_CycleEndsInTip", "Time remaining until this production cycle completes and delivers its output."));
            GUI.color = Color.white;
            cy += lineH + 2f;

            string avgSkillLine = isScavenging
                ? Tr("TSA_WD_Production_Info_AvgPawns", "Average colonists this cycle: " + avgSkill.ToString("F0"), avgSkill.ToString("F0"))
                : Tr("TSA_WD_Production_Info_AvgSkill", "Average " + skillCap + " Skill this cycle: " + avgSkill.ToString("F0"), skillCap, avgSkill.ToString("F0"));
            string avgTip = isScavenging
                ? Tr("TSA_WD_Production_Info_AvgPawnsTip", "During this production cycle the average colonist count was " + avgSkill.ToString("F0") + ". This determines the actual output at the end of the cycle.", avgSkill.ToString("F0"))
                : Tr("TSA_WD_Production_Info_AvgSkillTip", "During this production cycle the average effective " + skillCap + " skill (after diminishing returns) was " + avgSkill.ToString("F0") + ". This determines the actual output at the end of the cycle.", avgSkill.ToString("F0"), skillCap);
            Rect avgSkillRect = new Rect(ix, cy, iw, lineH);
            Widgets.Label(avgSkillRect, avgSkillLine);
            TooltipHandler.TipRegion(avgSkillRect, avgTip);
            cy += lineH;

            float outEndTop = cy;
            Rect outEndRect = new Rect(ix, cy, iw, lineH);
            Widgets.Label(outEndRect, Tr("TSA_WD_Production_Info_OutputCycleEnd", "Expected output at cycle end:"));
            cy += lineH;
            cy = Outpost_Dialog_UI.DrawOutcomeLines(
                ix + Outpost_Dialog_UI.OutcomeValueIndent,
                cy,
                iw - Outpost_Dialog_UI.OutcomeValueIndent,
                dialogCachedAvgItems,
                avgFallback,
                Outpost_Dialog_UI.OutcomeValueColor);
            if (isTrading)
                TooltipHandler.TipRegion(new Rect(ix, outEndTop, iw, cy - outEndTop), Outpost_Trading.GetDetailedMathTooltip(outpost, dialogCachedAvgCap));
            else if (!string.IsNullOrEmpty(dialogCachedAvgOutputFormula))
                TooltipHandler.TipRegion(new Rect(ix, outEndTop, iw, cy - outEndTop), dialogCachedAvgOutputFormula);
            else
                TooltipHandler.TipRegion(outEndRect, Tr("TSA_WD_Production_TableHeader_AverageTooltip", "Running average over the cycle; this is what you actually receive at cycle end."));
            ly += boxH + Outpost_Dialog_UI.OutcomeBoxGap;

            // ===== Snapshot: current skill + theoretical output now =====
            float currentRaw = GetProductionSkillRawForUi(outpost, isScavenging, isMining, isFarming, isHunting || isFishing, isRanch, isTrading);
            string curSkillDisplay = isScavenging
                ? snapshotSkill.ToString("F0")
                : OutpostSkillScaling.FormatRawEffective(currentRaw);
            string curSkillLine = isScavenging
                ? Tr("TSA_WD_Production_Info_CurrentPawns", "Colonists at Outpost: " + curSkillDisplay, curSkillDisplay)
                : Tr("TSA_WD_Production_Info_CurrentSkill", "Current " + skillCap + " Skill at Outpost: " + curSkillDisplay, skillCap, curSkillDisplay);
            string curTip = isScavenging
                ? Tr("TSA_WD_Production_Info_CurrentPawnsTip", "Currently, this outpost has " + curSkillDisplay + " colonists.", curSkillDisplay)
                : Tr("TSA_WD_Production_Info_CurrentSkillTip", "Currently, this outpost has " + currentRaw.ToString("F0") + " cumulative " + skillCap + " skill from pawns (effective " + OutpostSkillScaling.ToEffective(currentRaw).ToString("F0") + ").", currentRaw.ToString("F0"), skillCap);
            if (!isScavenging && OutpostSkillScaling.IsDiminished(currentRaw))
                curTip = curTip + "\n\n" + OutpostSkillScaling.BuildBandBreakdownTip(currentRaw);
            Rect curSkillRect = new Rect(lx, ly, lw, lineH);
            Widgets.Label(curSkillRect, curSkillLine);
            TooltipHandler.TipRegion(curSkillRect, curTip);
            ly += lineH;

            float outNowTop = ly;
            Rect outNowRect = new Rect(lx, ly, lw, lineH);
            GUI.color = Outpost_Dialog_UI.TheoreticalLabelColor;
            Widgets.Label(outNowRect, Tr("TSA_WD_Production_Info_OutputNow", "Theoretical output at this skill level:"));
            GUI.color = Color.white;
            ly += lineH;
            ly = Outpost_Dialog_UI.DrawOutcomeLines(
                lx + Outpost_Dialog_UI.OutcomeValueIndent,
                ly,
                lw - Outpost_Dialog_UI.OutcomeValueIndent,
                dialogCachedSnapshotItems,
                GetNonItemYieldText(true),
                Color.white);
            if (isTrading)
                TooltipHandler.TipRegion(new Rect(lx, outNowTop, lw, ly - outNowTop), Outpost_Trading.GetDetailedMathTooltip(outpost, dialogCachedSnapshotSkill));
            else if (!string.IsNullOrEmpty(dialogCachedSnapshotOutputFormula))
                TooltipHandler.TipRegion(new Rect(lx, outNowTop, lw, ly - outNowTop), dialogCachedSnapshotOutputFormula);
            else
                TooltipHandler.TipRegion(outNowRect, Tr("TSA_WD_Production_TableHeader_SnapshotTooltip", "Currently assigned skill and the resulting output."));
            ly += Outpost_Dialog_UI.AfterSnapshotGap;

            // Selected-for-this-cycle summary + change timer
            ThingDef cycleDef = outpost.GetProducingDefForCurrentCycle();
            PawnKindDef cycleKind = outpost.GetProducingPawnKindForCurrentCycle();
            string cycleName = cycleKind != null ? cycleKind.LabelCap : (cycleDef?.LabelCap ?? "None");
            Texture2D cycleIcon = cycleKind?.race?.uiIcon ?? cycleDef?.uiIcon;
            if (isTrading) { cycleName = Tr("TSA_WD_Production_Trading", "Trading"); cycleIcon = ThingDefOf.Silver?.uiIcon ?? TexCommand.Replant; }
            if (isScavenging)
            {
                var effKind = outpost.GetProducingScavengingKindForCurrentCycle();
                if (effKind.HasValue)
                {
                    cycleName = Tr("TSA_WD_Production_Scavenging", "Scavenging") + " (" + Outpost_Scavenging.GetKindShortLabel(effKind.Value) + ")";
                    cycleIcon = WorldDomination_UIUtils.UnknownWorldTargetPlaceholderIcon ?? TexCommand.Replant;
                }
                else
                {
                    cycleName = Tr("TSA_WD_Production_NoneLabel", "None");
                    cycleIcon = TexCommand.Replant;
                }
            }

            Widgets.DrawLineHorizontal(lx, ly, lw);
            ly += 6f;
            const float selIconSize = 24f;
            const float selRowH = 46f; // room for a two-line selection label (e.g. scavenging tiers)
            if (cycleIcon != null)
            {
                Rect selIconRect = new Rect(lx, ly, selIconSize, selIconSize);
                GUI.color = cycleKind?.race?.graphicData?.color ?? cycleDef?.graphicData?.color ?? Color.white;
                Widgets.DrawTextureFitted(selIconRect, cycleIcon, 1f);
                GUI.color = Color.white;
            }
            string selectedForCycle = Tr("TSA_WD_Production_SelectedForThisCycle", "Selected for this cycle: " + cycleName, cycleName);
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(lx + selIconSize + 6f, ly, lw - selIconSize - 6f, selRowH), selectedForCycle);
            Text.Anchor = TextAnchor.MiddleLeft;
            ly += selRowH + 2f;

            // If the cycle is locked but the player picked something different, it applies next cycle — call it out.
            if (outpost.IsSelectionLockedForThisCycle && !isTrading)
            {
                string nextName = null;
                Texture2D nextIcon = null;
                Color nextIconColor = Color.white;
                if (isScavenging)
                {
                    var selKindScav = outpost.SelectedScavengingKind;
                    if (selKindScav.HasValue && selKindScav != outpost.GetProducingScavengingKindForCurrentCycle())
                    {
                        nextName = Tr("TSA_WD_Production_Scavenging", "Scavenging") + " (" + Outpost_Scavenging.GetKindShortLabel(selKindScav.Value) + ")";
                        nextIcon = WorldDomination_UIUtils.UnknownWorldTargetPlaceholderIcon ?? TexCommand.Replant;
                    }
                }
                else
                {
                    PawnKindDef selKind = outpost.SelectedPawnKindForHunting;
                    ThingDef selDef = outpost.SelectedProductionDef;
                    if (selKind != null && selKind != cycleKind)
                    {
                        nextName = selKind.LabelCap;
                        nextIcon = selKind.race?.uiIcon;
                        nextIconColor = selKind.race?.graphicData?.color ?? Color.white;
                    }
                    else if (selDef != null && selDef != cycleDef)
                    {
                        nextName = selDef.LabelCap;
                        nextIcon = selDef.uiIcon;
                        nextIconColor = selDef.graphicData?.color ?? Color.white;
                    }
                }

                if (nextName != null)
                {
                    GUI.color = new Color(1f, 0.85f, 0.35f); // amber: pending change
                    if (nextIcon != null)
                    {
                        Rect nextIconRect = new Rect(lx, ly, selIconSize, selIconSize);
                        GUI.color = nextIconColor;
                        Widgets.DrawTextureFitted(nextIconRect, nextIcon, 1f);
                        GUI.color = new Color(1f, 0.85f, 0.35f);
                    }
                    string selectedForNext = Tr("TSA_WD_Production_SelectedForNextCycle", "Selected for next cycle: " + nextName, nextName);
                    Text.Anchor = TextAnchor.UpperLeft;
                    Rect nextRect = new Rect(lx + selIconSize + 6f, ly, lw - selIconSize - 6f, selRowH);
                    Widgets.Label(nextRect, selectedForNext);
                    Text.Anchor = TextAnchor.MiddleLeft;
                    GUI.color = Color.white;
                    TooltipHandler.TipRegion(nextRect, Tr("TSA_WD_Production_SelectedForNextCycleTip", "The current cycle is locked. This selection will start producing at the beginning of the next cycle.", nextName));
                    ly += selRowH + 2f;
                }
            }

            int interval = outpost.ProductionTicksIntervalPublic;
            int lockThreshold = (int)(interval * 0.75f);
            bool changeable = outpost.ProductionTicksLeft > lockThreshold;
            float changeableWindowDays = Mathf.Max(0, interval - lockThreshold) / 60000f;
            string timerLine = changeable
                ? Tr("TSA_WD_Production_SelectionChangeableFor", "Selection changeable for: " + (Mathf.Max(0, outpost.ProductionTicksLeft - lockThreshold) / 60000f).ToString("F1") + " days", (Mathf.Max(0, outpost.ProductionTicksLeft - lockThreshold) / 60000f).ToString("F1"))
                : Tr("TSA_WD_Production_SelectionLocked", "Selection locked for this cycle");
            GUI.color = changeable ? Color.green : Color.gray;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect timerRect = new Rect(lx, ly, lw, lineH);
            Widgets.Label(timerRect, timerLine);
            TooltipHandler.TipRegion(timerRect, Tr("TSA_WD_Production_SelectionChangeWindowTip", "Selection can be changed for the first " + changeableWindowDays.ToString("F1") + " days of a production cycle.", changeableWindowDays.ToString("F1")));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            // ===== RIGHT COLUMN (scroll list) starts at the columns' top =====
            y = columnsTop;

            const float rowPadding = 6f;
            const float itemSearchBarH = 28f;
            const float itemSearchGap = 6f;

            GUI.color = new Color(0.75f, 0.82f, 1f);
            Widgets.Label(new Rect(rightArea.x, y, rightArea.width, 22f), Tr("TSA_WD_Production_ChooseHeader", "Choose production:"));
            GUI.color = Color.white;
            y += 24f;

            if (showItemSearchBar)
            {
                string oldFilter = itemSearchFilter;
                Rect searchFieldRect = new Rect(rightArea.x, y, rightArea.width - 16f, itemSearchBarH);
                itemSearchFilter = Widgets.TextField(searchFieldRect, itemSearchFilter);
                if (itemSearchFilter != oldFilter)
                    scrollPosition = Vector2.zero;
                if (string.IsNullOrEmpty(itemSearchFilter))
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.4f);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Text.Font = GameFont.Tiny;
                    string ph = "TSA_WD_Production_SearchPlaceholder".Translate().ToString();
                    if (ph.Contains("TSA_WD_")) ph = "Filter by name…";
                    Widgets.Label(searchFieldRect, ph);
                    Text.Anchor = TextAnchor.UpperLeft;
                    Text.Font = GameFont.Small;
                    GUI.color = Color.white;
                }
                y += itemSearchBarH + itemSearchGap;
            }

            // Display order: currently selected item first.
            List<CachedHuntingRow> orderedHunt = null;
            List<CachedProductionRow> orderedProd = null;
            if (isHunting)
            {
                orderedHunt = new List<CachedHuntingRow>(cachedHuntingRows.Count);
                foreach (var row in cachedHuntingRows) if (HuntRowIsSelected(row)) orderedHunt.Add(row);
                foreach (var row in cachedHuntingRows) if (!HuntRowIsSelected(row)) orderedHunt.Add(row);
            }
            else
            {
                orderedProd = new List<CachedProductionRow>(cachedProductionRows.Count);
                foreach (var row in cachedProductionRows) if (ProdRowIsSelected(row)) orderedProd.Add(row);
                foreach (var row in cachedProductionRows) if (!ProdRowIsSelected(row)) orderedProd.Add(row);
            }

            float filteredScrollHeight = 8f;
            if (isHunting)
            {
                foreach (var row in orderedHunt)
                    if (HuntingRowMatchesItemSearch(row))
                        filteredScrollHeight += row.RowHeight + rowPadding;
            }
            else
            {
                foreach (var row in orderedProd)
                    if (ProductionRowMatchesItemSearch(row))
                        filteredScrollHeight += row.RowHeight + rowPadding;
            }

            const float nameLabelHeight = Outpost_Dialog_UI.ListRowNameHeight;
            const float formulaTopPadding = Outpost_Dialog_UI.ListRowFormulaTopPadding;
            const float formulaLineHeight = Outpost_Dialog_UI.ListRowFormulaLineHeight;

            float bottomRowH = 36f;
            Rect scrollViewRect = new Rect(rightArea.x, y, rightArea.width, columnsBottom - y);
            Rect viewRect = new Rect(0, 0, rightArea.width - 16f, Mathf.Max(filteredScrollHeight, 1f));
            Widgets.BeginScrollView(scrollViewRect, ref scrollPosition, viewRect);

            float curY = 0;
            if (isHunting)
            {
                float midX = iconColW;

                int visibleHuntingRow = 0;
                foreach (var row in orderedHunt)
                {
                    if (!HuntingRowMatchesItemSearch(row)) continue;

                    float contentW = viewRect.width - midX - listRowRightMargin;

                    Rect rowRect = new Rect(0, curY, viewRect.width, row.RowHeight + rowPadding);
                    if (visibleHuntingRow % 2 == 0) Widgets.DrawHighlight(rowRect);
                    bool isSelected = HuntRowIsSelected(row);
                    Outpost_Dialog_UI.DrawUnmetRequirementsRowTint(rowRect, !row.CanHunt);
                    Outpost_Dialog_UI.DrawSelectedRowTint(rowRect, isSelected);
                    float rowContentY = curY + (rowRect.height - row.RowHeight) / 2f;

                    Texture2D icon = row.Opt.Kind.race?.uiIcon;
                    if (icon == null && row.Opt.Products != null && row.Opt.Products.Count > 0) icon = row.Opt.Products[0].uiIcon;
                    float huntIconSize = 36f;
                    float huntIconY = rowContentY + (row.RowHeight - huntIconSize) / 2f;
                    Rect iconRect = new Rect(iconPadding, huntIconY, huntIconSize, huntIconSize);
                    Color animalColor = row.Opt.Kind.race?.graphicData?.color ?? Color.white;
                    GUI.color = animalColor;
                    if (icon != null) Widgets.DrawTextureFitted(iconRect, icon, 1f);
                    GUI.color = Color.white;

                    Rect nameRect = new Rect(midX, rowContentY, contentW, nameLabelHeight);
                    Widgets.Label(nameRect, row.Opt.Kind.LabelCap);

                    Rect animalClickRect = new Rect(0, rowContentY, midX + contentW, nameLabelHeight);

                    Text.Font = GameFont.Tiny;
                    GUI.color = Color.gray;
                    float lineYBase = rowContentY + nameLabelHeight + formulaTopPadding;
                    const float huntLineStep = 18f;
                    int lineIdx = 0;
                    if (row.ProductLines != null)
                    {
                        foreach (var pl in row.ProductLines)
                        {
                            if (pl.Product == null) continue;
                            float lineY = lineYBase + lineIdx * huntLineStep;
                            Rect productLineRect = new Rect(midX, lineY, contentW, huntLineStep);
                            Widgets.Label(productLineRect, CompactFormulaText(pl.CachedFormulaLine, pl.CachedPrefix));
                            string baseTip = pl.BaselineTooltip ?? "";
                            if (pl.CachedCount == 0 && !string.IsNullOrEmpty(row.AnimalRowTooltip))
                                baseTip = string.IsNullOrEmpty(baseTip) ? row.AnimalRowTooltip : baseTip + "\n\n" + row.AnimalRowTooltip;
                            TooltipHandler.TipRegion(productLineRect, BuildFormulaTooltip(pl.CachedFormulaLine));
                            lineIdx++;
                        }
                    }
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;

                    Outpost_Dialog_UI.FinishSelectableListRow(rowRect, isSelected);
                    if (Widgets.ButtonInvisible(rowRect) && row.CanHunt)
                    {
                        bool huntDeferred = outpost.IsSelectionLockedForThisCycle && outpost.GetProducingPawnKindForCurrentCycle() != row.Opt.Kind;
                        outpost.SetSelectedHuntingAnimal(row.Opt.Kind);
                        if (huntDeferred)
                            Messages.Message("TSA_WD_Production_NextCycle".Translate(), outpost, MessageTypeDefOf.NeutralEvent);
                    }
                    if (!row.CanHunt && !string.IsNullOrEmpty(row.DisabledTooltip))
                        TooltipHandler.TipRegion(rowRect, row.DisabledTooltip);
                    if (Mouse.IsOver(animalClickRect)) Widgets.DrawHighlight(animalClickRect);
                    if (Widgets.ButtonInvisible(animalClickRect))
                        OpenAnimalInfoCard(row.Opt.Kind);
                    TooltipHandler.TipRegion(animalClickRect, row.AnimalRowTooltip ?? "");

                    visibleHuntingRow++;
                    curY += row.RowHeight + rowPadding;
                }
            }
            else
            {
                float midX = iconColW;
                int visibleProductionRow = 0;
                foreach (var row in orderedProd)
                {
                    if (!ProductionRowMatchesItemSearch(row)) continue;

                    float contentW = viewRect.width - midX - listRowRightMargin;

                    Rect rowRect = new Rect(0, curY, viewRect.width, row.RowHeight + rowPadding);
                    if (visibleProductionRow % 2 == 0) Widgets.DrawHighlight(rowRect);
                    bool isSelected = ProdRowIsSelected(row);
                    Outpost_Dialog_UI.DrawUnmetRequirementsRowTint(rowRect, !row.CanProduce);
                    Outpost_Dialog_UI.DrawSelectedRowTint(rowRect, isSelected);
                    float rowContentY = curY + (rowRect.height - row.RowHeight) / 2f;

                    float prodIconSize = 32f;
                    float prodIconY = rowContentY + (row.RowHeight - prodIconSize) / 2f;
                    Rect iconRect = new Rect(iconPadding, prodIconY, prodIconSize, prodIconSize);
                    Color? rowColor = row.UseQuestionMarkIcon
                        ? (Color?)null
                        : row.Def.graphicData?.color ?? (row.IsMiningItem ? Outpost_Mining.GetChunkColor(row.Def) : null);
                    if (rowColor.HasValue) GUI.color = rowColor.Value;
                    Texture2D prodIcon = row.UseQuestionMarkIcon
                        ? WorldDomination_UIUtils.UnknownWorldTargetPlaceholderIcon
                        : row.Def.uiIcon;
                    if (prodIcon != null) Widgets.DrawTextureFitted(iconRect, prodIcon, 1f);
                    if (rowColor.HasValue) GUI.color = Color.white;

                    Rect labelRect = new Rect(midX, rowContentY, contentW, nameLabelHeight);
                    Widgets.Label(labelRect, row.ItemLabel);

                    Rect itemInfoRect = iconRect.ExpandedBy(2f);

                    if (!string.IsNullOrEmpty(row.Formula))
                    {
                        Text.Font = GameFont.Tiny;
                        GUI.color = Color.gray;
                        float formulaY = rowContentY + nameLabelHeight + formulaTopPadding;
                        Rect formulaRect = new Rect(midX, formulaY, contentW, formulaLineHeight);
                        Widgets.Label(formulaRect, CompactFormulaText(row.Formula, row.FormulaPrefix));
                        GUI.color = Color.white;
                        Text.Font = GameFont.Small;

                        TooltipHandler.TipRegion(formulaRect, BuildFormulaTooltip(row.Formula));
                    }

                    Outpost_Dialog_UI.FinishSelectableListRow(rowRect, isSelected);
                    if (Widgets.ButtonInvisible(rowRect) && row.CanProduce)
                    {
                        if (isFishing)
                        {
                            bool fishDeferred = outpost.IsSelectionLockedForThisCycle && outpost.GetProducingFishForCurrentCycle() != row.Def;
                            outpost.SetSelectedFishingFish(row.Def);
                            if (fishDeferred)
                                Messages.Message("TSA_WD_Production_NextCycle".Translate(), outpost, MessageTypeDefOf.NeutralEvent);
                        }
                        else if (row.IsScavengingKind)
                        {
                            bool isDifferent = outpost.IsSelectionLockedForThisCycle
                                               && outpost.GetProducingScavengingKindForCurrentCycle() != row.ScavengingKind;
                            outpost.SetSelectedScavenging(row.ScavengingKind);
                            if (isDifferent)
                                Messages.Message("TSA_WD_Production_NextCycle".Translate(), outpost, MessageTypeDefOf.NeutralEvent);
                        }
                        else
                        {
                            bool prodDeferred = outpost.IsSelectionLockedForThisCycle && outpost.GetProducingDefForCurrentCycle() != row.Def;
                            outpost.SetSelectedProduction(row.Def);
                            if (prodDeferred)
                                Messages.Message("TSA_WD_Production_NextCycle".Translate(), outpost, MessageTypeDefOf.NeutralEvent);
                        }
                    }
                    if (!row.CanProduce && !string.IsNullOrEmpty(row.DisabledTooltip))
                        TooltipHandler.TipRegion(rowRect, row.DisabledTooltip);
                    if (Mouse.IsOver(itemInfoRect)) Widgets.DrawHighlight(itemInfoRect);
                    if (Widgets.ButtonInvisible(itemInfoRect) && !row.IsScavengingKind)
                        Find.WindowStack.Add(new Dialog_InfoCard(row.Def));

                    visibleProductionRow++;
                    curY += row.RowHeight + rowPadding;
                }
            }

            curY += 8f;

            Widgets.EndScrollView();

            const float closeBtnHeight = 40f;
            float clearBtnW = 120f;
            float bottomY = inRect.height - bottomRowH - 4f;
            string clearBtnLabel = "TSA_WD_Production_Clear".Translate().ToString();
            if (clearBtnLabel.Contains("TSA_WD_")) clearBtnLabel = "Reset";
            if (Widgets.ButtonText(new Rect(inRect.width - listRightMargin - clearBtnW, bottomY, clearBtnW, closeBtnHeight), clearBtnLabel))
            {
                string confirmMsg = "TSA_WD_Production_ClearConfirm".Translate().ToString();
                if (confirmMsg.Contains("TSA_WD_")) confirmMsg = "Cancel production? This will reset the production cycle.";
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(confirmMsg, () => { outpost.SetSelectedProduction(null); }));
            }

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        /// <summary>Translate with fallback: returns <paramref name="fallback"/> when the key is missing/untranslated.</summary>
        private static string Tr(string key, string fallback, params NamedArgument[] args)
        {
            string s = (args != null && args.Length > 0) ? key.Translate(args).ToString() : key.Translate().ToString();
            return (s == key || s.Contains("TSA_WD_")) ? fallback : s;
        }

        /// <summary>Draws each produced item on its own line ("count Label"); falls back to a plain text line for non-item outputs. Returns the new y.</summary>
        private float DrawYieldLines(float x, float y, float w, List<ThingDefCountClass> items, string fallbackText, Color color)
            => Outpost_Dialog_UI.DrawOutcomeLines(x, y, w, items, fallbackText, color);

        /// <summary>Single-line yield text for outposts whose output is not a plain item list (trading/scavenging); null otherwise.</summary>
        private string GetNonItemYieldText(bool snapshot)
        {
            float skillVal = snapshot ? dialogCachedSnapshotSkill : dialogCachedAvgCap;
            if (isTrading)
            {
                int silver = snapshot ? dialogCachedTradingSilver : dialogCachedTradingSilverAvg;
                return silver + " " + (ThingDefOf.Silver?.LabelCap.ToString() ?? "Silver");
            }
            if (isScavenging)
                return Outpost_Scavenging.GetYieldSummaryLabel(outpost, skillVal);
            return null;
        }

        private bool ProdRowIsSelected(CachedProductionRow row)
        {
            if (row.IsScavengingKind)
                return outpost.SelectedScavengingKind.HasValue && outpost.SelectedScavengingKind.Value == row.ScavengingKind;
            if (isFishing)
                return row.Def != null && outpost.SelectedFishDef == row.Def;
            return row.Def != null && outpost.SelectedProductionDef == row.Def;
        }

        private bool HuntRowIsSelected(CachedHuntingRow row)
        {
            return row.Opt.Kind != null && outpost.SelectedPawnKindForHunting == row.Opt.Kind;
        }

        private static int MaxVirtualPawnSkill(List<VirtualPawnSummary> pawns, Func<VirtualPawnSummary, int> selector)
        {
            if (pawns == null || pawns.Count == 0) return 0;
            int best = 0;
            for (int i = 0; i < pawns.Count; i++)
            {
                int v = selector(pawns[i]);
                if (v > best) best = v;
            }
            return best;
        }

        private static float GetProductionSkillRawForUi(
            WorldObject_WD_Outpost outpost,
            bool isScavenging,
            bool isMining,
            bool isFarming,
            bool isHunting,
            bool isRanch,
            bool isTrading)
        {
            if (outpost == null || isScavenging) return 0f;
            if (isMining) return outpost.TotalMiningSkillRaw();
            if (isFarming || isHunting || isRanch) return outpost.GetFoodProductionCapacityRaw();
            if (isTrading) return outpost.GetTotalRelevantSkillRaw();
            return outpost.GetTotalRelevantSkillRaw();
        }

        private static string FormatNeedsSkillTip(string skillLabel, int minLevel, int bestLevel)
            => Tr("TSA_WD_Production_Tip_NeedsSkill", "Needs at least one pawn with {0} Lvl {1}. Best pawn has {2}", skillLabel, minLevel, bestLevel);

        private static string FormatNeedsResearchTip(string researchLabel)
            => Tr("TSA_WD_Production_Tip_NeedsResearch", "Research \"{0}\" required", researchLabel);

        private static string FormatNeedsPawnsTip(int minPawns, int havePawns)
            => Tr("TSA_WD_Production_Tip_NeedsPawns", "Needs at least {0} colonists at this outpost. Have {1}", minPawns, havePawns);

        private static string BuildProductionOptionUnmetTooltip(WorldObject_WD_Outpost outpost, ThingDef def, string skillNameFallback)
        {
            var opt = Outpost_Production_Utils.GetProductionOption(outpost, def);
            if (opt == null) return null;
            var tips = new List<string>(2);
            if (!string.IsNullOrEmpty(opt.requiredResearch) && !Outpost_Production_Utils.IsResearchDoneForOption(opt))
            {
                var project = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(opt.requiredResearch);
                string researchLabel = project?.LabelCap ?? opt.requiredResearch;
                tips.Add(FormatNeedsResearchTip(researchLabel));
            }
            if (opt.minSkillLevel > 0)
            {
                SkillDef scaleSkill = Outpost_Production_Utils.GetScalingSkillDefForProduction(outpost, opt);
                int best = scaleSkill != null && outpost?.VirtualPawns != null
                    ? MaxVirtualPawnSkill(outpost.VirtualPawns, vp => vp.GetSkill(scaleSkill))
                    : 0;
                if (best < opt.minSkillLevel)
                {
                    string skillLabel = Outpost_Production_Utils.SkillLabelCap(scaleSkill);
                    if (string.IsNullOrEmpty(skillLabel)) skillLabel = skillNameFallback;
                    tips.Add(FormatNeedsSkillTip(skillLabel, opt.minSkillLevel, best));
                }
            }
            return tips.Count == 0 ? null : string.Join("\n", tips);
        }

        private static void OpenAnimalInfoCard(PawnKindDef kind)
        {
            if (kind == null) return;
            Pawn temp = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, null, PawnGenerationContext.NonPlayer));
            if (temp == null) return;
            Patch_Dialog_InfoCard_PreClose.TempPawnsForInfoCard.Add(temp);
            Find.WindowStack.Add(new Dialog_InfoCard(temp));
        }

    }
}
