using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using UnityEngine;

namespace TSA_WorldDomination
{
    public static partial class WorldActions_Traveler
    {
        /// <summary>Must match <see cref="WorldObject_Traveler.ticksPerMove"/> on spawned mortar shells (see <see cref="WD_PathFollower"/> single-hop timing). Default for settings.</summary>
        public const int MortarShellTicksPerMove = 12;

        /// <summary>AA flak: lower = faster. Default for settings.</summary>
        public const int FlakShellTicksPerMove = 5;

        public static int GetMortarShellTicksPerMove()
        {
            float v = WorldDominationMod.settings?.mortarShellTicksPerMove ?? MortarShellTicksPerMove;
            return Mathf.Max(1, Mathf.RoundToInt(v));
        }

        public static int GetFlakShellTicksPerMove()
        {
            float v = WorldDominationMod.settings?.flakShellTicksPerMove ?? FlakShellTicksPerMove;
            return Mathf.Max(1, Mathf.RoundToInt(v));
        }
        /// <summary>Resolve a queued mortar shell: hit/miss letter; on hit, chip the same abstract strength total for settlements, caravans, or WD travelers.</summary>
        private static void ExecuteMortarStrike(WorldObject_Traveler traveler)
        {
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            WorldObject impactTarget = ResolveMortarImpactTarget(traveler);
            WorldObject fxAt = impactTarget ?? traveler.targetObject;
            if (fxAt != null && !fxAt.Destroyed)
                MortarWorldFx.NotifyMortarImpactHit(fxAt);
            else if (traveler != null && !traveler.Destroyed)
                MortarWorldFx.NotifyMortarImpactHit(traveler);

            // Miss: AT shells use their own log (never mortar miss letters).
            if (!traveler.mortarHit)
            {
                if (traveler.originObject is WorldObject_AT_Turret atGun)
                {
                    AtTurretNotifyUtility.NotifyShellMiss(atGun, impactTarget ?? traveler.targetObject);
                    return;
                }

                string originLabel = traveler.originObject?.LabelCap ?? traveler.Faction?.Name ?? "?";
                string targetLabel = impactTarget?.LabelCap ?? traveler.targetObject?.LabelCap ?? "?";
                string missText = "TSA_WD_Mortar_Miss_Text".Translate(originLabel, targetLabel);
                var lookMiss = impactTarget != null && !impactTarget.Destroyed ? new LookTargets(impactTarget) : new LookTargets(traveler);
                manager?.AddLog(new SpreadLogEntry(missText, traveler.originObject, impactTarget ?? traveler.targetObject));
                if (ShouldShowMortarLetter(WorldDominationMod.settings, traveler.originObject, impactTarget ?? traveler.targetObject, isHit: false, out _))
                {
                    LetterDef missLetter = IsPlayerFactionObject(traveler.originObject)
                        ? (DefDatabase<LetterDef>.GetNamedSilentFail("TSA_WD_NeutralSilent") ?? LetterDefOf.NeutralEvent)
                        : LetterDefOf.NeutralEvent;
                    Find.LetterStack.ReceiveLetter(
                        "TSA_WD_Mortar_Miss_Label".Translate(),
                        missText,
                        missLetter,
                        lookMiss);
                }
                return;
            }

            // Rolled a hit but the target is already gone (e.g. another shell wiped it first).
            if (impactTarget == null || impactTarget.Destroyed)
            {
                if (traveler.originObject is WorldObject_AT_Turret atGunGone)
                    AtTurretNotifyUtility.NotifyShellMiss(atGunGone, impactTarget ?? traveler.targetObject);
                return;
            }

            float shellPotency = Mathf.Max(0f, traveler.mortarDamage);

            if (impactTarget is Settlement settlement)
            {
                ApplyMortarHitToSettlement(traveler, manager, settlement, shellPotency);
                return;
            }

            if (impactTarget is Caravan caravan)
            {
                ApplyMortarHitToCaravan(traveler, manager, caravan, shellPotency);
                return;
            }

            if (impactTarget is WorldObject_WD_Outpost outpostTarget)
            {
                ApplyMortarHitToOutpost(traveler, manager, outpostTarget, shellPotency);
                return;
            }

            if (impactTarget is WorldObject_AT_Turret atTurretTarget)
            {
                ApplyMortarHitToAtTurret(traveler, manager, atTurretTarget, shellPotency);
                return;
            }

            if (impactTarget is WorldObject_Traveler wt)
            {
                if (wt.mission == TravelerMission.MortarStrike || wt.mission == TravelerMission.AntiAirStrike) return;
                ApplyMortarHitToWdTraveler(traveler, manager, wt, shellPotency);
            }
        }

        private static WorldObject ResolveMortarImpactTarget(WorldObject_Traveler traveler)
        {
            if (traveler == null) return null;
            WorldObject target = traveler.targetObject;
            if (target == null || target.Destroyed) return null;
            return target;
        }

        private static void PostMortarStrengthHitLetter(WorldComponent_SpreadManager manager, WorldObject origin, WorldObject lookTarget, float beforeTotal, float afterTotal, bool wiped, string destroyedSuffixKey)
        {
            if (origin is WorldObject_AT_Turret atTurret)
            {
                AtTurretNotifyUtility.NotifyShellOrStrengthHit(manager, atTurret, lookTarget, beforeTotal, afterTotal, wiped);
                return;
            }

            var seth = WorldDominationMod.settings;
            bool playerOrigin = IsPlayerFactionObject(origin);
            bool playerTarget = IsPlayerFactionObject(lookTarget);

            // Player mortar: only letter when the target is wiped; skip the useless Strength XXâ†’YY notice.
            if (playerOrigin)
            {
                string originLabel = origin?.LabelCap ?? "?";
                string targetKind = FormatMortarTargetLabel(lookTarget);
                TaggedString logText = wiped
                    ? "TSA_WD_Mortar_Destroyed_Text".Translate(originLabel, targetKind)
                    : "TSA_WD_Mortar_Hit_Settlement_Line".Translate(beforeTotal.ToString("F0"), afterTotal.ToString("F0"));
                manager?.AddLog(new SpreadLogEntry(logText.Resolve(), origin, lookTarget));

                if (!wiped) return;
                if (!ShouldShowMortarLetter(seth, origin, lookTarget, isHit: true, out _)) return;

                LetterDef silentBlue = DefDatabase<LetterDef>.GetNamedSilentFail("TSA_WD_NeutralSilent")
                    ?? LetterDefOf.NeutralEvent;
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_Mortar_Destroyed_Label".Translate(),
                    "TSA_WD_Mortar_Destroyed_Text".Translate(originLabel, targetKind),
                    silentBlue,
                    lookTarget != null && !lookTarget.Destroyed ? new LookTargets(lookTarget) : new LookTargets(origin));
                return;
            }

