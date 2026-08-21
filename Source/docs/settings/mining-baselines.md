# Mining baseline values

This advanced page sets the baseline quantity produced per cumulative Mining skill per production cycle for each mining resource. Before other output modifiers, the basic structure is:

`baseline quantity × cumulative Mining skill × terrain efficiency`

For example, a baseline of 10 and cumulative Mining skill of 5 produces 50 units before terrain efficiency and other multipliers.

## Stone blocks

Stone controls use whole-number values from 1 to 100.

| Resource group | Default |
|---|---:|
| Granite, marble, sandstone, limestone, and slate blocks | 25 each |
| Other modded `Blocks*` resources | 25 unless explicitly configured |

## Ores and mineables

The list is built dynamically from effective scatter ores, including supported modded resources, and uses values from 0.1 to 100.

| Resource | Default baseline |
|---|---:|
| Steel | 40 |
| Jade | 5 |
| Silver | 8 |
| Gold | 2.5 |
| Plasteel | 2.5 |
| Uranium | 6 |
| Spacer component | 0.2 |
| Industrial component | 1 |
| Other discovered ores | Computed from the resource's mining baseline |
| Unknown resource fallback | 10 |

Reset restores the canonical per-resource defaults from `Settings.cs`, including dynamically computed defaults for mineables not explicitly listed there.
