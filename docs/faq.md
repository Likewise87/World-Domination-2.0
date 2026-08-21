# FAQ and compatibility

Anything that rewrites **diplomacy**, **world-map interactions**, **base generation**, or **raid-point multipliers** can overlap with World Domination 2.0. Soft issues usually mean awkward overlap or shared systems. Hard issues mean do not enable that mod with WD.

## FAQ

??? question "Can this be added midgame?"
    Yes. Make a backup nonetheless.

??? question "Allies are coming to my colony and are not leaving"
    Not caused by WD. Likely [Faction Territories & Vassalage](https://steamcommunity.com/sharedfiles/filedetails/?id=3626725895) or a similar territory / escort system.

??? question "Compatible with Vehicle Raids?"
    Yes.

??? question "Where to find the controls?"
    There is a new tab with the **WD** icon at the bottom of the screen. It opens the main Dashboard.

    On the world map (globe) there is a small **WD** icon near the bottom right.

    When you select a settlement, outpost, traveler, or world tile, WD also adds inspect tabs and gizmos you should learn.

    More detail: [Dashboards and overlays](ui/dashboards-and-overlays.md).

??? question "Does WD replace RimWar?"
    Yes. World Domination 2.0 is meant to replace [RimWar](https://steamcommunity.com/sharedfiles/filedetails/?id=1798765665), not run beside it. Do not combine them.

??? question "Can I use the original World Domination?"
    No. PackageId `TSA.WorldDomination` and World Domination 2.0 are hard-incompatible.

??? question "Why did storyteller raids from some factions stop?"
    By default, WD blocks storyteller raids when the storyteller picks a faction managed by WD. Those factions attack through WD raid travelers instead.

    Storyteller raids from factions WD does not manage stay active by default (for example Mechanoids and manhunter packs). Configure this under raid settings.

??? question "Where can I report bugs?"
    Use the public repository: [Likewise87/World-Domination-2.0](https://github.com/Likewise87/World-Domination-2.0). Include reproduction steps, mod list, and the relevant log.

## Compatibility

### Hard incompatibility

??? failure "Visit Settlements"
    **Hard incompatibility.** Settlements can disappear after you visit them.

    Workshop: [Visit Settlements](https://steamcommunity.com/sharedfiles/filedetails/?id=3535955435) (packageId `alt4s.visitsettlements`).

??? failure "Original World Domination"
    **Hard incompatibility.** Do not run `TSA.WorldDomination` together with World Domination 2.0.

### Soft incompatibility / overlap

These can run, but they share design space with WD. Expect awkward overlap, missing integration, or one mod winning a contested system.

??? warning "Vanilla Base Generation Expanded (and other base-gen mods)"
    **Soft incompatibility.** WD generates settlement maps for visits and attacks. [Vanilla Base Generation Expanded](https://steamcommunity.com/workshop/filedetails/?id=3209927822) (and similar base-generation mods) compete for the same job. Only one generator wins, based on patch and load order. Tiered WD layouts may not appear as intended if another generator takes over.

    Prefer WD base generation if you want WD settlement types and tiers on visited maps.

??? warning "RimWar"
    **Soft / replace.** WD is intended to replace [RimWar](https://steamcommunity.com/sharedfiles/filedetails/?id=1798765665). Running both is unsupported.

??? warning "Vanilla Outposts Expanded"
    You can still build [Vanilla Outposts Expanded](https://steamcommunity.com/sharedfiles/filedetails/?id=2688104164) outposts, but WD ignores them completely. They do not join WD strength, logistics, or management.

??? warning "Faction Territories & Vassalage"
    Use [Faction Territories & Vassalage](https://steamcommunity.com/sharedfiles/filedetails/?id=3626725895) with **Vassalage deactivated**. Territory visuals can be fine; vassal systems overlap WD conquest and diplomacy.

??? warning "Empire (Refactored)"
    [Empire Refactored](https://steamcommunity.com/sharedfiles/filedetails/?id=3701480464) has overlaps that may feel awkward (extra colonies, orders, and world pressure beside WD).

??? warning "Simple Leadership"
    [Simple Leadership](https://steamcommunity.com/sharedfiles/filedetails/?id=3668307448) has overlaps that may feel awkward (faction / base leadership stories beside WD).

??? warning "Economics & Demography"
    [Economics & Demography](https://steamcommunity.com/sharedfiles/filedetails/?id=3692156692): WD can spawn pawns that E&D does not track. Killing those pawns can make E&D treat a faction as extinct incorrectly.

### Usually fine

??? tip "Vehicle Framework / Vehicle Raids"
    Compatible with vehicle play, including Vehicle Raids. WD travelers remain abstract strength pools even when icons look like vehicles or caravans.

    Related: [Vehicle Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=3014906877) (load before WD when used).

## General caution

Anything introducing diplomacy, world-map interactions, base generation, or raid-point multipliers might cause compatibility issues. Test uncertain lists on a backup save.
