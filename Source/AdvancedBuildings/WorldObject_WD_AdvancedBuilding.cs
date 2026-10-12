using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// NPC advanced building (Silo, Hospital, Watch Tower, Stable; artillery turrets subclass this).
    /// Owned by <see cref="builtBySettlement"/>; removed when the owner is razed or changes faction.
    /// Globe <see cref="Material"/> is the owner's settlement texture + faction tint (AT turret convention);
    /// type identity comes from the def's expanding icon. Both draw slightly transparent so settlements
    /// and outposts read as the solid sites.
    /// </summary>
    public class WorldObject_WD_AdvancedBuilding : WorldObject
    {
        /// <summary>Just-built buildings cannot be shelled for two days.</summary>
        public const int ShellGraceTicks = 120000;

        /// <summary>~10% transparent vs opaque settlements / outposts (Material + ExpandingIcon).</summary>
        public const float GlobeIconAlpha = 0.9f;

        private AdvancedBuildingDef buildingDef;
        public Settlement builtBySettlement;
        public int placedTick = -1;
        /// <summary>When true, the destroyed log line is skipped (owner wipe / already reported).</summary>
        public bool suppressDestroyedLetter;

        private Material cachedMaterial;
        private string cachedInspectString;
        private int cachedInspectTick = -999;
        private int cachedInspectVersion = -1;

        public AdvancedBuildingDef BuildingDef
        {
            get
            {
                if (buildingDef == null && def != null)
                    buildingDef = WorldActions_AdvancedBuildings.DefForWorldObjectDef(def);
                return buildingDef;
            }
        }

        public void InitBuilding(AdvancedBuildingDef bDef, Settlement owner)
        {
            buildingDef = bDef;
            builtBySettlement = owner;
            placedTick = Find.TickManager?.TicksGame ?? 0;
        }

        public bool IsArtillery => BuildingDef?.IsArtillery == true;

        /// <summary>Owner gone, razed, or now another faction (cleanup runs on the daily sweep, never during load).</summary>
        public bool IsOrphan
        {
            get
            {
                Settlement o = builtBySettlement;
                if (o == null || o.Destroyed) return true;
                if (o.Faction == null || o.Faction != Faction) return true;
                return false;
            }
        }

        public int ShellGraceTicksLeft
        {
            get
            {
                if (placedTick < 0) return 0;
                int now = Find.TickManager?.TicksGame ?? 0;
                return Mathf.Max(0, placedTick + ShellGraceTicks - now);
            }
        }

        public bool IsInShellGrace => ShellGraceTicksLeft > 0;

        public override string Label => BuildingDef != null ? BuildingDef.LabelText : base.Label;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref buildingDef, "advBuildingDef");
            Scribe_References.Look(ref builtBySettlement, "builtBySettlement");
            Scribe_Values.Look(ref placedTick, "placedTick", -1);
        }

        public override void SpawnSetup()
        {
            base.SpawnSetup();
            WorldComponent_AdvancedBuildings.Get()?.Register(this);
        }

        public override void PostAdd()
        {
            base.PostAdd();
            WorldComponent_AdvancedBuildings.Get()?.Register(this);
        }

        public override void PostRemove()
        {
            WorldComponent_AdvancedBuildings.Get()?.Unregister(this);
            base.PostRemove();
        }

        public override void Destroy()
        {
            if (!Destroyed && !suppressDestroyedLetter)
                AdvancedBuildingCombatUtility.LogBuildingDestroyed(this, null);
            WorldComponent_AdvancedBuildings.Get()?.Unregister(this);
            base.Destroy();
        }

        public override void SetFaction(Faction newFaction)
        {
            base.SetFaction(newFaction);
            cachedMaterial = null;
            Patch_WdWorldObjectNoExpandingIcon.NotifyIconModeChanged();
        }

        public override Material Material
        {
            get
            {
                if (cachedMaterial == null && Faction != null)
                {
                    string path = Faction.def?.settlementTexturePath
                        ?? "World/WorldObjects/Settlements/Settlement";
                    Color tint = Faction.Color;
                    tint.a *= GlobeIconAlpha;
                    cachedMaterial = MaterialPool.MatFrom(
                        path,
                        ShaderDatabase.WorldOverlayTransparentLit,
                        tint,
                        WorldMaterials.WorldObjectRenderQueue);
                }
                return cachedMaterial ?? base.Material;
            }
        }

        public override Color ExpandingIconColor
        {
            get
            {
                Color c = Faction?.Color ?? Color.white;
                c.a *= GlobeIconAlpha;
                return c;
            }
        }

        // No ExpandingIconRotation override: art is drawn upright. If top-down turret art lands, mirror
        // WorldObject_AT_Turret (facing state + ExpandingIconRotation + north-art offset).

        public override string GetInspectString()
        {
            int tick = Find.TickManager?.TicksGame ?? 0;
            int version = WorldComponent_AdvancedBuildings.Get()?.Version ?? 0;
            if (cachedInspectString != null && tick - cachedInspectTick < 60 && version == cachedInspectVersion)
                return cachedInspectString;
            cachedInspectTick = tick;
            cachedInspectVersion = version;
            cachedInspectString = BuildInspectString();
            return cachedInspectString;
        }

        protected virtual string BuildInspectString()
        {
            var sb = new StringBuilder();
            Settlement owner = builtBySettlement;
            Faction fac = Faction ?? owner?.Faction;
            string factionName = fac?.Name ?? "?";
            if (fac != null)
                factionName = factionName.Colorize(fac.Color);
            sb.AppendLine("TSA_WD_AdvBuild_Inspect_Faction".Translate(factionName).ToString());

            if (IsOrphan)
                sb.AppendLine("TSA_WD_AdvBuild_Inspect_OwnerLost".Translate().ToString().Colorize(ColorLibrary.RedReadable));
            else
                sb.AppendLine("TSA_WD_AdvBuild_Inspect_BuiltBySettlement".Translate(owner.LabelCap).ToString());

            AppendInspectExtraLines(sb);

            int grace = ShellGraceTicksLeft;
            if (grace > 0)
            {
                float days = grace / (float)GenDate.TicksPerDay;
                sb.AppendLine("TSA_WD_AdvBuild_Inspect_ShellProtectionDays".Translate(days.ToString("F1")).ToString());
            }

            return sb.ToString().TrimEndNewlines();
        }

        /// <summary>Artillery adds Strength / Reloading; economic buildings add nothing.</summary>
        protected virtual void AppendInspectExtraLines(StringBuilder sb) { }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            if (!Find.WorldSelector.IsSelected(this)) return;
            SettlementOwnedAssetSelectionLines.DrawToOwner(this, builtBySettlement);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
                yield return g;
        }
    }
}
