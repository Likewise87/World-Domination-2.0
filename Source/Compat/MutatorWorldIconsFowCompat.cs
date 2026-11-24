using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Optional: hide MutatorWorldIcons tiles that are still unexplored (black FoW).
    /// Grey shroud (explored, out of LOS) still shows mutator icons.
    /// Reflection-only — no compile reference to MutatorWorldIcons.dll.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class MutatorWorldIconsFowCompat
    {
        public const string PackageId = "Snobi.MutatorWorldIcons";
        private static bool active;

        static MutatorWorldIconsFowCompat()
        {
            try
            {
                if (!IsModActive())
                    return;

                Type layerType = AccessTools.TypeByName("MutatorWorldIcons.WorldDrawLayer_MutatorWorldIcons");
                if (layerType == null)
                {
                    Log.Warning("[TSA WD] MutatorWorldIcons FoW compat: layer type not found.");
                    return;
                }

                MethodInfo draw = AccessTools.Method(layerType, "DrawTileMaterials");
                if (draw == null)
                {
                    Log.Warning("[TSA WD] MutatorWorldIcons FoW compat: DrawTileMaterials missing.");
                    return;
                }

                var harmony = new Harmony("TSA.WorldDomination.MutatorWorldIconsFowCompat");
                harmony.Patch(
                    draw,
                    prefix: new HarmonyMethod(typeof(MutatorWorldIconsFowCompat), nameof(PrefixDrawTileMaterials)));
                active = true;
                Log.Message("[TSA WD] MutatorWorldIcons FoW compat active.");
            }
            catch (Exception ex)
            {
                active = false;
                Log.Warning($"[TSA WD] MutatorWorldIcons FoW compat disabled: {ex.Message}");
            }
        }

        public static bool IsActive => active;

        private static bool IsModActive()
        {
            return ModsConfig.IsActive(PackageId)
                || ModLister.GetActiveModWithIdentifier(PackageId, false) != null;
        }

        /// <summary>Harmony Prefix: skip mutator icons on unexplored (black) tiles when FoW is on.</summary>
        public static bool PrefixDrawTileMaterials(int tileId)
        {
            if (!WdFowDraw.DrawFogForCurrentView)
                return true;
            var intel = WorldComponent_SettlementIntel.Get();
            if (intel == null)
                return true;
            // Explored-bit only — no EnsureVisibleCacheFresh (draw-safe).
            return intel.TileFow.IsTileExploredForView(tileId);
        }
    }
}
