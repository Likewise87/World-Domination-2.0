# World generation

World Domination assigns every NPC settlement a **tier** and a **type**. Tier sets the broad power band. Type determines the settlement theme and the WD layout used when the map is generated.

## Settlement tiers and types

| Tier | Strategic role | Settlement types |
| --- | --- | --- |
| T1 | Resource camp | Logging camp, Mining camp, Farming camp |
| T2 | Developed village | Production village, Slave village |
| T3 | Fortified regional base | Fortress, shown with the world-map label **Town** |
| T4 | Major faction stronghold | Citadel |

T1 sites are common and form most of the early strategic landscape. T2 sites are less common and stronger. T3 fortresses and T4 citadels are rare centers of faction power.

Tier and type are WD data attached to the settlement. They affect systems such as starting offensive strength, defensive baseline, action activity, attack reach, and the layout selected for a visit.

## Visiting settlements

When you visit or attack an NPC settlement, WD generates a map using its own tiered layouts. A logging camp should not resemble a citadel, and a tribal site can use a different layout family from an industrial site.

This map-generation ownership can conflict with other mods that replace settlement base generation, such as [Vanilla Base Generation Expanded](https://steamcommunity.com/sharedfiles/filedetails/?id=3209927822). If a visited base has the wrong layout, fails to generate, or combines incompatible structures, check for another active base-generation mod before changing WD balance settings.

[Visit Settlements](https://steamcommunity.com/sharedfiles/filedetails/?id=3247900860) is explicitly incompatible because it overlaps this part of the game.

## WD: World Setup

During world setup, select the WD icon to open **WD: World Setup**. Use it before settling to review settlements, roads, and allegiances.

The setup window can recreate settlements. World Generation settings control the relative tier weights used during assignment. Changing those values does not alter the daily activity share of each tier.

## Rerolling tiers

Use the reroll or recreate function when you want a different strategic distribution. Review the world after the operation:

1. Turn on **Settlement tier labels** in the WD world-map menu.
2. Check the area around the intended starting tile.
3. Look for the expected mix of common T1 camps, some T2 villages, and rare T3 or T4 sites.
4. Reroll again before settling if the result does not fit the intended campaign.

Rerolling changes the assigned tier and associated type or layout selection. It does not mean that each faction receives an equal number of every tier.

??? note "Advanced"
    Default world-generation weights are T1 = 150, T2 = 45, T3 = 4, and T4 = 1.

    The total weight is 200. This gives approximate starting shares of 75% T1, 22.5% T2, 2% T3, and 0.5% T4.

    The daily action tier shares are separate settings: T1 = 0.20, T2 = 0.28, T3 = 0.38, and T4 = 0.60. They control how much of each tier participates in daily world actions. They are not settlement-generation probabilities.

## Related chapters

- [Strength and tiers](strength-and-tiers.md)
- [Travelers](../travelers.md)
- [World-map battles](../raids/world-map-battles.md)
