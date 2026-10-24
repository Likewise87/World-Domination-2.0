using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// When a pawn first becomes a colony prisoner on a player map (colony or outpost defense),
    /// optionally Smart Assign a post-recruit destination (prisoners window auto-toggle).
    /// Unspawned outpost storage captures also call
    /// <see cref="PrisonerRosterUtility.TryAutoSmartAssignNewCapture"/> from
    /// <see cref="WorldObject_WD_Outpost.TryCaptureAsPrisoner"/> (idempotent if already scheduled).
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

            string label = pawn.LabelShortCap ?? pawn.LabelCap ?? pawn.ThingID ?? "?";

            // MapHeld covers carried / inventory captures (Capture Them etc.) where Spawned is false.
            Map map = pawn.Map ?? pawn.MapHeld;
            if (map == null)
            {
                WDVerbose.Msg($"AutoSmartAssign patch skip: {label} map=null spawned={pawn.Spawned}");
                return;
            }

            // Temporary outpost-defense sites often have Faction null (non-KCSG path). Resolve the
            // linked outpost so auto Smart Assign still runs; do not treat the site as a colony MapParent.
            WD_MapComponent_OutpostDefense defense = map.GetComponent<WD_MapComponent_OutpostDefense>();
            WorldObject_WD_Outpost linked = defense?.LinkedOutpost;
            if (linked != null && !linked.Destroyed && linked.Faction?.IsPlayer == true)
            {
                WDVerbose.Msg($"AutoSmartAssign patch invoke: {label} via linkedOutpost={linked.LabelCap} spawned={pawn.Spawned}");
                PrisonerRosterUtility.TryAutoSmartAssignNewCapture(pawn, linked);
                return;
            }

            if (map.Parent is not MapParent mp
                || mp.Destroyed
                || mp.Faction?.IsPlayer != true)
            {
                WDVerbose.Msg(
                    $"AutoSmartAssign patch skip: {label} bad parent={map.Parent?.LabelCap ?? "null"} fac={map.Parent?.Faction?.Name ?? "null"} spawned={pawn.Spawned}");
                return;
            }

            if (mp is WorldObject_WD_Outpost)
            {
                // Parent is an outpost world object without a defense LinkedOutpost (should be rare).
                WDVerbose.Msg($"AutoSmartAssign patch skip: {label} parent is outpost without LinkedOutpost ({mp.LabelCap})");
                return;
            }

            WDVerbose.Msg($"AutoSmartAssign patch invoke: {label} via colony={mp.LabelCap} spawned={pawn.Spawned}");
            PrisonerRosterUtility.TryAutoSmartAssignNewCapture(pawn, holdingOutpost: null);
        }
    }
}
