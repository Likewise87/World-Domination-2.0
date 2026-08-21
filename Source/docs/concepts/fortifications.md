# Fortifications

Fortifications are world-map defenses placed on tiles: **road blocks**, **spike traps / caltrops**, and **AT turrets**. They are not the same as outpost wall or IED upgrades on a generated defense map.

Players build them from an outpost (or, when enabled, a colony) Build menu. NPC settlements place them through the **Fortify** world action when threatened.

## What they do

| Type | Role |
| --- | --- |
| Road block | Slows hostile travelers that must pass the tile. Light / Medium / Heavy add increasing movement penalties and HP. |
| Trap | Damages hostile ground travelers when they leave the trapped tile. Friends and neutrals are safe. One traveler can trigger only a limited number of traps on its route. |
| AT turret | Automatic gun that fires at eligible hostile ground targets in a short world range. See [Mortars, anti-air and AT turrets](world-weapons.md). |

Construction launches a traveler that spends outpost offensive strength (colony builds use Construction skill instead and do not spend outpost strength). Canceling after launch usually does not refund that strength.

## Player workflow

1. Select an outpost and open its Build / fortification commands.
2. Choose the project and a valid tile within planning range.
3. Wait for the construction traveler to arrive and finish.
4. Use remove / clear commands when you want the tile open again.

**Mark no-fortify zone** paints tiles where allies (and neutrals, if enabled) may not place new blocks or traps. Existing fortifications stay. Hostile factions ignore those marks. Erase marks instantly when you change the plan.

Use fortification overlays on the world map to see what already exists before you commit crews.

## How NPCs fortify

Fortify is a weighted daily action. A settlement only fortifies when it is threatened, not on fortify cooldown, and has a valid placement ring.

After Fortify is chosen, WD rolls what to place among currently valid options (road block, trap, or AT turret). Traps need a road tile. AT turrets need an off-road site and a free turret slot under that settlement's tier cap.

Higher tiers can send multiple road-block or trap crews on one Fortify. AT turret crews are always a single crew. Crews can travel a limited distance or place near the home settlement.

## Fighting through fortifications

Hostile raids and other travelers can lose strength to traps and AT fire before they reach you. Your own travelers face the same hazards in enemy territory. Plan routes, clear traps, and use artillery when a corridor is sealed.

??? note "Advanced"
    Default Fortify weight **64**, fortify cooldown **4 days**, fortify traveler strength **50**, placement ring roughly **2 to 8** tiles from the settlement, max travel **30** tiles.

    Default place weights among valid options: road block **60%**, trap **30%**, AT turret **10%**.

    NPC AT caps by tier default to **1 / 2 / 3 / 4**. Max traps triggered per traveler: **3**.

    Full numbers: [Settings: World Actions](../settings/daily-actions.md) and [Settings: Road building](../settings/road-building.md).

## Related chapters

- [Outpost actions](../outposts/actions.md)
- [World actions, weights and cooldowns](world-actions.md)
- [Mortars, anti-air and AT turrets](world-weapons.md)
- [Travelers](../travelers.md)
- [World-map battles](../raids/world-map-battles.md)
- [Settings: Road building](../settings/road-building.md)
