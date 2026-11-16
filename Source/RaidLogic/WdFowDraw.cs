using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Session-only "View intel as" FoW overlay: hide/dim world icons from a chosen faction's discovery.
    /// Not ModSettings. Draw helpers read SettlementIntel cache only (no sight math on the draw path).
    /// </summary>
    public static class WdFowDraw
    {
        public const float DimAlphaMul = 0.40f;

        /// <summary>
        /// Null or player = omniscient map (player sees everything).
        /// Non-player faction = hide/dim from that faction's discovery. Session only.
        /// </summary>
        public static Faction ViewFaction;

        /// <summary>True only when viewing as a non-player faction (FoW overlay on).</summary>
        public static bool IsActive =>
            ViewFaction != null
            && !ViewFaction.IsPlayer
            && WorldComponent_SettlementIntel.IsGateConfigured();

        public static bool IsFowTarget(WorldObject wo)
        {
            if (wo == null || wo.Destroyed) return false;
            if (wo is Settlement) return true;
            if (wo is WorldObject_WD_Outpost) return true;
            if (wo is WorldObject_AT_Turret) return true;
            if (wo is WorldObject_WD_AdvancedBuilding) return true;
            if (wo is WorldObject_Traveler t)
                return !WorldObject_Traveler.IsShellMission(t.mission);
            return false;
        }

        public static void SetViewFaction(Faction faction)
        {
            if (faction != null && !WorldComponent_SettlementIntel.IsGateConfigured())
                faction = null;
            // Player selection = omniscient (same as clearing to null).
            if (faction != null && faction.IsPlayer)
                faction = null;
            if (ViewFaction == faction) return;
            ViewFaction = faction;
            WorldComponent_SettlementIntel.Get()?.InvalidateFowDrawCache();
            Patch_WdWorldObjectNoExpandingIcon.NotifyIconModeChanged();
        }

        /// <summary>True when the menu should mark Player / omniscient as selected.</summary>
        public static bool IsViewingAsPlayer =>
            ViewFaction == null || ViewFaction.IsPlayer;

        public static void ClearIfGateOff()
        {
            if (ViewFaction != null && !WorldComponent_SettlementIntel.IsGateConfigured())
                SetViewFaction(null);
        }

        /// <summary>Multiply alpha when dimmed. Cache read only.</summary>
        public static Color ApplyIconColor(WorldObject wo, Color c)
        {
            if (wo == null || !IsActive) return c;
            var intel = WorldComponent_SettlementIntel.Get();
            if (intel == null) return c;
            intel.EnsureFowDrawCacheFresh();
            if (!intel.IsFowDimmed(wo)) return c;
            c.a *= DimAlphaMul;
            return c;
        }
    }
}
