# Mid Game and Late Game

Mid Game and Late Game adds two stages of strategic pushback as the player's outpost network grows. A stage activates when either its world-strength share threshold or its absolute outpost-strength threshold is reached. Late Game replaces Mid Game when both are eligible.

## Global controls

| Control | Default | Range or availability |
|---|---:|---|
| Enable Mid Game and Late Game | On | On or Off |
| Periodic goodwill drain | On | Shown while Mid/Late Game is enabled |
| Goodwill drain interval (days) | 10 days | 1 to 60; shown while goodwill drain is enabled |

Disabling Mid/Late Game hides and disables all escalation effects below. Goodwill drain skips permanent enemies.

## Bribe cost

Bribe cost is always available, even when Mid/Late Game is disabled.

| Control | Default | Range |
|---|---:|---:|
| Settlement silver per strength | 2.0 | 0.5 to 10.0 |
| Raid silver per strength (Early) | 1.5 | 0.5 to 10.0 |
| Raid silver per strength (Mid) | 2.0 | 0.5 to 10.0 |
| Raid silver per strength (Late) | 2.5 | 0.5 to 10.0 |

The settlement rate prices ceasefire bribes. The three raid rates price a bribe against a specific raid traveler according to the active escalation stage.

## Mid Game

| Control | Default | Range or availability |
|---|---:|---|
| Activate at global strength share | 15% | 0% to 100% |
| Activate at outpost strength | 6,000 | 100 to 25,000 |
| Player raid bias | 25% | 0% to 200% |
| Enemy growth multiplier | 1.50x | 1.00x to 3.00x |
| Attack range bonus | 50% | 0% to 200% |
| Scale Ally radius in Mid game | On | On or Off |
| Ally radius bonus | 40% | 0% to 200%; shown while ally scaling is enabled |
| Garrison boost | 15% | 0% to 100% |
| Expansion creep max tiles from parent | 4 tiles | 1 to 12 |
| Only fire T4 Mortars at player while Mid Game is active | Off | On or Off |
| Only fire T4 Anti Air at player while Mid Game is active | Off | On or Off |
| Outpost incidents (Mid Game) | On | On or Off |
| Outpost incident severity | 100 | 10 to 500 |
| Daily outpost incident chance | 3.75% | 0% to 100% |
| Goodwill drain per pulse | 4 | 0 to 50; shown while goodwill drain is enabled |

The incident severity and chance sliders remain editable even if the incident toggle is off. Enabling either Mid Game T4 player-target toggle also enables the corresponding Late Game toggle.

## Late Game

| Control | Default | Range or availability |
|---|---:|---|
| Activate at global strength share | 25% | 0% to 100% |
| Activate at outpost strength | 10,000 | 100 to 25,000 |
| Player raid bias | 50% | 0% to 200% |
| Enemy growth multiplier | 2.00x | 1.00x to 3.00x |
| Attack range bonus | 100% | 0% to 200% |
| Scale Ally radius in Late game | On | On or Off |
| Ally radius bonus | 100% | 0% to 200%; shown while ally scaling is enabled |
| Garrison boost | 30% | 0% to 100% |
| Expansion creep max tiles from parent | 8 tiles | 1 to 12 |
| Only fire T4 Mortars at player while Late Game is active | On | On or Off |
| Only fire T4 Anti Air at player while Late Game is active | On | On or Off |
| Outpost incidents (Late Game) | On | On or Off |
| Outpost incident severity | 200 | 10 to 500 |
| Daily outpost incident chance | 7.5% | 0% to 100% |
| Goodwill drain per pulse | 10 | 0 to 50; shown while goodwill drain is enabled |

Threshold normalization keeps Late Game's share and strength thresholds at or above their Mid Game counterparts. T4 player targeting also requires the relevant master weapon toggle under [NPC Artillery (T4)](t4-mortar.md).

## Related settings

- [Raids on Player](raid-points.md)
- [World Map Raids](world-raids.md)
- [Diplomacy, Buffs & Debuffs](diplomacy.md)
