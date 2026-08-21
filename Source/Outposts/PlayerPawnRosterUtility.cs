using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    public enum PawnRosterSkillHighlightMode
    {
        Off = 0,
        BestPerPawn = 1,
        Global0To20 = 2
    }

    public enum PlayerPawnLocationKind
    {
        Colony = 0,
        Outpost = 1,
        WorldCaravan = 2,
        PhysicalMap = 3,
        Camp = 4
    }

    public enum PlayerPawnOutpostRole
    {
        None,
        Occupant,
        StoredTransport,
        StoredMechanoid,
        StoredShuttle
    }

    public enum PlayerPawnSortCategory
    {
        Human = 0,
        Animal = 1,
        Mechanoid = 2,
        Vehicle = 3
    }

    public enum PlayerPawnTypeFilter
    {
        All = 0,
        Humanoid = 1,
        Animal = 2,
        Mechanoid = 3,
        Vehicle = 4
    }

    public enum PlayerPawnStarFilter
    {
        AllAnywhere = 0,
        StarredAnywhere = 1,
        NotStarredAnywhere = 2,
        AllColony = 3,
        StarredColony = 4,
        NotStarredColony = 5
    }

    public class PlayerPawnRosterEntry
    {
        public Pawn pawn = null!;
        public VirtualPawnSummary summary = null!;
        public PlayerPawnLocationKind locationKind;
        public PlayerPawnOutpostRole outpostRole;
        public PlayerPawnSortCategory pawnSortCategory;
        public string locationTypeLabel = "";
        public string locationTypeDefName = "";
        public string locationLabel = "";
        public string locationGroupKey = "";
        public int locationSortTier;
        public int colonySortIndex = int.MaxValue;
        public GlobalTargetInfo jumpTarget;
        public MapParent? mapParent;
        public WorldObject_WD_Outpost? sourceOutpost;
        public Caravan? sourceCaravan;
        public Texture2D? locationIcon;
        public Color locationIconColor = Color.white;
        public bool isMovable;
        public bool isSlave;
        public bool needsHealing;
        public bool isStarred;
        public string nameLabel = "";
        public string pawnTypeLabel = "";
        public int[] skillLevels = Array.Empty<int>();
        public string thingId = "";
        /// <summary>Biological age in whole years (sortable Age column).</summary>
        public int ageYears;
        /// <summary>Odyssey passenger shuttle when <see cref="outpostRole"/> is <see cref="PlayerPawnOutpostRole.StoredShuttle"/>.</summary>
        public Thing? shuttle;
    }

    public static class PlayerPawnRosterUtility
    {
        public const string DefaultSortColumn = "Default";

        public static readonly SkillDef[] AllSkillColumns =
        {
            SkillDefOf.Shooting,
            SkillDefOf.Melee,
            SkillDefOf.Plants,
            SkillDefOf.Animals,
            SkillDefOf.Construction,
            SkillDefOf.Social,
            SkillDefOf.Mining,
            SkillDefOf.Crafting,
            SkillDefOf.Intellectual,
            SkillDefOf.Cooking,
            SkillDefOf.Medicine,
            SkillDefOf.Artistic
        };

        private static readonly HashSet<string> IndexedThingIds = new HashSet<string>();
        private static readonly List<Pawn> MapVehicleAboardScratch = new List<Pawn>(16);
        private static readonly HashSet<Pawn> MapVehicleAboardSeen = new HashSet<Pawn>();

        public static List<PlayerPawnRosterEntry> BuildRoster(
            string? pawnNameSearchLower,
            string? locationNameSearchLower,
            string? locationTypeSearchLower,
            string? pawnTypeSearchLower,
            bool useDefaultGrouping,
            string sortColumn,
            bool sortAscending,
            PlayerPawnStarFilter starFilter = PlayerPawnStarFilter.AllAnywhere,
            PlayerPawnTypeFilter pawnTypeFilter = PlayerPawnTypeFilter.All)
        {
            var rows = new List<PlayerPawnRosterEntry>();
            IndexedThingIds.Clear();
            Faction player = Faction.OfPlayer;
            if (player == null) return rows;

            ScanColonies(rows, player);
            ScanCamps(rows, player);
            ScanPhysicalMaps(rows, player);
            ScanCaravans(rows, player);
            ScanOutposts(rows, player);

            if (!string.IsNullOrEmpty(pawnNameSearchLower))
            {
                rows.RemoveAll(e => e.nameLabel == null || !e.nameLabel.ToLowerInvariant().Contains(pawnNameSearchLower));
            }

            if (!string.IsNullOrEmpty(locationNameSearchLower))
            {
                rows.RemoveAll(e => !LocationNameMatches(e, locationNameSearchLower));
            }

            if (!string.IsNullOrEmpty(locationTypeSearchLower))
            {
                rows.RemoveAll(e => !LocationTypeMatches(e, locationTypeSearchLower));
            }

            if (pawnTypeFilter != PlayerPawnTypeFilter.All)
            {
                PlayerPawnSortCategory cat = ToSortCategory(pawnTypeFilter);
                rows.RemoveAll(e => e.pawnSortCategory != cat);
            }
            else if (!string.IsNullOrEmpty(pawnTypeSearchLower))
            {
                rows.RemoveAll(e => e.pawnTypeLabel == null || !e.pawnTypeLabel.ToLowerInvariant().Contains(pawnTypeSearchLower));
            }

            ApplyStarFilter(rows, starFilter);

            if (useDefaultGrouping || sortColumn == DefaultSortColumn)
                SortRowsDefault(rows);
            else
                SortRows(rows, sortColumn, sortAscending);

            return rows;
        }

        public static string StarFilterLabel(PlayerPawnStarFilter filter) =>
            StarFilterPrefix(filter) + StarFilterLabelText(filter);

        public static string StarFilterTip(PlayerPawnStarFilter filter) => filter switch
        {
            PlayerPawnStarFilter.StarredAnywhere => "TSA_WD_StarFilterTip_StarredAnywhere".Translate(),
            PlayerPawnStarFilter.NotStarredAnywhere => "TSA_WD_StarFilterTip_NotStarredAnywhere".Translate(),
            PlayerPawnStarFilter.AllColony => "TSA_WD_StarFilterTip_AllColony".Translate(),
            PlayerPawnStarFilter.StarredColony => "TSA_WD_StarFilterTip_StarredColony".Translate(),
            PlayerPawnStarFilter.NotStarredColony => "TSA_WD_StarFilterTip_NotStarredColony".Translate(),
            _ => "TSA_WD_StarFilterTip_AllAnywhere".Translate()
        };

        public static string OutpostStarFilterTip(OutpostPawnStarFilter filter) => filter switch
        {
            OutpostPawnStarFilter.Starred => "TSA_WD_OutpostStarFilterTip_Starred".Translate(),
            OutpostPawnStarFilter.NotStarred => "TSA_WD_OutpostStarFilterTip_NotStarred".Translate(),
            _ => "TSA_WD_OutpostStarFilterTip_All".Translate()
        };

        /// <summary>
        /// Anywhere: ★/☆ + label (no scope icon). Colony: player faction icon, then ★/☆ + label.
        /// </summary>
        public static FloatMenuOption MakeStarFilterMenuOption(PlayerPawnStarFilter filter, Action action)
        {
            if (IsColonyStarFilter(filter))
            {
                Texture2D house = Faction.OfPlayer?.def?.FactionIcon;
                if (house != null)
                {
                    string star = StarGlyph(filter);
                    string starOnly = star.NullOrEmpty()
                        ? StarFilterLabelText(filter)
                        : star + "  " + StarFilterLabelText(filter);
                    Color tint = Faction.OfPlayer?.Color ?? Color.white;
                    return new FloatMenuOption(starOnly, action, house, tint);
                }
            }
            return new FloatMenuOption(StarFilterLabel(filter), action);
        }

        public static string OutpostStarFilterLabel(OutpostPawnStarFilter filter) => filter switch
        {
            OutpostPawnStarFilter.Starred => "★  " + "TSA_WD_OutpostPawns_Filter_Starred".Translate(),
            OutpostPawnStarFilter.NotStarred => "☆  " + "TSA_WD_OutpostPawns_Filter_NotStarred".Translate(),
            _ => "TSA_WD_OutpostPawns_Filter_AllPawns".Translate()
        };

        private static bool IsColonyStarFilter(PlayerPawnStarFilter filter) =>
            filter == PlayerPawnStarFilter.AllColony
            || filter == PlayerPawnStarFilter.StarredColony
            || filter == PlayerPawnStarFilter.NotStarredColony;

        private static string StarFilterLabelText(PlayerPawnStarFilter filter) => filter switch
        {
            PlayerPawnStarFilter.StarredAnywhere => "TSA_WD_AllPlayerPawns_Filter_StarredAnywhere".Translate(),
            PlayerPawnStarFilter.NotStarredAnywhere => "TSA_WD_AllPlayerPawns_Filter_NotStarredAnywhere".Translate(),
            PlayerPawnStarFilter.AllColony => "TSA_WD_AllPlayerPawns_Filter_AllColony".Translate(),
            PlayerPawnStarFilter.StarredColony => "TSA_WD_AllPlayerPawns_Filter_StarredColony".Translate(),
            PlayerPawnStarFilter.NotStarredColony => "TSA_WD_AllPlayerPawns_Filter_NotStarredColony".Translate(),
            _ => "TSA_WD_AllPlayerPawns_Filter_AllAnywhere".Translate()
        };

        private static string StarGlyph(PlayerPawnStarFilter filter) => filter switch
        {
            PlayerPawnStarFilter.StarredAnywhere => "★",
            PlayerPawnStarFilter.StarredColony => "★",
            PlayerPawnStarFilter.NotStarredAnywhere => "☆",
            PlayerPawnStarFilter.NotStarredColony => "☆",
            _ => ""
        };

        private static string StarFilterPrefix(PlayerPawnStarFilter filter)
        {
            string star = StarGlyph(filter);
            // Closed filter button: no ∞. Colony uses ⌂ (float menu uses FactionIcon).
            if (IsColonyStarFilter(filter))
            {
                if (star.NullOrEmpty())
                    return "⌂  ";
                return "⌂ " + star + "  ";
            }
            if (star.NullOrEmpty())
                return "";
            return star + "  ";
        }

        private static void ApplyStarFilter(List<PlayerPawnRosterEntry> rows, PlayerPawnStarFilter filter)
        {
            if (filter == PlayerPawnStarFilter.AllAnywhere) return;
            rows.RemoveAll(e =>
            {
                bool colony = e.locationKind == PlayerPawnLocationKind.Colony;
                return filter switch
                {
                    PlayerPawnStarFilter.StarredAnywhere => !e.isStarred,
                    PlayerPawnStarFilter.NotStarredAnywhere => e.isStarred,
                    PlayerPawnStarFilter.AllColony => !colony,
                    PlayerPawnStarFilter.StarredColony => !colony || !e.isStarred,
                    PlayerPawnStarFilter.NotStarredColony => !colony || e.isStarred,
                    _ => false
                };
            });
        }

        private static bool LocationNameMatches(PlayerPawnRosterEntry e, string searchLower)
        {
            if (e.locationLabel != null && e.locationLabel.ToLowerInvariant().Contains(searchLower))
                return true;
            if (e.sourceOutpost != null && e.sourceOutpost.LabelCap.ToLowerInvariant().Contains(searchLower))
                return true;
            if (e.mapParent != null && e.mapParent.LabelCap.ToLowerInvariant().Contains(searchLower))
                return true;
            if (e.sourceCaravan != null && e.sourceCaravan.LabelCap.ToLowerInvariant().Contains(searchLower))
                return true;
            return false;
        }

        private static bool LocationTypeMatches(PlayerPawnRosterEntry e, string searchLower)
        {
            if (searchLower == PawnRosterHeaderFilter.LocationTypeOutpost.ToLowerInvariant())
                return e.locationKind == PlayerPawnLocationKind.Outpost;
            if (searchLower == PawnRosterHeaderFilter.LocationTypeColony.ToLowerInvariant())
                return e.locationKind == PlayerPawnLocationKind.Colony;
            if (searchLower == PawnRosterHeaderFilter.LocationTypeCaravan.ToLowerInvariant())
                return e.locationKind == PlayerPawnLocationKind.WorldCaravan;
            if (searchLower == PawnRosterHeaderFilter.LocationTypeCamp.ToLowerInvariant())
                return e.locationKind == PlayerPawnLocationKind.Camp;
            if (searchLower == PawnRosterHeaderFilter.LocationTypePhysicalMap.ToLowerInvariant())
                return e.locationKind == PlayerPawnLocationKind.PhysicalMap;
            if (!string.IsNullOrEmpty(e.locationTypeDefName)
                && e.locationTypeDefName.ToLowerInvariant().Contains(searchLower))
                return true;
            return e.locationTypeLabel != null && e.locationTypeLabel.ToLowerInvariant().Contains(searchLower);
        }

        private static void ScanPhysicalMaps(List<PlayerPawnRosterEntry> rows, Faction player)
        {
            var maps = Find.Maps;
            for (int mi = 0; mi < maps.Count; mi++)
            {
                Map map = maps[mi];
                if (map == null || !IsPhysicalMap(map)) continue;

                MapParent? parent = map.Parent;
                string baseLabel = parent?.LabelCap ?? "TSA_WD_AllPlayerPawns_PhysicalMapDefault".Translate();
                GlobalTargetInfo jump = parent != null ? new GlobalTargetInfo(parent) : new GlobalTargetInfo(map.Center, map);

                var pawns = map.mapPawns?.AllPawnsSpawned;
                if (pawns == null) continue;
                for (int pi = 0; pi < pawns.Count; pi++)
                {
                    Pawn p = pawns[pi];
                    if (p == null || p.Faction != player) continue;
                    string hostLabel = FormatPhysicalMapLabel(baseLabel);
                    var entry = new PlayerPawnRosterEntry
                    {
                        locationKind = PlayerPawnLocationKind.PhysicalMap,
                        locationTypeLabel = "TSA_WD_AllPlayerPawns_LocPhysicalMap".Translate(),
                        locationTypeDefName = "PhysicalMap",
                        locationLabel = hostLabel,
                        mapParent = parent,
                        jumpTarget = jump,
                        isMovable = false
                    };
                    TryAddToList(p, entry, rows);
                    TryAddAboardFromMapVehicle(
                        p,
                        player,
                        rows,
                        PlayerPawnLocationKind.PhysicalMap,
                        "TSA_WD_AllPlayerPawns_LocPhysicalMap".Translate(),
                        "PhysicalMap",
                        hostLabel,
                        parent,
                        jump,
                        colonySortIndex: 0);
                }
            }
        }

        private static bool IsPlayerColonyMapParent(MapParent? parent)
        {
            if (parent == null || !parent.HasMap || parent.Faction != Faction.OfPlayer) return false;
            if (Outpost_EstablishmentRequirements.IsActiveCamp(parent)) return false;
            if (parent is WorldObject_WD_Outpost) return false;
            if (parent.GetComponent<CompOutpostLogistics>() != null) return false;
            return parent.def?.defName != "TSA_WD_OutpostDefenseSite";
        }

        private static bool IsPlayerColonyMap(Map? map) =>
            map != null && IsPlayerColonyMapParent(map.Parent);

        private static void ScanColonies(List<PlayerPawnRosterEntry> rows, Faction player)
        {
            var colonyParents = new List<MapParent>();
            var allWo = Find.WorldObjects.AllWorldObjects;
            for (int wi = 0; wi < allWo.Count; wi++)
            {
                if (allWo[wi] is MapParent mp && IsPlayerColonyMapParent(mp))
                    colonyParents.Add(mp);
            }

            MapParent? primaryColony = Find.CurrentMap?.Parent;
            if (primaryColony != null && !IsPlayerColonyMapParent(primaryColony))
                primaryColony = null;

            colonyParents.Sort((a, b) => CompareColonyParents(a, b, primaryColony));

            for (int ci = 0; ci < colonyParents.Count; ci++)
            {
                MapParent mp = colonyParents[ci];
                Map map = mp.Map;
                if (map == null) continue;

                var pawns = map.mapPawns?.AllPawnsSpawned;
                if (pawns == null) continue;
                for (int pi = 0; pi < pawns.Count; pi++)
                {
                    Pawn p = pawns[pi];
                    if (p == null || p.Faction != player) continue;
                    string hostLabel = FormatColonyLabel(mp.LabelCap);
                    GlobalTargetInfo jump = new GlobalTargetInfo(mp);
                    var entry = new PlayerPawnRosterEntry
                    {
                        locationKind = PlayerPawnLocationKind.Colony,
                        locationTypeLabel = "TSA_WD_AllPlayerPawns_LocColony".Translate(),
                        locationTypeDefName = "Colony",
                        locationLabel = hostLabel,
                        mapParent = mp,
                        colonySortIndex = ci,
                        jumpTarget = jump,
                        isMovable = true
                    };
                    TryAddToList(p, entry, rows);
                    TryAddAboardFromMapVehicle(
                        p,
                        player,
                        rows,
                        PlayerPawnLocationKind.Colony,
                        "TSA_WD_AllPlayerPawns_LocColony".Translate(),
                        "Colony",
                        hostLabel,
                        mp,
                        jump,
                        colonySortIndex: ci);
                }
            }
        }

        private static void ScanCamps(List<PlayerPawnRosterEntry> rows, Faction player)
        {
            var allWo = Find.WorldObjects.AllWorldObjects;
            for (int wi = 0; wi < allWo.Count; wi++)
            {
                if (allWo[wi] is not MapParent mp) continue;
                if (!Outpost_EstablishmentRequirements.IsActiveCamp(mp)) continue;
                if (!mp.HasMap || mp.Map == null) continue;

                Map map = mp.Map;
                var pawns = map.mapPawns?.AllPawnsSpawned;
                if (pawns == null) continue;
                string hostLabel = FormatCampLabel(mp.LabelCap);
                GlobalTargetInfo jump = new GlobalTargetInfo(mp);
                for (int pi = 0; pi < pawns.Count; pi++)
                {
                    Pawn p = pawns[pi];
                    if (p == null || p.Faction != player) continue;
                    var entry = new PlayerPawnRosterEntry
                    {
                        locationKind = PlayerPawnLocationKind.Camp,
                        locationTypeLabel = "TSA_WD_AllPlayerPawns_LocCamp".Translate(),
                        locationTypeDefName = "Camp",
                        locationLabel = hostLabel,
                        mapParent = mp,
                        jumpTarget = jump,
                        isMovable = false
                    };
                    TryAddToList(p, entry, rows);
                    TryAddAboardFromMapVehicle(
                        p,
                        player,
                        rows,
                        PlayerPawnLocationKind.Camp,
                        "TSA_WD_AllPlayerPawns_LocCamp".Translate(),
                        "Camp",
                        hostLabel,
                        mp,
                        jump,
                        colonySortIndex: int.MaxValue);
                }
            }
        }

        /// <summary>
        /// Display-only: VF map vehicles despawn crew into holders; expand after the spawned vehicle shell.
        /// </summary>
        private static void TryAddAboardFromMapVehicle(
            Pawn vehicleOrPawn,
            Faction player,
            List<PlayerPawnRosterEntry> rows,
            PlayerPawnLocationKind locationKind,
            string locationTypeLabel,
            string locationTypeDefName,
            string hostLocationLabel,
            MapParent? mapParent,
            GlobalTargetInfo jumpTarget,
            int colonySortIndex)
        {
            if (!VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(vehicleOrPawn))
                return;

            MapVehicleAboardScratch.Clear();
            MapVehicleAboardSeen.Clear();
            VehicleFrameworkOutpostDissolveCompat.CollectPawnsAboardVehicleForRoster(
                vehicleOrPawn, MapVehicleAboardScratch, MapVehicleAboardSeen);

            string vehicleLabel = vehicleOrPawn.LabelCap;
            string aboardLocationLabel = $"{hostLocationLabel} ({vehicleLabel})";

            for (int i = 0; i < MapVehicleAboardScratch.Count; i++)
            {
                Pawn aboard = MapVehicleAboardScratch[i];
                if (aboard == null || aboard.Faction != player) continue;
                var entry = new PlayerPawnRosterEntry
                {
                    locationKind = locationKind,
                    locationTypeLabel = locationTypeLabel,
                    locationTypeDefName = locationTypeDefName,
                    locationLabel = aboardLocationLabel,
                    mapParent = mapParent,
                    colonySortIndex = colonySortIndex,
                    jumpTarget = jumpTarget,
                    isMovable = false
                };
                TryAddToList(aboard, entry, rows);
            }
        }

        private static int CompareColonyParents(MapParent a, MapParent b, MapParent? primaryColony)
        {
            if (primaryColony != null)
            {
                if (a == primaryColony && b != primaryColony) return -1;
                if (b == primaryColony && a != primaryColony) return 1;
            }
            return string.Compare(a.LabelCap, b.LabelCap, StringComparison.OrdinalIgnoreCase);
        }

        private static void ScanCaravans(List<PlayerPawnRosterEntry> rows, Faction player)
        {
            var allWo = Find.WorldObjects.AllWorldObjects;
            for (int wi = 0; wi < allWo.Count; wi++)
            {
                if (allWo[wi] is not Caravan caravan || caravan.Faction != player) continue;
                var pawns = caravan.PawnsListForReading;
                if (pawns == null) continue;
                string caravanLabel = caravan.LabelCap;
                for (int pi = 0; pi < pawns.Count; pi++)
                {
                    Pawn p = pawns[pi];
                    if (p == null) continue;
                    var entry = new PlayerPawnRosterEntry
                    {
                        locationKind = PlayerPawnLocationKind.WorldCaravan,
                        locationTypeLabel = "TSA_WD_AllPlayerPawns_LocCaravan".Translate(),
                        locationTypeDefName = "Caravan",
                        locationLabel = caravanLabel,
                        jumpTarget = new GlobalTargetInfo(caravan),
                        sourceCaravan = caravan,
                        isMovable = false
                    };
                    TryAddToList(p, entry, rows);
                }
            }
        }

        private static void ScanOutposts(List<PlayerPawnRosterEntry> rows, Faction player)
        {
            var allWo = Find.WorldObjects.AllWorldObjects;
            for (int wi = 0; wi < allWo.Count; wi++)
            {
                if (allWo[wi] is not WorldObject_WD_Outpost outpost || outpost.Faction != player) continue;
                string typeLabel = outpost.def?.LabelCap ?? "TSA_WD_AllPlayerPawns_LocOutpost".Translate();
                string outpostLabel = outpost.LabelCap;
                GlobalTargetInfo jump = new GlobalTargetInfo(outpost);

                AddOutpostPawnList(outpost.Occupants, PlayerPawnOutpostRole.Occupant);
                AddOutpostPawnList(outpost.StoredAnimalsAndVehicles, PlayerPawnOutpostRole.StoredTransport);
                AddOutpostPawnList(outpost.StoredMechanoids, PlayerPawnOutpostRole.StoredMechanoid);

                void AddOutpostPawnList(List<Pawn> list, PlayerPawnOutpostRole role)
                {
                    if (list == null) return;
                    for (int pi = 0; pi < list.Count; pi++)
                    {
                        Pawn p = list[pi];
                        var entry = new PlayerPawnRosterEntry
                        {
                            locationKind = PlayerPawnLocationKind.Outpost,
                            outpostRole = role,
                            locationTypeLabel = typeLabel,
                            locationTypeDefName = outpost.def?.defName ?? "",
                            locationLabel = outpostLabel,
                            jumpTarget = jump,
                            sourceOutpost = outpost,
                            isMovable = true
                        };
                        TryAddToList(p, entry, rows);
                    }
                }
            }
        }

        private static bool TryAddToList(Pawn pawn, PlayerPawnRosterEntry entry, List<PlayerPawnRosterEntry> rows)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return false;
            string tid = pawn.ThingID;
            if (string.IsNullOrEmpty(tid) || IndexedThingIds.Contains(tid)) return false;

            IndexedThingIds.Add(tid);
            entry.pawn = pawn;
            entry.thingId = tid;
            entry.summary = VirtualPawnSummary.FromPawn(pawn);
            entry.nameLabel = pawn.Name?.ToStringFull ?? pawn.LabelCap ?? pawn.Label ?? "—";
            entry.isSlave = OutpostPawnIdeologyUtil.IsSlaveHumanlike(pawn);
            entry.needsHealing = Outpost_OccupantProgression.OccupantShowsHurtIcon(pawn);
            entry.isStarred = WorldComponent_PlayerPawnFavorites.Get()?.IsStarred(tid) == true;
            entry.skillLevels = BuildSkillLevels(entry.summary);
            entry.ageYears = pawn.ageTracker != null ? pawn.ageTracker.AgeBiologicalYears : 0;
            entry.pawnSortCategory = ClassifyPawn(pawn, entry.outpostRole);
            entry.pawnTypeLabel = GetPawnTypeLabel(entry.pawnSortCategory);
            entry.locationSortTier = GetLocationSortTier(entry);
            entry.locationGroupKey = BuildLocationGroupKey(entry);
            PopulateLocationIcon(entry);
            rows.Add(entry);
            return true;
        }

        private static string FormatColonyLabel(string name) =>
            "TSA_WD_AllPlayerPawns_LocationColony".Translate(name);

        private static string FormatCampLabel(string name) =>
            name.NullOrEmpty() ? "TSA_WD_AllPlayerPawns_LocCamp".Translate() : name;

        private static string FormatPhysicalMapLabel(string name) =>
            "TSA_WD_AllPlayerPawns_LocationPhysicalMap".Translate(name);

        public static string FormatColonyLabelForDisplay(string name) => FormatColonyLabel(name);

        private static int GetLocationSortTier(PlayerPawnRosterEntry entry) =>
            entry.locationKind switch
            {
                PlayerPawnLocationKind.Colony => 0,
                PlayerPawnLocationKind.Outpost => 1,
                PlayerPawnLocationKind.Camp => 2,
                PlayerPawnLocationKind.WorldCaravan => 3,
                PlayerPawnLocationKind.PhysicalMap => 4,
                _ => 99
            };

        private static string BuildLocationGroupKey(PlayerPawnRosterEntry entry) =>
            entry.locationSortTier + "|" + entry.colonySortIndex + "|" + (entry.locationTypeLabel ?? "") + "|" + (entry.locationLabel ?? "");

        private static void PopulateLocationIcon(PlayerPawnRosterEntry entry)
        {
            switch (entry.locationKind)
            {
                case PlayerPawnLocationKind.Outpost:
                    if (entry.sourceOutpost?.def != null)
                    {
                        entry.locationIcon = entry.sourceOutpost.def.ExpandingIconTexture;
                        entry.locationIconColor = entry.sourceOutpost.Faction?.Color ?? Color.white;
                    }
                    break;
                case PlayerPawnLocationKind.Camp:
                    entry.locationIcon = ResolveCampLocationIcon(entry.mapParent);
                    entry.locationIconColor = Color.white;
                    break;
                default:
                    Faction player = Faction.OfPlayer;
                    if (player?.def?.FactionIcon != null)
                    {
                        entry.locationIcon = player.def.FactionIcon;
                        entry.locationIconColor = player.Color;
                    }
                    break;
            }
        }

        private static Texture2D? ResolveCampLocationIcon(MapParent? mapParent)
        {
            Texture2D? tex = mapParent?.ExpandingIcon;
            if (tex != null && tex != BaseContent.BadTex)
                return tex;

            WorldObjectDef? def = mapParent?.def ?? WorldObjectDefOf.Camp;
            tex = def?.ExpandingIconTexture;
            if (tex != null)
                return tex;

            return ContentFinder<Texture2D>.Get("World/WorldObjects/Expanding/Camp", false)
                ?? ContentFinder<Texture2D>.Get("World/WorldObjects/Camp", false);
        }

        /// <summary>Human → Animal → Mechanoid → Vehicle classification used by All Player Pawns and the outpost Pawns tab.</summary>
        public static PlayerPawnSortCategory ClassifyPawn(Pawn pawn, PlayerPawnOutpostRole role = PlayerPawnOutpostRole.None)
        {
            if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(pawn))
                return PlayerPawnSortCategory.Vehicle;
            if (role == PlayerPawnOutpostRole.StoredMechanoid || pawn.RaceProps?.IsMechanoid == true)
                return PlayerPawnSortCategory.Mechanoid;
            if (role == PlayerPawnOutpostRole.StoredTransport)
                return PlayerPawnSortCategory.Animal;
            if (pawn.RaceProps?.Humanlike == true)
                return PlayerPawnSortCategory.Human;
            if (pawn.RaceProps?.Animal == true)
                return PlayerPawnSortCategory.Animal;
            return PlayerPawnSortCategory.Animal;
        }

        private static int[] BuildSkillLevels(VirtualPawnSummary? summary)
        {
            var levels = new int[AllSkillColumns.Length];
            if (summary == null) return levels;
            for (int i = 0; i < AllSkillColumns.Length; i++)
                levels[i] = summary.GetSkill(AllSkillColumns[i]);
            return levels;
        }

        private const float PassionIconSize = 16f;
        private const float PassionIconGap = 2f;

        private static readonly PawnRosterSkillHighlightMode[] HighlightModeByWindow =
        {
            PawnRosterSkillHighlightMode.Off,
            PawnRosterSkillHighlightMode.Off,
            PawnRosterSkillHighlightMode.Off
        };

        private static readonly Color HeatDarkRed = new Color(0.55f, 0.08f, 0.08f, 0.42f);
        private static readonly Color HeatYellow = new Color(0.95f, 0.85f, 0.2f, 0.42f);
        private static readonly Color HeatLightGreen = new Color(0.55f, 0.9f, 0.45f, 0.42f);
        private static readonly Color HeatGreen = new Color(0.2f, 0.75f, 0.25f, 0.42f);
        private static readonly Color HeatLightPurple = new Color(0.72f, 0.55f, 0.92f, 0.42f);
        private static readonly Color HeatPurple = new Color(0.55f, 0.25f, 0.85f, 0.42f);
        private static readonly Color BestSkillHighlight = new Color(1f, 0.82f, 0.15f, 0.28f);

        private static readonly Color HighlightActiveTint = new Color(0.45f, 0.85f, 1f);

        /// <summary>Gap inside the Highlight / Columns / Restore cluster.</summary>
        public const float ViewControlsGap = 4f;

        private static int WindowIndex(PawnRosterColumnWindow window)
        {
            switch (window)
            {
                case PawnRosterColumnWindow.OutpostPawns: return 1;
                case PawnRosterColumnWindow.Prisoners: return 2;
                default: return 0;
            }
        }

        public static void ResetSkillDisplayOptions(PawnRosterColumnWindow window)
        {
            HighlightModeByWindow[WindowIndex(window)] = PawnRosterSkillHighlightMode.Off;
        }

        /// <summary>True when at least one full-skill column (Skill_*) is visible on the outpost pawns tab.</summary>
        public static bool AnyFullSkillColumnVisible(PawnRosterColumnWindow window)
        {
            SkillDef[] skills = AllSkillColumns;
            for (int i = 0; i < skills.Length; i++)
            {
                if (window == PawnRosterColumnWindow.OutpostPawns
                    && (skills[i] == SkillDefOf.Shooting || skills[i] == SkillDefOf.Melee))
                    continue;
                if (ColVisible(window, PawnRosterColumnIds.FullSkill(skills[i])))
                    return true;
            }
            return false;
        }

        public static bool ColVisible(PawnRosterColumnWindow window, string id)
        {
            WorldComponent_PawnRosterColumnPrefs prefs = WorldComponent_PawnRosterColumnPrefs.Get();
            return prefs == null || prefs.IsVisible(window, id);
        }

        public static void DrawSkillBlockSeparator(ref float curX, float y, float height)
        {
            curX += 6f;
            Color prev = GUI.color;
            GUI.color = Widgets.SeparatorLabelColor;
            Widgets.DrawLineVertical(curX, y + 2f, height - 4f);
            GUI.color = prev;
            curX += 6f;
        }

        public static int GetBestSkillLevel(int[] skillLevels)
        {
            int best = 0;
            if (skillLevels == null) return 0;
            for (int i = 0; i < skillLevels.Length; i++)
                if (skillLevels[i] > best) best = skillLevels[i];
            return best;
        }

        /// <summary>0 dark red, 7 yellow, 10 light green, 15 green; 15-18 light purple; 19-20 purple.</summary>
        public static Color GetGlobalSkillHeatColor(int level)
        {
            level = Mathf.Clamp(level, 0, 20);
            if (level >= 19) return HeatPurple;
            if (level >= 15) return HeatLightPurple;
            if (level <= 7)
                return Color.Lerp(HeatDarkRed, HeatYellow, level / 7f);
            if (level <= 10)
                return Color.Lerp(HeatYellow, HeatLightGreen, (level - 7) / 3f);
            return Color.Lerp(HeatLightGreen, HeatGreen, (level - 10) / 5f);
        }

        public static string SkillHighlightModeLabel(PawnRosterSkillHighlightMode mode)
        {
            switch (mode)
            {
                case PawnRosterSkillHighlightMode.BestPerPawn:
                    return "TSA_WD_PawnRoster_HighlightBest".Translate().ToString();
                case PawnRosterSkillHighlightMode.Global0To20:
                    return "TSA_WD_PawnRoster_HighlightGlobal".Translate().ToString();
                default:
                    return "TSA_WD_PawnRoster_HighlightOff".Translate().ToString();
            }
        }

        /// <summary>
        /// Draws Highlight + Columns + Restore as one row, packed to the left of <paramref name="rightX"/>.
        /// Returns the left edge of the cluster (Highlight icon).
        /// </summary>
        public static float DrawRosterViewControls(
            float y,
            float height,
            float rightX,
            PawnRosterColumnWindow window,
            Action onRestoreDefault,
            Action onColumns)
        {
            Text.Font = GameFont.Small;
            int idx = WindowIndex(window);
            float iconW = WorldDomination_UIUtils.RosterIconBtnSize;
            float gap = ViewControlsGap;

            Rect restoreBtn = new Rect(rightX - iconW, y, iconW, height);
            Rect columnsBtn = new Rect(restoreBtn.x - gap - iconW, y, iconW, height);
            Rect highlightBtn = new Rect(columnsBtn.x - gap - iconW, y, iconW, height);

            if (WorldDomination_UIUtils.ButtonIconOnly(
                restoreBtn,
                WorldDomination_UIUtils.RosterResetViewIcon,
                "TSA_WD_AllPlayerPawns_RestoreDefault".Translate()))
            {
                onRestoreDefault?.Invoke();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            Color? columnsTint = null;
            WorldComponent_PawnRosterColumnPrefs prefs = WorldComponent_PawnRosterColumnPrefs.Get();
            if (prefs != null && prefs.DiffersFromDefaults(window))
                columnsTint = HighlightActiveTint;
            if (WorldDomination_UIUtils.ButtonIconOnly(
                columnsBtn,
                WorldDomination_UIUtils.RosterColumnPickerIcon,
                "TSA_WD_PawnRoster_ColumnsToShow".Translate(),
                columnsTint))
            {
                onColumns?.Invoke();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            DrawHighlightModeControl(highlightBtn, idx);
            return highlightBtn.x;
        }

        private static void DrawHighlightModeControl(Rect btnRect, int idx)
        {
            PawnRosterSkillHighlightMode mode = HighlightModeByWindow[idx];
            bool active = mode != PawnRosterSkillHighlightMode.Off;
            string tooltip = "TSA_WD_PawnRoster_Highlight".Translate() + " " + SkillHighlightModeLabel(mode);
            Color? tint = active ? HighlightActiveTint : (Color?)null;
            if (WorldDomination_UIUtils.ButtonIconOnly(
                btnRect,
                WorldDomination_UIUtils.RosterHighlightIcon,
                tooltip,
                tint))
            {
                var options = new List<FloatMenuOption>
                {
                    new FloatMenuOption(SkillHighlightModeLabel(PawnRosterSkillHighlightMode.Off),
                        () => HighlightModeByWindow[idx] = PawnRosterSkillHighlightMode.Off),
                    new FloatMenuOption(SkillHighlightModeLabel(PawnRosterSkillHighlightMode.BestPerPawn),
                        () => HighlightModeByWindow[idx] = PawnRosterSkillHighlightMode.BestPerPawn),
                    new FloatMenuOption(SkillHighlightModeLabel(PawnRosterSkillHighlightMode.Global0To20),
                        () => HighlightModeByWindow[idx] = PawnRosterSkillHighlightMode.Global0To20)
                };
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        /// <summary>Centered skill level; passion flame after the number; optional best/global highlight.</summary>
        public static void DrawSkillLevelWithPassion(
            Rect cell,
            Pawn pawn,
            SkillDef skill,
            int level,
            bool isBestSkill = false,
            PawnRosterColumnWindow? window = null)
        {
            PawnRosterSkillHighlightMode mode = window.HasValue
                ? HighlightModeByWindow[WindowIndex(window.Value)]
                : PawnRosterSkillHighlightMode.Off;

            if (mode == PawnRosterSkillHighlightMode.BestPerPawn && isBestSkill && level > 0)
                Widgets.DrawBoxSolid(cell, BestSkillHighlight);
            else if (mode == PawnRosterSkillHighlightMode.Global0To20)
                Widgets.DrawBoxSolid(cell, GetGlobalSkillHeatColor(level));

            Texture2D passionIcon = null;
            if (pawn?.skills != null && skill != null)
            {
                SkillRecord rec = pawn.skills.GetSkill(skill);
                if (rec != null)
                {
                    if (rec.passion == Passion.Major) passionIcon = SkillUI.PassionMajorIcon;
                    else if (rec.passion == Passion.Minor) passionIcon = SkillUI.PassionMinorIcon;
                }
            }

            string levelText = level.ToString();
            Vector2 textSize = Text.CalcSize(levelText);
            float contentW = textSize.x;
            if (passionIcon != null)
                contentW += PassionIconSize + PassionIconGap;

            float startX = cell.x + Mathf.Max(0f, (cell.width - contentW) * 0.5f);
            float midY = cell.y + cell.height * 0.5f;

            TextAnchor prev = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(startX, cell.y, textSize.x + 2f, cell.height), levelText);
            Text.Anchor = prev;

            if (passionIcon != null)
            {
                float iconX = startX + textSize.x + PassionIconGap;
                Rect iconRect = new Rect(iconX, midY - PassionIconSize * 0.5f, PassionIconSize, PassionIconSize);
                GUI.DrawTexture(iconRect, passionIcon);
            }
        }

        public static string GetPawnTypeLabel(PlayerPawnSortCategory category) =>
            category switch
            {
                PlayerPawnSortCategory.Human => "TSA_WD_PawnType_Humanoid".Translate(),
                PlayerPawnSortCategory.Animal => "TSA_WD_PawnType_Animal".Translate(),
                PlayerPawnSortCategory.Mechanoid => "TSA_WD_PawnType_Mechanoid".Translate(),
                PlayerPawnSortCategory.Vehicle => "TSA_WD_PawnType_Vehicle".Translate(),
                _ => "—"
            };

        public static string TypeFilterLabel(PlayerPawnTypeFilter filter) => filter switch
        {
            PlayerPawnTypeFilter.Humanoid => "TSA_WD_PawnType_Humanoid".Translate(),
            PlayerPawnTypeFilter.Animal => "TSA_WD_PawnType_Animal".Translate(),
            PlayerPawnTypeFilter.Mechanoid => "TSA_WD_PawnType_Mechanoid".Translate(),
            PlayerPawnTypeFilter.Vehicle => "TSA_WD_PawnType_Vehicle".Translate(),
            _ => "TSA_WD_AllPlayerPawns_Filter_AllTypes".Translate()
        };

        public static PlayerPawnSortCategory ToSortCategory(PlayerPawnTypeFilter filter) => filter switch
        {
            PlayerPawnTypeFilter.Humanoid => PlayerPawnSortCategory.Human,
            PlayerPawnTypeFilter.Animal => PlayerPawnSortCategory.Animal,
            PlayerPawnTypeFilter.Mechanoid => PlayerPawnSortCategory.Mechanoid,
            PlayerPawnTypeFilter.Vehicle => PlayerPawnSortCategory.Vehicle,
            _ => PlayerPawnSortCategory.Human
        };

        private static bool IsPhysicalMap(Map map)
        {
            if (map == null || IsPlayerColonyMap(map)) return false;
            if (map.Parent?.def?.defName == "TSA_WD_OutpostDefenseSite") return true;
            return RapidResponseUtility.IsCaravanClashMap(map);
        }

        public static void SortRowsDefault(List<PlayerPawnRosterEntry> rows)
        {
            rows.Sort(CompareDefaultGroup);
        }

        private static int CompareDefaultGroup(PlayerPawnRosterEntry a, PlayerPawnRosterEntry b)
        {
            int tierCmp = a.locationSortTier.CompareTo(b.locationSortTier);
            if (tierCmp != 0) return tierCmp;

            if (a.locationKind == PlayerPawnLocationKind.Colony)
            {
                int colonyCmp = a.colonySortIndex.CompareTo(b.colonySortIndex);
                if (colonyCmp != 0) return colonyCmp;
            }

            if (a.locationKind == PlayerPawnLocationKind.Outpost)
            {
                int typeCmp = string.Compare(a.locationTypeLabel, b.locationTypeLabel, StringComparison.OrdinalIgnoreCase);
                if (typeCmp != 0) return typeCmp;
            }

            int locCmp = string.Compare(a.locationLabel, b.locationLabel, StringComparison.OrdinalIgnoreCase);
            if (locCmp != 0) return locCmp;

            int catCmp = ((int)a.pawnSortCategory).CompareTo((int)b.pawnSortCategory);
            if (catCmp != 0) return catCmp;

            return string.Compare(a.nameLabel, b.nameLabel, StringComparison.OrdinalIgnoreCase);
        }

        public static void SortRows(List<PlayerPawnRosterEntry> rows, string sortColumn, bool sortAscending)
        {
            rows.Sort((a, b) =>
            {
                int cmp = CompareEntries(a, b, sortColumn);
                return sortAscending ? cmp : -cmp;
            });
        }

        private static int CompareEntries(PlayerPawnRosterEntry a, PlayerPawnRosterEntry b, string sortColumn)
        {
            if (sortColumn == "LocationType")
                return string.Compare(a.locationTypeLabel, b.locationTypeLabel, StringComparison.OrdinalIgnoreCase);
            if (sortColumn == "LocationName")
                return string.Compare(a.locationLabel, b.locationLabel, StringComparison.OrdinalIgnoreCase);
            if (sortColumn == "PawnType")
                return string.Compare(a.pawnTypeLabel, b.pawnTypeLabel, StringComparison.OrdinalIgnoreCase);
            if (sortColumn == "Name")
                return string.Compare(a.nameLabel, b.nameLabel, StringComparison.OrdinalIgnoreCase);
            if (sortColumn == "Starred")
            {
                int as_ = a.isStarred ? 1 : 0;
                int bs = b.isStarred ? 1 : 0;
                return as_.CompareTo(bs);
            }
            if (sortColumn == "Age")
                return a.ageYears.CompareTo(b.ageYears);
            if (sortColumn == "Traits")
            {
                PrisonerRosterUtility.FormatTraits(a.pawn, out _, out string ta);
                PrisonerRosterUtility.FormatTraits(b.pawn, out _, out string tb);
                return string.Compare(ta, tb, StringComparison.OrdinalIgnoreCase);
            }
            if (sortColumn == "Xenotype")
                return PawnRosterTraitFilter.CompareXenotype(a.pawn, b.pawn);
            if (sortColumn == "Psycasts")
                return PawnRosterTraitFilter.ComparePsycasts(a.pawn, b.pawn);
            if (sortColumn == "Hurt")
            {
                int ah = a.needsHealing ? 1 : 0;
                int bh = b.needsHealing ? 1 : 0;
                return ah.CompareTo(bh);
            }

            for (int i = 0; i < AllSkillColumns.Length; i++)
            {
                if (sortColumn == AllSkillColumns[i].defName)
                {
                    int av = i < a.skillLevels.Length ? a.skillLevels[i] : 0;
                    int bv = i < b.skillLevels.Length ? b.skillLevels[i] : 0;
                    return av.CompareTo(bv);
                }
            }

            return CompareDefaultGroup(a, b);
        }

        /// <summary>
        /// True if <paramref name="thingId"/> was present in the last <see cref="BuildRoster"/> scan,
        /// before UI filters removed rows. Used to prune selection without dropping filtered-out pawns.
        /// </summary>
        public static bool WasInLastRosterScan(string thingId) =>
            !string.IsNullOrEmpty(thingId) && IndexedThingIds.Contains(thingId);

        /// <summary>
        /// Drop selection IDs that no longer exist in the world roster (not merely hidden by filters).
        /// Call after <see cref="BuildRoster"/> so <see cref="IndexedThingIds"/> is current.
        /// </summary>
        public static void PruneSelectionToLastScan(HashSet<string> selectedThingIds)
        {
            if (selectedThingIds == null || selectedThingIds.Count == 0) return;
            var drop = new List<string>();
            foreach (string id in selectedThingIds)
            {
                if (!WasInLastRosterScan(id))
                    drop.Add(id);
            }
            for (int i = 0; i < drop.Count; i++)
                selectedThingIds.Remove(drop[i]);
        }

        public static List<PlayerPawnRosterEntry> ResolveSelectedEntries(
            IReadOnlyList<PlayerPawnRosterEntry> allRows,
            HashSet<string> selectedThingIds)
        {
            var list = new List<PlayerPawnRosterEntry>();
            if (selectedThingIds == null || selectedThingIds.Count == 0) return list;
            if (allRows == null) return list;
            for (int i = 0; i < allRows.Count; i++)
            {
                PlayerPawnRosterEntry e = allRows[i];
                if (e.thingId != null && selectedThingIds.Contains(e.thingId))
                    list.Add(e);
            }
            return list;
        }

        /// <summary>
        /// Like <see cref="ResolveSelectedEntries"/>, but also includes selected pawns hidden by the
        /// current filter (rebuilds an unfiltered roster when needed). Use for action buttons, not every frame.
        /// </summary>
        public static List<PlayerPawnRosterEntry> ResolveSelectedEntriesIncludingHidden(
            IReadOnlyList<PlayerPawnRosterEntry> visibleRows,
            HashSet<string> selectedThingIds)
        {
            var list = ResolveSelectedEntries(visibleRows, selectedThingIds);
            if (selectedThingIds == null || selectedThingIds.Count == 0 || list.Count >= selectedThingIds.Count)
                return list;

            var full = BuildRoster(
                null, null, null, null,
                useDefaultGrouping: true,
                sortColumn: DefaultSortColumn,
                sortAscending: true,
                starFilter: PlayerPawnStarFilter.AllAnywhere,
                pawnTypeFilter: PlayerPawnTypeFilter.All);
            return ResolveSelectedEntries(full, selectedThingIds);
        }

        /// <summary>Build movable transfer entries for the selected pawns currently stored on one outpost.</summary>
        public static List<PlayerPawnRosterEntry> BuildTransferEntriesForOutpost(
            WorldObject_WD_Outpost outpost,
            HashSet<string> selectedThingIds)
        {
            var list = new List<PlayerPawnRosterEntry>();
            if (outpost == null || selectedThingIds == null || selectedThingIds.Count == 0)
                return list;

            string typeLabel = outpost.def?.LabelCap ?? "TSA_WD_AllPlayerPawns_LocOutpost".Translate();
            string outpostLabel = outpost.LabelCap;
            GlobalTargetInfo jump = new GlobalTargetInfo(outpost);

            void TryAdd(Pawn pawn, PlayerPawnOutpostRole role)
            {
                if (pawn?.ThingID == null || !selectedThingIds.Contains(pawn.ThingID)) return;
                if (pawn.Destroyed || pawn.Dead) return;
                var entry = new PlayerPawnRosterEntry
                {
                    locationKind = PlayerPawnLocationKind.Outpost,
                    outpostRole = role,
                    locationTypeLabel = typeLabel,
                    locationTypeDefName = outpost.def?.defName ?? "",
                    locationLabel = outpostLabel,
                    jumpTarget = jump,
                    sourceOutpost = outpost,
                    isMovable = true,
                    pawn = pawn,
                    thingId = pawn.ThingID,
                    summary = VirtualPawnSummary.FromPawn(pawn),
                    nameLabel = pawn.Name?.ToStringFull ?? pawn.LabelCap ?? pawn.Label ?? "—",
                    isSlave = OutpostPawnIdeologyUtil.IsSlaveHumanlike(pawn),
                    needsHealing = Outpost_OccupantProgression.OccupantShowsHurtIcon(pawn)
                };
                entry.skillLevels = BuildSkillLevels(entry.summary);
                entry.ageYears = pawn.ageTracker != null ? pawn.ageTracker.AgeBiologicalYears : 0;
                entry.pawnSortCategory = ClassifyPawn(pawn, role);
                entry.pawnTypeLabel = GetPawnTypeLabel(entry.pawnSortCategory);
                entry.locationSortTier = GetLocationSortTier(entry);
                entry.locationGroupKey = BuildLocationGroupKey(entry);
                PopulateLocationIcon(entry);
                list.Add(entry);
            }

            var occ = outpost.Occupants;
            if (occ != null)
            {
                for (int i = 0; i < occ.Count; i++)
                    TryAdd(occ[i], PlayerPawnOutpostRole.Occupant);
            }
            var stored = outpost.StoredAnimalsAndVehicles;
            if (stored != null)
            {
                for (int i = 0; i < stored.Count; i++)
                    TryAdd(stored[i], PlayerPawnOutpostRole.StoredTransport);
            }
            var mechs = outpost.StoredMechanoids;
            if (mechs != null)
            {
                for (int i = 0; i < mechs.Count; i++)
                    TryAdd(mechs[i], PlayerPawnOutpostRole.StoredMechanoid);
            }

            var shuttles = outpost.StoredPassengerShuttles;
            if (shuttles != null)
            {
                for (int i = 0; i < shuttles.Count; i++)
                {
                    Thing shuttle = shuttles[i];
                    if (shuttle?.ThingID == null || !selectedThingIds.Contains(shuttle.ThingID))
                        continue;
                    if (shuttle.Destroyed || !OdysseyShuttleOutpostEstablishmentCompat.IsPassengerShuttle(shuttle))
                        continue;

                    var entry = new PlayerPawnRosterEntry
                    {
                        locationKind = PlayerPawnLocationKind.Outpost,
                        outpostRole = PlayerPawnOutpostRole.StoredShuttle,
                        locationTypeLabel = typeLabel,
                        locationTypeDefName = outpost.def?.defName ?? "",
                        locationLabel = outpostLabel,
                        jumpTarget = jump,
                        sourceOutpost = outpost,
                        isMovable = true,
                        pawn = null!,
                        shuttle = shuttle,
                        thingId = shuttle.ThingID,
                        summary = null!,
                        nameLabel = shuttle.LabelCap ?? shuttle.Label ?? "—",
                        isSlave = false,
                        needsHealing = false,
                        skillLevels = Array.Empty<int>(),
                        pawnSortCategory = PlayerPawnSortCategory.Vehicle,
                        pawnTypeLabel = GetPawnTypeLabel(PlayerPawnSortCategory.Vehicle)
                    };
                    entry.locationSortTier = GetLocationSortTier(entry);
                    entry.locationGroupKey = BuildLocationGroupKey(entry);
                    PopulateLocationIcon(entry);
                    list.Add(entry);
                }
            }
            return list;
        }

        /// <summary>
        /// Smart-assign closest matching outposts for free pawns (current-tile geography).
        /// Returns assignments ready for batched transfer; failed = unmatched / not travel-ready / no skills.
        /// </summary>
        public static List<PlayerPawnTransferUtility.PlayerPawnTransferAssignment> SmartAssignDestinations(
            List<PlayerPawnRosterEntry> selected,
            out int failed)
        {
            failed = 0;
            var result = new List<PlayerPawnTransferUtility.PlayerPawnTransferAssignment>();
            if (selected == null || selected.Count == 0) return result;

            Dictionary<SkillDef, List<WorldObject_WD_Outpost>> bySkill = SmartAssignOutpostUtility.BuildOutpostsByRelevantSkill();
            if (bySkill.Count == 0)
            {
                failed = selected.Count;
                return result;
            }

            for (int i = 0; i < selected.Count; i++)
            {
                PlayerPawnRosterEntry entry = selected[i];
                if (!PlayerPawnTransferUtility.IsMovableTransferEntry(entry))
                {
                    failed++;
                    continue;
                }
                if (!PlayerPawnTransferUtility.IsCapableOfImmediateTransfer(entry.pawn))
                {
                    failed++;
                    continue;
                }

                PlanetTile fromTile = default;
                if (entry.sourceOutpost != null && entry.sourceOutpost.Tile.Valid)
                    fromTile = entry.sourceOutpost.Tile;
                else if (entry.mapParent != null && entry.mapParent.Tile.Valid)
                    fromTile = entry.mapParent.Tile;
                else
                {
                    failed++;
                    continue;
                }

                if (!SmartAssignOutpostUtility.TryFindSmartAssignOutpost(
                        entry.pawn,
                        fromTile,
                        bySkill,
                        entry.sourceOutpost,
                        out WorldObject_WD_Outpost? dest)
                    || dest == null)
                {
                    failed++;
                    continue;
                }

                result.Add(new PlayerPawnTransferUtility.PlayerPawnTransferAssignment
                {
                    entry = entry,
                    destination = new PlayerPawnTransferDestination
                    {
                        kind = PlayerPawnTransferDestinationKind.Outpost,
                        outpost = dest
                    }
                });
            }

            return result;
        }
    }
}
