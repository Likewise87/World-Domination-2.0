using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Optional IRBM (kazepsi.irbm) bridge: AI strategic missiles may target player WD outposts;
    /// strategic impacts apply warhead-tiered outpost strength damage instead of hard-Destroy.
    /// Reflection only — no compile-time reference to IRBM.dll.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class IrbmWdCompat
    {
        public const string PackageId = "kazepsi.irbm";

        private const float UtilityStrengthDamage = 200f;
        private const float VaporizeStrengthFloor = 1500f;
        private const float VaporizeStrengthCap = 5000f;
        private const float VaporizeScale = 0.15f;

        private static bool lookupDone;
        private static bool active;

        private static Type incidentWorkerType;
        private static Type strategicPayloadCompType;
        private static Type strategicPayloadPropsType;
        private static Type utilityType;
        private static Type modType;
        private static Type settingsType;
        private static Type doomsdayMgrType;

        private static MethodInfo findHostileSettlements;
        private static MethodInfo trySelectLaunchSource;
        private static MethodInfo launchMissile;
        private static MethodInfo hasMissileInventory;
        private static MethodInfo canLaunchMissiles;
        private static MethodInfo hasAnyLaunchCapability;
        private static MethodInfo selectWarheadByTech;
        private static MethodInfo selectBodyByTech;
        private static MethodInfo isGlobalProtectionActive;
        private static MethodInfo getInventory;

        private static FieldInfo settingsField;
        private static FieldInfo enableAiEventsField;
        private static FieldInfo enableAiStrategicField;
        private static FieldInfo isNuclearWarActiveField;
        private static FieldInfo destroyWorldObjectsField;
        private static FieldInfo damageAmountField;
        private static FieldInfo damageTimesField;
        private static FieldInfo tileRadiusField;
        private static FieldInfo worldObjectCompPropsField;

        [ThreadStatic]
        private static bool inStrategicImpact;

        [ThreadStatic]
        private static bool suppressIrbmOutpostLetters;

        [ThreadStatic]
        private static bool allowWdOutpostDestroy;

        [ThreadStatic]
        private static HashSet<int> outpostsHandledThisImpact;

        static IrbmWdCompat()
        {
            try
            {
                if (!IsModActive())
                    return;

                if (!BindReflection())
                {
                    Log.Warning("[TSA WD] IRBM compat: reflection bind failed; disabled.");
                    return;
                }

                var harmony = new Harmony("TSA.WorldDomination.IrbmWdCompat");

                MethodInfo canFire = AccessTools.Method(incidentWorkerType, "CanFireNowSub");
                MethodInfo tryExec = AccessTools.Method(incidentWorkerType, "TryExecuteWorker");
                MethodInfo onImpact = AccessTools.Method(strategicPayloadCompType, "OnImpact");
                if (canFire == null || tryExec == null || onImpact == null)
                {
                    Log.Warning("[TSA WD] IRBM compat: incident/impact methods missing; disabled.");
                    return;
                }

                harmony.Patch(canFire,
                    postfix: new HarmonyMethod(typeof(IrbmWdCompat), nameof(PostfixCanFireNowSub)));
                harmony.Patch(tryExec,
                    prefix: new HarmonyMethod(typeof(IrbmWdCompat), nameof(PrefixTryExecuteWorker)));
                harmony.Patch(onImpact,
                    prefix: new HarmonyMethod(typeof(IrbmWdCompat), nameof(PrefixOnImpact)),
                    postfix: new HarmonyMethod(typeof(IrbmWdCompat), nameof(PostfixOnImpact)));

                MethodInfo destroy = AccessTools.Method(typeof(WorldObject), nameof(WorldObject.Destroy), Type.EmptyTypes);
                if (destroy != null)
                {
                    harmony.Patch(destroy,
                        prefix: new HarmonyMethod(typeof(IrbmWdCompat), nameof(PrefixWorldObjectDestroy)));
                }

                MethodInfo receiveLetter = AccessTools.Method(
                    typeof(LetterStack),
                    nameof(LetterStack.ReceiveLetter),
                    new[]
                    {
                        typeof(TaggedString), typeof(TaggedString), typeof(LetterDef), typeof(LookTargets),
                        typeof(Faction), typeof(Quest), typeof(List<ThingDef>), typeof(string), typeof(int), typeof(bool)
                    });
                if (receiveLetter != null)
                {
                    harmony.Patch(receiveLetter,
                        prefix: new HarmonyMethod(typeof(IrbmWdCompat), nameof(PrefixReceiveLetter)));
                }

                active = true;
                Log.Message("[TSA WD] IRBM outpost strategic-missile compat active.");
            }
            catch (Exception ex)
            {
                active = false;
                Log.Warning($"[TSA WD] IRBM compat disabled: {ex.Message}");
            }
        }

        public static bool IsActive
        {
            get
            {
                EnsureLookup();
                return active;
            }
        }

        private static void EnsureLookup()
        {
            if (lookupDone) return;
            lookupDone = true;
            // Constructor already ran bind when mod present; IsActive just exposes result.
        }

        private static bool IsModActive()
        {
            return ModsConfig.IsActive(PackageId)
                || ModsConfig.IsActive(PackageId + "_steam")
                || ModLister.GetActiveModWithIdentifier(PackageId, false) != null;
        }

        private static bool BindReflection()
        {
            incidentWorkerType = AccessTools.TypeByName("IRBM.IncidentWorker_AIFactionStrategicMissile");
            strategicPayloadCompType = AccessTools.TypeByName("IRBM.WorldObjectComp_StrategicPayload");
            strategicPayloadPropsType = AccessTools.TypeByName("IRBM.WorldObjectCompProperties_StrategicPayload");
            utilityType = AccessTools.TypeByName("IRBM.AIFactionMissileUtility");
            modType = AccessTools.TypeByName("IRBM.IRBM_Mod");
            settingsType = AccessTools.TypeByName("IRBM.IRBM_Settings");
            doomsdayMgrType = AccessTools.TypeByName("IRBM.DoomsdayClockManager");

            if (incidentWorkerType == null || strategicPayloadCompType == null
                || strategicPayloadPropsType == null || utilityType == null)
                return false;

            findHostileSettlements = AccessTools.Method(utilityType, "FindHostileSettlements", new[] { typeof(Faction) });
            trySelectLaunchSource = AccessTools.Method(utilityType, "TrySelectLaunchSource");
            launchMissile = AccessTools.Method(utilityType, "LaunchMissile");
            hasMissileInventory = AccessTools.Method(utilityType, "HasMissileInventoryForCategory", new[] { typeof(Faction), typeof(string) });
            canLaunchMissiles = AccessTools.Method(utilityType, "CanLaunchMissiles", new[] { typeof(Faction) });
            hasAnyLaunchCapability = AccessTools.Method(utilityType, "HasAnyLaunchCapability", new[] { typeof(Faction) });
            selectWarheadByTech = AccessTools.Method(utilityType, "SelectWarheadByTechLevel");
            selectBodyByTech = AccessTools.Method(utilityType, "SelectBodyByTechLevel");
            isGlobalProtectionActive = AccessTools.Method(utilityType, "IsGlobalProtectionActive", Type.EmptyTypes);

            if (findHostileSettlements == null || trySelectLaunchSource == null || launchMissile == null
                || hasMissileInventory == null || canLaunchMissiles == null
                || selectWarheadByTech == null || selectBodyByTech == null)
                return false;

            destroyWorldObjectsField = AccessTools.Field(strategicPayloadPropsType, "destroyWorldObjects");
            damageAmountField = AccessTools.Field(strategicPayloadPropsType, "damageAmount");
            damageTimesField = AccessTools.Field(strategicPayloadPropsType, "damageTimes");
            tileRadiusField = AccessTools.Field(strategicPayloadPropsType, "tileRadius");
            worldObjectCompPropsField = AccessTools.Field(typeof(WorldObjectComp), "props");
            if (destroyWorldObjectsField == null || damageAmountField == null
                || damageTimesField == null || tileRadiusField == null || worldObjectCompPropsField == null)
                return false;

            if (modType != null && settingsType != null)
            {
                settingsField = AccessTools.Field(modType, "settings");
                enableAiEventsField = AccessTools.Field(settingsType, "enableAIEvents");
                enableAiStrategicField = AccessTools.Field(settingsType, "enableAIStrategicMissiles");
            }

            if (doomsdayMgrType != null)
            {
                isNuclearWarActiveField = AccessTools.Field(doomsdayMgrType, "isNuclearWarActive");
                getInventory = AccessTools.Method(doomsdayMgrType, "GetInventory", new[] { typeof(Faction) });
            }

            return true;
        }

        // ---- CanFireNowSub: allow fire when only outposts remain as targets ----

        public static void PostfixCanFireNowSub(IncidentParms parms, ref bool __result)
        {
            if (__result || !active) return;
            try
            {
                if (parms?.forced == true && parms.faction != null)
                {
                    if (HasStrategicInventory(parms.faction) && HasHostilePlayerOutpost(parms.faction))
                        __result = true;
                    return;
                }

                if (!AiStrategicEventsEnabled()) return;
                if (IsGlobalProtection()) return;

                foreach (Faction f in Find.FactionManager.AllFactions)
                {
                    if (f == null || f.IsPlayer) continue;
                    if (!CanLaunch(f) || !HasStrategicInventory(f)) continue;
                    if (HasHostilePlayerOutpost(f))
                    {
                        __result = true;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[TSA WD] IRBM CanFireNowSub postfix failed: {ex.Message}");
            }
        }

        // ---- TryExecuteWorker: settlements + player outposts, equal weight ----

        public static bool PrefixTryExecuteWorker(IncidentParms parms, ref bool __result)
        {
            if (!active) return true;
            try
            {
                if (TryLaunchExpandedStrategic(parms))
                {
                    __result = true;
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[TSA WD] IRBM TryExecuteWorker prefix failed: {ex.Message}");
            }

            // No successful expanded launch — let original settlement-only path try.
            return true;
        }

        private static bool TryLaunchExpandedStrategic(IncidentParms parms)
        {
            object clockMgr = doomsdayMgrType != null
                ? Current.Game?.GetComponent(doomsdayMgrType)
                : null;
            bool nuclearWar = clockMgr != null
                && isNuclearWarActiveField != null
                && (bool)isNuclearWarActiveField.GetValue(clockMgr);

            Faction attacker = parms?.faction;
            if (attacker == null)
            {
                var candidates = new List<Faction>();
                foreach (Faction f in Find.FactionManager.AllFactions)
                {
                    if (f == null || f.IsPlayer) continue;
                    if (!CanLaunch(f) || !HasStrategicInventory(f)) continue;
                    if (nuclearWar && getInventory != null)
                    {
                        object inv = getInventory.Invoke(clockMgr, new object[] { f });
                        if (inv is int n && n <= 0) continue;
                    }
                    if (hasAnyLaunchCapability != null
                        && !(bool)hasAnyLaunchCapability.Invoke(null, new object[] { f }))
                        continue;
                    candidates.Add(f);
                }

                if (candidates.Count == 0) return false;
                attacker = candidates.RandomElementByWeight(f =>
                    Mathf.Pow(3f, (float)((int)f.def.techLevel - 5) / 2f));
            }

            if (attacker == null) return false;

            var targets = new List<WorldObject>();
            CollectHostileSettlements(attacker, targets);
            CollectHostilePlayerOutposts(attacker, targets);
            if (targets.Count == 0) return false;

            TechLevel tech = attacker.def.techLevel;
            object warhead = selectWarheadByTech.Invoke(null, new object[] { attacker, tech, "Strategic" });
            if (warhead == null) return false;
            object body = selectBodyByTech.Invoke(null, new object[] { attacker, tech, warhead });
            if (body == null) return false;

            var inRange = new List<WorldObject>();
            for (int i = 0; i < targets.Count; i++)
            {
                WorldObject wo = targets[i];
                if (wo == null || wo.Destroyed) continue;
                if (TrySelectSource(attacker, SurfaceTile(wo.Tile), body, out _, out _, out _))
                    inRange.Add(wo);
            }

            if (inRange.Count == 0) return false;

            WorldObject picked = inRange.RandomElement();
            if (!TrySelectSource(attacker, SurfaceTile(picked.Tile), body, out PlanetTile launchTile, out _, out string sourceName))
                return false;

            Vector3 aim = Find.WorldGrid.GetTileCenter(picked.Tile);
            InvokeLaunchMissile(attacker, body, warhead, launchTile, SurfaceTile(picked.Tile), aim);
            Log.Message($"[TSA WD] IRBM strategic launch: {attacker.Name} from {sourceName} → {picked.Label}");
            return true;
        }

        private static void CollectHostileSettlements(Faction attacker, List<WorldObject> into)
        {
            object listObj = findHostileSettlements.Invoke(null, new object[] { attacker });
            if (listObj is not IList list) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is Settlement s && !s.Destroyed)
                    into.Add(s);
            }
        }

        private static void CollectHostilePlayerOutposts(Faction attacker, List<WorldObject> into)
        {
            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is not WorldObject_WD_Outpost op) continue;
                if (!IsEligiblePlayerOutpostTarget(op, attacker)) continue;
                into.Add(op);
            }
        }

        private static bool HasHostilePlayerOutpost(Faction attacker)
        {
            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_WD_Outpost op && IsEligiblePlayerOutpostTarget(op, attacker))
                    return true;
            }
            return false;
        }

        private static bool IsEligiblePlayerOutpostTarget(WorldObject_WD_Outpost op, Faction attacker)
        {
            if (op == null || op.Destroyed || attacker == null) return false;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(op)) return false;
            if (op.Faction == null || !op.Faction.IsPlayer) return false;
            return attacker.HostileTo(op.Faction);
        }

        // ---- Strategic impact: strength damage + block hard-Destroy on outposts ----

        public static void PrefixOnImpact(WorldObjectComp __instance, PlanetTile tile, IntVec3 cell)
        {
            if (!active) return;
            inStrategicImpact = true;
            suppressIrbmOutpostLetters = true;
            allowWdOutpostDestroy = false;
            outpostsHandledThisImpact = null;
        }

        public static void PostfixOnImpact(WorldObjectComp __instance, PlanetTile tile, IntVec3 cell)
        {
            if (!active) return;
            // Own WD letters must not be suppressed by the IRBM letter filter.
            suppressIrbmOutpostLetters = false;
            try
            {
                ApplyOutpostStrengthHitsInBlast(__instance, tile);
            }
            catch (Exception ex)
            {
                Log.Warning($"[TSA WD] IRBM outpost impact handling failed: {ex.Message}");
            }
            finally
            {
                inStrategicImpact = false;
                suppressIrbmOutpostLetters = false;
                allowWdOutpostDestroy = false;
                outpostsHandledThisImpact = null;
            }
        }

        public static bool PrefixWorldObjectDestroy(WorldObject __instance)
        {
            if (!inStrategicImpact || allowWdOutpostDestroy) return true;
            if (__instance is WorldObject_WD_Outpost)
                return false;
            return true;
        }

        public static bool PrefixReceiveLetter(LookTargets lookTargets)
        {
            if (!suppressIrbmOutpostLetters || lookTargets == null) return true;
            try
            {
                List<GlobalTargetInfo> targets = lookTargets.targets;
                if (targets == null) return true;
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i].WorldObject is WorldObject_WD_Outpost)
                        return false;
                }
            }
            catch
            {
                // ignore look-target shape mismatches
            }
            return true;
        }

        private static void ApplyOutpostStrengthHitsInBlast(WorldObjectComp payloadComp, PlanetTile tile)
        {
            if (payloadComp?.parent == null) return;
            object props = worldObjectCompPropsField.GetValue(payloadComp);
            if (props == null || !strategicPayloadPropsType.IsInstanceOfType(props)) return;

            float strengthDmg = ComputeOutpostStrengthDamage(props);
            if (strengthDmg <= 0f) return;

            int tileRadius = (int)tileRadiusField.GetValue(props);
            World world = Find.World;
            float avgTile = world.grid.AverageTileSize;
            float surfaceRadius = world.grid.Surface.Radius;
            WorldObject missile = payloadComp.parent;
            bool space = tile.LayerDef != null && tile.LayerDef.isSpace;
            Vector3 impactPos;
            if (!space)
            {
                FieldInfo targetVecField = AccessTools.Field(missile.GetType(), "targetVec");
                Vector3 targetVec = targetVecField != null ? (Vector3)targetVecField.GetValue(missile) : Vector3.zero;
                if (targetVec != Vector3.zero && targetVec.magnitude > surfaceRadius + 5f)
                {
                    space = true;
                    impactPos = targetVec;
                }
                else
                    impactPos = world.grid.GetTileCenter(tile);
            }
            else
            {
                FieldInfo targetVecField = AccessTools.Field(missile.GetType(), "targetVec");
                Vector3 targetVec = targetVecField != null ? (Vector3)targetVecField.GetValue(missile) : Vector3.zero;
                impactPos = targetVec != Vector3.zero ? targetVec : world.grid.GetTileCenter(tile);
            }

            float physicsRadius = tileRadius * 0.5f * avgTile;
            if (space)
                physicsRadius = Mathf.Max(physicsRadius, avgTile * 1.5f);

            Faction attackerFaction = missile.Faction;
            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (all[i] is not WorldObject_WD_Outpost op) continue;
                if (op.Destroyed || op == missile) continue;
                if (Vector3.Distance(op.DrawPos, impactPos) > physicsRadius) continue;
                ApplyStrengthHitToOutpost(op, strengthDmg, attackerFaction);
            }
        }

        private static float ComputeOutpostStrengthDamage(object props)
        {
            bool destroyWo = (bool)destroyWorldObjectsField.GetValue(props);
            if (!destroyWo)
                return UtilityStrengthDamage;

            float amount = (float)damageAmountField.GetValue(props);
            int times = (int)damageTimesField.GetValue(props);
            if (amount < 0f) amount = 0f;
            float scaled = amount * Mathf.Max(1, times) * VaporizeScale;
            return Mathf.Min(VaporizeStrengthCap, Mathf.Max(VaporizeStrengthFloor, scaled));
        }

        private static void ApplyStrengthHitToOutpost(WorldObject_WD_Outpost outpost, float potency, Faction attackerFaction)
        {
            if (outpost == null || outpost.Destroyed) return;
            outpostsHandledThisImpact ??= new HashSet<int>();
            if (!outpostsHandledThisImpact.Add(outpost.ID)) return;

            var comp = outpost.GetComponent<CompViralSpread>();
            if (comp == null) return;

            float beforeOff = comp.offensiveStrength;
            float beforeDef = comp.defensiveStrength;
            float off = beforeOff;
            float def = beforeDef;
            MortarFireUtils.ApplyMortarShellToOffensiveDefensiveStrength(potency, ref off, ref def);
            comp.offensiveStrength = off;
            comp.defensiveStrength = def;
            comp.ClampDefensiveStrengthToStructuralMax();

            bool wiped = comp.offensiveStrength + comp.defensiveStrength <= 0f;
            float beforeTotal = beforeOff + beforeDef;
            float afterTotal = comp.offensiveStrength + comp.defensiveStrength;

            string attackerLabel = attackerFaction?.Name ?? "Unknown".Translate().ToString();
            string outpostLabel = outpost.LabelCap;
            TaggedString text = wiped
                ? "TSA_WD_Irbm_OutpostDestroyed_Text".Translate(attackerLabel, outpostLabel, beforeTotal.ToString("F0"))
                : "TSA_WD_Irbm_OutpostHit_Text".Translate(
                    attackerLabel,
                    outpostLabel,
                    Mathf.Max(0f, beforeTotal - afterTotal).ToString("F0"),
                    afterTotal.ToString("F0"));

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            manager?.AddLog(new SpreadLogEntry(text.Resolve(), outpost, outpost));

            LetterDef letterDef = wiped ? LetterDefOf.ThreatBig : LetterDefOf.ThreatSmall;
            Find.LetterStack.ReceiveLetter(
                wiped
                    ? "TSA_WD_Irbm_OutpostDestroyed_Label".Translate()
                    : "TSA_WD_Irbm_OutpostHit_Label".Translate(),
                text,
                letterDef,
                wiped ? new LookTargets(new GlobalTargetInfo(outpost.Tile)) : new LookTargets(outpost));

            if (!wiped) return;

            int tileId = outpost.Tile.tileId;
            string label = outpost.LabelCap;
            Faction ruinFaction = outpost.Faction;
            allowWdOutpostDestroy = true;
            try
            {
                outpost.Destroy();
            }
            finally
            {
                allowWdOutpostDestroy = false;
            }
            WorldObject_WdSettlementRuin.Spawn(tileId, label, ruinFaction);
        }

        // ---- reflected helpers ----

        private static bool AiStrategicEventsEnabled()
        {
            if (settingsField == null || enableAiEventsField == null || enableAiStrategicField == null)
                return true;
            object settings = settingsField.GetValue(null);
            if (settings == null) return true;
            return (bool)enableAiEventsField.GetValue(settings)
                && (bool)enableAiStrategicField.GetValue(settings);
        }

        private static bool IsGlobalProtection()
        {
            if (isGlobalProtectionActive == null) return false;
            return (bool)isGlobalProtectionActive.Invoke(null, null);
        }

        private static bool CanLaunch(Faction f) =>
            f != null && (bool)canLaunchMissiles.Invoke(null, new object[] { f });

        private static bool HasStrategicInventory(Faction f) =>
            f != null && (bool)hasMissileInventory.Invoke(null, new object[] { f, "Strategic" });

        private static PlanetTile SurfaceTile(PlanetTile tile)
        {
            PlanetLayer surface = Find.WorldGrid.Surface;
            if (surface == null) return tile;
            return new PlanetTile(tile.tileId, surface);
        }

        private static bool TrySelectSource(
            Faction faction,
            PlanetTile target,
            object body,
            out PlanetTile launchTile,
            out WorldObject launchSite,
            out string sourceName)
        {
            launchTile = PlanetTile.Invalid;
            launchSite = null;
            sourceName = "Unknown";

            // Optional trailing ignoreRange=false — bind by name; pad to parameter count.
            ParameterInfo[] ps = trySelectLaunchSource.GetParameters();
            var args = new object[ps.Length];
            args[0] = faction;
            args[1] = target;
            args[2] = body;
            args[3] = PlanetTile.Invalid;
            args[4] = null;
            args[5] = null;
            if (ps.Length > 6)
                args[6] = false;

            bool ok = (bool)trySelectLaunchSource.Invoke(null, args);
            if (ok)
            {
                launchTile = (PlanetTile)args[3];
                launchSite = args[4] as WorldObject;
                sourceName = args[5] as string ?? sourceName;
            }
            return ok;
        }

        private static void InvokeLaunchMissile(
            Faction faction,
            object body,
            object warhead,
            PlanetTile launchTile,
            PlanetTile targetTile,
            Vector3 targetVec)
        {
            ParameterInfo[] ps = launchMissile.GetParameters();
            var args = new object[ps.Length];
            args[0] = faction;
            args[1] = body;
            args[2] = warhead;
            args[3] = launchTile;
            args[4] = targetTile;
            if (ps.Length > 5) args[5] = IntVec3.Invalid;
            if (ps.Length > 6) args[6] = targetVec;
            // remaining optional targets stay null / default
            launchMissile.Invoke(null, args);
        }
    }
}
