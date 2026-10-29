using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Player wipe of a WD traveler that was bound for an NPC settlement can grant goodwill
    /// with that settlement's faction. SSoT for open-field Destroy hooks, clash-map wins, and choke.
    /// </summary>
    public static class NpcRaidKillGoodwillUtility
    {
        public const float StrengthPerGoodwillUnit = 500f;

        public static void TryRewardPlayerForKillingTraveler(WorldObject_Traveler traveler, bool playerAttributed)
        {
            if (!playerAttributed || traveler == null) return;
            if (traveler.npcKillGoodwillGranted) return;

            if (TryRewardCore(
                traveler.Faction,
                traveler.travelerStrength,
                traveler.targetObject,
                traveler.originalTargetObject,
                traveler.cachedTargetKind,
                traveler.isTurretDetour,
                traveler.preTurretDetourTarget))
            {
                traveler.npcKillGoodwillGranted = true;
            }
        }

        /// <summary>Clash-map path: traveler already Destroy'd; use snapshot fields from StoreAndDestroy.</summary>
        public static void TryRewardFromSnapshot(
            Faction travelerFaction,
            float travelerStrength,
            WorldObject currentTarget,
            WorldObject originalTarget,
            RaidLaunchTargetKind cachedTargetKind,
            bool isTurretDetour,
            WorldObject preTurretDetourTarget,
            ref bool goodwillGrantedOnWin)
        {
            if (goodwillGrantedOnWin) return;

            if (TryRewardCore(
                travelerFaction,
                travelerStrength,
                currentTarget,
                originalTarget,
                cachedTargetKind,
                isTurretDetour,
                preTurretDetourTarget))
            {
                goodwillGrantedOnWin = true;
            }
        }

        /// <returns>True when goodwill was applied (caller should set idempotency flag).</returns>
        private static bool TryRewardCore(
            Faction travelerFaction,
            float travelerStrength,
            WorldObject currentTarget,
            WorldObject originalTarget,
            RaidLaunchTargetKind cachedTargetKind,
            bool isTurretDetour,
            WorldObject preTurretDetourTarget)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || seth.npcRaidKillGoodwillPer500Strength <= 0) return false;

            Settlement protectedSite = ResolveProtectedSettlement(
                currentTarget, originalTarget, cachedTargetKind, isTurretDetour, preTurretDetourTarget);
            if (protectedSite == null) return false;

            Faction recipient = protectedSite.Faction;
            if (!IsValidRecipient(recipient, travelerFaction)) return false;

            // Only travelers that were a threat to the protected faction (not their traders / friends).
            if (!WorldActions_Utils.SafeHostileTo(travelerFaction, recipient)) return false;

            int amount = ComputeGoodwillAmount(travelerStrength, seth.npcRaidKillGoodwillPer500Strength);
            if (amount <= 0) return false;

            GoodwillChangeNotifier.NotifyNpcTravelerKillAssist(recipient, protectedSite, amount);
            return true;
        }

        public static int ComputeGoodwillAmount(float travelerStrength, float per500)
        {
            if (per500 <= 0f || travelerStrength <= 0f) return 0;
            float raw = (travelerStrength / StrengthPerGoodwillUnit) * per500;
            return Mathf.RoundToInt(raw);
        }

        public static Settlement ResolveProtectedSettlement(
            WorldObject currentTarget,
            WorldObject originalTarget,
            RaidLaunchTargetKind cachedTargetKind,
            bool isTurretDetour,
            WorldObject preTurretDetourTarget)
        {
            if (IsNpcSettlement(currentTarget))
                return (Settlement)currentTarget;

            if (isTurretDetour && IsNpcSettlement(preTurretDetourTarget))
                return (Settlement)preTurretDetourTarget;

            bool kindIsPlayer = cachedTargetKind == RaidLaunchTargetKind.PlayerColony
                || cachedTargetKind == RaidLaunchTargetKind.PlayerSimulated;
            if (!kindIsPlayer
                && IsNpcSettlement(originalTarget)
                && IsPlayerSite(currentTarget))
                return (Settlement)originalTarget;

            return null;
        }

        private static bool IsNpcSettlement(WorldObject wo) =>
            wo is Settlement s
            && !s.Destroyed
            && s.Faction != null
            && !s.Faction.IsPlayer;

        private static bool IsPlayerSite(WorldObject wo)
        {
            if (wo == null || wo.Destroyed) return false;
            if (wo is Settlement s && s.Faction?.IsPlayer == true) return true;
            if (wo is WorldObject_WD_Outpost) return true;
            if (Outpost_EstablishmentRequirements.IsActiveCamp(wo)) return true;
            return false;
        }

        private static bool IsValidRecipient(Faction recipient, Faction travelerFaction)
        {
            if (recipient == null || recipient.IsPlayer) return false;
            if (recipient.defeated || recipient.def == null || recipient.def.hidden) return false;
            if (travelerFaction != null && recipient == travelerFaction) return false;
            if (WorldActions_Utils.IsPermanentEnemyOfPlayer(recipient)) return false;
            return true;
        }
    }
}
