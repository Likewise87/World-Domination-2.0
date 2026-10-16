using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Shared debug/setup UI helpers: wizard float menus anchored near the clicked dialog button.
    /// </summary>
    public static class DebugActions_FloatMenus
    {
        private static Vector2? nextMenuAnchor;

        /// <summary>Call from dialog dash buttons on click (before starting menus/targeters).</summary>
        public static void NotifyDialogButtonClicked(Rect buttonLocalRect)
        {
            if (Event.current != null)
                Event.current.Use();
            nextMenuAnchor = UI.GUIToScreenPoint(new Vector2(buttonLocalRect.xMax + 56f, buttonLocalRect.y));
        }

        public static void OpenCentered(List<FloatMenuOption> options)
        {
            Vector2 pos;
            if (nextMenuAnchor.HasValue)
                pos = nextMenuAnchor.Value;
            else
            {
                Vector2 m = UI.MousePositionOnUIInverted;
                pos = new Vector2(m.x + 20f, m.y);
            }

            var menu = new WdFixedPosFloatMenu(options, pos);
            menu.vanishIfMouseDistant = false;
            Find.WindowStack.Add(menu);
        }

        /// <summary>
        /// Player plus placeable NPC factions (non-hidden, not defeated, not WD-excluded).
        /// Used by forts / World Setup. Do not add defeated here.
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
                if (!WorldActions_Utils.IsWdParticipant(f))
                    continue;
                into.Add(f);
            }
        }

        /// <summary>
        /// NPC WD participants only (no player). Optionally includes defeated empires.
        /// </summary>
        public static void CollectWdParticipantFactions(List<Faction> into, bool includeDefeated)
        {
            into.Clear();
            var factions = Find.FactionManager?.AllFactionsListForReading;
            if (factions == null) return;

            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                if (f == null || f.IsPlayer || f.def == null || f.def.hidden)
                    continue;
                if (!includeDefeated && f.defeated)
                    continue;
                if (!WorldActions_Utils.IsWdParticipant(f))
                    continue;
                into.Add(f);
            }
        }

        public static string FactionMenuLabel(Faction f)
        {
            if (f == null) return "?";
            if (f.defeated)
                return f.Name + " [defeated]";
            return f.Name;
        }
    }
}
