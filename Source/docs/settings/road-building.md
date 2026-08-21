# Road building settings

This advanced page configures roads, road blocks, traps, AT Turrets, and decontamination projects. Road movement, work, and minimum Construction values are fallbacks used when Roads of the Rim is not active. Winter reduction is always applied.

## Roads

| Control | Default | What it changes |
|---|---:|---|
| Player outpost road range | 16 tiles | Maximum planning range from player outposts. |
| NPC road range | 25 tiles | Maximum range for NPC road attempts. |

Each road type exposes the same five controls.

| Road type | Movement difficulty | Work per segment | Dispatch strength | Min. Construction | Winter penalty reduction |
|---|---:|---:|---:|---:|---:|
| Dirt road | 0.70 | 250 | 50 | 5 | 15% |
| Stone road | 0.50 | 375 | 80 | 15 | 30% |
| Asphalt road | 0.30 | 500 | 125 | 25 | 50% |

Movement difficulty multiplies world path difficulty, so lower values are faster. Work is abstract project work. Dispatch strength is deducted when a road-builder traveler launches. Minimum Construction is the cumulative outpost requirement.

## Road blocks

| Control | Default | What it changes |
|---|---:|---|
| Range to build road blocks | 10 tiles | Maximum planning range for build and clear paths. |

| Road block | Work | Dispatch strength | Movement penalty | Max health |
|---|---:|---:|---:|---:|
| Light | 250 | 50 | +1.50 | 1,000 |
| Medium | 375 | 80 | +2.50 | 1,500 |
| Heavy | 500 | 125 | +4.00 | 2,500 |

The penalty is added after road multipliers when a ground traveler enters the tile. Max health determines placement health and how much hostile traffic the block can withstand.

## Traps

| Control | Default | What it changes |
|---|---:|---|
| Range to build traps | 10 tiles | Maximum planning range for trap build and clear paths. |

| Trap | Work | Dispatch strength | Damage | Max health |
|---|---:|---:|---:|---:|
| Spike trap | 250 | 50 | 100 | 500 |
| Caltrops | 375 | 80 | 200 | 1,000 |

| Control | Default | What it changes |
|---|---:|---|
| Max traps per traveler | 3 | Maximum traps one WD traveler can trigger. Later traps on its route are ignored. |

## AT Turrets

| Control | Default | What it changes |
|---|---:|---|
| Player global AT Turret cap | 50 | Maximum owned across all colonies and outposts. |
| AT Turrets per colony or outpost | 4 | Per-site ownership cap. |

Each turret type has independent construction and combat values.

| Type | Work | Dispatch strength | Min. Construction | Strength/HP | Damage | Cooldown | Range |
|---|---:|---:|---:|---:|---:|---:|---:|
| Light | 750 | 50 | 15 | 50 | 75 | 0.35 days | 4 tiles |
| Medium | 1,500 | 125 | 25 | 100 | 100 | 0.50 days | 5 tiles |
| Heavy | 2,250 | 175 | 35 | 150 | 125 | 0.75 days | 6 tiles |

| Accuracy control | Default |
|---|---:|
| Hit chance at 0 to 50% of range | 95% |
| Hit chance at 51 to 75% of range | 85% |
| Hit chance at 76 to 100% of range | 70% |

Dispatch strength is the traveling crew cost, not turret health. Strength/HP applies to newly built guns. Damage, cooldown, and range are live combat settings.

## Decontamination

| Control | Default | What it changes |
|---|---:|---|
| Range for decontamination | 20 tiles | Maximum planning range from an outpost. |
| Work needed to build | 350 | Abstract work for one polluted tile scrub. |
| Strength cost | 20 | Offensive strength paid when the decontamination traveler launches. |
| Pollution reduction | 40 percentage points | Pollution removed from each scrubbed world tile. |

Reset restores every value on this page and reapplies road movement settings immediately.
