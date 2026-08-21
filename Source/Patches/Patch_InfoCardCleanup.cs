using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>When a Dialog_InfoCard opened for a temp pawn (from WD outpost tab) is closed, destroy that pawn so we don't leak.</summary>
    [HarmonyPatch(typeof(Dialog_InfoCard))]
    public static class Patch_Dialog_InfoCard_PreClose
    {
        static MethodBase TargetMethod()
        {
            try
            {
                return AccessTools.Method(typeof(Dialog_InfoCard), "PreClose")
                    ?? AccessTools.Method(typeof(Window), "PreClose");
            }
            catch
            {
                return null;
            }
        }

        public static readonly HashSet<Pawn> TempPawnsForInfoCard = new HashSet<Pawn>();

        [HarmonyPostfix]
        public static void Postfix(Window __instance)
        {
            var thing = Traverse.Create(__instance).Field("thing").GetValue<Thing>();
            if (thing is Pawn pawn && TempPawnsForInfoCard.Contains(pawn))
            {
                TempPawnsForInfoCard.Remove(pawn);
                if (!pawn.Destroyed)
                    pawn.Destroy(DestroyMode.Vanish);
            }
        }
    }
}
