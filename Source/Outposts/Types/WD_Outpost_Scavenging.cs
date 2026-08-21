using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Scavenging outpost: fixed production interval from def; delivery bundle scales by pawn count and scavenging tier.</summary>
    public static class Outpost_Scavenging
    {
        /// <summary>Three scavenging tiers. Selection persists via <see cref="WorldObject_WD_Outpost.SelectedScavengingKind"/>.</summary>
        public enum ScavengingKind { Basic, Uncommon, Rare }

        public static readonly ScavengingKind[] AllKinds = { ScavengingKind.Basic, ScavengingKind.Uncommon, ScavengingKind.Rare };

        /// <summary>Legacy / fallback constant surfaced in UI: per-pawn market value for the Basic tier. Used only when no kind is selected.</summary>
        public const float MarketValuePerPawn = 400f;

        /// <summary>Minimum colonists required to pick this tier (and thus a delivery at all).</summary>
        public static int GetMinPawns(ScavengingKind kind)
        {
            switch (kind)
            {
                case ScavengingKind.Uncommon: return 8;
                case ScavengingKind.Rare: return 15;
                default: return 2;
            }
        }

        /// <summary>Silver market value added per colonist, per completed cycle, for the given tier.</summary>
        public static float GetMarketValuePerPawn(ScavengingKind kind)
        {
            switch (kind)
            {
                case ScavengingKind.Uncommon: return 300f;
                case ScavengingKind.Rare: return 200f;
                default: return 400f;
            }
        }

        /// <summary>Core reward generator for this tier (resolved by defName with fallback to Reward_ItemsStandard).</summary>
        private static ThingSetMakerDef ResolveThingSetMakerDef(ScavengingKind kind)
        {
            string preferred;
            switch (kind)
            {
                case ScavengingKind.Uncommon: preferred = "Reward_ItemsStandard"; break;
                case ScavengingKind.Rare: preferred = "Reward_ItemsStandard"; break;
                default: preferred = "Reward_ItemsStandard"; break;
            }
            var def = DefDatabase<ThingSetMakerDef>.GetNamedSilentFail(preferred);
            if (def == null) def = ThingSetMakerDefOf.Reward_ItemsStandard;
            return def;
        }

        /// <summary>Applied to <see cref="ThingSetMakerParams.techLevel"/> to bias the reward pool towards better tech for higher tiers.</summary>
        private static TechLevel? GetTechLevelBias(ScavengingKind kind)
        {
            switch (kind)
            {
                case ScavengingKind.Uncommon: return TechLevel.Industrial;
                case ScavengingKind.Rare: return TechLevel.Spacer;
                default: return null;
            }
        }

        /// <summary>Applied to <see cref="ThingSetMakerParams.qualityGenerator"/> to bias quality for higher tiers.</summary>
        private static QualityGenerator? GetQualityGeneratorBias(ScavengingKind kind)
        {
            switch (kind)
            {
                case ScavengingKind.Uncommon: return QualityGenerator.Reward;
                case ScavengingKind.Rare: return QualityGenerator.Reward;
                default: return null;
            }
        }

        public static string GetKindLabel(ScavengingKind kind)
        {
            return kind switch
            {
                ScavengingKind.Uncommon => "TSA_WD_Scavenging_KindLabel_Uncommon".Translate().Resolve(),
                ScavengingKind.Rare => "TSA_WD_Scavenging_KindLabel_Rare".Translate().Resolve(),
                _ => "TSA_WD_Scavenging_KindLabel_Basic".Translate().Resolve()
            };
        }

        public static string GetKindShortLabel(ScavengingKind kind)
        {
            return kind switch
            {
                ScavengingKind.Uncommon => "TSA_WD_Scavenging_KindShort_Uncommon".Translate().Resolve(),
                ScavengingKind.Rare => "TSA_WD_Scavenging_KindShort_Rare".Translate().Resolve(),
                _ => "TSA_WD_Scavenging_KindShort_Basic".Translate().Resolve()
            };
        }

        /// <summary>True if the outpost currently has enough colonists to execute the selected tier.</summary>
        public static bool CanUseKind(WorldObject_WD_Outpost outpost, ScavengingKind kind)
        {
            if (outpost == null) return false;
            return outpost.WorkerPawnCount >= GetMinPawns(kind);
        }

        /// <summary>Target delivery market value from an effective pawn count (e.g. time-weighted average headcount over the cycle).</summary>
        public static float GetTotalDeliveryMarketValue(float effectivePawnCount, ScavengingKind kind)
        {
            return Mathf.Max(0f, effectivePawnCount) * GetMarketValuePerPawn(kind);
        }

        public static float GetTotalDeliveryMarketValue(WorldObject_WD_Outpost outpost, ScavengingKind kind)
        {
            if (outpost == null) return 0f;
            return GetTotalDeliveryMarketValue(outpost.WorkerPawnCount, kind);
        }

        /// <summary>Effective kind for this outpost right now (locked-for-cycle takes priority; falls back to current selection). Null if the player has not selected a tier yet.</summary>
        public static ScavengingKind? GetEffectiveKind(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return null;
            return outpost.GetProducingScavengingKindForCurrentCycle();
        }

        /// <summary>Delivery capacity track for running average (matches pawn count).</summary>
        public static float GetDeliveryDrivingCapacity(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 0f;
            return outpost.WorkerPawnCount;
        }

        public static float GetTotalDeliveryMarketValue(WorldObject_WD_Outpost outpost)
        {
            var k = GetEffectiveKind(outpost);
            return k.HasValue ? GetTotalDeliveryMarketValue(outpost, k.Value) : 0f;
        }

        /// <param name="averagePawnDrivingCapacityThisCycle">Time-weighted average of <see cref="GetDeliveryDrivingCapacity"/> (pawn count) over the cycle, same units as recruiting/trading Social average.</param>
        public static bool Produce(WorldObject_WD_Outpost outpost, float averagePawnDrivingCapacityThisCycle)
        {
            if (outpost == null) return false;

            var maybeKind = GetEffectiveKind(outpost);
            if (!maybeKind.HasValue) return false;
            ScavengingKind kind = maybeKind.Value;
            if (!CanUseKind(outpost, kind)) return false;

            float totalMv = GetTotalDeliveryMarketValue(averagePawnDrivingCapacityThisCycle, kind);
            if (totalMv < 0.01f) return false;

            float minStrength = WorldDominationMod.settings?.outpostDeliveryMinStrength ?? 100f;
            var comp = outpost.GetComponent<CompViralSpread>();
            if (comp != null && comp.strength < minStrength) return false;

            var parms = default(ThingSetMakerParams);
            parms.totalMarketValueRange = new FloatRange(totalMv, totalMv);
            TechLevel? techBias = GetTechLevelBias(kind);
            if (techBias.HasValue) parms.techLevel = techBias.Value;
            QualityGenerator? qualityBias = GetQualityGeneratorBias(kind);
            if (qualityBias.HasValue) parms.qualityGenerator = qualityBias.Value;
            // Belt-and-suspenders: ThingSetMakerUtility already excludes MinifiedThing etc., but keep junk out of our pool.
            parms.validator = IsValidScavengingDeliveryDef;

            var maker = ResolveThingSetMakerDef(kind);
            List<ThingDefCountClass> list = null;
            for (int attempt = 0; attempt < 3 && (list == null || list.Count == 0); attempt++)
            {
                var generated = maker.root.Generate(parms);
                if (generated == null) continue;
                // ThingSetMaker allocates real Thing instances (weapons/apparel/quality items with comps). We only need
                // def + stackCount; the instances themselves have to be Destroy()'d or they leak until GC and bump
                // Thing.IDNumber counters for nothing.
                list = ThingsToDefCountsAndDiscard(generated);
            }
            if (list == null || list.Count == 0) return false;

            Outpost_Production_Utils.ApplyOutputMultiplierToDeliveryItems(list);
            WorldActions_Traveler.SpawnOutpostDeliveryTraveler(outpost, list);
            return true;
        }

        /// <summary>
        /// Rejects abstract/unusable reward defs (root MinifiedThing, blueprints, frames, non-haulable junk, etc.).
        /// Builds on vanilla <see cref="ThingSetMakerUtility.CanGenerate"/> and unwraps minified wrappers before counting.
        /// Also rejects defs whose runtime stuff state is contradictory after mod patches (e.g. FlakVest with Steel
        /// while <see cref="BuildableDef.MadeFromStuff"/> is false) — those trip CostListAdjusted red errors later
        /// when quests scan player-accessible items.
        /// </summary>
        private static bool IsValidScavengingDeliveryDef(ThingDef def)
        {
            if (def == null) return false;
            if (!ThingSetMakerUtility.CanGenerate(def)) return false;
            if (!def.PlayerAcquirable) return false;
            if (def.IsBlueprint || def.IsFrame) return false;
            if (def.destroyOnDrop) return false;
            if (def.thingClass != null && typeof(MinifiedThing).IsAssignableFrom(def.thingClass)) return false;
            // Buildings only if they can ship as a minified crate on arrival.
            if (def.category == ThingCategory.Building && !def.Minifiable) return false;
            if (def.category != ThingCategory.Item && !def.Minifiable) return false;
            if (def.BaseMarketValue <= 0f) return false;
            if (!CanSpawnWithoutStuffContradiction(def)) return false;
            return true;
        }

        /// <summary>Cached probe: ThingMaker leaves Stuff set on a def that is not MadeFromStuff (mod patch conflict).</summary>
        private static readonly Dictionary<ThingDef, bool> StuffSpawnOkCache = new Dictionary<ThingDef, bool>();

        /// <summary>
        /// False when MakeThing produces an instance with Stuff while def.MadeFromStuff is false.
        /// That pairing makes <see cref="CostListCalculator.CostListAdjusted(Verse.Thing)"/> log
        /// "Got AdjustedCostList for X with stuff Y but is not MadeFromStuff" (seen from quest trade-request
        /// accessibility scans of scavenging deliveries).
        /// </summary>
        private static bool CanSpawnWithoutStuffContradiction(ThingDef def)
        {
            if (def == null) return false;
            if (StuffSpawnOkCache.TryGetValue(def, out bool cached)) return cached;

            bool ok = true;
            Thing probe = null;
            try
            {
                if (def.MadeFromStuff)
                {
                    ThingDef stuff = GenStuff.DefaultStuffFor(def)
                        ?? GenStuff.RandomStuffByCommonalityFor(def, TechLevel.Undefined);
                    if (stuff == null)
                        ok = false;
                    else
                        probe = ThingMaker.MakeThing(def, stuff);
                }
                else
                {
                    probe = ThingMaker.MakeThing(def);
                    if (probe != null && probe.Stuff != null)
                    {
                        // Mod conflict: not stuffable in final def DB, but MakeThing/Harmony still assigned Stuff.
                        ok = false;
                        Log.Warning("[TSA WD] Scavenging: excluding " + def.defName
                            + " — MakeThing left Stuff=" + probe.Stuff.defName
                            + " but MadeFromStuff is false (contradictory mod patches).");
                    }
                }
            }
            catch
            {
                ok = false;
            }
            finally
            {
                DiscardGeneratedThing(probe);
            }

            StuffSpawnOkCache[def] = ok;
            return ok;
        }

        /// <summary>Converts generated Things to delivery rows (def/count/stuff/quality) and destroys the temps.
        /// Stuff is resolved from the live def after all patches — never trust a leftover Stuff on a non-stuffable def.</summary>
        private static List<ThingDefCountClass> ThingsToDefCountsAndDiscard(IEnumerable<Thing> things)
        {
            var list = new List<ThingDefCountClass>();
            foreach (Thing t in things)
            {
                if (t == null) continue;

                // If a MinifiedThing wrapper slipped through, keep the inner content def, never the abstract crate.
                Thing inner = t.GetInnerIfMinified() ?? t;
                ThingDef def = inner.def;
                int count = t.stackCount > 0 ? t.stackCount : 1;

                // Instance-level contradiction (ThingSetMaker/Harmony assigned Stuff on a non-stuffable def).
                if (inner.Stuff != null && !def.MadeFromStuff)
                {
                    DiscardGeneratedThing(t);
                    continue;
                }

                if (!IsValidScavengingDeliveryDef(def))
                {
                    DiscardGeneratedThing(t);
                    continue;
                }

                var row = new ThingDefCountClass(def, count)
                {
                    stuff = ResolveStuffForDef(def, inner.Stuff)
                };
                if (inner.TryGetQuality(out QualityCategory q))
                    row.quality = q;
                list.Add(row);

                DiscardGeneratedThing(t);
            }
            return list.Count == 0 ? null : list;
        }

        /// <summary>
        /// Runtime def-database check (after CE/patches/etc.): is this def stuffable, and with what?
        /// Returns null when not MadeFromStuff. Prefers <paramref name="candidateStuff"/> only if it still CanMake the def.
        /// </summary>
        private static ThingDef ResolveStuffForDef(ThingDef def, ThingDef candidateStuff)
        {
            if (def == null || !def.MadeFromStuff) return null;

            if (candidateStuff != null && candidateStuff.IsStuff && candidateStuff.stuffProps != null
                && candidateStuff.stuffProps.CanMake(def))
                return candidateStuff;

            ThingDef stuff = GenStuff.RandomStuffByCommonalityFor(def, TechLevel.Undefined);
            if (stuff == null)
                stuff = GenStuff.DefaultStuffFor(def);
            return stuff;
        }

        private static void DiscardGeneratedThing(Thing t)
        {
            if (t == null || t.Destroyed) return;
            try { t.Destroy(DestroyMode.Vanish); }
            catch { /* Off-map discard; ignore cleanup errors from optional comps. */ }
        }

        public static string GetProductionTooltip(WorldObject_WD_Outpost outpost)
        {
            var maybe = GetEffectiveKind(outpost);
            if (!maybe.HasValue)
                return "TSA_WD_Production_TooltipScavenging_None".Translate().Resolve();
            ScavengingKind kind = maybe.Value;
            int n = outpost?.WorkerPawnCount ?? 0;
            float perPawn = GetMarketValuePerPawn(kind);
            float mv = GetTotalDeliveryMarketValue(outpost, kind);
            int minPawns = GetMinPawns(kind);
            string kindLabel = GetKindLabel(kind);
            return "TSA_WD_Production_TooltipScavenging_Kind".Translate(
                kindLabel,
                perPawn.ToString("F0"),
                n.ToString(),
                mv.ToString("F0"),
                minPawns.ToString()).Resolve();
        }

        public static string GetProductionSummaryLine(WorldObject_WD_Outpost outpost)
        {
            var maybe = GetEffectiveKind(outpost);
            if (!maybe.HasValue) return null;
            ScavengingKind kind = maybe.Value;
            float mv = GetTotalDeliveryMarketValue(outpost, kind);
            string kindLabel = GetKindShortLabel(kind);
            return "TSA_WD_Production_SummaryScavenging_Kind".Translate(kindLabel, mv.ToString("F0")).Resolve();
        }

        /// <summary>Dynamic inspect line including current pawn-based value. Empty when no tier is selected so the inspect pane falls back to "None selected".</summary>
        public static string GetInspectProductLine(WorldObject_WD_Outpost outpost)
        {
            var maybe = GetEffectiveKind(outpost);
            if (!maybe.HasValue) return "";
            ScavengingKind kind = maybe.Value;
            float mv = GetTotalDeliveryMarketValue(outpost, kind);
            string kindLabel = GetKindShortLabel(kind);
            return "TSA_WD_Prod_ScavengingInspect_Kind".Translate(
                kindLabel,
                mv.ToString("F0"),
                (outpost?.WorkerPawnCount ?? 0).ToString(),
                GetMarketValuePerPawn(kind).ToString("F0")).Resolve();
        }

        /// <summary>Yield preview for a specific tier (used by the 3 option rows in the production dialog).</summary>
        public static string GetYieldPreviewLabel(WorldObject_WD_Outpost outpost, ScavengingKind kind)
        {
            float perPawn = GetMarketValuePerPawn(kind);
            float mv = GetTotalDeliveryMarketValue(outpost, kind);
            int n = outpost?.WorkerPawnCount ?? 0;
            return "TSA_WD_Production_ScavengingYieldPreview".Translate(n.ToString(), perPawn.ToString("F0"), mv.ToString("F0")).Resolve();
        }

        /// <summary>Short yield summary for the snapshot/average table cells (needs to fit in a narrow column). Returns a dash when no tier is selected yet.</summary>
        public static string GetYieldSummaryLabel(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "\u2014";
            return GetYieldSummaryLabel(outpost, outpost.WorkerPawnCount);
        }

        /// <summary>Same as <see cref="GetYieldSummaryLabel(WorldObject_WD_Outpost)"/> but with an explicit effective pawn count (e.g. snapshot vs cycle-average headcount).</summary>
        public static string GetYieldSummaryLabel(WorldObject_WD_Outpost outpost, float effectivePawnCount)
        {
            var k = GetEffectiveKind(outpost);
            if (!k.HasValue) return "\u2014";
            ScavengingKind kind = k.Value;
            float mv = GetTotalDeliveryMarketValue(effectivePawnCount, kind);
            string kindShort = GetKindShortLabel(kind);
            return "TSA_WD_Production_ScavengingYieldSummary".Translate(kindShort, mv.ToString("F0")).Resolve();
        }

        public static string GetKindRequirementTooltip(ScavengingKind kind)
        {
            int minPawns = GetMinPawns(kind);
            float perPawn = GetMarketValuePerPawn(kind);
            return "TSA_WD_Scavenging_KindRequirement".Translate(minPawns.ToString(), perPawn.ToString("F0")).Resolve();
        }
    }
}
