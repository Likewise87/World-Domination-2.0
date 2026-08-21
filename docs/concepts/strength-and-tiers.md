# Strength and tiers

Strength is WD's strategic combat currency. It is an abstract number used for world actions, traveler clashes, simulated battles, and the bridge into map raid points.

## Offensive and defensive strength

Each settlement or outpost can hold two local pools:

- **Offensive strength** is the deployable pool. Raids, expansion, construction missions, deliveries, and other dispatched travelers draw from it.
- **Defensive strength** is the site's dedicated garrison baseline. It remains at the site and contributes when that site is attacked.

The site's **total local defense** is offensive strength plus defensive strength. Offensive strength is included because forces not currently deployed can help defend their home.

Nearby eligible allies can add supporting strength to a battle. Their contribution is separate from the site's own local total.

## Ranking strength

The **Strength** value in **World Stats** and **Faction Details** uses the same ranking calculation:

- NPC settlements contribute offense plus defense.
- Player WD outposts contribute offense plus defense.
- Player colony maps are not assigned a WD ranking value.
- The player's ranked faction power therefore comes from WD outposts, not colony wealth or colony combat power.

This ranking number is not the same as the amount a site can launch. A site may rank highly because of defense while having little offensive strength available for a new mission.

## Threat strength

The dashboard's Threat display uses **attack-capable offensive strength**. It asks which hostile settlements can reach your colony and how much offense they and eligible supporters can deploy.

This is why a faction can rank highly in World Stats without appearing as the most immediate threat. Much of its strength may be defensive, too far away, committed to travelers, or outside attack reach.

Nearby and Far threat groups are based on the target colony's position within a hostile settlement's own attack range. See [Raids on you](../raids/raids-on-you.md).

## Spending and recovery

Launching a mission transfers or spends offensive strength at the origin. A large raid can leave a settlement or outpost less able to launch another action and can also reduce its local defense until offense is restored.

For player outposts, offensive and defensive pools recover toward their limits over time. Upgrades and other outpost factors can change effective values or recovery. Do not treat displayed strength as a permanent reserve. Check the outpost again after dispatching a raid, delivery, construction crew, or rapid-response force.

NPC settlements also change over time through growth, action outcomes, combat losses, upgrades, and faction effects.

??? note "Advanced"
    Default NPC offensive strength ranges are:

    - T1: 100 to 500
    - T2: 501 to 1,000
    - T3: 1,001 to 1,600
    - T4: 1,601 to 2,250

    Default defensive baselines are T1 = 100, T2 = 200, T3 = 350, and T4 = 500. A player WD outpost has a default base defensive strength of 100.

    Player outpost offensive recovery per day is the greater of 15% of its offensive target and a flat 80, applied toward the cap. Defensive recovery per day is the greater of 10% of its defensive cap and a flat 25, applied toward the cap.

    Default NPC attack-range baselines are T1 = 12, T2 = 16, T3 = 20, and T4 = 25 world tiles. Settlement age and escalation settings can increase effective range.

## Related chapters

- [World generation](world-generation.md)
- [Travelers](../travelers.md)
- [World-map battles](../raids/world-map-battles.md)
