using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public enum WD_RadiusOverlayCategory : byte
    {
        NpcSettlement = 0,
        FoodProducer = 1,
        People = 2,
        Warehouse = 3,
        Mortar = 4,
        /// <summary>Rapid response and other player outposts without a type-primary radius.</summary>
        RapidResponse = 5,
        /// <summary>T4 NPC settlements (Attack / Ally / Mortar / AA). Separate from lower-tier NPCs.</summary>
        NpcSettlementT4 = 6,
        /// <summary>AT Turret attack-range overlay (Attack / Off). Player-owned for now; NPCs may get placement later.</summary>
        AtTurret = 7
    }

    public enum WD_RadiusOverlayKind : byte
    {
        Off = 0,
        Attack = 1,
        Ally = 2,
        Mortar = 3,
        AA = 4,
        Food = 5,
        People = 6,
        Warehouse = 7
    }

    /// <summary>Global-per-category radius overlay toggle state (mod settings).</summary>
    /// <remarks>Caches a <see cref="Material"/>; must load on the main thread.</remarks>
    [StaticConstructorOnStartup]
    public static class WD_RadiusOverlayPrefs
    {
        public static WD_RadiusOverlayKind npcSettlement = WD_RadiusOverlayKind.Attack;
        public static WD_RadiusOverlayKind npcSettlementT4 = WD_RadiusOverlayKind.Attack;
        public static WD_RadiusOverlayKind foodProducer = WD_RadiusOverlayKind.Food;
        public static WD_RadiusOverlayKind people = WD_RadiusOverlayKind.People;
        public static WD_RadiusOverlayKind warehouse = WD_RadiusOverlayKind.Warehouse;
        public static WD_RadiusOverlayKind mortar = WD_RadiusOverlayKind.Mortar;
        public static WD_RadiusOverlayKind rapidResponse = WD_RadiusOverlayKind.Off;
        public static WD_RadiusOverlayKind atTurret = WD_RadiusOverlayKind.Attack;

        private static int suppressFillFrame = -1;

        public static void NotifySuppressFillThisFrame()
        {
            suppressFillFrame = Time.frameCount;
            if (WD_WorldLayer_OutpostCoverageFill.ClearTarget())
                WorldComponent_WDVisualizerToggle.MarkOutpostCoverageFillDirtyPublic();
        }

        public static bool IsFillSuppressedThisFrame =>
            suppressFillFrame == Time.frameCount || suppressFillFrame == Time.frameCount - 1;

        public static WD_RadiusOverlayKind Get(WD_RadiusOverlayCategory category)
        {
            switch (category)
            {
                case WD_RadiusOverlayCategory.FoodProducer: return foodProducer;
                case WD_RadiusOverlayCategory.People: return people;
                case WD_RadiusOverlayCategory.Warehouse: return warehouse;
                case WD_RadiusOverlayCategory.Mortar: return mortar;
                case WD_RadiusOverlayCategory.RapidResponse: return rapidResponse;
                case WD_RadiusOverlayCategory.NpcSettlementT4: return npcSettlementT4;
                case WD_RadiusOverlayCategory.AtTurret: return atTurret;
                default:
                    if (npcSettlement == WD_RadiusOverlayKind.Mortar || npcSettlement == WD_RadiusOverlayKind.AA)
                        return WD_RadiusOverlayKind.Attack;
                    return npcSettlement;
            }
        }

        public static void Set(WD_RadiusOverlayCategory category, WD_RadiusOverlayKind kind)
        {
            switch (category)
            {
                case WD_RadiusOverlayCategory.FoodProducer:
                    foodProducer = kind;
                    break;
                case WD_RadiusOverlayCategory.People:
                    people = kind;
                    break;
                case WD_RadiusOverlayCategory.Warehouse:
                    warehouse = kind;
                    break;
                case WD_RadiusOverlayCategory.Mortar:
                    mortar = kind;
                    break;
                case WD_RadiusOverlayCategory.RapidResponse:
                    rapidResponse = kind;
                    break;
                case WD_RadiusOverlayCategory.NpcSettlementT4:
                    npcSettlementT4 = kind;
                    break;
                case WD_RadiusOverlayCategory.AtTurret:
                    // Turrets only support Attack / Off.
                    if (kind != WD_RadiusOverlayKind.Off)
                        kind = WD_RadiusOverlayKind.Attack;
                    atTurret = kind;
                    break;
                default:
                    // Lower-tier NPCs only support Attack / Ally / Off.
                    if (kind == WD_RadiusOverlayKind.Mortar || kind == WD_RadiusOverlayKind.AA)
                        kind = WD_RadiusOverlayKind.Attack;
                    npcSettlement = kind;
                    break;
            }
        }

        /// <summary>Activate kind for category, or Off if it was already active.</summary>
        public static void Toggle(WD_RadiusOverlayCategory category, WD_RadiusOverlayKind kind)
        {
            Set(category, Get(category) == kind ? WD_RadiusOverlayKind.Off : kind);
        }

        public static bool IsActive(WD_RadiusOverlayCategory category, WD_RadiusOverlayKind kind) =>
            kind != WD_RadiusOverlayKind.Off && Get(category) == kind;

        public static void ExposeData()
        {
            Scribe_Values.Look(ref npcSettlement, "WD_RadiusOverlay_NpcSettlement", WD_RadiusOverlayKind.Attack);
            Scribe_Values.Look(ref npcSettlementT4, "WD_RadiusOverlay_NpcSettlementT4", WD_RadiusOverlayKind.Attack);
            Scribe_Values.Look(ref foodProducer, "WD_RadiusOverlay_FoodProducer", WD_RadiusOverlayKind.Food);
            Scribe_Values.Look(ref people, "WD_RadiusOverlay_People", WD_RadiusOverlayKind.People);
            Scribe_Values.Look(ref warehouse, "WD_RadiusOverlay_Warehouse", WD_RadiusOverlayKind.Warehouse);
            Scribe_Values.Look(ref mortar, "WD_RadiusOverlay_Mortar", WD_RadiusOverlayKind.Mortar);
            Scribe_Values.Look(ref rapidResponse, "WD_RadiusOverlay_RapidResponse", WD_RadiusOverlayKind.Off);
            Scribe_Values.Look(ref atTurret, "WD_RadiusOverlay_AtTurret", WD_RadiusOverlayKind.Attack);

            // Pre-T4-split saves may have stored Mortar/AA on the shared NPC slot.
            if (Scribe.mode == LoadSaveMode.LoadingVars
                && (npcSettlement == WD_RadiusOverlayKind.Mortar || npcSettlement == WD_RadiusOverlayKind.AA))
            {
                npcSettlementT4 = npcSettlement;
                npcSettlement = WD_RadiusOverlayKind.Attack;
            }
        }

        public static void ResetToDefaults()
        {
            npcSettlement = WD_RadiusOverlayKind.Attack;
            npcSettlementT4 = WD_RadiusOverlayKind.Attack;
            foodProducer = WD_RadiusOverlayKind.Food;
            people = WD_RadiusOverlayKind.People;
            warehouse = WD_RadiusOverlayKind.Warehouse;
            mortar = WD_RadiusOverlayKind.Mortar;
            rapidResponse = WD_RadiusOverlayKind.Off;
            atTurret = WD_RadiusOverlayKind.Attack;
        }

        public static WD_RadiusOverlayKind DefaultKind(WD_RadiusOverlayCategory category)
        {
            switch (category)
            {
                case WD_RadiusOverlayCategory.FoodProducer: return WD_RadiusOverlayKind.Food;
                case WD_RadiusOverlayCategory.People: return WD_RadiusOverlayKind.People;
                case WD_RadiusOverlayCategory.Warehouse: return WD_RadiusOverlayKind.Warehouse;
                case WD_RadiusOverlayCategory.Mortar: return WD_RadiusOverlayKind.Mortar;
                case WD_RadiusOverlayCategory.RapidResponse: return WD_RadiusOverlayKind.Attack;
                case WD_RadiusOverlayCategory.NpcSettlementT4: return WD_RadiusOverlayKind.Attack;
                case WD_RadiusOverlayCategory.AtTurret: return WD_RadiusOverlayKind.Attack;
                default: return WD_RadiusOverlayKind.Attack;
            }
        }

        public static bool TryGetCategory(WorldObject worldObject, out WD_RadiusOverlayCategory category)
        {
            category = default;
            if (worldObject == null || worldObject.Destroyed) return false;

            if (worldObject is Settlement settlement)
            {
                if (settlement.Faction == null || settlement.Faction.IsPlayer) return false;
                var comp = settlement.GetComponent<CompViralSpread>();
                if (comp == null) return false;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(settlement))
                    return false;
                category = comp.tier == SettlementTier.T4
                    ? WD_RadiusOverlayCategory.NpcSettlementT4
                    : WD_RadiusOverlayCategory.NpcSettlement;
                return true;
            }

            if (worldObject is WorldObject_WD_Outpost outpost)
            {
                if (outpost.Faction != Faction.OfPlayer) return false;
                if (outpost.IsMortarOutpost)
                {
                    category = WD_RadiusOverlayCategory.Mortar;
                    return true;
                }
                if (outpost.IsRapidResponseOutpost)
                {
                    category = WD_RadiusOverlayCategory.RapidResponse;
                    return true;
                }
                if (Outpost_Production_Utils.IsFoodProducerOutpost(outpost.def))
                {
                    category = WD_RadiusOverlayCategory.FoodProducer;
                    return true;
                }
                if (Outpost_Production_Utils.IsWarehouseOutpost(outpost.def))
                {
                    category = WD_RadiusOverlayCategory.Warehouse;
                    return true;
                }
                if (Outpost_Production_Utils.IsRecruitingOutpost(outpost.def)
                    || Outpost_Production_Utils.IsTradingOutpost(outpost.def)
                    || Outpost_Production_Utils.IsEmbassyOutpost(outpost.def))
                {
                    category = WD_RadiusOverlayCategory.People;
                    return true;
                }
                category = WD_RadiusOverlayCategory.RapidResponse;
                return true;
            }

            if (worldObject is WorldObject_AT_Turret)
            {
                category = WD_RadiusOverlayCategory.AtTurret;
                return true;
            }

            return false;
        }

        /// <summary>
        /// While a single world object is selected, paint its category's active radius (fill or hop ring).
        /// </summary>
        public static void DrawSelectDrivenIfNeeded(WorldObject worldObject)
        {
            if (worldObject == null || worldObject.Destroyed) return;
            if (IsFillSuppressedThisFrame) return;
            if (WorldComponent_WDVisualizerToggle.IsWorldTargeterActive()) return;
            var selector = Find.WorldSelector;
            if (selector == null || !selector.IsSelected(worldObject)) return;
            List<WorldObject> selected = selector.SelectedObjects;
            if (selected == null || selected.Count != 1) return;
            if (!TryGetCategory(worldObject, out WD_RadiusOverlayCategory category)) return;

            if (worldObject is WorldObject_WD_Outpost outpost)
            {
                if (Dialog_OutpostRangeAdjust.TryGetPreview(outpost, out _, out _)) return;
                if (Dialog_OutpostArtilleryConfigure.TryGetPreview(outpost, out _, out _, out _, out _)) return;
            }

            if (worldObject is WorldObject_AT_Turret atTurret
                && Dialog_AtTurretConfigure.TryGetPreview(atTurret, out _))
                return;

            WD_RadiusOverlayKind kind = Get(category);
            if (kind == WD_RadiusOverlayKind.Off) return;

            float radius;
            OutpostCoverageFillKind fillKind;
            Material hopMat;
            int tick = Find.TickManager?.TicksGame ?? 0;
            bool cacheHit = resolveCacheOk
                && resolveCacheObjectId == worldObject.ID
                && resolveCacheKind == kind
                && tick - resolveCacheTick < ResolveCacheFreshTicks;
            if (cacheHit)
            {
                radius = resolveCacheRadius;
                fillKind = resolveCacheFillKind;
                hopMat = resolveCacheHopMat;
            }
            else
            {
                if (!TryResolve(worldObject, kind, out radius, out fillKind, out hopMat))
                {
                    resolveCacheOk = false;
                    return;
                }
                resolveCacheOk = true;
                resolveCacheObjectId = worldObject.ID;
                resolveCacheKind = kind;
                resolveCacheRadius = radius;
                resolveCacheFillKind = fillKind;
                resolveCacheHopMat = hopMat;
                resolveCacheTick = tick;
            }

            bool accuracyBands = kind == WD_RadiusOverlayKind.Mortar
                || kind == WD_RadiusOverlayKind.AA
                || (kind == WD_RadiusOverlayKind.Attack && worldObject is WorldObject_AT_Turret);
            bool attackRangeBands = kind == WD_RadiusOverlayKind.Attack && worldObject is Settlement;
            bool zealAttackInnerCyan = false;
            if (attackRangeBands && worldObject is Settlement settlement)
            {
                var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
                zealAttackInnerCyan = manager != null
                    && settlement.Faction == manager.expansionistZealFaction
                    && tick < manager.expansionistZealExpiryTick;
            }

            WD_RadiusOverlayMode.DrawOrFill(worldObject, radius, fillKind, hopMat,
                accuracyBands: accuracyBands,
                attackRangeBands: attackRangeBands,
                zealAttackInnerCyan: zealAttackInnerCyan);

            if (kind == WD_RadiusOverlayKind.Mortar || kind == WD_RadiusOverlayKind.AA)
                Patch_SettlementT4TurretGizmos.MarkTurretRangeHoverPublic();
        }

        private const int ResolveCacheFreshTicks = 30;
        private static bool resolveCacheOk;
        private static int resolveCacheObjectId = -1;
        private static WD_RadiusOverlayKind resolveCacheKind;
        private static float resolveCacheRadius;
        private static OutpostCoverageFillKind resolveCacheFillKind;
        private static Material resolveCacheHopMat;
        private static int resolveCacheTick = -99999;

        /// <summary>Drop the select-driven radius resolve cache (e.g. after a configure dialog commits a new range).</summary>
        public static void InvalidateResolveCache()
        {
            resolveCacheOk = false;
            resolveCacheObjectId = -1;
            resolveCacheTick = -99999;
        }

        public static bool TryResolve(
            WorldObject worldObject,
            WD_RadiusOverlayKind kind,
            out float radius,
            out OutpostCoverageFillKind fillKind,
            out Material hopMat)
        {
            radius = 0f;
            fillKind = OutpostCoverageFillKind.Red;
            hopMat = WorldOverlayLineMaterials.RadiusRed;

            switch (kind)
            {
                case WD_RadiusOverlayKind.Ally:
                    radius = AllyRadiusPreview.GetRadius(worldObject);
                    fillKind = OutpostCoverageFillKind.Cyan;
                    hopMat = WorldOverlayLineMaterials.RadiusCyan;
                    return radius > 0f;

                case WD_RadiusOverlayKind.Attack:
                    return TryResolveAttack(worldObject, out radius, out fillKind, out hopMat);

                case WD_RadiusOverlayKind.Food:
                {
                    radius = WorldDominationMod.settings?.maxLogisticsRange ?? 0;
                    fillKind = OutpostCoverageFillKind.Green;
                    hopMat = WorldOverlayLineMaterials.LogisticsGreen;
                    return radius > 0f;
                }

                case WD_RadiusOverlayKind.People:
                {
                    if (worldObject is WorldObject_WD_Outpost peopleOutpost
                        && WD_WorldLayer_OutpostCoverageFill.TryGetCoverage(peopleOutpost, out int r, out _))
                    {
                        radius = r;
                        fillKind = OutpostCoverageFillKind.Purple;
                        hopMat = WorldOverlayLineMaterials.RecruitTradingRadiusRing;
                        return radius > 0f;
                    }
                    return false;
                }

                case WD_RadiusOverlayKind.Warehouse:
                {
                    if (worldObject is WorldObject_WD_Outpost wh)
                    {
                        radius = OutpostWarehouseAuraUtility.GetWarehouseAuraRadiusTiles(wh);
                        fillKind = OutpostCoverageFillKind.Purple;
                        hopMat = WorldOverlayLineMaterials.RecruitTradingRadiusRing;
                        return radius > 0f;
                    }
                    return false;
                }

                case WD_RadiusOverlayKind.Mortar:
                {
                    if (worldObject is WorldObject_WD_Outpost mortarOutpost)
                        radius = MortarFireUtils.GetPlayerMortarMaxRangeTiles(mortarOutpost);
                    else
                        radius = WorldDominationMod.settings?.npcMortarRange ?? WorldDominationSettings.DefNpcMortarRange;
                    fillKind = OutpostCoverageFillKind.Red;
                    hopMat = WorldOverlayLineMaterials.RadiusRed;
                    return radius > 0f;
                }

                case WD_RadiusOverlayKind.AA:
                {
                    if (worldObject is WorldObject_WD_Outpost aaOutpost)
                        radius = AntiAirFireUtils.GetPlayerAntiAirMaxRangeTiles(aaOutpost);
                    else
                        radius = AntiAirFireUtils.GetNpcAntiAirMaxRangeTiles();
                    fillKind = OutpostCoverageFillKind.Red;
                    hopMat = WorldOverlayLineMaterials.RadiusRed;
                    return radius > 0f;
                }

                default:
                    return false;
            }
        }

        private static bool TryResolveAttack(
            WorldObject worldObject,
            out float radius,
            out OutpostCoverageFillKind fillKind,
            out Material hopMat)
        {
            fillKind = OutpostCoverageFillKind.Red;
            hopMat = WorldOverlayLineMaterials.RadiusRed;
            radius = 0f;
            var seth = WorldDominationMod.settings;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();

            if (worldObject is Settlement settlement)
            {
                radius = SettlementAttackRangeUtil.GetNpcSettlementAttackRangeWithZeal(settlement, seth, manager);
                // Zeal uses cyan on attack-range band 0 only (see WD_WorldLayer_OutpostCoverageFill), not whole-disk fill.
                return radius > 0f;
            }

            if (worldObject is WorldObject_WD_Outpost outpost)
            {
                radius = seth?.raidTargetRadius ?? WorldDominationSettings.DefRaidTargetRadius;
                radius *= 1f + OutpostExpertUtility.GetStrategistAttackRangeBonusFraction(outpost);
                if (outpost.Faction == manager?.expansionistZealFaction
                    && Find.TickManager.TicksGame < manager.expansionistZealExpiryTick)
                    radius *= seth?.zealRaidRangeMult ?? WorldDominationSettings.DefZealRaidRangeMult;
                return radius > 0f;
            }

            if (worldObject is WorldObject_AT_Turret turret)
            {
                radius = turret.EffectiveRangeTiles;
                return radius > 0f;
            }

            return false;
        }
    }
}
