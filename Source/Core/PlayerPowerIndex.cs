using UnityEngine;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Transparent escalation metrics: player power is measured only by total WD outpost strength
    /// (same number as the player row in <see cref="Window_WorldStats"/>). Mid then Late activate when
    /// EITHER the player's global strength share OR the absolute outpost strength crosses that stage's
    /// threshold. No colony wealth, threat tiers, or apex logic.
    /// </summary>
    public static class PlayerPowerIndex
    {
        /// <summary>Sum of every player WD outpost's local defense power from the daily snapshot.</summary>
        public static float GetPlayerOutpostStrength(DailyWorldSnapshot snapshot)
        {
            float total = 0f;
            if (snapshot?.PlayerOutposts != null)
            {
                for (int i = 0; i < snapshot.PlayerOutposts.Count; i++)
                {
                    var o = snapshot.PlayerOutposts[i];
                    if (o == null || o.Destroyed) continue;
                    var comp = o.GetComponent<CompViralSpread>();
                    if (comp != null) total += comp.GetTotalLocalDefensePower();
                }
            }
            return Mathf.Max(0f, total);
        }

        /// <summary>Player outpost strength as a fraction of world total strength (which already includes player outposts).</summary>
        public static float ComputeGlobalShare(float playerOutpostStrength, SpreadLogEntry.GlobalWorldStats stats)
        {
            return stats == null ? 0f : ComputeGlobalShare(playerOutpostStrength, stats.GlobalTotalStr);
        }

        /// <summary>Share against an already-known world total, for refreshes that must not rescan the world.</summary>
        public static float ComputeGlobalShare(float playerOutpostStrength, float worldTotalStrength)
        {
            float denom = Mathf.Max(0f, worldTotalStrength);
            return denom > 0f ? Mathf.Clamp01(playerOutpostStrength / denom) : 0f;
        }

        /// <summary>Active escalation stage (Late overrides Mid).</summary>
        public static WdEscalationStage GetEscalationStage(float playerOutpostStrength, float globalShare, WorldDominationSettings seth)
            => WdEscalation.GetStage(playerOutpostStrength, globalShare, seth);

        /// <summary>True when Late stage is active.</summary>
        public static bool IsLateModifierActive(float playerOutpostStrength, float globalShare, WorldDominationSettings seth)
            => GetEscalationStage(playerOutpostStrength, globalShare, seth) == WdEscalationStage.Late;

        /// <summary>Deprecated name kept for call-site clarity: Late stage only (T4 gates).</summary>
        public static bool IsModifierActive(float playerOutpostStrength, float globalShare, WorldDominationSettings seth)
            => IsLateModifierActive(playerOutpostStrength, globalShare, seth);
    }
}
