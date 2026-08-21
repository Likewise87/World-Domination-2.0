using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public static class GoodwillCapUtility
    {
        public static int MaxGoodwillCap()
        {
            return Mathf.Max(100, WorldDominationMod.settings?.maxGoodwill ?? WorldDominationSettings.DefMaxGoodwill);
        }
    }

    /// <summary>
    /// Vanilla clamps baseGoodwill to [-100, 100] inside TryAffectGoodwillWith.
    /// Replace the +100 clamp with the configurable WD ceiling.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_Faction_TryAffectGoodwillWith_MaxGoodwill
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Faction), nameof(Faction.TryAffectGoodwillWith),
                new[] { typeof(Faction), typeof(int), typeof(bool), typeof(bool), typeof(HistoryEventDef), typeof(GlobalTargetInfo?) });
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            var clamp = AccessTools.Method(typeof(Mathf), nameof(Mathf.Clamp), new[] { typeof(int), typeof(int), typeof(int) });
            var capGetter = AccessTools.Method(typeof(GoodwillCapUtility), nameof(GoodwillCapUtility.MaxGoodwillCap));

            for (int i = 1; i < list.Count; i++)
            {
                if (!list[i].Calls(clamp)) continue;
                if (!LoadsInt(list[i - 1], 100)) continue;

                list[i - 1].opcode = OpCodes.Call;
                list[i - 1].operand = capGetter;
                break;
            }

            return list;
        }

        private static bool LoadsInt(CodeInstruction instruction, int value) =>
            Patch_GoodwillCap_Shared.LoadsInt(instruction, value);
    }

    /// <summary>
    /// GoodwillWith = Min(baseGoodwill, GetMaxGoodwill). GetMaxGoodwill starts at 100 then
    /// Min()s every cached situation. Situation workers that mean "no extra cap" also return 100,
    /// and Ideology natural-offset situations still get cached with that 100, so the manager Min
    /// re-caps effective goodwill at 100 even after TryAffect stores a higher base.
    /// Raise the uncapped ceiling everywhere it appears as vanilla's +100.
    /// </summary>
    [HarmonyPatch(typeof(GoodwillSituationManager), nameof(GoodwillSituationManager.GetMaxGoodwill))]
    public static class Patch_GoodwillSituationManager_GetMaxGoodwill
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var capGetter = AccessTools.Method(typeof(GoodwillCapUtility), nameof(GoodwillCapUtility.MaxGoodwillCap));
            foreach (CodeInstruction instruction in instructions)
            {
                if (Patch_GoodwillCap_Shared.LoadsInt(instruction, 100))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = capGetter;
                }
                yield return instruction;
            }
        }
    }

    /// <summary>Default situation worker returns +100 as "no restriction"; use WD ceiling instead.</summary>
    [HarmonyPatch(typeof(GoodwillSituationWorker), nameof(GoodwillSituationWorker.GetMaxGoodwill))]
    public static class Patch_GoodwillSituationWorker_GetMaxGoodwill
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var capGetter = AccessTools.Method(typeof(GoodwillCapUtility), nameof(GoodwillCapUtility.MaxGoodwillCap));
            foreach (CodeInstruction instruction in instructions)
            {
                if (Patch_GoodwillCap_Shared.LoadsInt(instruction, 100))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = capGetter;
                }
                yield return instruction;
            }
        }
    }

    /// <summary>
    /// Recalculate only caches situations that restrict below the absolute ceiling
    /// (or have a natural offset). Compare against WD ceiling so "uncapped" workers are filtered correctly.
    /// </summary>
    [HarmonyPatch(typeof(GoodwillSituationManager), "Recalculate", new[] { typeof(Faction), typeof(List<GoodwillSituationManager.CachedSituation>) })]
    public static class Patch_GoodwillSituationManager_Recalculate_Ceiling
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var capGetter = AccessTools.Method(typeof(GoodwillCapUtility), nameof(GoodwillCapUtility.MaxGoodwillCap));
            foreach (CodeInstruction instruction in instructions)
            {
                // Only the blt threshold: maxGoodwill < 100. Leave other constants alone if any.
                if (Patch_GoodwillCap_Shared.LoadsInt(instruction, 100))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = capGetter;
                }
                yield return instruction;
            }
        }
    }

    /// <summary>Inactive attacking-settlement situation returns +100 as uncapped.</summary>
    [HarmonyPatch(typeof(GoodwillSituationWorker_AttackingSettlement), nameof(GoodwillSituationWorker_AttackingSettlement.GetMaxGoodwill))]
    public static class Patch_GoodwillSituationWorker_AttackingSettlement_Uncapped
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var capGetter = AccessTools.Method(typeof(GoodwillCapUtility), nameof(GoodwillCapUtility.MaxGoodwillCap));
            foreach (CodeInstruction instruction in instructions)
            {
                if (Patch_GoodwillCap_Shared.LoadsInt(instruction, 100))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = capGetter;
                }
                yield return instruction;
            }
        }
    }

    /// <summary>Non-permanent-enemy path returns +100 as uncapped.</summary>
    [HarmonyPatch(typeof(GoodwillSituationWorker_PermanentEnemy), nameof(GoodwillSituationWorker_PermanentEnemy.GetMaxGoodwill))]
    public static class Patch_GoodwillSituationWorker_PermanentEnemy_Uncapped
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var capGetter = AccessTools.Method(typeof(GoodwillCapUtility), nameof(GoodwillCapUtility.MaxGoodwillCap));
            foreach (CodeInstruction instruction in instructions)
            {
                if (Patch_GoodwillCap_Shared.LoadsInt(instruction, 100))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = capGetter;
                }
                yield return instruction;
            }
        }
    }

    internal static class Patch_GoodwillCap_Shared
    {
        public static bool LoadsInt(CodeInstruction instruction, int value)
        {
            if (instruction == null) return false;
            if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int intVal && intVal == value)
                return true;
            if (instruction.opcode == OpCodes.Ldc_I4_S)
            {
                try
                {
                    return Convert.ToInt32(instruction.operand) == value;
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }
    }
}
