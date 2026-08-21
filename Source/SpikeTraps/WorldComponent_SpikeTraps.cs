using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// World-wide spike-trap / caltrops registry. Hostile travelers take flat strength damage when leaving
    /// a trapped tile; the trap also wears by traveler strength (see <see cref="TryTriggerOnTravelerExit"/>).
    /// </summary>
    public class WorldComponent_SpikeTraps : WorldComponent
    {
        private List<SpikeTrapRecord> records = new List<SpikeTrapRecord>();
        private bool[] presentByTile;
        private int[] recordIndexByTile;

        private static WorldComponent_SpikeTraps cached;
        private static World cachedWorld;

        public WorldComponent_SpikeTraps(World world) : base(world)
        {
            cached = this;
            cachedWorld = world;
        }

        public static WorldComponent_SpikeTraps Get()
        {
            World w = Find.World;
            if (w == null) return null;
            if (cached != null && cachedWorld == w) return cached;
            cached = w.GetComponent<WorldComponent_SpikeTraps>();
            cachedWorld = w;
            return cached;
        }

        public IReadOnlyList<SpikeTrapRecord> Records => records;

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            RebuildIndex();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref records, "spikeTraps", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (records == null) records = new List<SpikeTrapRecord>();
                var s = WorldDominationMod.settings;
                for (int i = 0; i < records.Count; i++)
                {
                    SpikeTrapRecord r = records[i];
                    if (r == null) continue;
                    float maxHp = s != null ? s.GetSpikeTrapMaxHealth(r.kind) : WorldDominationSettings.DefSpikeTrapSpikeMaxHealth;
                    if (r.health <= 0f || float.IsNaN(r.health) || float.IsInfinity(r.health))
                        r.health = maxHp;
                    else if (r.health > maxHp)
                        r.health = maxHp;
                }
                records.RemoveAll(r => r == null || r.tileId < 0);
                RebuildIndex();
            }
        }

        public override void WorldComponentUpdate()
        {
            if (records == null || records.Count == 0) return;
            if (!WorldRendererUtility.WorldRendered) return;
            if (!WorldComponent_WDVisualizerToggle.ShowRoadBlocksAndTraps) return;
            if (WD_WorldMapZoomUtil.IsSurfaceOverlayZoomedTooFarOut()) return;
            DrawTraps();
        }

        public bool HasTrapAt(int tileId)
        {
            EnsureIndex();
            return presentByTile != null && tileId >= 0 && tileId < presentByTile.Length && presentByTile[tileId];
        }

        public bool TryGet(int tileId, out SpikeTrapRecord record)
        {
            record = null;
            EnsureIndex();
            if (tileId < 0 || recordIndexByTile == null || tileId >= recordIndexByTile.Length) return false;
            int idx = recordIndexByTile[tileId];
            if (idx < 0 || idx >= records.Count) return false;
            record = records[idx];
            return record != null;
        }

        public bool TryPlaceOrUpgrade(int tileId, Faction builtBy, SpikeTrapKind kind, Settlement builtBySettlement = null)
        {
            if (tileId < 0) return false;
            if (!WorldActions_SpikeTraps.IsTileBaseEligibleForSpikeTrap(tileId)) return false;

            // Fortifications are mutually exclusive with each other. Never remove paved roads here.
            WorldActions_RoadBlocks.ClearIfPresent(tileId);

            float maxHp = WorldDominationMod.settings != null
                ? WorldDominationMod.settings.GetSpikeTrapMaxHealth(kind)
                : WorldDominationSettings.DefSpikeTrapSpikeMaxHealth;

            if (TryGet(tileId, out SpikeTrapRecord existing) && existing != null)
            {
                bool upgrade = SpikeTrapKindUtil.CanUpgradeTo(existing.kind, kind);
                bool claimHostile = WorldActions_RoadBlocks.CanClaimHostileFortification(builtBy, existing.builtByFaction);
                if (!upgrade && !claimHostile)
                    return false;
                existing.kind = kind;
                existing.builtByFaction = builtBy ?? existing.builtByFaction;
                if (builtBySettlement != null)
                    existing.builtBySettlement = builtBySettlement;
                else if (claimHostile)
                    existing.builtBySettlement = null;
                existing.health = maxHp;
                return true;
            }

            records.Add(new SpikeTrapRecord
            {
                tileId = tileId,
                builtByFaction = builtBy,
                builtBySettlement = builtBySettlement,
                kind = kind,
                health = maxHp
            });
            EnsureIndex();
            SetIndexSlot(tileId, records.Count - 1);
            return true;
        }

        public bool TryPlace(int tileId, Faction builtBy, SpikeTrapKind kind = SpikeTrapKind.Spike)
        {
            return TryPlaceOrUpgrade(tileId, builtBy, kind);
        }

        public bool TryClear(int tileId)
        {
            if (!TryGet(tileId, out SpikeTrapRecord rec) || rec == null) return false;
            int idx = recordIndexByTile[tileId];
            records.RemoveAt(idx);
            RebuildIndex();
            return true;
        }

        public int ClearBuiltBySettlement(Settlement settlement)
        {
            if (settlement == null || records == null || records.Count == 0) return 0;
            int removed = records.RemoveAll(r => r != null && r.builtBySettlement == settlement);
            if (removed > 0)
                RebuildIndex();
            return removed;
        }

        /// <summary>
        /// Hostile ground traveler leaving a trap tile: damage traveler, wear trap HP by traveler strength.
        /// Past the max-triggers cap: no damage, no wear.
        /// </summary>
        public void TryTriggerOnTravelerExit(int leftTileId, WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed || leftTileId < 0) return;
            if (traveler.mission == TravelerMission.RoadBuilding
                || traveler.mission == TravelerMission.RoadBlock
                || traveler.mission == TravelerMission.SpikeTrap
                || traveler.mission == TravelerMission.NpcFortify
                || traveler.mission == TravelerMission.NpcAtTurret
                || traveler.mission == TravelerMission.AtTurret)
                return;
            if (traveler.mission == TravelerMission.MortarStrike
                || traveler.mission == TravelerMission.AntiAirStrike
                || traveler.mission == TravelerMission.RapidResponseDropPod
                || traveler.mission == TravelerMission.RaidDropPod)
                return;
            if (traveler is WorldObject_Traveler_Outpost_Delivery delivery && delivery.deliveryViaDropPod)
                return;
            if (WD_PathFollower.IsBallisticWorldFlight(traveler))
                return;

            if (!TryGet(leftTileId, out SpikeTrapRecord rec) || rec == null) return;

            Faction builder = rec.builtByFaction;
            if (builder == null || traveler.Faction == null) return;
            if (!WorldActions_Utils.SafeHostileTo(traveler.Faction, builder)) return;

            var s = WorldDominationMod.settings;
            int maxTriggers = s != null ? s.spikeTrapMaxTriggersPerTraveler : WorldDominationSettings.DefSpikeTrapMaxTriggersPerTraveler;
            if (maxTriggers < 0) maxTriggers = 0;
            if (traveler.spikeTrapsTriggered >= maxTriggers)
                return;

            float damage = s != null ? s.GetSpikeTrapDamage(rec.kind) : WorldDominationSettings.DefSpikeTrapSpikeDamage;
            damage = Mathf.Max(0f, damage);
            if (damage <= 0f) return;

            traveler.travelerStrength = Mathf.Max(0f, traveler.travelerStrength - damage);
            traveler.spikeTrapsTriggered++;

            // Wear by traveler strength as they arrived (before trap damage).
            float travelerStrengthBefore = traveler.travelerStrength + damage;
            float wear = Mathf.Max(0f, travelerStrengthBefore);
            rec.health -= wear;

            bool destroyed = rec.health <= 0f;
            if (destroyed)
                TryClear(leftTileId);

            if (builder.IsPlayer)
            {
                Messages.Message(
                    destroyed
                        ? "TSA_WD_SpikeTrap_TriggeredDestroyed".Translate(traveler.LabelCap, damage.ToString("F0"))
                        : "TSA_WD_SpikeTrap_Triggered".Translate(traveler.LabelCap, damage.ToString("F0")),
                    new LookTargets(new GlobalTargetInfo(leftTileId)),
                    MessageTypeDefOf.NeutralEvent);
            }

            var seth = WorldDominationMod.settings;
            if (seth != null && seth.verboseLogging)
            {
                string text = "TSA_WD_Log_SpikeTrap_Triggered".Translate(
                    traveler.LabelCap,
                    damage.ToString("F0"));
                Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(
                    new SpreadLogEntry(text, traveler, traveler.originObject));
                WDVerbose.Msg($"SpikeTrap triggered tile={leftTileId} traveler={traveler.LabelCap} dmg={damage:F0} wear={wear:F0} destroyed={destroyed} count={traveler.spikeTrapsTriggered}");
            }

            if (traveler.travelerStrength <= 0.01f && !traveler.Destroyed)
            {
                var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
                TravelerEndpointUtility.AbortTraveler(
                    traveler,
                    "TSA_WD_Log_SpikeTrap_DestroyedTraveler".Translate(traveler.LabelCap),
                    manager);
            }
        }

        private void EnsureIndex()
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;
            if (presentByTile == null || presentByTile.Length != grid.TilesCount)
                RebuildIndex();
        }

        private void RebuildIndex()
        {
            WorldGrid grid = Find.WorldGrid;
            int n = grid != null ? grid.TilesCount : 0;
            if (n <= 0)
            {
                presentByTile = null;
                recordIndexByTile = null;
                return;
            }

            presentByTile = new bool[n];
            recordIndexByTile = new int[n];
            for (int i = 0; i < n; i++)
                recordIndexByTile[i] = -1;

            if (records == null) records = new List<SpikeTrapRecord>();
            for (int i = 0; i < records.Count; i++)
            {
                SpikeTrapRecord r = records[i];
                if (r == null || r.tileId < 0 || r.tileId >= n) continue;
                recordIndexByTile[r.tileId] = i;
                presentByTile[r.tileId] = true;
            }
        }

        private void SetIndexSlot(int tileId, int recordIndex)
        {
            if (presentByTile == null || tileId < 0 || tileId >= presentByTile.Length) return;
            recordIndexByTile[tileId] = recordIndex;
            presentByTile[tileId] = true;
        }

        private static Material MatFor(SpikeTrapKind kind, Color color)
        {
            return MaterialPool.MatFrom(
                SpikeTrapKindUtil.TexturePath(kind),
                ShaderDatabase.WorldOverlayTransparentLit,
                color,
                WorldMaterials.WorldObjectRenderQueue);
        }

        private void DrawTraps()
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;

            float size = WD_WorldMapZoomUtil.GetSurfaceOverlayQuadSize(0.66f * 1.1f * 0.8f);
            const float uprightRotation = -90f;
            for (int i = 0; i < records.Count; i++)
            {
                SpikeTrapRecord r = records[i];
                if (r == null || r.tileId < 0 || r.tileId >= grid.TilesCount) continue;
                Color color = r.builtByFaction?.Color ?? Color.cyan;
                Material mat = MatFor(r.kind, color);
                if (mat == null) continue;
                Vector3 center = grid.GetTileCenter(r.tileId);
                WorldRendererUtility.DrawQuadTangentialToPlanet(
                    center, size, WD_WorldMapZoomUtil.GetSurfaceOverlayDrawAltitude(), mat, uprightRotation);
            }
        }
    }
}
