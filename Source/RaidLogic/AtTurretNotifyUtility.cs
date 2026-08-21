using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// AT Turret letters and action-log lines, separate from mortar / T4 settlement artillery.
    /// </summary>
    public static class AtTurretNotifyUtility
    {
        public static bool IsPlayerFaction(WorldObject wo)
            => wo?.Faction != null && wo.Faction.IsPlayer;

        /// <summary>
        /// Shell impact (or clash kill) resolution: AT-specific log + gated letters.
        /// Does not use mortar notification toggles or mortar letter copy.
        /// </summary>
        public static void NotifyShellOrStrengthHit(
            WorldComponent_SpreadManager manager,
            WorldObject_AT_Turret origin,
            WorldObject target,
            float beforeStrength,
            float afterStrength,
            bool wiped)
        {
            if (origin == null) return;
            manager ??= Find.World?.GetComponent<WorldComponent_SpreadManager>();
            var seth = WorldDominationMod.settings;

            string gunLabel = origin.LabelCap;
            string targetLabel = FormatTargetLabel(target);
            bool playerGun = IsPlayerFaction(origin);
            bool playerTarget = IsPlayerFaction(target);
            // Wipe letters pin the death tile: Destroy often follows this call immediately.
            LookTargets look = wiped && target != null && target.Tile.Valid
                ? LookAtTileOr(target.Tile, origin)
                : LookFor(target, origin);

            if (playerGun)
            {
                TaggedString logText = wiped
                    ? "TSA_WD_AT_Turret_DestroyedTarget_Text".Translate(gunLabel, targetLabel)
                    : "TSA_WD_AT_Turret_DamagedTarget_Text".Translate(
                        gunLabel,
                        targetLabel,
                        beforeStrength.ToString("F0"),
                        afterStrength.ToString("F0"));
                manager?.AddLog(new SpreadLogEntry(logText.Resolve(), origin, target));

                if (wiped)
                {
                    if (seth == null || seth.notifyPlayerAtTurretKilledTarget)
                    {
                        Find.LetterStack.ReceiveLetter(
                            "TSA_WD_AT_Turret_DestroyedTarget_Label".Translate(),
                            logText,
                            DefDatabase<LetterDef>.GetNamedSilentFail("TSA_WD_NeutralSilent") ?? LetterDefOf.NeutralEvent,
                            look);
                    }
                }
                else if (seth != null && seth.notifyPlayerAtTurretDamagedTarget)
                {
                    Find.LetterStack.ReceiveLetter(
                        "TSA_WD_AT_Turret_DamagedTarget_Label".Translate(),
                        logText,
                        LetterDefOf.NeutralEvent,
                        look);
                }
                return;
            }

            if (playerTarget)
            {
                TaggedString logText = wiped
                    ? "TSA_WD_AT_Turret_NpcKilledYou_Text".Translate(gunLabel, targetLabel)
                    : "TSA_WD_AT_Turret_NpcDamagedYou_Text".Translate(
                        gunLabel,
                        targetLabel,
                        beforeStrength.ToString("F0"),
                        afterStrength.ToString("F0"));
                manager?.AddLog(new SpreadLogEntry(logText.Resolve(), origin, target));

                if (wiped)
                {
                    if (seth == null || seth.notifyNpcAtTurretKilledPlayer)
                    {
                        Find.LetterStack.ReceiveLetter(
                            "TSA_WD_AT_Turret_NpcKilledYou_Label".Translate(),
                            logText,
                            LetterDefOf.NegativeEvent,
                            look);
                    }
                }
                else if (seth != null && seth.notifyNpcAtTurretDamagedPlayer)
                {
                    Find.LetterStack.ReceiveLetter(
                        "TSA_WD_AT_Turret_NpcDamagedYou_Label".Translate(),
                        logText,
                        LetterDefOf.NegativeEvent,
                        look);
                }
                return;
            }

            // NPC vs NPC: action log only.
            TaggedString npcLog = wiped
                ? "TSA_WD_AT_Turret_DestroyedTarget_Text".Translate(gunLabel, targetLabel)
                : "TSA_WD_AT_Turret_DamagedTarget_Text".Translate(
                    gunLabel,
                    targetLabel,
                    beforeStrength.ToString("F0"),
                    afterStrength.ToString("F0"));
            manager?.AddLog(new SpreadLogEntry(npcLog.Resolve(), origin, target));
        }

        /// <summary>Player-owned AT Turret lost in combat (not ownership wipe).</summary>
        public static void NotifyPlayerTurretDestroyed(WorldObject_AT_Turret turret, WorldObject attacker = null)
        {
            if (turret == null || !IsPlayerFaction(turret)) return;
            if (turret.suppressDestroyedLetter) return;

            var seth = WorldDominationMod.settings;
            if (seth != null && !seth.notifyPlayerAtTurretDestroyed) return;

            string gunLabel = turret.LabelCap;
            TaggedString text = attacker != null && !attacker.Destroyed
                ? "TSA_WD_AT_Turret_PlayerDestroyedBy_Text".Translate(gunLabel, FormatTargetLabel(attacker))
                : "TSA_WD_AT_Turret_PlayerDestroyed_Text".Translate(gunLabel);

            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(
                new SpreadLogEntry(text.Resolve(), turret, attacker));

            Find.LetterStack.ReceiveLetter(
                "TSA_WD_AT_Turret_PlayerDestroyed_Label".Translate(),
                text,
                LetterDefOf.NegativeEvent,
                attacker != null && !attacker.Destroyed ? new LookTargets(attacker) : new LookTargets(turret.Tile));
        }

        /// <summary>AT shell miss: always write the WD action log (no mortar miss letter).</summary>
        public static void NotifyShellMiss(WorldObject_AT_Turret origin, WorldObject target)
        {
            if (origin == null) return;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null) return;

            string gunLabel = origin.LabelCap;
            string targetLabel = FormatTargetLabel(target);
            string text = "TSA_WD_AT_Turret_Miss_Text".Translate(gunLabel, targetLabel).Resolve();

            var entry = new SpreadLogEntry(text, origin, target);
            // SpreadLogEntry.FormatLabel uses "---" for Destroyed objects; keep readable actor/target columns.
            if (entry.labelA == "---" || string.IsNullOrEmpty(entry.labelA))
                entry.labelA = gunLabel;
            if (entry.labelB == "---" || string.IsNullOrEmpty(entry.labelB))
                entry.labelB = targetLabel;
            manager.AddLog(entry);
        }

        /// <summary>NPC AT shell hit a player caravan (pawn kill/wound resolution already applied).</summary>
        public static void NotifyNpcHitPlayerCaravan(
            WorldComponent_SpreadManager manager,
            WorldObject_AT_Turret origin,
            Caravan caravan,
            bool wiped,
            string bodyText,
            List<Pawn> lookPawns = null)
        {
            if (origin == null || caravan == null) return;
            manager ??= Find.World?.GetComponent<WorldComponent_SpreadManager>();
            var seth = WorldDominationMod.settings;

            // Capture before Destroy(); wiped letters must jump to the death tile, not the gun.
            PlanetTile siteTile = caravan.Tile;
            LookTargets look;
            if (wiped)
                look = LookAtTileOr(siteTile, origin);
            else if (lookPawns != null && lookPawns.Count > 0)
                look = new LookTargets(lookPawns);
            else
                look = LookFor(caravan, origin);

            // Deaths on a ground caravan use a dedicated ThreatBig letter (not AA drop-pod copy).
            if (lookPawns != null && lookPawns.Count > 0)
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_AT_Turret_NpcCaravanPawnsKilled_Label".Translate(),
                    bodyText,
                    LetterDefOf.ThreatBig,
                    wiped ? LookAtTileOr(siteTile, origin) : look);
                return;
            }

            if (wiped)
            {
                if (seth == null || seth.notifyNpcAtTurretKilledPlayer)
                {
                    Find.LetterStack.ReceiveLetter(
                        "TSA_WD_AT_Turret_NpcCaravanDestroyed_Label".Translate(),
                        bodyText,
                        LetterDefOf.NegativeEvent,
                        look);
                }
            }
            else if (seth == null || seth.notifyNpcAtTurretDamagedPlayer)
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_AT_Turret_NpcCaravanWounded_Label".Translate(),
                    bodyText,
                    LetterDefOf.NegativeEvent,
                    look);
            }
        }

        public static string FormatTargetLabel(WorldObject target)
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
            // LabelCap remains usable after Destroy for log lines.
            string label = target.LabelCap;
            return string.IsNullOrEmpty(label) ? "?" : label;
        }

        /// <summary>
        /// Prefer a live world object; if it is already destroyed, jump to its last tile so the letter
        /// still opens the kill site. Fall back to another live object / its tile only if needed.
        /// </summary>
        private static LookTargets LookFor(WorldObject primary, WorldObject fallback)
        {
            if (primary != null && !primary.Destroyed)
                return new LookTargets(primary);
            if (primary != null && primary.Tile.Valid)
                return new LookTargets(new GlobalTargetInfo(primary.Tile));
            if (fallback != null && !fallback.Destroyed)
                return new LookTargets(fallback);
            if (fallback != null && fallback.Tile.Valid)
                return new LookTargets(new GlobalTargetInfo(fallback.Tile));
            return null;
        }

        private static LookTargets LookAtTileOr(PlanetTile tile, WorldObject fallback)
        {
            if (tile.Valid)
                return new LookTargets(new GlobalTargetInfo(tile));
            return LookFor(fallback, null);
        }
    }
}
