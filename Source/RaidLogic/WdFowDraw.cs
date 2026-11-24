using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Session FoW overlay. Normal play uses player discovery when Settlement Intel is on.
    /// Debug "View World as" may override <see cref="ViewFaction"/> to an NPC.
    /// </summary>
    public static class WdFowDraw
    {
        public const float DimAlphaMul = 0.40f;


        /// <summary>
        /// Null or player = player FoW (normal play).
        /// Non-player = debug View World as that faction. Session only.
        /// </summary>
        public static Faction ViewFaction;

        /// <summary>Session toggle (default on). Debug may turn off to see full map while inspecting.</summary>
        public static bool ShowFogOfWar = true;

        /// <summary>
        /// Tracking/explore gate: Settlement Intel kill+gate on and session FoW draw enabled.
        /// Keep true while the player toggle is off so caravan/airborne explore and sight disks still run.
        /// </summary>
        public static bool IsActive =>
            ShowFogOfWar
            && WorldComponent_SettlementIntel.IsGateConfigured();

        /// <summary>
        /// Whether the current view should hide/draw FoW tiles and icons.
        /// Player view also requires <see cref="WorldDominationSettings.intelPlayerFogOfWar"/>.
        /// Debug View-as-NPC still draws that faction's fog when the player toggle is off.
        /// </summary>
        public static bool DrawFogForCurrentView
        {
            get
            {
                if (!IsActive) return false;
                if (!IsViewingAsPlayer) return true;
                var seth = WorldDominationMod.settings;
                return seth == null || seth.intelPlayerFogOfWar;
            }
        }

        /// <summary>Faction whose discovery / explored tiles drive the overlay.</summary>
        public static Faction EffectiveViewFaction
        {
            get
            {
                if (ViewFaction != null && !ViewFaction.IsPlayer)
                    return ViewFaction;
                return Faction.OfPlayerSilentFail;
            }
        }

        public static bool IsFowTarget(WorldObject wo)
        {
            if (wo == null || wo.Destroyed) return false;
            if (wo is Settlement) return true;
            if (wo is WorldObject_WD_Outpost) return true;
            if (wo is WorldObject_AT_Turret) return true;
            if (wo is WorldObject_WD_PlayerWatchTower) return true;
            if (wo is WorldObject_WD_AdvancedBuilding) return true;
            if (wo is WorldObject_Traveler t)
                return !WorldObject_Traveler.IsShellMission(t.mission);
            return false;
        }

        /// <summary>
        /// Tile overlays that are not WorldObjects (traps / road blocks / trench edges):
        /// hide on fully black (unexplored) tiles when FoW is active.
        /// </summary>
        public static bool ShouldDrawTileOverlay(int tileId)
        {
            if (!DrawFogForCurrentView) return true;
            if (tileId < 0) return false;
            var intel = WorldComponent_SettlementIntel.Get();
            if (intel?.TileFow == null) return true;
            return intel.TileFow.IsTileExploredForView(tileId);
        }

        public static void SetShowFogOfWar(bool enabled)
        {
            if (ShowFogOfWar == enabled) return;
            ShowFogOfWar = enabled;
            var intel = WorldComponent_SettlementIntel.Get();
            intel?.InvalidateFowDrawCache();
            intel?.NotifyTileFowViewChanged();
            Patch_WdWorldObjectNoExpandingIcon.NotifyIconModeChanged();
        }

        public static void SetViewFaction(Faction faction)
        {
            if (faction != null && !WorldComponent_SettlementIntel.IsGateConfigured())
                faction = null;
            // Player / null = player FoW (not omniscient).
            if (faction != null && faction.IsPlayer)
                faction = null;
            if (ViewFaction == faction) return;
            ViewFaction = faction;
            var intel = WorldComponent_SettlementIntel.Get();
            intel?.InvalidateFowDrawCache();
            intel?.NotifyTileFowViewChanged();
            Patch_WdWorldObjectNoExpandingIcon.NotifyIconModeChanged();
        }

        /// <summary>True when overlay uses player discovery (normal play or debug reset).</summary>
        public static bool IsViewingAsPlayer =>
            ViewFaction == null || ViewFaction.IsPlayer;

        public static void ClearIfGateOff()
        {
            if (!WorldComponent_SettlementIntel.IsGateConfigured())
            {
                if (ViewFaction != null)
                    SetViewFaction(null);
                if (!ShowFogOfWar)
                    SetShowFogOfWar(true);
            }
        }

        /// <summary>Multiply alpha when dimmed. Cache read only.</summary>
        public static Color ApplyIconColor(WorldObject wo, Color c)
        {
            // Cache read only on draw path.
            if (wo == null || !DrawFogForCurrentView) return c;
            var intel = WorldComponent_SettlementIntel.Get();
            if (intel == null || !intel.IsFowDimmed(wo)) return c;
            c.a *= DimAlphaMul;
            return c;
        }
    }
}
