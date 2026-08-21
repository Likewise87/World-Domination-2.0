# Caravans and travelers settings

This page configures World Domination travelers, water routing, WD trader caravans, outpost deliveries, trade goodwill, and storyteller traders. Here, **traveler** means a World Domination world object such as a raid, expansion party, or road builder. It does not mean every vanilla pawn caravan.

## Travel and attrition

| Control | Default | What it changes |
|---|---:|---|
| Strength attrition per hour | 1.5% | Percentage of current strength lost by a WD traveler each travel hour. |
| Maximum travel strength loss | 75% | Caps attrition loss relative to departure strength. At the default, at least 25% remains and attrition alone does not expire the traveler. |

## Water travel

| Control | Default | What it changes |
|---|---:|---|
| Allow caravans to travel over water | On | Enables special water-capable routing for WD travelers. This is also managed by performance presets. |
| Travelers only cross water if no land route exists | On | Tries standard land routing first. When off, land and water-capable routes are compared. |
| Traveler water tile movement difficulty | 4.00 | Difficulty applied when a WD traveler enters a water-covered tile. |
| Skip water path if land path is shorter than | 1.5 days | Avoids the more expensive water-route calculation for short land routes. Set to 0 to always calculate it. |

## Trader caravans

| Control | Default | What it changes |
|---|---:|---|
| Trader caravan strength cost | 100 | Strength paid by the sending NPC settlement. |
| Sender reward on arrival | 250 | Strength returned to the sender after successful arrival. |
| Receiver reward on arrival | 150 | Strength granted to the receiving WD settlement. Player outposts are not eligible receivers. |
| Mutual goodwill on arrival | 4 | Goodwill gained by both factions after a successful WD trade. |
| Minimum days between WD traders to player colony | 2 days | Per-colony cooldown measured from dispatch. |
| WD trader destination search radius | 50 tiles | Maximum path distance used to find a neutral or allied destination. |
| Upgrade chance T1 to T2 | 25% | Promotion chance when a trade reward reaches the T1 strength cap. |
| Upgrade chance T2 to T3 | 15% | Promotion chance when a trade reward reaches the T2 strength cap. |
| Upgrade chance T3 to T4 | 5% | Promotion chance when a trade reward reaches the T3 strength cap. |
| Escort strength floor, T1 sender | 75 | Minimum interception strength for a T1 trader. |
| Escort strength floor, T2 sender | 150 | Minimum interception strength for a T2 trader. |
| Escort strength floor, T3 sender | 300 | Minimum interception strength for a T3 trader. |
| Escort strength floor, T4 sender | 500 | Minimum interception strength for a T4 trader. |
| Full-force window after a trader is lost | 7 days | After interception, later traders from that settlement use full offensive-tier strength for this duration. Set to 0 to disable. |

## Goodwill from player trade

| Control | Default | What it changes |
|---|---:|---|
| Grant goodwill when trading with factions | On | Grants goodwill for completed silver-based trade. Favor trades are ignored. |
| Goodwill per 1,000 silver equivalent | 2 | Goodwill awarded per 1,000 market value exchanged. |

## Outpost delivery caravans

| Control | Default | What it changes |
|---|---:|---|
| Delivery caravan strength cost | 50 | Strength deducted when an outpost launches a production delivery. The delivery traveler carries this strength. |
| Minimum strength to send delivery | 100 | Required outpost strength before a pending delivery can launch. |

## Storyteller traders

| Control | Default | What it changes |
|---|---:|---|
| Disable storyteller trader caravans | Off | When on, blocks storyteller-spawned trader caravans while leaving WD trader caravans enabled. |
