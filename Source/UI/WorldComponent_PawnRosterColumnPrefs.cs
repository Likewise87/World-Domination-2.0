using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Per-save column visibility for All Player Pawns, Outpost Pawns tab, and Prisoners.</summary>
    public class WorldComponent_PawnRosterColumnPrefs : WorldComponent
    {
        private Dictionary<string, bool> allPlayerPawns = new Dictionary<string, bool>();
        private Dictionary<string, bool> outpostPawns = new Dictionary<string, bool>();
        private Dictionary<string, bool> prisoners = new Dictionary<string, bool>();

        private static WorldComponent_PawnRosterColumnPrefs cached;

        public WorldComponent_PawnRosterColumnPrefs(World world) : base(world)
        {
            cached = this;
        }

        public static WorldComponent_PawnRosterColumnPrefs Get()
        {
            if (cached != null && cached.world == Find.World) return cached;
            cached = Find.World?.GetComponent<WorldComponent_PawnRosterColumnPrefs>();
            return cached;
        }

        public bool IsVisible(PawnRosterColumnWindow window, string id)
        {
            if (id.NullOrEmpty()) return true;
            Dictionary<string, bool> map = MapFor(window);
            if (map != null && map.TryGetValue(id, out bool v))
                return v;
            return PawnRosterColumnCatalog.DefaultVisible(window, id);
        }

        public void SetVisible(PawnRosterColumnWindow window, string id, bool visible)
        {
            if (id.NullOrEmpty()) return;
            Dictionary<string, bool> map = MapFor(window);
            if (map == null) return;
            map[id] = visible;
        }

        public void ResetToDefaults(PawnRosterColumnWindow window)
        {
            Dictionary<string, bool> map = MapFor(window);
            map?.Clear();
        }

        public bool DiffersFromDefaults(PawnRosterColumnWindow window)
        {
            IReadOnlyList<PawnRosterColumnOption> opts = PawnRosterColumnCatalog.OptionsFor(window);
            for (int i = 0; i < opts.Count; i++)
            {
                PawnRosterColumnOption opt = opts[i];
                if (IsVisible(window, opt.Id) != opt.DefaultVisible)
                    return true;
            }
            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref allPlayerPawns, "pawnRosterColsAllPlayer", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref outpostPawns, "pawnRosterColsOutpost", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref prisoners, "pawnRosterColsPrisoners", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                allPlayerPawns ??= new Dictionary<string, bool>();
                outpostPawns ??= new Dictionary<string, bool>();
                prisoners ??= new Dictionary<string, bool>();
            }
        }

        private Dictionary<string, bool> MapFor(PawnRosterColumnWindow window)
        {
            switch (window)
            {
                case PawnRosterColumnWindow.OutpostPawns: return outpostPawns;
                case PawnRosterColumnWindow.Prisoners: return prisoners;
                default: return allPlayerPawns;
            }
        }
    }
}
