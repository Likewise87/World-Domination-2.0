using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KCSG;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class KCSG_Integration_Init
    {
        static KCSG_Integration_Init()
        {
            var harmony = new Harmony("TSA.WorldDomination.KCSG_Final_Solution");

            var genDefMethod = AccessTools.PropertyGetter(typeof(Settlement), "MapGeneratorDef");
            if (genDefMethod != null)
            {
                harmony.Patch(genDefMethod, postfix: new HarmonyMethod(typeof(KCSG_Integration_Patch), nameof(KCSG_Integration_Patch.ForceKCSGGeneratorPostfix)));
            }

            Type customGenType = AccessTools.TypeByName("KCSG.CustomGenOption");
            if (customGenType != null)
            {
                var targetMethod = AccessTools.Method(customGenType, "Generate", new[] { typeof(IntVec3), typeof(Map) });
                if (targetMethod != null)
                {
                    harmony.Patch(targetMethod, prefix: new HarmonyMethod(typeof(KCSG_Integration_Patch), nameof(KCSG_Integration_Patch.LayoutOverridePrefix)));
                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(typeof(KCSG_Integration_Patch), nameof(KCSG_Integration_Patch.CustomGenOptionGeneratePostfix)));
                    harmony.Patch(targetMethod, finalizer: new HarmonyMethod(typeof(KCSG_Integration_Patch), nameof(KCSG_Integration_Patch.LayoutOverrideFinalizer)));
                }
            }

            var pawnGroupMethod = AccessTools.Method(typeof(PawnGroupMakerUtility), nameof(PawnGroupMakerUtility.GeneratePawns),
                new[] { typeof(PawnGroupMakerParms), typeof(bool) });
            if (pawnGroupMethod != null)
            {
                harmony.Patch(pawnGroupMethod, prefix: new HarmonyMethod(typeof(KCSG_Integration_Patch), nameof(KCSG_Integration_Patch.SuppressOutpostDefenseKcsgPawnsPrefix)));
            }

            var postMapMethod = AccessTools.Method(typeof(MapParent), nameof(MapParent.PostMapGenerate));
            if (postMapMethod != null)
            {
                harmony.Patch(postMapMethod, postfix: new HarmonyMethod(typeof(KCSG_Integration_Patch), nameof(KCSG_Integration_Patch.PostMapGenerateUnfogPostfix)));
            }

            // Map-local rock swap: remape layout Marble/Granite/etc. to the map's dominant stone.
            Type symbolUtils = AccessTools.TypeByName("KCSG.SymbolUtils");
            if (symbolUtils != null)
            {
                MethodInfo symbolGenerate = AccessTools.Method(symbolUtils, "Generate", new[]
                {
                    AccessTools.TypeByName("KCSG.SymbolDef"),
                    AccessTools.TypeByName("KCSG.StructureLayoutDef"),
                    typeof(Map),
                    typeof(IntVec3),
                    typeof(Faction),
                    typeof(ThingDef),
                    typeof(ICollection<Thing>)
                });
                if (symbolGenerate != null)
                {
                    harmony.Patch(symbolGenerate,
                        prefix: new HarmonyMethod(typeof(KCSG_Integration_Patch), nameof(KCSG_Integration_Patch.SymbolGenerateRockRemapPrefix)),
                        postfix: new HarmonyMethod(typeof(KCSG_Integration_Patch), nameof(KCSG_Integration_Patch.SymbolGeneratePenAnimalsPostfix)),
                        finalizer: new HarmonyMethod(typeof(KCSG_Integration_Patch), nameof(KCSG_Integration_Patch.SymbolGenerateRockRemapFinalizer)));
                }

                // Private MakeThing path: fill null stuff even when remapper session is inactive
                // (Crashlanded / non-WD KCSG), and drop room wallStuff that WallLamp cannot use.
                MethodInfo generateBuildingAt = AccessTools.Method(symbolUtils, "GenerateBuildingAt", new[]
                {
                    typeof(Map),
                    typeof(IntVec3),
                    AccessTools.TypeByName("KCSG.SymbolDef"),
                    AccessTools.TypeByName("KCSG.StructureLayoutDef"),
                    typeof(Faction),
                    typeof(ICollection<Thing>),
                    typeof(ThingDef)
                });
                if (generateBuildingAt != null)
                {
                    harmony.Patch(generateBuildingAt,
                        prefix: new HarmonyMethod(typeof(KCSG_Integration_Patch), nameof(KCSG_Integration_Patch.GenerateBuildingAtStuffPrefix)));
                }
            }

            MethodInfo setTerrain = AccessTools.Method(typeof(TerrainGrid), nameof(TerrainGrid.SetTerrain),
                new[] { typeof(IntVec3), typeof(TerrainDef) });
            if (setTerrain != null)
            {
                harmony.Patch(setTerrain,
                    prefix: new HarmonyMethod(typeof(KCSG_Integration_Patch), nameof(KCSG_Integration_Patch.SetTerrainRockRemapPrefix)),
                    postfix: new HarmonyMethod(typeof(KCSG_Integration_Patch), nameof(KCSG_Integration_Patch.SetTerrainIndoorWipePostfix)));
            }

            MethodInfo setRoof = AccessTools.Method(typeof(RoofGrid), nameof(RoofGrid.SetRoof),
                new[] { typeof(IntVec3), typeof(RoofDef) });
            if (setRoof != null)
            {
                harmony.Patch(setRoof,
                    postfix: new HarmonyMethod(typeof(KCSG_Integration_Patch), nameof(KCSG_Integration_Patch.SetRoofIndoorWipePostfix)));
            }

            // KCSG Delaunay link-road crashes (OverflowException) when fewer than 3 outdoor doors.
            Type generateRoadResolver = AccessTools.TypeByName("KCSG.SymbolResolver_GenerateRoad");
            MethodInfo generateRoadResolve = generateRoadResolver != null
                ? AccessTools.Method(generateRoadResolver, "Resolve")
                : null;
            if (generateRoadResolve != null)
            {
                harmony.Patch(generateRoadResolve,
                    prefix: new HarmonyMethod(typeof(KCSG_Integration_Patch), nameof(KCSG_Integration_Patch.PreventKcsgRoadDelaunayOverflowPrefix)));
            }

            Type layoutUtils = AccessTools.TypeByName("KCSG.LayoutUtils");
            Type structureLayoutDef = AccessTools.TypeByName("KCSG.StructureLayoutDef");
            if (layoutUtils != null && structureLayoutDef != null)
            {
                MethodInfo layoutGenerate = AccessTools.Method(layoutUtils, "Generate", new[]
                {
                    structureLayoutDef,
                    typeof(CellRect),
                    typeof(Map),
                    typeof(ICollection<Thing>),
                    typeof(Faction),
                    typeof(bool),
                    typeof(Rot4?)
                });
                if (layoutGenerate != null)
                {
                    harmony.Patch(layoutGenerate,
                        prefix: new HarmonyMethod(typeof(KCSG_Integration_Patch), nameof(KCSG_Integration_Patch.LayoutUtilsGeneratePrefix)),
                        postfix: new HarmonyMethod(typeof(KCSG_Integration_Patch), nameof(KCSG_Integration_Patch.LayoutUtilsGeneratePostfix)));
                }
            }

            Type scatterPropsResolver = AccessTools.TypeByName("KCSG.SymbolResolver_ScatterPropsAround");
            MethodInfo scatterPropsResolve = scatterPropsResolver != null
                ? AccessTools.Method(scatterPropsResolver, "Resolve")
                : null;
            if (scatterPropsResolve != null)
            {
                harmony.Patch(scatterPropsResolve,
                    prefix: new HarmonyMethod(typeof(KCSG_Integration_Patch), nameof(KCSG_Integration_Patch.ScatterPropsAroundPrefix)),
                    finalizer: new HarmonyMethod(typeof(KCSG_Integration_Patch), nameof(KCSG_Integration_Patch.ScatterPropsAroundFinalizer)));
            }

            WDVerbose.MsgNoTick("KCSG integration: hooked generator, symbol, terrain, road, and scatter prop guards.");
        }
    }

    public static class KCSG_Integration_Patch
    {
        private const string OutpostDefenseLayoutDefName = "Player_Outpost";
        private static string SettlementLabel(Map map) => map?.Parent?.LabelCap ?? map?.Parent?.def?.defName ?? "unknown-settlement";

        [ThreadStatic] private static bool generatingOutpostDefenseSite;
        [ThreadStatic] private static List<ThingDef> savedScatterPropsDefs;

        public static void ScatterPropsAroundPrefix()
        {
            if (!VFEPropsCompat.IsModLoaded) return;

            SettlementLayoutDef layout = GenOption.settlementLayout;
            PropsOptions props = GenOption.PropsOptions;
            if (layout == null || props == null) return;
            if (layout.GetModExtension<WdSettlementScatterPropsExtension>() == null) return;

            savedScatterPropsDefs = props.scatterPropsDefs;
            props.scatterPropsDefs = WdPropCategoryScatter.BuildScatterList(layout, savedScatterPropsDefs);
        }

        public static void ScatterPropsAroundFinalizer()
        {
            if (savedScatterPropsDefs == null) return;
            PropsOptions props = GenOption.PropsOptions;
            if (props != null)
                props.scatterPropsDefs = savedScatterPropsDefs;
            savedScatterPropsDefs = null;
        }

        public static void ForceKCSGGeneratorPostfix(Settlement __instance, ref MapGeneratorDef __result)
        {
            if (__instance == null || __instance.Faction == null || __instance.Faction.def == null || __instance.Faction.IsPlayer) return;
            if (!WorldActions_Utils.IsWdBaseGenEligible(__instance.Faction)) return;
            if (!WorldActions_Utils.IsWdSurfaceTile(__instance.Tile)) return;
            if (WorksitesExpandedCompat.ShouldSkipWdKcsgInterference(__instance)) return;

            string label = __instance.LabelCap;

            if (WorldActions_Utils.HasActiveQuest(__instance))
            {
                LogKcsgHijackDecision(label, hijacked: false, "active quest");
                return;
            }

            if (__instance.GetComponent<CompViralSpread>() == null)
            {
                LogKcsgHijackDecision(label, hijacked: false, "not a WD settlement");
                return;
            }

            try
            {
                var kcsgGen = DefDatabase<MapGeneratorDef>.GetNamed("KCSG_Base_Faction", false);
                if (kcsgGen == null)
                {
                    LogKcsgHijackDecision(label, hijacked: false, "KCSG_Base_Faction def missing");
                    return;
                }

                __result = kcsgGen;
                EnsureKcsgCustomGenOption(__instance.Faction.def);
                LogKcsgHijackDecision(label, hijacked: true, __instance.Faction.Name);
            }
            catch (Exception ex)
            {
                LogKcsgHijackDecision(label, hijacked: false, $"error: {ex.Message}");
            }
        }

        private static void LogKcsgHijackDecision(string settlementLabel, bool hijacked, string detail)
        {
            WDVerbose.MsgNoTick($"KCSG hijack {(hijacked ? "yes" : "no")} for {settlementLabel} ({detail})");
        }

        public static void LayoutOverridePrefix(object __instance, IntVec3 loc, Map map)
        {
            if (map?.Parent == null) return;
            if (WorksitesExpandedCompat.ShouldSkipWdKcsgInterference(map)) return;
            // Outpost defense always uses WD layouts regardless of the settlement-attack toggle.
            if (IsOutpostDefenseSite(map.Parent))
            {
                generatingOutpostDefenseSite = true;
                ConfigureOutpostDefenseLayout(__instance, map, loc);
                KcsgRockTypeRemapper.Begin(map);
                WdLayoutSpawnCellTracker.Begin(map);
                return;
            }

            if (!(map.Parent is Settlement settlement)) return;
            if (settlement.Faction == null || settlement.Faction.IsPlayer) return;
            if (!WorldActions_Utils.IsWdBaseGenEligible(settlement.Faction)) return;
            if (!WorldActions_Utils.IsWdSurfaceTile(settlement.Tile)) return;

            // QUEST EXCLUSION
            if (WorldActions_Utils.HasActiveQuest(settlement))
            {
                WDVerbose.MsgNoTick($"KCSG layout override skipped for {settlement.LabelCap}: active quest");
                return;
            }

            var spreadComp = settlement.GetComponent<CompViralSpread>();
            if (spreadComp == null) return;

            Type genType = __instance.GetType();

            // Set basic KCSG flags
            FieldInfo bridgeField = AccessTools.Field(genType, "preventBridgeable");
            if (bridgeField != null) bridgeField.SetValue(__instance, false);

            FieldInfo randomLocField = AccessTools.Field(genType, "spawnInRandomFreeLocation");
            if (randomLocField != null) randomLocField.SetValue(__instance, false);

            bool isTribal = settlement.Faction.def.techLevel <= TechLevel.Medieval;
            string techPrefix = isTribal ? "Tribal" : "Generic";
            string tier = spreadComp.tier.ToString();
            string baseType = (spreadComp.tier == SettlementTier.T4) ? "Citadel" : spreadComp.subType;

            string specificPattern = $"TSA_{techPrefix}_{tier}_{baseType}";
            string fallbackPattern = $"TSA_{techPrefix}_{tier}";

            Type layoutDefType = AccessTools.TypeByName("KCSG.SettlementLayoutDef");
            if (layoutDefType == null) return;

            Type dbType = typeof(DefDatabase<>).MakeGenericType(layoutDefType);
            var allLayouts = (IEnumerable<Def>)AccessTools.Property(dbType, "AllDefs").GetValue(null);
            if (allLayouts == null) return;

            var validLayouts = allLayouts.Where(d => d != null && d.defName.StartsWith(specificPattern)).ToList();
            if (!validLayouts.Any())
            {
                validLayouts = allLayouts.Where(d => d != null && d.defName.StartsWith(fallbackPattern)).ToList();
            }

            if (validLayouts.Any())
            {
                var chosen = validLayouts.RandomElement();
                if (chosen != null)
                {
                    // --- SURGICAL INJECTION: Apply Garrison Multiplier from Settings ---
                    var s = WorldDominationMod.settings;
                    float targetMult = 1f;

                    if (isTribal)
                    {
                        if (spreadComp.tier == SettlementTier.T1) targetMult = s.kcsgMultTribalT1;
                        else if (spreadComp.tier == SettlementTier.T2) targetMult = s.kcsgMultTribalT2;
                        else if (spreadComp.tier == SettlementTier.T3) targetMult = s.kcsgMultTribalT3;
                        else if (spreadComp.tier == SettlementTier.T4) targetMult = s.kcsgMultTribalT4;
                    }
                    else
                    {
                        if (spreadComp.tier == SettlementTier.T1) targetMult = s.kcsgMultGenericT1;
                        else if (spreadComp.tier == SettlementTier.T2) targetMult = s.kcsgMultGenericT2;
                        else if (spreadComp.tier == SettlementTier.T3) targetMult = s.kcsgMultGenericT3;
                        else if (spreadComp.tier == SettlementTier.T4) targetMult = s.kcsgMultGenericT4;
                    }

                    // --- DYNAMIC SCALING: weaken the garrison for a depleted settlement (tier layout unchanged). ---
                    // A settlement at full offensive strength fields 100% of the configured tier multiplier; a depleted
                    // one fields proportionally less, but never below the configured floor.
                    float offMax = CompViralSpread.GetStrengthRange(spreadComp.tier).max;
                    float offRatio = offMax > 0f ? spreadComp.offensiveStrength / offMax : 1f;
                    if (offRatio < 0f) offRatio = 0f;
                    else if (offRatio > 1f) offRatio = 1f;

                    float minScale = s.garrisonOffensiveStrengthMinScale;
                    if (minScale < 0f) minScale = 0f;
                    else if (minScale > 1f) minScale = 1f;

                    float dynamicScale = offRatio > minScale ? offRatio : minScale;
                    targetMult *= dynamicScale;

                    var spreadManager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
                    if (spreadManager != null && WdEscalation.IsMidOrLate(spreadManager) && s.enableLateGameScaling)
                    {
                        float boost = WdEscalation.GetGarrisonBoostPct(s, spreadManager.cachedEscalationStage);
                        if (boost < 0f) boost = 0f;
                        targetMult *= 1f + boost;
                    }

                    WDVerbose.MsgNoTick($"KCSG layout plan settlement={settlement.LabelCap} layout={chosen.defName} tier={spreadComp.tier} subtype={spreadComp.subType} garrison off={spreadComp.offensiveStrength:F0}/{offMax:F0} ratio={offRatio:P0} floor={minScale:P0} finalMult={targetMult:F2}");

                    // Drill down into DefenseOptions -> pawnGroupMultiplier
                    FieldInfo defenseField = AccessTools.Field(chosen.GetType(), "defenseOptions");
                    if (defenseField != null)
                    {
                        object defenseObj = defenseField.GetValue(chosen);
                        if (defenseObj != null)
                        {
                            FieldInfo multField = AccessTools.Field(defenseObj.GetType(), "pawnGroupMultiplier");
                            multField?.SetValue(defenseObj, targetMult);
                        }
                    }

                    EnsureSettlementRoads(chosen);
                    RecordPendingSettlementRect(chosen, loc, map);

                    // Finalize the choice for KCSG
                    FieldInfo listField = AccessTools.Field(genType, "chooseFromSettlements");
                    if (listField != null)
                    {
                        var newList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(layoutDefType));
                        newList.Add(chosen);
                        listField.SetValue(__instance, newList);
                    }

                    ApplyAdaptiveTerrainPrep(__instance, genType, chosen, loc, map, settlement);
                    KcsgRockTypeRemapper.Begin(map);
                    WdLayoutSpawnCellTracker.Begin(map);
                    WDVerbose.MsgNoTick($"KCSG layout chosen settlement={settlement.LabelCap} layout={chosen.defName} loc={loc}");
                }
            }
        }

        public static void LayoutUtilsGeneratePrefix(KCSG.StructureLayoutDef layout)
        {
            KcsgRockTypeRemapper.BeginLayout(layout?.defName, layout?.tags);
            KcsgRockTypeRemapper.RerollCropForLayout();
            KcsgRockTypeRemapper.RerollOresForLayout();
        }

        public static void LayoutUtilsGeneratePostfix(KCSG.StructureLayoutDef layout, CellRect rect, Map map)
        {
            WdLayoutSpawnCellTracker.RecordGenerateRect(map, rect);
            KcsgRockTypeRemapper.EnsureBuildableFloorsUnderLayout(map, rect);
            KcsgRockTypeRemapper.EndLayout(map, rect);
        }

        private const float BlendPerlinScale = 0.07f;
        private const float BlendPerlinJitter = 0.75f;
        private const float BlendPassThreshold = 0.32f;
        private const float BlendFalloffPower = 0.6f;

        /// <summary>
        /// If too much of the layout is unbuildable (or Always clear is on), convert blocked cells only
        /// and optionally bleed that convert + filth/chunk wipe outward with a noisy falloff.
        /// </summary>
        private static void ApplyAdaptiveTerrainPrep(object generator, Type genType, Def chosenLayout, IntVec3 loc, Map map, Settlement settlement)
        {
            var s = WorldDominationMod.settings;
            if (s == null || !s.kcsgAdaptiveTerrainPrep) return;
            if (generator == null || genType == null || chosenLayout == null || map == null) return;

            FieldInfo sizeField = AccessTools.Field(chosenLayout.GetType(), "settlementSize");
            if (sizeField == null) return;
            IntVec2 size = (IntVec2)sizeField.GetValue(chosenLayout);
            if (size.x <= 0 || size.z <= 0) return;

            int maxDim = Mathf.Max(size.x, size.z);
            int blendReach = Mathf.Clamp(Mathf.RoundToInt(maxDim * 0.1f), 5, 15);

            CellRect rect = CellRect.CenteredOn(loc, size.x, size.z);
            rect = rect.ClipInsideMap(map);
            int total = 0;
            int blocked = 0;
            foreach (IntVec3 c in rect)
            {
                total++;
                if (IsCellBlockedForSettlement(c, map))
                    blocked++;
            }

            if (total <= 0) return;

            float blockedFraction = (float)blocked / total;
            float threshold = Mathf.Clamp01(s.kcsgBlockedFlattenThreshold);
            bool flatten = s.experimentalAlwaysClearKcsgRect || blockedFraction > threshold;
            bool blend = flatten && s.experimentalKcsgRectBlend;

            FieldInfo preClearField = AccessTools.Field(genType, "preGenClear");
            FieldInfo fullClearField = AccessTools.Field(genType, "fullClear");
            fullClearField?.SetValue(generator, false);

            if (!flatten)
            {
                preClearField?.SetValue(generator, false);
            }
            else
            {
                preClearField?.SetValue(generator, !blend);
                TerrainDef floor = KcsgRockTypeRemapper.FindMostCommonBuildableTerrain(map);
                if (blend)
                {
                    CellRect area = rect.ExpandedBy(blendReach).ClipInsideMap(map);
                    foreach (IntVec3 c in area)
                    {
                        if (!c.InBounds(map)) continue;
                        if (!PassesBlendMask(c, rect, blendReach)) continue;
                        WipeFilthAndChunksAt(c, map);
                        FlattenBlockedCell(c, map, floor);
                    }
                }
                else
                {
                    foreach (IntVec3 c in rect)
                    {
                        if (!c.InBounds(map)) continue;
                        FlattenBlockedCell(c, map, floor);
                    }
                }
            }

            string label = settlement?.LabelCap ?? "settlement";
            string mode = !flatten ? "keep" : blend ? "flatten+blend" : "flatten";
            WDVerbose.MsgNoTick(
                $"KCSG terrain prep settlement={label} layout={chosenLayout?.defName ?? "?"} loc={loc} "
                + $"rect={rect} blocked={blocked}/{total} ({blockedFraction:P0}) threshold={threshold:P0} mode={mode} "
                + $"floor={(KcsgRockTypeRemapper.FindMostCommonBuildableTerrain(map)?.defName ?? "none")}");
        }

        private static bool PassesBlendMask(IntVec3 c, CellRect rect, int blendReach)
        {
            float dist = EuclideanDistanceToRect(c, rect);
            if (dist <= 0.001f) return true;
            if (dist > blendReach) return false;
            float falloff = Mathf.Pow(1f - dist / blendReach, BlendFalloffPower);
            float n = Mathf.PerlinNoise(c.x * BlendPerlinScale, c.z * BlendPerlinScale);
            return falloff + (n - 0.5f) * BlendPerlinJitter > BlendPassThreshold;
        }

        private static float EuclideanDistanceToRect(IntVec3 c, CellRect rect)
        {
            float nx = Mathf.Clamp(c.x, rect.minX, rect.maxX);
            float nz = Mathf.Clamp(c.z, rect.minZ, rect.maxZ);
            float dx = c.x - nx;
            float dz = c.z - nz;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static void FlattenBlockedCell(IntVec3 c, Map map, TerrainDef floor)
        {
            ClearNaturalRoofAt(c, map);
            if (!IsCellBlockedForSettlement(c, map)) return;
            DestroyNaturalRockAt(c, map);
            if (floor != null)
                map.terrainGrid.SetTerrain(c, floor);
        }

        /// <summary>Rock roof (thin), overhead mountain, and any other natural roof left after terrain prep.</summary>
        private static void ClearNaturalRoofAt(IntVec3 c, Map map)
        {
            if (!c.InBounds(map)) return;
            RoofDef roof = map.roofGrid.RoofAt(c);
            if (roof != null && roof.isNatural)
                map.roofGrid.SetRoof(c, null);
        }

        private static void DestroyNaturalRockAt(IntVec3 c, Map map)
        {
            List<Thing> things = c.GetThingList(map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                Thing t = things[i];
                if (t == null || t.Destroyed) continue;
                if (t is Mineable || (t.def?.building != null && t.def.building.isNaturalRock))
                    t.Destroy();
            }
        }

        private static void WipeFilthAndChunksAt(IntVec3 c, Map map)
        {
            List<Thing> things = c.GetThingList(map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                Thing t = things[i];
                if (t == null || t.Destroyed) continue;
                if (t is Pawn) continue;
                if (t.def.category == ThingCategory.Plant) continue;
                if (t is Mineable) continue;
                if (t.def.building != null && t.def.building.isNaturalRock) continue;
                bool filth = t.def.category == ThingCategory.Filth;
                bool building = t.def.category == ThingCategory.Building;
                bool chunk = t.def.thingCategories != null && t.def.thingCategories.Contains(ThingCategoryDefOf.Chunks);
                if (filth || building || chunk)
                    t.Destroy();
            }
        }

        private static bool IsCellBlockedForSettlement(IntVec3 c, Map map)
        {
            if (!c.InBounds(map)) return true;
            if (!c.Walkable(map)) return true;

            RoofDef roof = map.roofGrid.RoofAt(c);
            if (roof != null && roof.isNatural) return true;

            TerrainDef terrain = c.GetTerrain(map);
            if (!KcsgRockTypeRemapper.IsTerrainBuildableForSettlement(terrain)) return true;

            List<Thing> things = c.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t == null) continue;
                if (t is Mineable) return true;
                if (t.def?.building != null && t.def.building.isNaturalRock) return true;
            }

            return false;
        }

        public static void LayoutOverrideFinalizer()
        {
            generatingOutpostDefenseSite = false;
            KcsgRockTypeRemapper.End();
            WdLayoutSpawnCellTracker.End();
        }

        /// <summary>
        /// Without Combat Extended, Turret_M240B symbols are skipped so sandbags/wire still spawn.
        /// </summary>
        public static bool SymbolGenerateRockRemapPrefix(object __0, Map __2, IntVec3 __3, ref bool __state)
        {
            __state = false;
            if (ShouldSkipCeMgTurretSymbol(__0))
                return false;

            if (KcsgRockTypeRemapper.SessionActive)
            {
                ThingDef originalThing = KcsgRockTypeRemapper.GetSymbolThingDef(__0);
                KcsgRockTypeRemapper.PrepareSymbolVerification(originalThing, __3);
                __state = KcsgRockTypeRemapper.PushAndRemapSymbol(__0);
            }
            else
            {
                // Plan default-stuff must not depend on remapper Begin — player Crashlanded
                // (and other non-WD KCSG) spawn WallLamp_* with null stuff and never start a session.
                KcsgRockTypeRemapper.EnsureDefaultStuffIfNeeded(__0);
            }
            if (!KcsgRockTypeRemapper.SessionActive) return true;
            ThingDef thing = KcsgRockTypeRemapper.GetSymbolThingDef(__0);
            if (KcsgRockTypeRemapper.IsLayoutCropPlant(thing))
                KcsgRockTypeRemapper.EnsureFertileUnderCrop(__2, __3);
            return true;
        }

        private static bool ShouldSkipCeMgTurretSymbol(object symbol)
        {
            if (symbol == null) return false;
            if (ModsConfig.IsActive("CETeam.CombatExtended")) return false;

            if (symbol is Def def && def.defName != null && def.defName.StartsWith("Turret_M240B", StringComparison.Ordinal))
                return true;

            ThingDef thing = KcsgRockTypeRemapper.GetSymbolThingDef(symbol);
            return thing?.defName != null && thing.defName.StartsWith("Turret_M240B", StringComparison.Ordinal);
        }

        /// <summary>
        /// Prefix for KCSG.SymbolUtils.GenerateBuildingAt — args: map, cell, symbol, layout, faction, spawnedThings, wallStuff.
        /// </summary>
        public static void GenerateBuildingAtStuffPrefix(object __2, ref ThingDef __6)
        {
            KcsgRockTypeRemapper.EnsureDefaultStuffIfNeeded(__2);
            KcsgRockTypeRemapper.ClearIncompatibleWallStuff(__2, ref __6);
        }

        public static void SymbolGenerateRockRemapFinalizer(bool __state)
        {
            if (__state)
                KcsgRockTypeRemapper.PopSymbolRestore();
        }

        public static void SymbolGeneratePenAnimalsPostfix(object __0, Map __2, IntVec3 __3)
        {
            KcsgRockTypeRemapper.CompleteSymbolVerification(__2, __3);
            KcsgRockTypeRemapper.ApplyRandomCropGrowth(__2, __3);
            KcsgRockTypeRemapper.ProtectLayoutDebrisAfterSymbolSpawn(__0, __2, __3);
            if (!KcsgRockTypeRemapper.SessionActive) return;
            ThingDef thing = KcsgRockTypeRemapper.GetSymbolThingDef(__0);
            if (KcsgRockTypeRemapper.IsLayoutNaturalRockOrMineable(thing))
                KcsgRockTypeRemapper.ApplyRoughHewnHalo(__2, __3);
            if (!KcsgRockTypeRemapper.PenLivestockEnabled) return;
            if (KcsgRockTypeRemapper.IsPenMarker(thing))
                KcsgRockTypeRemapper.TrySpawnPenAnimals(__2, __3);
        }

        public static void SetTerrainRockRemapPrefix(ref TerrainDef newTerr)
        {
            if (newTerr == null) return;

            if (KcsgRockTypeRemapper.Active)
            {
                TerrainDef rockRemapped = KcsgRockTypeRemapper.RemapTerrain(newTerr);
                if (rockRemapped != null && rockRemapped != newTerr)
                    newTerr = rockRemapped;
            }

            if (KcsgRockTypeRemapper.SessionActive)
            {
                TerrainDef farmRemapped = KcsgRockTypeRemapper.RemapFarmTerrain(newTerr);
                if (farmRemapped != null && farmRemapped != newTerr)
                    newTerr = farmRemapped;
            }
        }

        public static void SetTerrainIndoorWipePostfix(IntVec3 c, TerrainDef newTerr)
        {
            if (!KcsgRockTypeRemapper.SessionActive || newTerr == null) return;
            if (!KcsgRockTypeRemapper.IsLayoutPlacedFloor(newTerr)) return;
            KcsgRockTypeRemapper.WipeIndoorCellDebris(c);
        }

        public static void SetRoofIndoorWipePostfix(IntVec3 c, RoofDef def)
        {
            if (!KcsgRockTypeRemapper.SessionActive) return;
            if (!KcsgRockTypeRemapper.IsLayoutPlacedRoof(def)) return;
            KcsgRockTypeRemapper.WipeIndoorCellDebris(c);
        }

        /// <summary>
        /// KCSG's Delaunay ctor does <c>new int[(2*n-5)*3]</c> and only skips when outdoor doors == 0.
        /// With 1–2 unique outdoor doors that size is negative → OverflowException. Skip link roads instead.
        /// </summary>
        public static bool PreventKcsgRoadDelaunayOverflowPrefix()
        {
            try
            {
                Type genUtils = AccessTools.TypeByName("KCSG.SettlementGenUtils");
                FieldInfo doorsField = genUtils != null ? AccessTools.Field(genUtils, "doors") : null;
                var doors = doorsField?.GetValue(null) as List<IntVec3>;
                if (doors == null || doors.Count < 3)
                {
                    WDVerbose.MsgNoTick($"Skipping kcsg_generateroad: tracked doors={doors?.Count ?? 0} (need ≥3 unique outdoor).");
                    return false;
                }

                Map map = RimWorld.BaseGen.BaseGen.globalSettings?.map;
                if (map == null) return false;

                var outdoor = new HashSet<IntVec3>();
                foreach (IntVec3 door in doors)
                {
                    if (!door.InBounds(map)) continue;
                    if (!IsOutdoorLinkableDoorCell(door, map)) continue;
                    outdoor.Add(door);
                    if (outdoor.Count >= 3) return true;
                }

                WDVerbose.MsgNoTick($"Skipping kcsg_generateroad: unique outdoor link doors={outdoor.Count} (Delaunay needs ≥3).");
                return false;
            }
            catch (Exception e)
            {
                WDVerbose.MsgNoTick($"kcsg_generateroad guard failed ({e.Message}); skipping roads.");
                return false;
            }
        }

        /// <summary>Mirrors KCSG SymbolResolver_GenerateRoad outdoor-door filter.</summary>
        private static bool IsOutdoorLinkableDoorCell(IntVec3 door, Map map)
        {
            foreach (IntVec3 adj in GenAdjFast.AdjacentCellsCardinal(door))
            {
                if (!adj.InBounds(map)) continue;
                if (adj.UsesOutdoorTemperature(map)) return true;
                // Mineable presence also qualifies in KCSG; keep parity when possible.
                if (adj.GetFirstMineable(map) != null) return true;
            }
            return false;
        }

        public static void PostMapGenerateUnfogPostfix(MapParent __instance)
        {
            Map map = __instance?.Map;
            if (map == null) return;

            try
            {
                WdSettlementMapUnfog.TryResolveSettlementRect(map, out CellRect settlementRect);
                WdSettlementMapUnfog.UnfogKcsgSettlement(map);
                WdSettlementRockInteriorFog.Apply(map, settlementRect);
            }
            finally
            {
                WdSettlementMapUnfog.ClearPendingRect(map);
                WdLayoutSpawnCellTracker.Clear(map);
            }
        }

        public static void CustomGenOptionGeneratePostfix(IntVec3 loc, Map map)
        {
            // After all structures/roads/scatter: re-drop rock chunks under floors/roofs are common.
            // Session still active here (finalizer Ends the remapper after this postfix).
            KcsgRockTypeRemapper.WipeDebrisOnAllTrackedIndoorCells(map);

            if (IsOutpostDefenseSite(map?.Parent))
            {
                IntVec3 center = WD_OutpostDefenseMapUtility.ResolveKcsgSettlementCenter(loc);
                WD_OutpostDefenseMapUtility.RecordSettlementCenter(map, center);
                WDVerbose.MsgNoTick($"KCSG outpost defense center settlement={SettlementLabel(map)} generateLoc={loc} settlementCenter={center} mapCenter={map.Center}.");

                return;
            }

            // NPC settlement attack maps: WD MG nests, force power, shelf loot, turret silence.
            var settings = WorldDominationMod.settings;
            WdMgNestSpawner.TrySpawn(map);
            if (settings != null && settings.kcsgForceSettlementPower)
                WdSettlementMapPower.ForceSettlementMapPowered(map);
            WdSettlementLootFiller.FillShelves(map);
            if (settings != null && settings.kcsgSilenceDefeatedTurrets)
                WdSettlementTurretSilence.EnsureOnMap(map);
        }

        public static bool SuppressOutpostDefenseKcsgPawnsPrefix(PawnGroupMakerParms parms, ref IEnumerable<Pawn> __result)
        {
            if (!generatingOutpostDefenseSite) return true;
            if (parms.groupKind != PawnGroupKindDefOf.Settlement) return true;

            __result = Enumerable.Empty<Pawn>();
            return false;
        }

        public static bool EnsureKcsgCustomGenOption(FactionDef factionDef)
        {
            if (factionDef == null) return false;

            Type extensionType = AccessTools.TypeByName("KCSG.CustomGenOption");
            if (extensionType == null) return false;

            if (factionDef.modExtensions == null)
                factionDef.modExtensions = new List<DefModExtension>();

            if (factionDef.modExtensions.Any(ex => ex != null && extensionType.IsAssignableFrom(ex.GetType())))
                return true;

            try
            {
                var newExtension = (DefModExtension)Activator.CreateInstance(extensionType);
                FieldInfo listField = AccessTools.Field(extensionType, "chooseFromSettlements");
                if (listField != null)
                {
                    Type layoutDefType = AccessTools.TypeByName("KCSG.SettlementLayoutDef");
                    if (layoutDefType != null)
                    {
                        var emptyList = Activator.CreateInstance(typeof(List<>).MakeGenericType(layoutDefType));
                        listField.SetValue(newExtension, emptyList);
                    }
                }
                factionDef.modExtensions.Add(newExtension);
                return true;
            }
            catch (Exception ex)
            {
                WDVerbose.MsgNoTick($"KCSG custom generation option setup failed for {factionDef.defName}: {ex.Message}");
                return false;
            }
        }

        private static bool IsOutpostDefenseSite(MapParent parent)
        {
            return parent?.def?.defName == "TSA_WD_OutpostDefenseSite";
        }

        private static void ConfigureOutpostDefenseLayout(object generator, Map map, IntVec3 loc)
        {
            if (generator == null || map == null) return;

            Type genType = generator.GetType();
            FieldInfo bridgeField = AccessTools.Field(genType, "preventBridgeable");
            if (bridgeField != null) bridgeField.SetValue(generator, false);

            FieldInfo tryFindField = AccessTools.Field(genType, "tryFindFreeArea");
            if (tryFindField != null) tryFindField.SetValue(generator, false);

            Type layoutDefType = AccessTools.TypeByName("KCSG.SettlementLayoutDef");
            if (layoutDefType == null) return;

            Type dbType = typeof(DefDatabase<>).MakeGenericType(layoutDefType);
            var allLayouts = (IEnumerable<Def>)AccessTools.Property(dbType, "AllDefs").GetValue(null);
            if (allLayouts == null) return;

            Def chosen = allLayouts.FirstOrDefault(d => d != null && d.defName == OutpostDefenseLayoutDefName);
            if (chosen == null)
            {
                var fallbackLayouts = allLayouts
                    .Where(d => d != null && (d.defName.StartsWith("TSA_Generic_T1") || d.defName.StartsWith("TSA_Tribal_T1")))
                    .ToList();
                if (!fallbackLayouts.Any())
                {
                    WDVerbose.MsgNoTick($"KCSG outpost defense layout {OutpostDefenseLayoutDefName} missing; no fallback T1 layout found.");
                    return;
                }

                chosen = fallbackLayouts.RandomElement();
                WDVerbose.MsgNoTick($"KCSG outpost defense falling back to {chosen.defName} ({OutpostDefenseLayoutDefName} not loaded).");
            }

            if (chosen == null) return;

            // The defense encounter already spawns the real outpost defenders and the incoming raid.
            FieldInfo defenseField = AccessTools.Field(chosen.GetType(), "defenseOptions");
            if (defenseField != null)
            {
                object defenseObj = defenseField.GetValue(chosen);
                if (defenseObj != null)
                {
                    Type defenseType = defenseObj.GetType();
                    FieldInfo multField = AccessTools.Field(defenseType, "pawnGroupMultiplier");
                    multField?.SetValue(defenseObj, 0f);
                    SetDefenseBool(defenseType, defenseObj, "addTurrets", false);
                    SetDefenseBool(defenseType, defenseObj, "addMortars", false);
                    SetDefenseBool(defenseType, defenseObj, "addEdgeDefense", false);
                    SetDefenseBool(defenseType, defenseObj, "addSandbags", false);
                }
            }

            EnsureSettlementRoads(chosen);
            RecordPendingSettlementRect(chosen, loc, map);

            FieldInfo listField = AccessTools.Field(genType, "chooseFromSettlements");
            if (listField != null)
            {
                var newList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(layoutDefType));
                newList.Add(chosen);
                listField.SetValue(generator, newList);
            }

            WDVerbose.MsgNoTick($"KCSG outpost defense site using layout {chosen.defName}.");

            ApplyAdaptiveTerrainPrep(generator, genType, chosen, loc, map, null);
        }

        private static void SetDefenseBool(Type defenseType, object defenseObj, string fieldName, bool value)
        {
            FieldInfo field = AccessTools.Field(defenseType, fieldName);
            if (field != null && field.FieldType == typeof(bool))
                field.SetValue(defenseObj, value);
        }

        private static void RecordPendingSettlementRect(Def chosenLayout, IntVec3 loc, Map map)
        {
            if (chosenLayout == null || map == null) return;

            FieldInfo sizeField = AccessTools.Field(chosenLayout.GetType(), "settlementSize");
            if (sizeField == null) return;

            IntVec2 size = (IntVec2)sizeField.GetValue(chosenLayout);
            if (size.x <= 0 || size.z <= 0) return;

            CellRect rect = CellRect.CenteredOn(loc, size.x, size.z).ClipInsideMap(map);
            WdSettlementMapUnfog.RecordPendingRect(map, rect);
        }

        private static void EnsureSettlementRoads(Def chosenLayout)
        {
            if (chosenLayout == null) return;

            Type layoutType = chosenLayout.GetType();
            FieldInfo roadField = AccessTools.Field(layoutType, "roadOptions");
            if (roadField == null) return;

            Type roadType = roadField.FieldType;
            object roadObj = roadField.GetValue(chosenLayout);
            if (roadObj == null && roadType != null)
            {
                roadObj = Activator.CreateInstance(roadType);
                roadField.SetValue(chosenLayout, roadObj);
            }

            if (roadObj == null) return;

            string terrainName = UsesConcreteRoads(chosenLayout.defName) ? "Concrete" : "PackedDirt";
            SetRoadBool(roadObj, "addMainRoad", false);
            SetRoadBool(roadObj, "addLinkRoad", true);
            SetRoadTerrain(roadObj, "linkRoadDef", terrainName);
        }

        private static bool UsesConcreteRoads(string defName) =>
            defName != null && (defName.StartsWith("TSA_Generic_T3") || defName.StartsWith("TSA_Generic_T4"));

        private static void SetRoadBool(object roadObj, string fieldName, bool value)
        {
            FieldInfo field = AccessTools.Field(roadObj.GetType(), fieldName);
            if (field != null && field.FieldType == typeof(bool))
                field.SetValue(roadObj, value);
        }

        private static void SetRoadInt(object roadObj, string fieldName, int value)
        {
            FieldInfo field = AccessTools.Field(roadObj.GetType(), fieldName);
            if (field != null && field.FieldType == typeof(int))
                field.SetValue(roadObj, value);
        }

        private static void SetRoadTerrain(object roadObj, string fieldName, string terrainDefName)
        {
            FieldInfo field = AccessTools.Field(roadObj.GetType(), fieldName);
            if (field == null) return;

            if (field.FieldType == typeof(TerrainDef))
                field.SetValue(roadObj, TerrainDef.Named(terrainDefName));
            else if (field.FieldType == typeof(string))
                field.SetValue(roadObj, terrainDefName);
        }
    }
}