# World actions, weights and cooldowns

NPC settlements do not sit idle. Each day, factions receive action opportunities and spend them on weighted **world actions**. Most successful actions launch a visible [traveler](../travelers.md): a raid, trader, road crew, fortify crew, or similar mission. These are abstract strength pools, not pawn caravans.

## How often settlements act

Every WD settlement contributes its tier's **actions per day** to its faction's daily share. Higher tiers contribute more. Underdog and world-leader temporary effects can multiply that share further.

A settlement also has a **daily action cap**: the maximum number of distinct actions it may start in one day. Caps stop a single powerful site from firing every eligible action on the same tick.

World-generation tier weights (how many T1 vs T4 settlements exist) are a different system. Do not confuse them with daily action shares.

## Action weights

When a settlement gets an opportunity, WD rolls among eligible actions using relative **weights**. Larger weights are more likely. Weights are not fixed percentages of all settlements.

Typical actions include:

| Action | What it does |
| --- | --- |
| Raid | Launch a combat traveler at a valid target |
| Minor / Major incident | Local strength loss or disruption without a full raid |
| Build road | Send a road crew |
| Trader | Send a trader caravan |
| Fortify | Place road blocks, traps, or AT turrets near the front |
| Develop | Grow strength when the settlement is near its tier cap |

**Develop** only enters the roll when the settlement is at or above **95%** of its current tier maximum. Settlements that are not near the cap never compete with Develop's weight.

The settings checkbox that shows Develop inside the percentage display changes **only the UI**. It does not change the roll.

## Cooldowns

Each action has its own cooldown. Finishing a Fortify does not block Raids unless that settlement is also on raid cooldown.

| Cooldown idea | Meaning |
| --- | --- |
| Action cooldowns | Block repeating that same action for a short time |
| Defense Shield | After being raided, the settlement is protected from being raided again for a short window. It can still act as an attacker. |
| Growth / passive growth | Silent daily strength gain with no grow cooldown |

Eligibility also matters. A settlement that is not threatened will not Fortify. A settlement without a valid road target will not Build Road even if the weight is high.

## Reading the strategic map

If a faction feels hyperactive:

- it may have many high-tier settlements
- underdog boost may be raising its action share
- raid and fortify weights may be high relative to traders

If a faction feels stagnant:

- many sites may be on long cooldowns (especially Expansion)
- Develop may be unavailable because sites are not near cap
- world-leader handicap may be pushing incidents instead of useful growth

??? note "Advanced"
    Default actions/day by tier: T1 **0.20**, T2 **0.28**, T3 **0.38**, T4 **0.60**.

    Default weights: Develop **240**, Raid **200**, Fortify **64**, Minor incident **80**, Major incident **16**, Build road **48**, Trader **48**.

    Default caps: T1/T2 **1** action/day, T3/T4 **2**.

    Default cooldowns (days): road **0.1**, expansion **14**, raid **0.2**, Defense Shield **1**, incident **2**, trader **1**, fortify **4**.

    Full tables: [Settings: World Actions](../settings/daily-actions.md).

## Related chapters

- [Strength and tiers](strength-and-tiers.md)
- [Fortifications](fortifications.md)
- [Travelers](../travelers.md)
- [Diplomacy and escalation](../diplomacy.md)
- [Settings: World Actions](../settings/daily-actions.md)
- [Settings: Growth and expand](../settings/growth.md)
- [Settings: World raids](../settings/world-raids.md)
