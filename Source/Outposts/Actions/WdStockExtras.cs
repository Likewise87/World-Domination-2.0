using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Instance state that vanilla sealed <see cref="ThingDefCountClass"/> cannot carry.
    /// Payment pools use taint/HP/biocode. Outpost stock may also carry dye (<see cref="hasColor"/>)
    /// and Ideology <see cref="styleDef"/> when Experimental retain-color is on. Runtime attachment
    /// is via <see cref="WdStockExtrasTable"/>; warehouse/armory stock snapshots visuals on save.
    /// </summary>
    public class WdStockExtras
    {
        public bool wornByCorpse;
        /// <summary>-1 = unset (warehouse-style / unspecified).</summary>
        public int hitPoints = -1;
        public bool biocoded;

        public bool hasColor;
        public Color color;
        public ThingStyleDef styleDef;

        public bool HasVisual => hasColor || styleDef != null;
    }

    /// <summary>Index-aligned visual snapshot for save/load of stock lists.</summary>
    public class WdStockVisualRecord : IExposable
    {
        public bool hasColor;
        public Color color;
        public ThingStyleDef styleDef;

        public bool IsEmpty => !hasColor && styleDef == null;

        public void ExposeData()
        {
            Scribe_Values.Look(ref hasColor, "hasColor", false);
            Scribe_Values.Look(ref color, "color", default(Color));
            Scribe_Defs.Look(ref styleDef, "styleDef");
        }

        public static WdStockVisualRecord FromExtras(WdStockExtras extras)
        {
            if (extras == null || !extras.HasVisual) return new WdStockVisualRecord();
            return new WdStockVisualRecord
            {
                hasColor = extras.hasColor,
                color = extras.color,
                styleDef = extras.styleDef
            };
        }

        public void ApplyTo(WdStockExtras extras)
        {
            if (extras == null || IsEmpty) return;
            extras.hasColor = hasColor;
            extras.color = color;
            extras.styleDef = styleDef;
        }
    }

    public static class WdStockExtrasTable
    {
        private static readonly ConditionalWeakTable<ThingDefCountClass, WdStockExtras> Table =
            new ConditionalWeakTable<ThingDefCountClass, WdStockExtras>();

        public static bool TryGet(ThingDefCountClass row, out WdStockExtras extras)
        {
            if (row == null)
            {
                extras = null;
                return false;
            }
            return Table.TryGetValue(row, out extras);
        }

        public static WdStockExtras GetOrCreate(ThingDefCountClass row)
        {
            if (row == null) return null;
            if (Table.TryGetValue(row, out WdStockExtras existing))
                return existing;
            var created = new WdStockExtras();
            Table.Add(row, created);
            return created;
        }

        public static void CopyTo(ThingDefCountClass from, ThingDefCountClass to)
        {
            if (from == null || to == null) return;
            if (!TryGet(from, out WdStockExtras src)) return;
            WdStockExtras dst = GetOrCreate(to);
            dst.wornByCorpse = src.wornByCorpse;
            dst.hitPoints = src.hitPoints;
            dst.biocoded = src.biocoded;
            dst.hasColor = src.hasColor;
            dst.color = src.color;
            dst.styleDef = src.styleDef;
        }

        public static bool GetWornByCorpse(ThingDefCountClass row) =>
            TryGet(row, out WdStockExtras e) && e.wornByCorpse;

        public static bool GetBiocoded(ThingDefCountClass row) =>
            TryGet(row, out WdStockExtras e) && e.biocoded;

        public static int GetHitPoints(ThingDefCountClass row) =>
            TryGet(row, out WdStockExtras e) ? e.hitPoints : -1;

        public static bool HasVisualExtras(ThingDefCountClass row) =>
            TryGet(row, out WdStockExtras e) && e.HasVisual;

        public static bool SameVisualIdentity(ThingDefCountClass a, ThingDefCountClass b)
        {
            TryGet(a, out WdStockExtras ea);
            TryGet(b, out WdStockExtras eb);
            bool aColor = ea != null && ea.hasColor;
            bool bColor = eb != null && eb.hasColor;
            if (aColor != bColor) return false;
            if (aColor && ea.color != eb.color) return false;
            ThingStyleDef sa = ea?.styleDef;
            ThingStyleDef sb = eb?.styleDef;
            return sa == sb;
        }

        /// <summary>
        /// When Experimental retain-color is on, copy active CompColorable dye and StyleDef onto the row.
        /// </summary>
        public static void AttachVisualsFromThing(ThingDefCountClass row, Thing thing)
        {
            if (row == null || thing == null) return;
            if (WorldDominationMod.settings?.experimentalRetainOutpostEquipmentColor == false)
                return;

            bool hasColor = false;
            Color color = default;
            CompColorable colorable = (thing as ThingWithComps)?.GetComp<CompColorable>();
            if (colorable != null && colorable.Active)
            {
                hasColor = true;
                color = colorable.Color;
            }

            ThingStyleDef style = thing.StyleDef;
            if (!hasColor && style == null) return;

            WdStockExtras extras = GetOrCreate(row);
            extras.hasColor = hasColor;
            extras.color = color;
            extras.styleDef = style;
        }

        public static void ApplyVisualsToThing(Thing thing, ThingDefCountClass row)
        {
            if (thing == null || row == null) return;
            if (!TryGet(row, out WdStockExtras extras) || !extras.HasVisual) return;
            ApplyVisualsToThing(thing, extras);
        }

        public static void ApplyVisualsToThing(Thing thing, WdStockExtras extras)
        {
            if (thing == null || extras == null || !extras.HasVisual) return;

            if (extras.hasColor)
                thing.SetColor(extras.color);

            if (extras.styleDef != null)
                thing.StyleDef = extras.styleDef;
        }

        /// <summary>Save-time dump / load-time reattach for a stock list. Pass null snap when not saving.</summary>
        public static void ExposeVisualSnapshot(List<ThingDefCountClass> stock, ref List<WdStockVisualRecord> snap, string label)
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                snap = null;
                if (stock != null && stock.Count > 0)
                {
                    snap = new List<WdStockVisualRecord>(stock.Count);
                    for (int i = 0; i < stock.Count; i++)
                    {
                        ThingDefCountClass row = stock[i];
                        if (TryGet(row, out WdStockExtras extras) && extras.HasVisual)
                            snap.Add(WdStockVisualRecord.FromExtras(extras));
                        else
                            snap.Add(new WdStockVisualRecord());
                    }
                }
            }

            Scribe_Collections.Look(ref snap, label, LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && snap != null && stock != null)
            {
                int n = stock.Count < snap.Count ? stock.Count : snap.Count;
                for (int i = 0; i < n; i++)
                {
                    WdStockVisualRecord rec = snap[i];
                    if (rec == null || rec.IsEmpty) continue;
                    ThingDefCountClass row = stock[i];
                    if (row == null) continue;
                    rec.ApplyTo(GetOrCreate(row));
                }
            }
        }
    }
}
