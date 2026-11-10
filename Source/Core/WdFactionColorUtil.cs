using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Apply live <see cref="Faction.color"/> overrides from Configure Scope and invalidate
    /// baked world-map materials that cached the old tint.
    /// </summary>
    public static class WdFactionColorUtil
    {
        private static readonly FieldInfo SettlementCachedMat =
            AccessTools.Field(typeof(Settlement), "cachedMat");
        private static readonly FieldInfo WorldObjectExpandingMaterial =
            AccessTools.Field(typeof(WorldObject), "expandingMaterial");
        private static readonly FieldInfo AdvancedBuildingCachedMaterial =
            AccessTools.Field(typeof(WorldObject_WD_AdvancedBuilding), "cachedMaterial");

        /// <summary>
        /// Spectrum color ignoring any <see cref="Faction.color"/> override (for picker default / reset preview).
        /// </summary>
        public static Color SpectrumColor(Faction faction)
        {
            if (faction?.def == null) return Color.white;
            List<Color> spectrum = faction.def.colorSpectrum;
            if (spectrum.NullOrEmpty()) return Color.white;
            return ColorsFromSpectrum.Get(spectrum, faction.colorFromSpectrum);
        }

        /// <summary>Set or clear the faction color override and refresh world/UI tints.</summary>
        public static void Apply(Faction faction, Color? color)
        {
            if (faction == null) return;
            faction.color = color;
            InvalidateCachesForFaction(faction);
        }

        public static void InvalidateCachesForFaction(Faction faction)
        {
            if (faction == null) return;

            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                for (int i = 0; i < settlements.Count; i++)
                {
                    Settlement s = settlements[i];
                    if (s == null || s.Destroyed || s.Faction != faction) continue;
                    ClearSettlementCachedMat(s);
                    ClearExpandingMaterial(s);
                }
            }

            IReadOnlyList<WorldObject_Traveler> travelers = WorldObject_Traveler.LiveTravelers;
            if (travelers != null)
            {
                for (int i = 0; i < travelers.Count; i++)
                {
                    WorldObject_Traveler t = travelers[i];
                    if (t == null || t.Destroyed || t.Faction != faction) continue;
                    t.InvalidateTravelerMaterialCache();
                    ClearExpandingMaterial(t);
                }
            }

            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            if (outposts != null)
            {
                for (int i = 0; i < outposts.Count; i++)
                {
                    WorldObject_WD_Outpost o = outposts[i];
                    if (o == null || o.Destroyed || o.Faction != faction) continue;
                    o.InvalidateWorldMapIconCache();
                    ClearExpandingMaterial(o);
                }
            }

            IReadOnlyList<WorldObject_AT_Turret> turrets = WorldObject_AT_Turret.LiveTurrets;
            if (turrets != null)
            {
                for (int i = 0; i < turrets.Count; i++)
                {
                    WorldObject_AT_Turret turret = turrets[i];
                    if (turret == null || turret.Destroyed || turret.Faction != faction) continue;
                    turret.InvalidateWorldMapIconCache();
                    ClearExpandingMaterial(turret);
                }
            }

            IReadOnlyList<WorldObject_WD_AdvancedBuilding> buildings =
                WorldComponent_AdvancedBuildings.Get()?.All;
            if (buildings != null)
            {
                for (int i = 0; i < buildings.Count; i++)
                {
                    WorldObject_WD_AdvancedBuilding b = buildings[i];
                    if (b == null || b.Destroyed || b.Faction != faction) continue;
                    ClearAdvancedBuildingCachedMaterial(b);
                    ClearExpandingMaterial(b);
                }
            }

            Patch_WdWorldObjectNoExpandingIcon.NotifyIconModeChanged();
        }

        private static void ClearSettlementCachedMat(Settlement settlement)
        {
            if (settlement == null || SettlementCachedMat == null) return;
            SettlementCachedMat.SetValue(settlement, null);
        }

        private static void ClearExpandingMaterial(WorldObject wo)
        {
            if (wo == null || WorldObjectExpandingMaterial == null) return;
            WorldObjectExpandingMaterial.SetValue(wo, null);
        }

        private static void ClearAdvancedBuildingCachedMaterial(WorldObject_WD_AdvancedBuilding building)
        {
            if (building == null || AdvancedBuildingCachedMaterial == null) return;
            AdvancedBuildingCachedMaterial.SetValue(building, null);
        }
    }
}
