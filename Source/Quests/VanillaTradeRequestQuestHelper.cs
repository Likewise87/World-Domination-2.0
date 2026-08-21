using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Vanilla <c>TradeRequest</c> quests do not fail when their settlement is captured or deleted.
    /// WD (and other wipe paths) remove the world object; fail those quests then.
    /// </summary>
    public static class VanillaTradeRequestQuestHelper
    {
        public const string QuestDefName = "TradeRequest";

        private static readonly List<Quest> failScratch = new List<Quest>(2);

        public static void FailIfSettlementLost(WorldObject worldObject)
        {
            if (worldObject is Settlement settlement)
                FailIfSettlementLost(settlement);
        }

        public static void FailIfSettlementLost(Settlement settlement)
        {
            if (settlement == null) return;
            if (Current.ProgramState != ProgramState.Playing) return;
            QuestManager manager = Find.QuestManager;
            if (manager == null) return;

            failScratch.Clear();
            List<Quest> quests = manager.QuestsListForReading;
            for (int i = 0; i < quests.Count; i++)
            {
                Quest quest = quests[i];
                if (quest == null || quest.State != QuestState.Ongoing) continue;
                if (quest.root == null || quest.root.defName != QuestDefName) continue;
                if (!TargetsSettlement(quest, settlement)) continue;
                failScratch.Add(quest);
            }

            for (int i = 0; i < failScratch.Count; i++)
                failScratch[i].End(QuestEndOutcome.Fail);
            failScratch.Clear();
        }

        private static bool TargetsSettlement(Quest quest, Settlement settlement)
        {
            foreach (GlobalTargetInfo target in quest.QuestLookTargets)
            {
                if (target.WorldObject == settlement)
                    return true;
            }

            List<QuestPart> parts = quest.PartsListForReading;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] is QuestPart_InitiateTradeRequest request && request.settlement == settlement)
                    return true;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(WorldObjectsHolder), nameof(WorldObjectsHolder.Remove))]
    public static class Patch_FailTradeRequestOnSettlementRemoved
    {
        public static void Prefix(WorldObject o)
        {
            VanillaTradeRequestQuestHelper.FailIfSettlementLost(o);
        }
    }
}
