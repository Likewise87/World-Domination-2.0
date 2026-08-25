using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>One row in the assault artillery outpost picker.</summary>
    public sealed class AssaultArtilleryOutpostEntry
    {
        public WorldObject_WD_Outpost? Outpost;
        public bool IsGhostRelay;
        public float DistanceTiles;
        public int EtaTicks;
        public int ScatterRadius;
        public bool OnCooldown;
        public bool NoShootingSkill;
        public float CooldownDaysLeft;
        public WD_AssaultArtillerySupport.ShellUnlockTier Tier;
        /// <summary>Prebuilt at menu refresh - do not Translate every GUI frame.</summary>
        public string CachedEtaSpread = string.Empty;
        public string CachedTip = string.Empty;

        public bool IsSelectable =>
            IsGhostRelay || (Outpost != null && !OnCooldown && !NoShootingSkill);

        public string DisplayLabel =>
            IsGhostRelay
                ? "TSA_WD_AssaultArtillery_GhostRelay".Translate().ToString()
                : Outpost?.LabelCap ?? string.Empty;

        public Texture2D? DisplayIcon
        {
            get
            {
                if (IsGhostRelay)
                    return ContentFinder<Texture2D>.Get("UI/Tab/WD", false);
                return Outpost?.def?.ExpandingIconTexture;
            }
        }

        public float EtaSeconds => EtaTicks / 60f;
    }

    /// <summary>Partitioned outpost list for assault artillery UI.</summary>
    public sealed class AssaultArtilleryMenuData
    {
        public readonly List<AssaultArtilleryOutpostEntry> GhostRelay = new List<AssaultArtilleryOutpostEntry>(1);
        public readonly List<AssaultArtilleryOutpostEntry> Ready = new List<AssaultArtilleryOutpostEntry>();
        public readonly List<AssaultArtilleryOutpostEntry> Disabled = new List<AssaultArtilleryOutpostEntry>();

        public bool HasSelectableOutpost
        {
            get
            {
                if (GhostRelay.Count > 0 && GhostRelay[0].IsSelectable) return true;
                return Ready.Count > 0;
            }
        }

        public AssaultArtilleryOutpostEntry? FirstSelectable
        {
            get
            {
                if (Ready.Count > 0) return Ready[0];
                if (GhostRelay.Count > 0 && GhostRelay[0].IsSelectable) return GhostRelay[0];
                return null;
            }
        }
    }

    /// <summary>Assault-map artillery support: outpost query, shell unlock tiers, targeter, issue (CD + enqueue).</summary>
    [StaticConstructorOnStartup]
    public static class WD_AssaultArtillerySupport
    {
        public const string ImprovedShellsDefName = "TSA_WD_Upgrade_ImprovedMortarShells";
        public const string AdvancedShellsDefName = "TSA_WD_Upgrade_AdvancedMortarShells";
        public const string ShellHighExplosive = "Shell_HighExplosive";
        public const string ShellHighExplosiveAirburst = "Shell_HighExplosive_HFuzed";
        public const string ShellSmoke = "Shell_Smoke";
        public const string ShellAntigrain = "Shell_AntigrainWarhead";

        public const int ShellCount = 5;
        public const int AntigrainShellCount = 1;
        public const int ShellIntervalMinTicks = 20;
        public const int ShellIntervalMaxTicks = 70;
        public const int ScatterMin = 4;
        public const int ScatterMax = 40;
        /// <summary>~10s at 60 tps. GUI must not rebuild nearby-outpost scans every frame.</summary>
        public const int MenuRefreshIntervalTicks = 600;

        private static AssaultArtilleryMenuData? cachedMenu;
        private static int cachedMenuSettlementId = -1;
        private static int cachedMenuTick = int.MinValue;
        private static bool cachedCanOpen;

        // Mortar shell defs are static for the process - discover once, never on GUI/tooltip paths.
        private static readonly List<ThingDef> EmptyShellList = new List<ThingDef>();
        private static bool shellCatalogReady;
        private static List<ThingDef> cachedShellsAll = EmptyShellList;
        private static List<ThingDef> cachedShellsBasic = EmptyShellList;
        private static List<ThingDef> cachedShellsImproved = EmptyShellList;
        private static List<ThingDef> cachedShellsAdvanced = EmptyShellList;
        private static string cachedTooltipBasic = string.Empty;
        private static string cachedTooltipImproved = string.Empty;
        private static string cachedTooltipAdvanced = string.Empty;
        private static Texture2D? cachedImprovedUpgradeIcon;
        private static Texture2D? cachedAdvancedUpgradeIcon;

        public enum ShellUnlockTier : byte
        {
            Basic = 0,
            Improved = 1,
            Advanced = 2
        }

        public static bool IsUnlimitedMode =>
            WorldDominationMod.settings?.experimentalUnlimitedAssaultMortarSupport == true;

        public static void InvalidateMenuCache()
        {
            cachedMenuTick = int.MinValue;
            cachedMenu = null;
            cachedMenuSettlementId = -1;
            cachedCanOpen = false;
        }

        /// <summary>Throttled menu for GUI. Force rebuild when <paramref name="force"/> (dialog open) or after invalidate.</summary>
        public static AssaultArtilleryMenuData GetAssaultArtilleryMenuCached(Settlement? settlement, bool force = false)
        {
            if (settlement == null)
            {
                InvalidateMenuCache();
                return new AssaultArtilleryMenuData();
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            int settlementId = settlement.ID;
            int delta = tick - cachedMenuTick;
            if (!force
                && cachedMenu != null
                && cachedMenuSettlementId == settlementId
                && delta >= 0
                && delta < MenuRefreshIntervalTicks)
                return cachedMenu;

            cachedMenu = BuildAssaultArtilleryMenu(settlement);
            cachedMenuSettlementId = settlementId;
            cachedMenuTick = tick;
            cachedCanOpen = cachedMenu.HasSelectableOutpost
                || cachedMenu.Disabled.Count > 0
                || cachedMenu.GhostRelay.Count > 0;
            return cachedMenu;
        }

        public static bool IsEligibleAssaultMap(Map map)
        {
            if (map?.Parent is not Settlement settlement) return false;
            if (settlement.Faction == null || Faction.OfPlayer == null) return false;
            return settlement.Faction.HostileTo(Faction.OfPlayer);
        }

        public static Settlement? GetAssaultedSettlement(Map? map) =>
            map?.Parent as Settlement;

        /// <summary>Antigrain fires a single shell; all other assault shells use <see cref="ShellCount"/>.</summary>
        public static int GetVolleyShellCount(ThingDef? shellDef)
        {
            if (shellDef != null && shellDef.defName == ShellAntigrain)
                return AntigrainShellCount;
            return ShellCount;
        }

        public static int GetShellEtaTicks(float distanceTiles) =>
            Mathf.RoundToInt(distanceTiles * WorldActions_Traveler.GetMortarShellTicksPerMove());

        /// <summary>Assault-map spread only - not world-map hit logic.</summary>
        public static int GetAssaultScatterRadius(WorldObject_WD_Outpost? outpost, float distanceTiles, float maxRangeTiles)
        {
            if (outpost == null) return ScatterMin;

            float distFrac = Mathf.Clamp01(distanceTiles / Mathf.Max(1f, maxRangeTiles));
            int baseSpread = Mathf.RoundToInt(Mathf.Lerp(ScatterMin, ScatterMax, distFrac));
            float bestShooting = outpost.GetHighestVirtualPawnSkill(SkillDefOf.Shooting);
            int skillTrim = Mathf.RoundToInt(bestShooting * 0.3f);
            int upgradeTrim = Mathf.RoundToInt(outpost.GetBuiltUpgradeMortarHitChanceBonus() * 20f);
            return Mathf.Clamp(baseSpread - skillTrim - upgradeTrim, ScatterMin, ScatterMax);
        }

        public static ShellUnlockTier GetBestGlobalShellTier()
        {
            ShellUnlockTier best = ShellUnlockTier.Basic;
            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
            {
                WorldObject_WD_Outpost op = outposts[i];
                if (op == null || op.Destroyed || !op.IsMortarOutpost) continue;
                ShellUnlockTier t = GetShellTier(op);
                if (t > best) best = t;
            }
            return best;
        }

        /// <summary>Prefer <see cref="GetAssaultArtilleryMenuCached"/> for GUI; this always rebuilds.</summary>
        public static AssaultArtilleryMenuData GetAssaultArtilleryMenu(Settlement? settlement) =>
            BuildAssaultArtilleryMenu(settlement);

        private static AssaultArtilleryMenuData BuildAssaultArtilleryMenu(Settlement? settlement)
        {
            var data = new AssaultArtilleryMenuData();
            if (settlement == null) return data;

            if (IsUnlimitedMode)
            {
                float ghostDist = 0f;
                WorldObject? ghostOrigin = ResolveGhostRelayOrigin(settlement, out ghostDist);
                var ghost = new AssaultArtilleryOutpostEntry
                {
                    IsGhostRelay = true,
                    Tier = GetBestGlobalShellTier(),
                    ScatterRadius = ScatterMin,
                    EtaTicks = ghostOrigin != null ? GetShellEtaTicks(ghostDist) : 0,
                    DistanceTiles = ghostDist
                };
                FillCachedEntryStrings(ghost);
                data.GhostRelay.Add(ghost);
            }

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            int targetTile = settlement.Tile;
            var inRange = new List<(WorldObject_WD_Outpost op, float dist)>();

            IReadOnlyList<WorldObject_WD_Outpost> playerOutposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < playerOutposts.Count; i++)
            {
                WorldObject_WD_Outpost outpost = playerOutposts[i];
                if (outpost == null || outpost.Destroyed || !outpost.IsMortarOutpost) continue;

                float range = MortarFireUtils.GetPlayerMortarMaxRangeTiles(outpost);
                float dist = manager != null
                    ? WorldActions_Utils.GetDistance(outpost.Tile, targetTile, manager)
                    : Find.WorldGrid.ApproxDistanceInTiles(outpost.Tile, targetTile);
                if (dist > range) continue;
                inRange.Add((outpost, dist));
            }

            inRange.Sort((a, b) => a.dist.CompareTo(b.dist));

            int tick = Find.TickManager.TicksGame;
            for (int i = 0; i < inRange.Count; i++)
            {
                WorldObject_WD_Outpost outpost = inRange[i].op;
                float dist = inRange[i].dist;
                float maxRange = MortarFireUtils.GetPlayerMortarMaxRangeTiles(outpost);
                CompViralSpread? comp = outpost.GetComponent<CompViralSpread>();
                bool onCooldown = comp != null && comp.IsMortarOnCooldown;
                bool noSkill = outpost.GetHighestVirtualPawnSkill(SkillDefOf.Shooting) <= 0f;
                float cdLeft = onCooldown && comp != null
                    ? (comp.mortarCooldownTick - tick) / 60000f
                    : 0f;

                var entry = new AssaultArtilleryOutpostEntry
                {
                    Outpost = outpost,
                    DistanceTiles = dist,
                    EtaTicks = GetShellEtaTicks(dist),
                    ScatterRadius = GetAssaultScatterRadius(outpost, dist, maxRange),
                    OnCooldown = onCooldown,
                    NoShootingSkill = noSkill,
                    CooldownDaysLeft = cdLeft,
                    Tier = GetShellTier(outpost)
                };
                FillCachedEntryStrings(entry);

                if (entry.IsSelectable)
                    data.Ready.Add(entry);
                else
                    data.Disabled.Add(entry);
            }

            return data;
        }

        private static void FillCachedEntryStrings(AssaultArtilleryOutpostEntry entry)
        {
            entry.CachedEtaSpread = "TSA_WD_AssaultArtillery_RowEtaSpread".Translate(
                entry.EtaSeconds.ToString("F1"),
                entry.ScatterRadius).ToString();
            if (entry.IsGhostRelay)
            {
                entry.CachedTip = "TSA_WD_AssaultArtillery_GhostRelayTip".Translate().ToString();
                return;
            }
            if (entry.Outpost == null)
            {
                entry.CachedTip = string.Empty;
                return;
            }
            entry.CachedTip = "TSA_WD_AssaultArtillery_OutpostRowTip".Translate(
                entry.DistanceTiles.ToString("F0"),
                entry.EtaSeconds.ToString("F1"),
                entry.ScatterRadius,
                GetAssaultShellUnlockTooltip(entry.Tier)).ToString();
        }

        public static ShellUnlockTier GetShellTier(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return ShellUnlockTier.Basic;
            if (outpost.GetUpgradeLevel(AdvancedShellsDefName) > 0)
                return ShellUnlockTier.Advanced;
            if (outpost.GetUpgradeLevel(ImprovedShellsDefName) > 0)
                return ShellUnlockTier.Improved;
            return ShellUnlockTier.Basic;
        }

        /// <summary>Upgrade icons + cached unlock tips. Shell catalog is load-time; this only checks upgrade levels.</summary>
        public static List<(Texture2D icon, string tooltip)> GetShellUpgradeIcons(WorldObject_WD_Outpost? outpost)
        {
            EnsureShellCatalog();
            var list = new List<(Texture2D, string)>(2);
            if (outpost == null) return list;

            if (outpost.GetUpgradeLevel(ImprovedShellsDefName) > 0 && cachedImprovedUpgradeIcon != null)
                list.Add((cachedImprovedUpgradeIcon, cachedTooltipImproved));
            if (outpost.GetUpgradeLevel(AdvancedShellsDefName) > 0 && cachedAdvancedUpgradeIcon != null)
                list.Add((cachedAdvancedUpgradeIcon, cachedTooltipAdvanced));
            return list;
        }

        public static string GetAssaultShellUnlockTooltip(ShellUnlockTier tier)
        {
            EnsureShellCatalog();
            switch (tier)
            {
                case ShellUnlockTier.Basic:
                    return cachedTooltipBasic;
                case ShellUnlockTier.Improved:
                    return cachedTooltipImproved;
                case ShellUnlockTier.Advanced:
                    return cachedTooltipAdvanced;
                default:
                    return string.Empty;
            }
        }

        /// <summary>True for Improved / Advanced mortar-shell upgrades that gate assault-map ammo.</summary>
        public static bool IsAssaultShellUnlockUpgrade(OutpostUpgradeDef? def) =>
            def != null
            && (def.defName == ImprovedShellsDefName || def.defName == AdvancedShellsDefName);

        public static ShellUnlockTier GetShellTierForUpgrade(OutpostUpgradeDef? def)
        {
            if (def == null) return ShellUnlockTier.Basic;
            if (def.defName == AdvancedShellsDefName) return ShellUnlockTier.Advanced;
            if (def.defName == ImprovedShellsDefName) return ShellUnlockTier.Improved;
            return ShellUnlockTier.Basic;
        }

        /// <summary>Shells newly unlocked by this upgrade tier (excludes shells already available at the prior tier).</summary>
        public static List<ThingDef> GetShellsUnlockedByUpgrade(ShellUnlockTier tier)
        {
            EnsureShellCatalog();
            List<ThingDef> current = GetUnlockedShellDefs(tier);
            if (tier == ShellUnlockTier.Basic || current == null || current.Count == 0)
                return current ?? EmptyShellList;

            ShellUnlockTier prior = tier == ShellUnlockTier.Advanced
                ? ShellUnlockTier.Improved
                : ShellUnlockTier.Basic;
            List<ThingDef> priorList = GetUnlockedShellDefs(prior);
            var priorSet = new HashSet<ThingDef>(priorList);
            var delta = new List<ThingDef>();
            for (int i = 0; i < current.Count; i++)
            {
                ThingDef shell = current[i];
                if (shell != null && !priorSet.Contains(shell))
                    delta.Add(shell);
            }
            return delta.Count > 0 ? delta : current;
        }

        public static string FormatShellLabelsPublic(List<ThingDef> shells) => FormatShellLabels(shells);

        public static List<ThingDef> GetUnlockedShellDefs(ShellUnlockTier tier)
        {
            EnsureShellCatalog();
            switch (tier)
            {
                case ShellUnlockTier.Basic:
                    return cachedShellsBasic;
                case ShellUnlockTier.Improved:
                    return cachedShellsImproved;
                default:
                    return cachedShellsAdvanced;
            }
        }

        /// <summary>Shells shown for a support row. Unlimited ghost relay → all mortar shells.</summary>
        public static List<ThingDef> GetUnlockedShellDefsForSupport(AssaultArtilleryOutpostEntry? entry)
        {
            if (entry == null) return EmptyShellList;
            if (entry.IsGhostRelay && IsUnlimitedMode)
                return GetAllMortarShellDefs();
            return GetUnlockedShellDefs(entry.Tier);
        }

        public static List<ThingDef> GetAllMortarShellDefs()
        {
            EnsureShellCatalog();
            return cachedShellsAll;
        }

        /// <summary>
        /// Discover mortar shell defs once after defs load. GUI / menu paths only read these lists
        /// and check outpost upgrade tiers - never re-scan DefDatabase.
        /// </summary>
        private static void EnsureShellCatalog()
        {
            if (shellCatalogReady) return;

            var all = new List<ThingDef>();
            var seen = new HashSet<ThingDef>();

            void TryDiscover(ThingDef d)
            {
                if (d == null || !IsMortarShellDef(d) || !HasResolvableProjectile(d)) return;
                if (!seen.Add(d)) return;
                all.Add(d);
            }

            // With CE, assault artillery uses 120mm mortar ammo only (not 81mm / vanilla MortarShells).
            if (AssaultArtilleryCeCompat.IsActive)
            {
                AssaultArtilleryCeCompat.ForEach120mmMortarShell(TryDiscover);
            }
            else
            {
                ThingCategoryDef mortarCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("MortarShells");
                if (mortarCat != null)
                {
                    foreach (ThingDef d in mortarCat.DescendantThingDefs)
                        TryDiscover(d);
                }

                // Named fallbacks when CE is absent.
                TryDiscover(DefDatabase<ThingDef>.GetNamedSilentFail(ShellHighExplosive));
                TryDiscover(DefDatabase<ThingDef>.GetNamedSilentFail(ShellHighExplosiveAirburst));
                TryDiscover(DefDatabase<ThingDef>.GetNamedSilentFail(ShellSmoke));
                TryDiscover(DefDatabase<ThingDef>.GetNamedSilentFail(ShellAntigrain));
                TryDiscover(DefDatabase<ThingDef>.GetNamedSilentFail("Shell_EMP"));
                TryDiscover(DefDatabase<ThingDef>.GetNamedSilentFail("Shell_Incendiary"));
                TryDiscover(DefDatabase<ThingDef>.GetNamedSilentFail("Shell_Firefoam"));
                TryDiscover(DefDatabase<ThingDef>.GetNamedSilentFail("Shell_Toxic"));
                TryDiscover(DefDatabase<ThingDef>.GetNamedSilentFail("Shell_Deadlife"));
            }

            all.Sort((a, b) => string.CompareOrdinal(a.label, b.label));

            var basic = new List<ThingDef>();
            var improved = new List<ThingDef>();
            var advanced = new List<ThingDef>();
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef d = all[i];
                if (IsShellAllowedForTier(d, ShellUnlockTier.Basic))
                    basic.Add(d);
                if (IsShellAllowedForTier(d, ShellUnlockTier.Improved))
                    improved.Add(d);
                if (IsShellAllowedForTier(d, ShellUnlockTier.Advanced))
                    advanced.Add(d);
            }

            cachedShellsAll = all;
            cachedShellsBasic = basic;
            cachedShellsImproved = improved;
            cachedShellsAdvanced = advanced;

            cachedTooltipBasic = "TSA_WD_AssaultArtillery_Unlock_Basic".Translate().ToString();
            cachedTooltipImproved = "TSA_WD_AssaultArtillery_Unlock_Improved"
                .Translate(FormatShellLabels(improved)).ToString();
            cachedTooltipAdvanced = "TSA_WD_AssaultArtillery_Unlock_Advanced"
                .Translate(FormatShellLabels(advanced)).ToString();

            cachedImprovedUpgradeIcon = LoadUpgradeIconTexture(ImprovedShellsDefName);
            cachedAdvancedUpgradeIcon = LoadUpgradeIconTexture(AdvancedShellsDefName);

            shellCatalogReady = true;
        }

        private static string FormatShellLabels(List<ThingDef> shells)
        {
            if (shells == null || shells.Count == 0) return "-";
            var sb = new StringBuilder();
            for (int i = 0; i < shells.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(shells[i].LabelCap);
            }
            return sb.ToString();
        }

        private static Texture2D? LoadUpgradeIconTexture(string defName)
        {
            OutpostUpgradeDef? def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(defName);
            if (def == null || def.imagePath.NullOrEmpty()) return null;
            return ContentFinder<Texture2D>.Get(def.imagePath, false);
        }

        /// <summary>Vanilla uses projectileWhenLoaded; CE mortar AmmoDefs use detonateProjectile / ammo-set links.</summary>
        public static bool HasResolvableProjectile(ThingDef shell)
        {
            if (shell == null) return false;
            if (shell.projectileWhenLoaded != null) return true;
            if (AssaultArtilleryCeCompat.ResolveProjectileDef(shell) != null) return true;
            return shell.defName == ShellHighExplosive
                || shell.defName == ShellSmoke
                || shell.defName == ShellAntigrain;
        }

        public static bool IsMortarShellDef(ThingDef shell)
        {
            if (shell == null) return false;

            ThingCategoryDef mortarCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("MortarShells");
            if (mortarCat != null && shell.IsWithinCategory(mortarCat))
                return true;

            if (shell.thingCategories != null)
            {
                for (int i = 0; i < shell.thingCategories.Count; i++)
                {
                    ThingCategoryDef cat = shell.thingCategories[i];
                    if (cat != null && cat.defName == "MortarShells")
                        return true;
                }
            }

            if (shell.tradeTags != null)
            {
                for (int i = 0; i < shell.tradeTags.Count; i++)
                {
                    if (shell.tradeTags[i] == "MortarShell")
                        return true;
                }
            }

            if (AssaultArtilleryCeCompat.IsCeAssaultMortarShell(shell))
                return true;

            return shell.defName == ShellHighExplosive
                || shell.defName == ShellHighExplosiveAirburst
                || shell.defName == ShellSmoke
                || shell.defName == ShellAntigrain
                || shell.defName == "Shell_EMP"
                || shell.defName == "Shell_Incendiary"
                || shell.defName == "Shell_Firefoam"
                || shell.defName == "Shell_Toxic"
                || shell.defName == "Shell_Deadlife"
                || shell.defName == "Shell_120mmMortar_HE"
                || shell.defName == "Shell_120mmMortar_HE_HFuzed"
                || shell.defName == "Shell_120mmMortar_Smoke"
                || shell.defName == "Shell_120mmMortar_Incendiary"
                || shell.defName == "Shell_120mmMortar_EMP"
                || shell.defName == "Shell_120mmMortar_Firefoam";
        }

        public static bool IsShellAllowedForTier(ThingDef shell, ShellUnlockTier tier)
        {
            if (shell == null) return false;
            if (IsBasicTierShell(shell))
                return true;
            if (shell.defName == ShellAntigrain)
                return tier >= ShellUnlockTier.Advanced;
            return tier >= ShellUnlockTier.Improved;
        }

        private static bool IsBasicTierShell(ThingDef shell) =>
            shell.defName == ShellHighExplosive
            || shell.defName == ShellHighExplosiveAirburst
            || shell.defName == ShellSmoke
            || shell.defName == "Shell_120mmMortar_HE"
            || shell.defName == "Shell_120mmMortar_HE_HFuzed"
            || shell.defName == "Shell_120mmMortar_Smoke";

        public static bool CanOpenDialog(Map map)
        {
            if (!IsEligibleAssaultMap(map)) return false;
            Settlement? settlement = GetAssaultedSettlement(map);
            GetAssaultArtilleryMenuCached(settlement);
            return cachedCanOpen;
        }

        public static bool CanIssueStrike(Map map, AssaultArtilleryOutpostEntry? support, out string denyReason)
        {
            denyReason = string.Empty;
            if (!IsEligibleAssaultMap(map))
            {
                denyReason = "TSA_WD_AssaultArtillery_NotAssaultMap".Translate();
                return false;
            }

            // Multiple ready outposts may fire concurrently. Per-outpost cooldown (and ghost-relay
            // Selectable state) already gate repeat fire from the same support source.
            if (support == null || !support.IsSelectable)
            {
                denyReason = "TSA_WD_AssaultArtillery_SelectOutpost".Translate();
                return false;
            }

            return true;
        }

        public static void OpenDialog(Map map)
        {
            if (map == null) return;
            Window? existing = Find.WindowStack?.WindowOfType<Dialog_WdAssaultArtillery>();
            if (existing != null)
            {
                existing.Close();
                return;
            }
            Settlement? settlement = GetAssaultedSettlement(map);
            GetAssaultArtilleryMenuCached(settlement, force: true);
            Find.WindowStack?.Add(new Dialog_WdAssaultArtillery(map));
        }

        public static void BeginTargeting(Map map, AssaultArtilleryOutpostEntry support, ThingDef shellDef)
        {
            if (map == null || shellDef == null || support == null) return;
            if (!CanIssueStrike(map, support, out string deny))
            {
                Messages.Message(deny, MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.WindowStack?.WindowOfType<Dialog_WdAssaultArtillery>()?.Close(false);

            var parms = new TargetingParameters
            {
                canTargetLocations = true,
                canTargetPawns = false,
                canTargetBuildings = false,
                canTargetAnimals = false,
                canTargetHumans = false,
                canTargetMechs = false
            };

            Find.Targeter.BeginTargeting(
                parms,
                (LocalTargetInfo target) =>
                {
                    if (!target.IsValid || !target.Cell.InBounds(map))
                    {
                        Messages.Message("TSA_WD_AssaultArtillery_InvalidCell".Translate(), MessageTypeDefOf.RejectInput, false);
                        return;
                    }
                    TryIssueStrike(map, support, shellDef, target.Cell);
                },
                null,
                null,
                null);
        }

        public static bool TryIssueStrike(Map map, AssaultArtilleryOutpostEntry support, ThingDef shellDef, IntVec3 aimCell)
        {
            if (map == null || shellDef == null || support == null || !aimCell.InBounds(map)) return false;
            if (!CanIssueStrike(map, support, out string deny))
            {
                Messages.Message(deny, MessageTypeDefOf.RejectInput, false);
                return false;
            }

            Settlement? settlement = GetAssaultedSettlement(map);
            if (settlement == null) return false;

            WorldObject? origin;
            if (support.IsGhostRelay)
                origin = ResolveGhostRelayOrigin(settlement, out _);
            else
                origin = support.Outpost;

            if (origin == null)
            {
                Messages.Message("TSA_WD_AssaultArtillery_NoSupport".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            if (!support.IsGhostRelay && support.Outpost != null)
            {
                CompViralSpread? comp = support.Outpost.GetComponent<CompViralSpread>();
                MortarFireUtils.ApplyPlayerMortarCooldown(comp, support.Outpost);
                WD_Outpost_Mortar.InvalidateFireGizmoCache(support.Outpost);
            }

            WorldObject_Traveler? shell = WorldActions_Traveler.SpawnAssaultArtilleryShell(
                origin,
                settlement,
                aimCell,
                shellDef,
                support.ScatterRadius);
            if (shell == null) return false;

            InvalidateMenuCache();

            WD_MapComponent_AssaultArtillery? tracker = WD_MapComponent_AssaultArtillery.GetOrAdd(map);
            tracker?.RegisterInboundShell(shell);

            float etaSeconds = shell.CachedLaunchTotalTravelTicks > 0f
                ? shell.CachedLaunchTotalTravelTicks / 60f
                : support.EtaSeconds;
            string etaLabel = etaSeconds.ToString("F1");
            Messages.Message(
                "TSA_WD_AssaultArtillery_Inbound".Translate(shellDef.LabelCap, etaLabel),
                new TargetInfo(aimCell, map),
                MessageTypeDefOf.ThreatBig);
            return true;
        }

        /// <summary>Called when assault mortar shell arrives on the world map - start local map volley.</summary>
        public static void ExecuteAssaultArtilleryArrival(WorldObject_Traveler traveler)
        {
            if (traveler == null || !traveler.assaultArtillerySupport) return;
            if (traveler.targetObject is not Settlement settlement) return;

            ThingDef? shellDef = DefDatabase<ThingDef>.GetNamedSilentFail(traveler.assaultShellDefName);
            if (shellDef == null) return;

            Map? map = settlement.Map;
            if (map == null) return;

            WD_MapComponent_AssaultArtillery? tracker = WD_MapComponent_AssaultArtillery.GetOrAdd(map);
            if (tracker == null) return;

            tracker.ClearInboundShell(traveler.ID);
            int originTile = traveler.originObject?.Tile ?? -1;
            tracker.EnqueueStrike(
                traveler.assaultAimCell,
                shellDef,
                inboundDelayTicks: 0,
                GetVolleyShellCount(shellDef),
                traveler.assaultScatterRadius > 0 ? traveler.assaultScatterRadius : ScatterMin,
                originTile);
        }

        /// <summary>Clear inbound tracking when shell is destroyed in flight (e.g. AA).</summary>
        public static void NotifyAssaultShellRemoved(WorldObject_Traveler traveler)
        {
            if (traveler == null || !traveler.assaultArtillerySupport) return;
            if (traveler.targetObject is not Settlement settlement) return;

            WD_MapComponent_AssaultArtillery? tracker = settlement.Map?.GetComponent<WD_MapComponent_AssaultArtillery>();
            if (tracker == null || !tracker.ClearInboundShell(traveler.ID)) return;

            if (settlement.Map == null) return;

            Messages.Message(
                "TSA_WD_AssaultArtillery_ShellShotDown".Translate(),
                MessageTypeDefOf.ThreatBig);
        }

        /// <summary>Nearest player colony or WD outpost to the assault target (ghost relay origin).</summary>
        public static WorldObject? ResolveGhostRelayOrigin(Settlement assaultTarget, out float distanceTiles)
        {
            distanceTiles = 0f;
            if (assaultTarget == null) return null;

            int targetTile = assaultTarget.Tile;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            WorldObject? nearest = null;
            float bestDist = float.MaxValue;

            void Consider(WorldObject? wo)
            {
                if (wo == null || wo.Destroyed || wo.Tile < 0) return;
                float dist = manager != null
                    ? WorldActions_Utils.GetDistance(wo.Tile, targetTile, manager)
                    : Find.WorldGrid.ApproxDistanceInTiles(wo.Tile, targetTile);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    nearest = wo;
                }
            }

            Consider(InfluenceUtils.GetPlayerColony());

            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
            {
                WorldObject_WD_Outpost op = outposts[i];
                if (op == null || op.Destroyed) continue;
                Consider(op);
            }

            distanceTiles = nearest != null ? bestDist : 0f;
            return nearest;
        }
    }
}
