using HarmonyLib;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Patch_OutpostDefenseMechanoidDraft
    {
        static Patch_OutpostDefenseMechanoidDraft()
        {
            var harmony = new Harmony("TSA.WorldDomination.OutpostDefenseMechanoidDraft");

            harmony.Patch(
                AccessTools.Method(typeof(MechanitorUtility), nameof(MechanitorUtility.CanDraftMech)),
                postfix: new HarmonyMethod(typeof(Patch_OutpostDefenseMechanoidDraft), nameof(CanDraftMech_Postfix)));

            harmony.Patch(
                AccessTools.PropertyGetter(typeof(Pawn_DraftController), nameof(Pawn_DraftController.ShowDraftGizmo)),
                postfix: new HarmonyMethod(typeof(Patch_OutpostDefenseMechanoidDraft), nameof(ShowDraftGizmo_Postfix)));

            harmony.Patch(
                AccessTools.Method(typeof(MechanitorUtility), nameof(MechanitorUtility.InMechanitorCommandRange)),
                postfix: new HarmonyMethod(typeof(Patch_OutpostDefenseMechanoidDraft), nameof(InMechanitorCommandRange_Postfix)));

            var canGoFeral = AccessTools.Method(typeof(CompOverseerSubject), "CanGoFeral");
            if (canGoFeral != null)
            {
                harmony.Patch(
                    canGoFeral,
                    prefix: new HarmonyMethod(typeof(Patch_OutpostDefenseMechanoidDraft), nameof(CanGoFeral_Prefix)));
            }
        }

        public static void CanDraftMech_Postfix(Pawn mech, ref AcceptanceReport __result)
        {
            if (__result || !WD_OutpostDefenseMechanoidControlUtil.ShouldBypassMechanitorControl(mech))
                return;
            if (mech.needs?.energy != null && mech.needs.energy.IsLowEnergySelfShutdown)
                return;
            __result = true;
        }

        public static void ShowDraftGizmo_Postfix(Pawn_DraftController __instance, ref bool __result)
        {
            if (__result)
                return;
            if (WD_OutpostDefenseMechanoidControlUtil.ShouldBypassMechanitorControl(__instance.pawn))
                __result = true;
        }

        public static void InMechanitorCommandRange_Postfix(Pawn mech, ref bool __result)
        {
            if (__result)
                return;
            if (WD_OutpostDefenseMechanoidControlUtil.ShouldBypassMechanitorControl(mech))
                __result = true;
        }

        public static bool CanGoFeral_Prefix(Pawn pawn, ref bool __result)
        {
            if (!WD_OutpostDefenseMechanoidControlUtil.ShouldBypassMechanitorControl(pawn))
                return true;
            __result = false;
            return false;
        }
    }
}
