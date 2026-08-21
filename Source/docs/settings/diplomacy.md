# Diplomacy, Buffs & Debuffs

This dialog controls autonomous NPC diplomacy, goodwill rules, player-ordered faction actions, settlement purchases and investments, bribes, and temporary power-balance effects. Controls hidden behind an enable checkbox retain their configured values while disabled.

## Diplomacy, Buffs & Debuffs

### Dynamic world relations

| Control | Default | Range or availability |
|---|---:|---|
| Enable Random Allegiance Changes | On | On or Off |
| Daily Allegiance Change Chance | 3% | 0% to 100%; shown while random changes are enabled |
| Enable strong-faction wars | On | On or Off |
| Strong-faction war chance | 10% | 0% to 100%; shown while strong-faction wars are enabled |
| Top strong factions considered | 30% | 5% to 100%; shown while strong-faction wars are enabled |
| Only after Mid or Late Game | Off | Shown while strong-faction wars are enabled |
| Daily Revolt Chance | 2.0% | 0% to 100% |

Random allegiance changes alter NPC-to-NPC relations. Strong-faction wars separately cool allied pairs to neutral or escalate neutral pairs to war among the strongest factions. Revolts return a defeated faction by taking lower-tier settlements from leading NPC factions, never from the player.

### Vanilla goodwill

| Control | Default | Range |
|---|---:|---:|
| Maximum goodwill | 200 | 100 to 200 |
| No Goodwill from Hostile factions on Settlement Conquest | On | On or Off |
| Disable goodwill loss for nearby settlements | On | On or Off |

### Allied raid orders

| Control | T1 default | T2 default | T3 default | T4 default |
|---|---:|---:|---:|---:|
| Ally-claims-target goodwill costs | 15 | 25 | 35 | 45 |
| Award-to-player goodwill costs | 30 | 50 | 70 | 90 |
| Conquest gift goodwill rewards | 15 | 28 | 45 | 70 |

Each tier cell is a separate slider from 0 to the configured Maximum goodwill.

| Other control | Default | Range |
|---|---:|---:|
| Minimum allied raid success chance | 50% | 0% to 100% |

### Ordered road building

| Control | Default | Range |
|---|---:|---:|
| Dirt road | 0.40 goodwill per segment | 0 to Maximum goodwill |
| Stone road | 0.70 goodwill per segment | 0 to Maximum goodwill |
| Asphalt road | 1.00 goodwill per segment | 0 to Maximum goodwill |
| Order trader caravan (goodwill) | 10 goodwill | 0 to Maximum goodwill |

### Buy settlement

| Control | Default | Range or availability |
|---|---:|---|
| Enable buy settlement | On | On or Off |
| Ask (T1 silver) | 5,000 | 500 to 50,000 |
| Ask (T2 silver) | 12,000 | 500 to 50,000 |
| Ask (T3 silver) | 20,000 | 500 to 80,000 |
| Ask (T4 silver) | 30,000 | 500 to 100,000 |
| Silver per goodwill point | 200 | 10 to 500 |
| Max ask share payable in goodwill | 100% | 0% to 100% |

The six price controls are shown while settlement buying is enabled.

### Diplomacy negotiate

| Control | Default | Range or availability |
|---|---:|---|
| Enable diplomacy negotiate | On | On or Off |
| Ask floor (silver) | 8,000 | 1,000 to 20,000 |
| Ask ceiling (silver) | 40,000 | 5,000 to 80,000 |

The ask controls are shown while diplomacy negotiation is enabled.

### Faction bribe

| Control | Default | Range or availability |
|---|---:|---|
| Enable faction bribes | On | On or Off |
| Ceasefire days (short) | 10 days | 1 to 60 |
| Ceasefire days (medium) | 20 days | 1 to 90 |
| Ceasefire days (long) | 30 days | 1 to 120 |
| Medium package discount | 10% | 0% to 50% |
| Long package discount | 20% | 0% to 50% |
| Raid ask floor (of launch strength) | 50% | 0% to 100% |
| Bribe investment fraction | 50% | 0% to 100% |
| Raid bribe investment radius | 50 tiles | 5 to 100 |
| Silver per goodwill bonus point | 400 | 50 to 2,000 |

