using HarmonyLib;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// <see cref="Faction.IsPlayer"/> is <c>def.isPlayer</c>. Settlements can keep a Faction
    /// whose <c>def</c> is null after a removed faction mod / corrupt load; every tick that
    /// calls IsPlayer then NREs. Treat null-def factions as non-player.
    /// </summary>
    [HarmonyPatch(typeof(Faction), nameof(Faction.IsPlayer), MethodType.Getter)]
    public static class Patch_Faction_IsPlayer_NullDef
    {
        public static bool Prefix(Faction __instance, ref bool __result)
        {
            if (__instance == null || __instance.def == null)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// <see cref="Faction.Hidden"/> is <c>hidden ?? def.hidden</c>. Same null-def survivors
    /// NRE when the nullable override is unset. Treat null-def as hidden (exclude from sim UI).
    /// </summary>
    [HarmonyPatch(typeof(Faction), nameof(Faction.Hidden), MethodType.Getter)]
    public static class Patch_Faction_Hidden_NullDef
    {
        public static bool Prefix(Faction __instance, ref bool __result)
        {
            if (__instance == null || __instance.def == null)
            {
                __result = true;
                return false;
            }
            return true;
        }
    }
}
