# Outpost tooling

Working outposts need **virtual tooling**, much like occupants need virtual food. Mining outposts can either deliver their goods to the colony, or turn them into tooling for other outposts. They cannot do both. Switch it with the **Output** gizmo on the outpost.

## Who needs it

Mining, Farming, Hunting, Fishing, Ranch and production outposts each keep a **tooling stockpile**, like virtual food. It drains per day by a flat amount plus an amount per pawn living there (prisoners do not count), both set by the outpost's tier: **Tier 1: 10 + 3 per pawn, Tier 2: 20 + 5 per pawn, Tier 3: 30 + 7 per pawn**. Built upgrades add any `toolingNeed` they declare in XML (default 0, so an advanced mine upgrade can need more than a basic one). The stockpile holds up to 200 (setting).

When the stockpile is **empty**, the outpost loses **66%** of its output (items and virtual food). New outposts start with an empty stockpile.

## Who makes it

A mining outpost set to **Generate tooling** delivers nothing to the colony. Its output becomes tooling instead:

```
tooling per day = Σ (items per cycle × item value) / cycle days
```

Item values are ratios. Defaults: **plasteel 3, steel 2, wood 1**. An item with no value gives no tooling. Outposts that generate tooling need none themselves.

Generators share their output within the logistics range. First they cover each outpost's daily need (nearest first), then the leftover refills the emptiest stockpiles. Several generators can feed one outpost, and surplus beyond full stockpiles is wasted.

Outposts that need tooling also accept it by hand: transport pods carrying steel, plasteel, wood (or any item with a tooling value) landed on them are converted straight into their stockpile, up to its cap. Whatever does not fit goes to the usual storage or is refunded.

## Changing and adding items

- **In game:** Settings > Outposts and food > Outpost tooling > Tooling values. Edit any ratio, or **Add item** to give any item a value (for example a pickaxe worth 50 wood).
- **In XML:** add a `TSA_WorldDomination.WdToolingValueDef` with a `thingDef` and a `value`. See `Defs/Tooling/WD_ToolingValues.xml`.

The role follows the production choice rule: a change applies at once until the cycle locks (after its first quarter), and from the next cycle after that.

You can also assign tooling by hand. Open the **Supply** tab (Tooling page) on a generator, switch to **Manual**, and use **+/-** per outpost. A manual generator sends tooling only through its assignments; the rest is shown as Unassigned.

??? note "Advanced"
    Settings: master switch, stockpile size (200), output lost without tooling (66%), and the per-item values. With the master switch off nothing needs tooling and every outpost delivers normally.

## Related chapters

- [Outpost food](outpost-food.md)
- [Outpost power](outpost-power.md)
- [Settings: Outposts and food](../settings/outposts.md)
