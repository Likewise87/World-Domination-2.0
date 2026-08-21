using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Patch_DisinformationGizmo
    {
        private static Texture2D cachedIconActive;
        private static Texture2D cachedIconCooldown;

        public static IEnumerable<Gizmo> GetGizmos(Caravan caravan, List<(Settlement Settlement, CompViralSpread Comp)> neighborSettlements)
        {
            if (caravan == null || neighborSettlements == null || neighborSettlements.Count == 0) yield break;

            var seth = WorldDominationMod.settings;

            Pawn bestDiplomat = null;
            int bestDiplomatLevel = -1;
            foreach (var p in caravan.PawnsListForReading)
            {
                if (!p.RaceProps.Humanlike || p.Downed || p.Dead || p.WorkTagIsDisabled(WorkTags.Social)) continue;
                int level = p.skills.GetSkill(SkillDefOf.Social).Level;
                if (level > bestDiplomatLevel)
                {
                    bestDiplomatLevel = level;
                    bestDiplomat = p;
                }
            }

            if (bestDiplomat == null) yield break;
            int skill = bestDiplomat.skills.GetSkill(SkillDefOf.Social).Level;

            var snapshot = new List<(Settlement Settlement, CompViralSpread Comp)>(neighborSettlements.Count);
            for (int i = 0; i < neighborSettlements.Count; i++)
                snapshot.Add(neighborSettlements[i]);

            bool anyAvailable = false;
            for (int ti = 0; ti < snapshot.Count; ti++)
            {
                if (!snapshot[ti].Comp.IsEspionageOnCooldown) { anyAvailable = true; break; }
            }

            CompViralSpread singleComp = snapshot.Count == 1 ? snapshot[0].Comp : null;
            Pawn diplomat = bestDiplomat;

            Command_Action disinformation = new Command_Action
            {
                defaultLabel = "TSA_WD_Dis_GizmoLabel".Translate(),
                icon = anyAvailable
                    ? (cachedIconActive ?? (cachedIconActive = ContentFinder<Texture2D>.Get("UI/Commands/Disinformation", false) ?? TexCommand.Attack))
                    : (cachedIconCooldown ?? (cachedIconCooldown = ContentFinder<Texture2D>.Get("UI/Commands/Disinformation_Cooldown", false) ?? TexCommand.ForbidOff)),
                action = () =>
                {
                    if (snapshot.Count == 1)
                    {
                        ExecuteDisinformation(singleComp, caravan, diplomat);
                    }
                    else
                    {
                        List<FloatMenuOption> options = new List<FloatMenuOption>();
                        for (int i = 0; i < snapshot.Count; i++)
                        {
                            var target = snapshot[i];
                            float displayChance = GetCurrentDisChance(target.Comp, diplomat, seth);
                            string label = "TSA_WD_Dis_MenuOption".Translate(target.Settlement.LabelCap, target.Comp.tier.ToString(), displayChance.ToString("P0"));

                            if (target.Comp.IsEspionageOnCooldown)
                            {
                                options.Add(new FloatMenuOption(label + " " + "TSA_WD_OnCooldown".Translate(), null));
                            }
                            else
                            {
                                CompViralSpread clickComp = target.Comp;
                                options.Add(new FloatMenuOption(label, () =>
                                {
                                    ExecuteDisinformation(clickComp, caravan, diplomat);
                                }));
                            }
                        }
                        Find.WindowStack.Add(new FloatMenu(options));
                    }
                }
            };

            if (!anyAvailable)
            {
                disinformation.Disable("TSA_WD_Dis_DisabledAlert".Translate());
            }

            var primeTarget = snapshot[0];
            for (int ti = 0; ti < snapshot.Count; ti++)
            {
                if (!snapshot[ti].Comp.IsEspionageOnCooldown) { primeTarget = snapshot[ti]; break; }
            }
            int tier = (int)primeTarget.Comp.tier;
            float healthMult = GetPawnHealthFactor(diplomat, seth);
            float totalChance = GetCurrentDisChance(primeTarget.Comp, diplomat, seth);
            float baseSuccessPct = seth.TotalDisWeight > 0 ? seth.weightDisSuccess / seth.TotalDisWeight : 0f;
            float skillWeightDelta = skill * seth.disSkillSuccessWeightBonus;
            float tierWeightDelta = tier * seth.disTierSuccessWeightPenalty;

            disinformation.defaultDesc = "TSA_WD_Dis_GizmoDesc".Translate(
                diplomat.LabelShort,
                skill,
                baseSuccessPct.ToString("P1"),
                skillWeightDelta.ToString("N0"),
                tierWeightDelta.ToString("N0"),
                healthMult.ToString("P0"),
                totalChance.ToString("P0")
            );

            yield return disinformation;
        }

        private static float GetPawnHealthFactor(Pawn pawn, WorldDominationSettings s)
        {
            float avgCap = (
                pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness) +
                pawn.health.capacities.GetLevel(PawnCapacityDefOf.Moving) +
                pawn.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation) +
                pawn.health.capacities.GetLevel(PawnCapacityDefOf.Talking) +
                pawn.health.capacities.GetLevel(PawnCapacityDefOf.Sight) +
                pawn.health.capacities.GetLevel(PawnCapacityDefOf.Hearing)
            ) / 6f;

            return Mathf.Lerp(1f, avgCap, s.disHealthImpactWeight);
        }

        private static float GetCurrentDisChance(CompViralSpread comp, Pawn pawn, WorldDominationSettings s)
        {
            int skill = pawn.skills.GetSkill(SkillDefOf.Social).Level;
            int tier = (int)comp.tier;
            float wSuccess = Mathf.Max(1f, s.weightDisSuccess + skill * s.disSkillSuccessWeightBonus - tier * s.disTierSuccessWeightPenalty);
            wSuccess *= GetPawnHealthFactor(pawn, s);
            float totalWeight = wSuccess + s.weightDisCleanFail + s.weightDisInjuredFail + s.weightDisFatalFail;
            float chance = totalWeight > 0 ? wSuccess / totalWeight : 0f;
            return Mathf.Clamp(chance, 0.01f, 0.99f);
        }

        private static void ExecuteDisinformation(CompViralSpread comp, Caravan caravan, Pawn pawn)
        {
            if (comp.IsEspionageOnCooldown) return;

            var s = WorldDominationMod.settings;
            comp.espionageCooldownUntilTick = Find.TickManager.TicksGame + Mathf.RoundToInt(s.disCooldownDays * 60000f);

            int socialSkill = pawn.skills.GetSkill(SkillDefOf.Social).Level;
            int tier = (int)comp.tier;

            float wSuccess = Mathf.Max(1f, s.weightDisSuccess + socialSkill * s.disSkillSuccessWeightBonus - tier * s.disTierSuccessWeightPenalty);
            wSuccess *= GetPawnHealthFactor(pawn, s);

            float totalWeight = wSuccess + s.weightDisCleanFail + s.weightDisInjuredFail + s.weightDisFatalFail;
            float roll = Rand.Range(0f, totalWeight);

            Settlement settlement = comp.parent as Settlement;
            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();

            if (roll < wSuccess)
            {
                HandleSuccess(comp, caravan, pawn, settlement, manager, s);
            }
            else
            {
                string outcome;
                if (roll < wSuccess + s.weightDisCleanFail) outcome = "CLEAN";
                else if (roll < wSuccess + s.weightDisCleanFail + s.weightDisInjuredFail) outcome = "INJURY";
                else outcome = "FATAL";

                string saveContext = "";

                if (outcome == "INJURY")
                {
                    if (Rand.Value < pawn.skills.GetSkill(SkillDefOf.Social).Level * s.disSocialCleanBonus)
                    {
                        outcome = "CLEAN";
                        saveContext = "SILVERTONGUE";
                    }
                }
                else if (outcome == "FATAL")
                {
                    int combatSkill = Mathf.Max(pawn.skills.GetSkill(SkillDefOf.Shooting).Level, pawn.skills.GetSkill(SkillDefOf.Melee).Level);
                    if (Rand.Value < combatSkill * s.disCombatSurvivalBonus)
                    {
                        outcome = "INJURY";
                        saveContext = "FIGHTBACK";
                    }
                }

                ApplyFailureOutcome(outcome, saveContext, pawn, tier, caravan, settlement, manager);
            }
        }

        private static void HandleSuccess(CompViralSpread comp, Caravan caravan, Pawn pawn, Settlement settlement, WorldComponent_SpreadManager manager, WorldDominationSettings s)
        {
            float damage = s.disBaseReduction + pawn.skills.GetSkill(SkillDefOf.Social).Level * s.disSkillReductionBonus;
            float oldStr = comp.strength;
            comp.strength -= damage;

            if (comp.strength <= 0)
            {
                int tile = settlement.Tile;
                string originalName = settlement.Name ?? settlement.LabelCap;
                Faction faction = settlement.Faction;
                Find.LetterStack.ReceiveLetter("TSA_WD_Dis_LetterCollapsedLabel".Translate(), "TSA_WD_Dis_LetterCollapsedText".Translate(pawn.LabelShort, originalName), LetterDefOf.PositiveEvent, new GlobalTargetInfo(tile));
                manager?.AddLog(new SpreadLogEntry("TSA_WD_Dis_LogDestroyed".Translate(oldStr.ToString("F0")), caravan, settlement));
                Find.WorldObjects.Remove(settlement);
                WorldObject_WdSettlementRuin.Spawn(tile, originalName, faction);
            }
            else
            {
                comp.CheckTierUpdate(true);
                float newStr = comp.strength;
                Find.LetterStack.ReceiveLetter("TSA_WD_Dis_LetterSuccessLabel".Translate(), "TSA_WD_Dis_LetterSuccessText".Translate(pawn.LabelShort, settlement.Label, damage.ToString("F0")), LetterDefOf.PositiveEvent, settlement);
                manager?.AddLog(new SpreadLogEntry("TSA_WD_Dis_LogSuccess".Translate(oldStr.ToString("F0"), newStr.ToString("F0")), caravan, settlement));
            }
            WorldActions_Utils.RefreshMap();
        }

        private static void ApplyFailureOutcome(string outcome, string saveContext, Pawn pawn, int tier, Caravan caravan, Settlement settlement, WorldComponent_SpreadManager manager)
        {
            string label = "TSA_WD_Dis_LetterFailedLabel".Translate();
            string text = "TSA_WD_Dis_LetterFailedTextBase".Translate(pawn.LabelShort, settlement.Label);
            LetterDef letterDef = LetterDefOf.NeutralEvent;
            string logSuffix = "TSA_WD_Dis_LogClean".Translate();

            if (outcome == "CLEAN")
            {
                text += saveContext == "SILVERTONGUE"
                    ? "TSA_WD_Dis_FailureSilvertongue".Translate()
                    : "TSA_WD_Dis_FailureClean".Translate();
            }
            else
            {
                if (settlement.Faction != null)
                    GoodwillChangeNotifier.NotifySpyOpFailure(settlement.Faction, settlement, "TSA_WD_Dis_GizmoLabel", -30);

                if (outcome == "INJURY")
                {
                    letterDef = LetterDefOf.ThreatSmall;
                    logSuffix = "TSA_WD_Dis_LogInjured".Translate();
                    ApplyInjuries(pawn, tier);

                    if (saveContext == "FIGHTBACK")
                        text += "TSA_WD_Dis_FailureFightback".Translate();
                    else
                        text += "TSA_WD_Dis_FailureSpotted".Translate();
                }
                else // FATAL
                {
                    letterDef = LetterDefOf.Death;
                    logSuffix = "TSA_WD_Dis_LogKilled".Translate();
                    pawn.Kill(null);
                    text += "TSA_WD_Dis_FailureExecuted".Translate();
                }
            }

            Find.LetterStack.ReceiveLetter(label, text, letterDef, settlement);
            manager?.AddLog(new SpreadLogEntry("TSA_WD_Dis_LogMain".Translate(logSuffix), caravan, settlement));
        }

        private static void ApplyInjuries(Pawn pawn, int tier)
        {
            int numHits = Rand.RangeInclusive(1, 4);
            float minDmg = 5f;
            float maxDmg = 13f;

            for (int i = 0; i < numHits; i++)
            {
                var partsExternal = new List<BodyPartRecord>();
                foreach (var part in pawn.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside))
                    partsExternal.Add(part);
                var hitPart = partsExternal.Count > 0 ? partsExternal.RandomElement() : null;
                if (hitPart == null) break;
                DamageDef dmgDef = Rand.Chance(0.5f) ? DamageDefOf.Blunt : DamageDefOf.Cut;
                float finalDmg = Rand.Range(minDmg, maxDmg);
                DamageInfo dinfo = new DamageInfo(dmgDef, finalDmg, 0f, -1f, null, hitPart);
                pawn.TakeDamage(dinfo);
            }
        }
    }
}