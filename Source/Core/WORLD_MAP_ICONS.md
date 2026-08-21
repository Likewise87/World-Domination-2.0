# World-map icons and cached textures

How to use this file: open when adding or changing globe-mesh `Material`, expanding icons, or static `Texture2D` / `Material` fields. Do not use it for keyed copy, tiles/pathing (`PLANET_LAYERS.md`), or hub windows (`UI_WINDOWS.md`). After a code change that moves an icon rule, edit this file in the same pass.

## Unity assets on static fields (`Texture2D` / `Material`)

RimWorld requires types that declare **static** `Texture2D` or `Material` fields to be marked with `[StaticConstructorOnStartup]`. Those assets must load on the **main thread** at startup. Without the attribute, the game logs:

`Type X probably needs a StaticConstructorOnStartup attribute, because it has a field … of type Texture2D/Material.`

**Always check this when adding cached icons, mats, or lazy `ContentFinder` / `MaterialPool` fields.**

Rules:

- Put `[StaticConstructorOnStartup]` on the **exact type that owns the static field** (not a sibling helper in the same file, unless the field lives there).
- Prefer a small dedicated static holder (see `WD_PlaySettingsWorldRowAssets`, `WorldOverlayLineMaterials`) when a `WorldComponent` / dialog would otherwise only exist for caching icons.
- Nested / companion types with their own static assets need their **own** attribute (e.g. `Dialog_OutpostSelection` vs `WD_OutpostSelectionCachedDefs`).
- Properties that only *return* a `Texture2D` without storing one do not need the attribute; **fields** do.
- Dictionaries of materials (`Dictionary<int, Material>`) are not flagged the same way, but if you add a bare `static Material` / `static Texture2D` field, add the attribute.

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
- **Close-zoom disappearing icons:** vanilla fades expanding icons at `WorldCameraZoomRange.VeryClose` and shows Material instead. With Material skipped, icons must stay. Also patch `WorldObjectSelectionUtility.HiddenBehindTerrainNow`: near the surface the camera–icon chord clips inside the planet sphere (`obstructsExpandingIcons`), so the hide test false-positives and blanks every upright icon. Bypass that hide only at Close/VeryClose for ForceFixedIcon objects that are on the **camera-facing hemisphere** (`Dot(DrawPos, camPos) > 0`); keep far-side hide so icons do not show through the planet. At Far/VeryFar leave vanilla hide alone.
- Road-block `DrawQuadTangentialToPlanet` rotation and FlakSmoke `GUI.matrix` rotation were red herrings for this bug.
