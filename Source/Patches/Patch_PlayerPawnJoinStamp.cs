using HarmonyLib;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Join Stamp: first time a pawn becomes (or spawns as) player faction.
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.SetFaction))]
    public static class Patch_Thing_SetFaction_JoinStamp
    {
        public static void Prefix(Thing __instance, Faction newFaction, out Faction __state)
        {
            __state = __instance?.Faction;
        }

        public static void Postfix(Thing __instance, Faction newFaction, Faction __state)
        {
            if (__instance is not Pawn pawn) return;
            if (newFaction == null || !newFaction.IsPlayer) return;
            if (__state != null && __state.IsPlayer) return;
            WorldComponent_PlayerPawnJoinTimes.Get()?.NoteJoinedPlayerFaction(pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class Patch_Pawn_SpawnSetup_JoinStamp
    {
        public static void Postfix(Pawn __instance, Map map, bool respawningAfterLoad)
        {
            if (respawningAfterLoad) return;
            if (__instance == null || __instance.Destroyed) return;
            if (__instance.Faction?.IsPlayer != true) return;
            WorldComponent_PlayerPawnJoinTimes.Get()?.NoteJoinedPlayerFaction(__instance);
        }
    }
}
