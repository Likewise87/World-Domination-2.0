using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Optional <c>telardo.DragSelect</c> bridge. No compile-time typerefs to DragSelect.dll —
    /// resolve via reflection so WD works with or without that mod (and survives API TypeLoads).
    /// </summary>
    internal static class WdDragSelectAccess
    {
        private const string PackageId = "telardo.DragSelect";
        private const string PackageIdSteam = "telardo.DragSelect_steam";

        private static bool resolved;
        private static bool available;
        private static MethodInfo buttonInvisibleDraggable;
        private static object leftPressedValue;

        /// <summary>True when DragSelect is active and <c>ButtonInvisibleDraggable</c> resolved.</summary>
        public static bool IsAvailable
        {
            get
            {
                EnsureResolved();
                return available;
            }
        }

        /// <summary>
        /// Returns true when DragSelect reports LeftPressed for this control.
        /// False if DragSelect is missing, failed to resolve, or did not fire.
        /// </summary>
        public static bool TryLeftPressed(Rect rect, int stableHash)
        {
            EnsureResolved();
            if (!available || buttonInvisibleDraggable == null || leftPressedValue == null)
                return false;

            try
            {
                object result = buttonInvisibleDraggable.Invoke(
                    null,
                    new object[] { rect, false, stableHash });
                return result != null && result.Equals(leftPressedValue);
            }
            catch (Exception ex)
            {
                available = false;
                if (Prefs.DevMode)
                    Log.Warning($"[WorldDomination] DragSelect ButtonInvisibleDraggable failed; falling back to vanilla input. {ex.InnerException?.Message ?? ex.Message}");
                return false;
            }
        }

        private static void EnsureResolved()
        {
            if (resolved) return;
            resolved = true;
            available = false;
            buttonInvisibleDraggable = null;
            leftPressedValue = null;

            try
            {
                if (!ModsConfig.IsActive(PackageId) && !ModsConfig.IsActive(PackageIdSteam))
                    return;

                Type utilityType = AccessTools.TypeByName("DragSelect.DraggingUtility");
                if (utilityType == null)
                    return;

                buttonInvisibleDraggable = AccessTools.Method(
                    utilityType,
                    "ButtonInvisibleDraggable",
                    new[] { typeof(Rect), typeof(bool), typeof(int) });
                if (buttonInvisibleDraggable == null)
                    return;

                Type resultEnum = AccessTools.TypeByName("DragSelect.DraggingUtility+MyDraggableResult")
                    ?? utilityType.GetNestedType("MyDraggableResult", BindingFlags.Public | BindingFlags.NonPublic);
                if (resultEnum == null || !resultEnum.IsEnum)
                    return;

                leftPressedValue = Enum.Parse(resultEnum, "LeftPressed");
                available = true;
            }
            catch (Exception ex)
            {
                available = false;
                buttonInvisibleDraggable = null;
                leftPressedValue = null;
                if (Prefs.DevMode)
                    Log.Warning($"[WorldDomination] DragSelect soft-dep resolve failed; using vanilla input. {ex.Message}");
            }
        }
    }
}
