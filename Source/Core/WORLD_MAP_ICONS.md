# World-map icons and cached textures

How to use this file: open when adding or changing globe-mesh `Material`, expanding icons, or any static `Texture2D` / `Material` fields anywhere in the codebase. Do not use it for keyed copy, tiles/pathing (`PLANET_LAYERS.md`), or hub windows (`UI_WINDOWS.md`). After a code change that moves an icon rule, edit this file in the same pass.

## Unity assets on static fields (`Texture2D` / `Material`)

RimWorld requires types that declare **static** `Texture2D` or `Material` fields to be marked with `[StaticConstructorOnStartup]`. Those assets must load on the **main thread** at startup. This is a general C# rule, not just a world-map one. Without the attribute, the game logs:

`Type X probably needs a StaticConstructorOnStartup attribute, because it has a field … of type Texture2D/Material.`

**Always check this when adding cached icons, mats, or lazy `ContentFinder` / `MaterialPool` fields.**

Rules:

- Put `[StaticConstructorOnStartup]` on the **exact type that owns the static field** (not a sibling helper in the same file, unless the field lives there).
- Prefer a small dedicated static holder (see `WD_PlaySettingsWorldRowAssets`, `WorldOverlayLineMaterials`) when a `WorldComponent` / dialog would otherwise only exist for caching icons.
- Nested / companion types with their own static assets need their **own** attribute (e.g. `Dialog_OutpostSelection` vs `WD_OutpostSelectionCachedDefs`).
- Properties that only *return* a `Texture2D` without storing one do not need the attribute; **fields** do.
- Dictionaries of materials (`Dictionary<int, Material>`) are not flagged the same way, but if you add a bare `static Material` / `static Texture2D` field, add the attribute.
- Treat this warning as a regression, not as harmless noise. If a new static cached icon/texture/material is added, the owning type must get the attribute in the same pass.

## WD outpost world-map icons (`Material` vs expanding)

Vanilla draws world objects in two layers. Mixing their textures is what made outposts look "rotated" or stacked.

| Layer | API | WD outpost source | When visible |
|-------|-----|-------------------|--------------|
| Globe mesh | `WorldObject.Material` → `DrawQuadTangentialToPlanet` | `Faction.def.settlementTexturePath` (settlement-style, faction-colored) | Far / close zoom; planet-tangent (can look tilted on screen) |
| Expanding UI | `WorldObject.ExpandingIcon` (default from def) | XML `expandingIconTexture` (outpost-type art, e.g. `WorldObjects/WD_Outpost_Farming`) | Screen-upright IMGUI icons while expand transition is active |

**Hard rules (Jul 2026 regression):**

