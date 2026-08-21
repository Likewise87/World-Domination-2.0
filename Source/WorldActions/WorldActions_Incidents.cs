using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace TSA_WorldDomination
{
    public static class WorldActions_Incidents
    {
        public static void AttemptMinorIncident(Settlement s, CompViralSpread comp, WorldComponent_SpreadManager manager)
        {
            HandleIncident(s, comp, manager, false);
        }

        public static void AttemptMajorIncident(Settlement s, CompViralSpread comp, WorldComponent_SpreadManager manager)
        {
            HandleIncident(s, comp, manager, true);
        }

        private static void HandleIncident(Settlement s, CompViralSpread comp, WorldComponent_SpreadManager manager, bool isMajor)
        {
            // --- SURGICAL: INCIDENT COOLDOWN CHECK ---
            // If the settlement recently had an incident, it is immune to new ones for the CD duration.
            if (comp.IsIncidentOnCooldown) return;

            var seth = WorldDominationMod.settings;
            float oldStr = comp.strength;
            SettlementTier oldTier = comp.tier;

            // 1. Apply Diplomacy Multipliers (leader: more severe loss; underdog: reduced loss)
            float mult = 1f;
            if (s.Faction == manager.currentWorldLeader && Find.TickManager.TicksGame < manager.leaderHandicapExpiryTick) mult = seth.leaderIncidentSeverityMult;
            if (s.Faction == manager.currentWeakestUnderdog && Find.TickManager.TicksGame < manager.underdogBuffExpiryTick) mult = seth.underdogIncidentSeverityMult;

            float loss = (isMajor ? seth.majorIncidentSeverity : seth.minorIncidentSeverity) * mult;
            WDVerbose.Msg($"Incident {(isMajor ? "major" : "minor")}: {s.LabelCap} loss={loss:F0}");
            comp.strength -= loss;

            // Incident channel only; duration from the Incident Cooldown setting.
            comp.incidentCooldownTick = Find.TickManager.TicksGame
                + CompViralSpread.CooldownTicksFromDays(seth.cooldownIncidentDays);

            string logKeyPrefix = isMajor ? "TSA_WD_Log_MajorInc" : "TSA_WD_Log_MinorInc";

            // 2. Check for Immediate Destruction
            if (comp.strength < 0)
            {
                int tile = s.Tile;
                string originalName = s.Name ?? s.LabelCap;
                Faction faction = s.Faction;

                var obliterated = new SpreadLogEntry($"{logKeyPrefix}_Obliterated".Translate(oldStr.ToString("F0"), comp.strength.ToString("F0")), s);
                obliterated.highlightKind = SpreadLogHighlightKind.IncidentSettlementDestroyed;
                manager.AddLog(obliterated);

                if (WorldDominationMod.settings.notifySettlementRaided
                    && WD_NotifyProximity.IsWithinPlayerNotificationRadius(tile))
                {
                    string letterLabel = (isMajor ? "TSA_WD_Letter_MajorInc_Label" : "TSA_WD_Letter_MinorInc_Label").Translate();
                    string letterText = (isMajor ? "TSA_WD_Letter_MajorInc_Text" : "TSA_WD_Letter_MinorInc_Text").Translate(originalName);
                    Find.LetterStack.ReceiveLetter(letterLabel, letterText, LetterDefOf.NeutralEvent, new GlobalTargetInfo(tile));
                }

                Find.WorldObjects.Remove(s);
                // Same timed blocking ruins as raid raze (incident always razes; no conquest replacement).
                WorldObject_WdSettlementRuin.Spawn(tile, originalName, faction);
                WorldActions_Utils.RefreshMap();
                return;
            }

            // 3. Tier update: demote at most one level per incident (destruction above if strength went negative).
            comp.CheckTierUpdateLimitedDemotion(1);

            if (comp.tier < oldTier)
            {
                manager.AddLog(new SpreadLogEntry($"{logKeyPrefix}_Downgrade".Translate(loss.ToString("F0"), oldStr.ToString("F0"), comp.strength.ToString("F0"), comp.tier.ToString()), s));
                WorldActions_Utils.RefreshMap();
            }
            else
            {
                manager.AddLog(new SpreadLogEntry($"{logKeyPrefix}_Standard".Translate(loss.ToString("F0"), oldStr.ToString("F0"), comp.strength.ToString("F0")), s));
            }
        }

    }
}