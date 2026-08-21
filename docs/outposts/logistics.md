# Food and logistics

Outpost occupants consume virtual food. This resource is tracked as a number at each outpost rather than as physical meals in inventory. Open the **Food** tab to inspect current storage, maximum capacity, daily production, local demand, incoming supply, outgoing allocations, and projected net change.

## Reading the Food tab

- **Current / max food** is the stored reserve. Surplus is wasted when storage is full.
- **Daily food net** includes local production, local consumption, food sent to other sites, and food received from them.
- **Local food balance** is the amount available before distribution after local production and demand are considered.
- **Recipients in range** lists outposts that this producer can supply.
- **Receiving Food From** lists active inbound supply lines.

Every outpost receives the configured universal base food production. Farming and Hunting Outposts can add skill-based virtual food production, modified by their tile conditions. Hydroponics adds flat daily food and a Silo raises storage capacity.

## Distribution modes

Food-producing outposts can use one of these allocation strategies:

### Smart deficit distribution

The **Smart** mode first covers reachable shortages, starting with the worst deficits and combining surplus from all Smart producers in range. Each producer then keeps an equal share of its remaining surplus. The rest is distributed by raising the lowest recipient net values toward the same positive balance.

Use Smart for a connected network that should adapt automatically when occupants, production, or food settings change.

### Equal

**Equal** divides surplus evenly among in-range outposts that currently have a deficit. It is predictable, but it does not account for the relative depth of each shortage as carefully as Smart mode.

### Feed

**Feed** first prioritizes outposts that can be fully supplied. Any food left after satisfying those sites is shared among the remaining recipients. Use it when keeping several smaller garrisons completely fed is preferable to partially supporting every site.

### Manual

**Manual** disables automatic allocation changes. Use the plus and minus controls to set each recipient's amount. Manual assignments do not adapt to new occupants, lost producers, range changes, or other world changes. Review them after every major transfer.

**Reset allocation** switches the producer to Manual and clears all food sent from it to other outposts.

### Keep here

**Keep here** is the producer's undistributed virtual food. It remains available to fill that producer's own reserve. In Smart mode, the system assigns each participating producer an equal share of the remaining surplus before equalizing recipient nets.

An optional **All to colony** strategy sends all available production from that hub only to the player's mapped colony, ignoring other outposts and prioritizing colonies at the top of the destination list.

## Range and network design

Food can move only between eligible sites within the configured food logistics radius. Use the **Food Supply Radius** overlay before founding remote sites. A chain of outposts is not automatically a relay unless each producer can directly reach the intended recipients under the active logistics rules.

Keep a positive daily net and a stored buffer at border outposts. A route that exactly balances at zero has no protection against population transfers, lost producers, or setting changes.

## Warehouse routing

Virtual food logistics and physical item logistics are separate:

- The Food tab moves abstract food between in-range outposts.
- A Warehouse Outpost stores physical items delivered by production cycles or shipments.

To route a production outpost's goods into storage, choose **Set delivery destination** and select a player warehouse. Clearing that destination restores delivery to the nearest colony. In the warehouse, set a separate **ship destination** for outbound stock, then ship selected goods by land or drop pod. Daily auto-shipping can send the entire stock when the warehouse has enough offensive strength.

Warehouse inventory does **not spoil**. This makes a warehouse a safe buffer for perishables and a useful collection point when the colony is distant or temporarily unsafe. Goods remain stored until a shipment is ordered or automatic shipping sends them.

The warehouse's productivity aura is separate from storage. It improves eligible nearby goods producers, virtual food producers, and Academy training. Only the strongest warehouse aura in range applies to a given producer, and Research Outposts are not affected.

## Preventing starvation

Low food is dangerous, not merely an efficiency penalty. Occupants can die of starvation when an outpost remains unfed.

To stabilize a failing site:

1. Check whether the problem is local demand, insufficient production, range, or an outdated Manual assignment.
2. Move the producer to Smart mode or increase its manual allocation.
3. Add or improve a Farming or Hunting Outpost within range.
4. Build Hydroponics for flat production or a Silo for a larger emergency reserve.
5. Reduce the number of eating occupants until supply recovers.
6. Convert suitable food carried by a player caravan into virtual food at the linked outpost when immediate relief is required.

Do not confuse warehouse food items with the outpost's virtual food reserve. Physical meals in warehouse storage do not automatically feed nearby outpost occupants.

??? note "Advanced"
    At default settings, a player Warehouse Outpost provides **+15% productivity** to eligible producers within its configured aura. Only the best warehouse in range applies. Logistics Network and Warehouse Forklifters each add a further 10 percentage points and 5 tiles to that warehouse's aura.

    Food capacity, universal production, consumption, logistics radius, starvation behavior, warehouse aura, and delivery strength costs are configurable. See [Settings: Outposts and food](../settings/outposts.md) and [Settings: Outpost skill scaling](../settings/outpost-skill-scaling.md).
