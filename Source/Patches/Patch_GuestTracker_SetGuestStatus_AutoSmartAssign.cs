using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// When a pawn first becomes a colony prisoner on a player map, optionally Smart Assign a
    /// post-recruit destination (prisoners window auto-toggle). Outpost captures assign from
    /// <see cref="WorldObject_WD_Outpost.TryCaptureAsPrisoner"/> instead.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_GuestTracker), nameof(Pawn_GuestTracker.SetGuestStatus))]
    public static class Patch_GuestTracker_SetGuestStatus_AutoSmartAssign
    {
        private static readonly AccessTools.FieldRef<Pawn_GuestTracker, Pawn> PawnField =
            AccessTools.FieldRefAccess<Pawn_GuestTracker, Pawn>("pawn");

        public static void Prefix(Pawn_GuestTracker __instance, out bool __state)
        {
            Pawn pawn = __instance != null ? PawnField(__instance) : null;
            __state = pawn != null
                && !pawn.Destroyed
                && pawn.RaceProps?.Humanlike == true
                && !pawn.IsPrisonerOfColony;
        }

        public static void Postfix(Pawn_GuestTracker __instance, bool __state)
        {
            if (!__state || __instance == null) return;
            Pawn pawn = PawnField(__instance);
            if (pawn == null || pawn.Destroyed || pawn.Dead) return;
            if (!pawn.IsPrisonerOfColony) return;
            if (!pawn.Spawned) return;
            if (pawn.Map?.Parent is not MapParent mp
                || mp.Destroyed
                || mp.Faction?.IsPlayer != true
                || mp is WorldObject_WD_Outpost)
                return;

            PrisonerRosterUtility.TryAutoSmartAssignNewCapture(pawn, holdingOutpost: null);
        }
    }
}
