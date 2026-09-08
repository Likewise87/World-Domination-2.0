using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Mid-fight drafted edge leave must not abandon WD temp encounters.
    /// Do NOT patch <see cref="ExitMapGrid.MapUsesExitGrid"/> — that getter is polled every UI frame by
    /// <c>ExitMapGridUpdate</c> (even when vanilla caches the bool per tick), and was a severe hitch.
    /// Block the rare leave actions instead; the exit overlay may still draw until the fight ends.
    /// </summary>
    [HarmonyPatch(typeof(CaravanExitMapUtility), nameof(CaravanExitMapUtility.CanExitMapAndJoinOrCreateCaravanNow))]
    public static class Patch_CanExitMapAndJoinOrCreateCaravanNow_WdTempEncounters
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (!__result) return;
            if (!WD_TempEncounterExitMapUtility.ShouldBlockPawnExit(pawn)) return;
            __result = false;
            WD_TempEncounterExitMapUtility.NotifyBlocked(pawn);
        }
    }

    /// <summary>
    /// Hard stop for drafted <see cref="JobDriver_Goto.TryExitMap"/> → <see cref="Pawn.ExitMap"/>
    /// and any other leave path that still reaches ExitMap while the encounter is active.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExitMap))]
    public static class Patch_Pawn_ExitMap_WdTempEncounters
    {
        public static bool Prefix(Pawn __instance)
        {
            if (!WD_TempEncounterExitMapUtility.ShouldBlockPawnExit(__instance))
                return true;
            WD_TempEncounterExitMapUtility.NotifyBlocked(__instance);
            return false;
        }
    }

    /// <summary>
    /// Belt-and-suspenders for the caravan join/create helper (also called from <see cref="Pawn.ExitMap"/>).
    /// </summary>
    [HarmonyPatch(typeof(CaravanExitMapUtility), nameof(CaravanExitMapUtility.ExitMapAndJoinOrCreateCaravan))]
    public static class Patch_ExitMapAndJoinOrCreateCaravan_WdTempEncounters
    {
        public static bool Prefix(Pawn pawn)
        {
            if (!WD_TempEncounterExitMapUtility.ShouldBlockPawnExit(pawn))
                return true;
            WD_TempEncounterExitMapUtility.NotifyBlocked(pawn);
            return false;
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
