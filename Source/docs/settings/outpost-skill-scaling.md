# Outpost skill scaling settings

This page controls diminishing returns for cumulative outpost production skills. Founding requirements continue to use raw skill even when scaling is enabled.

| Control | Default | What it changes |
|---|---:|---|
| Enable diminishing returns | On | Applies the configured efficiency bands to cumulative production skill. |
| Hard cap, raw skill | 280 | Raw skill above this value adds no production capacity. |
| Band 1 end and efficiency | 60 at 100% | Skill points 0 through 60 count fully. |
| Band 2 end and efficiency | 100 at 80% | Skill points 61 through 100 count at 80%. |
| Band 3 end and efficiency | 160 at 60% | Skill points 101 through 160 count at 60%. |
| Band 4 end and efficiency | 220 at 40% | Skill points 161 through 220 count at 40%. |
| Band 5 end and efficiency | 280 at 20% | Skill points 221 through 280 count at 20%. |
| Sample raw skill | 100 | Preview-only value used to display effective skill. It is not saved as a gameplay setting. |

Band ends are kept in ascending order, and a band's efficiency cannot exceed the preceding band. Reset restores the five thresholds, efficiencies, hard cap, and enabled state shown above.
