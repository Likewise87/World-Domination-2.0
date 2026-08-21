using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// World-wide road-block registry with an O(1) tile-indexed presence array for pathfinding hot paths.
    /// </summary>
    public class WorldComponent_RoadBlocks : WorldComponent
    {
        private List<RoadBlockRecord> records = new List<RoadBlockRecord>();
        /// <summary>Per-tile presence: 0 = none, else kind+1. Sized to <see cref="WorldGrid.TilesCount"/>.</summary>
        private byte[] kindByTile;
        private int[] recordIndexByTile;

        private static WorldComponent_RoadBlocks cached;
        private static World cachedWorld;

        public WorldComponent_RoadBlocks(World world) : base(world)
        {
            cached = this;
            cachedWorld = world;
        }

        public static WorldComponent_RoadBlocks Get()
        {
            World w = Find.World;
            if (w == null) return null;
            if (cached != null && cachedWorld == w) return cached;
            cached = w.GetComponent<WorldComponent_RoadBlocks>();
            cachedWorld = w;
            return cached;
        }

        public IReadOnlyList<RoadBlockRecord> Records => records;

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            RebuildIndex();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref records, "roadBlocks", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (records == null) records = new List<RoadBlockRecord>();
                var s = WorldDominationMod.settings;
                for (int i = 0; i < records.Count; i++)
                {
                    RoadBlockRecord r = records[i];
                    if (r == null) continue;
                    float maxHp = s != null ? s.GetRoadBlockMaxHealth(r.kind) : WorldDominationSettings.DefRoadBlockNormalMaxHealth;
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
            DrawBlocks();
        }

        public bool HasBlockAt(int tileId)
        {
            EnsureIndex();
            return kindByTile != null && tileId >= 0 && tileId < kindByTile.Length && kindByTile[tileId] != 0;
        }

        public bool TryGet(int tileId, out RoadBlockRecord record)
        {
            record = null;
            EnsureIndex();
            if (tileId < 0 || recordIndexByTile == null || tileId >= recordIndexByTile.Length) return false;
            int idx = recordIndexByTile[tileId];
            if (idx < 0 || idx >= records.Count) return false;
            record = records[idx];
            return record != null;
        }

        /// <summary>
        /// Flat movement difficulty added after the road multiplier.
        /// Light/Normal/Heavy always apply; <see cref="RoadBlockKind.Gate"/> only vs hostiles (unused in UI).
        /// </summary>
        public float GetFlatPenaltyAt(int tileId, Faction moverFaction = null)
        {
            if (!TryGet(tileId, out RoadBlockRecord rec) || rec == null) return 0f;
            if (rec.kind == RoadBlockKind.Gate)
            {
                if (moverFaction == null || rec.builtByFaction == null) return 0f;
                if (!WorldActions_Utils.SafeHostileTo(moverFaction, rec.builtByFaction)) return 0f;
            }

            var s = WorldDominationMod.settings;
            float penalty = s != null
                ? s.GetRoadBlockFlatPenalty(rec.kind)
                : WorldDominationSettings.DefRoadBlockNormalFlatPenalty;
            return Mathf.Max(0f, penalty);
        }

        public static float GetFlatPenalty(int tileId, Faction moverFaction = null)
        {
            WorldComponent_RoadBlocks comp = Get();
            return comp != null ? comp.GetFlatPenaltyAt(tileId, moverFaction) : 0f;
        }

        /// <summary>
        /// Place a new block, or upgrade an existing lower-tier block to <paramref name="kind"/> (full max HP).
        /// </summary>
        public bool TryPlaceOrUpgrade(int tileId, Faction builtBy, RoadBlockKind kind, Settlement builtBySettlement = null)
        {
            if (tileId < 0) return false;
            if (!RoadBlockKindUtil.IsPlaceableFromUi(kind)) return false;
            if (!WorldActions_RoadBlocks.IsTileBaseEligibleForRoadBlock(tileId)) return false;

            // Fortifications are mutually exclusive with each other. Never remove paved roads here.
            WorldActions_SpikeTraps.ClearIfPresent(tileId);

            float maxHp = WorldDominationMod.settings != null
                ? WorldDominationMod.settings.GetRoadBlockMaxHealth(kind)
                : WorldDominationSettings.DefRoadBlockNormalMaxHealth;

            if (TryGet(tileId, out RoadBlockRecord existing) && existing != null)
            {
                bool upgrade = RoadBlockKindUtil.CanUpgradeTo(existing.kind, kind);
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
                EnsureIndex();
                SetIndexSlot(tileId, recordIndexByTile[tileId], kind);
                WD_WorldLayer_MovementDifficultyOverlay.InvalidateAndDirtyIfActive();
                return true;
            }

            var rec = new RoadBlockRecord
            {
                tileId = tileId,
                builtByFaction = builtBy,
                builtBySettlement = builtBySettlement,
                kind = kind,
                health = maxHp
            };
            records.Add(rec);
            EnsureIndex();
            SetIndexSlot(tileId, records.Count - 1, kind);
            WD_WorldLayer_MovementDifficultyOverlay.InvalidateAndDirtyIfActive();
            return true;
        }

        public bool TryPlace(int tileId, Faction builtBy, RoadBlockKind kind = RoadBlockKind.Normal)
        {
            return TryPlaceOrUpgrade(tileId, builtBy, kind);
        }

        public bool TryClear(int tileId)
        {
            if (!TryGet(tileId, out RoadBlockRecord rec) || rec == null) return false;
            int idx = recordIndexByTile[tileId];
            records.RemoveAt(idx);
            RebuildIndex();
            WD_WorldLayer_MovementDifficultyOverlay.InvalidateAndDirtyIfActive();
            return true;
        }

        public int ClearBuiltBySettlement(Settlement settlement)
        {
            if (settlement == null || records == null || records.Count == 0) return 0;
            int removed = records.RemoveAll(r => r != null && r.builtBySettlement == settlement);
            if (removed > 0)
            {
                RebuildIndex();
                WD_WorldLayer_MovementDifficultyOverlay.InvalidateAndDirtyIfActive();
            }
            return removed;
        }

        /// <summary>Adjust HP by <paramref name="delta"/> (positive heal, negative damage). Clamps to [0, max]. Clears at ≤ 0.</summary>
        public bool TryAdjustHealth(int tileId, float delta, out float newHealth)
        {
            newHealth = 0f;
            if (!TryGet(tileId, out RoadBlockRecord rec) || rec == null) return false;
            float max = WorldDominationMod.settings != null
                ? WorldDominationMod.settings.GetRoadBlockMaxHealth(rec.kind)
                : WorldDominationSettings.DefRoadBlockNormalMaxHealth;
            rec.health = Mathf.Clamp(rec.health + delta, 0f, max);
            newHealth = rec.health;
            if (rec.health <= 0f)
            {
                TryClear(tileId);
                newHealth = 0f;
            }
            return true;
        }

        /// <summary>
        /// Wear when a hostile WD ground traveler leaves a blocked tile. Damage equals traveler strength.
        /// Skips ballistic flight, road / road-block / spike-trap crews.
        /// </summary>
        public void ApplyTravelerExitDamage(int leftTileId, WorldObject_Traveler traveler)
        {
            if (traveler == null || leftTileId < 0) return;
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

            if (!TryGet(leftTileId, out RoadBlockRecord rec) || rec == null) return;

            Faction builder = rec.builtByFaction;
            if (builder == null || traveler.Faction == null) return;
            if (!WorldActions_Utils.SafeHostileTo(traveler.Faction, builder)) return;

            float damage = Mathf.Max(0f, traveler.travelerStrength);
            if (damage <= 0f) return;

            rec.health -= damage;
            if (rec.health > 0f) return;

            TryClear(leftTileId);

            if (builder.IsPlayer)
            {
                Messages.Message(
                    "TSA_WD_RoadBlock_DestroyedByTraffic".Translate(),
                    new LookTargets(new GlobalTargetInfo(leftTileId)),
                    MessageTypeDefOf.NegativeEvent);
            }

            var seth = WorldDominationMod.settings;
            if (seth != null && seth.verboseLogging)
            {
                string text = "TSA_WD_Log_RoadBlock_DestroyedByTraffic".Translate(
                    traveler.LabelCap,
                    damage.ToString("F0"));
                Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(
                    new SpreadLogEntry(text, traveler, traveler.originObject));
                WDVerbose.Msg($"RoadBlock destroyed by traffic tile={leftTileId} traveler={traveler.LabelCap} dmg={damage:F0}");
            }
        }

        private void EnsureIndex()
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;
            if (kindByTile == null || kindByTile.Length != grid.TilesCount)
                RebuildIndex();
        }

        private void RebuildIndex()
        {
            WorldGrid grid = Find.WorldGrid;
            int n = grid != null ? grid.TilesCount : 0;
            if (n <= 0)
            {
                kindByTile = null;
                recordIndexByTile = null;
                return;
            }

            kindByTile = new byte[n];
            recordIndexByTile = new int[n];
            for (int i = 0; i < n; i++)
                recordIndexByTile[i] = -1;

            if (records == null) records = new List<RoadBlockRecord>();
            for (int i = 0; i < records.Count; i++)
            {
                RoadBlockRecord r = records[i];
                if (r == null || r.tileId < 0 || r.tileId >= n) continue;
                recordIndexByTile[r.tileId] = i;
                kindByTile[r.tileId] = (byte)((int)r.kind + 1);
            }
        }

        private void SetIndexSlot(int tileId, int recordIndex, RoadBlockKind kind)
        {
            if (kindByTile == null || tileId < 0 || tileId >= kindByTile.Length) return;
            recordIndexByTile[tileId] = recordIndex;
            kindByTile[tileId] = (byte)((int)kind + 1);
        }

        private static Material MatFor(RoadBlockKind kind)
        {
            return MaterialPool.MatFrom(
                RoadBlockKindUtil.TexturePath(kind),
                ShaderDatabase.WorldOverlayTransparentLit,
                Color.white,
                WorldMaterials.WorldObjectRenderQueue);
        }

        private void DrawBlocks()
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;

            float size = WD_WorldMapZoomUtil.GetSurfaceOverlayQuadSize(0.66f * 1.1f);
            for (int i = 0; i < records.Count; i++)
            {
                RoadBlockRecord r = records[i];
                if (r == null || r.tileId < 0 || r.tileId >= grid.TilesCount) continue;
                Material mat = MatFor(r.kind);
                if (mat == null) continue;
                Vector3 center = grid.GetTileCenter(r.tileId);
                // Only Medium (Normal enum) uses a seeded random spin; Light/Heavy (and Gate) stay upright like traps.
                float rotation = r.kind == RoadBlockKind.Normal
                    ? Rand.RangeSeeded(0f, 360f, r.tileId)
                    : -90f;
                WorldRendererUtility.DrawQuadTangentialToPlanet(
                    center, size, WD_WorldMapZoomUtil.GetSurfaceOverlayDrawAltitude(), mat, rotation);
            }
        }
    }
}
