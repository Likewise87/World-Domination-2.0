using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    public static class DebugActions_OutpostTypeChange
    {
        [DebugAction("World Domination", "Force outpost type change…",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void ForceOutpostTypeChange()
        {
            WorldObject_WD_Outpost outpost = Find.WorldSelector?.SingleSelectedObject as WorldObject_WD_Outpost;
            if (outpost == null || outpost.Faction != Faction.OfPlayer || outpost.Destroyed)
            {
                Messages.Message("WD debug: select a player outpost.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            var opts = new List<FloatMenuOption>();
            foreach (var d in WD_OutpostSelectionCachedDefs.List)
            {
                if (d == null || d.defName == outpost.def?.defName) continue;
                WorldObjectDef captured = d;
                WorldObject_WD_Outpost capturedOutpost = outpost;
                opts.Add(new FloatMenuOption(captured.LabelCap, () =>
                {
                    if (OutpostTypeChangeUtility.DebugForceApply(capturedOutpost, captured, out string reason))
                        Messages.Message($"WD debug: applied {captured.defName}", MessageTypeDefOf.TaskCompletion, false);
                    else
                        Messages.Message(reason ?? "WD debug: apply failed", MessageTypeDefOf.RejectInput, false);
                }));
            }
            if (opts.Count == 0)
            {
                Messages.Message("WD debug: no alternate types.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            DebugActions_FloatMenus.OpenCentered(opts);
        }
    }
}