- **Never** point `WorldObject_WD_Outpost.Material` at `def.texture` / outpost-type art. That stacks the same art as an upright expanding icon **on top of** a planet-tangent mesh → "normal icon + rotated copy".
- Keep `Material` on `Faction.def.settlementTexturePath` (fallback `World/WorldObjects/Settlements/Settlement`), same as the working Jul 22 backup.
- Outpost-type identity when zoomed for expanding icons comes from XML `expandingIconTexture` only. Do not override `ExpandingIcon` to `FactionIcon` (that made every outpost look like the colony).
- If AA (or any upgrade) should change the look, swap **`ExpandingIcon` only** (or XML), never the `Material` path used for the globe mesh. Mortar + AA upgrade uses `WorldObjects/WD_Outpost_Mortar_AA` via `WorldObject_WD_Outpost.ExpandingIcon`.
- Do not empty-override `Draw()` to "fix" tilt. Restore the settlement-path `Material` instead.
- **Suppressing the globe mesh for WD outposts / travelers / settlements:** use isolated `Patch_WdWorldObjectNoExpandingIcon` (TransitionPct=1 **plus** both Expandable/NonExpandable `ShouldSkip` so Material never draws), gated by Notifications settings toggles (default on). That keeps the upright expanding icon at every zoom and avoids the planet-tangent / double-image look. Do not point Material at outpost-type art as a substitute.
- **Mortar / flak shells + `MortarWorldFx`:** at far zoom use the same `WD_WorldMapZoomUtil.IsSurfaceOverlayZoomedTooFarOut` gate as road blocks / spike traps. Shell travelers (`MortarStrike` / `AntiAirStrike` only — not drop pods) force TransitionPct=0 and skip both Material layers so they do not stay visible from space when always-show traveler icons is on.
- **Close-zoom disappearing icons:** vanilla fades expanding icons at `WorldCameraZoomRange.VeryClose` and shows Material instead. With Material skipped, icons must stay (`TransitionPct` Prefix/Postfix = 1 for ForceFixedIcon). Also patch `WorldObjectSelectionUtility.HiddenBehindTerrainNow`: near the surface the camera–icon chord clips inside the planet sphere (`obstructsExpandingIcons`), so the hide test false-positives and blanks upright settlement/outpost icons. Bypass that hide only at Close/VeryClose for ForceFixedIcon objects on the **camera-facing hemisphere**, measured in the object's `PlanetLayer.Origin` frame (`Dot(DrawPos - Origin, CameraPosition - Origin) > 0`). Do not use world-origin Dot (wrong when layer Origin is offset). Keep far-side hide so icons do not show through the planet. At Far/VeryFar leave vanilla hide alone. Never restore Material for ForceFixedIcon to “fix” VeryClose blanks.
- Road-block `DrawQuadTangentialToPlanet` rotation and FlakSmoke `GUI.matrix` rotation were red herrings for this bug.

## Traveler expanding / Material icons (`ResolveIconTexturePath`)

`WorldObject_Traveler` resolves art per instance (shared defs, mission flags). Both `ExpandingIcon` and `Material` use `ResolveIconTexturePath()` (cached; call `InvalidateTravelerMaterialCache` if the path can change after spawn).

| Traveler | Path |
|----------|------|
| Turtle consolidate | `WorldObjects/Caravan_Turtle` (Raid def is only the spawn vehicle) |
| Vanguard mass relocation | `WorldObjects/Caravan_Vanguard` (also on `TSA_WD_Traveler_MassRelocation` XML) |
| Invasion land raid (`isInvasionRaid`) | `WorldObjects/Caravan_Invasion` |
| Invasion rally / host (`DesperationRally` + `!isDesperationRaid`) | `WorldObjects/Caravan_Invasion` |
| Invasion drop-pod / gravship | Keep def `DropPod_Raiders` / `Gravship_Raiders` |
| Else | Def `expandingIconTexture` / `texture` |

Do not point traveler `Material` at settlement art. Keep mission overrides in `ResolveIconTexturePath`, not one-off XML defs per flag.

## Fortifications (AT turrets, roadblocks, traps)

| Thing | Globe / overlay | Expanding / identity | Build float menu |
|-------|-----------------|----------------------|------------------|
| AT turret | `Material` = settlement path + faction tint (never AT_Gun) | `WorldObjects/AT_Gun_{Light\|Medium\|Heavy}` via `AtTurretUtility.TexturePathForTier` | `UI/Commands/AT_Gun_{Light\|Medium\|Heavy}_Side` via `UiSideTexturePathForTier` |
| Roadblock | Overlay `MatFrom` + `builtByFaction.Color` (cyan fallback) | n/a (not a WorldObject) | Kind icons from `RoadBlockKindUtil.TexturePath` → `WorldObjects/RoadBlock_{Light\|Medium\|Heavy}` |
| Spike / caltrops | Overlay `MatFrom` + faction tint | n/a | `WorldObjects/WorldSpikeTrap` / `WorldObjects/Caltrops` |

Do **not** use `*_Colorized` texture filenames. Greyscale WorldObjects art is tinted at draw time. Do **not** point AT build menus at world `AT_Gun_*` or ExpandingIcon at `*_Side`.
