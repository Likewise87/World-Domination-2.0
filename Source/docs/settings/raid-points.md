# Raids on Player

Raids on Player controls whether WD can attack your holdings, how frequently those attacks may launch, and how traveler strength becomes map raid points. The vanilla Threat Scale already feeds the storyteller baseline used by the clamp and is not applied a second time.

## World Raids on Player Colony

| Control | Default | Range or availability |
|---|---:|---|
| Allow Player Colony to be Raided | On | On or Off |
| Player Raid Cooldown | 5.0 days | 0 to 15 days; shown when colony raids are enabled |
| Allow Player Outposts to be Raided | On | On or Off |
| Max player WD raids per day | 1 | 1 to 10 |
| Max player WD raids per 4 days | 2 | 1 to 20 |
| Max player WD raids per 7 days | 3 | 1 to 30 |
| Block storyteller raids (only World Domination raids) | On | On or Off |
| Allow storyteller raids from non-WD factions | On | Shown while storyteller raids from WD factions are blocked |

The three rate caps are global across all player colonies and outposts. The 4-day cap cannot be lower than the daily cap, and the 7-day cap cannot be lower than the 4-day cap. Per-target cooldowns apply in addition to these global windows. The outpost defense cooldown has a separate 5-day default in Outpost Settings.

Blocking storyteller raids affects random raids selected from WD-managed factions. Quests, forced incidents, comms, developer actions, and WD world-map raids are not blocked. With the non-WD exception enabled, factions outside WD management may still be selected by the storyteller.

## Raid point clamping

| Control | Default | Range or availability |
|---|---:|---|
| Always use Strength as Raid points | Off | On or Off |
| Always use Strength for manual outpost defense | Off | On or Off |
| Scale with mid and late game difficulty | On | Shown unless Always use Strength as Raid points is enabled |
| Min Early Game | 0.75x storyteller threat | 0.05x to 2.00x |
| Max Early Game | 1.30x storyteller threat | 0.50x to 50.00x |
| Min Mid Game | 0.90x storyteller threat | 0.05x to 2.00x |
| Max Mid Game | 1.80x storyteller threat | 0.50x to 50.00x |
| Min Late Game | 1.00x storyteller threat | 0.05x to 2.00x |
| Max Late Game | 2.30x storyteller threat | 0.50x to 50.00x |
| Minimum fraction of storyteller threat | 0.75x | 0.05x to 2.00x; used when staged scaling is unavailable or off |
| Maximum fraction of storyteller threat | 2.25x | 0.50x to 50.00x; used when staged scaling is unavailable or off |
| Min Raid Points | 60 | 50 to 500 |
| Max Raid Points | 10,000 | 1,000 to 20,000 |

The stage bands are shown only when staged clamping and Mid/Late Game are both enabled. The single minimum and maximum pair appears otherwise. In each active pair, the maximum is normalized so it cannot fall below the minimum.

**Always use Strength as Raid points** bypasses the storyteller band for colony raids and caravan clashes. **Always use Strength for manual outpost defense** separately bypasses it for manual outpost defense maps. The absolute Min and Max Raid Points remain available because reinforcement-style WD spawns use them even when the main storyteller band is bypassed.

## Related settings

- [World Map Raids](world-raids.md)
- [Mid Game and Late Game](late-game.md)
- [Raids on you](../raids/raids-on-you.md)
