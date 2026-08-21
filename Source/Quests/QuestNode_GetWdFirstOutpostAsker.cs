using System.Collections.Generic;
using TSA_WorldDomination;
using Verse;

namespace RimWorld.QuestGen
{
    /// <summary>Picks a WD surface Ally (preferred) or Neutral asker into slate for the first-outpost intro quest.</summary>
    public class QuestNode_GetWdFirstOutpostAsker : QuestNode
    {
        [NoTranslate]
        public SlateRef<string> storeAs;

        protected override bool TestRunInt(Slate slate)
        {
            string key = storeAs.GetValue(slate) ?? "faction";
            if (slate.TryGet(key, out Faction existing) && existing != null)
                return true;
            if (!WdFirstOutpostQuestHelper.TryPickAsker(out Faction asker))
                return false;
            slate.Set(key, asker);
            return true;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            string key = storeAs.GetValue(slate) ?? "faction";
            Faction asker;
            if (!slate.TryGet(key, out asker) || asker == null)
            {
                if (!WdFirstOutpostQuestHelper.TryPickAsker(out asker))
                    return;
                slate.Set(key, asker);
            }

            // Same as QuestNode_GetFaction: register for InvolvedFactions / hostile signals / description tokens.
            if (!asker.Hidden)
            {
                var part = new QuestPart_InvolvedFactions();
                part.factions.Add(asker);
                QuestGen.quest.AddPart(part);
            }
        }
    }
}
