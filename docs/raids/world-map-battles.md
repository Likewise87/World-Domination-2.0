# World-map battles

WD gives NPC settlements recurring opportunities to act. These daily actions make factions grow, move, trade, improve territory, and fight without requiring the player to visit every map.

## Daily world actions

A settlement selected for activity can attempt an eligible action:

- **Grow:** add offensive strength at the settlement.
- **Raid:** commit offensive strength to a raid traveler.
- **Trader:** send a trade traveler to an eligible destination.
- **Build road:** dispatch a road-building traveler.
- **Fortify:** place defensive projects such as road blocks, traps, or AT turrets through fortify travelers.
- **Minor incident:** apply a smaller strategic incident.
- **Major incident:** apply a larger, less common strategic incident.

The action must still pass its own requirements. A selected settlement may lack a valid target, sufficient strength, a route, or an available cooldown.

Higher tiers are allowed more daily action attempts, and tier activity shares determine how much of each tier is considered. These settings are separate from the weights used to generate settlement tiers.

## Raid travelers and arrival

A world raid begins when a settlement spends offensive strength and creates a raid traveler. The traveler moves toward its target and can lose strength before arrival. Nearby same-faction settlements and eligible diplomatic allies can support attacks or defenses within the ally radius.

When a raid reaches an NPC settlement, WD resolves the battle through the strategic simulation. It compares effective attacker strength with total defender strength, then rolls against the configured win-chance curve. The chosen outcome also applies losses to the attacker, defender, and supporting allies.

The same simulation handles attacks against player outposts when automatic defense is chosen. Manual outpost defense instead opens a temporary combat map.

## Conquest and razing

An attacker victory can produce one of two strategic results:

- **Conquest:** the settlement changes hands. Surviving attacker strength is limited by the destination tier and only part of the new garrison is retained.
- **Raze:** the settlement is destroyed and leaves a temporary ruin instead of changing faction.

The raze roll makes successful raids less predictable. A faction may gain territory, or it may remove a rival site from the map.

When the player defeats an NPC settlement on a map, separate player conquest options can apply. Do not assume every player victory follows the NPC simulated conquest flow.

## Attacks on player outposts

A hostile raid traveler can target a WD outpost if player-outpost raids are enabled and the target is available. The arriving attack opens the defense decision:

1. Review attacker strength, outpost offense and defense, nearby allied support, and available occupants.
2. Choose automatic resolution to use WD's strategic battle model.
3. Choose manual defense to deploy eligible occupants on a temporary map.
4. After the result, surviving strength, injuries, captives, cooldowns, and ownership are handled according to the selected defense path.

Automatic resolution makes ally strength especially important because all eligible abstract support is included in the combat totals. Manual defense rewards direct tactical control but exposes real deployed pawns to map combat.

??? note "Advanced"
    The default attacker win chance is read from the attacker-to-defender strength ratio curve:

    - 0.10 ratio: 3%
    - 0.25 ratio: 10%
    - 0.50 ratio: 20%
    - 1.00 ratio: 42%
    - 1.50 ratio: 58%
    - 2.00 ratio: 70%
    - 3.00 ratio: 88%
    - 4.00 ratio: 94%
    - 5.00 ratio: 95%
    - 6.00 ratio: 99%

    Values between thresholds are interpolated by the battle model. Outcome severity then determines losses, so winning does not imply preserving the full attacking pool.

    A conquered site's default garrison retention is 20% of the applicable surviving strength. The default raze chance is 35%.

    Default daily action weights are Grow = 240, Raid = 200, Minor Incident = 80, Major Incident = 16, Build Road = 48, Trader = 48, and Fortify = 64. Weights are relative, and unavailable actions are excluded from a valid choice.

    Default daily action caps are 1 action for T1 and T2 settlements, and 2 actions for T3 and T4 settlements.

    The default raid action cooldown is 0.2 days. The default being-raided defense cooldown is 1 day.

    Allied forces use a default loss multiplier of 0.40, so supporting allies take 40% of the loss rate that would otherwise apply to the main force.

## Related chapters

- [World generation](../concepts/world-generation.md)
- [Strength and tiers](../concepts/strength-and-tiers.md)
- [Travelers](../travelers.md)
- [Raids on you](raids-on-you.md)
