using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Mortar outpost: gizmos (manual strike, Configure Artillery) and shared fire helpers.
    /// Manual strikes use the same distance-band hit chance as auto-fire and can target hostile settlements, caravans, or hostile WD travelers in range.
    /// Auto mortar / AA settings live in <see cref="Dialog_OutpostArtilleryConfigure"/>.
    /// Impact explosion overlay is triggered from <see cref="WorldActions_Traveler.ExecuteMortarStrike"/> via <see cref="MortarWorldFx"/> (drawn in <see cref="WorldComponent_SpreadManager.WorldComponentOnGUI"/>).
    /// </summary>
    [StaticConstructorOnStartup]
    public static class WD_Outpost_Mortar
    {
        private static Texture2D cachedMortarIcon;
        private static Texture2D cachedConfigureIcon;

        /// <summary>Per-outpost cache for the Launch gizmo's ready-state strings; refreshed every
        /// <see cref="FireGizmoCacheLifetimeTicks"/> ticks (no sub-second staleness that matters). RimWorld re-queries
        /// gizmos every GUI frame while the outpost is selected, so without this we re-run skill aggregation +
        /// Translate allocations ~60×/s.</summary>
        private sealed class FireGizmoCache
        {
            public int lastRefreshTick = -99999;
            public bool onCooldown;
            public float cooldownDaysLeft;
            public bool hasShootingSkill;
            public string desc;
        }

        private static readonly Dictionary<int, FireGizmoCache> fireGizmoCacheByOutpostId = new Dictionary<int, FireGizmoCache>();
        private const int FireGizmoCacheLifetimeTicks = 60;

        public static IEnumerable<Gizmo> GetGizmos(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) yield break;
            if (outpost.Faction != Faction.OfPlayer) yield break;
            if (!outpost.IsMortarOutpost) yield break;

            var comp = outpost.GetComponent<CompViralSpread>();
            FireGizmoCache cache = GetOrRefreshFireGizmoCache(outpost, comp);

            Command_Action fire = new Command_Action
            {
                defaultLabel = "TSA_WD_Mortar_LaunchLabel".Translate(),
                defaultDesc = cache.desc,
                icon = cachedMortarIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/ShootMortar", false) ?? TexCommand.Attack,
                action = () => StartMortarTargeting(outpost)
            };
            if (cache.onCooldown)
                fire.Disable("TSA_WD_Mortar_ReasonCooldown".Translate());
            else if (!cache.hasShootingSkill)
                fire.Disable("TSA_WD_Mortar_ReasonNoSkill".Translate());
            yield return fire;

            yield return new Command_Action
            {
                defaultLabel = "TSA_WD_Artillery_ConfigureLabel".Translate(),
                defaultDesc = AntiAirFireUtils.HasAntiAirUpgrade(outpost)
                    ? "TSA_WD_Artillery_ConfigureDescWithAA".Translate()
                    : "TSA_WD_Artillery_ConfigureDesc".Translate(),
                icon = cachedConfigureIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/MortarRadius", false) ?? TexCommand.Attack,
                action = () => Dialog_OutpostArtilleryConfigure.Open(outpost),
                onHover = () =>
                {
                    // Legacy hop rings only; fill mode uses dedicated Mortar/AA hover gizmos.
                    if (!WD_RadiusOverlayMode.UseHopRadiusRings || outpost == null || outpost.Destroyed) return;
                    PlanetLayer layer = PlanetSurfaceWorldActions.LayerOf(outpost);
                    PlanetTile tile = new PlanetTile(outpost.Tile, layer);
                    float mortarRange = MortarFireUtils.GetPlayerMortarMaxRangeTiles(outpost);
                    WorldMapRadiusVisual.DrawApproxRadiusRing(tile, mortarRange, WorldOverlayLineMaterials.RadiusRed);
                    if (AntiAirFireUtils.HasAntiAirUpgrade(outpost))
                    {
                        float aaRange = AntiAirFireUtils.GetPlayerAntiAirMaxRangeTiles(outpost);
                        WorldMapRadiusVisual.DrawApproxRadiusRing(tile, aaRange, WorldOverlayLineMaterials.RecruitTradingRadiusRing);
                    }
                }
            };
        }

        private static FireGizmoCache GetOrRefreshFireGizmoCache(WorldObject_WD_Outpost outpost, CompViralSpread comp)
        {
            int id = outpost.ID;
            int tick = Find.TickManager.TicksGame;
            if (!fireGizmoCacheByOutpostId.TryGetValue(id, out var cache))
            {
                cache = new FireGizmoCache();
                fireGizmoCacheByOutpostId[id] = cache;
            }
            if (tick - cache.lastRefreshTick < FireGizmoCacheLifetimeTicks && cache.desc != null)
                return cache;

            var seth = WorldDominationMod.settings;
            float bestShooting = outpost.GetHighestVirtualPawnSkill(SkillDefOf.Shooting);
            float cumShooting = outpost.GetSkillSum(SkillDefOf.Shooting);
            bool onCooldown = comp != null && comp.IsMortarOnCooldown;

            cache.onCooldown = onCooldown;
            cache.hasShootingSkill = bestShooting > 0f;

            if (onCooldown && comp != null)
            {
                cache.cooldownDaysLeft = (comp.mortarCooldownTick - tick) / 60000f;
                cache.desc = "TSA_WD_Mortar_LaunchDescCooldown".Translate(cache.cooldownDaysLeft.ToString("F1")).ToString();
            }
            else if (!cache.hasShootingSkill)
            {
                cache.desc = "TSA_WD_Mortar_LaunchDescNoSkill".Translate().ToString();
            }
            else
            {
                float dmg = MortarFireUtils.GetPlayerMortarShellDamage(outpost);
                float range = MortarFireUtils.GetPlayerMortarConfiguredMaxRangeTiles(outpost);
                cache.desc = "TSA_WD_Mortar_LaunchDesc".Translate(
                    range.ToString("F0"),
                    dmg.ToString("F0"),
                    bestShooting.ToString("F0"),
                    cumShooting.ToString("F0")).ToString();
            }
            cache.lastRefreshTick = tick;
            return cache;
        }

        /// <summary>Drop the cached fire-gizmo state for this outpost (call on destroy or when skills/cooldown changes
        /// must be reflected immediately). Safe to call for non-mortar outposts.</summary>
        public static void InvalidateFireGizmoCache(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return;
            fireGizmoCacheByOutpostId.Remove(outpost.ID);
        }

        private static void StartMortarTargeting(WorldObject_WD_Outpost source)
        {
            CameraJumper.TryJump(source.Tile);
            var seth = WorldDominationMod.settings;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            float range = MortarFireUtils.GetPlayerMortarConfiguredMaxRangeTiles(source);

            Find.WorldTargeter.BeginTargeting(
                (target) =>
                {
                    var wo = target.WorldObject;
                    if (!MortarFireUtils.IsValidMortarManualTarget(wo, source, manager, range)) return false;
                    MortarFireUtils.FireManualAtWorldTarget(source, wo);
                    return true;
                },
                false, null, false,
                () =>
                {
                    if (source != null)
                        WD_RadiusOverlayMode.DrawOrFill(source, range, OutpostCoverageFillKind.Red, WorldOverlayLineMaterials.RadiusRed, accuracyBands: true);
                },
                null,
                (target) =>
                {
                    if (!target.IsValid || target.Tile < 0) return false;
                    if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(target.Tile)) return false;
                    if (!target.HasWorldObject) return false;
                    return MortarFireUtils.IsValidMortarManualTarget(target.WorldObject, source, manager, range);
                }
            );
        }
    }

    /// <summary>Shared mortar fire resolution: cooldown, banded hit chance + skill bonus, shell spawn. Used by manual and defensive
    /// player outpost shots and NPC tier-4 settlements.</summary>
    public static class MortarFireUtils
    {
        /// <summary>Hostile settlement, AT Turret, caravan, or <see cref="WorldObject_Traveler"/> within mortar range.</summary>
        public static bool IsValidMortarManualTarget(WorldObject wo, WorldObject_WD_Outpost source, WorldComponent_SpreadManager manager, float? rangeOverride = null)
        {
            if (wo == null || wo.Destroyed || source == null || source.Destroyed) return false;
            if (wo == source) return false;
            var seth = WorldDominationMod.settings;
            float range = rangeOverride ?? MortarFireUtils.GetPlayerMortarConfiguredMaxRangeTiles(source);
            float dist = manager != null
                ? WorldActions_Utils.GetDistance(source.Tile, wo.Tile, manager)
                : Find.WorldGrid.ApproxDistanceInTiles(source.Tile, wo.Tile);
            if (dist > range) return false;
            switch (wo)
            {
                case Settlement s:
                    return s.Faction != null && WorldActions_Utils.SafeHostileTo(s.Faction, Faction.OfPlayer);
                case WorldObject_AT_Turret at:
                    return at.Faction != null && WorldActions_Utils.SafeHostileTo(at.Faction, Faction.OfPlayer);
                case Caravan c:
                    return c.Faction != null && WorldActions_Utils.SafeHostileTo(c.Faction, Faction.OfPlayer);
                case WorldObject_Traveler t:
                    return t.Faction != null && WorldActions_Utils.SafeHostileTo(t.Faction, Faction.OfPlayer);
                default:
                    return false;
            }
        }

        public static void FireManualAtWorldTarget(WorldObject_WD_Outpost origin, WorldObject target)
        {
            if (origin == null || target == null) return;
            if (!(target is Settlement || target is WorldObject_AT_Turret || target is Caravan || target is WorldObject_Traveler)) return;
            var seth = WorldDominationMod.settings;
            var comp = origin.GetComponent<CompViralSpread>();
            if (comp == null || comp.IsMortarOnCooldown) return;

            float bestShooting = origin.GetHighestVirtualPawnSkill(SkillDefOf.Shooting);
            if (bestShooting <= 0f)
            {
                Messages.Message("TSA_WD_Mortar_ReasonNoSkill".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            float damage = GetPlayerMortarShellDamage(origin);

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            float maxRange = MortarFireUtils.GetPlayerMortarConfiguredMaxRangeTiles(origin);
            int aimTile = MortarCaravanIntercept.ResolveMortarAimTileId(origin, target, maxRange);
            float dist = manager != null
                ? WorldActions_Utils.GetDistance(origin.Tile, aimTile, manager)
                : Find.WorldGrid.ApproxDistanceInTiles(origin.Tile, aimTile);
            float hitBonus = origin.GetBuiltUpgradeMortarHitChanceBonus();
            bool hit = RollMortarHit(dist, maxRange, bestShooting, seth, hitBonus);

            ApplyPlayerMortarCooldown(comp, origin);
            WD_Outpost_Mortar.InvalidateFireGizmoCache(origin);
            WorldActions_Traveler.SpawnMortarTraveler(origin, target, damage, guaranteedHit: hit, aimTileIdOverride: aimTile);

            manager?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_MortarLaunched".Translate(origin.LabelCap, target.LabelCap, damage.ToString("F0")),
                origin, target));
        }

        public static void FireDefensiveAtTraveler(WorldObject_WD_Outpost origin, WorldObject_Traveler target, float approxTileDist)
        {
            if (origin == null || target == null || target.Destroyed) return;
            // AA owns hostile drop pods.
            if (target.mission == TravelerMission.RaidDropPod)
            {
                AntiAirFireUtils.TryEngageDropPod(origin, target);
                return;
            }
            var seth = WorldDominationMod.settings;
            var comp = origin.GetComponent<CompViralSpread>();
            if (comp == null || comp.IsMortarOnCooldown) return;

            float bestShooting = origin.GetHighestVirtualPawnSkill(SkillDefOf.Shooting);
            if (bestShooting <= 0f) return;

            float damage = GetPlayerMortarShellDamage(origin);
            float maxRange = MortarFireUtils.GetPlayerMortarMaxRangeTiles(origin);
            // Ground intercept gating shares a combined mortar/AA wake-up radius, so re-check against the
            // mortar's own (possibly shrunk) range here — otherwise a target beyond mortar range but within
            // AA range would slip through and get shelled anyway.
            if (approxTileDist > maxRange) return;
            float hitBonus = origin.GetBuiltUpgradeMortarHitChanceBonus();
            bool hit = RollMortarHit(approxTileDist, maxRange, bestShooting, seth, hitBonus);
            int aimTile = MortarCaravanIntercept.ResolveMortarAimTileId(origin, target, maxRange);

            ApplyPlayerMortarCooldown(comp, origin);
            WD_Outpost_Mortar.InvalidateFireGizmoCache(origin);
            WorldActions_Traveler.SpawnMortarTraveler(origin, target, damage, guaranteedHit: hit, aimTileIdOverride: aimTile);
        }

        /// <summary>NPC settlement defensive shot (no pawns → flat damage + equivalent skill for hit chance).</summary>
        public static void FireNpcSettlementAtTraveler(Settlement origin, WorldObject_Traveler target, float approxTileDist)
        {
            if (origin == null || target == null || target.Destroyed) return;
            if (target.mission == TravelerMission.RaidDropPod)
            {
                AntiAirFireUtils.TryEngageFromSettlement(origin, target);
                return;
            }
            var seth = WorldDominationMod.settings;
            var comp = origin.GetComponent<CompViralSpread>();
            if (comp == null || comp.IsMortarOnCooldown) return;
            if (comp.tier != SettlementTier.T4) return;

            float damage = seth?.npcMortarDamage ?? WorldDominationSettings.DefNpcMortarDamage;
            float skillEquiv = seth?.npcMortarSkillEquivalent ?? WorldDominationSettings.DefNpcMortarSkillEquivalent;
            float maxRange = seth?.npcMortarRange ?? WorldDominationSettings.DefNpcMortarRange;
            // Ground intercept gating shares a combined mortar/AA wake-up radius, so re-check against the
            // settlement's own mortar range here — otherwise a target beyond mortar range but within AA range
            // would slip through and get shelled anyway.
            if (approxTileDist > maxRange) return;
            bool hit = RollMortarHit(approxTileDist, maxRange, skillEquiv, seth, 0f, useNpcBands: true);
            int aimTile = MortarCaravanIntercept.ResolveMortarAimTileId(origin, target, maxRange);

            if (!NpcT4GlobalFireStagger.TryClaimMortarFire()) return;
            ApplyNpcMortarCooldown(comp);
            WorldActions_Traveler.SpawnMortarTraveler(origin, target, damage, guaranteedHit: hit, aimTileIdOverride: aimTile);
        }

        /// <summary>NPC tier-4 settlement idle fallback: fire at a nearby static target (hostile settlement, AT Turret, or player outpost). Reuses the NPC fire damage/hit/cooldown path.</summary>
        public static void FireNpcSettlementAtStaticTarget(Settlement origin, WorldObject target, float approxTileDist)
        {
            if (origin == null || target == null || target.Destroyed) return;
            if (!(target is Settlement || target is WorldObject_WD_Outpost || target is WorldObject_AT_Turret)) return;
            var seth = WorldDominationMod.settings;
            var comp = origin.GetComponent<CompViralSpread>();
            if (comp == null || comp.IsMortarOnCooldown) return;
            if (comp.tier != SettlementTier.T4) return;

            float damage = seth?.npcMortarDamage ?? WorldDominationSettings.DefNpcMortarDamage;
            float skillEquiv = seth?.npcMortarSkillEquivalent ?? WorldDominationSettings.DefNpcMortarSkillEquivalent;
            float range = seth?.npcMortarRange ?? WorldDominationSettings.DefNpcMortarRange;
            // Defense-in-depth: FindNearestStaticMortarTarget already pre-filters by range, but re-check here
            // too so this method is safe regardless of caller.
            if (approxTileDist > range) return;
            bool hit = RollMortarHit(approxTileDist, range, skillEquiv, seth, 0f, useNpcBands: true);

            if (!NpcT4GlobalFireStagger.TryClaimMortarFire()) return;
            ApplyNpcMortarCooldown(comp);
            // Static targets resolve to their own tile inside SpawnMortarTraveler (no caravan leading needed).
            WorldActions_Traveler.SpawnMortarTraveler(origin, target, damage, guaranteedHit: hit);
        }

        /// <summary>AT Turret defensive shot: settings damage/cooldown/accuracy bands, own cooldown field.
        /// Letters/logs use <see cref="AtTurretNotifyUtility"/> (not mortar toggles).</summary>
        public static void FireFromAtTurret(WorldObject_AT_Turret origin, WorldObject target, float approxTileDist)
        {
            if (origin == null || origin.Destroyed || target == null || target.Destroyed) return;
            if (origin.IsOnCooldown) return;

            var seth = WorldDominationMod.settings;
            float maxRange = origin.EffectiveRangeTiles;
            bool hit = RollAtTurretHit(approxTileDist, maxRange, WorldObject_AT_Turret.DefaultSkillEquivalent, seth);
            int aimTile = MortarCaravanIntercept.ResolveMortarAimTileId(origin, target, maxRange);

            origin.ApplyCooldown();
            float damage = seth != null
                ? seth.GetAtTurretDamage(origin.tier)
                : WorldObject_AT_Turret.DefaultDamage;
            WorldActions_Traveler.SpawnMortarTraveler(origin, target, Mathf.Max(0f, damage), guaranteedHit: hit, aimTileIdOverride: aimTile);
        }

        /// <summary>Player mortar shell strength: global base + Σ upgrade <see cref="OutpostUpgradeDef.mortarShellDamageBonus"/> × level.</summary>
        public static float GetPlayerMortarShellDamage(WorldObject_WD_Outpost origin)
        {
            var seth = WorldDominationMod.settings;
            float d = seth?.mortarBaseShellDamage ?? WorldDominationSettings.DefMortarBaseShellDamage;
            if (origin != null)
                d += origin.GetBuiltUpgradeMortarShellDamageBonus();
            return Mathf.Max(0f, d);
        }

        /// <summary>Settings + upgrade bonuses + Strategist expert % (no per-outpost shrink).</summary>
        public static float GetPlayerMortarConfiguredMaxRangeTiles(WorldObject_WD_Outpost origin)
        {
            var seth = WorldDominationMod.settings;
            float baseR = Mathf.Max(1f, seth?.mortarRange ?? WorldDominationSettings.DefMortarRange);
            float upg = origin?.GetBuiltUpgradeMortarRangeBonus() ?? 0f;
            float r = Mathf.Max(1f, baseR + upg);
            float expert = OutpostExpertUtility.GetStrategistAttackRangeBonusFraction(origin);
            if (expert > 0f)
                r *= 1f + expert;
            return r;
        }

        /// <summary>Effective mortar range (respects per-outpost shrink override).</summary>
        public static float GetPlayerMortarMaxRangeTiles(WorldObject_WD_Outpost origin)
        {
            float max = GetPlayerMortarConfiguredMaxRangeTiles(origin);
            if (origin == null) return max;
            float ov = origin.MortarRangeOverride;
            if (ov < 0f) return max;
            float min = Mathf.Min(Dialog_OutpostRangeAdjust.MinTiles, max);
            return Mathf.Clamp(ov, min, max);
        }

        private static void ApplyNpcMortarCooldown(CompViralSpread comp)
        {
            float days = Mathf.Max(0.1f, WorldDominationMod.settings?.npcMortarCooldownDays ?? WorldDominationSettings.DefNpcMortarCooldownDays);
            comp.mortarCooldownTick = Find.TickManager.TicksGame + Mathf.RoundToInt(days * 60000f);
        }

        private static void ApplyPlayerMortarCooldown(CompViralSpread comp, WorldObject_WD_Outpost origin)
        {
            if (comp == null || origin == null) return;
            float days = GetPlayerMortarEffectiveCooldownDays(origin, out _, out _, out _, out _);
            comp.mortarCooldownTick = Find.TickManager.TicksGame + Mathf.RoundToInt(days * 60000f);
        }

        /// <summary>Cooldown duration after skill + upgrade reductions (same formula as <see cref="ApplyPlayerMortarCooldown"/>).</summary>
        public static float GetPlayerMortarEffectiveCooldownDays(WorldObject_WD_Outpost origin, out float baseCooldownDays, out float durationMultiplier, out float fromSkillReduction, out float fromUpgradeReduction)
        {
            baseCooldownDays = 5f;
            durationMultiplier = 1f;
            fromSkillReduction = 0f;
            fromUpgradeReduction = 0f;
            var seth = WorldDominationMod.settings;
            baseCooldownDays = Mathf.Max(0.1f, seth?.cooldownMortarDays ?? WorldDominationSettings.DefCooldownMortarDays);
            if (origin == null)
                return baseCooldownDays;
            fromSkillReduction = WorldDominationSettings.MortarCooldownReductionPerCumulativeShootingSkill * origin.GetSkillSum(SkillDefOf.Shooting);
            fromUpgradeReduction = origin.GetBuiltUpgradeMortarCooldownReduction();
            durationMultiplier = Mathf.Max(WorldDominationSettings.MortarCooldownMultiplierFloor, 1f - fromSkillReduction - fromUpgradeReduction);
            return baseCooldownDays * durationMultiplier;
        }

        /// <summary>
        /// Accuracy band from distance as a fraction of max range: 0 = 0–50%, 1 = 51–75%, 2 = 76–100%.
        /// Shared by combat hit rolls and world-map coverage fills.
        /// </summary>
        public static int GetAccuracyBandIndex(float distance, float maxRange)
        {
            float r = Mathf.Max(1f, maxRange);
            float frac = Mathf.Clamp01(distance / r);
            if (frac <= 0.5f) return 0;
            if (frac <= 0.75f) return 1;
            return 2;
        }

        /// <summary>Base hit chance from distance band (fraction of max range); best shooter adds <see cref="WorldDominationSettings.MortarHitFlatBonusPerBestShootingLevel"/> per skill level; then upgrade additive bonus. NPC T4 settlement fire uses its own decoupled bands when <paramref name="useNpcBands"/> is true.</summary>
        public static float BandBaseHitChance(float distance, float maxRange, WorldDominationSettings seth, bool useNpcBands = false)
        {
            var s = seth ?? WorldDominationMod.settings;
            int band = GetAccuracyBandIndex(distance, maxRange);
            if (useNpcBands)
            {
                switch (band)
                {
                    case 0:
                        return Mathf.Clamp01(s?.npcMortarHitChance0To50PctRange ?? WorldDominationSettings.DefNpcMortarHitChance0To50PctRange);
                    case 1:
                        return Mathf.Clamp01(s?.npcMortarHitChance51To75PctRange ?? WorldDominationSettings.DefNpcMortarHitChance51To75PctRange);
                    default:
                        return Mathf.Clamp01(s?.npcMortarHitChance76To100PctRange ?? WorldDominationSettings.DefNpcMortarHitChance76To100PctRange);
                }
            }
            switch (band)
            {
                case 0:
                    return Mathf.Clamp01(s?.mortarHitChance0To50PctRange ?? WorldDominationSettings.DefMortarHitChance0To50PctRange);
                case 1:
                    return Mathf.Clamp01(s?.mortarHitChance51To75PctRange ?? WorldDominationSettings.DefMortarHitChance51To75PctRange);
                default:
                    return Mathf.Clamp01(s?.mortarHitChance76To100PctRange ?? WorldDominationSettings.DefMortarHitChance76To100PctRange);
            }
        }

        /// <summary>Whether the shell hits: band base + best-shooter flat (+1 pp per Shooting level) + upgrade hit bonus. NPC T4 settlement fire uses its own decoupled bands when <paramref name="useNpcBands"/> is true.</summary>
        public static bool RollMortarHit(float distance, float range, float bestShootingSkill, WorldDominationSettings seth, float upgradeHitChanceBonus = 0f, bool useNpcBands = false)
        {
            var s = seth ?? WorldDominationMod.settings;
            float maxR = Mathf.Max(1f, range);
            float baseHit = BandBaseHitChance(distance, maxR, s, useNpcBands);
            float fromBest = Mathf.Max(0f, bestShootingSkill) * WorldDominationSettings.MortarHitFlatBonusPerBestShootingLevel;
            float hitChance = Mathf.Clamp01(baseHit + fromBest + upgradeHitChanceBonus);
            return Rand.Value < hitChance;
        }

        /// <summary>AT Turret base hit chance from its own Experimental accuracy band settings.</summary>
        public static float BandBaseAtTurretHitChance(float distance, float maxRange, WorldDominationSettings seth)
        {
            var s = seth ?? WorldDominationMod.settings;
            switch (GetAccuracyBandIndex(distance, maxRange))
            {
                case 0:
                    return Mathf.Clamp01(s?.atTurretHitChance0To50PctRange ?? WorldDominationSettings.DefAtTurretHitChance0To50PctRange);
                case 1:
                    return Mathf.Clamp01(s?.atTurretHitChance51To75PctRange ?? WorldDominationSettings.DefAtTurretHitChance51To75PctRange);
                default:
                    return Mathf.Clamp01(s?.atTurretHitChance76To100PctRange ?? WorldDominationSettings.DefAtTurretHitChance76To100PctRange);
            }
        }

        /// <summary>AT Turret hit roll: AT accuracy bands + skill-equivalent flat bonus (same +1 pp/level as mortars).</summary>
        public static bool RollAtTurretHit(float distance, float range, float skillEquivalent, WorldDominationSettings seth)
        {
            float maxR = Mathf.Max(1f, range);
            float baseHit = BandBaseAtTurretHitChance(distance, maxR, seth);
            float fromSkill = Mathf.Max(0f, skillEquivalent) * WorldDominationSettings.MortarHitFlatBonusPerBestShootingLevel;
            return Rand.Value < Mathf.Clamp01(baseHit + fromSkill);
        }

        public static string MissionMaskLabel(MissionMask mask)
        {
            if (mask == MissionMask.All) return "TSA_WD_Mortar_Filter_All".Translate();
            var parts = new List<string>();
            if ((mask & MissionMask.Raider) != 0) parts.Add("TSA_WD_Mortar_Filter_Raider".Translate());
            if ((mask & MissionMask.Expansion) != 0) parts.Add("TSA_WD_Mortar_Filter_Expansion".Translate());
            if ((mask & MissionMask.Road) != 0) parts.Add("TSA_WD_Mortar_Filter_Road".Translate());
            if ((mask & MissionMask.Trader) != 0) parts.Add("TSA_WD_Mortar_Filter_Trader".Translate());
            if ((mask & MissionMask.Fortify) != 0) parts.Add("TSA_WD_Mortar_Filter_Fortify".Translate());
            return parts.Count == 0 ? "—" : string.Join(", ", parts);
        }

        /// <summary>
        /// Split mortar shell damage across offensive and defensive strength: take up to half the shell from each pool first,
        /// then apply any remainder to offensive then defensive while that pool still has headroom. Never drives a pool below zero.
        /// </summary>
        public static void ApplyMortarShellToOffensiveDefensiveStrength(float shellPotency, ref float offensiveStrength, ref float defensiveStrength)
        {
            float damage = Mathf.Max(0f, shellPotency);
            float off = Mathf.Max(0f, offensiveStrength);
            float def = Mathf.Max(0f, defensiveStrength);
            if (damage <= 0f)
            {
                offensiveStrength = off;
                defensiveStrength = def;
                return;
            }

            float half = damage * 0.5f;
            float fromDef = Mathf.Min(def, half);
            float fromOff = Mathf.Min(off, half);
            float remaining = damage - fromDef - fromOff;
            def -= fromDef;
            off -= fromOff;

            if (remaining > 0.0001f)
            {
                float extraOff = Mathf.Min(off, remaining);
                off -= extraOff;
                remaining -= extraOff;
            }
            if (remaining > 0.0001f)
            {
                float extraDef = Mathf.Min(def, remaining);
                def -= extraDef;
            }

            offensiveStrength = Mathf.Max(0f, off);
            defensiveStrength = Mathf.Max(0f, def);
        }
    }
}
