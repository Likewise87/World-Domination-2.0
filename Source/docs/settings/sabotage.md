# Sabotage settings

This advanced page controls the outcome weights, skill modifiers, recovery checks, damage, and cooldown for pawn sabotage missions.

## Base outcome weights

Weights are relative, not direct percentages. Their current share of the total determines the base outcome chance.

| Outcome | Default weight |
|---|---:|
| Success | 37 |
| Clean failure | 32 |
| Injured failure | 25 |
| Fatal failure | 6 |

## Success modifiers

| Control | Default | What it changes |
|---|---:|---|
| Success weight bonus per Crafting level | 5.0 | Adds flat success weight for each Crafting level. |
| Success weight penalty per target tier | 5.0 | Subtracts success weight for each settlement tier. |
| Health impact on success | 100% | At 100%, a pawn at 50% health has half its modified success weight. |

## Saving throws

| Control | Default | What it changes |
|---|---:|---|
| Silvertongue bonus per Social level | 2% | Per-level chance to convert injury into a clean escape. |
| Fight-your-way-out bonus per Combat level | 2% | Per-level chance to convert a fatal result into injury, using the better of Shooting and Melee. |

## Success outcome and cooldown

| Control | Default | What it changes |
|---|---:|---|
| Base strength reduction | 225 | Strength removed on success before the Crafting bonus. |
| Reduction per Crafting level | 20 | Additional strength removed for each Crafting level. |
| Cooldown after attempt | 5 days | Target high-alert period that blocks another sabotage attempt. |

The simulation rows for relevant skill levels 0, 10, and 20 are live previews, not editable or saved settings. They show the resulting success, clean-failure, injury, and death probabilities against a healthy T2 target.
