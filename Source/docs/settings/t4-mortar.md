# NPC Artillery (T4)

NPC Artillery configures mortar and anti-air weapons attached to eligible enemy Tier 4 settlements. Master toggles control whether the weapons operate at all. Separate player-target toggles permit attacks on your assets only during the configured Mid/Late escalation stages.

??? note "Advanced"
    This dialog is available from General Settings only when **Show advanced settings** is enabled.

## Enemy T4 settlement turrets

| Control | Default | Range |
|---|---:|---|
| T4 turrets: min tech level | Neolithic | Tech-level dropdown |

The tech gate is shared by enemy T4 mortars and anti-air. Neolithic permits essentially every faction.

## Mortar

| Control | Default | Range or availability |
|---|---:|---|
| Enemy T4 settlements fire mortars | On | On or Off |
| May target your WD travelers and outposts | On | Shown while enemy mortars are enabled |
| T4 mortar range (tiles) | 40 | 10 to 250 |
| T4 mortar cooldown (days) | 5.0 days | 0.1 to 20.0 |
| Enemy settlement mortar damage | 150 strength | 0 to 600 |
| Enemy settlement equivalent shooting skill | 10 | 0 to 40 |
| Hit chance at 0-50% of max range | 80% | 0% to 100% |
| Hit chance at 51-75% of max range | 55% | 0% to 100% |
| Hit chance at 76-100% of max range | 30% | 0% to 100% |

Equivalent shooting skill adds one percentage point per level after the range-band chance. The player-target toggle is the Late Game permission. Mid Game has a separate permission on the [Mid Game and Late Game](late-game.md) page, and enabling the Mid Game permission forces this Late Game permission on.

## Anti-air

| Control | Default | Range or availability |
|---|---:|---|
| Enemy T4 settlements fire anti-air | On | On or Off |
| AA may target your airborne assets | On | Shown while enemy anti-air is enabled |
| T4 AA range (tiles) | 32 | 10 to 250 |
| T4 AA cooldown (seconds) | 120 seconds | 5 to 300 |
| T4 AA damage | 800 | 100 to 2,000 |
| T4 AA skill equivalent | 10 | 0 to 40 |
| T4 Anti-Air hit chance at 0-50% of max range | 80% | 0% to 100% |
| T4 Anti-Air hit chance at 51-75% of max range | 55% | 0% to 100% |
| T4 Anti-Air hit chance at 76-100% of max range | 30% | 0% to 100% |
| T4 Anti-Air hit chance vs mortar shells | 80% | 0% to 100% |
| Interception scan interval (seconds) | 30 seconds | 5 to 120 |

Skill equivalent applies to pods and other non-shell airborne targets. The mortar-shell chance is flat and ignores both range bands and skill. The scan interval is shared with player artillery and Rapid Response. Enemy T4 settlements scan at three times the configured interval to reduce performance cost.

## Related settings

- [Mid Game and Late Game](late-game.md)
- [Notifications](notifications.md)
- [World Map Raids](world-raids.md)
