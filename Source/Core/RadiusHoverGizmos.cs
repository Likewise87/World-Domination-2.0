using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Radius overlay toggle using pre-colored on/off icons (no engine tint).</summary>
    [StaticConstructorOnStartup]
    public class Command_RadiusOverlayToggle : Command_Toggle
    {
        public Texture2D iconOn;
        public Texture2D iconOff;

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            icon = isActive != null && isActive() ? iconOn : iconOff;
            return base.GizmoOnGUI(topLeft, maxWidth, parms);
        }
    }

    /// <summary>Global-per-category radius overlay toggles. Fill is driven by selection + prefs.</summary>
    [StaticConstructorOnStartup]
    public static class RadiusHoverGizmos
    {
        private static Texture2D cachedAttackOn;
        private static Texture2D cachedAttackOff;
        private static Texture2D cachedFoodOn;
        private static Texture2D cachedFoodOff;
        private static Texture2D cachedPeopleOn;
        private static Texture2D cachedPeopleOff;
        private static Texture2D cachedWarehouseOn;
        private static Texture2D cachedWarehouseOff;
        private static Texture2D cachedMortarOn;
        private static Texture2D cachedMortarOff;
        private static Texture2D cachedAaOn;
        private static Texture2D cachedAaOff;
        private static Texture2D cachedTurretAttackOn;
        private static Texture2D cachedTurretAttackOff;

        public static IEnumerable<Gizmo> GetForOutpost(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.Destroyed || outpost.Faction != Faction.OfPlayer)
                yield break;
            if (!WD_RadiusOverlayPrefs.TryGetCategory(outpost, out WD_RadiusOverlayCategory category))
                yield break;

            // Outpost attack fill is solid (no equal-quarter bands). Settlement attack uses bands.
            yield return MakeToggle(
                category,
                WD_RadiusOverlayKind.Attack,
                "TSA_WD_Gizmo_ShowAttackRadius",
                "TSA_WD_Gizmo_ShowAttackRadiusDesc",
                GetAttackRangeLabel(outpost),
                ref cachedAttackOn,
                ref cachedAttackOff,
                "UI/Commands/AttackRadius",
                TexCommand.Attack);

            if (outpost.IsMortarOutpost)
            {
                yield return MakeToggle(
                    category,
                    WD_RadiusOverlayKind.Mortar,
                    "TSA_WD_Gizmo_ShowMortarRadius",
                    "TSA_WD_Gizmo_ShowMortarRadiusDesc",
                    MortarFireUtils.GetPlayerMortarMaxRangeTiles(outpost).ToString("F0"),
                    ref cachedMortarOn,
                    ref cachedMortarOff,
                    "UI/Commands/MortarRadius",
                    TexCommand.Attack,
                    AppendAccuracyBandsLegend(isNpc: false, forAntiAir: false));

                if (AntiAirFireUtils.HasAntiAirUpgrade(outpost))
                {
                    yield return MakeToggle(
                        category,
                        WD_RadiusOverlayKind.AA,
                        "TSA_WD_Gizmo_ShowAARadius",
                        "TSA_WD_Gizmo_ShowAARadiusDesc",
                        AntiAirFireUtils.GetPlayerAntiAirMaxRangeTiles(outpost).ToString("F0"),
                        ref cachedAaOn,
                        ref cachedAaOff,
                        "UI/Commands/AntiAirRadius",
                        TexCommand.Attack,
                        AppendAccuracyBandsLegend(isNpc: false, forAntiAir: true));
                }
            }

            if (category == WD_RadiusOverlayCategory.FoodProducer
                && (WorldDominationMod.settings?.maxLogisticsRange ?? 0) > 0)
            {
                yield return MakeToggle(
                    category,
                    WD_RadiusOverlayKind.Food,
                    "TSA_WD_Gizmo_ShowFoodRadius",
                    "TSA_WD_Gizmo_ShowFoodRadiusDesc",
                    (WorldDominationMod.settings?.maxLogisticsRange ?? 0).ToString("F0"),
                    ref cachedFoodOn,
                    ref cachedFoodOff,
                    "UI/Commands/ShowFoodRadius",
                    BaseContent.BadTex);
            }

            if (category == WD_RadiusOverlayCategory.People
                && WD_WorldLayer_OutpostCoverageFill.TryGetCoverage(outpost, out int peopleR, out _))
            {
                yield return MakeToggle(
                    category,
                    WD_RadiusOverlayKind.People,
                    "TSA_WD_Gizmo_ShowPeopleRadius",
                    "TSA_WD_Gizmo_ShowPeopleRadiusDesc",
                    peopleR.ToString("F0"),
                    ref cachedPeopleOn,
                    ref cachedPeopleOff,
                    "UI/Commands/ShowSettlementRadius",
                    BaseContent.BadTex);
            }

            if (category == WD_RadiusOverlayCategory.Warehouse
                && OutpostWarehouseAuraUtility.GetWarehouseAuraRadiusTiles(outpost) > 0f)
            {
                float whR = OutpostWarehouseAuraUtility.GetWarehouseAuraRadiusTiles(outpost);
                float auraPct = OutpostWarehouseAuraUtility.GetWarehouseAuraBonusFraction(outpost) * 100f;
                var cmd = MakeToggle(
                    category,
                    WD_RadiusOverlayKind.Warehouse,
                    "TSA_WD_Gizmo_ShowWarehouseRadius",
                    "TSA_WD_Gizmo_ShowWarehouseRadiusDesc",
                    whR.ToString("F0"),
                    ref cachedWarehouseOn,
                    ref cachedWarehouseOff,
                    "UI/Commands/WarehouseRadius",
                    BaseContent.BadTex);
                cmd.defaultDesc = "TSA_WD_Gizmo_ShowWarehouseRadiusDesc".Translate(whR.ToString("F0")).ToString()
                    + "\n\n"
                    + "TSA_WD_Gizmo_WarehouseAuraBoostLine".Translate(auraPct.ToString("F0")).ToString();
                yield return cmd;
            }
        }

        public static IEnumerable<Gizmo> GetAttackForSettlement(Settlement settlement)
        {
            if (!WD_RadiusOverlayPrefs.TryGetCategory(settlement, out WD_RadiusOverlayCategory category))
                yield break;

            var seth = WorldDominationMod.settings;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            float range = SettlementAttackRangeUtil.GetNpcSettlementAttackRangeWithZeal(settlement, seth, manager);

            yield return MakeToggle(
                category,
                WD_RadiusOverlayKind.Attack,
                "TSA_WD_Gizmo_ShowAttackRadius",
                "TSA_WD_Gizmo_ShowAttackRadiusDesc",
                range.ToString("F0"),
                ref cachedAttackOn,
                ref cachedAttackOff,
                "UI/Commands/AttackRadius",
                TexCommand.Attack,
                "\n\n" + "TSA_WD_Gizmo_AttackRadiusBandsLegend".Translate());
        }

        public static IEnumerable<Gizmo> GetForAtTurret(WorldObject_AT_Turret turret)
        {
            if (turret == null || turret.Destroyed)
                yield break;

            // Do not gate on TryGetCategory here — AT Turrets always get their own Attack toggle.
            // Player and NPC turrets both expose Show range (configure dialog stays player-only).
            const WD_RadiusOverlayCategory category = WD_RadiusOverlayCategory.AtTurret;

            yield return MakeToggle(
                category,
                WD_RadiusOverlayKind.Attack,
                "TSA_WD_Gizmo_ShowAttackRadius",
                "TSA_WD_Gizmo_ShowAttackRadiusDesc",
                turret.EffectiveRangeTiles.ToString("F0"),
                ref cachedTurretAttackOn,
                ref cachedTurretAttackOff,
                "UI/Commands/AT_Radius",
                TexCommand.Attack,
                AppendAtTurretAccuracyBandsLegend());
        }

        public static IEnumerable<Gizmo> GetT4TurretRadius(Settlement settlement, bool mortarEligible, bool aaEligible)
        {
            if (!WD_RadiusOverlayPrefs.TryGetCategory(settlement, out WD_RadiusOverlayCategory category))
                yield break;

            if (mortarEligible)
            {
                float range = WorldDominationMod.settings?.npcMortarRange ?? WorldDominationSettings.DefNpcMortarRange;
                yield return MakeToggle(
                    category,
                    WD_RadiusOverlayKind.Mortar,
                    "TSA_WD_Gizmo_ShowMortarRadius",
                    "TSA_WD_Gizmo_ShowMortarRadiusDesc",
                    range.ToString("F0"),
                    ref cachedMortarOn,
                    ref cachedMortarOff,
                    "UI/Commands/MortarRadius",
                    TexCommand.Attack,
                    AppendAccuracyBandsLegend(isNpc: true, forAntiAir: false));
            }

            if (aaEligible)
            {
                float range = AntiAirFireUtils.GetNpcAntiAirMaxRangeTiles();
                yield return MakeToggle(
                    category,
                    WD_RadiusOverlayKind.AA,
                    "TSA_WD_Gizmo_ShowAARadius",
                    "TSA_WD_Gizmo_ShowAARadiusDesc",
                    range.ToString("F0"),
                    ref cachedAaOn,
                    ref cachedAaOff,
                    "UI/Commands/AntiAirRadius",
                    TexCommand.Attack,
                    AppendAccuracyBandsLegend(isNpc: true, forAntiAir: true));
            }
        }

        private static Command_RadiusOverlayToggle MakeToggle(
            WD_RadiusOverlayCategory category,
            WD_RadiusOverlayKind kind,
            string labelKey,
            string descKey,
            string rangeLabel,
            ref Texture2D cachedOn,
            ref Texture2D cachedOff,
            string texPath,
            Texture2D fallback,
            string extraDesc = null)
        {
            Texture2D on = cachedOn ??= ContentFinder<Texture2D>.Get(texPath, false) ?? fallback;
            Texture2D off = cachedOff ??= ContentFinder<Texture2D>.Get(texPath + "_Off", false) ?? on;
            string desc = descKey.Translate(rangeLabel).ToString();
            if (!extraDesc.NullOrEmpty())
                desc += extraDesc;
            return new Command_RadiusOverlayToggle
            {
                defaultLabel = labelKey.Translate(),
                defaultDesc = desc,
                iconOn = on,
                iconOff = off,
                isActive = () => WD_RadiusOverlayPrefs.IsActive(category, kind),
                toggleAction = () => WD_RadiusOverlayPrefs.Toggle(category, kind)
            };
        }

        private static string GetAttackRangeLabel(WorldObject_WD_Outpost outpost)
        {
            if (WD_RadiusOverlayPrefs.TryResolve(outpost, WD_RadiusOverlayKind.Attack, out float r, out _, out _))
                return r.ToString("F0");
            return "0";
        }

        /// <summary>Color legend for mortar/AA accuracy rings. Hit % from live settings (player or NPC).</summary>
        private static string AppendAccuracyBandsLegend(bool isNpc, bool forAntiAir)
        {
            var s = WorldDominationMod.settings;
            float band0;
            float band1;
            float band2;
            if (forAntiAir)
            {
                if (isNpc)
                {
                    band0 = s?.npcAntiAirHitChance0To50PctRange ?? WorldDominationSettings.DefNpcAntiAirHitChance0To50PctRange;
                    band1 = s?.npcAntiAirHitChance51To75PctRange ?? WorldDominationSettings.DefNpcAntiAirHitChance51To75PctRange;
                    band2 = s?.npcAntiAirHitChance76To100PctRange ?? WorldDominationSettings.DefNpcAntiAirHitChance76To100PctRange;
                }
                else
                {
                    band0 = s?.antiAirHitChance0To50PctRange ?? WorldDominationSettings.DefAntiAirHitChance0To50PctRange;
                    band1 = s?.antiAirHitChance51To75PctRange ?? WorldDominationSettings.DefAntiAirHitChance51To75PctRange;
                    band2 = s?.antiAirHitChance76To100PctRange ?? WorldDominationSettings.DefAntiAirHitChance76To100PctRange;
                }
                return "\n\n" + "TSA_WD_Gizmo_AARadiusBandsLegend".Translate(
                    FormatHitPct(band0), FormatHitPct(band1), FormatHitPct(band2));
            }

            if (isNpc)
            {
                band0 = s?.npcMortarHitChance0To50PctRange ?? WorldDominationSettings.DefNpcMortarHitChance0To50PctRange;
                band1 = s?.npcMortarHitChance51To75PctRange ?? WorldDominationSettings.DefNpcMortarHitChance51To75PctRange;
                band2 = s?.npcMortarHitChance76To100PctRange ?? WorldDominationSettings.DefNpcMortarHitChance76To100PctRange;
            }
            else
            {
                band0 = s?.mortarHitChance0To50PctRange ?? WorldDominationSettings.DefMortarHitChance0To50PctRange;
                band1 = s?.mortarHitChance51To75PctRange ?? WorldDominationSettings.DefMortarHitChance51To75PctRange;
                band2 = s?.mortarHitChance76To100PctRange ?? WorldDominationSettings.DefMortarHitChance76To100PctRange;
            }
            return "\n\n" + "TSA_WD_Gizmo_MortarRadiusBandsLegend".Translate(
                FormatHitPct(band0), FormatHitPct(band1), FormatHitPct(band2));
        }

        private static string AppendAtTurretAccuracyBandsLegend()
        {
            var s = WorldDominationMod.settings;
            float band0 = s?.atTurretHitChance0To50PctRange ?? WorldDominationSettings.DefAtTurretHitChance0To50PctRange;
            float band1 = s?.atTurretHitChance51To75PctRange ?? WorldDominationSettings.DefAtTurretHitChance51To75PctRange;
            float band2 = s?.atTurretHitChance76To100PctRange ?? WorldDominationSettings.DefAtTurretHitChance76To100PctRange;
            return "\n\n" + "TSA_WD_Gizmo_AtTurretRadiusBandsLegend".Translate(
                FormatHitPct(band0), FormatHitPct(band1), FormatHitPct(band2));
        }

        private static string FormatHitPct(float chance01) =>
            (Mathf.Clamp01(chance01) * 100f).ToString("F0");
    }
}
