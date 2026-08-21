# Outposts and food logistics settings

This page controls player outpost actions, founding requirements, production, food logistics, and the outpost option offered after conquest. Advanced-only controls are marked below. Reset this page restores both the outpost and food-logistics defaults.

## General settings

| Control | Default | What it changes |
|---|---:|---|
| Enable launch attack | On | Shows the Launch Attack action on player outposts. |
| Enable build roads | On | Shows road construction in the Build menu. Existing projects can still be cancelled when off. |
| Enable build road blocks | On | Shows road-block build and clear actions. |
| Enable build traps | On | Shows spike-trap build and clear actions. |
| Min. distance to establish outpost | 4 tiles | Required distance from settlements and other outposts. |
| Outpost build cost multiplier | 100% | Multiplies the base caravan-founded outpost cost. |
| Raid protection cooldown | 5 days | Protects a player outpost from being selected for another NPC raid after founding, launch, or failed defense. |
| Player outpost attack range | 25 tiles | Maximum target radius used when hostile settlements search for player outposts and by applicable player outpost attacks. |
| Player outpost defensive strength | 100 | Base local defensive strength before upgrades. |
| Pollution reduces fertility, animals, and fish | On | Applies the tile pollution multiplier to farming, hunting, and fishing ecology. Mining is unaffected. |
| Outpost upgrades cost materials | On | Requires the materials listed by each upgrade. |
| Outpost upgrades have research requirements | On | Enforces research prerequisites listed by upgrades. |

## Establishment requirements

All establishment checks default to **On**.

| Control | What turning it off allows |
|---|---|
| Require allowed biome for outpost type | Founding in biomes normally excluded by that outpost type. |
| Require fertile tiles for farming outposts | Farming or logging outposts on low-fertility or water-covered tiles. |
| Require animal abundance for hunting outposts | Hunting outposts on tiles with very few animals. |
| Require fish stocks for fishing outposts | Fishing outposts on coastal tiles with low fish stocks. The coast requirement remains. |
| Require hills for mining outposts | Mining outposts on flat tiles. |
| Require research for advanced outposts | Founding without the listed research projects. |
| Require nearby neutral/allied settlements | Founding types with a nearby-settlement requirement in isolated regions. |
| Require minimum pawns to found outpost | Founding with fewer pawns than the outpost type normally requires. |
| Require minimum cumulative skills | Founding and production without the type's cumulative skill minimum. |
| Require establishment cost | Founding without paying the wood or custom resource cost. |

## Production

| Control | Default | What it changes |
|---|---:|---|
| Production time multiplier | 100% | Multiplies cycle length. Lower values produce more frequent payout attempts. |
| Production output multiplier | 100% | Multiplies delivery quantities, silver, and rounded recruit output. |
| Warehouse aura bonus | 15% | Best nearby warehouse productivity bonus for eligible producers, virtual food, and academy XP. |
| Warehouse aura radius | 12 tiles | World-tile radius in which a warehouse can provide its aura. |
| Embassies may gain goodwill with hostile factions | On | Allows embassy cycles to improve goodwill with temporarily hostile factions. Permanent enemies remain excluded. |
| Silver budget per skill per delivery | 100 | Market-value budget used to derive crop, hunting, and mining baseline quantities. |
| Clamp outpost skills at level 20 | Off | When on, production math counts no skill above 20. |
| Occupant skill XP per successful payout | 5,000 XP | XP granted in each relevant skill to each occupant when a delivery actually launches. |
| Max skill level for outpost XP | 10 | No payout XP is awarded to a relevant skill at or above this level. |

### Academy

| Control | Default | What it changes |
|---|---:|---|
| Academy base XP per day | 2,000 XP | Daily XP per eligible student before teacher scaling. |
| Academy minimum teacher skill | 8 | Minimum selected-skill level needed to teach. |
| Academy teach cap offset | 3 | Student cap equals teacher level minus this offset. |
| Academy uses flat XP | Off | Off uses passions, global learning speed, and other vanilla learning modifiers. On grants the exact flat XP. |

### Experts

Expert bonuses scale toward their maximum at the reference skill level.

| Control | Default | Maximum effect |
|---|---:|---|
| Reference skill level | 20 | Skill level at which an expert reaches the configured maximum. |
| Strategist max bonus | 50% | Manual raid range and mortar or anti-air range. |
| Entertainer max bonus | 25% | Production output. |
| Cook max bonus | 25% | Production output and offensive recovery. |
| Doctor max bonus | 50% | Occupant healing and offensive recovery. |
| Engineer max bonus | 50% | Road work speed and defensive recovery. |
| Engineer construction radius max | 30% | Construction planning radius. |
| Warden max resistance bonus | 30% | Prisoner resistance reduction from the assigned Warden. |

### Strength recovery and healing

These controls appear only when advanced settings are shown.

| Control | Default | What it changes |
|---|---:|---|
| Defensive recovery, percent of max per day | 10% | Daily defensive recovery calculation. |
| Offensive recovery, percent of target cap per day | 15% | Daily offensive recovery before upgrade bonuses. |
| Minimum defensive recovery per day | 25 | Flat floor for defensive recovery. |
| Minimum offensive recovery per day | 80 | Flat floor for offensive recovery before upgrade bonuses. |
| Occupant healing severity per day | 2.0 | Daily severity removed from non-permanent injuries on stored occupants. Hospital upgrades multiply it. |

## Food logistics

Only **Activate Food Logistics** is shown in basic mode. The remaining controls require both food logistics and advanced settings.

| Control | Default | What it changes |
|---|---:|---|
| Activate Food Logistics | On | Enables virtual-food consumption and supply lines. |
| Consumption per pawn | 2.0 food/day | Daily virtual-food use per outpost pawn. |
| Daily food production per outpost, base | 3.0 food/day | Fixed production for every outpost before farming or hunting skill output. |
| Minimum virtual food tile multiplier | 80% | Floor applied to farming fertility or hunting animal-abundance multipliers. |
| Max food per outpost | 300 | Virtual-food storage capacity. |
| Max support radius | 25 tiles | Maximum distance at which a farming or hunting hub can assign food. |

## Outpost after conquest

| Control | Default | What it changes |
|---|---:|---|
| Offer outpost on conquered settlements | On | After a player conquest, leaves ruins and offers to establish a World Domination outpost after the map is exited. Off uses vanilla settlement-defeat behavior. |
| Pawns after conquering T1 settlement | 2 | Generated colonists when founding on former T1 ruins. |
| Pawns after conquering T2 settlement | 4 | Generated colonists when founding on former T2 ruins. |
| Pawns after conquering T3 settlement | 9 | Generated colonists when founding on former T3 ruins. |
| Pawns after conquering T4 settlement | 14 | Generated colonists when founding on former T4 ruins. |
| Min. relevant skill level for conquest founders | 4 | Raises generated founders' outpost-relevant skills to at least this level. |
