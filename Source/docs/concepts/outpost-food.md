# Outpost food

Outpost occupants eat **virtual food**, a number tracked on each outpost rather than physical meals in a stockpile. If that number stays empty while people still need to eat, occupants can starve.

This page explains why the system exists and how to keep a network alive. For distribution modes, warehouse routing, and tab fields, see [Food and logistics](../outposts/logistics.md).

## Virtual food vs physical meals

| System | What it is |
| --- | --- |
| Virtual food | Abstract daily production, consumption, and transfers between outposts |
| Warehouse inventory | Physical items you can ship to a colony or another warehouse |

Physical food sitting in a Warehouse Outpost does **not** automatically feed nearby outposts. Convert suitable food from a player caravan into virtual food at the linked outpost when you need an emergency top-up.

## How an outpost stays fed

Every outpost receives a small universal base production. Farming and Hunting Outposts add skill-based production modified by the tile. Hydroponics adds flat daily food. A Silo raises storage capacity.

Occupants consume food each day. When current food reaches zero and the outpost is still not receiving a positive pulse, starvation checks can kill an occupant.

Use the **Food** tab and the **Food Supply Radius** overlay to see:

- current / max storage
- local production and demand
- who this site can supply
- who is already sending food here

## Network design

Food moves only within the configured logistics range. A chain of outposts is not a free relay. Each producer must be able to reach the recipient directly under the active rules.

Practical habits:

1. Keep at least one Farming or Hunting producer within range of every garrison.
2. Prefer **Smart** distribution for a living network; use **Manual** only when you will maintain it.
3. Leave a stored buffer at border and Rapid Response sites. A net of exactly zero fails the moment occupancy rises or a producer is lost.
4. Add Hydroponics or a Silo before founding far from your food belt.

Dashboard outpost rows are sorted by current food so hungry sites rise to the top.

## Starvation and recovery

When an outpost is critically low, treat it as an emergency:

1. Fix allocation (Smart or a higher Manual share).
2. Reinforce production in range, or reduce occupants.
3. Deliver convertible food by caravan if the site still has people alive.

??? note "Advanced"
    Defaults: food logistics on; **2.0** food consumed per pawn per day; **3.0** base production per outpost per day; **1.0** extra production per relevant skill point on Farming / Hunting; max food **300**; logistics range **25** tiles. Hydroponics and Silo values are upgrade defs. Full controls: [Settings: Outposts and food](../settings/outposts.md).

## Related chapters

- [Food and logistics](../outposts/logistics.md)
- [Outpost upgrades](../outposts/upgrades.md)
- [Outpost types](../outposts/types.md)
- [Dashboards and overlays](../ui/dashboards-and-overlays.md)
- [Settings: Outposts and food](../settings/outposts.md)
- [Settings: Outpost skill scaling](../settings/outpost-skill-scaling.md)
