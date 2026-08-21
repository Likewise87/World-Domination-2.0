# Diplomacy

Diplomacy determines who supports raids, which settlements you can trade with, and which factions can be persuaded to act for you. Open **WD > Diplomacy Matrix** to see the relationships between every visible faction. Player-facing colors reflect each faction's relation with you, while matrix cells show ally, neutral, or hostile relations between the two factions.

## Wars, peace, and alliances

WD can create wars, peace treaties, and alliances through random diplomacy. Permanent-enemy and hive relationships can be locked so WD does not rewrite them. Each relation pair receives a cooldown after a WD change.

Strong-faction wars are a separate balancing system. The strongest NPC powers can cool an alliance to neutral or turn a neutral relationship into war. Allies do not jump directly to hostile. By default this system is enabled and does not wait for Mid or Late Game.

You can also negotiate with an allied or neutral faction. Select its row in the Diplomacy Matrix and use **Negotiate** to ask it to declare war, cease fire, or become allied with another faction. The request must pass relation, strength, and cooldown checks. Payment is sent as goods, and the relationship changes when the delivery arrives.

WD raises the default maximum goodwill to **200**. Gifts, trades, embassies, conquest rewards, and other WD systems can therefore keep improving a relationship past vanilla's usual 100 cap.

## Gifts and settlement investment

Select an allied or neutral settlement and use **Send gift**. Goods leave from your nearest colony or warehouse. When the gift arrives:

- goodwill increases
- the silver-equivalent value can strengthen nearby settlements of the recipient faction
- enough investment can fill a settlement's current tier and fund a tier upgrade

If the gift caravan is intercepted, its goods are lost and the interceptor can invest the seized value into its own nearby settlements.

After conquering a settlement, you may give the ruins to an allied or neutral faction. The recipient establishes a settlement and you gain tier-based goodwill.

## Buying settlements

Allied and neutral WD settlements show **Buy settlement** when buying is enabled. You may pay with physical goods, goodwill, or both. Payment is deducted immediately, then a purchase traveler carries it to the target.

On arrival, choose to convert the site into an outpost, recruit its inhabitants, or let another allied or neutral faction settle it. You cannot leave the tile empty or gift it back to the seller.

The deal can fail if the settlement disappears, changes tier, the relationship becomes hostile, or the seller falls below the required settlement count. Those invalidation cases refund payment. Destruction of the purchase traveler does not. Overpayment is never refunded, and payment value strengthens the seller's nearby settlements.

## Asking allies for military and road support

Select an allied settlement to order a raid against one of your hostile targets. The preview must meet the configured minimum win chance. You then choose who receives the conquered site:

- **Ally claims target** costs less goodwill. The ally keeps a successful conquest.
- **Award to player** costs more goodwill. A successful conquest leaves the outcome for you.

If the order is cancelled before it can proceed, the goodwill cost can be refunded.

Allied settlements can also be ordered to build a road to a chosen destination. The goodwill charge combines a base cost based on settlement tier and a per-segment cost based on road quality. Unbuilt segments are refunded if the project is cancelled, aborted, destroyed, or invalidated by hostility.

## Reinforcements when you attack

Attacking an enemy settlement can pull allied settlements into the battle. Both attacker and defender use the same base ally pull radius. Nearby allies contribute strength to the world-map resolution and are shown in the force breakdown. This is why the target's local strength alone may understate the force you will face.

The base ally radius is 6 tiles. Mid and Late Game can increase it for both player and NPC attacks. Check the raid preview and Faction Details instead of assuming a settlement is isolated.

## Temporary world balancing

### Underdog growth boost

The weakest faction can become small and nimble for a limited period. It receives a larger share of daily actions, gains more strength from growth, and suffers fewer and less severe incidents.

### World leader handicap

The current strongest faction can suffer internal strife and logistical pressure. Its incidents become more likely and remove more strength.

### Anti-leader coalition

Smaller factions can temporarily ally against the dominant faction and declare war on it. Coalition members receive a strong raid priority toward the coalition target. When the coalition ends, affected relationships return to their recorded pre-coalition state when possible.

### Expansionist zeal

An ambitious faction can temporarily gain longer raid range and lower travel attrition, letting its strength pools reach farther with more strength remaining.

## Mid and Late Game escalation

Escalation is driven by your total WD outpost strength and your share of world strength. Meeting either threshold activates the stage. Late Game replaces Mid Game rather than stacking with it.

Mid Game:

- biases reachable hostile raids toward your colonies and outposts
- increases hostile growth and NPC attack range
- increases the ally pull radius
- adds a 15% garrison boost
- allows hostile expansion to creep toward you by up to 4 tiles from its parent
- enables lighter outpost attrition incidents and periodic goodwill drain
- leaves T4 mortar and anti-air attacks on the player off by default

Late Game:

- increases the raid bias, growth, range, ally support, garrisons, and expansion pressure further
- raises the garrison boost to 30% and expansion creep to 8 tiles
- increases outpost incident severity and frequency
- increases goodwill drain
- permits enemy T4 mortars and anti-air to target player forces when their master settings are enabled

The WD dashboard and World Stats show the active stage and a tooltip with its current effects.

??? note "Advanced"
    These are default settings, not fixed rules. Mod settings and difficulty presets can change them.

    **Escalation defaults**

    - Mid activates at **15% world strength share OR 6,000 outpost strength**
    - Late activates at **25% world strength share OR 10,000 outpost strength**
    - Mid: player raid bias **+25%**, hostile growth **1.5x**, NPC attack range **+50%**, ally radius **+40%**
    - Late: player raid bias **+50%**, hostile growth **2x**, NPC attack range **+100%**, ally radius **+100%**
    - Mid garrison **+15%**, expansion creep **4 tiles**, incident **100 strength** at **3.75% per day**, goodwill **-4 every 10 days**
    - Late garrison **+30%**, expansion creep **8 tiles**, incident **200 strength** at **7.5% per day**, goodwill **-10 every 10 days**
    - Storyteller clamp bands: Mid **90% to 180%**, Late **100% to 230%**

    **Buff and handicap defaults**

    - Underdog: daily action share **2x**, incident weight **0.5x**, incident severity **0.5x**, growth gain **2x**
    - World leader: incident weight **2x**, incident severity **2x**
    - Expansionist zeal: raid range **1.5x**, travel attrition **0.5x**
    - Leader handicap, underdog boost, and zeal each last **10 days** with a **15 day cooldown**
    - Anti-leader coalition lasts **15 days** with a **20 day cooldown**
    - Daily trigger chances after eligibility and cooldown checks: leader **35%**, underdog **25%**, zeal **20%**, coalition **25%**
    - Coalition raid priority: **75%**

    **Orders, purchases, and conquest defaults**

    - Minimum allied raid win chance: **50%**
    - Ally-claims-target raid goodwill cost, T1 to T4: **15 / 25 / 35 / 45**
    - Award-to-player raid goodwill cost, T1 to T4: **30 / 50 / 70 / 90**
    - Road order base goodwill cost, T1 to T4: **5 / 8 / 12 / 15**
    - Road goodwill per segment by road quality T1 to T3: **0.4 / 0.7 / 1.0**, with payment rounded up and refunds rounded down
    - Goodwill for gifting conquered ruins, T1 to T4: **15 / 28 / 45 / 70**
    - Settlement purchase asks, T1 to T4: **5,000 / 12,000 / 20,000 / 30,000 silver**
    - One goodwill covers **200 silver** of a purchase ask by default, subject to the retained-goodwill floor
    - Maximum goodwill: **200**
