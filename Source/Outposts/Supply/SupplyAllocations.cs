using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public enum SupplyResource
    {
        Food = 0,
        Tooling = 1,
        Power = 2,
    }

    public enum SupplyMode
    {
        Smart = 0,
        Manual = 1,
    }

    /// <summary>One source-to-consumer transfer as last computed by a grid (tooling per day, or watts).</summary>
    public struct SupplyFlow
    {
        public int sourceId;
        public int destId;
        public int sourceTile;
        public int destTile;
        public float amount;
    }

    /// <summary>
    /// Saved manual allocations for tooling and power, keyed by tile (stable across the outpost type-change move).
    /// Food keeps its own links in <see cref="WorldComponent_LogisticsManager.manualLinks"/>.
    /// </summary>
    public class WorldComponent_SupplyAllocations : WorldComponent
    {
        public class Link : IExposable
        {
            public int resource;
            public int sourceTile;
            public int destTile;
            public float amount;

            public void ExposeData()
            {
                Scribe_Values.Look(ref resource, "resource");
                Scribe_Values.Look(ref sourceTile, "sourceTile");
                Scribe_Values.Look(ref destTile, "destTile");
                Scribe_Values.Look(ref amount, "amount");
            }
        }

        public class ModeEntry : IExposable
        {
            public int resource;
            public int sourceTile;
            public int mode;

            public void ExposeData()
            {
                Scribe_Values.Look(ref resource, "resource");
                Scribe_Values.Look(ref sourceTile, "sourceTile");
                Scribe_Values.Look(ref mode, "mode");
            }
        }

        public List<Link> links = new List<Link>();
        public List<ModeEntry> modes = new List<ModeEntry>();

        public WorldComponent_SupplyAllocations(World world) : base(world) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref links, "links", LookMode.Deep);
            Scribe_Collections.Look(ref modes, "modes", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                links ??= new List<Link>();
                modes ??= new List<ModeEntry>();
                links.RemoveAll(l => l == null);
                modes.RemoveAll(m => m == null);
            }
        }
    }

    /// <summary>Facade over <see cref="WorldComponent_SupplyAllocations"/> and the tooling / power grids (Tooling and Power only).</summary>
    public static class SupplyAllocations
    {
        private static WorldComponent_SupplyAllocations cached;

        private static WorldComponent_SupplyAllocations Comp
        {
            get
            {
                World world = Find.World;
                if (world == null) return null;
                if (cached == null || cached.world != world)
                    cached = world.GetComponent<WorldComponent_SupplyAllocations>();
                return cached;
            }
        }

        private static int TileOf(WorldObject_WD_Outpost outpost) => outpost.Tile.tileId;

        public static SupplyMode GetMode(SupplyResource res, int sourceTile)
        {
            WorldComponent_SupplyAllocations comp = Comp;
            if (comp == null) return SupplyMode.Smart;
            for (int i = 0; i < comp.modes.Count; i++)
            {
                WorldComponent_SupplyAllocations.ModeEntry m = comp.modes[i];
                if (m.resource == (int)res && m.sourceTile == sourceTile)
                    return (SupplyMode)m.mode;
            }
            return SupplyMode.Smart;
        }

        public static SupplyMode GetMode(SupplyResource res, WorldObject_WD_Outpost source) =>
            source == null ? SupplyMode.Smart : GetMode(res, TileOf(source));

        /// <summary>Switching to Manual seeds the links from the source's current flows so the numbers do not jump.</summary>
        public static void SetMode(SupplyResource res, WorldObject_WD_Outpost source, SupplyMode mode)
        {
            WorldComponent_SupplyAllocations comp = Comp;
            if (comp == null || source == null) return;
            int tile = TileOf(source);
            if (GetMode(res, tile) == mode) return;

            if (mode == SupplyMode.Manual)
            {
                var flows = new List<SupplyFlow>(8);
                GetOutgoing(res, source, flows);
                comp.links.RemoveAll(l => l.resource == (int)res && l.sourceTile == tile);
                for (int i = 0; i < flows.Count; i++)
                {
                    float amount = (float)Math.Round(flows[i].amount, 1);
                    if (amount <= 0.0001f) continue;
                    comp.links.Add(new WorldComponent_SupplyAllocations.Link
                    {
                        resource = (int)res,
                        sourceTile = tile,
                        destTile = flows[i].destTile,
                        amount = amount
                    });
                }
                comp.modes.Add(new WorldComponent_SupplyAllocations.ModeEntry { resource = (int)res, sourceTile = tile, mode = (int)SupplyMode.Manual });
            }
            else
            {
                comp.modes.RemoveAll(m => m.resource == (int)res && m.sourceTile == tile);
            }
            MarkDirty(res);
        }

        public static float GetAssigned(SupplyResource res, int sourceTile, int destTile)
        {
            WorldComponent_SupplyAllocations comp = Comp;
            if (comp == null) return 0f;
            for (int i = 0; i < comp.links.Count; i++)
            {
                WorldComponent_SupplyAllocations.Link l = comp.links[i];
                if (l.resource == (int)res && l.sourceTile == sourceTile && l.destTile == destTile)
                    return l.amount;
            }
            return 0f;
        }

        public static float GetAssignedSum(SupplyResource res, int sourceTile)
        {
            WorldComponent_SupplyAllocations comp = Comp;
            if (comp == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < comp.links.Count; i++)
            {
                WorldComponent_SupplyAllocations.Link l = comp.links[i];
                if (l.resource == (int)res && l.sourceTile == sourceTile)
                    sum += l.amount;
            }
            return sum;
        }

        /// <summary>Destination tile and amount of each link leaving <paramref name="sourceTile"/>.</summary>
        public static void GetAssignedLinks(SupplyResource res, int sourceTile, List<KeyValuePair<int, float>> into)
        {
            into.Clear();
            WorldComponent_SupplyAllocations comp = Comp;
            if (comp == null) return;
            for (int i = 0; i < comp.links.Count; i++)
            {
                WorldComponent_SupplyAllocations.Link l = comp.links[i];
                if (l.resource == (int)res && l.sourceTile == sourceTile && l.amount > 0.0001f)
                    into.Add(new KeyValuePair<int, float>(l.destTile, l.amount));
            }
        }

        /// <summary>Sets one link (switching the source to Manual first), clamped to the source's budget.</summary>
        public static void SetAssigned(SupplyResource res, WorldObject_WD_Outpost source, int destTile, float amount)
        {
            WorldComponent_SupplyAllocations comp = Comp;
            if (comp == null || source == null) return;
            int tile = TileOf(source);
            if (GetMode(res, tile) != SupplyMode.Manual)
                SetMode(res, source, SupplyMode.Manual);

            float others = GetAssignedSum(res, tile) - GetAssigned(res, tile, destTile);
            float max = Mathf.Max(0f, GetBudget(res, source) - others);
            float clamped = (float)Math.Round(Mathf.Clamp(amount, 0f, max), 1);

            comp.links.RemoveAll(l => l.resource == (int)res && l.sourceTile == tile && l.destTile == destTile);
            if (clamped > 0.0001f)
                comp.links.Add(new WorldComponent_SupplyAllocations.Link { resource = (int)res, sourceTile = tile, destTile = destTile, amount = clamped });
            MarkDirty(res);
        }

        /// <summary>Switch to Manual and zero every assignment leaving this source.</summary>
        public static void Reset(SupplyResource res, WorldObject_WD_Outpost source)
        {
            WorldComponent_SupplyAllocations comp = Comp;
            if (comp == null || source == null) return;
            int tile = TileOf(source);
            comp.links.RemoveAll(l => l.resource == (int)res && l.sourceTile == tile);
            if (GetMode(res, tile) != SupplyMode.Manual)
                comp.modes.Add(new WorldComponent_SupplyAllocations.ModeEntry { resource = (int)res, sourceTile = tile, mode = (int)SupplyMode.Manual });
            MarkDirty(res);
        }

        /// <summary>What the source can hand out: plant watts, or generator tooling per day.</summary>
        public static float GetBudget(SupplyResource res, WorldObject_WD_Outpost source)
        {
            if (source == null) return 0f;
            switch (res)
            {
                case SupplyResource.Power: return Outpost_PowerPlant.GetRemotePowerWatts(source);
                case SupplyResource.Tooling: return OutpostTooling.GetGeneratorOutput(source);
                default: return 0f;
            }
        }

        public static void GetOutgoing(SupplyResource res, WorldObject_WD_Outpost source, List<SupplyFlow> into)
        {
            into.Clear();
            if (source == null) return;
            if (res == SupplyResource.Power) OutpostPowerGrid.GetOutgoing(source.ID, into);
            else if (res == SupplyResource.Tooling) OutpostTooling.GetOutgoing(source.ID, into);
        }

        public static void GetIncoming(SupplyResource res, WorldObject_WD_Outpost dest, List<SupplyFlow> into)
        {
            into.Clear();
            if (dest == null) return;
            if (res == SupplyResource.Power) OutpostPowerGrid.GetIncoming(dest.ID, into);
            else if (res == SupplyResource.Tooling) OutpostTooling.GetIncoming(dest.ID, into);
        }

        public static void MarkDirty(SupplyResource res)
        {
            if (res == SupplyResource.Power) OutpostPowerGrid.MarkDirty();
            else if (res == SupplyResource.Tooling) OutpostTooling.MarkDirty();
        }
    }
}
