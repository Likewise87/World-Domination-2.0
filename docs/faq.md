# FAQ and compatibility

## Can I add World Domination 2.0 to an existing save?

Yes. The mod can be added during the midgame. Make a backup first, as you should before adding any major world-system mod. WD needs to initialize settlement strength, world actions, diplomacy, and its outpost systems in a world that was generated without them, so review the World Stats and settings after loading.

## Does WD replace RimWar?

Yes. World Domination 2.0 is intended to replace RimWar, not run beside it. Both occupy the same strategic space with moving world forces, faction expansion, settlement attacks, and world diplomacy. Do not combine them.

## Can I use Visit Settlements?

No. **Visit Settlements is a hard incompatibility.** It overlaps settlement visiting and map generation. Settlements can disappear after being visited.

## Can I use the original World Domination?

No. `TSA.WorldDomination` and World Domination 2.0 overlap directly and are declared incompatible.

## Does WD use Vanilla Outposts Expanded outposts?

You can still build Vanilla Outposts Expanded outposts, but WD ignores them. They do not count as WD outposts, do not contribute to WD outpost strength or escalation, and do not join WD logistics or management screens.

## What about base-generation mods?

WD generates maps for visited and attacked settlements. Vanilla Base Generation Expanded and other mods that replace settlement base generation try to control the same step. Only one generator wins, based on patch and load order, so layouts may be missing, mixed, or different from what one mod expects.

Vanilla Base Generation Expanded is not listed as a hard incompatibility, but the overlap is real. If settlement maps generate incorrectly, test without the other base generator before reporting a WD balance or layout bug.

## Are Empire and Simple Leadership compatible?

**Empire (Refactored)** and **Simple Leadership** have overlapping concepts with WD and can feel awkward together. They may run, but their leadership, faction, and world-action systems operate independently and are not fully integrated. Expect duplicate or contradictory strategic stories rather than a seamless combined system.

## What is the Economics & Democracy caveat?

Economics & Democracy may not know about pawns spawned by WD. Killing those pawns can cause E&D to consider their faction extinct incorrectly. This is not presented as a hard startup incompatibility, but it can corrupt E&D's interpretation of faction survival.

## Can I use Faction Territories & Vassalage?

Yes, with **Vassalage deactivated**. The remaining territory features can be used. WD has its own faction ownership, diplomacy, conquest, and world-map highlighting, so enabling both vassal systems would overlap.

## Are Vehicle Framework and vehicle raids supported?

Yes. Vehicle Framework and Vanilla Vehicles Expanded are recognized in load ordering, WD roads apply their speed bonus to Vehicle Framework vehicles, and vehicle-based raid graphics are supported. Vehicle raids can be used.

WD travelers are still abstract moving strength pools. They are not pawn or vehicle caravans, even if their world icon depicts a force.

## Where are the controls?

- Bottom bar: open the **WD** main tab
- World map: use the WD overlay control
- Default shortcuts: hold **Left Alt** and press `X` for WD, `D` for Diplomacy, `S` for World Stats, `F` for Outpost Overview, `G` for Active Travelers, `A` for Your Pawns, or `Y` for Prisoners
- World map overlays: hold the same key and press `1` to `7`, or `Q`, `W`, `E`, `R`, `T`
- Mod options: **Options > Mod settings > World Domination 2.0**, or use the gear in the WD dashboard
- Outpost management: select a WD outpost on the world map for Stats, Food, Pawns, Experts, Upgrades, and applicable Storage tabs

See [Dashboards and overlays](ui/dashboards-and-overlays.md) for the complete key list.

## Why did I stop receiving ordinary storyteller raids?

By default, WD blocks random storyteller raids when the storyteller selects a faction managed by WD. Those factions attack through WD's world-map raid system instead, so their forces have an origin, travel time, and strength pool.

Quest raids, forced or scripted incidents, comms, dev tools, and WD world raids are not blocked by that setting. Storyteller raids from factions not controlled by WD remain allowed by default. Both behaviors can be changed in WD's raid settings.

## Which mod categories need extra caution?

Anything that rewrites diplomacy, world-map interactions, settlement ownership, base generation, or raid-point multipliers can overlap with WD. Test uncertain combinations on a copy of the save and include a full mod list and log when reporting a conflict.

## Where can I report bugs?

Use the public repository: [Likewise87/World-Domination-2.0](https://github.com/Likewise87/World-Domination-2.0). Include clear reproduction steps, the affected save if possible, your full mod list, and the relevant log.
