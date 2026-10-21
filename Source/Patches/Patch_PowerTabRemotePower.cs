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
	/// <summary>
	/// Compatibility with Power Tab 2 Fixed Fork (Owlchemist.PowerTab.forked.RunningBugs).
	/// Injects remote WD power-plant outposts into PowerTabUtility.GeneratePowerAudit.
	/// </summary>
	[StaticConstructorOnStartup]
	public static class Patch_PowerTabRemotePower
	{
		private const string PowerTabPackageId = "Owlchemist.PowerTab.forked.RunningBugs";
		private const string ProxyThingDefName = "TSA_WD_RemotePowerTabSource";

		private static Type powerTabThingType;
		private static ConstructorInfo powerTabThingCtor;
		private static object powerTypeProducer;

		private static FieldInfo thingField;
		private static FieldInfo currentPowerOutputField;
		private static FieldInfo parentTabWidthField;
		private static FieldInfo barFillField;
		private static FieldInfo isBatteryField;

		private static readonly Dictionary<int, Building> proxyBuildingsByOutpostId = new Dictionary<int, Building>();

		static Patch_PowerTabRemotePower()
		{
			if (!IsPowerTabActive())
				return;

			try
			{
				Type utilityType = AccessTools.TypeByName("PowerTab.PowerTabUtility");
				powerTabThingType = AccessTools.TypeByName("PowerTab.UIElements.PowerTabThing");
				Type powerTypeEnumType = AccessTools.TypeByName("PowerTab.PowerTabUtility+PowerType")
					?? utilityType?.GetNestedType("PowerType", BindingFlags.Public | BindingFlags.NonPublic);
				if (utilityType == null || powerTabThingType == null || powerTypeEnumType == null)
					return;

				MethodInfo generateAudit = AccessTools.Method(utilityType, "GeneratePowerAudit", new[] { typeof(Thing) });
				MethodInfo drawThing = AccessTools.Method(powerTabThingType, "Draw", new[] { typeof(float) });
				if (generateAudit == null || drawThing == null)
					return;

				powerTypeProducer = Enum.Parse(powerTypeEnumType, "Producer");
				powerTabThingCtor = AccessTools.Constructor(
					powerTabThingType,
					new[] { typeof(Thing), typeof(float), typeof(float), typeof(float), powerTypeEnumType });
				if (powerTabThingCtor == null)
					return;

				thingField = AccessTools.Field(powerTabThingType, "thing");
				currentPowerOutputField = AccessTools.Field(powerTabThingType, "currentPowerOutput");
				parentTabWidthField = AccessTools.Field(powerTabThingType, "parentTabWidth");
				barFillField = AccessTools.Field(powerTabThingType, "barFill");
				isBatteryField = AccessTools.Field(powerTabThingType, "isBattery");

				var harmony = new Harmony("TSA.WorldDomination.PowerTabCompat");
				harmony.Patch(
					generateAudit,
					postfix: new HarmonyMethod(typeof(Patch_PowerTabRemotePower), nameof(GeneratePowerAuditPostfix)));
				harmony.Patch(
					drawThing,
					prefix: new HarmonyMethod(typeof(Patch_PowerTabRemotePower), nameof(PowerTabThingDrawPrefix)));
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

		public static void GeneratePowerAuditPostfix(Thing SelThing, object __result)
		{
			try
			{
				if (__result == null || SelThing == null)
					return;

				IDictionary dict = __result as IDictionary;
				if (dict == null)
					return;

				CompPower tracking = SelThing.TryGetComp<CompPower>();
				PowerNet net = tracking?.PowerNet;
				if (net == null)
					return;

				List<WorldObject_WD_Outpost> outposts = GetPowerPlantOutposts();
				if (outposts.Count == 0)
					return;

				int netCount = CountPowerNets(net.Map);
				if (netCount <= 0)
					return;

				for (int i = 0; i < outposts.Count; i++)
				{
					WorldObject_WD_Outpost outpost = outposts[i];
					float watts = Outpost_PowerPlant.GetRemotePowerWatts(outpost) / netCount;
					if (watts <= 0f)
						continue;

					Building proxy = GetOrCreateProxyBuilding(outpost, watts);
					if (proxy == null)
						continue;

					object row = powerTabThingCtor.Invoke(new object[]
					{
						proxy,
						watts,
						watts,
						0f,
						powerTypeProducer
					});
					dict[proxy] = row;
				}
			}
			catch (Exception ex)
			{
				Log.WarningOnce($"[TSA WD] Failed to add remote outpost power to Power Tab: {ex.Message}", 0x51A0D51);
			}
		}

		public static bool PowerTabThingDrawPrefix(object __instance, float y)
		{
			Thing thing = thingField?.GetValue(__instance) as Thing;
			if (thing?.def?.defName != ProxyThingDefName)
				return true;

			float parentTabWidth = GetFieldFloat(parentTabWidthField, __instance, 450f);
			try
			{
				Type modType = AccessTools.TypeByName("PowerTab.Mod");
				object powerTab = AccessTools.Field(modType, "powerTab")?.GetValue(null);
				if (powerTab != null)
				{
					object innerSize = AccessTools.Field(powerTab.GetType(), "innerSize")?.GetValue(powerTab);
					if (innerSize is Vector2 size)
						parentTabWidth = size.x;
				}
			}
			catch
			{
				// keep fallback width
			}

			int currentPowerOutput = 0;
			object powerVal = currentPowerOutputField?.GetValue(__instance);
			if (powerVal is int powerInt)
				currentPowerOutput = powerInt;
			else if (powerVal is float powerFloat)
				currentPowerOutput = Mathf.RoundToInt(powerFloat);

			float barFill = GetFieldFloat(barFillField, __instance, 1f);
			bool isBattery = isBatteryField?.GetValue(__instance) is bool battery && battery;

			Rect mainRect = new Rect(0f, y, parentTabWidth - 12f - 16f, 28f);
			Widgets.DrawHighlightIfMouseover(mainRect);
			// No ButtonInvisible / CameraJumper — proxy is not on the map.

			Widgets.ThingIcon(new Rect(0f, y, 28f, 28f), thing);
			Widgets.Label(new Rect(35f, y + 3f, parentTabWidth / 2.5f, 22f), thing.LabelCap);

			Widgets.FillableBar(
				new Rect(parentTabWidth / 2.5f + 40f, y, parentTabWidth / 2f - 25f, 28f).ContractedBy(2f),
				Mathf.Clamp01(barFill));

			string powerDrawStr = isBattery ? $"{currentPowerOutput} Wd" : $"{currentPowerOutput} W";
			float textWidth = Text.CalcSize(powerDrawStr).x;
			Rect wattBkgRect = new Rect(parentTabWidth / 2.5f + 40f, y, textWidth + 16f, 28f);
			Widgets.DrawRectFast(wattBkgRect.ContractedBy(6f), Color.black);
			Widgets.Label(new Rect(wattBkgRect.x + 6f, y + 3f, textWidth, 28f), powerDrawStr);
			return false;
		}

		private static List<WorldObject_WD_Outpost> GetPowerPlantOutposts()
		{
			var result = new List<WorldObject_WD_Outpost>();
			IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
			for (int i = 0; i < outposts.Count; i++)
			{
				WorldObject_WD_Outpost outpost = outposts[i];
				if (outpost != null && !outpost.Destroyed && Outpost_PowerPlant.GetRemotePowerWatts(outpost) > 0f)
					result.Add(outpost);
			}
			return result;
		}

		private static int CountPowerNets(Map map)
		{
			var nets = map?.powerNetManager?.AllNetsListForReading;
			return nets?.Count ?? 0;
		}

		private static Building GetOrCreateProxyBuilding(WorldObject_WD_Outpost outpost, float watts)
		{
			int id = outpost.ID;
			if (!proxyBuildingsByOutpostId.TryGetValue(id, out Building proxy) || proxy == null || proxy.Destroyed)
			{
				ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(ProxyThingDefName);
				if (def == null)
				{
					Log.WarningOnce(
						"[TSA WD] Missing ThingDef TSA_WD_RemotePowerTabSource; cannot display remote outpost power in Power Tab.",
						0x51A0D52);
					return null;
				}

				proxy = ThingMaker.MakeThing(def) as Building;
				if (proxy == null)
				{
					Log.WarningOnce(
						"[TSA WD] TSA_WD_RemotePowerTabSource must use a Building thingClass for Power Tab 2.",
						0x51A0D53);
					return null;
				}
				proxyBuildingsByOutpostId[id] = proxy;
			}

			if (proxy is WD_RemotePowerTabThing remoteThing)
			{
				string outpostLabel = outpost.LabelCap;
				remoteThing.powerTabLabel = string.IsNullOrEmpty(outpostLabel) ? outpost.Label : outpostLabel;
			}

			CompPowerTrader powerTrader = proxy.TryGetComp<CompPowerTrader>();
			if (powerTrader != null)
			{
				powerTrader.PowerOn = true;
				powerTrader.PowerOutput = watts;
			}
			return proxy;
		}

		private static float GetFieldFloat(FieldInfo field, object instance, float fallback)
		{
			object value = field?.GetValue(instance);
			return value is float f ? f : fallback;
		}
	}

	public class WD_RemotePowerTabThing : Building
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
