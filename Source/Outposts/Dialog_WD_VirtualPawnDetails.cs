using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Details window for a virtual pawn (name, skills, strength). On click from the Pawns tab.</summary>
    public class Dialog_WD_VirtualPawnDetails : Window
    {
        private readonly WorldObject_WD_Outpost outpost;
        private readonly VirtualPawnSummary summary;
        private Vector2 scrollPosition;

        public override Vector2 InitialSize => new Vector2(380f, 420f);

        public Dialog_WD_VirtualPawnDetails(WorldObject_WD_Outpost outpost, VirtualPawnSummary summary)
        {
            this.outpost = outpost;
            this.summary = summary;
            doCloseButton = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            float curY = 0f;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, curY, inRect.width, 28f), summary?.name ?? "—");
            curY += 32f;
            Text.Font = GameFont.Small;

            Rect scrollRect = new Rect(0f, curY, inRect.width, inRect.height - curY - 40f);
            Rect viewRect = new Rect(0f, 0f, scrollRect.width - 20f, 320f);

            Widgets.BeginScrollView(scrollRect, ref scrollPosition, viewRect);

            curY = 0f;
            Widgets.ListSeparator(ref curY, viewRect.width, "TSA_WD_PawnDetails_Skills".Translate());
            curY += 4f;

            DrawSkillLine(ref curY, viewRect.width, SkillDefOf.Shooting, summary.shooting);
            DrawSkillLine(ref curY, viewRect.width, SkillDefOf.Melee, summary.melee);
            DrawSkillLine(ref curY, viewRect.width, SkillDefOf.Plants, summary.plants);
            DrawSkillLine(ref curY, viewRect.width, SkillDefOf.Animals, summary.animals);
            DrawSkillLine(ref curY, viewRect.width, SkillDefOf.Construction, summary.construction);
            DrawSkillLine(ref curY, viewRect.width, SkillDefOf.Social, summary.social);
            DrawSkillLine(ref curY, viewRect.width, SkillDefOf.Mining, summary.mining);
            DrawSkillLine(ref curY, viewRect.width, SkillDefOf.Crafting, summary.crafting);

            curY += 8f;
            Widgets.ListSeparator(ref curY, viewRect.width, "TSA_WD_Strength".Translate());
            Widgets.Label(new Rect(0f, curY, viewRect.width, 24f), summary.CombatStrength.ToString("F0"));
            curY += 28f;

            Widgets.EndScrollView();

            Pawn pawnForRemove = summary?.pawn != null && !summary.pawn.Destroyed && outpost.Occupants.Contains(summary.pawn)
                ? summary.pawn
                : outpost.Occupants?.FirstOrDefault(p => p.ThingID == summary?.pawn?.ThingID);

            Rect removeRect = new Rect(0f, inRect.height - 36f, 160f, 32f);
            if (pawnForRemove != null && !OutpostPawnIdeologyUtil.IsSlaveHumanlike(pawnForRemove))
            {
                if (Widgets.ButtonText(removeRect, "TSA_WD_PawnDetails_Remove".Translate()))
                {
                    if (pawnForRemove != null)
                        Outpost_RemovePawn.TryRemovePawn(outpost, pawnForRemove);
                    Close();
                }
            }
        }

        private static void DrawSkillLine(ref float curY, float width, SkillDef def, int level)
        {
            Widgets.Label(new Rect(0f, curY, width * 0.6f, 22f), def.LabelCap);
            Widgets.Label(new Rect(width * 0.6f, curY, width * 0.4f, 22f), level.ToString());
            curY += 24f;
        }
    }
}
