using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    public static class DebugActions_Warehouse
    {
        [DebugAction("World Domination", "Warehouse: add full stack...",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void AddFullStackToWarehouse()
        {
            Messages.Message(
                "WD debug: click a warehouse, then pick food, stone blocks, steel, or components (one full stack).",
                MessageTypeDefOf.NeutralEvent);

            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    if (target.WorldObject is not WorldObject_WD_Outpost outpost
                        || !Outpost_Production_Utils.IsWarehouseOutpost(outpost.def)
                        || outpost.Faction != Faction.OfPlayer)
                    {
                        Messages.Message("WD debug: select a player warehouse outpost.", MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    var comp = CompOutpostWarehouse.Get(outpost);
                    if (comp == null)
                    {
                        Messages.Message("WD debug: warehouse has no storage component.", MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    OpenThingDefMenu(outpost, comp);
                    return true;
                },
                true,
                null,
                false,
                null,
                null);
        }

        private static void OpenThingDefMenu(WorldObject_WD_Outpost warehouse, CompOutpostWarehouse comp)
        {
            var opts = new List<FloatMenuOption>();
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                if (!IsDebugDepositable(def)) continue;
                ThingDef captured = def;
                opts.Add(new FloatMenuOption(
                    captured.LabelCap,
                    () => DepositFullStack(warehouse, comp, captured),
                    captured));
            }

            if (opts.Count == 0)
            {
                Messages.Message("WD debug: no depositable ThingDefs found.", MessageTypeDefOf.RejectInput);
                return;
            }

            opts.Sort((a, b) => string.CompareOrdinal(a.Label, b.Label));
            DebugActions_FloatMenus.OpenCentered(opts);
        }

        private static bool IsDebugDepositable(ThingDef def)
        {
            if (def == null || def.IsBlueprint || def.IsFrame) return false;
            if (def.category != ThingCategory.Item) return false;
            if (def.stackLimit <= 0) return false;
            if (def.label == null) return false;

            if (Outpost_Warehouse_Delivery.IsNutritionGivingStock(def))
                return true;
            if (def.defName != null && def.defName.StartsWith("Blocks"))
                return true;
            if (def == ThingDefOf.Steel)
                return true;
            if (def == ThingDefOf.ComponentIndustrial || def == ThingDefOf.ComponentSpacer)
                return true;
            return false;
        }

        private static void DepositFullStack(
            WorldObject_WD_Outpost warehouse,
            CompOutpostWarehouse comp,
            ThingDef def)
        {
            int count = def.stackLimit > 0 ? def.stackLimit : 1;
            ThingDef stuff = null;
            if (def.MadeFromStuff)
                stuff = GenStuff.DefaultStuffFor(def);

            var row = new ThingDefCountClass(def, count) { stuff = stuff };
            comp.TryDeposit(new List<ThingDefCountClass> { row });
            Messages.Message(
                $"WD debug: added {count}x {def.LabelCap} to {warehouse.LabelCap}.",
                warehouse,
                MessageTypeDefOf.PositiveEvent);
        }
    }
}
