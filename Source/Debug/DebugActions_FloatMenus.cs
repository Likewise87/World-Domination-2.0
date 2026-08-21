using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Shared debug UI helpers (centered float menus survive the edge-docked Dev toolbar).</summary>
    public static class DebugActions_FloatMenus
    {
        public static void OpenCentered(List<FloatMenuOption> options)
        {
            Find.WindowStack.Add(new CenteredDebugFloatMenu(options));
        }

        /// <summary>
        /// Player plus placeable NPC factions (non-hidden, not defeated, not WD-excluded).
        /// </summary>
        public static void CollectDebugFactions(List<Faction> into)
        {
            into.Clear();
            var factions = Find.FactionManager?.AllFactionsListForReading;
            if (factions == null) return;

            Faction player = Faction.OfPlayer;
            if (player != null)
                into.Add(player);

            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                if (f == null || f.IsPlayer || f.defeated || f.def == null || f.def.hidden)
                    continue;
                if (WorldActions_Utils.IsExcludedFaction(f))
                    continue;
                into.Add(f);
            }
        }

        private sealed class CenteredDebugFloatMenu : FloatMenu
        {
            public CenteredDebugFloatMenu(List<FloatMenuOption> options) : base(options)
            {
                vanishIfMouseDistant = false;
            }

            protected override void SetInitialSizeAndPosition()
            {
                Vector2 size = InitialSize;
                float x = (UI.screenWidth - size.x) * 0.5f;
                float y = (UI.screenHeight - size.y) * 0.5f;
                if (x < 0f) x = 0f;
                if (y < 0f) y = 0f;
                windowRect = new Rect(x, y, size.x, size.y);
            }
        }
    }
}
