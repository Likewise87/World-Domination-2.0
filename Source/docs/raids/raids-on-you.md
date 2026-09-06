# Raids on you

World Domination attacks the player through visible raid travelers. A hostile settlement commits offensive strength, launches a traveler, and sends it toward a colony or WD outpost. Travel attrition, interception, fortifications, and artillery can change the attack before arrival.

## Colony raids

For a raid that reaches a player colony, WD treats the traveler's remaining strength as approximately the raid points to generate. It then clamps that value to a configurable band around `StorytellerUtility.DefaultThreatPointsNow` for the selected baseline map.

In practical terms:

1. The hostile settlement and supporting allies commit offensive strength.
2. The raid traveler loses any applicable strength during travel or combat.
3. Remaining strength becomes the raw raid-point request.
4. WD applies the active storyteller floor and ceiling.
5. The resulting points create the map raid.

Ground raids walk in. Drop-pod raids (T3/T4) and Experimental T4 gravship raids arrive ballistically. Gravship arrival uses the Gravship Raids Workshop mod with WD's clamped points and faction when Odyssey and that mod are loaded; otherwise WD falls back to a drop-pod style raid.

The vanilla **Threat Scale** setting already affects `DefaultThreatPointsNow`. WD uses that storyteller value as the clamp baseline. It does not apply the vanilla threat multiplier a second time after clamping.

This arrangement keeps visible WD strength meaningful while preventing the default configuration from producing colony raids far outside the storyteller's current threat range.

## Nearby and Far threats

The WD dashboard splits hostiles that can reach your colony into two groups:

- **Nearby:** your colony lies in the inner half of the hostile settlement's current attack range.
- **Far:** your colony is still reachable but lies in the outer half of that range.

Nearby and Far do not mean safe and unsafe. Both groups can attack. The split helps distinguish close pressure from longer-range threats and affects how the dashboard presents likely attackers.

The Threat display uses attack-capable offensive strength, including eligible support. It does not use the faction ranking total.

## Outpost raids

WD raid travelers can also target player outposts. When an attack reaches an outpost, choose the available defense path:

- **Automatic defense:** WD resolves the battle using abstract strength. The outpost's offensive and defensive strength, attacker strength, and eligible allied support feed directly into the simulation.
- **Manual defense:** WD generates a temporary defense map. You deploy eligible outpost occupants and fight the raid as a normal map encounter.

Allies matter in both paths, but they are especially direct and visible in automatic resolution because their abstract strength enters the simulated totals. On a manual map, actual deployed forces and map combat determine the outcome.

Use the outpost's statistics and ally-radius display before choosing. A strong allied network can make automatic defense attractive. A valuable specialist garrison or a tactically favorable player force may justify manual defense.

## Storyteller raids

By default, WD blocks storyteller raids from factions with **WD Actions** enabled. Those factions pressure the player through world actions and raid travelers instead of appearing without a strategic origin. When the storyteller would pick a blocked faction, that `RaidEnemy` attempt is cancelled after faction resolve (not redirected to another faction).

Factions with **WD Actions** off allow storyteller raids by default. Configure per faction in **World Setup → WD Faction scope and Allegiances** (Storyteller column), or from mod Settings in-game. Manhunter packs and mech clusters use other incident workers and are not gated by this column.

If you customized the old Threat settings checkboxes (**Block storyteller raids** / **Allow non-WD raids**), set per-faction rules again. New games and untouched saves use the new defaults without action.

??? note "Advanced"
    With escalation-based clamping enabled by default, colony raid points are clamped to these percentages of the current storyteller baseline:

    - Early: 75% to 130%
    - Mid: 90% to 180%
    - Late: 100% to 230%

    If staged clamping is disabled, the legacy default band is 75% to 225%.

    **Always use strength as raid points** is off by default. The separate always-use-strength option for outpost defense is also off by default. Enabling either applicable option bypasses its storyteller-band clamp.

    The absolute minimum raid-points default is 60. Colony raids and player-outpost raids are both enabled.

    The player colony raid cooldown is 5 days. The global WD raid caps across colonies and outposts are 1 raid in 1 day, 2 raids in 4 days, and 3 raids in 7 days.

    The base ally pull radius is 6 world tiles.

    Colony launch eligibility uses a separate strength gate against storyteller points. The fresh required effective-attacker ratio starts at 0.7. It softens by 0.1 for each quiet day since the colony was last selected as a WD raid target, down to zero. This is not the general NPC or outpost minimum raid-ratio setting.

## Related chapters

- [Strength and tiers](../concepts/strength-and-tiers.md)
- [Travelers](../travelers.md)
- [World-map battles](world-map-battles.md)
