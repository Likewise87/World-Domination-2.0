# Outpost experts

Experts are occupants assigned to specialist roles in the **Experts** tab. They remain normal outpost occupants, but their relevant skill provides an additional strategic benefit.

An outpost needs at least four humanoid occupants before it can assign its first expert. Add more occupants to unlock further slots, then choose a role and an eligible pawn. If the humanoid population falls below the required capacity, excess assignments are removed.

## Expert roles

### Strategist

The Strategist increases the range of manual raids launched from the outpost. At an Artillery Outpost, the same role also increases mortar and anti-air attack range. The bonus scales with Intellectual skill.

Use a Strategist at a forward base that needs to reach distant targets, or at an Artillery Outpost whose coverage falls just short of a contested route.

### Entertainer

The Entertainer increases production output. The bonus scales with the higher of Artistic or Social skill.

Assign this role to a productive goods, virtual food, or training site. Outpost types without a supported production path do not receive this bonus.

### Cook

The Cook increases production output and offensive strength recovery. Both bonuses scale with Cooking skill.

This is a strong general-purpose role for production sites that frequently launch deliveries, attacks, or construction crews.

### Doctor

The Doctor improves occupant healing and offensive strength recovery. Both bonuses scale with Medicine skill.

Use a Doctor at frequently attacked outposts, sites that launch operations often, or locations holding wounded occupants.

### Engineer

The Engineer speeds up construction projects, extends their planning radius, and improves defensive strength recovery. Project speed and recovery scale with the higher of Construction or Crafting skill up to the configured maximum. Planning radius uses the same skill but has its own maximum bonus.

Construction projects include roads and road blocks. The Engineer is therefore most valuable at an infrastructure hub or exposed border outpost.

### Warden

Every outpost reduces prisoner resistance according to its cumulative Social skill. The Warden adds a percentage bonus to that resistance reduction. Captives still lose resistance without a Warden, but only from the base cumulative Social contribution.

Assign a Warden where defense victories regularly produce prisoners. The role does not replace the need for Social-skilled occupants.

## Assignment guidance

- Assign the role to a pawn with a strong governing skill, not merely an otherwise idle occupant.
- Check the expanded benefit display in the Experts tab before confirming. It shows the actual bonus at the pawn's current skill.
- Match the role to the outpost's work. A Strategist provides little value at a rear-area warehouse, while an Entertainer may have no production path at a military-only site.
- Keep population changes in mind before transferring occupants away. Losing a slot can automatically remove an expert assignment.

??? note "Advanced"
    Expert capacity is:

    `floor(humanoid occupants / 4)`

    The result is also limited by the number of available expert roles. Animals, vehicles, mechanoids, and prisoners that do not qualify as humanoid occupants do not add slots.

    Examples:

    | Humanoid occupants | Expert slots |
    |---:|---:|
    | 0 to 3 | 0 |
    | 4 to 7 | 1 |
    | 8 to 11 | 2 |
    | 12 to 15 | 3 |
    | 16 to 19 | 4 |
    | 20 to 23 | 5 |
    | 24 or more | Up to all 6 roles |
