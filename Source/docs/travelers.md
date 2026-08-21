# Travelers

Travelers are WD world objects that move a mission and an abstract **strength pool** between tiles. NPC travelers do not contain generated pawns. Only the player forms real pawn Caravans.

Some traveler labels include the word "Caravan" because that is their in-game mission name. For NPC forces, the label still represents a strength pool rather than a pawn group.

## Traveler roles

### NPC military and development

- **Raider Caravan:** a ground raid force moving toward a settlement, player colony, outpost, traveler, or other valid target.
- **Raider Drop Pods:** a ballistic raid force that moves directly toward its target.
- **Expansion Caravan:** carries a faction's attempt to establish a new settlement.
- **Road Builder Caravan:** carries an NPC road project.
- **Trader Caravan:** carries a WD trade mission and its escort strength.

### Player outpost missions

- **Outpost Road Builder Caravan:** constructs a selected road route.
- **Outpost Road Block Crew:** builds or clears road blocks.
- **Outpost Spike Trap Crew:** builds a spike trap or caltrops.
- **Outpost Decontamination Crew:** reduces pollution around its destination.
- **Outpost Raider Caravan:** carries an attack launched from a player WD outpost.
- **Outpost Delivery:** transports a completed outpost production delivery.
- **Outpost Upgrade Caravan:** carries an ordered outpost upgrade.

### Artillery and interception

- **Mortar Shell:** a ballistic strike from an artillery-capable source.
- **AT Shell:** an anti-traveler shot from an AT turret.
- **Flak Shell:** an anti-air projectile fired at eligible airborne targets.
- **Rapid Response Caravan:** a fast intercept mission launched by a Rapid Response outpost.
- **Rapid Response Drop Pods:** the drop-pod version of a rapid-response dispatch.

### Settlement and diplomacy missions

- **Settlement Purchase Caravan:** carries a settlement purchase mission.
- **Settlement Gift Caravan:** carries a gift to a settlement.
- **Settlement Bribe Caravan:** carries a bribe or ceasefire arrangement.
- **Diplomacy Negotiate Caravan:** carries a negotiation mission.

## Reading a traveler

Select a traveler on the world map. Its inspection panel identifies its faction, role, current strength, destination, and travel state. Relationship highlights can show whether it is hostile, neutral, allied, or targeting the player.

Strength is the traveler's combat and mission pool. The origin commits that strength when the traveler launches. Travel, fortifications, pollution where enabled, artillery, interception, and battle can reduce it before arrival.

## Travel attrition

Ground travel gradually removes strength. The longer and harder the journey, the more of the departure pool can be lost. This makes distance strategically important:

- A distant raid can arrive weaker than it launched.
- A long construction mission can be vulnerable before completing its project.
- Roads can shorten exposure to attrition.
- Water-capable routing can connect otherwise separated land areas, but entering water carries its own movement difficulty.

Inspect both departure strength and current strength when evaluating an inbound threat.

## Encounters with player Caravans

A player's real pawn Caravan can clash with a hostile WD traveler on the world map. WD compares the Caravan's combat contribution with the traveler's remaining strength and resolves the encounter through the clash system.

The player side remains a real Caravan with pawns, inventory, carrying capacity, injuries, prisoners, and loot handling. The NPC side remains an abstract strength pool. A clash does not imply that the NPC traveler secretly contained pawns.

Avoid weak hostile travelers when your Caravan is carrying irreplaceable colonists or cargo. Intercepting a damaged raid traveler can be useful, but the player Caravan still accepts the risk of a real encounter.

## Mortars, AT turrets, and anti-air

World-map artillery creates visible shell travelers:

- Mortar shells can damage eligible hostile settlements or travelers.
- AT shells attack eligible ground travelers.
- Flak shells attack eligible airborne targets.

Mortar and AT hits remove strength from the target. Anti-air can intercept eligible pod or aerial travel, while flak shells themselves travel as world objects. Range, targeting filters, accuracy, cooldowns, and upgrades depend on the firing source and settings.

Rapid Response outposts offer a different defense. They dispatch an intercept traveler against selected hostile mission types within range. This can destroy or weaken a raid before it reaches a colony or outpost.

??? note "Advanced"
    Default ground travel loss is 1.5% of departure strength per hour. Cumulative travel loss is capped at 75% of departure strength.

    The default movement difficulty for entering a water-covered tile is 4.

    Combat results use the traveler's current strength after applicable attrition and damage. See [World-map battles](raids/world-map-battles.md) for the simulated win-chance curve and post-battle retention rules.

## Related chapters

- [Strength and tiers](concepts/strength-and-tiers.md)
- [Raids on you](raids/raids-on-you.md)
- [World-map battles](raids/world-map-battles.md)
