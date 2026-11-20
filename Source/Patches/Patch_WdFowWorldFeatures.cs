using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Hide landform / feature name labels for features the View World as faction has never explored.
    /// Cheap: HashSet lookup of feature uniqueIDs filled when tiles are revealed.
    /// </summary>
    [HarmonyPatch(typeof(WorldFeatures), "UpdateAlpha")]
    public static class Patch_WdFowWorldFeatures
    {
        public static bool Prefix(WorldFeatureTextMesh text, WorldFeature feature)
        {
            if (!WdFowDraw.IsActive || feature == null || text == null)
                return true;

            var intel = WorldComponent_SettlementIntel.Get();
            if (intel?.TileFow == null || intel.TileFow.IsFeatureKnownForView(feature))
                return true;

            text.Color = new Color(1f, 1f, 1f, 0f);
            return false;
        }
    }
}
