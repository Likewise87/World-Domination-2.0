using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Player-painted tiles where allies (and optionally neutrals) may not place new road blocks / traps.
    /// </summary>
    public class WorldComponent_FortifyBlacklist : WorldComponent
    {
        private HashSet<int> tiles = new HashSet<int>();
        private List<int> scribeList;

        public WorldComponent_FortifyBlacklist(World world) : base(world)
        {
        }

        public static WorldComponent_FortifyBlacklist Get() =>
            Find.World?.GetComponent<WorldComponent_FortifyBlacklist>();

        public int Count => tiles?.Count ?? 0;

        public IEnumerable<int> EnumerateTiles()
        {
            if (tiles == null) yield break;
            foreach (int t in tiles)
                yield return t;
        }

        public bool Contains(int tileId) =>
            tileId >= 0 && tiles != null && tiles.Contains(tileId);

        public void AddRange(IEnumerable<int> toAdd)
        {
            if (toAdd == null) return;
            tiles ??= new HashSet<int>();
            bool changed = false;
            foreach (int t in toAdd)
            {
                if (t < 0) continue;
                if (tiles.Add(t)) changed = true;
            }
            if (changed) NotifyOverlayDirty();
        }

        public void RemoveRange(IEnumerable<int> toRemove)
        {
            if (toRemove == null || tiles == null || tiles.Count == 0) return;
            bool changed = false;
            foreach (int t in toRemove)
            {
                if (tiles.Remove(t)) changed = true;
            }
            if (changed) NotifyOverlayDirty();
        }

        public void Clear()
        {
            if (tiles == null || tiles.Count == 0) return;
            tiles.Clear();
            NotifyOverlayDirty();
        }

        /// <summary>True when NPC fortify must skip this tile for the given actor faction.</summary>
        public static bool BlocksNpcFortify(int tileId, Faction actorFaction)
        {
            var seth = WorldDominationMod.settings;
            if (!(seth?.enableFortifyBlacklist ?? WorldDominationSettings.DefEnableFortifyBlacklist)) return false;
            if (actorFaction == null || actorFaction.IsPlayer) return false;

            var bl = Get();
            if (bl == null || !bl.Contains(tileId)) return false;

            Faction player = Faction.OfPlayerSilentFail;
            if (player == null) return false;

            FactionRelationKind kind = WorldActions_Utils.SafeRelationKindWith(actorFaction, player);
            if (kind == FactionRelationKind.Ally) return true;
            if (kind == FactionRelationKind.Neutral && (seth?.fortifyBlacklistApplyToNeutral ?? WorldDominationSettings.DefFortifyBlacklistApplyToNeutral))
                return true;
            return false;
        }

        public static void NotifyOverlayDirty()
        {
            WorldComponent_WDVisualizerToggle.MarkFortifyBlacklistOverlayDirtyPublic();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                scribeList = tiles != null && tiles.Count > 0
                    ? new List<int>(tiles)
                    : null;
            }

            Scribe_Collections.Look(ref scribeList, "fortifyBlacklistTiles", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                tiles = new HashSet<int>();
                if (scribeList != null)
                {
                    for (int i = 0; i < scribeList.Count; i++)
                    {
                        int t = scribeList[i];
                        if (t >= 0) tiles.Add(t);
                    }
                }
                scribeList = null;
            }
        }
    }
}
