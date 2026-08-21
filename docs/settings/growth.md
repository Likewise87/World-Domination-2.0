# Growth & Expansion

Growth & Expansion controls passive NPC strength gain, the world settlement cap, where new settlements may be founded, local density, upgrade prerequisites, and defensive baselines.

## General Growth Settings

| Control | Default | Range |
|---|---:|---:|
| Max Planet Settlements | 400 | 30 to 1,250 |
| T1 passive / day | 50 strength | 0 to 300 |
| T2 passive / day | 80 strength | 0 to 300 |
| T3 passive / day | 110 strength | 0 to 300 |
| T4 passive / day | 140 strength | 0 to 300 |

Passive gain is flat per tier before Mid/Late Game and underdog multipliers. Performance presets also set Max Planet Settlements to 200, 400, or 800.

## Expansion & Local Density Limits

| Control | Default | Range |
|---|---:|---:|
| Min Expansion Radius | 5 tiles | 2 to 20 |
| Max Expansion Radius | 12 tiles | 5 to 80 |
| Local Max T1 Outposts | 5 | 1 to 25 |
| Local Max T2 Villages | 4 | 1 to 25 |
| Local Max T3 Towns | 3 | 1 to 25 |
| Local Max T4 Citadels | 1 | 1 to 25 |
| T1 same-tier neighbors to upgrade | 1 | 0 to 5 |
| T2 same-tier neighbors to upgrade | 1 | 0 to 5 |
| T3 same-tier neighbors to upgrade | 2 | 0 to 5 |

Maximum expansion radius is automatically kept at or above the minimum. The maximum radius is also the radius used to count local settlements. Same-tier neighbor requirements apply when upgrading T1 to T2, T2 to T3, and T3 to T4.

## Defensive strength baselines

| Control | Default | Range |
|---|---:|---:|
| T1 defensive strength | 100 | 0 to 1,000 |
| T2 defensive strength | 200 | 0 to 1,500 |
| T3 defensive strength | 350 | 0 to 2,000 |
| T4 defensive strength | 500 | 0 to 3,000 |

These values are the base local defensive strength for NPC settlements of each tier.

??? note "Advanced"
    **Incident Severity** appears only when **Show advanced settings** is enabled.

## Incident Severity

| Control | Default | Range |
|---|---:|---:|
| Minor Incident Strength Loss | 150 | 10 to 500 |
| Major Incident Strength Loss | 450 | 100 to 1,500 |

These values are the strength removed by minor internal setbacks and major catastrophes.

## Related settings

- [World Actions](daily-actions.md)
- [Mid Game and Late Game](late-game.md)
- [Strength and tiers](../concepts/strength-and-tiers.md)
