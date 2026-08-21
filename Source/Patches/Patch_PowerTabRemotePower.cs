using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

#nullable disable

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Patch_PowerTabRemotePower
    {
        private const string PowerTabPackageId = "Mersid.PowerTab";
        private const string ProxyThingDefName = "TSA_WD_RemotePowerTabSource";

        private static Type powerTabType;
        private static Type compPowerTrackerType;
        private static PropertyInfo trackingProperty;
        private static PropertyInfo powerTrackersProperty;
        private static FieldInfo powerTrackersField;
        private static FieldInfo maxObservedPowerOutputField;

        private static Type powerTabThingType;
        private static PropertyInfo thingProperty;
        private static PropertyInfo powerProperty;
        private static FieldInfo barFillField;
        private static FieldInfo parentTabWidthField;

        private static readonly Dictionary<int, ThingWithComps> proxyThingsByOutpostId = new Dictionary<int, ThingWithComps>();
        private static readonly Dictionary<int, object> proxyTrackersByOutpostId = new Dictionary<int, object>();

        static Patch_PowerTabRemotePower()
        {
            if (!IsPowerTabActive())
                return;

            try
            {
                powerTabType = AccessTools.TypeByName("PowerTab.PowerTab");
                compPowerTrackerType = AccessTools.TypeByName("PowerTab.CompPowerTracker");
                powerTabThingType = AccessTools.TypeByName("PowerTab.UIElements.PowerTabThing");
                if (powerTabType == null || compPowerTrackerType == null || powerTabThingType == null)
                    return;

                MethodInfo buildTrackers = AccessTools.Method(powerTabType, "BuildTrackers");
                MethodInfo drawThing = AccessTools.Method(powerTabThingType, "Draw", new[] { typeof(float) });
                if (buildTrackers == null || drawThing == null)
                    return;

                trackingProperty = AccessTools.Property(powerTabType, "Tracking");
                powerTrackersProperty = AccessTools.Property(powerTabType, "PowerTrackers");
                powerTrackersField = AccessTools.Field(powerTabType, "<PowerTrackers>k__BackingField");
                maxObservedPowerOutputField = AccessTools.Field(compPowerTrackerType, "_maxObservedPowerOutput");

                thingProperty = AccessTools.Property(powerTabThingType, "Thing");
                powerProperty = AccessTools.Property(powerTabThingType, "Power");
                barFillField = AccessTools.Field(powerTabThingType, "_barFill");
                parentTabWidthField = AccessTools.Field(powerTabThingType, "_parentTabWidth");

                var harmony = new Harmony("TSA.WorldDomination.PowerTabCompat");
                harmony.Patch(buildTrackers, postfix: new HarmonyMethod(typeof(Patch_PowerTabRemotePower), nameof(BuildTrackersPostfix)));
                harmony.Patch(drawThing, prefix: new HarmonyMethod(typeof(Patch_PowerTabRemotePower), nameof(PowerTabThingDrawPrefix)));
            }
            catch (Exception ex)
            {
                Log.Warning($"[TSA WD] Power Tab compatibility hook disabled: {ex.Message}");
            }
        }

        private static bool IsPowerTabActive()
        {
            if (ModsConfig.IsActive(PowerTabPackageId) || ModsConfig.IsActive(PowerTabPackageId + "_steam"))
                return true;

            var mods = LoadedModManager.RunningModsListForReading;
            for (int i = 0; i < mods.Count; i++)
            {
                string packageId = mods[i]?.PackageId;
                if (string.Equals(packageId, PowerTabPackageId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(packageId, PowerTabPackageId + "_steam", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static void BuildTrackersPostfix(object __instance)
        {
            try
            {
                CompPower tracking = trackingProperty?.GetValue(__instance, null) as CompPower;
                PowerNet net = tracking?.PowerNet;
                if (net == null) return;

                IList trackers = GetPowerTrackers(__instance);
                if (trackers == null) return;

                List<WorldObject_WD_Outpost> outposts = GetPowerPlantOutposts();
                if (outposts.Count == 0) return;

                int netCount = CountPowerNets(net.Map);
                if (netCount <= 0) return;

                for (int i = 0; i < outposts.Count; i++)
                {
                    WorldObject_WD_Outpost outpost = outposts[i];
                    float watts = Outpost_PowerPlant.GetRemotePowerWatts(outpost) / netCount;
                    if (watts <= 0f) continue;

                    object tracker = GetOrCreateProxyTracker(outpost, watts);
                    if (tracker != null && !trackers.Contains(tracker))
                        trackers.Add(tracker);
                }
            }
            catch (Exception ex)
            {
                Log.WarningOnce($"[TSA WD] Failed to add remote outpost power to Power Tab: {ex.Message}", 0x51A0D51);
            }
        }

        public static bool PowerTabThingDrawPrefix(object __instance, float y)
        {
            Thing thing = thingProperty?.GetValue(__instance, null) as Thing;
            if (thing?.def?.defName != ProxyThingDefName)
                return true;

            float parentTabWidth = GetFieldFloat(parentTabWidthField, __instance, 450f);
            float power = GetPropertyFloat(powerProperty, __instance, 0f);
            float barFill = GetFieldFloat(barFillField, __instance, 1f);

            Rect mainRect = new Rect(0f, y, parentTabWidth - GenUI.GapTiny * 3f - GenUI.ScrollBarWidth, GenUI.ListSpacing);
            Widgets.DrawHighlightIfMouseover(mainRect);

            Rect iconRect = new Rect(0f, y, GenUI.ListSpacing, GenUI.ListSpacing);
            Widgets.ThingIcon(iconRect, thing);

            Rect labelRect = new Rect(35f, y + 3f, parentTabWidth / 2.5f, Text.SmallFontHeight);
            Widgets.Label(labelRect, thing.LabelCap);

            Rect barRect = new Rect(parentTabWidth / 2.5f + 40f, y, parentTabWidth / 2f - 25f, GenUI.ListSpacing);
            Widgets.FillableBar(barRect.ContractedBy(2f), Mathf.Clamp01(barFill));

            string powerDrawStr = $"{power:F0} W";
            float textWidth = Text.CalcSize(powerDrawStr).x;
            Rect wattBkgRect = new Rect(parentTabWidth / 2.5f + 40f, y, textWidth + 16f, GenUI.ListSpacing);
            Widgets.DrawRectFast(wattBkgRect.ContractedBy(GenUI.GapTiny * 1.5f), Color.black);

            Rect wattLabelRect = new Rect(wattBkgRect.x + 6f, y + 3f, textWidth, GenUI.ListSpacing);
            Widgets.Label(wattLabelRect, powerDrawStr);
            return false;
        }

        private static IList GetPowerTrackers(object powerTab)
        {
            return powerTrackersProperty?.GetValue(powerTab, null) as IList
                ?? powerTrackersField?.GetValue(powerTab) as IList;
        }

        private static List<WorldObject_WD_Outpost> GetPowerPlantOutposts()
        {
            var result = new List<WorldObject_WD_Outpost>();
            if (Find.WorldObjects == null) return result;

            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_WD_Outpost outpost && Outpost_PowerPlant.GetRemotePowerWatts(outpost) > 0f)
                    result.Add(outpost);
            }
            return result;
        }

        private static int CountPowerNets(Map map)
        {
            var nets = map?.powerNetManager?.AllNetsListForReading;
            return nets?.Count ?? 0;
        }

        private static object GetOrCreateProxyTracker(WorldObject_WD_Outpost outpost, float watts)
        {
            ThingWithComps thing = GetOrCreateProxyThing(outpost, watts);
            if (thing == null || compPowerTrackerType == null) return null;

            int id = outpost.ID;
            if (!proxyTrackersByOutpostId.TryGetValue(id, out object tracker) || !compPowerTrackerType.IsInstanceOfType(tracker))
            {
                tracker = Activator.CreateInstance(compPowerTrackerType);
                var trackerComp = tracker as ThingComp;
                if (trackerComp == null) return null;

                trackerComp.parent = thing;
                if (!thing.AllComps.Contains(trackerComp))
                    thing.AllComps.Add(trackerComp);
                proxyTrackersByOutpostId[id] = tracker;
            }

            var comp = tracker as ThingComp;
            if (comp != null && comp.parent != thing)
                comp.parent = thing;

            maxObservedPowerOutputField?.SetValue(tracker, watts);
            return tracker;
        }

        private static ThingWithComps GetOrCreateProxyThing(WorldObject_WD_Outpost outpost, float watts)
        {
            int id = outpost.ID;
            if (!proxyThingsByOutpostId.TryGetValue(id, out ThingWithComps proxyThing) || proxyThing == null || proxyThing.Destroyed)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(ProxyThingDefName);
                if (def == null)
                {
                    Log.WarningOnce("[TSA WD] Missing ThingDef TSA_WD_RemotePowerTabSource; cannot display remote outpost power in Power Tab.", 0x51A0D52);
                    return null;
                }
                proxyThing = ThingMaker.MakeThing(def) as ThingWithComps;
                proxyThingsByOutpostId[id] = proxyThing;
            }

            if (proxyThing is WD_RemotePowerTabThing remoteThing)
            {
                string outpostLabel = outpost.LabelCap;
                remoteThing.powerTabLabel = string.IsNullOrEmpty(outpostLabel) ? outpost.Label : outpostLabel;
            }

            CompPowerTrader powerTrader = proxyThing?.TryGetComp<CompPowerTrader>();
            if (powerTrader != null)
            {
                powerTrader.PowerOn = true;
                powerTrader.PowerOutput = watts;
            }
            return proxyThing;
        }

        private static float GetFieldFloat(FieldInfo field, object instance, float fallback)
        {
            object value = field?.GetValue(instance);
            return value is float f ? f : fallback;
        }

        private static float GetPropertyFloat(PropertyInfo property, object instance, float fallback)
        {
            object value = property?.GetValue(instance, null);
            return value is float f ? f : fallback;
        }
    }

    public class WD_RemotePowerTabThing : ThingWithComps
    {
        public string powerTabLabel;

        public override string Label => string.IsNullOrEmpty(powerTabLabel) ? base.Label : powerTabLabel;
        public override string LabelNoCount => Label;
        public override string LabelCap => Label.CapitalizeFirst();
        public override string LabelCapNoCount => LabelCap;
        public override string LabelShort => Label;
        public override string LabelShortCap => LabelCap;
        public override string LabelMouseover => LabelCap;
    }
}
