using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Manual outpost defense / trench win: bank floor armory items and corpse gear into the
    /// defending outpost via <see cref="OutpostStorageUtility"/> before the temp map is removed.
    /// </summary>
    public static class OutpostDefenseLootUtility
    {
        private static readonly List<Thing> salvageScratch = new List<Thing>(128);
        private static readonly List<Thing> apparelScratch = new List<Thing>(16);
        private static readonly List<Thing> equipmentScratch = new List<Thing>(8);
        private static readonly List<Thing> inventoryScratch = new List<Thing>(32);

        /// <summary>
        /// Salvages loose map armory items and corpse gear into <paramref name="outpost"/>.
        /// Returns how many Things were stored (not refunded).
        /// </summary>
        public static int SalvageMapIntoOutpostArmory(WorldObject_WD_Outpost outpost, Map map)
        {
            if (outpost == null || outpost.Destroyed || map == null) return 0;

            salvageScratch.Clear();
            CollectLooseArmoryItems(map, salvageScratch);
            CollectCorpseArmoryItems(map, salvageScratch);

            int stored = 0;
            for (int i = 0; i < salvageScratch.Count; i++)
            {
                Thing thing = salvageScratch[i];
                if (thing == null || thing.Destroyed) continue;
                if (TryBank(outpost, thing))
                    stored++;
            }

            salvageScratch.Clear();
            return stored;
        }

        private static bool TryBank(WorldObject_WD_Outpost outpost, Thing thing)
        {
            if (thing.Spawned)
                thing.DeSpawn(DestroyMode.Vanish);
            else
                thing.holdingOwner?.Remove(thing);

            if (OutpostStorageUtility.TryStoreThing(outpost, thing))
                return true;

            OutpostStorageUtility.RefundUnstoredThing(thing);
            return false;
        }

        /// <summary>
        /// Spawned armory items whose holder chain reaches the map without a Building, Pawn, or Corpse.
        /// Skips building inventories (turrets, hoppers, CE MG nests).
        /// </summary>
        private static void CollectLooseArmoryItems(Map map, List<Thing> into)
        {
            List<Thing> all = map.listerThings?.AllThings;
            if (all == null) return;

            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t == null || t.Destroyed || !t.Spawned) continue;
                if (t is Corpse || t is Pawn) continue;
                if (!IsArmoryCandidate(t)) continue;
                if (!IsLooseOnMap(t)) continue;
                into.Add(t);
            }
        }

        private static void CollectCorpseArmoryItems(Map map, List<Thing> into)
        {
            List<Thing> all = map.listerThings?.AllThings;
            if (all == null) return;

            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is not Corpse corpse || corpse.Destroyed) continue;
                Pawn inner = corpse.InnerPawn;
                if (inner == null || inner.Destroyed) continue;
                StripPawnArmoryItems(inner, into);
            }
        }

        private static void StripPawnArmoryItems(Pawn pawn, List<Thing> into)
        {
            if (pawn.apparel != null)
            {
                apparelScratch.Clear();
                List<Apparel> worn = pawn.apparel.WornApparel;
                for (int i = 0; i < worn.Count; i++)
                {
                    Apparel ap = worn[i];
                    if (ap != null && !ap.Destroyed && IsArmoryCandidate(ap))
                        apparelScratch.Add(ap);
                }
                for (int i = 0; i < apparelScratch.Count; i++)
                {
                    var ap = (Apparel)apparelScratch[i];
                    if (ap.Destroyed) continue;
                    pawn.apparel.Remove(ap);
                    into.Add(ap);
                }
                apparelScratch.Clear();
            }

            if (pawn.equipment != null)
            {
                equipmentScratch.Clear();
                List<ThingWithComps> eqs = pawn.equipment.AllEquipmentListForReading;
                for (int i = 0; i < eqs.Count; i++)
                {
                    ThingWithComps eq = eqs[i];
                    if (eq != null && !eq.Destroyed && IsArmoryCandidate(eq))
                        equipmentScratch.Add(eq);
                }
                for (int i = 0; i < equipmentScratch.Count; i++)
                {
                    var eq = (ThingWithComps)equipmentScratch[i];
                    if (eq.Destroyed) continue;
                    pawn.equipment.Remove(eq);
                    into.Add(eq);
                }
                equipmentScratch.Clear();
            }

            if (pawn.inventory?.innerContainer != null)
            {
                inventoryScratch.Clear();
                ThingOwner<Thing> inv = pawn.inventory.innerContainer;
                for (int i = 0; i < inv.Count; i++)
                {
                    Thing t = inv[i];
                    if (t != null && !t.Destroyed && IsArmoryCandidate(t))
                        inventoryScratch.Add(t);
                }
                for (int i = 0; i < inventoryScratch.Count; i++)
                {
                    Thing t = inventoryScratch[i];
                    if (t.Destroyed) continue;
                    if (!inv.Contains(t)) continue;
                    Thing taken = inv.Take(t, t.stackCount);
                    if (taken != null && !taken.Destroyed)
                        into.Add(taken);
                }
                inventoryScratch.Clear();
            }
        }

        private static bool IsArmoryCandidate(Thing thing)
        {
            if (thing?.def == null) return false;
            Thing content = thing.GetInnerIfMinified() ?? thing;
            return content?.def != null && OutpostArmoryUtility.IsArmoryItem(content.def);
        }

        private static bool IsLooseOnMap(Thing thing)
        {
            IThingHolder holder = thing.ParentHolder;
            int guard = 0;
            while (holder != null && guard++ < 24)
            {
                if (holder is Map) return true;
                if (holder is Building || holder is Pawn || holder is Corpse)
                    return false;

                if (holder is Thing asThing)
                {
                    holder = asThing.ParentHolder;
                    continue;
                }

                if (holder is ThingOwner owner)
                {
                    holder = owner.Owner;
                    continue;
                }

                // Unknown holder shape: refuse (safer than yanking from a building tracker).
                return false;
            }
            return false;
        }
    }
}
