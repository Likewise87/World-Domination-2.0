using LudeonTK;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Single Dev Tools entry for the Playing-time WD world debug overlay.</summary>
    public static class DebugActions_WorldDebug
    {
        [DebugAction("World Domination", "WD Debug Tools",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void OpenWdWorldDebug()
        {
            if (Find.WindowStack == null) return;
            var existing = Find.WindowStack.WindowOfType<Dialog_WdWorldDebug>();
            if (existing != null)
            {
                Find.WindowStack.TryRemove(existing);
                return;
            }
            Find.WindowStack.Add(new Dialog_WdWorldDebug());
        }
    }
}
