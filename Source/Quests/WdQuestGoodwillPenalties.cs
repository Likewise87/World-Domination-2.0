using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Quest-fail goodwill loss for future WD quests.
    /// Call explicitly on fail paths; <c>quest.End(Fail)</c> alone does not apply this.
    /// <paramref name="amount"/> must be negative (default -20).
    /// </summary>
    public static class WdQuestGoodwillPenalties
    {
        public const int DefaultLoss = -20;

        /// <summary>Applies a negative goodwill change toward the player. Returns false if nothing applied.</summary>
        public static bool ApplyLossOnQuestFailed(Faction asker, int amount = DefaultLoss, string reasonKey = null)
        {
            if (asker == null || asker.IsPlayer || asker.defeated)
                return false;
            if (amount >= 0)
                return false;

            if (!GoodwillChangeNotifier.TryAffectPlayerGoodwill(asker, amount, out int now))
                return false;

            string factionName = asker.Name ?? "Faction";
            string changeStr = amount.ToString("+0;-#");
            if (!reasonKey.NullOrEmpty())
            {
                Messages.Message(
                    "TSA_WD_QuestGoodwill_LossWithReason".Translate(factionName, changeStr, now, reasonKey.Translate()),
                    MessageTypeDefOf.NegativeEvent);
            }
            else
            {
                Messages.Message(
                    "TSA_WD_QuestGoodwill_Loss".Translate(factionName, changeStr, now),
                    MessageTypeDefOf.NegativeEvent);
            }

            return true;
        }
    }
}
