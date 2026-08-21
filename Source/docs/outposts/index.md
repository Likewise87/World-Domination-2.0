# Outposts

Outposts extend your colony across the world map. They turn occupants and local conditions into food, goods, research, power, recruits, diplomacy, or military reach without requiring a permanent colony map.

## Founding an outpost

There are two ways to establish an outpost:

1. **From a player caravan.** Stop the caravan on the intended world tile, select it, and choose **Establish outpost**.
2. **On conquered settlement ruins.** After taking an enemy settlement, choose **Establish outpost** in the conquest outcome. The available outpost tier cannot exceed the conquered settlement's tier.

The establishment window is the final authority on whether a type can be built. It lists the required occupants, cumulative skills, research, materials, biome conditions, local productivity, nearby settlements, and minimum distance from other sites. Requirements that do not apply to conquest, such as caravan materials, are identified in the interface.

Choose the site before choosing the specialization. Farming depends on fertility, hunting on animal abundance, fishing on ocean-coast fish stocks, and mining on hilliness and tile modifiers. Recruiting, trading, and embassies need nearby neutral or allied settlements. See [Outpost types](types.md) for every specialization.

## Offensive and defensive strength

An outpost maintains two related strength pools:

- **Offensive strength** is the deployable budget used for raids, expeditions, deliveries, interceptors, and other launched actions. It also contributes when the outpost is attacked.
- **Defensive strength** represents permanent fortifications. It protects the site but is not sent away on offensive missions.

Launching an action immediately reserves or spends offensive strength. A busy outpost can therefore be less prepared for the next attack. Allow strength to recover before committing the same site to another operation.

When a defense uses a temporary battle map, the available offensive strength is also the pawn deployment budget. If the outpost cannot afford every occupant, only the selected pawns enter the battle. Unselected occupants remain abstracted at the outpost and do not participate.

After a successful defense, enable **Take prisoners** if surviving enemies should become outpost captives. This works for both automatic resolution and manual defense maps. Prisoners appear in the Pawns tab, where cumulative Social skill and a Warden expert can reduce their resistance.

## Outpost tabs

Select an outpost and use its inspector tabs to manage it:

- **Stats** explains production, strength, recovery, ranges, tile productivity, food demand, and other type-specific values.
- **Food** shows virtual food storage, daily production and consumption, incoming supply, and distribution controls. See [Food and logistics](logistics.md).
- **Pawns** manages occupants, prisoners, transfers, and stored animals, vehicles, or mechanoids.
- **Experts** assigns specialist roles supported by the outpost's humanoid population. See [Experts](experts.md).
- **Upgrades** spends stored or delivered materials on permanent improvements. See [Upgrades](upgrades.md).
- **Storage** appears on Warehouse Outposts and manages inventory and outbound shipping.

World-map commands around the inspector cover attacks, construction, range overlays, delivery destinations, and specialist actions. See [Outpost actions](actions.md).

## Operating priorities

1. Keep enough food at every occupied outpost.
2. Leave offensive strength in reserve when hostile travelers or raids are nearby.
3. Match occupants to the specialization's relevant skill.
4. Route valuable production to a warehouse when a colony delivery would be inconvenient.
5. Build defensive upgrades at exposed sites and production upgrades where tile conditions are already strong.
6. Use range overlays before placing support outposts so their food, warehouse, artillery, rapid response, and ally coverage overlaps useful targets.

??? note "Advanced"
    At default settings, offensive strength recovers each day by the greater of **15% of its occupant-based target cap** or **80 strength**. Defensive strength recovers by the greater of **10% of its target** or **25 strength**. Recovery bonuses from experts and upgrades multiply the applicable result.

    Every player outpost begins with a base defensive strength target of **100** before upgrades and other bonuses.

    After a player outpost is raided, the default raid-protection cooldown is **5 days**. These values are configurable under [Settings: Outposts and food](../settings/outposts.md).
