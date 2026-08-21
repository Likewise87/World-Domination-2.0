# Outpost actions

Select an outpost on the world map to see its available commands. Commands vary by outpost type, research, upgrades, current project, and enabled settings.

## Launch Attack

**Launch Attack** sends an outpost raid against a settlement or other valid hostile target within range. Choose the target and commit offensive strength. Nearby allies inside the ally pull radius may contribute according to their faction relationship and the battle.

The committed strength leaves the outpost while the operation is underway. Do not launch merely because a target is in range. Check the outpost's remaining defense and nearby hostile travelers first. A Strategist expert increases manual raid range.

## Build menu

The **Build** menu starts world projects from an outpost. Construction speed scales with cumulative Construction skill, while an Engineer expert can improve speed and planning radius. Most projects send a crew and spend offensive strength. Cancelling a crew after launch does not normally refund that strength.

### Roads

Choose **Build Road**, then click a destination. Hold Shift while clicking to add waypoints and click without Shift to confirm the final destination. The outpost constructs the planned route over time. Road removal follows the same path-planning method and removes existing road segments.

### Road blocks

Road blocks increase movement difficulty on their world tile. Choose a light, normal, or heavy block where available, then place the first block directly on a tile. Add further nodes with Shift. Clear road blocks with the corresponding removal command or **Remove fortifications**.

### Spike traps

World spike traps damage hostile ground travelers when they leave a trapped tile, then the trap is destroyed. Friendly and neutral travelers are unaffected. A traveler can trigger only a limited number of traps. Traps cannot share invalid sites such as settlements, outposts, road blocks, or existing traps.

### AT turrets

AT turrets fire automatically on configured hostile ground travelers passing within range. **Machining** is required. Set target categories and raid filters after placement. Turrets require free, passable tiles and are subject to both global and per-site caps.

### Remove fortifications

This command sends a crew along a planned path to remove road blocks, spike traps, and AT turrets. You may click any passable destination, but work is performed only on tiles that contain fortifications.

### Decontamination

The **Decontamination Crew** scrubs pollution from affected tiles along a planned path. Only polluted path tiles receive work. This action requires **Biotech**, **Machining**, and the **Decontamination Equipment** outpost upgrade.

### Colony construction

A player colony can start supported road, road-block, and spike-trap projects from its own world-map Build menu. Colony projects use colony Construction skill and do not spend outpost strength.

## Artillery and anti-air

Artillery Outposts can fire a manual mortar strike at a hostile target within their full configured range. Shooting skill affects performance. Use **Configure Artillery** to select automatic target types, raid filters, and operating range.

The **Anti-Air Gun** upgrade enables automatic flak fire against hostile drop pods and other supported airborne targets. Anti-air has its own short cooldown. Mortar-shell interception uses a flat shot-down chance, while pods and aerial targets use distance-based accuracy bands.

Use **Adjust Range** to reduce automatic mortar or flak coverage when you want to avoid distant targets. Manual mortar strikes continue to use the full configured range.

## Rapid Response

Configure a Rapid Response Outpost to intercept selected categories of hostile travelers, including raiders, traders, expansion forces, road builders, and fortification crews. Raider interception can be limited by whether the raid targets you, an ally, or another faction. Minimum relative strength prevents weak dispatches, while the maximum ratio limits how much strength is sent.

After **Transport Pods** research, use the Pawns tab to drop selected outpost colonists on a passable tile, hostile traveler, colony, or outpost within drop-pod range. At least one pawn must remain at the origin. Prisoners and stored animals or vehicles cannot be launched this way.

!!! warning
    Drop pods can be intercepted by hostile anti-air coverage, including anti-air at enemy Tier 4 settlements. Read the confirmation warning before launching valuable colonists or warehouse cargo.

## Warehouse shipping

In a Warehouse Outpost:

1. Choose **Set ship destination** and select a player colony or warehouse.
2. Open **Storage**, choose **Ship goods**, and select the stacks to send.
3. Launch by land traveler, or use drop pods after **Transport Pods** research.

Land shipments follow a world route and arrive at the map edge or delivery spot. Drop-pod shipments travel directly and arrive at the colony map edge, delivery spot, or target warehouse. **Auto ship once per day** sends the warehouse's entire stock using the selected delivery mode, provided a destination is set and the outpost has enough strength.

Production outposts have a separate **delivery destination** control. Use it to route their normal cycle deliveries to a warehouse instead of the nearest colony.

## Management toggles

- **Auto-Add Arrivals** automatically absorbs any player caravan that reaches the outpost tile. Occupants join the outpost, while supported animals, vehicles, and mechanoids are stored.
- **Take prisoners** keeps surviving hostile captives after a successful defense. Disable it when the outpost should not maintain prisoners.
- **Adjust Range** limits Rapid Response auto-interception or automatic artillery and flak operations. It does not reduce Rapid Response drop-pod range or manual mortar range.
- **Mark no-fortify zone** paints tiles where allies, and neutrals if enabled, may not place new road blocks or traps. Existing fortifications remain.
- **Erase no-fortify marks** removes those restrictions instantly. Neither marking command sends a crew.

## Radius overlays

Use the world-map overlay commands to inspect:

- ally pull radius
- manual attack radius and target-likelihood bands
- food supply radius
- People Radius for recruiting, trading, and embassies
- warehouse productivity aura
- mortar and AT turret accuracy bands
- anti-air coverage

Overlay toggles apply to all player sites of the same relevant type. Use them when choosing new sites and before committing travelers or drop pods.

??? note "Advanced"
    Default project and fortification limits are:

    | Rule | Default |
    |---|---:|
    | Road-block planning range | 10 tiles |
    | Spike-trap planning range | 10 tiles |
    | Decontamination planning range | 20 tiles |
    | AT turret global cap | 50 |
    | AT turret cap per origin site | 4 |
    | Light road-block movement penalty | +1.5 |
    | Normal road-block movement penalty | +2.5 |
    | Heavy road-block movement penalty | +4 |

    Project work, offensive strength costs, damage, health, trigger limits, and feature availability are configurable. See [Settings: Outposts and food](../settings/outposts.md) and [Settings: Road building](../settings/road-building.md) for the complete current values.
