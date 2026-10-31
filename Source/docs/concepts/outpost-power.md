# Outpost power

Outposts need power. A **power outpost** either feeds the colony's power nets (the old behaviour) or supplies other outposts. It cannot do both. Switch it with the **Power** gizmo on the power outpost.

## How supply works

- Every outpost draws a base **1000 W** (setting) plus any `powerWattsNeed` its built upgrades declare in XML (default 0).
- A power outpost set to **Supply outposts** splits its watts between player outposts in range. The range is the food logistics range (25 tiles by default). Several plants can feed one outpost.
- An outpost is **powered** only when its whole need is covered. Nearest outposts are served first, and a plant's leftover watts go to the next outpost it can fully cover.
- Power outposts power themselves. Warehouses are handled separately and do not draw power yet.

## Powerlines

A powerline tower adds a **6-tile radius** of power range around itself. Outposts inside that radius draw from every supplying power outpost the tower is chained to.

**Build > Build powerline** works like the other world buildings: the outpost accumulates progress from its Construction skill, then a crew carrying expedition strength and the materials walks to the tile and raises the tower. You can cancel a project from the outpost's gizmo.

- **One powerline per outpost.** An outpost supports a single tower, built or in progress.
- **No two powerlines within 5 tiles** of each other (built or planned).
- **Inside the power range.** The tile must already be covered, either by a supplying power outpost's range or by another tower's radius, so the covered area never has gaps. Towers chain outward from a plant this way.
- Powerlines cost materials, the Electricity research and Construction skill, and can be dismantled from their own gizmo. A tower only counts while it is connected to a plant that supplies outposts.

## Without power

| Outpost | Effect |
| --- | --- |
| Production (items and virtual food) | 50% output |
| Research | 50% research speed |
| Recruiting and Trading | 50% radius |
| Rapid Response | Cannot launch drop pods |
| Automatic turrets (upgrades) | No turrets on the defense map, no defensive strength |
| Mortar | 50% range |
| Anti-air | 33% less accuracy |

You can also assign watts by hand. Open the **Supply** tab (Power page) on a plant that supplies outposts, switch to **Manual**, and use **+/-** per outpost. A manual plant sends power only through its assignments, and an outpost is powered only when it receives its full need.

??? note "Advanced"
    Settings (Outposts and food > Outpost power grid): master switch, base need, unpowered effectiveness (50%), unpowered range (50%), anti-air accuracy loss (33%), powerline radius (6 tiles), powerline build work and crew strength. With the master switch off every power outpost feeds the colony and nothing needs power.

## Related chapters

- [Outpost food](outpost-food.md)
- [Outpost types](../outposts/types.md)
- [Settings: Outposts and food](../settings/outposts.md)
