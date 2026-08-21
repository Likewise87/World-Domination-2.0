using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Patch_SabotageGizmo
    {
        private static Texture2D cachedIconActive;
        private static Texture2D cachedIconCooldown;

        public static IEnumerable<Gizmo> GetGizmos(Caravan caravan, List<(Settlement Settlement, CompViralSpread Comp)> neighborSettlements)
        {
            if (caravan == null || neighborSettlements == null || neighborSettlements.Count == 0) yield break;

            var seth = WorldDominationMod.settings;

            Pawn bestCrafter = null;
            int bestCrafterLevel = -1;
            foreach (var p in caravan.PawnsListForReading)
            {
                if (!p.RaceProps.Humanlike || p.Downed || p.Dead || p.WorkTagIsDisabled(WorkTags.ManualSkilled)) continue;
                int level = p.skills.GetSkill(SkillDefOf.Crafting).Level;
                if (level > bestCrafterLevel)
                {
                    bestCrafterLevel = level;
                    bestCrafter = p;
                }
            }

            if (bestCrafter == null) yield break;
            int skill = bestCrafter.skills.GetSkill(SkillDefOf.Crafting).Level;

            var snapshot = new List<(Settlement Settlement, CompViralSpread Comp)>(neighborSettlements.Count);
            for (int i = 0; i < neighborSettlements.Count; i++)
                snapshot.Add(neighborSettlements[i]);

            bool anyAvailable = false;
            for (int ti = 0; ti < snapshot.Count; ti++)
            {
                if (!snapshot[ti].Comp.IsEspionageOnCooldown) { anyAvailable = true; break; }
            }

            CompViralSpread singleComp = snapshot.Count == 1 ? snapshot[0].Comp : null;
            Pawn crafter = bestCrafter;

            Command_Action sabotage = new Command_Action
            {
                defaultLabel = "TSA_WD_Sab_GizmoLabel".Translate(),
                icon = anyAvailable
                    ? (cachedIconActive ?? (cachedIconActive = ContentFinder<Texture2D>.Get("UI/Commands/Sabotage", false) ?? TexCommand.Attack))
                    : (cachedIconCooldown ?? (cachedIconCooldown = ContentFinder<Texture2D>.Get("UI/Commands/Sabotage_Cooldown", false) ?? TexCommand.ForbidOff)),

                action = () =>
                {
                    if (snapshot.Count == 1)
                    {
                        ExecuteSabotage(singleComp, caravan, crafter);
                    }
                    else
                    {
                        List<FloatMenuOption> options = new List<FloatMenuOption>();
                        for (int i = 0; i < snapshot.Count; i++)
                        {
                            var target = snapshot[i];
                            float displayChance = GetCurrentSuccessChance(target.Comp, crafter, seth);
                            string label = "TSA_WD_Sab_MenuOption".Translate(target.Settlement.LabelCap, target.Comp.tier.ToString(), displayChance.ToString("P0"));

                            if (target.Comp.IsEspionageOnCooldown)
                            {
                                options.Add(new FloatMenuOption(label + " " + "TSA_WD_OnCooldown".Translate(), null));
                            }
                            else
                            {
                                CompViralSpread clickComp = target.Comp;
                                options.Add(new FloatMenuOption(label, () =>
                                {
                                    ExecuteSabotage(clickComp, caravan, crafter);
                                }));
                            }
                        }
                        Find.WindowStack.Add(new FloatMenu(options));
                    }
                }
            };

            if (!anyAvailable) sabotage.Disable("TSA_WD_Sab_DisabledAlert".Translate());

            float totalChance = GetCurrentSuccessChance(snapshot[0].Comp, crafter, seth);
            sabotage.defaultDesc = "TSA_WD_Sab_GizmoDesc".Translate(crafter.LabelShort, skill, totalChance.ToString("P0"));
            yield return sabotage;
        }

        private static float GetPawnHealthFactor(Pawn pawn, WorldDominationSettings s)
        {
            float avgCap = (pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness) + pawn.health.capacities.GetLevel(PawnCapacityDefOf.Moving) + pawn.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation) + pawn.health.capacities.GetLevel(PawnCapacityDefOf.Talking) + pawn.health.capacities.GetLevel(PawnCapacityDefOf.Sight) + pawn.health.capacities.GetLevel(PawnCapacityDefOf.Hearing)) / 6f;
            return Mathf.Lerp(1f, avgCap, s.sabotageHealthImpactWeight);
        }

        private static float GetCurrentSuccessChance(CompViralSpread comp, Pawn pawn, WorldDominationSettings s)
        {
            int skill = pawn.skills.GetSkill(SkillDefOf.Crafting).Level;
            int tier = (int)comp.tier;
            float wSuccess = Mathf.Max(1f, s.weightSabSuccess + skill * s.sabotageSkillSuccessWeightBonus - tier * s.sabotageTierSuccessWeightPenalty);
            wSuccess *= GetPawnHealthFactor(pawn, s);
            float totalWeight = wSuccess + s.weightSabCleanFail + s.weightSabInjuredFail + s.weightSabFatalFail;
            float chance = totalWeight > 0 ? wSuccess / totalWeight : 0f;
            return Mathf.Clamp(chance, 0.01f, 0.99f);
        }

        private static void ExecuteSabotage(CompViralSpread comp, Caravan caravan, Pawn pawn)
        {
            if (comp.IsEspionageOnCooldown) return;

            var s = WorldDominationMod.settings;
            comp.espionageCooldownUntilTick = Find.TickManager.TicksGame + Mathf.RoundToInt(s.sabotageCooldownDays * 60000f);

            float wSuccess = Mathf.Max(1f, s.weightSabSuccess + pawn.skills.GetSkill(SkillDefOf.Crafting).Level * s.sabotageSkillSuccessWeightBonus - (int)comp.tier * s.sabotageTierSuccessWeightPenalty);
            wSuccess *= GetPawnHealthFactor(pawn, s);

            float totalWeight = wSuccess + s.weightSabCleanFail + s.weightSabInjuredFail + s.weightSabFatalFail;
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
                if (roll < wSuccess + s.weightSabCleanFail) outcome = "CLEAN";
                else if (roll < wSuccess + s.weightSabCleanFail + s.weightSabInjuredFail) outcome = "INJURY";
                else outcome = "FATAL";

                string saveContext = "";

                if (outcome == "INJURY")
                {
                    if (Rand.Value < pawn.skills.GetSkill(SkillDefOf.Social).Level * s.sabotageSocialCleanBonus)
                    {
                        outcome = "CLEAN";
                        saveContext = "SILVERTONGUE";
                    }
                }
                else if (outcome == "FATAL")
                {
                    int combatSkill = Mathf.Max(pawn.skills.GetSkill(SkillDefOf.Shooting).Level, pawn.skills.GetSkill(SkillDefOf.Melee).Level);
                    if (Rand.Value < combatSkill * s.sabotageCombatSurvivalBonus)
                    {
                        outcome = "INJURY";
                        saveContext = "FIGHTBACK";
                    }
                }

                ApplyFailureOutcome(outcome, saveContext, pawn, (int)comp.tier, caravan, settlement, manager);
            }
        }

        private static void HandleSuccess(CompViralSpread comp, Caravan caravan, Pawn pawn, Settlement settlement, WorldComponent_SpreadManager manager, WorldDominationSettings s)
        {
            float damage = s.sabotageBaseReduction + pawn.skills.GetSkill(SkillDefOf.Crafting).Level * s.sabotageSkillReductionBonus;
            float oldStr = comp.strength;
            comp.strength -= damage;

            if (comp.strength <= 0)
            {
                int tile = settlement.Tile;
                string originalName = settlement.Name ?? settlement.LabelCap;
                Faction faction = settlement.Faction;
                Find.LetterStack.ReceiveLetter("TSA_WD_Sab_LetterObliteratedLabel".Translate(), "TSA_WD_Sab_LetterObliteratedText".Translate(pawn.LabelShort, originalName), LetterDefOf.PositiveEvent, new GlobalTargetInfo(tile));
                manager?.AddLog(new SpreadLogEntry("TSA_WD_Sab_LogDestroyed".Translate(oldStr.ToString("F0")), caravan, settlement));
                Find.WorldObjects.Remove(settlement);
                WorldObject_WdSettlementRuin.Spawn(tile, originalName, faction);
            }
            else
            {
                comp.CheckTierUpdate(true);
                float newStr = comp.strength;
                Find.LetterStack.ReceiveLetter("TSA_WD_Sab_LetterSuccessLabel".Translate(), "TSA_WD_Sab_LetterSuccessText".Translate(pawn.LabelShort, settlement.Label, damage.ToString("F0")), LetterDefOf.PositiveEvent, settlement);
                manager?.AddLog(new SpreadLogEntry("TSA_WD_Sab_LogSuccess".Translate(oldStr.ToString("F0"), newStr.ToString("F0")), caravan, settlement));
            }
            WorldActions_Utils.RefreshMap();
        }

        private static void ApplyFailureOutcome(string outcome, string saveContext, Pawn pawn, int tier, Caravan caravan, Settlement settlement, WorldComponent_SpreadManager manager)
        {
            string label = "TSA_WD_Sab_LetterFailedLabel".Translate();
            string text = "TSA_WD_Sab_LetterFailedTextBase".Translate(pawn.LabelShort, settlement.Label);
            LetterDef letterDef = LetterDefOf.NeutralEvent;
            string logSuffix = "TSA_WD_Sab_LogClean".Translate();

            if (outcome == "CLEAN")
            {
                text += saveContext == "SILVERTONGUE"
                    ? "TSA_WD_Sab_FailureSilvertongue".Translate()
                    : "TSA_WD_Sab_FailureClean".Translate();
            }
            else
            {
                if (settlement.Faction != null)
                    GoodwillChangeNotifier.NotifySpyOpFailure(settlement.Faction, settlement, "TSA_WD_Sab_GizmoLabel", -30);

                if (outcome == "INJURY")
                {
                    letterDef = LetterDefOf.ThreatSmall;
                    logSuffix = "TSA_WD_Sab_LogInjured".Translate();
                    ApplyInjuries(pawn, tier);

                    if (saveContext == "FIGHTBACK")
                        text += "TSA_WD_Sab_FailureFightback".Translate();
                    else
                        text += "TSA_WD_Sab_FailureSpotted".Translate();
                }
                else // FATAL
                {
                    letterDef = LetterDefOf.Death;
                    logSuffix = "TSA_WD_Sab_LogKilled".Translate();
                    pawn.Kill(null);
                    text += "TSA_WD_Sab_FailureExecuted".Translate();
                }
            }

            Find.LetterStack.ReceiveLetter(label, text, letterDef, settlement);
            manager?.AddLog(new SpreadLogEntry("TSA_WD_Sab_LogMain".Translate(logSuffix), caravan, settlement));
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