The nine tuning controls are shown while faction bribes are enabled. Ceasefire bribes block new WD raids but do not recall raid travelers already in flight.

### Faction settlement investment

| Control | Default | Range or availability |
|---|---:|---|
| Enable gift/buy strength investment | On | On or Off |
| Strength per 100 silver | 20 | 0 to 50 |
| Investment radius (tiles) | 50 | 5 to 60 |
| Silver to upgrade T1 to T2 | 1,500 | 0 to 20,000 |
| Silver to upgrade T2 to T3 | 4,000 | 0 to 30,000 |
| Silver to upgrade T3 to T4 | 9,000 | 0 to 50,000 |
| Tier-up success chance | 50% | 0% to 100% |

The six tuning controls are shown while investment is enabled. Payment fills nearby same-faction settlements to their current tier cap, then attempts paid tier upgrades.

??? note "Advanced"
    **Temporary Buffs and Debuffs** appears only when **Show advanced settings** is enabled.

## Temporary Buffs and Debuffs

### World leader handicap

| Control | Default | Range or availability |
|---|---:|---|
| Enable | On | On or Off |
| Duration | 10.0 days | 0.5 to 60 |
| Cooldown | 15.0 days | 0.5 to 60 |
| Leader debuff trigger chance | 35% | 0% to 100% |
| Incident likelihood multiplier | 2.0x | 0.5x to 4.0x |
| Incident strength loss multiplier | 2.0x | 0.5x to 4.0x |

### Underdog growth buff

| Control | Default | Range or availability |
|---|---:|---|
| Enable | On | On or Off |
| Duration | 10.0 days | 0.5 to 60 |
| Cooldown | 15.0 days | 0.5 to 60 |
| Underdog buff trigger chance | 25% | 0% to 100% |
| Underdog: Daily action share multiplier | 2.0x | 1.0x to 4.0x |
| Incident likelihood multiplier | 0.50x | 0.10x to 1.00x |
| Incident strength loss multiplier | 0.50x | 0.10x to 1.00x |
| Underdog: Growth strength gain multiplier | 2.0x | 1.0x to 4.0x |

### Expansionist zeal

| Control | Default | Range or availability |
|---|---:|---|
| Enable | On | On or Off |
| Duration | 10.0 days | 0.5 to 60 |
| Cooldown | 15.0 days | 0.5 to 60 |
| Zeal Buff Trigger Chance | 20% | 0% to 100% |
| Zeal: Raid range multiplier | 1.5x | 1.0x to 4.0x |
| Zeal: Travel attrition multiplier | 0.50x | 0.10x to 1.00x |

### Anti-leader coalition

| Control | Default | Range or availability |
|---|---:|---|
| Enable | On | On or Off |
| Duration | 15.0 days | 0.5 to 60 |
| Cooldown | 20.0 days | 0.5 to 60 |
| Coalition trigger chance | 25% | 0% to 100% |

## Allowed Diplomacy Changes

This in-game-only matrix controls which NPC faction pairs World Domination may change. The player is not listed because random WD diplomacy does not change player relations. Clicking a pair toggles **Locked** or **Change Possible**.

| Control | Default or effect |
|---|---|
| Filter by Name | Empty |
| Clear | Clears the filter |
| Lock All | Locks every listed pair |
| Allow All | Clears every listed lock |
| Default | Restores the initial defaults: Insectoid/Hive pairs are locked against everyone, and mutually hostile permanent-enemy pairs are locked |
| Perm. Hostile | Locks every pair involving a permanently hostile faction and every Insectoid/Hive pair |

The locks apply only to relationship changes attempted by World Domination.

## Related settings

- [Notifications](notifications.md)
- [Mid Game and Late Game](late-game.md)
