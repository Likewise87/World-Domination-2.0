using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// NPC settlements with a known T1/T2 subtype: ExpandingIcon = subtype art only when
    /// <see cref="WorldComponent_WDVisualizerToggle.ShowSettlementTypeIcons"/> is on.
    /// Off → vanilla FactionIcon. ExpandingIconColor stays vanilla (faction tint).
    /// </summary>
    public static class Patch_SettlementTypeExpandingIcon
    {
        [HarmonyPatch(typeof(Settlement), "get_ExpandingIcon")]
        public static class Settlement_ExpandingIcon_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Settlement __instance, ref Texture2D __result)
            {
                if (!WorldComponent_WDVisualizerToggle.ShowSettlementTypeIcons)
                    return true;
                if (!SettlementTypeIconUtility.TryGetExpandingIcon(__instance, out Texture2D tex))
                    return true;
                __result = tex;
                return false;
            }
        }
    }
}
