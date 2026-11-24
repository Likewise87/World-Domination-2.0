using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Player-facing FoW helpers for letters, LookTargets, jumps, and list UIs.
    /// No-ops / full reveal when Settlement Intel kill switch is off.
    /// </summary>
    public static class WdPlayerIntel
    {
        public static bool TrackingActive => WorldComponent_SettlementIntel.IsTracking();

        /// <summary>Tracking on and player FoW toggle on. Off = player omniscient for UI/interaction; NPC raid Knows unchanged.</summary>
        public static bool PlayerFogActive
        {
            get
            {
                if (!TrackingActive) return false;
                var seth = WorldDominationMod.settings;
                return seth == null || seth.intelPlayerFogOfWar;
            }
        }

        public static Faction Player => Faction.OfPlayerSilentFail;

        public static bool PlayerKnows(WorldObject wo)
        {
            if (wo == null || wo.Destroyed) return false;
            if (!PlayerFogActive) return true;
            var intel = WorldComponent_SettlementIntel.Get();
            if (intel == null) return true;
            Faction p = Player;
            if (p == null) return true;
            if (wo.Faction == p) return true;
            if (wo is WorldObject_Traveler t)
                return intel.IsTravelerDiscovered(p, t);
            if (wo is Caravan)
                return IsCurrentlyVisibleToFaction(p, wo);
            return intel.IsDiscovered(p, wo);
        }

        public static bool PlayerKnowsTraveler(WorldObject_Traveler t) => PlayerKnows(t);

        /// <summary>Whether letters/alerts may jump or LookTarget this object.</summary>
        public static bool CanJumpOrLookTarget(WorldObject wo)
        {
            if (wo == null || wo.Destroyed) return false;
            if (!PlayerFogActive) return true;
            var seth = WorldDominationMod.settings;
            if (seth != null && seth.intelNotifyJumpUndiscovered) return true;
            Faction p = Player;
            if (p != null && wo.Faction == p) return true;
            return PlayerKnows(wo);
        }

        public static bool ShouldListTraveler(WorldObject_Traveler t)
        {
            if (t == null || t.Destroyed) return false;
            if (!PlayerFogActive) return true;
            Faction p = Player;
            if (p != null && t.Faction == p) return true;
            var seth = WorldDominationMod.settings;
            if (seth != null && seth.intelListUndiscoveredTravelers) return true;
            return PlayerKnowsTraveler(t);
        }

        /// <summary>Letter/inspect label: full name if known, else faction name or unknown.</summary>
        public static string LabelForLetter(WorldObject wo)
        {
            if (wo == null || wo.Destroyed) return "?";
            if (!PlayerFogActive || PlayerKnows(wo))
                return wo.LabelCap;
            Faction f = wo.Faction;
            if (f != null)
                return f.Name;
            return "TSA_WD_Intel_UnknownSite".Translate().ToString();
        }

        /// <summary>Faction Details row name: ??? when undiscovered.</summary>
        public static string FoggedSettlementListName(WorldObject wo)
        {
            if (wo == null || wo.Destroyed) return "?";
            if (!PlayerFogActive || PlayerKnows(wo))
                return wo.LabelCap;
            return FoggedNameLabel;
        }

        public static string FoggedNameLabel => "TSA_WD_Intel_FoggedName".Translate().ToString();

        public static string SettlementUndiscoveredTip =>
            "TSA_WD_Intel_SettlementUndiscoveredTip".Translate().ToString();

        /// <summary>Any UI cell showing ??? (fogged name) should use this tip.</summary>
        public static void TipIfFoggedName(Rect rect, string display)
        {
            if (string.IsNullOrEmpty(display)) return;
            if (display == FoggedNameLabel)
                TooltipHandler.TipRegion(rect, SettlementUndiscoveredTip);
        }

        public static LookTargets LookTargetsOrFallback(WorldObject preferred, WorldObject fallback)
        {
            if (CanJumpOrLookTarget(preferred))
                return new LookTargets(preferred);
            if (CanJumpOrLookTarget(fallback))
                return new LookTargets(fallback);
            // Undiscovered: do not silent-jump to tile (reveals origin).
            return null;
        }

        /// <summary>Letter LookTargets helper: null when FoW forbids jump.</summary>
        public static LookTargets LookTargetsOrNull(WorldObject wo) =>
            CanJumpOrLookTarget(wo) ? new LookTargets(wo) : null;

        public static LookTargets LookTargetsOrNull(WorldObject preferred, WorldObject fallback) =>
            LookTargetsOrFallback(preferred, fallback);

        /// <summary>Keep only jump-allowed objects for multi-target letters.</summary>
        public static LookTargets LookTargetsFromKnown(List<WorldObject> candidates)
        {
            if (candidates == null || candidates.Count == 0) return null;
            if (!PlayerFogActive) return new LookTargets(candidates);
            var known = new List<WorldObject>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                WorldObject wo = candidates[i];
                if (CanJumpOrLookTarget(wo))
                    known.Add(wo);
            }
            return known.Count > 0 ? new LookTargets(known) : null;
        }

        /// <summary>True if the firing faction currently has LOS on the target (not merely discovered).</summary>
        public static bool IsCurrentlyVisibleToFaction(Faction faction, WorldObject target)
        {
            if (faction == null || target == null || target.Destroyed) return false;
            if (!TrackingActive) return true;
            // Player mortar / interact: respect player FoW toggle without Discover-all.
            if (faction.IsPlayer && !PlayerFogActive) return true;
            if (target.Faction == faction) return true;
            var intel = WorldComponent_SettlementIntel.Get();
            if (intel == null) return true;
            return intel.IsInCurrentSightOfFaction(faction, target);
        }

        /// <summary>
        /// Scout-quest-style reveal: Discover for the player plus a short FoW tile disk (6 tiles).
        /// </summary>
        public const float SettlementRevealRadiusTiles = 6f;

        public static void RevealSettlementToPlayer(Settlement settlement)
        {
            if (settlement == null || settlement.Destroyed) return;
            Faction player = Player;
            if (player == null) return;

            WorldComponent_SettlementIntel intel = WorldComponent_SettlementIntel.Get();
            if (intel == null) return;

            intel.Discover(player, settlement);
            if (WorldComponent_SettlementIntel.IsTracking())
                intel.TileFow.RevealTilesInRadius(player, settlement.Tile, SettlementRevealRadiusTiles);
        }

        /// <summary>
        /// When <see cref="WorldDominationSettings.intelUncoverOnRaid"/> is on, reveal a raiding settlement
        /// at player-bound launch (map + Discover) so incoming letters can name and jump to it.
        /// </summary>
        public static void TryUncoverRaiderSettlementOnPlayerRaid(WorldObject attacker)
        {
            if (attacker is not Settlement settlement || settlement.Destroyed) return;
            if (!TrackingActive) return;
            var seth = WorldDominationMod.settings;
            if (seth == null || !seth.intelUncoverOnRaid) return;
            RevealSettlementToPlayer(settlement);
        }
    }
}
