using System.Text;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Flat trench movement penalty (Def 0.5), same bake as road blocks. Runs after RotR and road-block patches.
    /// </summary>
    [HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.GetRoadMovementDifficultyMultiplier))]
    [HarmonyAfter("Mlie.RoadsOfTheRim")]
    public static class Patch_GetRoadMovementDifficultyMultiplier_Trench
    {
        [HarmonyPrefix]
        public static void Prefix(PlanetTile toTile, out bool __state)
        {
            __state = toTile.Valid;
        }

        [HarmonyPostfix]
        public static void Postfix(PlanetTile fromTile, PlanetTile toTile, StringBuilder explanation, bool __state, ref float __result)
        {
            if (!WorldComponent_WdTrenches.FeatureEnabled) return;

            PlanetTile trenchTile = __state ? toTile : fromTile;
            if (!trenchTile.Valid) return;

            float penalty = WorldComponent_WdTrenches.GetFlatPenalty(trenchTile.tileId);
            if (penalty <= 0f) return;

            float terrain = WorldPathGrid.CalculatedMovementDifficultyAt(trenchTile, false);
            if (terrain < 0.01f) terrain = 0.01f;
            __result += penalty / terrain;

            if (explanation != null)
            {
                explanation.AppendLine();
                explanation.Append("TSA_WD_Trench_MovementExplanation".Translate(penalty.ToString("0.#")));
                explanation.AppendLine();
            }
        }
    }
}
