using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Patch_FortifyGizmo
    {
        private static Texture2D cachedIconActive;
        private static Texture2D cachedIconCooldown;

        public static IEnumerable<Gizmo> GetGizmos(Caravan caravan, List<(Settlement Settlement, CompViralSpread Comp)> neighborSettlements)
        {
            if (caravan == null || neighborSettlements == null || neighborSettlements.Count == 0) yield break;

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

            Faction player = Faction.OfPlayer;
            var snapshot = new List<(Settlement Settlement, CompViralSpread Comp)>();
            for (int i = 0; i < neighborSettlements.Count; i++)
            {
                var n = neighborSettlements[i];
                if (n.Settlement?.Faction == null) continue;
                if (WorldActions_Utils.SafeHostileTo(n.Settlement.Faction, player)) continue;
                snapshot.Add(n);
            }

            if (snapshot.Count == 0) yield break;

            bool anyAvailable = false;
            for (int ti = 0; ti < snapshot.Count; ti++)
            {
                if (!snapshot[ti].Comp.IsAidOnCooldown) { anyAvailable = true; break; }
            }

            CompViralSpread singleComp = snapshot.Count == 1 ? snapshot[0].Comp : null;
            Pawn crafter = bestCrafter;

            Command_Action fortify = new Command_Action
            {
                defaultLabel = "TSA_WD_Fort_GizmoLabel".Translate(),
                icon = anyAvailable
                    ? (cachedIconActive ?? (cachedIconActive = ContentFinder<Texture2D>.Get("UI/Commands/Fortify", false) ?? TexCommand.Attack))
                    : (cachedIconCooldown ?? (cachedIconCooldown = ContentFinder<Texture2D>.Get("UI/Commands/Fortify_Cooldown", false) ?? TexCommand.ForbidOff)),
                action = () =>
                {
                    if (snapshot.Count == 1)
                    {
                        ExecuteFortify(singleComp, caravan, crafter);
                    }
                    else
                    {
                        List<FloatMenuOption> options = new List<FloatMenuOption>();
                        for (int i = 0; i < snapshot.Count; i++)
                        {
                            var target = snapshot[i];
                            float chance = GetFortifySuccessChance(skill);
                            string label = "TSA_WD_Fort_MenuOption".Translate(target.Settlement.LabelCap, chance.ToString("P0"));

                            if (target.Comp.IsAidOnCooldown)
                            {
                                options.Add(new FloatMenuOption(label + " " + "TSA_WD_OnCooldown".Translate(), null));
                            }
                            else
                            {
                                CompViralSpread clickComp = target.Comp;
                                options.Add(new FloatMenuOption(label, () => ExecuteFortify(clickComp, caravan, crafter)));
                            }
                        }
                        Find.WindowStack.Add(new FloatMenu(options));
                    }
                }
            };

            if (!anyAvailable) fortify.Disable("TSA_WD_Fort_DisabledAlert".Translate());

            float totalChance = GetFortifySuccessChance(skill);
            fortify.defaultDesc = "TSA_WD_Fort_GizmoDesc".Translate(crafter.LabelShort, skill, totalChance.ToString("P0"));
            yield return fortify;
        }

        private static float GetFortifySuccessChance(int skill) => Mathf.Lerp(0.05f, 0.60f, skill / 20f);
        private static float GetFortifyStrengthGain(int skill) => Mathf.Lerp(60f, 250f, skill / 20f);

        private static void ExecuteFortify(CompViralSpread comp, Caravan caravan, Pawn pawn)
        {
            if (comp.IsAidOnCooldown) return;

            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();
            comp.aidCooldownUntilTick = Find.TickManager.TicksGame + 300000;

            int skill = pawn.skills.GetSkill(SkillDefOf.Crafting).Level;
            float roll = Rand.Value;
            float chance = GetFortifySuccessChance(skill);

            if (roll <= chance)
            {
                float gain = GetFortifyStrengthGain(skill);
                float oldStr = comp.strength;
                comp.strength += gain;
                comp.CheckTierUpdate(true);

                Find.LetterStack.ReceiveLetter("TSA_WD_Fort_LetterSuccessLabel".Translate(),
                    "TSA_WD_Fort_LetterSuccessText".Translate(pawn.LabelShort, comp.parent.Label, gain.ToString("F0")),
                    LetterDefOf.PositiveEvent, comp.parent);

                manager?.AddLog(new SpreadLogEntry("TSA_WD_Fort_LogSuccess".Translate(oldStr.ToString("F0"), comp.strength.ToString("F0")), caravan, comp.parent as Settlement));
            }
            else
            {
                Find.LetterStack.ReceiveLetter("TSA_WD_Fort_LetterFailedLabel".Translate(),
                    "TSA_WD_Fort_LetterFailedText".Translate(pawn.LabelShort, comp.parent.Label),
                    LetterDefOf.NeutralEvent, comp.parent);

                manager?.AddLog(new SpreadLogEntry("TSA_WD_Fort_LogFailed".Translate(), caravan, comp.parent as Settlement));
            }

            WorldActions_Utils.RefreshMap();
        }
    }
}