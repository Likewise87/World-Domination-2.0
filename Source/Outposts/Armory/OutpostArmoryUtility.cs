using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Display buckets for the Overview "Armory" column and the Armory inspect line.</summary>
    public enum ArmoryBucket
    {
        Guns,
        Bows,
        Melee,
        Grenades,
        Armor,
        Medicine,
        Drugs,
        Food,
        Ammo
    }

    /// <summary>
    /// Type filter shared by <see cref="Dialog_OutpostArmory"/> and <see cref="Window_AllPlayerGear"/>.
    /// Headgear / Torso / Legs are coverage filters and overlap with each other and with Armor.
    /// </summary>
    public enum ArmoryTypeFilter : byte
    {
        All = 0,
        Guns,
        Bows,
        Melee,
        Grenades,
        Ammo,
        /// <summary>Helmets and body armor (vanilla ApparelArmor category tree).</summary>
        Armor,
        Headgear,
        Torso,
        Legs,
        Medicine,
        Drugs,
        Food,
        /// <summary>Utility apparel and anything else that fits no other kind.</summary>
        Other
    }

    /// <summary>
    /// Single owner for Armory rules: what may be stored, what must stay a real Thing, how a
    /// deposited item is normalized, and every map-free move between an occupant and the store.
    /// No UI file duplicates any of this.
    /// </summary>
    public static class OutpostArmoryUtility
    {
        private static bool catalogBuilt;
        private static HashSet<ThingDef> armoryDefs;
        private static HashSet<ThingDef> ammoDefs;

        /// <summary>
        /// Items in the store that cannot be rebuilt from def + stuff + quality. Rows carry neither
        /// taint nor biocode, and neither is ever cleared, so such items stay real Things.
        /// </summary>
        public static bool IsIrreplaceable(Thing thing) =>
            IsTainted(thing) || IsBiocoded(thing) || HasUniqueState(thing);

        public static bool IsTainted(Thing thing) => thing is Apparel apparel && apparel.WornByCorpse;

        public static bool IsBiocoded(Thing thing)
        {
            CompBiocodable bio = (thing as ThingWithComps)?.GetComp<CompBiocodable>();
            return bio != null && bio.Biocoded;
        }

        /// <summary>Highlight for tainted apparel labels in the Armory and All Player Gear.</summary>
        public static readonly Color TaintedColor = new Color(1f, 0.85f, 0.25f);

        /// <summary>
        /// Item label without the vanilla taint marker; taint is shown by <see cref="TaintedColor"/>
        /// and <see cref="TaintedTip"/> instead.
        /// </summary>
        public static string DisplayLabel(Thing thing)
        {
            if (thing == null) return "";
            if (!IsTainted(thing)) return thing.LabelCapNoCount;
            return GenLabel.ThingLabel(thing.def, thing.Stuff, 1).CapitalizeFirst();
        }

        /// <summary>Tooltip line for tainted apparel, or null.</summary>
        public static string TaintedTip(Thing thing) =>
            IsTainted(thing) ? "TSA_WD_Armory_TaintedTip".Translate().ToString() : null;

        /// <summary>Store label tag for a kept-as-is Thing ("unique", "biocoded"), or empty when taint is the only reason.</summary>
        public static string StoredThingTag(Thing thing)
        {
            var tags = new List<string>(2);
            if (HasUniqueState(thing)) tags.Add("TSA_WD_Armory_UniqueTag".Translate());
            if (IsBiocoded(thing)) tags.Add("TSA_WD_Armory_BiocodedTag".Translate());
            return string.Join(", ", tags);
        }

        private static bool HasUniqueState(Thing thing)
        {
            if (thing == null) return false;

            if (thing.questTags != null && thing.questTags.Count > 0) return true;

            var tc = thing as ThingWithComps;
            if (tc?.AllComps == null) return false;

            for (int i = 0; i < tc.AllComps.Count; i++)
            {
                ThingComp comp = tc.AllComps[i];
                if (comp == null) continue;

                if (comp is CompBladelinkWeapon blade)
                {
                    if (blade.CodedPawn != null) return true;
                    if (blade.TraitsListForReading != null && blade.TraitsListForReading.Count > 0) return true;
                    continue;
                }

                if (comp is CompArt art && art.Active) return true;
                if (comp is CompGeneratedNames names && !string.IsNullOrEmpty(names.Name)) return true;
            }

            return false;
        }

        /// <summary>
        /// Collapses instance state so an ordinary item merges into a counted row: full hit points.
        /// Deliberately generous; stored gear comes back repaired. Taint and biocode are never
        /// cleared; such items are irreplaceable and do not reach this.
        /// </summary>
        public static void Normalize(Thing thing)
        {
            if (thing == null) return;
            RepairToFull(thing);
        }

        private static void RepairToFull(Thing thing)
        {
            if (thing?.def == null || !thing.def.useHitPoints) return;
            int max = thing.MaxHitPoints;
            if (max > 0) thing.HitPoints = max;
        }

        public static bool IsArmoryItem(ThingDef def)
        {
            if (def == null) return false;
            EnsureCatalog();
            return armoryDefs.Contains(def);
        }

        public static bool IsAmmoDef(ThingDef def)
        {
            if (def == null) return false;
            EnsureCatalog();
            return ammoDefs.Contains(def);
        }

        /// <summary>Built once on first use, never from a draw path (Performance Rule 10).</summary>
        private static void EnsureCatalog()
        {
            if (catalogBuilt) return;
            catalogBuilt = true;
            armoryDefs = new HashSet<ThingDef>();
            ammoDefs = new HashSet<ThingDef>();

            List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef def = all[i];
                if (def == null || def.category != ThingCategory.Item) continue;
                if (CompOutpostWarehouse.IsUnusableMinifiedDef(def)) continue;
                if (!def.PlayerAcquirable) continue;

                bool isAmmo = OutpostCeAmmoCompat.IsCeAmmoDef(def);
                if (isAmmo)
                {
                    ammoDefs.Add(def);
                    armoryDefs.Add(def);
                    continue;
                }

                if (def.IsWeapon || def.IsApparel || def.IsMedicine || def.IsDrug
                    || (def.ingestible != null && def.IsNutritionGivingIngestible))
                {
                    armoryDefs.Add(def);
                }
            }
        }

        public static ArmoryBucket BucketFor(ThingDef def)
        {
            if (def == null) return ArmoryBucket.Guns;
            if (IsAmmoDef(def)) return ArmoryBucket.Ammo;
            if (def.IsApparel) return ArmoryBucket.Armor;
            if (def.IsMedicine) return ArmoryBucket.Medicine;
            if (def.IsDrug) return ArmoryBucket.Drugs;
            if (IsGrenadeWeapon(def)) return ArmoryBucket.Grenades;
            if (IsBowWeapon(def)) return ArmoryBucket.Bows;
            if (def.IsRangedWeapon) return ArmoryBucket.Guns;
            if (def.IsMeleeWeapon) return ArmoryBucket.Melee;
            if (def.ingestible != null && def.IsNutritionGivingIngestible) return ArmoryBucket.Food;
            return ArmoryBucket.Guns;
        }

        /// <summary>Filter entries in dropdown order (after All).</summary>
        public static readonly ArmoryTypeFilter[] TypeFilterOrder =
        {
            ArmoryTypeFilter.Guns,
            ArmoryTypeFilter.Bows,
            ArmoryTypeFilter.Melee,
            ArmoryTypeFilter.Grenades,
            ArmoryTypeFilter.Ammo,
            ArmoryTypeFilter.Armor,
            ArmoryTypeFilter.Headgear,
            ArmoryTypeFilter.Torso,
            ArmoryTypeFilter.Legs,
            ArmoryTypeFilter.Medicine,
            ArmoryTypeFilter.Drugs,
            ArmoryTypeFilter.Food,
            ArmoryTypeFilter.Other
        };

        private static ThingCategoryDef apparelArmorCategory;
        private static bool apparelArmorCategoryResolved;

        /// <summary>Helmets and body armor: anything in the ApparelArmor category tree (includes ArmorHeadgear).</summary>
        public static bool IsArmorApparel(ThingDef def)
        {
            if (def == null || !def.IsApparel) return false;
            if (!apparelArmorCategoryResolved)
            {
                apparelArmorCategoryResolved = true;
                apparelArmorCategory = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("ApparelArmor");
            }
            return apparelArmorCategory != null && def.IsWithinCategory(apparelArmorCategory);
        }

        public static bool CoversHead(ThingDef def)
        {
            List<BodyPartGroupDef> groups = def?.apparel?.bodyPartGroups;
            if (groups == null) return false;
            return groups.Contains(BodyPartGroupDefOf.UpperHead)
                || groups.Contains(BodyPartGroupDefOf.FullHead)
                || groups.Contains(BodyPartGroupDefOf.Eyes);
        }

        public static bool CoversTorso(ThingDef def)
        {
            List<BodyPartGroupDef> groups = def?.apparel?.bodyPartGroups;
            return groups != null && groups.Contains(BodyPartGroupDefOf.Torso);
        }

        public static bool CoversLegs(ThingDef def)
        {
            List<BodyPartGroupDef> groups = def?.apparel?.bodyPartGroups;
            return groups != null && groups.Contains(BodyPartGroupDefOf.Legs);
        }

        /// <summary>Single display kind for the Type column. Coverage filters may match more defs.</summary>
        public static ArmoryTypeFilter TypeFilterFor(ThingDef def)
        {
            if (def == null) return ArmoryTypeFilter.Other;
            if (IsAmmoDef(def)) return ArmoryTypeFilter.Ammo;
            if (def.IsApparel)
            {
                if (IsArmorApparel(def)) return ArmoryTypeFilter.Armor;
                if (CoversHead(def)) return ArmoryTypeFilter.Headgear;
                if (CoversTorso(def)) return ArmoryTypeFilter.Torso;
                if (CoversLegs(def)) return ArmoryTypeFilter.Legs;
                return ArmoryTypeFilter.Other;
            }
            if (def.IsMedicine) return ArmoryTypeFilter.Medicine;
            if (def.IsDrug) return ArmoryTypeFilter.Drugs;
            if (IsGrenadeWeapon(def)) return ArmoryTypeFilter.Grenades;
            if (IsBowWeapon(def)) return ArmoryTypeFilter.Bows;
            if (def.IsRangedWeapon) return ArmoryTypeFilter.Guns;
            if (def.IsMeleeWeapon) return ArmoryTypeFilter.Melee;
            if (def.ingestible != null && def.IsNutritionGivingIngestible) return ArmoryTypeFilter.Food;
            return ArmoryTypeFilter.Other;
        }

        public static bool MatchesTypeFilter(ThingDef def, ArmoryTypeFilter filter)
        {
            switch (filter)
            {
                case ArmoryTypeFilter.All:      return true;
                case ArmoryTypeFilter.Headgear: return def != null && def.IsApparel && CoversHead(def);
                case ArmoryTypeFilter.Torso:    return def != null && def.IsApparel && CoversTorso(def);
                case ArmoryTypeFilter.Legs:     return def != null && def.IsApparel && CoversLegs(def);
                default:                        return TypeFilterFor(def) == filter;
            }
        }

        public static bool IsGrenadeWeapon(ThingDef def)
        {
            if (def == null || !def.IsRangedWeapon) return false;
            return def.weaponTags != null && def.weaponTags.Contains("GrenadeDestructive");
        }

        /// <summary>
        /// Bows and crossbows: tag contains "Bow", or the primary projectile defName contains
        /// Arrow / Bolt. Grenades are excluded first.
        /// </summary>
        public static bool IsBowWeapon(ThingDef def)
        {
            if (def == null || !def.IsRangedWeapon) return false;
            if (IsGrenadeWeapon(def)) return false;

            if (def.weaponTags != null)
            {
                for (int i = 0; i < def.weaponTags.Count; i++)
                {
                    string tag = def.weaponTags[i];
                    if (string.IsNullOrEmpty(tag)) continue;
                    if (tag.IndexOf("Bow", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }

            List<VerbProperties> verbs = def.Verbs;
            if (verbs == null) return false;
            for (int i = 0; i < verbs.Count; i++)
            {
                ThingDef projectile = verbs[i]?.defaultProjectile;
                if (projectile == null) continue;
                string name = projectile.defName ?? "";
                if (name.IndexOf("Arrow", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (name.IndexOf("Bolt", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        public static string TypeFilterLabel(ArmoryTypeFilter filter)
        {
            string label;
            switch (filter)
            {
                case ArmoryTypeFilter.All:      label = "TSA_WD_Armory_WeaponFilter_All".Translate(); break;
                case ArmoryTypeFilter.Guns:     label = "TSA_WD_Armory_WeaponFilter_Guns".Translate(); break;
                case ArmoryTypeFilter.Bows:     label = "TSA_WD_Armory_WeaponFilter_Bows".Translate(); break;
                case ArmoryTypeFilter.Melee:    label = "TSA_WD_Armory_WeaponFilter_Melee".Translate(); break;
                case ArmoryTypeFilter.Grenades: label = "TSA_WD_Armory_WeaponFilter_Grenades".Translate(); break;
                case ArmoryTypeFilter.Ammo:     label = "TSA_WD_Armory_TypeFilter_Ammo".Translate(); break;
                case ArmoryTypeFilter.Armor:    label = "TSA_WD_Armory_TypeFilter_Armor".Translate(); break;
                case ArmoryTypeFilter.Headgear: label = "TSA_WD_Armory_TypeFilter_Headgear".Translate(); break;
                case ArmoryTypeFilter.Torso:    label = "TSA_WD_Armory_TypeFilter_Torso".Translate(); break;
                case ArmoryTypeFilter.Legs:     label = "TSA_WD_Armory_TypeFilter_Legs".Translate(); break;
                case ArmoryTypeFilter.Medicine: label = "TSA_WD_Armory_TypeFilter_Medicine".Translate(); break;
                case ArmoryTypeFilter.Drugs:    label = "TSA_WD_Armory_TypeFilter_Drugs".Translate(); break;
                case ArmoryTypeFilter.Food:     label = "TSA_WD_Armory_TypeFilter_Food".Translate(); break;
                case ArmoryTypeFilter.Other:    label = "TSA_WD_Armory_WeaponFilter_Other".Translate(); break;
                default:                        label = filter.ToString(); break;
            }
            return label.CapitalizeFirst();
        }

        /// <summary>
        /// Dropdown choices with "n/total" counts. Coverage filters overlap, so their counts can sum
        /// past the total. <paramref name="defs"/> is one entry per row.
        /// </summary>
        public static List<HeaderFilterChoice> BuildTypeFilterChoices(
            List<ThingDef> defs,
            ArmoryTypeFilter current,
            Action<ArmoryTypeFilter> select)
        {
            int total = defs?.Count ?? 0;
            string CountOf(int n) => total > 0 ? n + "/" + total : null;

            var list = new List<HeaderFilterChoice>
            {
                new HeaderFilterChoice(
                    TypeFilterLabel(ArmoryTypeFilter.All),
                    current == ArmoryTypeFilter.All,
                    () => select(ArmoryTypeFilter.All),
                    separatorAfter: true,
                    countLabel: CountOf(total))
            };

            for (int f = 0; f < TypeFilterOrder.Length; f++)
            {
                ArmoryTypeFilter filter = TypeFilterOrder[f];
                int n = 0;
                for (int i = 0; i < total; i++)
                    if (MatchesTypeFilter(defs[i], filter)) n++;
                list.Add(new HeaderFilterChoice(
                    TypeFilterLabel(filter),
                    current == filter,
                    () => select(filter),
                    countLabel: CountOf(n)));
            }
            return list;
        }

        /// <summary>
        /// Opt-out experimental gate. When off, intake falls back to destroying arriving gear and
        /// the UI entry points hide, but anything already stored stays scribed.
        /// </summary>
        public static bool FeatureEnabled =>
            WorldDominationMod.settings?.experimentalOutpostArmory
            ?? WorldDominationSettings.DefExperimentalOutpostArmory;

        /// <summary>Mutations are invalid while occupants are spawned on a temp battlefield map.</summary>
        public static bool CanMutate(WorldObject_WD_Outpost outpost) =>
            FeatureEnabled && outpost != null && !outpost.Destroyed && !outpost.ManualDefenseActive;

        /// <summary>
        /// Stores a real Thing, routing it to the abstract stock or the uniques owner. The caller
        /// still owns <paramref name="thing"/> on a false return; on true the Thing has either been
        /// consumed into the owner or must be destroyed by the caller.
        /// </summary>
        public static bool TryDeposit(WorldObject_WD_Outpost outpost, Thing thing, out bool consumedThing)
        {
            consumedThing = false;
            if (!FeatureEnabled) return false;
            if (outpost == null || thing == null || thing.Destroyed) return false;

            var armory = CompOutpostArmory.Get(outpost);
            if (armory == null) return false;

            Thing content = thing.GetInnerIfMinified() ?? thing;
            if (content?.def == null || content.Destroyed) return false;
            if (!IsArmoryItem(content.def)) return false;

            try
            {
                if (IsIrreplaceable(content))
                {
                    RepairToFull(content);
                    if (!armory.Uniques.TryAddOrTransfer(content, canMergeWithExistingStacks: false))
                        return false;
                    consumedThing = true;
                    return true;
                }

                // CE magazines are unloaded first so the weapon itself stays an ordinary stacking row.
                OutpostCeAmmoCompat.TryUnloadMagazineInto(content, row => armory.DepositStockRow(row));

                Normalize(content);
                int count = content.stackCount > 0 ? content.stackCount : 1;
                var row = new ThingDefCountClass(content.def, count)
                {
                    stuff = CompOutpostWarehouse.ResolveStuffForDeposit(content.def, content.Stuff)
                };
                if (content.TryGetQuality(out QualityCategory q))
                    row.quality = q;

                armory.DepositStockRow(row);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[WD] Armory deposit soft-fail for {content.def?.defName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>Convenience wrapper that destroys an ordinary Thing once its row is banked.</summary>
        public static bool TryDepositAndDispose(WorldObject_WD_Outpost outpost, Thing thing)
        {
            if (!TryDeposit(outpost, thing, out bool consumed)) return false;
            if (!consumed && thing != null && !thing.Destroyed)
                thing.Destroy(DestroyMode.Vanish);
            return true;
        }

        /// <summary>Materializes one stock row back into a real Thing. Null when the def cannot spawn.</summary>
        public static Thing MakeFromStockRow(ThingDefCountClass row, int count)
        {
            if (row?.thingDef == null || count <= 0) return null;
            return WorldActions_Traveler.MakeDeliveryThing(row.thingDef, count, row.stuff, row.quality);
        }

        /// <summary>
        /// Removes a Thing from whichever pawn tracker holds it. Caller must re-home or destroy
        /// the returned Thing immediately (map-free: never TryDrop).
        /// </summary>
        public static Thing TryTakeFromPawn(Pawn pawn, Thing thing, int count, out string failReason)
        {
            failReason = null;
            if (pawn == null || thing == null || thing.Destroyed) return null;

            if (thing is Apparel ap && pawn.apparel != null && pawn.apparel.IsLocked(ap))
            {
                failReason = "TSA_WD_Armory_FailLocked".Translate(ap.LabelCap);
                return null;
            }

            return DetachFromPawn(pawn, thing, count);
        }

        /// <summary>
        /// Moves one equipped/worn/carried item from a pawn into the store. Map-free: never calls
        /// TryDrop or MakeRoomFor, and never leaves a Thing without a holder.
        /// </summary>
        public static bool TryStoreFromPawn(WorldObject_WD_Outpost outpost, Pawn pawn, Thing thing, int count, out string failReason)
        {
            failReason = null;
            if (!CanMutate(outpost)) { failReason = "TSA_WD_Armory_FailManualDefense".Translate(); return false; }
            if (pawn == null || thing == null || thing.Destroyed) return false;

            var armory = CompOutpostArmory.Get(outpost);
            if (armory == null) return false;

            Thing detached = TryTakeFromPawn(pawn, thing, count, out failReason);
            if (detached == null) return false;

            if (TryDeposit(outpost, detached, out bool consumed))
            {
                if (!consumed && !detached.Destroyed)
                    detached.Destroy(DestroyMode.Vanish);
                return true;
            }

            // Rejected by category: put it back rather than losing it.
            if (!TryGiveBackToPawn(pawn, detached))
                armory.Uniques.TryAddOrTransfer(detached, canMergeWithExistingStacks: false);
            failReason = "TSA_WD_Armory_FailNotStorable".Translate(detached.LabelCap);
            return false;
        }

        /// <summary>Removes a Thing from whichever pawn tracker holds it, re-homing it immediately.</summary>
        private static Thing DetachFromPawn(Pawn pawn, Thing thing, int count)
        {
            // TryDropEquipment routes through pawn.MapHeld, which is null for outpost occupants.
            // Remove is a bare detach, so the caller must re-home or destroy the Thing immediately.
            if (thing is ThingWithComps eq && pawn.equipment != null && pawn.equipment.Contains(eq))
            {
                // Stackable primaries (grenades): a partial move leaves the rest equipped.
                if (count > 0 && count < eq.stackCount)
                    return eq.SplitOff(count);
                pawn.equipment.Remove(eq);
                return eq;
            }

            if (thing is Apparel apparel && pawn.apparel != null && pawn.apparel.WornApparel.Contains(apparel))
            {
                pawn.apparel.Remove(apparel);
                return apparel;
            }

            if (pawn.inventory?.innerContainer != null && pawn.inventory.innerContainer.Contains(thing))
            {
                int take = count > 0 && count < thing.stackCount ? count : thing.stackCount;
                return pawn.inventory.innerContainer.Take(thing, take);
            }

            return null;
        }

        private static bool TryGiveBackToPawn(Pawn pawn, Thing thing)
        {
            if (pawn?.inventory?.innerContainer == null || thing == null) return false;
            return pawn.inventory.innerContainer.TryAdd(thing, canMergeWithExistingStacks: true);
        }

        /// <summary>Equips or stows a stock row on a pawn, materializing the item first.</summary>
        public static bool TryGiveStockToPawn(WorldObject_WD_Outpost outpost, Pawn pawn, ThingDefCountClass row, int count, out string failReason)
        {
            failReason = null;
            if (!CanMutate(outpost)) { failReason = "TSA_WD_Armory_FailManualDefense".Translate(); return false; }
            if (pawn == null || row?.thingDef == null || count <= 0) return false;

            var armory = CompOutpostArmory.Get(outpost);
            if (armory == null) return false;

            count = ClampAssignCount(pawn, row.thingDef, count);
            if (count <= 0) { failReason = NoCapacityReason(pawn, row.thingDef); return false; }

            int taken = armory.WithdrawUpToMatching(row, count);
            if (taken <= 0) return false;

            Thing made = MakeFromStockRow(row, taken);
            if (made == null)
            {
                armory.DepositStockRow(new ThingDefCountClass(row.thingDef, taken) { stuff = row.stuff, quality = row.quality });
                return false;
            }

            if (TryGiveThingToPawn(outpost, pawn, made, out failReason)) return true;

            // Could not be worn or carried: bank it again so nothing is lost.
            TryDepositAndDispose(outpost, made);
            return false;
        }

        /// <summary>
        /// Equips an already-detached unique Thing. The caller must have taken it out of the store
        /// first; on failure it is banked again so the item is never orphaned.
        /// </summary>
        public static bool TryGiveUniqueToPawn(WorldObject_WD_Outpost outpost, Pawn pawn, Thing thing, out string failReason)
        {
            failReason = null;
            if (thing == null) return false;
            if (!CanMutate(outpost))
            {
                failReason = "TSA_WD_Armory_FailManualDefense".Translate();
                Rebank(outpost, thing);
                return false;
            }

            if (TryGiveThingToPawn(outpost, pawn, thing, out failReason)) return true;
            Rebank(outpost, thing);
            return false;
        }

        private static void Rebank(WorldObject_WD_Outpost outpost, Thing thing)
        {
            if (thing == null || thing.Destroyed) return;
            var armory = CompOutpostArmory.Get(outpost);
            if (armory == null)
            {
                thing.Destroy(DestroyMode.Vanish);
                return;
            }
            if (!armory.Uniques.TryAddOrTransfer(thing, canMergeWithExistingStacks: false))
                thing.Destroy(DestroyMode.Vanish);
        }

        /// <summary>
        /// Ammo and grenades always offer a count picker when more than one can be moved.
        /// CE grenades are often stackable ammo-like throwables.
        /// </summary>
        public static bool NeedsAssignCountPrompt(ThingDef def)
        {
            if (def == null) return false;
            if (IsAmmoDef(def)) return true;
            if (IsGrenadeWeapon(def)) return true;
            return false;
        }

        /// <summary>
        /// Where a given item lands on <paramref name="pawn"/>: worn, the primary slot, or inventory.
        /// A second weapon goes to inventory instead of being refused, and CE grenades always do.
        /// </summary>
        public static bool GoesToInventory(Pawn pawn, ThingDef def)
        {
            if (def == null) return true;
            if (def.IsApparel) return false;
            if (def.equipmentType != EquipmentType.Primary) return true;
            if (OutpostCeAmmoCompat.IsCeActive && IsGrenadeWeapon(def)) return true;
            return pawn?.equipment?.Primary != null;
        }

        /// <summary>
        /// CE limits what fits in inventory by bulk and weight. Worn apparel and the primary slot are
        /// not inventory, so only inventory-bound items are clamped.
        /// </summary>
        public static int ClampAssignCount(Pawn pawn, ThingDef def, int want)
        {
            if (want <= 0) return 0;
            if (!OutpostCeAmmoCompat.IsCeActive) return want;
            if (!GoesToInventory(pawn, def)) return want;
            return OutpostCeAmmoCompat.ClampToInventoryFit(pawn, def, want);
        }

        /// <summary>Why <paramref name="pawn"/> cannot carry more of <paramref name="def"/>, with free CE bulk and weight when known.</summary>
        public static string NoCapacityReason(Pawn pawn, ThingDef def)
        {
            if (pawn != null && def != null
                && OutpostCeAmmoCompat.IsCeActive
                && OutpostCeAmmoCompat.TryGetFreeCapacity(pawn, out float bulk, out float weight))
            {
                return "TSA_WD_Armory_FailNoCapacityCe".Translate(
                    pawn.LabelShortCap,
                    def.label,
                    Mathf.Max(0f, bulk).ToString("0.##"),
                    Mathf.Max(0f, weight).ToString("0.##"));
            }
            return "TSA_WD_Armory_FailNoCapacity".Translate();
        }

        private static bool TryGiveThingToPawn(WorldObject_WD_Outpost outpost, Pawn pawn, Thing thing, out string failReason)
        {
            failReason = null;

            if (thing is Apparel apparel)
            {
                if (pawn.apparel == null) return false;
                if (!ApparelUtility.HasPartsToWear(pawn, apparel.def))
                {
                    failReason = "TSA_WD_Armory_FailNoBodyParts".Translate(apparel.LabelCap, pawn.LabelShortCap);
                    return false;
                }
                if (!apparel.PawnCanWear(pawn))
                {
                    failReason = "TSA_WD_Armory_FailCannotWear".Translate(apparel.LabelCap, pawn.LabelShortCap);
                    return false;
                }
                // Biocoded to someone else, gender, ideology role and similar vanilla wear gates.
                if (!EquipmentUtility.CanEquip(apparel, pawn, out string cantWear))
                {
                    failReason = cantWear;
                    return false;
                }

                // Wear(dropReplaced: true) needs a map, and dropReplaced: false bare-removes the
                // conflicting piece, orphaning it. So conflicts go into the store before wearing.
                List<Apparel> conflicts = FindConflictingApparel(pawn, apparel.def);
                for (int i = 0; i < conflicts.Count; i++)
                {
                    if (pawn.apparel.IsLocked(conflicts[i]))
                    {
                        failReason = "TSA_WD_Armory_FailLocked".Translate(conflicts[i].LabelCap);
                        return false;
                    }
                }
                for (int i = 0; i < conflicts.Count; i++)
                {
                    Apparel old = conflicts[i];
                    pawn.apparel.Remove(old);
                    if (!TryDepositAndDispose(outpost, old))
                        Rebank(outpost, old);
                }

                pawn.apparel.Wear(apparel, dropReplacedApparel: false, locked: false);
                return true;
            }

            if (!GoesToInventory(pawn, thing.def) && thing is ThingWithComps eq)
            {
                if (!CanReceivePrimaryWeapon(pawn, eq, out failReason))
                    return false;
                pawn.equipment.AddEquipment(eq);
                return true;
            }

            if (pawn.inventory?.innerContainer == null) return false;
            if (OutpostCeAmmoCompat.IsCeActive
                && OutpostCeAmmoCompat.ClampToInventoryFit(pawn, thing.def, thing.stackCount) < thing.stackCount)
            {
                failReason = NoCapacityReason(pawn, thing.def);
                return false;
            }
            if (!pawn.inventory.innerContainer.TryAdd(thing, canMergeWithExistingStacks: true))
            {
                failReason = NoCapacityReason(pawn, thing.def);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Whether <paramref name="pawn"/> can take <paramref name="thing"/> without destroying or
        /// orphaning it. Used to cancel pawn-to-pawn drags before the source is stripped.
        /// </summary>
        public static bool CanReceiveThing(Pawn pawn, Thing thing, out string failReason)
        {
            failReason = null;
            if (pawn == null || thing?.def == null) return false;

            if (thing is Apparel apparel)
            {
                if (pawn.apparel == null) return false;
                if (!ApparelUtility.HasPartsToWear(pawn, apparel.def))
                {
                    failReason = "TSA_WD_Armory_FailNoBodyParts".Translate(apparel.LabelCap, pawn.LabelShortCap);
                    return false;
                }
                if (!apparel.PawnCanWear(pawn))
                {
                    failReason = "TSA_WD_Armory_FailCannotWear".Translate(apparel.LabelCap, pawn.LabelShortCap);
                    return false;
                }
                if (!EquipmentUtility.CanEquip(apparel, pawn, out string cantWear))
                {
                    failReason = cantWear;
                    return false;
                }
                return true;
            }

            if (!GoesToInventory(pawn, thing.def) && thing is ThingWithComps eq)
                return CanReceivePrimaryWeapon(pawn, eq, out failReason);

            if (pawn.inventory?.innerContainer == null || ClampAssignCount(pawn, thing.def, 1) <= 0)
            {
                failReason = NoCapacityReason(pawn, thing.def);
                return false;
            }
            return true;
        }

        private static bool CanReceivePrimaryWeapon(Pawn pawn, ThingWithComps eq, out string failReason)
        {
            failReason = null;
            if (pawn?.equipment == null) return false;
            if (pawn.equipment.Primary != null)
            {
                failReason = "TSA_WD_Armory_FailPrimaryOccupied".Translate(
                    pawn.LabelShortCap,
                    pawn.equipment.Primary.LabelCapNoCount);
                return false;
            }
            if (!EquipmentUtility.CanEquip(eq, pawn, out string cantReason))
            {
                failReason = cantReason;
                return false;
            }
            return true;
        }

        /// <summary>Apparel occupying a layer the incoming piece needs, so it can be stored first.</summary>
        public static List<Apparel> FindConflictingApparel(Pawn pawn, ThingDef incoming)
        {
            var result = new List<Apparel>();
            if (pawn?.apparel == null || incoming == null) return result;
            List<Apparel> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                Apparel a = worn[i];
                if (a == null) continue;
                if (!ApparelUtility.CanWearTogether(incoming, a.def, pawn.RaceProps.body))
                    result.Add(a);
            }
            return result;
        }

        /// <summary>Per-bucket totals across both halves of the store.</summary>
        public static Dictionary<ArmoryBucket, int> GetStoresSummary(WorldObject_WD_Outpost outpost)
        {
            var result = new Dictionary<ArmoryBucket, int>();
            var armory = CompOutpostArmory.Get(outpost);
            if (armory == null) return result;

            List<ThingDefCountClass> stock = armory.ArmoryRows();
            for (int i = 0; i < stock.Count; i++)
            {
                var e = stock[i];
                ArmoryBucket b = BucketFor(e.thingDef);
                result.TryGetValue(b, out int cur);
                result[b] = cur + e.count;
            }

            ThingOwner<Thing> uniques = armory.Uniques;
            if (uniques != null)
            {
                for (int i = 0; i < uniques.Count; i++)
                {
                    Thing t = uniques[i];
                    if (t?.def == null) continue;
                    ArmoryBucket b = BucketFor(t.def);
                    result.TryGetValue(b, out int cur);
                    result[b] = cur + (t.stackCount > 0 ? t.stackCount : 1);
                }
            }

            return result;
        }

        public static string BucketLabel(ArmoryBucket bucket)
        {
            switch (bucket)
            {
                case ArmoryBucket.Guns:     return "TSA_WD_Armory_Bucket_Guns".Translate();
                case ArmoryBucket.Bows:     return "TSA_WD_Armory_Bucket_Bows".Translate();
                case ArmoryBucket.Melee:    return "TSA_WD_Armory_Bucket_Melee".Translate();
                case ArmoryBucket.Grenades: return "TSA_WD_Armory_Bucket_Grenades".Translate();
                case ArmoryBucket.Armor:    return "TSA_WD_Armory_Bucket_Armor".Translate();
                case ArmoryBucket.Medicine: return "TSA_WD_Armory_Bucket_Medicine".Translate();
                case ArmoryBucket.Drugs:    return "TSA_WD_Armory_Bucket_Drugs".Translate();
                case ArmoryBucket.Food:     return "TSA_WD_Armory_Bucket_Food".Translate();
                case ArmoryBucket.Ammo:     return "TSA_WD_Armory_Bucket_Ammo".Translate();
                default:                    return bucket.ToString();
            }
        }

        /// <summary>
        /// Overview column text: the two largest buckets inline, the full breakdown in the tooltip.
        /// </summary>
        public static void FormatStoresSummary(WorldObject_WD_Outpost outpost, out string label, out string tooltip, out int total)
        {
            label = null;
            tooltip = null;
            total = 0;

            Dictionary<ArmoryBucket, int> summary = GetStoresSummary(outpost);
            if (summary.Count == 0) return;

            var ordered = new List<KeyValuePair<ArmoryBucket, int>>(summary);
            ordered.Sort((a, b) => b.Value.CompareTo(a.Value));

            var inline = new List<string>();
            var full = new List<string>();
            for (int i = 0; i < ordered.Count; i++)
            {
                total += ordered[i].Value;
                string part = ordered[i].Value.ToString() + " " + BucketLabel(ordered[i].Key);
                full.Add(part);
                if (i < 2) inline.Add(part);
            }
            if (total <= 0) return;

            label = string.Join(", ", inline);
            if (ordered.Count > 2)
                label += " " + "TSA_WD_Armory_StoresMore".Translate((ordered.Count - 2).ToString());
            tooltip = "TSA_WD_Armory_StoresTooltip".Translate(total.ToString()) + "\n\n" + string.Join("\n", full);
        }
    }
}
