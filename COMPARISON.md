# RedFoxGirl comparison and recovery changes — 1.1.0

Reference: RedFoxGirl(1).cmo3 (original). Baseline: RedFoxGirl(2).cmo3 (recovered).
Both supplied archives were decoded and their serialized object data compared. Version 1.1.0 was then tested by regenerating a project from the earlier supplied runtime export and companion JSONs; the original project was used only for verification.

| Item | Original | Supplied recovery | Version 1.1.0 output |
|---|---:|---:|---:|
| Parameters | 49 | 49 | 49 |
| Warp + rotation deformers | 37 | 37 | 37 |
| Meshes | 41 | 38 | 38 |
| Native physics setups | 12 | 0 | 12 |
| Parameter groups including root | 2 | 0 | 2 |
| Model-image objects | 42 | 1 | 1 |
| Source layer objects | 80 | 1 | 1 |

## Geometry checks

All 38 common meshes have identical triangle indices. Their 315 keyform position arrays match exactly in the serialized coordinate system when indexed by parameter IDs and key values. This strengthens the earlier position comparison, which matched position arrays without checking parameter-grid associations. This does not validate rendered output, all deformer transforms, or interpolation between keys.

The absent mesh IDs are `mouth`, `ArtMesh0`, and `ArtMesh1`. They exist in the original but not in the runtime MOC3 supplied previously. This build does not invent them or copy them from the original.

## Physics improvement

The original's native physics structure was used to understand the editor representation. Generic import code now maps physics3.json into that structure:

- 12 setups, 26 input links, 12 output links, and 44 particles restored.
- 626 serialized fields compared, including parameter references, names, axis types, reflection flags, weights, scales, vertex positions and normalization.
- Non-numeric fields match; maximum numeric deviation is 0.000002, consistent with source/export decimal rounding.
- FPS restored from runtime metadata.
- Non-default global gravity/wind is retained in JSON and explicitly warned as not mapped to Editor fields. The supplied export uses default gravity/wind.

The output uses new GUIDs, as expected for reconstruction. The selected physics setup defaults to the first imported setup; original selection state is not recoverable.

## Parameter-group improvement

The expression folder and root group are reconstructed from cdi3.json. Names and member sets match the original. Group colors and expansion states use defaults; these editor preferences are not present in CDI. Parent relationships are validated; cycles or unknown parents produce a report warning.

## Still different

The runtime atlas does not contain the original PSD authoring structure. The original has 42 model-image objects and 80 source-layer objects, while the recovered output uses one atlas-backed model image/layer. Original GUIDs, deformer labels, comments, Bezier/editor extensions, selection preferences and unused geometry are not fully recreated. Deformer counts match, but counts alone do not establish all their editor behavior is equivalent.

Expressions, motions, pose, userdata and VTube Studio configuration remain original files in the recovery ZIP rather than native editor configurations. Companion files are not silently discarded.

## Validation limits

Shared C# workflow tests cover conversion, names, physics/groups counts, missing textures, output naming, backups, refusal of unapproved overwrite, engine-failure protection, invalid input and companion path restrictions. The Windows x64 executable builds successfully. Independent Python CAFF/XML inspection verified the generated structures and compared the physics/groups to the original.

The Windows EXE and generated model still need manual opening in Windows/Cubism Editor. No claim of full visual or animated equivalence is made.


## 1.2.0 linked control update

Optional inference sets the editor combined flag for adjacent parameters with shared two-axis bindings. RedFoxGirl produces 12 pairs (11 reference pairs plus Body X/Y). Inference is reported explicitly; it does not recover original UI choices exactly. Tests verify the disabled option, unchanged runtime JSON, native physics/folders, output paths and failed-conversion protection. Windows/Cubism visual validation was not performed.


## Version 1.3.0
RedFoxGirl: 6 expression snapshots, 38 meshes, 315 mesh keyforms, 399 local interpolation samples, zero checked-data mismatches, 9 winding warnings requiring visual review. Motion and pose conversion were tested with synthetic fixtures because this sample pack does not supply them. See README for scope and limits.


## Version 1.4.0
Optional PSD import adds source images without changing atlas rig links. RedFoxGirl PSD: 35 raster layers, 29 unique name candidates, two ambiguous fronthair names and four unmatched layer names. Exact raw pixel equality against an independent PSD reader; embedded ICC profile preserved. Existing rig validation remains at 38 meshes, 315 keyforms and 399 local samples. Windows/Cubism source-image display still requires user testing.


## Version 1.4.1
RedFoxGirl MOC3 header version 4 maps to SDK/Cubism 4.2. The recovered targetVersionNo is 4020000, matching the original reference. Header mappings and rejection of unknown versions were checked, and the recovery regression suite passed. No original project is required at runtime. Windows dropdown display remains to be visually confirmed.
