# World Map Raids

World Map Raids configures strategic raid selection and automatic battle resolution between settlements, outposts, and travelers. Relative strength is attacker strength divided by defender strength. Travel attrition itself is configured under Caravans & Trade.

## World Raid Simulation Parameters

| Control | Default | Range |
|---|---:|---:|
| Pre-raid exact path and travel cost calculation | 30% | 0% to 100% |
| Anti-leader coalition raid priority | 75% | 0% to 100% |
| Ally pull radius | 6 tiles | 5 to 200 |
| Min Strength Ratio | 1.00x | 0.50x to 2.00x |
| Raze Chance | 35% | 0% to 100% |
| Ruin linger (days) | 7.0 days | 5.0 to 10.0 |

The exact-path percentage is also managed by Performance presets. The minimum strength ratio is used for NPC and outpost targets. Colony raids use a separate storyteller-points launch gate. A successful automatic NPC raid rolls Raze Chance; a raze leaves blocking ruins for the configured duration.

## NPC Attack Range

| Control | Default | Range |
|---|---:|---:|
| T1 baseline range | 12 tiles | 5 to 60 |
| T2 baseline range | 16 tiles | 5 to 60 |
| T3 baseline range | 20 tiles | 5 to 60 |
| T4 baseline range | 25 tiles | 5 to 60 |
| Settlement age range bonus | 200% | 0% to 400% |
| Days to max age bonus | 120 days | 1 to 300 |
| Garrison retain (%) | 20% | 5% to 75% |

The age bonus grows from zero on the founding day to its maximum at the configured age. Mid/Late Game bonuses are added separately. Garrison retain is the minimum share of tier maximum or occupant maximum that a settlement or outpost keeps at home when dispatching raids, traders, or building crews.

## Raid arrival styles

| Control | Default | Range |
|---|---:|---:|
| T3 drop-pod raid chance | 25% | 0% to 100% |
| T4 drop-pod raid chance | 40% | 0% to 100% |
| Drop-pod raids: min tech level | Neolithic | Tech-level dropdown |
| Drop-pod attrition multiplier | 6.0 | 1.0 to 10.0 |
| Colony siege chance (walk raids) | 25% | 0% to 100% |

Siege chance is rolled only for walking T3 and T4 raids that reach a colony. Drop-pod raids use crow-flies distance and apply their multiplied attrition at launch.

## Attacker win likelihood per relative strength ratio

Each row is an editable win-chance slider from 0% to 100%.

| Relative strength | Default win chance |
|---:|---:|
| 0.00x | 0% |
| 0.10x | 3% |
| 0.25x | 10% |
| 0.50x | 20% |
| 1.00x | 42% |
| 1.50x | 58% |
| 2.00x | 70% |
| 3.00x | 88% |
| 4.00x | 94% |
| 5.00x | 95% |
| 6.00x | 99% |

??? note "Advanced"
    The four battle-margin sections below appear only when **Show advanced settings** is enabled. Every Close, Normal, and Decisive cell is an editable 0% to 100% slider. The three cells in a row are normalized to total 100%.

## When the attacker wins, how decisive is the victory?

| Relative strength | Close Attacker Win | Normal Attacker Win | Decisive Attacker Win |
|---:|---:|---:|---:|
| 0.00x | 98% | 2% | 0% |
| 0.10x | 98% | 2% | 0% |
| 0.25x | 89.2% | 8.3% | 2.5% |
| 0.50x | 74.4% | 18.9% | 6.7% |
| 1.00x | 45% | 40% | 15% |
| 1.50x | 40.5% | 36.5% | 23% |
| 2.00x | 36% | 33% | 31% |
| 3.00x | 27% | 26% | 47% |
| 4.00x | 18% | 19% | 63% |
| 5.00x | 9% | 12% | 79% |
| 6.00x | 0% | 5% | 95% |

## When the attacker loses, how bad will the loss be?

| Relative strength | Close Attacker Loss | Normal Attacker Loss | Decisive Attacker Loss |
|---:|---:|---:|---:|
| 0.00x | 0% | 5% | 95% |
| 0.10x | 0% | 5% | 95% |
| 0.25x | 7.5% | 10.8% | 81.7% |
| 0.50x | 20% | 20.6% | 59.4% |
| 1.00x | 45% | 40% | 15% |
| 1.50x | 50.3% | 36.2% | 13.5% |
| 2.00x | 55.6% | 32.4% | 12% |
| 3.00x | 66.2% | 24.8% | 9% |
| 4.00x | 76.8% | 17.2% | 6% |
| 5.00x | 87.4% | 9.6% | 3% |
| 6.00x | 98% | 2% | 0% |

## When the defender loses, how bad will the loss be?

| Relative strength | Close Defender Loss | Normal Defender Loss | Decisive Defender Loss |
|---:|---:|---:|---:|
| 0.00x | 98% | 2% | 0% |
| 0.10x | 98% | 2% | 0% |
| 0.25x | 89.2% | 8.3% | 2.5% |
| 0.50x | 74.4% | 18.9% | 6.7% |
| 1.00x | 45% | 40% | 15% |
| 1.50x | 44% | 37.5% | 18.5% |
| 2.00x | 43% | 35% | 22% |
| 3.00x | 41% | 30% | 29% |
| 4.00x | 39% | 25% | 36% |
| 5.00x | 37% | 20% | 43% |
| 6.00x | 35% | 15% | 50% |

## When the defender wins, how decisive is the victory?

The default distribution is the same as **When the attacker loses, how bad will the loss be?** for every relative-strength row. These are separate controls and can be edited independently.

## Strength loss per outcome (global)

Every cell is an editable 0% to 100% slider. Close means a narrow or hard-fought battle. Decisive means a one-sided battle.

| Margin | Attacker Win | Attacker Loss | Defender Win | Defender Loss |
|---|---:|---:|---:|---:|
| Close | 60% | 30% | 35% | 15% |
| Normal | 35% | 60% | 20% | 28% |
| Decisive | 15% | 80% | 10% | 45% |

## Player raid limits

The player colony/outpost enable switches, per-colony cooldown, global 1-day, 4-day, and 7-day caps, and storyteller raid controls are in [Raids on Player](raid-points.md). Defaults are On for colony and outpost raids, a 5-day colony cooldown, and global caps of 1, 2, and 3.
