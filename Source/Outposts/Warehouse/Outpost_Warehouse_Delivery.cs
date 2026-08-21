using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Outpost_Warehouse_Delivery
    {
        public static bool IsWarehouseOutpost(WorldObject_WD_Outpost outpost) =>
            outpost != null && Outpost_Production_Utils.IsWarehouseOutpost(outpost.def);

        /// <summary>Outposts that ship items via <see cref="WorldActions_Traveler.SpawnOutpostDeliveryTraveler"/>.</summary>
        public static bool UsesItemDeliveryTraveler(WorldObjectDef def)
        {
            if (def == null) return false;
            if (Outpost_Production_Utils.IsWarehouseOutpost(def)) return false;
            if (Outpost_Production_Utils.IsMortarOutpost(def)) return false;
            if (Outpost_Production_Utils.IsRapidResponseOutpost(def)) return false;
            if (Outpost_Production_Utils.IsAcademyOutpost(def)) return false;
            if (Outpost_Production_Utils.IsResearchOutpost(def)) return false;
            if (Outpost_Production_Utils.IsPowerPlantOutpost(def)) return false;
            if (Outpost_Production_Utils.IsRecruitingOutpost(def)) return false;
            if (Outpost_Production_Utils.IsEmbassyOutpost(def)) return false;
            return true;
        }

        public static bool IsValidItemDeliveryDestination(WorldObject target, WorldObject_WD_Outpost sender = null)
        {
            if (target == null || target.Destroyed || !target.Spawned) return false;
            if (WorldActions_Utils.IsSpace(target)) return false;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(target.Tile)) return false;

            if (target is WorldObject_WD_Outpost wh && IsWarehouseOutpost(wh))
                return wh.Faction == Faction.OfPlayer && wh != sender;

            if (target is MapParent mp && mp.Faction == Faction.OfPlayer && mp.HasMap)
                return true;

            return false;
        }

        public static bool TryResolveDeliveryTarget(WorldObject_WD_Outpost sender, out WorldObject target)
        {
            target = null;
            if (sender == null) return false;

            if (sender.itemDeliveryTargetWorldObjectId >= 0)
            {
                WorldObject explicitTarget = Find.WorldObjects.AllWorldObjects.Find(
                    o => o != null && o.ID == sender.itemDeliveryTargetWorldObjectId);
                if (IsValidItemDeliveryDestination(explicitTarget, sender))
                {
                    target = explicitTarget;
                    return true;
                }
            }

            return TryFindNearestPlayerColony(sender.Tile, out target);
        }

        public static bool TryFindNearestPlayerColony(int fromTile, out WorldObject target)
        {
            target = null;
            MapParent best = null;
            float bestDist = float.MaxValue;
            var maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map == null || !map.IsPlayerHome) continue;
                if (!(map.Parent is MapParent mp) || !mp.HasMap) continue;
                if (WorldActions_Utils.IsSpace(mp)) continue;
                float d = Find.WorldGrid.ApproxDistanceInTiles(fromTile, mp.Tile);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = mp;
                }
            }
            target = best;
            return target != null;
        }

        public static string GetDestinationLabel(WorldObject target)
        {
            if (target == null) return "TSA_WD_Warehouse_DestNone".Translate();
            return target.LabelCap;
        }

        /// <summary>Label with destination kind for warehouse ship UI, e.g. "Base (Colony)" or "Depot (Warehouse Outpost)".</summary>
        public static string GetDestinationLabelWithKind(WorldObject target)
        {
            if (target == null) return "TSA_WD_Warehouse_DestNone".Translate();
            string name = target.LabelCap;
            if (target is WorldObject_WD_Outpost wo && IsWarehouseOutpost(wo))
                return name + " (" + "TSA_WD_Warehouse_DestKind_Warehouse".Translate() + ")";
            if (target is MapParent mp && mp.Faction == Faction.OfPlayer && mp.HasMap)
                return name + " (" + "TSA_WD_Warehouse_DestKind_Colony".Translate() + ")";
            return name;
        }

        private static Texture2D cachedDeliveryMouseIcon;
        /// <summary>Legacy flag; delivery targeting no longer draws a tinted overlay.</summary>
        public static bool CyanDeliveryMouseOverlayActive;

        /// <summary>Delivery destination command / world-targeter mouse icon (untinted).</summary>
        public static Texture2D GetDeliveryTargetMouseIcon()
        {
            if (cachedDeliveryMouseIcon != null) return cachedDeliveryMouseIcon;
            cachedDeliveryMouseIcon = ContentFinder<Texture2D>.Get("UI/Commands/DeliveryDestination", false)
                ?? ContentFinder<Texture2D>.Get("UI/Commands/DeliveryTarget", false)
                ?? ContentFinder<Texture2D>.Get("WorldObjects/Caravan_OutpostGoods", false)
                ?? ContentFinder<Texture2D>.Get("WorldObjects/WD_Outpost_Warehouse", false)
                ?? TexCommand.Attack;
            return cachedDeliveryMouseIcon;
        }

        /// <summary>No-op: delivery destination icon is shown untinted by the world targeter.</summary>
        public static void DrawCyanDeliveryMouseOverlayIfActive()
        {
            if (CyanDeliveryMouseOverlayActive
                && (Find.WorldTargeter == null || !Find.WorldTargeter.IsTargeting))
            {
                CyanDeliveryMouseOverlayActive = false;
            }
        }

        public static WorldObject ResolveExplicitDeliveryTarget(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.itemDeliveryTargetWorldObjectId < 0) return null;
            WorldObject wo = Find.WorldObjects.AllWorldObjects.Find(
                o => o != null && o.ID == outpost.itemDeliveryTargetWorldObjectId);
            return IsValidItemDeliveryDestination(wo, outpost) ? wo : null;
        }

        /// <summary>Destination for UI: explicit target when set, otherwise nearest player colony (default cycle shipping).</summary>
        public static WorldObject ResolveDisplayDeliveryTarget(WorldObject_WD_Outpost outpost)
        {
            WorldObject explicitDest = ResolveExplicitDeliveryTarget(outpost);
            if (explicitDest != null) return explicitDest;
            TryResolveDeliveryTarget(outpost, out WorldObject resolved);
            return resolved;
        }

        /// <summary>While delivery/ship destination gizmos are hovered, skip food logistics overlays (next world frame inclusive).</summary>
        private static int hideFoodLogisticsOverlayUntilFrame = -1;

        public static bool ShouldHideFoodLogisticsOverlay =>
            Time.frameCount <= hideFoodLogisticsOverlayUntilFrame;

        /// <summary>Purple line to explicit delivery/ship destination while hovering the destination gizmo.</summary>
        public static void DrawHoverOverlayLines(WorldObject_WD_Outpost outpost)
        {
            hideFoodLogisticsOverlayUntilFrame = Time.frameCount + 1;
            WD_RadiusOverlayPrefs.NotifySuppressFillThisFrame();
            if (outpost == null) return;

            if (Outpost_Production_Utils.IsWarehouseOutpost(outpost.def))
            {
                var wh = CompOutpostWarehouse.Get(outpost);
                WorldObject shipDest = wh?.ResolveShipDestination();
                if (shipDest != null && IsValidItemDeliveryDestination(shipDest, outpost))
                    DrawDeliveryLine(outpost.Tile, shipDest.Tile);
                return;
            }

            if (!UsesItemDeliveryTraveler(outpost.def)) return;
            WorldObject deliveryDest = ResolveDisplayDeliveryTarget(outpost);
            if (deliveryDest != null)
                DrawDeliveryLine(outpost.Tile, deliveryDest.Tile);
        }

        private static void DrawDeliveryLine(int startTile, int endTile)
        {
            GenDraw_WorldLineSmooth.DrawSmoothWorldLine(
                startTile,
                endTile,
                Find.WorldGrid,
                WorldOverlayLineMaterials.RecruitRedirectLine,
                1f,
                GenDraw_WorldLineSmooth.GetPathLineLift());
        }

        /// <summary>Start world targeting and open a float menu of valid colonies / warehouses.</summary>
        public static void BeginItemDeliveryDestinationChoice(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return;
            BeginItemDeliveryTargetTargeting(outpost);
            ShowDeliveryDestinationFloatMenu(outpost, chosen =>
            {
                outpost.itemDeliveryTargetWorldObjectId = chosen.ID;
                Messages.Message("TSA_WD_Warehouse_DeliveryDestSet".Translate(chosen.LabelCap), outpost, MessageTypeDefOf.PositiveEvent);
                StopDeliveryTargeting();
            });
        }

        /// <summary>Start world targeting and open a float menu of valid colonies / warehouses for warehouse shipments.</summary>
        public static void BeginShipDestinationChoice(CompOutpostWarehouse warehouseComp, WorldObject_WD_Outpost warehouse)
        {
            if (warehouseComp == null || warehouse == null) return;
            BeginShipDestinationTargeting(warehouseComp, warehouse);
            ShowDeliveryDestinationFloatMenu(warehouse, chosen =>
            {
                warehouseComp.shipDestinationWorldObjectId = chosen.ID;
                Messages.Message("TSA_WD_Warehouse_ShipDestSet".Translate(chosen.LabelCap), warehouse, MessageTypeDefOf.PositiveEvent);
                StopDeliveryTargeting();
            });
        }

        public static void BeginItemDeliveryTargetTargeting(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return;
            CameraJumper.TryJump(outpost.Tile);
            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    WorldObject wo = target.WorldObject;
                    if (!IsValidItemDeliveryDestination(wo, outpost))
                    {
                        Messages.Message("TSA_WD_Warehouse_InvalidDestination".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }
                    outpost.itemDeliveryTargetWorldObjectId = wo.ID;
                    Messages.Message("TSA_WD_Warehouse_DeliveryDestSet".Translate(wo.LabelCap), outpost, MessageTypeDefOf.PositiveEvent);
                    return true;
                },
                true,
                GetDeliveryTargetMouseIcon(),
                false,
                null,
                null);
        }

        public static void BeginShipDestinationTargeting(CompOutpostWarehouse warehouseComp, WorldObject_WD_Outpost warehouse)
        {
            if (warehouseComp == null || warehouse == null) return;
            CameraJumper.TryJump(warehouse.Tile);
            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    WorldObject wo = target.WorldObject;
                    if (!IsValidItemDeliveryDestination(wo, warehouse))
                    {
                        Messages.Message("TSA_WD_Warehouse_InvalidDestination".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }
                    warehouseComp.shipDestinationWorldObjectId = wo.ID;
                    Messages.Message("TSA_WD_Warehouse_ShipDestSet".Translate(wo.LabelCap), warehouse, MessageTypeDefOf.PositiveEvent);
                    return true;
                },
                true,
                GetDeliveryTargetMouseIcon(),
                false,
                null,
                null);
        }

        private static void StopDeliveryTargeting()
        {
            CyanDeliveryMouseOverlayActive = false;
            if (Find.WorldTargeter != null && Find.WorldTargeter.IsTargeting)
                Find.WorldTargeter.StopTargeting();
        }

        private static void ShowDeliveryDestinationFloatMenu(WorldObject_WD_Outpost sender, Action<WorldObject> onChosen)
        {
            if (sender == null || onChosen == null) return;

            var destinations = CollectValidItemDeliveryDestinations(sender);
            if (destinations.Count == 0) return;

            int currentId = -1;
            if (Outpost_Production_Utils.IsWarehouseOutpost(sender.def))
            {
                var wh = CompOutpostWarehouse.Get(sender);
                if (wh != null) currentId = wh.shipDestinationWorldObjectId;
            }
            else
            {
                currentId = sender.itemDeliveryTargetWorldObjectId;
            }

            var options = new List<FloatMenuOption>(destinations.Count);
            for (int i = 0; i < destinations.Count; i++)
            {
                WorldObject dest = destinations[i];
                string label = GetDestinationLabelWithKind(dest);
                if (dest.ID == currentId)
                    label = "TSA_WD_Warehouse_DeliveryDestCurrent".Translate(label).ToString();

                Texture2D icon = GetDestinationMenuIcon(dest);
                WorldObject captured = dest;
                options.Add(new FloatMenuOption(label, () => onChosen(captured), icon, Color.cyan));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>Player colonies and warehouse outposts valid as delivery destinations for <paramref name="sender"/>, nearest first.</summary>
        public static List<WorldObject> CollectValidItemDeliveryDestinations(WorldObject_WD_Outpost sender)
        {
            var list = new List<WorldObject>();
            if (sender == null) return list;

            var maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map?.Parent == null) continue;
                if (IsValidItemDeliveryDestination(map.Parent, sender))
                    list.Add(map.Parent);
            }

            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_WD_Outpost wo
                    && IsWarehouseOutpost(wo)
                    && IsValidItemDeliveryDestination(wo, sender)
                    && !list.Contains(wo))
                {
                    list.Add(wo);
                }
            }

            int fromTile = sender.Tile;
            list.Sort((a, b) =>
            {
                float da = Find.WorldGrid.ApproxDistanceInTiles(fromTile, a.Tile);
                float db = Find.WorldGrid.ApproxDistanceInTiles(fromTile, b.Tile);
                int cmp = da.CompareTo(db);
                if (cmp != 0) return cmp;
                return string.CompareOrdinal(a.LabelCap, b.LabelCap);
            });
            return list;
        }

        private static Texture2D GetDestinationMenuIcon(WorldObject destination)
        {
            if (destination is WorldObject_WD_Outpost outpost && outpost.def?.ExpandingIconTexture != null)
                return outpost.def.ExpandingIconTexture;
            if (destination?.Faction?.def?.FactionIcon != null)
                return destination.Faction.def.FactionIcon;
            if (destination?.ExpandingIcon != null)
                return destination.ExpandingIcon;
            return GetDeliveryTargetMouseIcon();
        }
    }
}
