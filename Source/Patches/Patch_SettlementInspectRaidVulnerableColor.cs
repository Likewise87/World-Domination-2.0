using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Patch_SettlementInspectRaidVulnerableColor
    {
        static Patch_SettlementInspectRaidVulnerableColor()
        {
            var harmony = new Harmony("TSA.WorldDomination.SettlementInspectRaidVulnerableColor");
            harmony.Patch(
                AccessTools.Method(typeof(Settlement), nameof(Settlement.GetInspectString)),
                postfix: new HarmonyMethod(typeof(Patch_SettlementInspectRaidVulnerableColor), nameof(Postfix))
            );
        }

        public static void Postfix(Settlement __instance, ref string __result)
        {
            if (__instance?.Faction?.IsPlayer != true) return;
            if (__instance.GetComponent<CompViralSpread>() == null) return;

            __result = CompViralSpread.ApplyPlayerSettlementInspectColors(__result);
        }
    }
}
