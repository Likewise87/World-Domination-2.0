using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Soft second chance after a failed simulated auto-resolve: injure some occupants, cut outpost strength,
    /// persist raid context, and reopen the defense choice dialog.
    /// </summary>
    public static class WD_OutpostDefenseSkirmishUtility
    {
        public const float OccupantInjuryFraction = 0.2f;
        public const float DefenderStrengthLossFraction = 0.15f;

        /// <summary>Light bruise/cut used by AA pod wounds (single hit, 8-18 severity). High armor pen so plated apparel does not eat the hit.</summary>
        public static void ApplyBruiseOrCut(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed) return;
            ApplyBruiseOrCut(pawn, Rand.Range(8f, 18f));
        }

        private static void ApplyBruiseOrCut(Pawn pawn, float amount)
        {
            DamageDef dmgDef = Rand.Bool ? DamageDefOf.Blunt : DamageDefOf.Cut;
            // armorPenetration 1 = full pen vs typical apparel so virtual/AA wounds remain visible.
            pawn.TakeDamage(new DamageInfo(dmgDef, Mathf.Max(1f, amount), armorPenetration: 1f));
        }

        /// <summary>Skirmish / auto-resolve captive wounds: 2-4 hits, each 8-18 severity with ±25% variance.</summary>
        public static void ApplySkirmishInjuries(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed) return;
            int hits = Rand.RangeInclusive(2, 4);
            for (int i = 0; i < hits; i++)
            {
                float amount = Rand.Range(8f, 18f) * Rand.Range(0.75f, 1.25f);
                ApplyBruiseOrCut(pawn, amount);
            }
        }

        public static void BeginSkirmishFollowUp(
            WorldObject_Traveler traveler,
            WorldObject_WD_Outpost outpost,
            WorldComponent_SpreadManager manager)
        {
            if (traveler == null || outpost == null || outpost.Destroyed) return;

            int pawnsHurt = ApplyOccupantInjuries(outpost);
            var strengthComp = outpost.GetComponent<CompViralSpread>();
            float strengthBefore = strengthComp?.GetTotalLocalDefensePower() ?? 0f;
            strengthComp?.ReduceStrength(DefenderStrengthLossFraction, allowDemotion: true);
            float strengthLost = Mathf.Max(0f, strengthBefore - (strengthComp?.GetTotalLocalDefensePower() ?? 0f));
            outpost.CapturePendingSkirmishFromTraveler(traveler);
            outpost.SetPendingSkirmishLossSummary(strengthLost, pawnsHurt);

            manager?.AddLog(new SpreadLogEntry(
                "TSA_WD_OutpostDefense_SkirmishLog".Translate(outpost.LabelCap, traveler.Faction?.Name ?? "Unknown"),
                traveler,
                outpost));

            Find.LetterStack.ReceiveLetter(
                "TSA_WD_OutpostDefense_SkirmishLetter_Label".Translate(),
                "TSA_WD_OutpostDefense_SkirmishLetter_Text".Translate(outpost.LabelCap, traveler.Faction?.Name ?? "Unknown"),
                LetterDefOf.ThreatBig,
                outpost);

            OpenSkirmishDialog(outpost, manager);
        }

        public static void OpenSkirmishDialog(WorldObject_WD_Outpost outpost, WorldComponent_SpreadManager manager)
        {
            if (outpost == null || outpost.Destroyed || !outpost.PendingSkirmishDefense) return;
            if (DialogAlreadyOpenFor(outpost)) return;

            WorldObject_Traveler traveler = outpost.RecreatePendingSkirmishTraveler();
            if (traveler == null)
            {
                Log.Warning($"[TSA WD] Pending skirmish on {outpost.LabelCap} could not rebuild raid context; clearing.");
                outpost.ClearPendingSkirmishDefense();
                return;
            }

            Find.WindowStack.Add(new Dialog_OutpostDefenseChoice(traveler, outpost, manager, isSkirmishFollowUp: true));
        }

        public static void TryReopenPendingSkirmishDialogs()
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            var worldObjects = Find.WorldObjects?.AllWorldObjects;
            if (worldObjects == null) return;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();

            for (int i = 0; i < worldObjects.Count; i++)
            {
                if (worldObjects[i] is WorldObject_WD_Outpost outpost
                    && !outpost.Destroyed
                    && outpost.PendingSkirmishDefense
                    && outpost.Faction == Faction.OfPlayer)
                {
                    OpenSkirmishDialog(outpost, manager);
                }
            }
        }

        public static bool DialogAlreadyOpenFor(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || Find.WindowStack == null) return false;
            var windows = Find.WindowStack.Windows;
            for (int i = 0; i < windows.Count; i++)
            {
                if (windows[i] is Dialog_OutpostDefenseChoice choice && choice.IsForOutpost(outpost))
                    return true;
            }
            return false;
        }

        private static int ApplyOccupantInjuries(WorldObject_WD_Outpost outpost)
        {
            List<Pawn> pool = new List<Pawn>();
            List<Pawn> occupants = outpost.Occupants;
            for (int i = 0; i < occupants.Count; i++)
            {
                Pawn p = occupants[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (p.RaceProps == null || !p.RaceProps.Humanlike) continue;
                pool.Add(p);
            }

            if (pool.Count == 0) return 0;

            int hurtCount = Mathf.Max(1, Mathf.CeilToInt(pool.Count * OccupantInjuryFraction));
            hurtCount = Mathf.Min(hurtCount, pool.Count);
            pool.Shuffle();
            for (int i = 0; i < hurtCount; i++)
                ApplySkirmishInjuries(pool[i]);
            outpost.SetOccupantsNeedHealing(true);
            return hurtCount;
        }
    }
}