            // Enemy mortar hitting your assets: who fired, what was hit, strength, destroyed or not.
            if (playerTarget)
            {
                string attackerLabel = FormatMortarAttackerLabel(origin);
                string yourAsset = FormatMortarTargetLabel(lookTarget);
                TaggedString text = wiped
                    ? "TSA_WD_Mortar_DestroyedYou_Text".Translate(attackerLabel, yourAsset)
                    : "TSA_WD_Mortar_HitOnYou_Text".Translate(
                        attackerLabel,
                        yourAsset,
                        beforeTotal.ToString("F0"),
                        afterTotal.ToString("F0"));
                manager?.AddLog(new SpreadLogEntry(text.Resolve(), origin, lookTarget));
                if (!ShouldShowMortarLetter(seth, origin, lookTarget, isHit: true, out LetterDef hitYouLetter)) return;
                Find.LetterStack.ReceiveLetter(
                    wiped
                        ? "TSA_WD_Mortar_DestroyedYou_Label".Translate()
                        : "TSA_WD_Mortar_HitOnYou_Label".Translate(),
                    text,
                    hitYouLetter,
                    lookTarget != null && !lookTarget.Destroyed ? new LookTargets(lookTarget) : new LookTargets(origin));
                return;
            }

            TaggedString npcText = "TSA_WD_Mortar_Hit_Settlement_Line".Translate(
                beforeTotal.ToString("F0"),
                afterTotal.ToString("F0"));
            if (wiped)
                npcText += destroyedSuffixKey.Translate();
            manager?.AddLog(new SpreadLogEntry(npcText.Resolve(), origin, lookTarget));
            if (ShouldShowMortarLetter(seth, origin, lookTarget, isHit: true, out LetterDef letterDef))
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_Mortar_Hit_Label".Translate(),
                    npcText,
                    letterDef,
                    new LookTargets(lookTarget));
            }
        }

        /// <summary>Attacker label for enemy mortar letters (settlement name, with faction when useful).</summary>
        private static string FormatMortarAttackerLabel(WorldObject origin)
        {
            if (origin == null) return "?";
            string name = origin.LabelCap;
            if (origin.Faction != null && !origin.Faction.IsPlayer && !origin.Faction.Hidden)
            {
                string factionName = origin.Faction.Name;
                if (!factionName.NullOrEmpty() && name.IndexOf(factionName, StringComparison.OrdinalIgnoreCase) < 0)
                    return "TSA_WD_Mortar_AttackerWithFaction".Translate(name, factionName).ToString();
            }
            return name;
        }

        /// <summary>Short target description (mission type for WD travelers, outpost/settlement name otherwise).</summary>
        private static string FormatMortarTargetLabel(WorldObject target)
        {
            if (target == null) return "?";
            if (target is WorldObject_Traveler traveler)
                return WorldObject_Traveler.GetMissionTypeLabel(traveler.mission);
            if (target is Caravan)
            {
                string factionName = target.Faction?.Name;
                if (!factionName.NullOrEmpty())
                    return "TSA_WD_Mortar_Destroyed_CaravanWithFaction".Translate(factionName).ToString();
                return "TSA_WD_Mortar_Destroyed_Caravan".Translate().ToString();
            }
            return target.LabelCap;
        }

        /// <summary>True if a world object belongs to the player faction (WD outpost, shell, settlement, etc.).</summary>
        private static bool IsPlayerFactionObject(WorldObject wo)
            => wo?.Faction != null && wo.Faction.IsPlayer;

        /// <summary>
        /// Airborne AA target owned by the player: player-faction traveler/shell/pod, or one launched from a player-faction origin.
        /// </summary>
        private static bool IsPlayerOwnedAirborne(WorldObject airborne)
        {
            if (airborne == null) return false;
            if (IsPlayerFactionObject(airborne)) return true;
            if (airborne is WorldObject_Traveler t && IsPlayerFactionObject(t.originObject))
                return true;
            return false;
        }

        /// <summary>
        /// Decides whether a mortar letter should be shown and with which tone, classifying the shot into one of three
        /// player-configurable buckets: your mortar firing (neutral), enemy mortar hitting you (negative), or
        /// enemy-vs-enemy fire (neutral, off by default to avoid spam).
        /// </summary>
        private static bool ShouldShowMortarLetter(WorldDominationSettings seth, WorldObject origin, WorldObject target, bool isHit, out LetterDef letterDef)
        {
            // 1. Your mortar outpost firing at a target.
            if (IsPlayerFactionObject(origin))
            {
                letterDef = LetterDefOf.NeutralEvent;
                return seth == null || seth.notifyMortarHit;
            }
            // 2. Enemy mortar firing at one of your WD assets.
            if (IsPlayerFactionObject(target))
            {
                letterDef = isHit ? LetterDefOf.NegativeEvent : LetterDefOf.NeutralEvent;
                return seth == null || seth.notifyNpcMortarHitPlayer;
            }
            // 3. Enemy mortar firing at another NPC.
            letterDef = LetterDefOf.NeutralEvent;
            return seth != null && seth.notifyNpcMortarHitNpc;
        }

        private static void ApplyMortarHitToSettlement(WorldObject_Traveler shell, WorldComponent_SpreadManager manager, Settlement settlement, float shellPotency)
        {
            if (settlement.Destroyed) return;
            var comp = settlement.GetComponent<CompViralSpread>();
            if (comp == null) return;
            if (comp.IsPlayerMapSettlement) return;

            float beforeOff = comp.offensiveStrength;
            float beforeDef = comp.defensiveStrength;
            float off = comp.offensiveStrength;
            float def = comp.defensiveStrength;
            MortarFireUtils.ApplyMortarShellToOffensiveDefensiveStrength(shellPotency, ref off, ref def);
            comp.offensiveStrength = off;
            comp.defensiveStrength = def;
            comp.ClampDefensiveStrengthToStructuralMax();
            comp.CheckTierUpdate(allowDemotion: true);

            bool allowWipeFromMortar = !comp.IsPlayerMapSettlement && settlement.Faction != null && !settlement.Faction.IsPlayer;
            bool wiped = allowWipeFromMortar && comp.offensiveStrength + comp.defensiveStrength <= 0f;

            float beforeTotal = beforeOff + beforeDef;
            float afterTotal = comp.offensiveStrength + comp.defensiveStrength;
            PostMortarStrengthHitLetter(manager, shell.originObject, settlement, beforeTotal, afterTotal, wiped, "TSA_WD_Mortar_Hit_DestroyedSuffix");
            if (wiped)
                settlement.Destroy();
        }

        private static void ApplyMortarHitToOutpost(WorldObject_Traveler shell, WorldComponent_SpreadManager manager, WorldObject_WD_Outpost outpost, float shellPotency)
        {
            if (outpost == null || outpost.Destroyed) return;
            var comp = outpost.GetComponent<CompViralSpread>();
            if (comp == null) return;

            float beforeOff = comp.offensiveStrength;
            float beforeDef = comp.defensiveStrength;
            float off = comp.offensiveStrength;
            float def = comp.defensiveStrength;
            MortarFireUtils.ApplyMortarShellToOffensiveDefensiveStrength(shellPotency, ref off, ref def);
            comp.offensiveStrength = off;
            comp.defensiveStrength = def;
            comp.ClampDefensiveStrengthToStructuralMax();

            // Sustained enemy bombardment can wipe a player outpost once its strength is fully depleted. Reuse the
            // raid path's teardown: WorldObject.Destroy() drops the outpost and its deep-scribed occupants with it
            // (same as Raid_Simulated.HandlePlayerOutpostRaidArrival).
            bool wiped = comp.offensiveStrength + comp.defensiveStrength <= 0f;

            float beforeTotal = beforeOff + beforeDef;
            float afterTotal = comp.offensiveStrength + comp.defensiveStrength;
            PostMortarStrengthHitLetter(manager, shell.originObject, outpost, beforeTotal, afterTotal, wiped, "TSA_WD_Mortar_Hit_DestroyedOutpostSuffix");
            if (wiped)
                outpost.Destroy();
        }

        private static void ApplyMortarHitToAtTurret(WorldObject_Traveler shell, WorldComponent_SpreadManager manager, WorldObject_AT_Turret turret, float shellPotency)
        {
            if (turret == null || turret.Destroyed) return;
            float before = turret.strength;
            turret.strength = Mathf.Max(0f, before - Mathf.Max(0f, shellPotency));
            float after = turret.strength;
            bool wiped = after <= 0.01f;
            PostMortarStrengthHitLetter(manager, shell.originObject, turret, before, after, wiped, "TSA_WD_Mortar_Hit_DestroyedMobileSuffix");
            if (wiped)
            {
                // Mortar letter owns the wipe notice; skip the generic AT-destroyed letter.
                turret.suppressDestroyedLetter = true;
                turret.Destroy();
            }
        }

        private static void ApplyMortarHitToCaravan(WorldObject_Traveler shell, WorldComponent_SpreadManager manager, Caravan caravan, float shellPotency)
        {
            if (caravan == null || caravan.Destroyed) return;

            // NPC AT shells vs player caravans: tiered pawn kill/wound, not vitality.
            if (shell?.originObject is WorldObject_AT_Turret atGun
                && caravan.Faction?.IsPlayer == true
                && !AtTurretNotifyUtility.IsPlayerFaction(atGun))
            {
                ApplyAtTurretHitToPlayerCaravan(atGun, caravan);
                return;
            }

            manager ??= Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null) return;
            manager.ApplyMortarShellToCaravanVitality(caravan, shellPotency, out float beforePool, out float afterPool, out bool depleted);
            PostMortarStrengthHitLetter(manager, shell.originObject, caravan, beforePool, afterPool, depleted, "TSA_WD_Mortar_Hit_DestroyedMobileSuffix");
            if (depleted)
                caravan.Destroy();
        }

        /// <summary>
        /// AT shell vs player caravan: Light/Medium hit 1 pawn (60% wound / 40% kill);
        /// Heavy hits up to 2 distinct pawns with independent rolls. Not the AA wipe lottery.
        /// </summary>
        private static void ApplyAtTurretHitToPlayerCaravan(
            WorldObject_AT_Turret origin,
            Caravan caravan)
        {
            if (caravan == null || caravan.Destroyed || origin == null) return;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            string originLabel = origin.LabelCap;
            string caravanLabel = caravan.LabelCap;

            List<Pawn> pawns = new List<Pawn>();
            CollectCaravanPawns(caravan, pawns);
            if (pawns.Count == 0)
            {
                string emptyText = "TSA_WD_AT_Turret_NpcCaravanEmptyDestroyed_Text".Translate(originLabel, caravanLabel);
                manager?.AddLog(new SpreadLogEntry(emptyText, origin, caravan));
                AtTurretNotifyUtility.NotifyNpcHitPlayerCaravan(manager, origin, caravan, wiped: true, emptyText);
                if (!caravan.Destroyed)
                    caravan.Destroy();
                return;
            }

            int slots = origin.tier == AtTurretTier.Heavy ? 2 : 1;
            AntiAirStylePawnHitResult hit = ApplyAtTurretTieredHitToPawns(pawns, slots);

            bool wiped = !CaravanHasLivingPawn(caravan);
            if (wiped)
            {
                string namesBlock = FormatKilledPawnNamesBlock(hit.KilledNames);
                string wipeText = hit.KilledNames.Count == 0
                    ? "TSA_WD_AT_Turret_NpcCaravanDestroyed_Text".Translate(originLabel, caravanLabel)
                    : "TSA_WD_AT_Turret_NpcCaravanWiped_Text".Translate(originLabel, caravanLabel, namesBlock);
                manager?.AddLog(new SpreadLogEntry(wipeText, origin, caravan));
                AtTurretNotifyUtility.NotifyNpcHitPlayerCaravan(manager, origin, caravan, wiped: true, wipeText, hit.KilledPawns);
                if (!caravan.Destroyed)
                    caravan.Destroy();
                return;
            }

            string woundText = hit.Killed > 0
                ? "TSA_WD_AT_Turret_NpcCaravanWounded_WithNames_Text".Translate(
                    originLabel, caravanLabel, hit.Killed, hit.Wounded, FormatKilledPawnNamesBlock(hit.KilledNames))
                : "TSA_WD_AT_Turret_NpcCaravanWounded_Text".Translate(originLabel, caravanLabel, hit.Killed, hit.Wounded);
            manager?.AddLog(new SpreadLogEntry(woundText, origin, caravan));
            AtTurretNotifyUtility.NotifyNpcHitPlayerCaravan(manager, origin, caravan, wiped: false, woundText, hit.KilledPawns);
        }

        /// <summary>Pick up to <paramref name="slotCount"/> distinct living pawns; each 40% kill / 60% wound.</summary>
        private static AntiAirStylePawnHitResult ApplyAtTurretTieredHitToPawns(List<Pawn> pawns, int slotCount)
        {
            var result = new AntiAirStylePawnHitResult
            {
                KilledNames = new List<string>(),
                KilledPawns = new List<Pawn>()
            };
            if (pawns == null || pawns.Count == 0 || slotCount <= 0)
            {
                result.EmptyCarrier = true;
                return result;
            }

            List<Pawn> living = new List<Pawn>();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p != null && !p.Dead)
                    living.Add(p);
            }
            if (living.Count == 0)
            {
                result.EmptyCarrier = true;
                return result;
            }

            int targets = Mathf.Min(slotCount, living.Count);
            // Fisherâ€“Yates partial shuffle for distinct picks.
            for (int i = 0; i < targets; i++)
            {
                int j = Rand.Range(i, living.Count);
                Pawn tmp = living[i];
                living[i] = living[j];
                living[j] = tmp;
            }

            for (int i = 0; i < targets; i++)
            {
                Pawn p = living[i];
                if (p == null || p.Dead) continue;
                // 40% kill, 60% wound
                if (Rand.Value < 0.40f)
                {
                    string name = p.LabelShortCap;
                    p.Kill(null);
                    if (p.Dead)
                    {
                        result.Killed++;
                        result.KilledNames.Add(name);
                        result.KilledPawns.Add(p);
                    }
                }
                else
                {
                    ApplyAntiAirBruiseOrCut(p);
                    result.Wounded++;
                }
            }

            return result;
        }

        private static void CollectCaravanPawns(Caravan caravan, List<Pawn> into)
        {
            if (caravan?.PawnsListForReading == null || into == null) return;
            List<Pawn> list = caravan.PawnsListForReading;
            for (int i = 0; i < list.Count; i++)
            {
                Pawn p = list[i];
                if (p != null && !p.Dead)
                    into.Add(p);
            }
        }

        private static bool CaravanHasLivingPawn(Caravan caravan)
        {
            if (caravan?.PawnsListForReading == null) return false;
            List<Pawn> list = caravan.PawnsListForReading;
            for (int i = 0; i < list.Count; i++)
            {
                Pawn p = list[i];
                if (p != null && !p.Dead)
                    return true;
            }
            return false;
        }

        private static void ApplyMortarHitToWdTraveler(WorldObject_Traveler shell, WorldComponent_SpreadManager manager, WorldObject_Traveler target, float shellPotency)
        {
            if (target == null || target.Destroyed) return;
            float before = target.travelerStrength;
            target.travelerStrength = Mathf.Max(0f, before - shellPotency);
            float after = target.travelerStrength;
            bool wiped = after <= 0.01f;
            PostMortarStrengthHitLetter(manager, shell.originObject, target, before, after, wiped, "TSA_WD_Mortar_Hit_DestroyedMobileSuffix");
            if (wiped)
            {
                StampTraderInterceptedIfApplicable(target);
                target.Destroy();
                return;
            }

            // Surviving AT shell hit: divert onto the firing turret (save/restore detour).
            AtTurretRetaliationUtility.TryBeginDetourAfterAtShellHit(target, shell.originObject);
        }

        /// <summary>Launches a mortar-strike shell traveler. <paramref name="target"/> is the strength-hit object;
        /// the flight aim tile is <paramref name="aimTileIdOverride"/> when &gt;= 0, otherwise resolved (caravans: intercept along route).</summary>
        public static WorldObject_Traveler SpawnMortarTraveler(
            WorldObject origin,
            WorldObject target,
            float damage,
            bool guaranteedHit,
            int aimTileIdOverride = -1)
        {
            if (origin == null || target == null) return null;
            string defName = origin is WorldObject_AT_Turret
                ? "TSA_WD_Traveler_AT_Shell"
                : "TSA_WD_Traveler_MortarStrike";
            var def = DefDatabase<WorldObjectDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                Log.Error($"[TSA World Domination] Missing WorldObjectDef {defName}.");
                return null;
            }

            var traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = origin.Tile;
            traveler.SetFaction(origin.Faction);
            traveler.originObject = origin;
            traveler.targetObject = target;
            traveler.mission = TravelerMission.MortarStrike;
            traveler.mortarDamage = Mathf.Max(0f, damage);
            traveler.mortarHit = guaranteedHit;
            // ticksPerMove: lower = faster (progress per world tick is 1/ticksPerMove).
            traveler.ticksPerMove = GetMortarShellTicksPerMove();
            traveler.travelerStrength = 1f;
            traveler.initialStrength = 1f;
            Find.WorldObjects.Add(traveler);
            if (origin is WorldObject_AT_Turret atGun)
                WdWorldMapSound.PlayAtTurretFire(atGun.tier);
            else
                WdWorldMapSound.PlayMortarFire();
            float maxR = origin is WorldObject_WD_Outpost wd && wd.IsMortarOutpost
                ? MortarFireUtils.GetPlayerMortarMaxRangeTiles(wd)
                : (WorldDominationMod.settings?.npcMortarRange ?? WorldDominationSettings.DefNpcMortarRange);
            int aimTile = aimTileIdOverride >= 0 ? aimTileIdOverride : MortarCaravanIntercept.ResolveMortarAimTileId(origin, target, maxR);
            // Harden against an invalid aim tile so we never start a bogus path that could leak a stationary shell.
            if (aimTile < 0)
                aimTile = target.Tile;
            if (aimTile < 0)
            {
                // No valid tile to aim at: resolve the strike immediately (applies hit/miss to the target) and clean up.
                ExecuteMortarStrike(traveler);
                if (!traveler.Destroyed)
                    traveler.Destroy();
                return traveler;
            }
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(aimTile, origin));
            // AT shells spawn near the tile rim toward the aim so they do not pop from under the gun icon.
            if (origin is WorldObject_AT_Turret)
                traveler.ApplyBallisticSpawnOffsetTowardAim(0.8f);
            // SpawnSetup already registered the traveler; wake again after path so dest-based AA range works.
            // AT Turret shells are not AA targets (mortars and drop pods only).
            if (!traveler.IsAtTurretShell() && AntiAirFireUtils.IsAirborneAaTargetMission(traveler.mission))
                AntiAirFireUtils.WakeAllForMortarShell(traveler);
            return traveler;
        }

        /// <summary>Launches an AA flak shell: re-leads the drop pod at fire time, then applies light aim jitter.</summary>
        public static WorldObject_Traveler SpawnFlakTraveler(
            WorldObject origin,
            WorldObject target,
            float damage,
            bool guaranteedHit,
            bool isResolver,
            AntiAirFireUtils.AntiAirTargetKind kind = AntiAirFireUtils.AntiAirTargetKind.RaidDropPod)
        {
            if (origin == null || target == null || target.Destroyed) return null;
            var def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_FlakShell");
            if (def == null)
            {
                Log.Error("[TSA World Domination] Missing WorldObjectDef TSA_WD_Traveler_FlakShell.");
                return null;
            }

            float maxRange = AntiAirFireUtils.GetAntiAirMaxRangeForOrigin(origin);
            Vector3 meetPos;
            float flightTicks;
            if (target is WorldObject_Traveler ballistic
                && (kind == AntiAirFireUtils.AntiAirTargetKind.RaidDropPod || kind == AntiAirFireUtils.AntiAirTargetKind.MortarStrike))
            {
                if (!AntiAirIntercept.TryResolveLeadFlight(origin, ballistic, maxRange, GetFlakShellTicksPerMove(),
                        out meetPos, out flightTicks, out _))
                    return null;
            }
            else if (!AntiAirIntercept.TryResolveLeadFlightForWorldObject(origin, target, maxRange, GetFlakShellTicksPerMove(),
                         out meetPos, out flightTicks, out _))
            {
                return null;
            }

            // Cosmetics scatter; the resolver flies true lead so the kill lines up with the target.
            // Recalc flight after jitter so shells keep fixed speed (same as mortar) instead of syncing arrival.
            Vector3 fromPos = Find.WorldGrid.GetTileCenter(origin.Tile);
            if (!isResolver)
            {
                meetPos = AntiAirFireUtils.JitterMeetRandom(meetPos);
                flightTicks = AntiAirIntercept.FlightTicksAtFixedSpeed(fromPos, meetPos, GetFlakShellTicksPerMove());
            }

            var traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = origin.Tile;
            traveler.SetFaction(origin.Faction);
            traveler.originObject = origin;
            traveler.targetObject = target;
            traveler.mission = TravelerMission.AntiAirStrike;
            traveler.mortarDamage = Mathf.Max(0f, damage);
            traveler.mortarHit = guaranteedHit;
            traveler.antiAirIsResolver = isResolver;
            traveler.antiAirTargetKind = (byte)kind;
            traveler.ticksPerMove = GetFlakShellTicksPerMove();
            traveler.travelerStrength = 1f;
            traveler.initialStrength = 1f;
            traveler.BeginAntiAirLeadFlight(fromPos, meetPos, flightTicks);
            Find.WorldObjects.Add(traveler);
            WdWorldMapSound.PlayFlakFire();
            return traveler;
        }

        /// <summary>Shell arrives: resolve damage, despawn shell, then wipe the target only after the shell is gone.</summary>
        public static void FinishAntiAirShell(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return;
            var kind = (AntiAirFireUtils.AntiAirTargetKind)traveler.antiAirTargetKind;
            WorldObject wipeTarget = ExecuteAntiAirStrike(traveler);
            Vector3 shellPos = traveler.DrawPos;
            Vector3 targetPos = wipeTarget != null && !wipeTarget.Destroyed ? wipeTarget.DrawPos : shellPos;
            MortarWorldFx.NotifyFlakSmokeAt(shellPos);
            if (!traveler.Destroyed)
                traveler.Destroy();
            if (wipeTarget != null && !wipeTarget.Destroyed)
            {
                Color factionColor = wipeTarget.Faction?.Color ?? Color.white;
                if (kind == AntiAirFireUtils.AntiAirTargetKind.RaidDropPod
                    || kind == AntiAirFireUtils.AntiAirTargetKind.VanillaTransportPods)
                    MortarWorldFx.NotifyDropPodExplosionAt(targetPos, factionColor);
                else if (kind == AntiAirFireUtils.AntiAirTargetKind.MortarStrike)
                    MortarWorldFx.NotifyArtilleryShellDestroyedAt(targetPos, factionColor);
                else if (wipeTarget is WorldObject_Traveler ground
                         && (ground.mission == TravelerMission.Raid
                             || ground.mission == TravelerMission.RapidResponseIntercept))
                {
                    // Destroyed-caravan overlay fires from WorldObject_Traveler.Destroy.
                }
                else
                    MortarWorldFx.NotifyExplosionAt(targetPos);
                wipeTarget.Destroy();
            }
        }

        /// <summary>Resolver applies hit/damage. Returns the world object to destroy after the shell despawns (null if not wiped).</summary>
        public static WorldObject ExecuteAntiAirStrike(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return null;
            if (!traveler.antiAirIsResolver)
                return null;

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            WorldObject impactTarget = traveler.targetObject;
            string originLabel = traveler.originObject?.LabelCap ?? traveler.Faction?.Name ?? "?";
            string targetLabel = DescribeAntiAirTarget(impactTarget);
            var look = impactTarget != null && !impactTarget.Destroyed ? new LookTargets(impactTarget) : new LookTargets(traveler);
            bool notify = ShouldNotifyAntiAir(traveler.originObject, impactTarget);
            LetterDef letterDef = GetAntiAirLetterDef(traveler.originObject, impactTarget);

            var kind = (AntiAirFireUtils.AntiAirTargetKind)traveler.antiAirTargetKind;

            if (!traveler.mortarHit)
            {
                string missText;
                string missLabel;
                LetterDef missLetter = letterDef;
                if (kind == AntiAirFireUtils.AntiAirTargetKind.MortarStrike
                    && IsPlayerOwnedAirborne(impactTarget))
                {
                    string shellOrigin = DescribeAntiAirTarget(impactTarget);
                    string aaFaction = traveler.originObject?.Faction?.Name
                        ?? traveler.Faction?.Name
                        ?? "?";
                    missText = "TSA_WD_AntiAir_PlayerMortarShell_Miss_Text".Translate(shellOrigin, originLabel, aaFaction);
                    missLabel = "TSA_WD_AntiAir_PlayerMortarShell_Miss_Label".Translate();
                    missLetter = LetterDefOf.NeutralEvent;
                }
                else if (kind == AntiAirFireUtils.AntiAirTargetKind.MortarStrike)
                {
                    missText = "TSA_WD_AntiAir_MortarShell_Miss_Text".Translate(originLabel, targetLabel);
                    missLabel = "TSA_WD_AntiAir_MortarShell_Miss_Label".Translate();
                }
                else
                {
                    missText = "TSA_WD_AntiAir_Miss_Text".Translate(originLabel, targetLabel);
                    missLabel = "TSA_WD_AntiAir_Miss_Label".Translate();
                }
                manager?.AddLog(new SpreadLogEntry(missText, traveler.originObject, impactTarget));
                if (notify)
                {
                    Find.LetterStack.ReceiveLetter(
                        missLabel,
                        missText,
                        missLetter,
                        look);
                }
                return null;
            }

            if (impactTarget == null || impactTarget.Destroyed) return null;

            if (kind == AntiAirFireUtils.AntiAirTargetKind.MortarStrike)
            {
                string hitText;
                string hitLabel;
                LetterDef hitLetter = letterDef;
                LookTargets hitLook = traveler.originObject != null ? new LookTargets(traveler.originObject) : null;
                if (IsPlayerOwnedAirborne(impactTarget))
                {
                    string shellOrigin = DescribeAntiAirTarget(impactTarget);
                    string aaFaction = traveler.originObject?.Faction?.Name
                        ?? traveler.Faction?.Name
                        ?? "?";
                    hitText = "TSA_WD_AntiAir_PlayerMortarShell_Destroyed_Text".Translate(shellOrigin, originLabel, aaFaction);
                    hitLabel = "TSA_WD_AntiAir_PlayerMortarShell_Destroyed_Label".Translate();
                    hitLetter = LetterDefOf.NeutralEvent;
                }
                else
                {
                    GetMortarShellLauncherLabels(impactTarget, out string settlementName, out string factionName);
                    if (IsPlayerFactionObject(traveler.originObject))
                    {
                        hitText = "TSA_WD_AntiAir_MortarShell_Destroyed_Text".Translate(originLabel, settlementName, factionName);
                    }
                    else
                    {
                        hitText = "TSA_WD_AntiAir_MortarShell_Destroyed_Npc_Text".Translate(originLabel, settlementName, factionName);
                    }
                    hitLabel = "TSA_WD_AntiAir_MortarShell_Destroyed_Label".Translate();
                }
                manager?.AddLog(new SpreadLogEntry(hitText, traveler.originObject, impactTarget));
                if (notify)
                {
                    Find.LetterStack.ReceiveLetter(
                        hitLabel,
                        hitText,
                        hitLetter,
                        hitLook);
                }
                AntiAirFireUtils.NotifyTargetDestroyed(impactTarget);
                return impactTarget;
            }

            if (kind == AntiAirFireUtils.AntiAirTargetKind.VanillaTransportPods
                && impactTarget is TravellingTransporters pods)
            {
                bool wipe = ApplyVanillaTransportPodAaHit(pods, originLabel, notify, traveler.originObject, letterDef);
                if (wipe)
                {
                    AntiAirFireUtils.NotifyTargetDestroyed(pods);
                    return pods;
                }
                MortarWorldFx.NotifyFlakHitAt(pods.DrawPos);
                return null;
            }

            if (kind == AntiAirFireUtils.AntiAirTargetKind.VehicleFrameworkAerial
                && VehicleFrameworkAerialAaCompat.IsAerialVehicleInFlight(impactTarget))
            {
                return ApplyVehicleFrameworkAerialAaHit(
                    impactTarget, originLabel, notify, traveler.originObject, letterDef);
            }

            if (!(impactTarget is WorldObject_Traveler hitPod) || hitPod.Destroyed) return null;

            float shellPotency = Mathf.Max(0f, traveler.mortarDamage);
            float before = hitPod.travelerStrength;
            hitPod.travelerStrength = Mathf.Max(0f, before - shellPotency);
            float after = hitPod.travelerStrength;
            bool wiped = after <= 0.01f;

            string hitTextPod = wiped
                ? "TSA_WD_AntiAir_Hit_Destroyed_Text".Translate(originLabel, targetLabel, before.ToString("F0"))
                : "TSA_WD_AntiAir_Hit_Text".Translate(originLabel, targetLabel, before.ToString("F0"), after.ToString("F0"), shellPotency.ToString("F0"));
            manager?.AddLog(new SpreadLogEntry(hitTextPod, traveler.originObject, hitPod));

            bool cargoPod = OutpostDispatchMode.IsPlayerCargoDropPod(hitPod);
            bool forceCargoWipeLetter = wiped && cargoPod && IsPlayerOwnedAirborne(hitPod) && !IsPlayerFactionObject(traveler.originObject);
            if (notify || forceCargoWipeLetter)
            {
                string letterLabel = wiped
                    ? "TSA_WD_AntiAir_Hit_Destroyed_Label".Translate()
                    : "TSA_WD_AntiAir_Hit_Label".Translate();
                string letterText = hitTextPod;
                LetterDef sendDef = wiped
                    ? (IsPlayerFactionObject(traveler.originObject) ? LetterDefOf.PositiveEvent : letterDef)
                    : letterDef;

                if (forceCargoWipeLetter)
                {
                    if (hitPod is WorldObject_Traveler_Outpost_Upgrade)
                    {
                        letterLabel = "TSA_WD_AntiAir_UpgradeDropPod_Destroyed_Label".Translate();
                        letterText = "TSA_WD_AntiAir_UpgradeDropPod_Destroyed_Text".Translate(originLabel, targetLabel);
                    }
                    else
                    {
                        letterLabel = "TSA_WD_AntiAir_GoodsDropPod_Destroyed_Label".Translate();
                        letterText = "TSA_WD_AntiAir_GoodsDropPod_Destroyed_Text".Translate(originLabel, targetLabel);
                    }
                    sendDef = LetterDefOf.NegativeEvent;
                }

                Find.LetterStack.ReceiveLetter(
                    letterLabel,
                    letterText,
                    sendDef,
                    wiped ? (traveler.originObject != null ? new LookTargets(traveler.originObject) : null) : look);
            }

            if (wiped)
            {
                KillRapidResponsePawnsAndNotify(hitPod, traveler.originObject, originLabel);
                AntiAirFireUtils.NotifyTargetDestroyed(hitPod);
                return hitPod;
            }

            MortarWorldFx.NotifyFlakHitAt(hitPod.DrawPos);
            return null;
        }

        /// <summary>
        /// Enemy T4 AA destroying a Rapid Response pod kills its real passengers.
        /// Taking the pawn list first prevents the traveler's normal Destroy cleanup from returning them to the origin outpost.
        /// The death letter is deliberately unconditional and independent of AA notification settings.
        /// </summary>
        private static void KillRapidResponsePawnsAndNotify(
            WorldObject_Traveler hitPod,
            WorldObject aaOrigin,
            string originLabel)
        {
            if (!(hitPod is WorldObject_Traveler_RapidResponseDropPod rapidPod)) return;
            if (!ShouldSendAntiAirPassengerDeathLetter(aaOrigin)) return;

            List<Pawn> passengers = rapidPod.TakeCarriedPawns();
            if (passengers == null || passengers.Count == 0) return;

            List<string> killedNames = new List<string>();
            for (int i = 0; i < passengers.Count; i++)
            {
                Pawn pawn = passengers[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                string pawnName = pawn.LabelShortCap;
                pawn.Kill(null);
                if (pawn.Dead)
                    killedNames.Add(pawnName);
            }

            TryNotifyAntiAirPassengerDeaths(aaOrigin, originLabel, killedNames);
        }

        /// <summary>
        /// Same gate as Rapid Response passenger deaths: enemy T4 AA only (never player AA).
        /// Independent of <see cref="WorldDominationSettings.notifyT4AntiAirHitPlayer"/>.
        /// </summary>
        private static bool ShouldSendAntiAirPassengerDeathLetter(WorldObject aaOrigin)
        {
            CompViralSpread aaComp = aaOrigin?.GetComponent<CompViralSpread>();
            return aaComp != null
                && aaComp.tier == SettlementTier.T4
                && !IsPlayerFactionObject(aaOrigin);
        }

        /// <summary>
        /// Unconditional who-died letter for player drop-pod / transport-pod passengers killed by enemy T4 AA.
        /// Shared by Rapid Response travelers and vanilla <see cref="TravellingTransporters"/>.
        /// </summary>
        private static bool TryNotifyAntiAirPassengerDeaths(
            WorldObject aaOrigin,
            string originLabel,
            List<string> killedNames)
        {
            if (killedNames == null || killedNames.Count == 0) return false;
            if (!ShouldSendAntiAirPassengerDeathLetter(aaOrigin)) return false;

            string names = FormatKilledPawnNamesBlock(killedNames);
            Find.LetterStack.ReceiveLetter(
                "TSA_WD_AntiAir_RapidResponsePawnsKilled_Label".Translate(),
                "TSA_WD_AntiAir_RapidResponsePawnsKilled_Text".Translate(originLabel, names),
                LetterDefOf.ThreatBig,
                aaOrigin != null ? new LookTargets(aaOrigin) : null);
            return true;
        }

        /// <summary>
        /// AA letter buckets (mirrors mortar):
        /// 1a) Your AA vs hostile mortar shells â†’ <see cref="WorldDominationSettings.notifyPlayerAntiAirVsHostileMortarShell"/> (off by default)
        /// 1b) Your AA vs other airborne â†’ <see cref="WorldDominationSettings.notifyAntiAirHit"/>
        /// 2) Enemy AA vs your mortar shells â†’ <see cref="WorldDominationSettings.notifyPlayerMortarShellShotDown"/> (off by default)
        /// 3) Enemy AA vs your other airborne â†’ <see cref="WorldDominationSettings.notifyT4AntiAirHitPlayer"/>
        /// 4) Enemy AA vs other NPC airborne â†’ <see cref="WorldDominationSettings.notifyNpcMortarHitNpc"/> (off by default)
        /// </summary>
        private static bool ShouldNotifyAntiAir(WorldObject aaOrigin, WorldObject airborneTarget)
        {
            var seth = WorldDominationMod.settings;

            // 1. Your AA firing.
            if (IsPlayerFactionObject(aaOrigin))
            {
                if (IsHostileMortarShell(airborneTarget))
                    return seth != null && seth.notifyPlayerAntiAirVsHostileMortarShell;
                return seth == null || seth.notifyAntiAirHit;
            }

            // 2/3. Enemy AA engaging your shells / drop pods (faction or launcher origin).
            if (IsPlayerOwnedAirborne(airborneTarget))
            {
                if (IsPlayerMortarShell(airborneTarget))
                    return seth != null && seth.notifyPlayerMortarShellShotDown;
                // Cargo upgrade/goods drop pods always letter when destroyed (see ExecuteAntiAirStrike force path).
                if (OutpostDispatchMode.IsPlayerCargoDropPod(airborneTarget))
                    return true;
                return seth == null || seth.notifyT4AntiAirHitPlayer;
            }

            // 4. Enemy AA vs other NPC airborne â€” never use the player-AA flag here.
            return seth != null && seth.notifyNpcMortarHitNpc;
        }

        private static LetterDef GetAntiAirLetterDef(WorldObject aaOrigin, WorldObject airborneTarget)
        {
            if (IsPlayerFactionObject(aaOrigin))
                return LetterDefOf.PositiveEvent;
            if (IsPlayerMortarShell(airborneTarget))
                return LetterDefOf.NeutralEvent;
            if (IsPlayerOwnedAirborne(airborneTarget))
                return LetterDefOf.NegativeEvent;
            return LetterDefOf.NeutralEvent;
        }

        private static bool IsPlayerMortarShell(WorldObject airborne)
        {
            return airborne is WorldObject_Traveler t
                && t.mission == TravelerMission.MortarStrike
                && !t.IsAtTurretShell()
                && IsPlayerOwnedAirborne(t);
        }

        private static bool IsHostileMortarShell(WorldObject airborne)
        {
            return airborne is WorldObject_Traveler t
                && t.mission == TravelerMission.MortarStrike
                && !t.IsAtTurretShell()
                && !IsPlayerOwnedAirborne(t);
        }

        /// <summary>Prefer launcher name for shells/pods so letters are not "â€¦from Mortar Shell".</summary>
        private static string DescribeAntiAirTarget(WorldObject airborne)
        {
            if (airborne == null) return "?";
            if (airborne is WorldObject_Traveler t)
            {
                if (TravelerEndpointUtility.IsLiveEndpoint(t.originObject))
                    return t.originObject.LabelCap;
                if (t.Faction != null)
                    return t.Faction.Name;
            }
            return airborne.LabelCap;
        }

        /// <summary>Settlement/outpost name and faction for the launcher of a mortar shell traveler.</summary>
        private static void GetMortarShellLauncherLabels(WorldObject airborne, out string settlementName, out string factionName)
        {
            settlementName = "?";
            factionName = "?";
            if (!(airborne is WorldObject_Traveler t)) return;

            if (TravelerEndpointUtility.IsLiveEndpoint(t.originObject))
                settlementName = t.originObject.LabelCap;
            else if (!t.LabelCap.NullOrEmpty())
                settlementName = t.LabelCap;

            factionName = t.originObject?.Faction?.Name
                ?? t.Faction?.Name
                ?? "?";
        }

        /// <summary>
        /// AA hit on a Vehicle Framework aerial: crash via VF (downed-shuttle incident). Returns null because
        /// <see cref="VehicleFrameworkAerialAaCompat.TryCrashFromAntiAir"/> owns world-object cleanup.
        /// </summary>
        private static WorldObject ApplyVehicleFrameworkAerialAaHit(
            WorldObject aerial,
            string originLabel,
            bool notify,
            WorldObject origin,
            LetterDef letterDef)
        {
            if (aerial == null || aerial.Destroyed) return null;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            LetterDef destroyLetter = letterDef ?? LetterDefOf.NegativeEvent;
            if (IsPlayerOwnedAirborne(aerial))
                destroyLetter = LetterDefOf.NegativeEvent;

            string text = "TSA_WD_AntiAir_VfAerial_Destroyed_Text".Translate(originLabel, aerial.LabelCap);
            manager?.AddLog(new SpreadLogEntry(text, origin, aerial));
            if (notify)
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_AntiAir_VfAerial_Destroyed_Label".Translate(),
                    text,
                    destroyLetter,
                    origin != null ? new LookTargets(origin) : new LookTargets(aerial));
            }

            AntiAirFireUtils.NotifyTargetDestroyed(aerial);
            VehicleFrameworkAerialAaCompat.TryCrashFromAntiAir(aerial, origin);
            return null;
        }

        /// <summary>After AA hit on vanilla pods: 50/50 full wipe vs per-pawn kill/bruise with â‰¥1 survivor.
        /// Empty cargo pods can be destroyed with no pawns aboard.
        /// Returns true if the traveling pods world object should be destroyed.</summary>
        private static bool ApplyVanillaTransportPodAaHit(
            TravellingTransporters pods,
            string originLabel,
            bool notify,
            WorldObject origin,
            LetterDef letterDef)
        {
            if (pods == null || pods.Destroyed) return true;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            LetterDef destroyLetter = letterDef ?? LetterDefOf.NegativeEvent;
            // Destroyed pods are always a red letter for the player when their cargo is lost;
            // when the player AA wiped enemy pods, keep the caller letter (usually PositiveEvent).
            if (IsPlayerOwnedAirborne(pods))
                destroyLetter = LetterDefOf.NegativeEvent;

            List<Pawn> pawns = new List<Pawn>();
            CollectTransportPodPawns(pods, pawns);
            bool playerPods = IsPlayerOwnedAirborne(pods);
            AntiAirStylePawnHitResult hit = ApplyAntiAirStyleHitToPawns(
                pawns,
                origin,
                originLabel,
                notifyPassengerDeaths: playerPods);

            if (hit.EmptyCarrier)
            {
                string emptyText = "TSA_WD_AntiAir_VanillaPods_Destroyed_Text".Translate(originLabel, pods.LabelCap);
                manager?.AddLog(new SpreadLogEntry(emptyText, origin, pods));
                if (notify)
                {
                    Find.LetterStack.ReceiveLetter(
                        "TSA_WD_AntiAir_VanillaPods_Destroyed_Label".Translate(),
                        emptyText,
                        destroyLetter,
                        origin != null ? new LookTargets(origin) : null);
                }
                return true;
            }

            if (hit.FullWipe)
            {
                string namesBlock = FormatKilledPawnNamesBlock(hit.KilledNames);
                string wipeText = hit.DeathLetterSent || hit.KilledNames.Count == 0
                    ? "TSA_WD_AntiAir_VanillaPods_Destroyed_Text".Translate(originLabel, pods.LabelCap)
                    : "TSA_WD_AntiAir_VanillaPods_Wiped_Text".Translate(originLabel, pods.LabelCap, namesBlock);
                manager?.AddLog(new SpreadLogEntry(wipeText, origin, pods));
                if (notify)
                {
                    LookTargets look = hit.KilledPawns.Count > 0
                        ? new LookTargets(hit.KilledPawns)
                        : (origin != null ? new LookTargets(origin) : null);
                    string wipeLabel = hit.DeathLetterSent || hit.KilledNames.Count == 0
                        ? "TSA_WD_AntiAir_VanillaPods_Destroyed_Label".Translate()
                        : "TSA_WD_AntiAir_VanillaPods_Wiped_Label".Translate();
                    Find.LetterStack.ReceiveLetter(wipeLabel, wipeText, destroyLetter, look);
                }
                return true;
            }

            string woundText = hit.Killed > 0 && !hit.DeathLetterSent
                ? "TSA_WD_AntiAir_VanillaPods_Wounded_WithNames_Text".Translate(
                    originLabel, pods.LabelCap, hit.Killed, hit.Wounded, FormatKilledPawnNamesBlock(hit.KilledNames))
                : "TSA_WD_AntiAir_VanillaPods_Wounded_Text".Translate(originLabel, pods.LabelCap, hit.Killed, hit.Wounded);
            manager?.AddLog(new SpreadLogEntry(woundText, origin, pods));
            if (notify)
            {
                LookTargets look = hit.KilledPawns.Count > 0
                    ? new LookTargets(hit.KilledPawns)
                    : new LookTargets(pods);
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_AntiAir_VanillaPods_Wounded_Label".Translate(),
                    woundText,
                    letterDef ?? LetterDefOf.NeutralEvent,
                    look);
            }
            return false;
        }

        private struct AntiAirStylePawnHitResult
        {
            public bool EmptyCarrier;
            public bool FullWipe;
            public int Killed;
            public int Wounded;
            public List<string> KilledNames;
            public List<Pawn> KilledPawns;
            public bool DeathLetterSent;
        }

        /// <summary>
        /// Shared AA/AT passenger resolution: empty â†’ destroy carrier; multi â†’ 50/50 full wipe vs per-pawn
        /// kill/bruise with â‰¥1 survivor; solo â†’ always bruise/cut.
        /// </summary>
        private static AntiAirStylePawnHitResult ApplyAntiAirStyleHitToPawns(
            List<Pawn> pawns,
            WorldObject origin,
            string originLabel,
            bool notifyPassengerDeaths)
        {
            var result = new AntiAirStylePawnHitResult
            {
                KilledNames = new List<string>(),
                KilledPawns = new List<Pawn>()
            };
            if (pawns == null || pawns.Count == 0)
            {
                result.EmptyCarrier = true;
                result.FullWipe = true;
                return result;
            }

            bool fullWipe = pawns.Count > 1 && Rand.Bool;
            if (pawns.Count == 1)
                fullWipe = false;

            if (fullWipe)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn p = pawns[i];
                    if (p == null || p.Dead) continue;
                    string name = p.LabelShortCap;
                    p.Kill(null);
                    if (p.Dead)
                    {
                        result.KilledNames.Add(name);
                        result.KilledPawns.Add(p);
                        result.Killed++;
                    }
                }
                result.FullWipe = true;
                if (notifyPassengerDeaths)
                    result.DeathLetterSent = TryNotifyAntiAirPassengerDeaths(origin, originLabel, result.KilledNames);
                return result;
            }

            int survivorIndex = Rand.Range(0, pawns.Count);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || p.Dead) continue;
                if (i == survivorIndex || !Rand.Bool)
                {
                    ApplyAntiAirBruiseOrCut(p);
                    result.Wounded++;
                }
                else
                {
                    string name = p.LabelShortCap;
                    p.Kill(null);
                    result.Killed++;
                    if (p.Dead)
                    {
                        result.KilledNames.Add(name);
                        result.KilledPawns.Add(p);
                    }
                }
            }

            if (notifyPassengerDeaths)
                result.DeathLetterSent = TryNotifyAntiAirPassengerDeaths(origin, originLabel, result.KilledNames);
            return result;
        }

        private static string FormatKilledPawnNamesBlock(List<string> killedNames)
        {
            if (killedNames == null || killedNames.Count == 0) return string.Empty;
            return "  - " + string.Join("\n  - ", killedNames);
        }

        /// <summary>
        /// Vanilla <see cref="TravellingTransporters.GetDirectlyHeldThings"/> always returns null;
        /// passengers live in nested transporter containers exposed via <see cref="TravellingTransporters.Pawns"/>.
        /// </summary>
        private static void CollectTransportPodPawns(TravellingTransporters pods, List<Pawn> into)
        {
            if (pods == null || into == null) return;
            foreach (Pawn p in pods.Pawns)
            {
                if (p != null && !into.Contains(p))
                    into.Add(p);
            }
        }

        private static void ApplyAntiAirBruiseOrCut(Pawn pawn)
            => WD_OutpostDefenseSkirmishUtility.ApplyBruiseOrCut(pawn);
    }
}
