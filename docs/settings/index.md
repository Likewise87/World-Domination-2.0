# Settings reference

World Domination's mod settings are a hub of focused dialogs. Most changes take effect immediately or when the dialog closes. Every child dialog has its own reset button, while **Reset ALL Settings** restores the complete configuration. The defaults in this reference come from `Source/Settings/Settings.cs` as of August 2026.

## Main settings controls

| Control | Default | What it does |
|---|---:|---|
| Show advanced settings | Off | Shows extra menu rows and advanced sections inside several dialogs. |
| Show mod update popup | On | Shows the update window once after loading a map following a mod update. |
| Verbose logging | Off | Writes extra `[WD]` and `[WD Perf]` diagnostic lines. |

Advanced mode unlocks NPC Artillery, Building Projects, Mining baselines, Sabotage, Disinformation, Initial World Generation, and Map Generation and Garrison. It also reveals advanced controls inside World Actions, World Map Raids, Growth & Expansion, and Diplomacy, Buffs & Debuffs.

## Setting presets

Performance and Difficulty are independent packs. Choosing an entry does nothing until **Apply** is clicked, and applying one pack changes only the fields assigned to that pack. If those fields are later edited, the hub reports the applied state as **Custom**.

### Performance

| Preset | Summary |
|---|---|
| Best Performance | Uses crow-flies raid preparation, disables water routes and enemy T4 artillery, slows interception scans, shortens world searches, caps the world at 200 settlements, and caps NPC ambush interceptors at 4. |
| Normal Performance | Default. Uses 30% exact path preparation, water travel, 30-second scans, normal search ranges, a 400-settlement cap, and an NPC ambush interceptor cap of 8. |
| Reduced Performance | Uses 80% exact path preparation, 15-second scans, water travel, full search ranges, an 800-settlement cap, pollution repathing, and no NPC ambush interceptor cap. |

### Difficulty

| Preset | Summary |
|---|---|
| Easy Difficulty | Uses 8-day colony and outpost raid cooldowns, caps WD raids at 1 per day, 1 per 4 days, and 2 per 7 days, uses a 50% to 150% storyteller band, lowers raid weight, disables Mid/Late Game, and disables outpost skill diminishing returns. |
| Medium Difficulty | Default. Uses 5-day raid cooldowns, caps WD raids at 1, 2, and 3 in the three windows, enables Mid/Late Game at the default thresholds, and enables T4 attacks on the player in Late Game only. |
| Hard Difficulty | Uses 3-day raid cooldowns, caps WD raids at 2, 4, and 6, uses a 100% to 300% storyteller band, increases raid weight and Late Game pressure, and permits T4 attacks on the player from Mid Game onward. |

## Settings hub structure

### Settings only available in-game

**Allowed Diplomacy Changes** opens the faction-pair lock matrix. It is unavailable from the main menu. See [Diplomacy, Buffs & Debuffs](diplomacy.md#allowed-diplomacy-changes).

### General Settings

- [Notifications](notifications.md)
- [World Actions](daily-actions.md)
- [World Map Raids](world-raids.md)
- [Growth & Expansion](growth.md)
- [Raids on Player](raid-points.md)
- [Mid Game and Late Game](late-game.md)
- [Diplomacy, Buffs & Debuffs](diplomacy.md)
- [NPC Artillery (T4)](t4-mortar.md), Advanced

### WD Outposts and Caravans/Travelers

- [Outpost Settings](outposts.md)
- [Outpost skill scaling](outpost-skill-scaling.md)
- [Player Artillery](player-artillery.md)
- [Caravans & Trade](caravans.md)
- [Building Projects](road-building.md), Advanced
- [Mining baselines](mining-baselines.md), Advanced

### Manual Player Interactions

- [Sabotage](sabotage.md), Advanced
- [Disinformation](disinformation.md), Advanced

### Miscellaneous

- [Initial World Generation](world-generation.md), Advanced
- [Map Generation and Garrison](garrison.md), Advanced
- [Experimental](experimental.md)

**Verbose logging** is located directly in this section.

## Related guide chapters

- [Getting started](../getting-started.md)
- [Strength and tiers](../concepts/strength-and-tiers.md)
- [Travelers](../travelers.md)
- [Raids on you](../raids/raids-on-you.md)
