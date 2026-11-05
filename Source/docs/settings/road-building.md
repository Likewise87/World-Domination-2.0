# Building Projects settings

Settings for Building projects that can be executed by player Outposts and for the most part also by the Colony and NPC settlements. Global cost multipliers (strength, materials, build time) always apply. Covers roads, bridges, trenches, road blocks, traps, AT Turrets (build and combat), and decontamination. Road movement values below apply only when Roads of the Rim is not active; Work, strength, and min Construction always apply to WD projects. Winter reduction always applies. Clear/deconstruct use strength and build time, not materials.

| Control | Default | What it changes |
|---|---:|---|
| Strength cost multiplier | 100% | Scales dispatch strength for build and clear/deconstruct (roads, bridges, trenches, blocks, traps, AT, decontamination). Per-type strength sliders below are bases. At 0% strength is free and hidden in the build menu. |
| Material cost multiplier | 100% | Scales world-build material counts (not outpost upgrades). Clear/deconstruct stay free of materials. At 0% materials are free and hidden in the build menu. |
| Building time multiplier | 100% | Scales prep duration for build and clear/deconstruct. Per-type Work sliders below are bases. At 0% prep is instant. |

## Roads

| Control | Default | What it changes |
|---|---:|---|
| Player outpost road range | 16 tiles | Maximum planning range from player outposts. |
| NPC road range | 40 tiles | Maximum range for NPC road attempts. |

Each road type exposes the same five controls.

| Road type | Movement difficulty | Work per segment | Dispatch strength | Min. Construction | Winter penalty reduction |
|---|---:|---:|---:|---:|---:|
| Dirt road | 0.70 | 250 | 50 | 5 | 30% |
| Stone road | 0.50 | 375 | 80 | 15 | 50% |
| Asphalt road | 0.30 | 500 | 125 | 25 | 75% |

Movement difficulty multiplies world path difficulty, so lower values are faster. Work is abstract project work. Dispatch strength is deducted when a road-builder traveler launches. Minimum Construction is the cumulative outpost requirement.

Player material costs (per tile) are XML-authored in `Common/Defs/WorldBuild/WD_Roads.xml`: Dirt free; Stone 30 any stone; Asphalt 20 steel + 20 chemfuel. Taken from the colony map and warehouse outposts when the crew launches.

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

Player material costs (per placement) are in `Common/Defs/WorldBuild/WD_RoadBlocks.xml`: Light 30 wood; Medium 60 any stone + 25 steel; Heavy 80 any stone + 50 steel.

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

Player material costs (per placement) are in `Common/Defs/WorldBuild/WD_SpikeTraps.xml`: Spike 40 wood; Caltrops 60 steel.

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

Player material costs (per gun) are on each AT `WorldObjectDef` in `Common/Defs/WorldBuild/WD_AT_Turrets.xml`: Light 50 steel + 1 component; Medium 60 steel + 2 components; Heavy 10 plasteel + 3 components.

## Decontamination

| Control | Default | What it changes |
|---|---:|---|
| Range for decontamination | 20 tiles | Maximum planning range from an outpost. |
| Work needed to build | 350 | Abstract work for one polluted tile scrub. |
| Strength cost | 20 | Offensive strength paid when the decontamination traveler launches. |
| Pollution reduction | 40 percentage points | Pollution removed from each scrubbed world tile. |

Reset restores every value on this page and reapplies road movement settings immediately.
