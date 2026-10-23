using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// When an NPC settlement is selected, draw affiliation-colored ownership lines to its
    /// AT turrets and advanced buildings. Patches <see cref="WorldObject"/> because
    /// <see cref="Settlement"/> does not override <c>DrawExtraSelectionOverlays</c>.
    /// </summary>
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.DrawExtraSelectionOverlays))]
    public static class Patch_SettlementOwnedAssetSelectionLines
    {
        [HarmonyPostfix]
        public static void Postfix(WorldObject __instance)
        {
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceLayerSelected()) return;
            if (__instance is not Settlement settlement) return;
            if (settlement.Destroyed) return;
            if (!Find.WorldSelector.IsSelected(settlement)) return;
            SettlementOwnedAssetSelectionLines.DrawForSettlement(settlement);
        }
    }
}
