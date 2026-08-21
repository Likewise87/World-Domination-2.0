# Mortars, anti-air and AT turrets

WD uses three different world-map weapon systems. They all remove **strength** from travelers or sites, but they have different ranges, cooldowns, and who can fire them.

## The three systems

| System | Typical owner | Targets |
| --- | --- | --- |
| Mortar | Player Artillery Outpost; NPC T4 batteries | Hostile settlements and eligible travelers within mortar range |
| Anti-air | Player outpost with Anti-Air Gun upgrade; NPC T4 batteries | Drop pods, aerial travelers, and (separately) incoming mortar shells |
| AT turret | Player or NPC fortification on a tile | Hostile ground travelers in a short tile range |

None of these are the same as a settlement-map turret you place in architect mode. AT guns and mortar shells are world objects.

## Player artillery and anti-air

Artillery Outposts can:

- fire a **manual** mortar strike at a hostile target in full configured range
- run **automatic** mortar fire against configured target types inside the adjusted auto range
- unlock **anti-air** after the Anti-Air Gun upgrade

Shooting skill and mortar upgrades improve hit chance and shorten mortar cooldown. Anti-air uses a short real-time cooldown and its own range.

**Adjust Range** shrinks automatic mortar or flak coverage. Manual mortar shots still use full range. Drop-pod launches from Rapid Response sites can be shot down by hostile AA, including T4 settlement batteries. Read the confirmation warning before launching valuable pawns or cargo.

## Enemy T4 batteries

Tier 4 settlements can field mortar and anti-air when those master options are enabled.

Firing **at the player** is gated by Mid / Late Game:

- Mid Game defaults leave player-targeted T4 mortar and AA **off**
- Late Game defaults turn those player-targeted options **on**

NPC mortar shells usually deal less strength damage than player shells. AA still uses range bands against pods and a flat chance against mortar shells.

Watch Mid / Late status on the dashboard before assuming the sky is safe.

## AT turrets

AT turrets are built as fortification projects. Players need enough Machining and respect global and per-site caps. Light, Medium, and Heavy guns differ in range, damage, cooldown, and HP.

They auto-engage eligible hostile ground targets. Experimental settings can let NPC AT guns also fire on player WD travelers and real pawn caravans.

AT turrets can be overrun by a player caravan on their tile and can be damaged by mortar fire. They do not replace a Rapid Response interceptor; they are a static choke-point weapon.

## Overlays

Use mortar, AT, and anti-air overlays to paint accuracy bands and coverage before you found an Artillery Outpost or walk a caravan under a citadel.

??? note "Advanced"
    Player mortar defaults: range **40** tiles, cooldown **5 days**, base shell damage **300**, hit bands **80% / 55% / 30%**.

    Player AA defaults: range **32** tiles, damage **800**, cooldown **120** seconds (floor **20**), vs mortar shells **80%**.

    NPC T4 mortar default damage **150**. Mid player-target toggles default off; Late defaults on.

    AT Light / Medium / Heavy default ranges **4 / 5 / 6** tiles, damage **75 / 100 / 125**, cooldowns **0.35 / 0.5 / 0.75** days. Player caps: **50** global, **4** per site.

    Full tables: [Settings: Player artillery](../settings/player-artillery.md), [Settings: T4 mortar (NPC)](../settings/t4-mortar.md), [Settings: Road building](../settings/road-building.md), [Settings: Late game](../settings/late-game.md).

## Related chapters

- [Outpost actions](../outposts/actions.md)
- [Outpost upgrades](../outposts/upgrades.md)
- [Fortifications](fortifications.md)
- [Travelers](../travelers.md)
- [Diplomacy and escalation](../diplomacy.md)
- [Raids on you](../raids/raids-on-you.md)
