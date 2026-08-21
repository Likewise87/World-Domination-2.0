# Spy operations

Sabotage and disinformation let a normal player caravan weaken nearby WD settlements without launching a raid. Both actions risk the selected pawn and can destroy a settlement if the strength reduction brings it to zero.

## Where to find them

Move a player caravan next to one or more eligible WD settlements, then select the caravan on the world map. Its command bar adds:

- **Sabotage**, if the caravan has a conscious, mobile humanlike pawn capable of skilled manual work
- **Spread Disinformation**, if the caravan has a conscious, mobile humanlike pawn capable of Social work

The mod automatically selects the caravan's best eligible pawn for each operation. If several settlements are in range, clicking the command opens a target list with tier and displayed success chance. A settlement on espionage cooldown is marked unavailable.

Sabotage and disinformation use the same settlement espionage alert timer. An attempt immediately puts the target on cooldown, whether it succeeds or fails.

## Sabotage

Sabotage uses the selected pawn's **Crafting** skill.

On success, the target loses:

`base reduction + Crafting level × reduction per Crafting level`

The settlement can drop to a lower tier. If its strength reaches zero, it is removed and temporary ruins remain on the tile.

Success becomes more likely with Crafting and less likely against higher-tier settlements. Pawn health also matters. At the default 100% health impact, reduced Consciousness, Moving, Manipulation, Talking, Sight, or Hearing lowers the success weight.

## Disinformation

Disinformation uses the selected pawn's **Social** skill. It represents rumors, informants, and political disruption rather than physical damage, but mechanically it also removes settlement strength.

On success, the target loses:

`base reduction + Social level × reduction per Social level`

It can reduce the target's tier or collapse it into temporary ruins. Social increases the success weight, target tier reduces it, and the same six health capacities affect the attempt.

## Failure and risk

Each operation rolls among four weighted results:

- **Success**: settlement strength is reduced
- **Clean failure**: no strength reduction, but the pawn escapes safely
- **Injury**: the pawn suffers 1 to 4 random cut or blunt injuries
- **Fatal failure**: the pawn is executed

Failed operations have two skill-based recovery checks:

- Social can convert an injury into a clean escape
- the higher of Shooting or Melee can convert a fatal result into an injury

Detection means an injury or fatal result. It costs **30 goodwill** with the target faction. A clean failure does not apply that goodwill penalty. The cooldown still applies in every case.

Practical precautions:

- send healthy pawns, because poor capacities reduce success
- favor high Crafting for sabotage or high Social for disinformation
- bring combat skill to improve survival after a fatal roll
- use Social as additional protection against an injury roll
- inspect the shown target tier and success chance before committing
- remember that the pawn remains part of the caravan after a nonfatal result, including with new wounds

??? note "Advanced"
    The values below are defaults. The four base numbers are relative weights, not direct percentages. The final probability is the modified result weight divided by the sum of all outcome weights.

    **Sabotage defaults**

    - Base weights, success / clean / injury / fatal: **37 / 32 / 25 / 6**
    - Success weight per Crafting level: **+5**
    - Success weight per target tier: **-5**
    - Health impact: **100%**
    - Social injury-to-clean save per level: **2%**
    - highest combat skill fatal-to-injury save per level: **2%**
    - Base strength reduction: **225**
    - Additional reduction per Crafting level: **20**
    - Target cooldown after every attempt: **5 days**

    Success weight is clamped to at least 1 before health is applied. The displayed final chance is clamped between 1% and 99%.

    **Disinformation defaults**

    - Base weights, success / clean / injury / fatal: **40 / 30 / 25 / 5**
    - Success weight per Social level: **+5**
    - Success weight per target tier: **-5**
    - Health impact: **100%**
    - Social injury-to-clean save per level: **2%**
    - highest combat skill fatal-to-injury save per level: **2%**
    - Base strength reduction: **150**
    - Additional reduction per Social level: **15**
    - Target cooldown after every attempt: **5 days**

    Success weight and displayed chance use the same lower and upper clamps as sabotage.

## Settings

See [Sabotage settings](settings/sabotage.md) and [Disinformation settings](settings/disinformation.md) for every configurable weight, modifier, save, outcome value, and live T2 example.
