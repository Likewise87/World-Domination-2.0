using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Player colony helpers. Influence Radius was removed; raid reach uses settlement attack range.
    /// </summary>
    public static class InfluenceUtils
    {
        /// <summary>Finds the single player colony Settlement on the WD planet surface. Returns null if none exists yet.</summary>
        public static Settlement GetPlayerColony()
        {
            var list = Find.WorldObjects?.Settlements;
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null || list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                Settlement s = list[i];
                if (s.Faction != player) continue;
                if (!WorldActions_Utils.IsWdSurfaceTile(s.Tile)) continue;
                return s;
            }
            return null;
        }
    }
}
