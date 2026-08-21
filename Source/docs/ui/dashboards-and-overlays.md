# Dashboards and overlays

World Domination adds a **WD** main tab to the bottom bar. It is the quickest place to judge the world situation, inspect your network, and open the mod's management windows.

## WD main tab

The upper status area summarizes:

- WD raids launched at your colonies and outposts during the current 1, 4, and 7 day windows
- Your three highest and three lowest goodwill relationships
- Your faction's strength rank, world strength share, active Mid or Late Game stage, and total pawns across your colonies and outposts

The main body has three columns.

### Threats

**Nearby threats** are hostile settlements that can reach your colony and are in the inner half of their own attack radius. They are sorted by distance. **Far threats** can still reach you but are in the outer half of their attack radius. They are sorted by attack strength.

Each row can show either strength as a percentage of your current storyteller raid points or absolute points. Use the `% / #` control to switch. Use `R / C` to switch between raw strength and the strength after WD's storyteller-band clamp. Hover a threat for supporting allies, estimated travel time, and the alternate raw or clamped value. Click it to jump to the settlement.

### Your outposts

This column gives a compact status line for each WD outpost. Click `F`, `S`, or `P` to cycle sorting and display:

- `F`: current food, maximum food, and daily net food, with the lowest stock first
- `S`: current and maximum offensive strength, with the strongest first
- `P`: humanoid and total worker pawn counts, with the largest first

Click an outpost to select it on the world map. The heading opens **Outpost Overview**.

### Travelers

Travelers are moving strength pools used for raids, deliveries, expansion, construction, and other WD world actions. They are not pawn caravans.

Rows show mission type, origin, target, elapsed and total route time, and current versus departure strength. Hover the strength value for the projected arrival strength. A red notice calls out hostile travelers targeting you. Click a row to jump to that traveler, or click the heading for the full **Active Travelers** list.

## Navigation

The WD tab links to:

- **Diplomacy Matrix**: relations, goodwill, WD relation cooldowns, and negotiation
- **Outpost Overview**: all player outposts in a sortable management table
- **World Stats**: faction strength rankings and tier breakdowns
- **Action Log**: the latest 500 WD events and actions
- **Active Travelers**: every moving WD strength pool
- **Your Pawns / All Player Pawns**: filter and move pawns across colonies and outposts
- **Prisoners**: review prisoners and assign destinations after recruitment

The gear button opens WD settings.

## World Stats and Faction Details

**World Stats** is the strategic ranking screen. It lists every faction's settlement count, strength, and world share by tier and in total. It also shows the current escalation stage and temporary faction statuses such as world leader, underdog, expansionist zeal, and anti-leader coalition membership. Click **Details** on a faction row to inspect its individual holdings.

**Faction Details** is the settlement-level view. It lists tier, specialty, name, strength, distance from your colony, and road projects for the player. It can be filtered by type or name and sorted by any main column.

!!! note "The strength number is shared"
    Faction Details uses the local defense total, offense plus defense. This is the same number used to rank settlements and player outposts in World Stats. It is not the raid-launch pool or incident pool.

## Outpost tabs

Select one of your WD outposts on the world map to use its inspect tabs:

- **Stats**: strength pools, production, logistics, action cooldowns, raid protection, and type-specific statistics
- **Food**: virtual food stock, local daily balance, incoming support, and food distribution assignments
- **Pawns**: occupants, prisoners, mechanoids, animals, vehicles, skills, transfers, and removals
- **Experts**: assign specialists to the outpost's available expert slots
- **Upgrades**: review requirements, materials, research gates, active construction, and completed upgrades
- **Storage**: warehouse inventory and stored goods for warehouse-capable outposts

Tabs only appear when they apply. For example, Storage is relevant to warehouse outposts.

## World map WD control

The WD control on the world map opens the overlay menu. Overlays show tile data (fertility, animals, mining, movement, pollution) and labels for settlements, outposts, and fortifications. The hold key is configurable in WD settings (default **Left Alt**).

??? note "Advanced"
    Hold the configured key and press:

    - `X`: WD main tab
    - `A`: Your Pawns
    - `S`: World Stats
    - `D`: Diplomacy Matrix
    - `Y`: Prisoners
    - `F`: Outpost Overview
    - `G`: Active Travelers

    On the world map, the same hold key also toggles overlays:

    - `1`: blocked tiles
    - `2`: fertility
    - `3`: animals
    - `4`: fish
    - `5`: mining
    - `6`: movement
    - `7`: pollution
    - `Q`: highlight relationships
    - `W`: highlight player
    - `E`: settlement tier labels
    - `R`: road blocks and traps
    - `T`: outpost labels

    Press the same combination again to turn that overlay off.

