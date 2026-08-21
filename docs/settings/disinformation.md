# Disinformation settings

This advanced page controls outcome weights, skill modifiers, recovery checks, damage, and cooldown for pawn disinformation campaigns.

## Base outcome weights

Weights are relative, not direct percentages.

| Outcome | Default weight |
|---|---:|
| Success | 40 |
| Clean failure | 30 |
| Injured failure | 25 |
| Fatal failure | 5 |

## Success modifiers

| Control | Default | What it changes |
|---|---:|---|
| Success weight bonus per Social level | 5.0 | Adds flat success weight for each Social level. |
| Success weight penalty per settlement tier | 5.0 | Subtracts success weight for each target tier. |
| Health impact on success | 100% | At 100%, a pawn at 50% health has half its modified success weight. |

## Saving throws

| Control | Default | What it changes |
|---|---:|---|
| Silvertongue bonus per Social level | 2% | Per-level chance to convert injury into a clean escape. |
| Fight-your-way-out bonus per Combat level | 2% | Per-level chance to convert a fatal result into injury, using the better of Shooting and Melee. |

## Success outcome and cooldown

| Control | Default | What it changes |
|---|---:|---|
| Base strength point reduction | 150 | Strength removed on success before the Social bonus. |
| Strength reduction per Social level | 15 | Additional strength removed for each Social level. |
| Cooldown after attempt | 5 days | Target high-alert period that blocks another disinformation attempt. |

The skill-level 0, 10, and 20 examples are live previews against a healthy T2 target. They are not editable settings.
