# World Actions

World Actions controls how NPC settlements receive daily action opportunities, which actions they prefer, and how often a settlement may repeat an action. Weights are relative likelihoods, not fixed percentages. Develop competes only when a settlement is at 95% to 100% of its tier maximum strength.

## Daily Actions per Settlement Tier

| Control | Default | Range |
|---|---:|---:|
| Tier 1 | 0.20 actions/day | 0.05 to 2.50 |
| Tier 2 | 0.28 actions/day | 0.05 to 2.50 |
| Tier 3 | 0.38 actions/day | 0.05 to 2.50 |
| Tier 4 | 0.60 actions/day | 0.05 to 2.50 |

Each settlement contributes its tier value to its faction's daily action share.

## Action Likelihood

| Control | Default weight | Range |
|---|---:|---:|
| Raid likelihood | 200 | 0 to 400 |
| Minor Incident likelihood | 80 | 0 to 400 |
| Major Incident likelihood | 16 | 0 to 400 |
| Build Road likelihood | 48 | 0 to 400 |
| Trader Caravan likelihood | 48 | 0 to 400 |
| Fortify likelihood | 64 | 0 to 400 |
| Develop likelihood | 240 | 0 to 400 |
| Include Develop in the percentages above | Off | On or Off |

The percentage checkbox changes only the percentages displayed in the settings menu. It does not alter action rolls.

## NPC Fortify

### Placement rules

| Control | Default | Range |
|---|---:|---:|
| Min distance from settlement | 2 tiles | 1 to 20 |
| Min distance from other settlements | 2 tiles | 0 to 20 |
| Max distance from settlement | 8 tiles | 2 to 30 |
| Max travel to place fortifications | 30 tiles | 5 to 80 |
| Territory link range | 35 tiles | 10 to 80 |
| Fortify traveler strength | 50 | 10 to 200 |
| Clear fortifications on builder loss | Off | On or Off |
| Enable mark tiles where allies may not build fortifications | On | On or Off |
| Apply to neutral too | On | On or Off |

Maximum distance is automatically kept at or above minimum distance. The neutral option is shown only while no-fortify marks are enabled. Hostile factions ignore those marks.

### What to place

| Control | Default | Range |
|---|---:|---:|
| Road block chance | 60% | 0% to 100% |
| Trap chance | 30% | 0% to 100% |
| AT Turret chance | 10% | 0% to 100% |

These are relative weights among currently valid choices and are normalized for the roll. Traps require a road tile. AT Turrets require an off-road site and an available turret slot.

### Multi-caravan launches

| Control | Default | Range |
|---|---:|---:|
| T1 chance of 2 caravans | 25% | 0% to 100% |
| T2 chance of 2 caravans | 50% | 0% to 100% |
| T3 chance of 2 caravans | 100% | 0% to 100% |
| T4 chance of 3 caravans | 30% | 0% to 100% |

These chances apply to road-block and trap crews, not AT Turret crews. T4 otherwise sends two crews.

### AT Turret caps

| Control | Default | Range |
|---|---:|---:|
| T1 max AT Turrets | 1 | 0 to 8 |
| T2 max AT Turrets | 2 | 0 to 8 |
| T3 max AT Turrets | 3 | 0 to 8 |
| T4 max AT Turrets | 4 | 0 to 8 |

??? note "Advanced"
    The following sections appear only when **Show advanced settings** is enabled.

## Settlement Action Capacity

| Control | Default | Range |
|---|---:|---:|
| Tier 1 action cap | 1 | 1 to 5 |
| Tier 2 action cap | 1 | 1 to 5 |
| Tier 3 action cap | 2 | 1 to 5 |
| Tier 4 action cap | 2 | 1 to 5 |

The cap is the maximum number of distinct actions a settlement can initiate in one day.

## Action Specific Cooldowns

| Control | Default | Range |
|---|---:|---:|
| Cooldown after Road Building | 0.1 days | 0 to 15 |
| Cooldown after Expansion | 14.0 days | 0 to 15 |
| Cooldown after Raiding | 0.2 days | 0 to 10 |
| Defense Shield | 1.0 day | 0 to 10 |
| Incident Cooldown | 2.0 days | 0 to 10 |
| Cooldown after Trader Caravan | 1.0 day | 0 to 10 |
| Cooldown after Fortify | 4.0 days | 0 to 15 |

Each cooldown blocks only its associated action. Defense Shield prevents the settlement from being raided again but does not stop it from acting as an attacker. Growth itself has no cooldown.
