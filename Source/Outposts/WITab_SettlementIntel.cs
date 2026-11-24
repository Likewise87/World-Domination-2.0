using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Lean intel sections for the Stats tab Intel pane (<see cref="WITab_OutpostAndSettlement_Stats"/>).</summary>
    internal static class SettlementIntelSnapshot
    {
        private sealed class SightCandidate
        {
            public string Label = "";
            public string Value = "";
            public string Tooltip = "";
            public float Distance;
            public Texture2D LabelIcon;
            public Color LabelIconColor = Color.white;
            public Faction LabelFaction;
        }

        public static OutpostStatsSnapshot Build(WorldObject worldObject)
        {
            var snap = new OutpostStatsSnapshot();
            if (worldObject == null) return snap;

            snap.Sections.Add(BuildActiveNow());
            if (worldObject is Settlement colony
                && colony.Faction?.IsPlayer == true
                && colony.HasMap)
            {
                snap.Sections.Add(BuildPlayerColonyWatchTowers(colony));
            }
            snap.Sections.Add(BuildKnownTo(worldObject));
            snap.Sections.Add(BuildGotBurned(worldObject));
            if (worldObject is Settlement settlement)
            {
                snap.Sections.Add(BuildLineOfSight(settlement));
                snap.Sections.Add(BuildSitesInSight(settlement));
                snap.Sections.Add(BuildTravelersInSight(settlement));
            }

            return snap;
        }

        private static OutpostStatsSection BuildPlayerColonyWatchTowers(Settlement colony)
        {
            var section = new OutpostStatsSection
            {
                Title = "",
                FullWidth = true
            };
            int used = WorldActions_WatchTowers.CountUsedSlots(colony);
            int max = WorldActions_WatchTowers.MaxPerBuilder;
            AddRow(section,
                "TSA_WD_OutpostStats_Row_WatchTower".Translate().ToString(),
                "TSA_WD_OutpostStats_Row_WatchTowerValue".Translate(used, max).ToString(),
                "TSA_WD_OutpostStats_Row_WatchTowerTip".Translate().ToString());
            return section;
        }

        private static OutpostStatsSection BuildActiveNow()
        {
            bool tracking = WorldComponent_SettlementIntel.IsTracking();
            bool active = WorldComponent_SettlementIntel.IsActive();
            var gate = WorldDominationMod.settings?.gateSettlementIntel ?? WdThreatStageGate.Never;
            var section = new OutpostStatsSection
            {
                Title = "",
                FullWidth = true
            };
            AddRow(section,
                "TSA_WD_SettlementIntel_Row_Tracking".Translate().ToString(),
                YesNo(tracking),
                "TSA_WD_SettlementIntel_Row_TrackingTip".Translate(WdEscalation.ThreatGateLabel(gate)).ToString());
            AddRow(section,
                "TSA_WD_SettlementIntel_Row_Active".Translate().ToString(),
                YesNo(active),
                "TSA_WD_SettlementIntel_Row_ActiveTip".Translate(WdEscalation.ThreatGateLabel(gate)).ToString());
            return section;
        }

        private static OutpostStatsSection BuildKnownTo(WorldObject worldObject)
        {
            var section = new OutpostStatsSection
            {
                Title = "TSA_WD_SettlementIntel_Section_KnownTo".Translate().ToString(),
                FullWidth = true
            };

            var intel = WorldComponent_SettlementIntel.Get();
            if (WorldComponent_SettlementIntel.IsAlwaysKnown(worldObject))
            {
                AddRow(section,
                    "TSA_WD_SettlementIntel_Row_AlwaysKnown".Translate().ToString(),
                    "TSA_WD_SettlementIntel_Yes".Translate().ToString(),
                    "TSA_WD_SettlementIntel_Row_AlwaysKnownTip".Translate().ToString());
            }

            var factions = Find.FactionManager?.AllFactionsListForReading;
            if (factions == null || factions.Count == 0)
            {
                AddRow(section,
                    "TSA_WD_SettlementIntel_Row_None".Translate().ToString(),
                    "-",
                    "TSA_WD_SettlementIntel_Row_NoneTip".Translate().ToString());
                return section;
            }

            // Stable order: player first, then name. Include defeated; skip hidden / temporary.
            var ordered = new List<Faction>();
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                if (f == null || f.def == null || f.def.hidden) continue;
                if (f.temporary) continue;
                ordered.Add(f);
            }
            ordered.Sort((a, b) =>
            {
                if (a.IsPlayer != b.IsPlayer) return a.IsPlayer ? -1 : 1;
                return string.CompareOrdinal(a.Name, b.Name);
            });

            for (int i = 0; i < ordered.Count; i++)
            {
                Faction f = ordered[i];
                bool knows = intel != null && intel.IsDiscovered(f, worldObject);
                string label = FactionLabel(f);
                string tip = knows
                    ? "TSA_WD_SettlementIntel_KnownFactionYesTip".Translate(f.Name).ToString()
                    : "TSA_WD_SettlementIntel_KnownFactionNoTip".Translate(f.Name).ToString();
                if (worldObject.Faction == f)
                    tip = "TSA_WD_SettlementIntel_KnownFactionOwnerTip".Translate(f.Name).ToString();
                else if (WorldComponent_SettlementIntel.IsAlwaysKnown(worldObject))
                    tip = "TSA_WD_SettlementIntel_KnownFactionAlwaysTip".Translate(f.Name).ToString();
                AddRow(section, label, YesNo(knows), tip, labelFaction: f);
            }

            return section;
        }

        private static OutpostStatsSection BuildGotBurned(WorldObject worldObject)
        {
            var section = new OutpostStatsSection
            {
                Title = "TSA_WD_SettlementIntel_Section_GotBurned".Translate().ToString(),
                FullWidth = true
            };

            var intel = WorldComponent_SettlementIntel.Get();
            var factions = Find.FactionManager?.AllFactionsListForReading;
            if (intel == null || factions == null)
            {
                AddRow(section,
                    "TSA_WD_SettlementIntel_Row_None".Translate().ToString(),
                    "-",
                    "TSA_WD_SettlementIntel_GotBurnedNoneTip".Translate().ToString());
                return section;
            }

            int listed = 0;
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                if (f == null || f.def?.hidden == true || f.temporary) continue;
                if (!intel.IsGotBurned(f, worldObject, out int daysLeft)) continue;
                AddRow(section,
                    FactionLabel(f),
                    "TSA_WD_SettlementIntel_Row_DaysLeft".Translate(daysLeft).ToString(),
                    "TSA_WD_SettlementIntel_GotBurnedRowTip".Translate(f.Name, daysLeft).ToString(),
                    labelFaction: f);
                listed++;
            }

            if (listed == 0)
            {
                AddRow(section,
                    "TSA_WD_SettlementIntel_Row_None".Translate().ToString(),
                    "-",
                    "TSA_WD_SettlementIntel_GotBurnedNoneTip".Translate().ToString());
            }
            return section;
        }

        private static OutpostStatsSection BuildLineOfSight(Settlement observer)
        {
            var section = new OutpostStatsSection
            {
                Title = "TSA_WD_SettlementIntel_Section_Los".Translate().ToString(),
                FullWidth = true
            };

            float awareness = WorldComponent_SettlementIntel.GetAwarenessRadius(observer);
            float los = WorldComponent_SettlementIntel.GetLosRadius(observer);
            string medStr = WorldComponent_SettlementIntel.GetAwarenessSpotMinTravelerStrength().ToString("F0");
            var seth = WorldDominationMod.settings;

            if (WorldComponent_SettlementIntel.IsPlayerColonyObserver(observer))
            {
                int pawns = WorldComponent_SettlementIntel.GetPlayerColonySightPawnCount(observer);
                float losPer = WorldComponent_SettlementIntel.GetPlayerSightLosPerPawn(seth);
                float awarenessPer = WorldComponent_SettlementIntel.GetPlayerSightAwarenessPerPawn(seth);
                AddRow(section,
                    "TSA_WD_SettlementIntel_Row_SightPawns".Translate().ToString(),
                    pawns.ToString(),
                    "TSA_WD_SettlementIntel_Row_SightPawnsTip".Translate(
                        losPer.ToString("F1"),
                        awarenessPer.ToString("F1")).ToString());
                AddRow(section,
                    "TSA_WD_SettlementIntel_Row_PerPawnLos".Translate().ToString(),
                    losPer.ToString("F1"),
                    "TSA_WD_SettlementIntel_Row_SightPawnsTip".Translate(
                        losPer.ToString("F1"),
                        awarenessPer.ToString("F1")).ToString());
                AddRow(section,
                    "TSA_WD_SettlementIntel_Row_PerPawnAwareness".Translate().ToString(),
                    awarenessPer.ToString("F1"),
                    "TSA_WD_SettlementIntel_Row_SightPawnsTip".Translate(
                        losPer.ToString("F1"),
                        awarenessPer.ToString("F1")).ToString());
            }
            else
            {
                float baseline = WorldComponent_SettlementIntel.GetViewBaselineRange(observer);
                AddRow(section,
                    "TSA_WD_SettlementIntel_Row_BaselineR".Translate().ToString(),
                    baseline.ToString("F1"),
                    "TSA_WD_SettlementIntel_Row_BaselineRTip".Translate().ToString());
            }

            AddRow(section,
                "TSA_WD_SettlementIntel_Row_Los".Translate().ToString(),
                los.ToString("F1"),
                "TSA_WD_SettlementIntel_Row_LosTip".Translate().ToString());
            AddRow(section,
                "TSA_WD_SettlementIntel_Row_Awareness".Translate().ToString(),
                awareness.ToString("F1"),
                "TSA_WD_SettlementIntel_Row_AwarenessTip".Translate(medStr).ToString());
            return section;
        }

        private static OutpostStatsSection BuildSitesInSight(Settlement observer)
        {
            var section = new OutpostStatsSection
            {
                Title = "TSA_WD_SettlementIntel_Section_SitesInSight".Translate().ToString(),
                FullWidth = true
            };

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            float maxScan = WorldComponent_SettlementIntel.GetMaxLos(observer);

            var found = new List<SightCandidate>();
            Faction owner = observer.Faction;
            var intel = WorldComponent_SettlementIntel.Get();

            void Consider(WorldObject other)
            {
                if (other == null || other.Destroyed || other == observer) return;
                if (other.GetComponent<CompViralSpread>() == null) return;
                float d = WorldActions_Utils.GetDistance(observer.Tile, other.Tile, manager);
                if (d > maxScan && d > 1f) return;
                float sight = WorldComponent_SettlementIntel.GetSightRange(observer, other);
                if (d > 1f && d > sight) return;

                string factionName = other.Faction?.Name
                    ?? "TSA_WD_SettlementIntel_UnknownFaction".Translate().ToString();
                bool known = owner != null && (intel == null || intel.IsDiscovered(owner, other));
                string kind = other is WorldObject_WD_Outpost
                    ? "TSA_WD_SettlementIntel_KindOutpost".Translate().ToString()
                    : "TSA_WD_SettlementIntel_KindSettlement".Translate().ToString();

                Texture2D siteIcon = ResolveWorldObjectIcon(other);
                found.Add(new SightCandidate
                {
                    Label = other.LabelCap,
                    Value = "TSA_WD_SettlementIntel_SiteValue".Translate(
                        factionName,
                        d.ToString("F1")).ToString(),
                    Tooltip = "TSA_WD_SettlementIntel_SiteInSightTip".Translate(
                        kind,
                        other.LabelCap,
                        factionName,
                        d.ToString("F1"),
                        sight.ToString("F1"),
                        YesNo(known)).ToString(),
                    Distance = d,
                    LabelIcon = siteIcon,
                    LabelIconColor = other.ExpandingIconColor,
                    // Avoid a double faction badge when ExpandingIcon already is the faction icon.
                    LabelFaction = IconIsFactionBadge(siteIcon, other.Faction) ? null : other.Faction
                });
            }

            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                for (int i = 0; i < settlements.Count; i++)
                    Consider(settlements[i]);
            }

            // Prefer daily snapshot; fall back to throttled player-outpost cache (never AllWorldObjects).
            var snap = manager?.CurrentDailySnapshot;
            if (snap?.PlayerOutposts != null)
            {
                for (int i = 0; i < snap.PlayerOutposts.Count; i++)
                    Consider(snap.PlayerOutposts[i]);
            }
            else
            {
                IReadOnlyList<WorldObject_WD_Outpost> cached = WdPlayerOutpostCache.PlayerOutposts;
                for (int i = 0; i < cached.Count; i++)
                    Consider(cached[i]);
            }

            found.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            if (found.Count == 0)
            {
                AddRow(section,
                    "TSA_WD_SettlementIntel_Row_None".Translate().ToString(),
                    "-",
                    "TSA_WD_SettlementIntel_SitesInSightNoneTip".Translate().ToString());
                return section;
            }

            for (int i = 0; i < found.Count; i++)
                AddSightRow(section, found[i]);
            return section;
        }

        private static OutpostStatsSection BuildTravelersInSight(Settlement observer)
        {
            var section = new OutpostStatsSection
            {
                Title = "TSA_WD_SettlementIntel_Section_TravelersInSight".Translate().ToString(),
                FullWidth = true
            };

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            float maxLos = WorldComponent_SettlementIntel.GetMaxLos(observer);
            var found = new List<SightCandidate>();
            Faction owner = observer.Faction;
            var intel = WorldComponent_SettlementIntel.Get();

            IReadOnlyList<WorldObject_Traveler> live = WorldObject_Traveler.LiveTravelers;
            for (int i = 0; i < live.Count; i++)
            {
                WorldObject_Traveler t = live[i];
                if (t == null || t.Destroyed) continue;
                if (t.Faction != null && t.Faction == owner) continue;
                float d = WorldActions_Utils.GetDistanceUncached(observer.Tile, t.Tile);
                float sight = WorldComponent_SettlementIntel.GetSightRange(observer, t);
                if (d > 1f && d > sight) continue;
                if (d > maxLos && d > 1f) continue;

                string factionName = t.Faction?.Name
                    ?? "TSA_WD_SettlementIntel_UnknownFaction".Translate().ToString();
                bool known = owner != null && (intel == null || intel.IsTravelerDiscovered(owner, t));
                Texture2D icon = WorldDomination_UIUtils.CachedTravelerMissionIcon(t) ?? ResolveWorldObjectIcon(t);
                found.Add(new SightCandidate
                {
                    Label = t.LabelCap,
                    Value = "TSA_WD_SettlementIntel_SiteValue".Translate(
                        factionName,
                        d.ToString("F1")).ToString(),
                    Tooltip = "TSA_WD_SettlementIntel_TravelerInSightTip".Translate(
                        t.LabelCap,
                        factionName,
                        d.ToString("F1"),
                        sight.ToString("F1"),
                        YesNo(known)).ToString(),
                    Distance = d,
                    LabelIcon = icon,
                    LabelIconColor = t.ExpandingIconColor,
                    LabelFaction = t.Faction
                });
            }

            List<Caravan> caravans = Find.WorldObjects?.Caravans;
            if (caravans != null)
            {
                for (int i = 0; i < caravans.Count; i++)
                {
                    Caravan c = caravans[i];
                    if (c == null || c.Destroyed) continue;
                    if (c.Faction != null && c.Faction == owner) continue;
                    // Only player caravans (real pawns); NPC mobiles are WD travelers.
                    if (c.Faction?.IsPlayer != true) continue;
                    float d = WorldActions_Utils.GetDistanceUncached(observer.Tile, c.Tile);
                    float caravanSight = maxLos;
                    if (d > 1f && d > caravanSight) continue;

                    string factionName = c.Faction?.Name
                        ?? "TSA_WD_SettlementIntel_UnknownFaction".Translate().ToString();
                    found.Add(new SightCandidate
                    {
                        Label = c.LabelCap,
                        Value = "TSA_WD_SettlementIntel_SiteValue".Translate(
                            factionName,
                            d.ToString("F1")).ToString(),
                        Tooltip = "TSA_WD_SettlementIntel_CaravanInSightTip".Translate(
                            c.LabelCap,
                            factionName,
                            d.ToString("F1"),
                            caravanSight.ToString("F1")).ToString(),
                        Distance = d,
                        LabelIcon = ResolveWorldObjectIcon(c),
                        LabelIconColor = c.ExpandingIconColor,
                        LabelFaction = c.Faction
                    });
                }
            }

            found.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            if (found.Count == 0)
            {
                AddRow(section,
                    "TSA_WD_SettlementIntel_Row_None".Translate().ToString(),
                    "-",
                    "TSA_WD_SettlementIntel_TravelersInSightNoneTip".Translate().ToString());
                return section;
            }

            for (int i = 0; i < found.Count; i++)
                AddSightRow(section, found[i]);
            return section;
        }

        private static string YesNo(bool yes) =>
            yes
                ? "TSA_WD_SettlementIntel_Yes".Translate().ToString()
                : "TSA_WD_SettlementIntel_No".Translate().ToString();

        private static string FactionLabel(Faction f)
        {
            if (f == null) return "?";
            if (f.defeated)
                return f.Name + " " + "TSA_WD_SettlementIntel_DefeatedSuffix".Translate();
            return f.Name;
        }

        private static Texture2D ResolveWorldObjectIcon(WorldObject wo)
        {
            if (wo == null) return null;
            Texture2D tex = wo.ExpandingIcon;
            if (tex != null) return tex;
            return wo.def?.ExpandingIconTexture;
        }

        private static bool IconIsFactionBadge(Texture2D icon, Faction faction)
        {
            if (icon == null || faction?.def?.FactionIcon == null) return false;
            return icon == faction.def.FactionIcon;
        }

        private static void AddSightRow(OutpostStatsSection section, SightCandidate c)
        {
            if (c == null) return;
            AddRow(section, c.Label, c.Value, c.Tooltip, c.LabelIcon, c.LabelIconColor, c.LabelFaction);
        }

        private static void AddRow(
            OutpostStatsSection section,
            string label,
            string value,
            string tooltip,
            Texture2D labelIcon = null,
            Color? labelIconColor = null,
            Faction labelFaction = null)
        {
            section.Rows.Add(new OutpostStatRow
            {
                Label = label ?? "",
                Value = value ?? "-",
                Tooltip = tooltip ?? "",
                LabelIcon = labelIcon,
                LabelIconColor = labelIconColor ?? Color.white,
                LabelFaction = labelFaction
            });
        }
    }
}
