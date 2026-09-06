using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Vanilla drafted edge leave uses <see cref="ExitMapGrid.MapUsesExitGrid"/>. While hostiles exist,
    /// FormCaravan cannot reform so the exit grid turns on (flee). Ambush normally blocks that via
    /// <c>blockExitGridUntilBattleIsWon</c>, but WD clash raids arrive after map gen so WonBattle can latch early.
    /// Outpost defense sites have no FormCaravanComp / CaravansBattlefield at all.
    /// Force the exit grid off for active WD temporary encounters only.
    /// </summary>
    [HarmonyPatch(typeof(ExitMapGrid), "get_MapUsesExitGrid")]
    public static class Patch_ExitMapGrid_MapUsesExitGrid_WdTempEncounters
    {
        private static readonly AccessTools.FieldRef<ExitMapGrid, Map> MapField =
            AccessTools.FieldRefAccess<ExitMapGrid, Map>("map");

        public static void Postfix(ExitMapGrid __instance, ref bool __result)
        {
            if (!__result) return;
            Map map = MapField(__instance);
            if (WD_TempEncounterExitMapUtility.BlocksPlayerEdgeExit(map))
                __result = false;
        }
    }

    /// <summary>
    /// WD owns clash Ambush win letters. Skip vanilla CheckWonBattle for the whole life of the clash tracker
    /// (not only while encounterActive), so empty pre-raid maps and post-WD-win ticks cannot fire
    /// LetterCaravansBattlefieldVictory.
    /// </summary>
    [HarmonyPatch(typeof(CaravansBattlefield), "CheckWonBattle")]
    public static class Patch_CaravansBattlefield_CheckWonBattle_WdClash
    {
        public static bool Prefix(CaravansBattlefield __instance)
        {
            Map map = __instance?.Map;
            if (WD_TempEncounterExitMapUtility.SuppressesVanillaAmbushWonBattle(map))
                return false;
            return true;
        }
    }
}
