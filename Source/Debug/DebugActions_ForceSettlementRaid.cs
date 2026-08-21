using System.Linq;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Dev tools: force an NPC settlement through <see cref="WorldActions_Raid"/> immediately.</summary>
    public static class DebugActions_ForceSettlementRaid
    {
        [DebugAction("World Domination", "Force raid from clicked NPC settlement",
            actionType = DebugActionType.ToolWorld,
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void ForceRaidFromClickedSettlement()
        {
            TryForceRaidAtMouse(forceDropPod: false);
        }

        [DebugAction("World Domination", "Force DROP-POD raid from clicked T4 settlement",
            actionType = DebugActionType.ToolWorld,
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void ForceDropPodRaidFromClickedSettlement()
        {
            TryForceRaidAtMouse(forceDropPod: true);
        }

        private static void TryForceRaidAtMouse(bool forceDropPod)
        {
            int tile = GenWorld.MouseTile();
            if (tile < 0) return;

            Settlement settlement = Find.WorldObjects.ObjectsAt(tile).OfType<Settlement>().FirstOrDefault();
            if (settlement == null)
            {
                Messages.Message("WD debug: click an NPC settlement.", MessageTypeDefOf.RejectInput);
                return;
            }

            if (settlement.Faction == null || settlement.Faction.IsPlayer)
            {
                Messages.Message("WD debug: click an NPC settlement (not player).", MessageTypeDefOf.RejectInput);
                return;
            }

            if (!WorldActions_Raid.DebugForceImmediateRaid(settlement, forceDropPod, out string failReason))
            {
                Messages.Message(
                    "WD debug force raid failed (" + settlement.LabelCap + "): " + (failReason ?? "unknown"),
                    MessageTypeDefOf.RejectInput);
                return;
            }

            string mode = forceDropPod ? "drop-pod" : "normal (walk or drop by rules)";
            Messages.Message(
                "WD debug: forced " + mode + " raid from " + settlement.LabelCap + ".",
                MessageTypeDefOf.PositiveEvent);
        }
    }
}
