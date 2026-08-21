# Experimental settings

These options expose systems that may substantially change world behavior, performance, or balance. Defaults below come from `Settings.cs`.

## Outpost strength budgets

| Control | Default | What it changes |
|---|---:|---|
| Outpost withdrawal uses strength budget | On | Checks selected pawn withdrawal against current uncapped offensive strength with a 5% allowance. Over-budget selections open resolution choices. |
| Outpost defense deployment uses strength budget | On | Limits pawns selected for Fight Manually according to current offensive strength. |

## Base generation

| Control | Default | What it changes |
|---|---:|---|
| Adaptive terrain prep | On | Replaces blocked terrain under a WD/KCSG settlement layout when needed. |
| Always clear KCSG rectangle | Off | When on, always prepares the full layout rectangle instead of using the blocked-area threshold. |
| Nuke when blocked above | 25% | With adaptive prep on and always-clear off, prepares blocked cells when the unbuildable share exceeds this value. |
| Blend KCSG rectangle | On | Blends prepared layout terrain with the surrounding map. |

## Target-of-opportunity retargeting

| Control | Default | What it changes |
|---|---:|---|
| Enable target-of-opportunity retargeting | On | Allows an in-flight raid near a weaker static settlement or outpost to change target. It never retargets onto another moving traveler. |
| Eligibility roll chance | 15% | Cheap initial roll before target strength calculations. |
| Required ratio advantage to switch | 0.25 | Required improvement in attacker-to-defense ratio over the current target. |
| Max retargets per raid | 2 | Target-of-opportunity changes allowed for one raid. |
| Max target changes per raid | 3 | Lifetime total across target-of-opportunity and post-victory marauding. |

## Post-victory marauding

| Control | Default | What it changes |
|---|---:|---|
| Continue raiding after conquest | On | Allows a surviving auto-resolved raid to select another nearby target after victory. |
| Chance to continue after a win | 50% | Roll made immediately after victory. |
| Minimum surviving strength to continue | 500 | Required remaining raid strength. |
| Max chained targets | 3 | Maximum consecutive victories that may continue into another target. |

## Settlement-launched ambushes

| Control | Default | What it changes |
|---|---:|---|
| Settlement ambush of passing travelers | On | Lets eligible NPC settlements launch interceptors against hostile passing traders, mission travelers, uninvolved raids, or real player caravans. |
| Ambush chance | 50% | Initial ambush roll before strength calculations. |
| Minimum strength ratio to launch | 1.60 | Settlement available raid strength divided by passing target strength. |
| Max relative strength to send | 2.0x | Caps interceptor strength relative to the target. |
| Minimum settlement tier to ambush | T2 | Lowest NPC settlement tier allowed to watch and launch. |
| Max concurrent ambushes | 8 | World cap for active settlement ambushes. Set to 0 for unlimited. |
| Ambush watch range | 5 tiles | Radius in which eligible passing targets are observed. |

## World actions and raid logic

| Control | Default | What it changes |
|---|---:|---|
| Colony world Build | On | Adds world-map road, road-block, and trap construction to the player colony settlement. |
| Player conquest can raze | Off | Applies the automatic raid raze roll to player or simulated player conquests. |
| Enable First Outpost quest | On | Allows the First Outpost quest. |
| Enable Common Enemy Settlement quest | On | Allows the common-enemy settlement quest. |
| Enable Colony Road Link quest | On | Allows the colony road-link quest. |
| Enable World Domination Victory quest | On | Allows the WD victory quest and updates an active game immediately when changed. |
| NPC AT Turrets target player WD travelers | On | Allows hostile NPC guns to engage player-faction ground WD travelers. |
| NPC AT Turrets target player caravans | On | Allows hostile NPC guns to engage real player pawn caravans. |
| Allow opportunity features from the start | On | Lets target-of-opportunity, marauding, and ambush systems ignore their normal mid or late escalation gate. |
| Enable world-map sounds | On | Enables World Domination sound effects on the world map. |

## World-map icons

| Control | Default | What it changes |
|---|---:|---|
| Always show outpost traveler icons | On | Prevents outpost and WD traveler icons from collapsing due to zoom. |
| Always show settlement icons | On | Prevents settlement icons from collapsing due to zoom. |

## Controls and transfer behavior

| Control | Default | What it changes |
|---|---:|---|
| World map overlay hold key | Left Alt | Modifier used with WD page and overlay shortcuts. |
| Auto-add arrivals by default | On | New outposts default to automatically adding arriving caravans. |
| Give food on prisoner recruit transfer | On | Supplies travel food when a recruited prisoner transfers from an outpost. |
| Give food on all player pawn transfers | On | Supplies travel food on other player-pawn transfers from outposts. |
| Show outpost requirements preview in WD menu | Off | Adds the outpost requirement preview to the World Domination menu. |

## Outpost upkeep

| Control | Default | What it changes |
|---|---:|---|
| Enable outpost upkeep | Off | Enables periodic silver upkeep for outpost occupants. |
| Silver per occupant | 30 | Silver charged for each occupant per interval. |
| Upkeep interval | 15 days | Time between upkeep charges. |

## Pollution

| Control | Default | What it changes |
|---|---:|---|
| Pollution strength damage | On | Enables exit damage for selected ground WD travelers and daily pollution damage for NPC settlements and WD outposts. |
| Waster pirate pollution immunity | On | Exempts PirateWaster factions from traveler damage, site damage, and auto-decontamination. |
| Raider travelers take damage | On | Applies pollution exit damage to ground raids and Rapid Response travelers. |
| Expansion travelers take damage | Off | Applies exit damage to expansion travelers. |
| Construction travelers take damage | Off | Applies exit damage to road, block, trap, and fortify crews. |
| Traders and logistics take damage | Off | Applies exit damage to ground trader, delivery, upgrade, buy, and gift travelers. |
| Player WD travelers take damage | Off | Permits the selected type rules to affect player-faction WD travelers. |
| Ignore pollution below | 6% | No traveler exit damage below this tile pollution. |
| Damage at threshold | 6 | Strength damage at the threshold end of the scaling curve. |
| Damage at full pollution | 400 | Strength damage at 100% pollution. |
| Site damage radius | 2 tiles | Radius sampled for daily settlement and outpost pollution damage. |
| NPC decontamination strength cost | 10 | Strength paid for an NPC auto-decontamination crew. |
| Add pollution to path cost | On | Makes supported WD traveler routing account for polluted tiles. |
| Repath for pollution changes | Off | Allows active routes to be recalculated as pollution conditions change. |
| Pre-commit pollution cancellation | On | Cancels a route before commitment when pollution makes it invalid under the active rules. |

## Verbose logging

This control is on the main settings page rather than inside the Experimental window.

| Control | Default | What it changes |
|---|---:|---|
| Verbose logging | Off | Writes additional diagnostic information to the RimWorld log. Enable it only while investigating a problem because it can produce substantial output. |
