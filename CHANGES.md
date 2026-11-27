# MocRecovery 2.0.3

- Added an experimental layered working PSD export from the compiled model and its texture atlas PNGs. Enable it in the recovery options to save a separate .working.psd beside the recovered project.

# MocRecovery 2.02

- Restore the front-to-back order of ArtMeshes inside each Part from the compiled draw-order groups. Overlapping meshes with equal draw order now follow the source stacking order, including lips, teeth, and the mouth interior.

# MocRecovery 2.01

- The optional naming pass labels generically named ArtMeshes with their owning Part name followed by the numeric suffix of their runtime ID, such as Light111. The original ID is preserved. Existing descriptive names and PSD-derived labels take precedence.

# MocRecovery 2.0

- Added an Artwork tab with one row per ArtMesh. It shows the artwork source and image dimensions, and explains skipped crops, including meshes with no triangles or no covered atlas pixels.
- The table reports placement error from the saved transforms in atlas pixels. For atlas crops, it also compares visible pixels and partially transparent edges with the source atlas.
- Added placement.json to the recovery ZIP alongside artwork.json.

# MocRecovery 1.93

- Added a Physics tab after Texture accuracy with per-group inputs, outputs, normalization ranges, particles and import notes.

- The Texture accuracy tab displays each mesh in a spaced table with aligned percentages, vertex counts and pixel offsets.
- The Recovery tab shows the completion summary and report header. The dedicated Recovery report tab holds the details.

- Combined all release notes in CHANGES.md, with the latest release first.

# MocRecovery 1.92

- Rebuild out-of-tolerance ArtMesh texture reference positions from the original atlas UVs. Preserve keyed geometry.
- Add a Texture accuracy tab and a separate per-model text report with the percentage of vertices within 0.1 atlas pixel and predicted maximum offsets before and after correction.

# MocRecovery 1.91

- Preserve each warp deformer’s runtime transform mode during recovery. Mixed quad and legacy warps retain their individual settings. The recovery report includes the counts for each mode.

# MocRecovery 1.9

- Write editor model settings alongside recovered physics groups, including the shared root parameter group, model options, effect settings, guides, brush settings and random-pose defaults. Generate transparent placeholder model icons.
- Build these fields from conversion data. An original editor project is never required. Models with imported physics and no CDI group definitions receive a root parameter group.

# MocRecovery 1.8.6

- Decode non-interlaced indexed PNG atlases at 1, 2, 4 and 8 bits per pixel for ArtMesh crops and PSD artwork matching. Palette alpha values are preserved, including partial transparency. Palette entries without an alpha value are opaque.
- Added regression checks comparing indexed and RGBA atlases through artwork sampling and triangle-masked crop generation.

# MocRecovery 1.8.5

- Open a full-window Recovery report tab after a successful conversion. Switch back to Recovery to review inputs. Select the warning count to jump to the grouped engine and validation warnings.

- Align the model and output paths in one column. Show indexed textures and list attached PSD filenames with full paths on hover, individual removal and Clear PSDs.
- Stack both PSD options in the left column, with parameter-grid recovery and deformer naming in the right column. Place Create .cmo3, Cancel and Open folder below the options.
- Give the word-wrapped recovery report a vertical scrollbar and Copy report button. On success show the saved filename, engine and validation warning count, and number of skipped atlas mesh crops.

# MocRecovery 1.8.4

- Align Choose .moc3 and Choose output folder with the left edge of the other buttons. Each selected path appears to the right of its button.
- Show the current output folder before and after model selection. The folder button now uses the same padding and automatic sizing as the other buttons.
- Rename the PSD option to “Attempt PSD texture relinking (experimental)”.

# MocRecovery 1.8.3

- Preserve the selected physics group identifier’s note and assign its shared-object index. Validate both fields before saving a recovery.
- Minor GUI fix: Cancel now uses the same sizing, padding, and alignment as Create .cmo3 and Open folder.
- Experimental PSD texture relinking turns on whenever a new PSD is attached and applies to all attached PSDs.

# MocRecovery 1.8.2

- Fixed physics group serialization. The selected group now shares its identifier with the group list. Recovery also writes Physics window settings and validates the group list and selection.

# MocRecovery 1.8.1

- Add multiple optional PSDs, including files from different folders. PSDs are read one at a time. Artwork matching considers layers from all selected files before relinking. Each source has its own group, layer inventory and backup location.

# MocRecovery 1.8

- Added “Guess deformer names from ArtMeshes (experimental)”, enabled by default. A placeholder deformer ID can take a unique mesh label when it directly controls artwork and all controlled meshes agree. Shared candidates, existing labels and descriptive IDs are protected. Clearing the option keeps the stored labels. See Output.recovery.txt for individual decisions.

- Recover editable rotation deformer, part and glue blend shapes from Cubism 5.0-compatible runtime exports. Rotation targets restore origin, angle, scale, opacity and available color offsets. Part targets restore draw order and the exported draw-order group flag. Glue targets restore keyed intensity while preserving the existing mesh bindings and vertex weights.
- Restore exported weight constraints for these targets. Output.recovery.txt lists editable target counts separately for warp deformers, ArtMeshes, parts, rotation deformers and glue.
- Verified the sample model’s ten rotation keys and one part key against its editor project. A synthetic glue target passed end-to-end conversion. The included BlendShapeTests exercise all five target types in little- and big-endian tables. Visual playback in Cubism Editor remains unverified.

# MocRecovery 1.7.3

- Recover editable warp and ArtMesh blend targets by runtime object, parameter and key. Objects with several ordinary keyforms use the grid key closest to the ordinary parameter defaults. An exact tie selects the first key on that axis.
- Restore geometry and opacity offsets, ArtMesh draw order and Cubism 5 per-key multiply and screen color offsets. Rebuild native weight limits with their controlling parameters, breakpoint values and weights.
- Report separate warp, ArtMesh and weight breakpoint totals. Rotation deformer, part and glue blend targets are still skipped.
- Include standalone synthetic fixtures in source/BlendShapeTests for an unconstrained warp and a constrained ArtMesh, covering different coordinate spaces, multiple ordinary forms and both byte orders.

# MocRecovery 1.7.2

The recovery report is saved as `Output.recovery.txt` in the output folder for every model. When a report already exists, the app asks before replacing it and keeps a timestamped backup after an approved replacement.

Publishing now checks that the current release entry appears at the top of CHANGES.md and includes both changelog files beside the executable.

# MocRecovery 1.7.1

- Choose an output folder for the CMO3, recovery report and ZIP. Each launch defaults to the folder containing MocRecovery.exe. Use app folder restores that destination during the session.
- Cancel an active conversion and retry with the same model, PSD and texture selections. Cancellation stops the conversion engine or takes effect at the next processing checkpoint. Existing outputs are preserved. Final saving completes as one operation.

# MocRecovery 1.7.0

## Blend shapes

MocRecovery reads blend-shape parameter types and warp keyform offsets from Cubism 4.2 and later MOC3 files. It rebuilds editable warp targets for unconstrained bindings with one ordinary base form. The recovery report identifies skipped constrained bindings, warps with multiple ordinary forms, and unsupported target types. Blend-shape color animation remains unverified.

## Editable artwork without a PSD

Each ArtMesh can now have its own source image cropped from a runtime texture atlas. The converter uses the mesh triangles and UV coordinates to mask the crop. It embeds the image in the CMO3 and exports a PNG to `editor-import/atlas-artwork/` in the recovery ZIP. The accompanying `artwork.json` maps each crop to its mesh ID, atlas page and pixel bounds.

When experimental PSD relinking succeeds, that mesh keeps its verified PSD source. Other eligible meshes use atlas crops. The active rendering input remains the atlas. Extraction supports non-interlaced 8-bit RGB and RGBA PNG pages. The report identifies missing or unsupported pages.

## Example verification

- Recovered five blend-shape parameter types and six lip targets from the runtime MOC3. MouthX, MouthPressLipOpen and MouthPucker each regained their −1 and +1 keyforms. Every recovered lattice coordinate matched the runtime base plus its stored offset.
- Without a PSD, all 38 runtime ArtMeshes received individual atlas images. With Example.psd and experimental relinking enabled, eight meshes retained verified PSD links and 30 received atlas images.
- The visible pixels in the exported crops matched the atlas. Regression tests covered both conversion paths. Visual playback in Cubism Editor remains unverified.

Conversion uses the runtime model, available companion files and an optional PSD. The original editor project is never a recovery input.

# MocRecovery 1.6.2

The window opens maximized by default, using the available desktop work area. Standard window controls remain available. The restored window size and recovery behavior are unchanged. Windows cross-build verified; live Windows visual testing was not performed.

# MocRecovery 1.6.1

- Replaces the marquee indicator with a determinate bar and adjacent percentage label.
- Progress resets to 0% on each conversion. A successful conversion retains 100%; selecting another model clears the indicator.
- Core stages use monotonic, bounded 0–100 reporting. PSD import reports completed layers; matching/relinking reports phases/candidates; ZIP preservation reports completed files.
- The percentage is a weighted stage estimate, not a time estimate. The backend exposes no internal percentage, so its stage holds while it runs rather than inventing timer progress.
- 100% is emitted only after the output CMO3, preservation ZIP and report have been installed successfully. Failures retain their last partial percentage.
- Late queued callbacks are ignored after completion/failure, so an older update cannot replace the final status.
- Existing string progress callbacks remain supported. Recovery behavior and output naming are unchanged.
- Automated integration checks cover monotonic range, initial/final values, existing output files at 100%, intermediate archive percentages, failure without 100%, and PSD-layer increments. Windows visual testing remains outstanding.

Determinate percentage progress added. Integration checks verify 0–100 monotonicity, 100 only after committed outputs, no 100 on failure, intermediate ZIP progress and PSD-layer increments. Existing recovery and PSD checks pass. Percentage is stage-based, not a time estimate. Windows visual testing remains outstanding.

# MocRecovery 1.6.0

## New control
Exact checkbox text: Attempt PSD texture relinking (Warning: Experimental)

The option defaults off, is enabled only when a PSD is selected and artwork matching is on, and is disabled during conversion. Clearing/replacing the PSD or selecting another model clears it. Turning off artwork matching also clears/disables relinking. The existing artwork-name matching checkbox remains checked by default when a PSD is selected.

## What is restored
For an accepted candidate, the recovered mesh's CTextureInput_ModelImage now references the imported PSD CModelImage rather than the atlas source image. Its optionalTransformOnCanvas is calculated from the fitted PSD-to-atlas mapping, the recovered atlas-to-canvas mapping, and the inverse of the PSD model-image placement transform. Source-image memos identify restored links.

This matches the source-link-plus-active-atlas pattern inspected in the supplied original project. The currentTextureInputData remains the atlas input. TextureState, GPU texture references, UVs, mesh positions/keyforms, deformer hierarchy and IDs remain unchanged. The original CMO3 is not needed by the algorithm.

## Alignment and rejection
- Starts only from accepted, unique artwork matches.
- Fits independent horizontal/vertical scale and translation with the candidate quarter-turn rotation; arbitrary shear and free-angle rotations are not fitted.
- Uses bounded previews for fitting, then checks a separate 33-by-33 grid inside mesh triangles against full-resolution PNG pixels. Source and atlas sampling use bilinear premultiplied RGBA; source pixels themselves are not modified.
- Requires at least 64 non-negligible-alpha validation samples, normalized RGBA RMS error at most 0.035, and mean alpha error at most 0.035.
- Requires the existing UV/atlas-to-canvas mapping to agree with emitted mesh positions within one canvas pixel.
- Skips ambiguous/weak artwork matches, PSD layer opacity below 255, non-normal blend modes, clipping, recorded masks/effects/smart-object limitations, unavailable inputs and failed checks.
- Completes checks before writing a source reference or transform. Rejected candidates retain their previous source input.

These thresholds are experimental, not a probability of correctness or a rendering guarantee. No ICC conversion, runtime deformation simulation, automatic atlas rebuild, reconstruction of original authoring transforms or guaranteed future PSD reimport is claimed.

## Reports
The on-screen recovery report lists linked counts and a reason for every skipped mesh. editor-import/psd-source/texture-relinks.json contains mesh IDs, candidate PSD layer indices, statuses, full-resolution sample error metrics, geometry-fit error, accepted PSD-to-atlas matrices and written optional transforms. The original PSD and atlas files remain preserved in the recovery ZIP. Existing overwrite backups remain in effect.

## Validation
Synthetic tests cover all four quarter-turn rotations, source/canvas transform composition, full-resolution bilinear pixel sampling, reference changes, and unchanged active atlas/geometry/UVs. They reject altered non-affine geometry, unsupported layer effects and unrelated atlas artwork without mutating source references or transforms. Relinking defaults off in the core plan.

Example test result: 8 source links restored from 25 accepted artwork-name candidates; 30 meshes skipped (13 without a unique artwork match, 15 failing the held-out pixel check, 1 with unsupported layer properties, 1 with excessive atlas-to-canvas fit error).
Restored: ArtMesh (mad sign), depressed, nose, blush, face, pupilright, pupilleft, legleft.
Existing recovery regression tests pass. Enabled/disabled PSD integration tests check actual source-reference targets and unchanged active atlas inputs.
Windows/Cubism visual validation remains outstanding; user testing is necessary before relying on source updates or atlas regeneration. No Live2D SDK/Core dependency was added.

Experimental PSD source texture relinking restores 8 Example source links; 30 meshes are skipped with reasons. Full-resolution held-out pixel samples and canvas-fit checks gate each write. Geometry, UVs, IDs and active atlas inputs remain unchanged. Enabled/disabled integration, four rotations, transform composition, rejected-candidate non-mutation and recovery regression checks pass. Windows/Cubism visual and source-update testing remains outstanding.

# MocRecovery 1.5.1

## Checkbox behavior
- New option: Try preserving PSD layer names using artwork matching (experimental).
- Automatically checked and enabled when a PSD is selected, including a replacement PSD.
- Can be unchecked for the previous name-only behavior.
- Disabled without a PSD and while recovery runs. Clearing the PSD or choosing another model resets it to its checked default, disabled until a PSD is selected.
- A separate auto-sized row keeps it above the report; the label wraps to the available width.

## What matching does
PSD rasters and atlas regions are compared by premultiplied color, alpha shape and aspect, with four quarter-turn rotations. Atlas samples are limited by mesh UV triangles. Row-wise PNG decoding and small previews avoid allocating full PSD or atlas RGBA buffers. Strong candidates require a distance of at most 0.025, a runner-up gap of at least 0.012 and a score no more than 65% of the runner-up score. Scores are heuristic distances, not probabilities.

Accepted candidates restore mesh display labels and add the inferred mesh ID association to the imported source image memo. Stored IDs, runtime geometry, deformer data, source pixels, texture links and transforms are unchanged by matching. The existing later naming pass may derive deformer display labels from the updated mesh labels, with deformer IDs preserved.

artwork-matches.json records candidates, scores, rotation and acceptance/ambiguity/rejection. A candidate shown on a rejected or ambiguous row is not an accepted association. layers.json retains the separate name-only check, explicitly labeled as such.

## Limits
This is not automatic texture relinking. Original placement/scale transforms and original layer identifiers are not reconstructed. Identical duplicate artwork cannot establish a unique original layer. Missing artwork from other source files cannot be supplied by this PSD. Shader effects, atlas edits, color-profile differences and resampling can reduce reliability. Matching uses raw stored color values without ICC conversion and supports non-interlaced 8-bit RGB/RGBA atlas PNGs; other PNG types are reported and skipped for matching while the original files remain preserved. Existing unique-name behavior remains as a fallback when no artwork match is accepted.

## Tests
Example: 25 accepted artwork-name matches, 3 ambiguous and 10 insufficiently close. ArtMesh is correctly labeled mad sign. Both fronthair meshes remain ambiguous because their PSD candidate artwork is indistinguishable. No original CMO3 was used by matching.
Unit checks cover pixel decoding, unrelated IDs, identical candidates, unrelated images, transparent layers, quarter-turn rotation, tiny images and default-enabled plans.
Both enabled and disabled full-PSD integration paths and the recovery regression suite are tested. The enabled integration run peaked at 472,400 KiB RSS (about 461 MiB) in the Linux test environment; this is not a Windows memory guarantee.
Windows/Cubism visual testing remains outstanding. No Live2D SDK/Core dependency was added.

PSD artwork matching is checked by default when a PSD is selected. Example: 25 accepted, 3 ambiguous and 10 insufficiently close matches; ArtMesh resolves to mad sign and duplicate hair remains ambiguous. Both checkbox states, matching unit checks and recovery regression checks pass. Enabled PSD recovery peaked at 472,400 KiB RSS in Linux. IDs and atlas links remain unchanged. Windows UI/Cubism visual testing is pending.

# MocRecovery 1.5.0

This release concentrates on validation and usable companion-file outputs. It does not require an original CMO3 and does not change stored deformer IDs. Optional PSD import and the 1.4.4 memory improvements remain.

## 1. Stronger validation

### Warp and rotation deformers
Previously, validation concentrated on ArtMeshes. It now also compares recovered warp and rotation sources with the decoded MOC model:
- Warp row/column divisions, expected control-point counts, and every keyed control-point coordinate.
- Rotation origins, angles, scales, base angles, and reflection flags.
- Deformer opacity, multiply colors and screen colors.
- Canvas/local coordinate-space tags, including the canvas scaling used by the exporter.
- Mesh and deformer parent links, deformer hierarchy cycles, and deformer object counts.
- Bound parameters, ordered keys, axis products, keyform references, binding references and grid indices.
The validator uses backend form-index metadata to identify deformers after display-name inference. IDs and names are not rewritten by these checks.

### Simultaneous parameter sampling
Single-axis defaults, limits, keys and interval midpoints are retained. Samples are deduplicated. Each object also gets pairwise minimum/maximum combinations and up to 32 deterministic interior combinations when it has multiple bound parameters. The total is capped at 2,048 samples per object to avoid unbounded work. Mesh local positions and opacity are compared at these samples; sampled draw-order differences are checked with allowance for the exporter's documented rounding. Deformer local numeric fields are also compared at sampled parameter values. Mesh multiply/screen colors are checked at keyforms.

These comparisons verify that the emitted local data agrees with the decoded input. They do not independently reproduce Cubism's world-space evaluator. Triangle winding changes remain review warnings because they can be intentional.

### Physics comparison
For each imported physics setup, validation now compares the companion file directly against the emitted editor data: parameter IDs, input/output counts, weights, axis types, reflection, output particle indices and scale, particle positions, mobility, delay, acceleration, radius, and normalization ranges. Existing finite-number and particle-reference checks remain; parameter references, allowed axis types and weight bounds are also checked.
A setup that could not be imported is reported as unverified, not silently counted as a successful comparison. Global gravity/wind and simulation over time remain outside this comparison.

### Visual review motions
The validation folder now contains parameter-pairs.motion3.json when co-bound pairs exist. It visits four limit corners for each pair while other parameters use defaults, then returns to defaults. A companion schedule JSON identifies parameters and time ranges. Up to 64 distinct co-bound pairs are included. The existing single-parameter sweep is retained.

## 2. Companion-file conversion

### Expressions
Existing full-weight snapshots remain. Each convertible expression also produces a .fade-preview.motion3.json clip: an isolated fade-in against defaults, a one-second hold, and fade-out using the supplied fade times (one second when omitted). The cosine envelope is sampled at approximately 30 FPS, with at most 300 intervals per fade to bound output size. This is a convenience preview with resolved/clamped expression targets, not a native expression controller or exact runtime blend evaluation. Zero-duration fades are instantaneous at their boundary; stopping/resetting the clip restores the base pose. Runtime stacking, expression priorities and blending against an animated base are not recreated.

### Poses
Each group/choice snapshot now includes every pose group. Non-selected groups use their first listed choice, avoiding leftover visibility from a previously imported snapshot. Linked parts follow their owning choice. All groups are validated before any pose snapshot is written; an invalid later group no longer leaves apparently usable partial pose previews. Repeated part ownership across groups is rejected as ambiguous. FadeInTime is recorded in index.json; automatic selection and runtime crossfade logic are not baked.

### Motions
The unmodified motion file remains available. A second .editor.motion3.json copy retains only Parameter and PartOpacity curves whose IDs exist in the recovered model. Counts are recalculated. Retained curve values, segment types/control points, per-curve fade fields, top-level fade metadata and events are preserved. Unknown targets and model-level effects are omitted only from this filtered copy, with counts in the report/index.
Motion checking now also validates fade durations and event times/counts/UTF-8 byte totals. Editor import is not guaranteed to execute runtime events, sounds or model effects.

## Verification

Example without its original CMO3:
- 38 meshes, 315 mesh keyforms, 829 local mesh samples.
- 37 deformers, 206 deformer keyforms, 837 local deformer samples.
- 12 imported physics setups compared with the supplied physics companion.
- No checked-data mismatches; nine mesh winding-review warnings remain.
- Six expression snapshots plus six fade previews; Example does not supply motion/pose fixtures, so those paths also use synthetic tests.
- Regression checks retain stored IDs, ArtMesh-derived names, SDK 4.2 target, output paths, backups and failed-conversion protection.

Deliberate corruption tests detect altered warp points, rotation angle, parent link, deformer grid access and physics scale. Synthetic companion tests cover expression fade start/midpoint/target/return, all motion segment types, unsupported motion targets, retained fade metadata and Unicode events, invalid events, multi-group pose resets, linked parts and rejection of partial pose exports. Generated review/import motions pass the motion validator. Optional PSD integration is tested separately.

## What is still not verified or rebuilt

No rendered clipping/compositing comparison, exact world-space deformer evaluation, physics time simulation, exhaustive parameter-combination coverage, CAN3 scene generation, native expression/pose controllers, or automatic motion model-effect baking is claimed. Windows/Cubism visual import behavior still needs user testing. Originals and unsupported information remain in the recovery ZIP. This release adds no Live2D SDK/Core dependency.

38 meshes / 315 keyforms / 829 mesh samples; 37 deformers / 206 keyforms / 837 deformer samples; 12 physics setups compared with companion fields. Zero checked-data mismatches; nine winding warnings remain. Corruption, companion-conversion and optional-PSD integration tests pass. Windows/Cubism visual testing remains outstanding.

# MocRecovery 1.4.4

Replaced full-raster PSD allocations with row decoding and chunked PNG encoding. CAFF reads/writes no longer allocate full archive copies. Full recovery and PSD integration checks pass; all 35 Example PSD layers have identical decompressed scanlines, dimensions and ICC metadata versus v1.4.3, and all PNG CRCs validate.

Measured Linux .NET 10 process peak RSS for all-layer PSD extraction: v1.4.3 3,619,140 KiB; v1.4.4 192,236 KiB (94.7% lower). Full v1.4.4 recovery with the supplied PSD: 513,848 KiB. These are test measurements, not Windows memory guarantees. The user's exact Windows OutOfMemoryException was not reproduced; the high-allocation paths were eliminated and failure reports now include stage and stack trace. Windows GUI and Cubism visual testing remain for the user.

# MocRecovery 1.4.3

ArtMesh-derived naming changes only deformer display labels. Example: 14 labels derived, 23 ambiguous labels retained; all 37 IDs unchanged. Warp21 displays as tail. Tests verify unchanged non-name XML data and existing recovery checks.

# MocRecovery 1.4.2

All 37 stored Example deformer IDs are now preserved as IDs and display labels. Descriptive UTF-8 and XML-special-character identifiers were also tested. Existing SDK-target and recovery regression checks passed. Friendly names absent from the compiled file remain unavailable.

# MocRecovery 1.4.1

Example MOC3 header version 4 maps to SDK/Cubism 4.2. The recovered targetVersionNo is 4020000, matching the original reference. Header mappings and rejection of unknown versions were checked, and the recovery regression suite passed. No original project is required at runtime. Windows dropdown display remains to be visually confirmed.

# MocRecovery 1.4.0

Optional PSD import adds source images without changing atlas rig links. Example PSD: 35 raster layers, 29 unique name candidates, two ambiguous fronthair names and four unmatched layer names. Exact raw pixel equality against an independent PSD reader; embedded ICC profile preserved. Existing rig validation remains at 38 meshes, 315 keyforms and 399 local samples. Windows/Cubism source-image display still requires user testing.

# MocRecovery 1.3.0

Example: 6 expression snapshots, 38 meshes, 315 mesh keyforms, 399 local interpolation samples, zero checked-data mismatches, 9 winding warnings requiring visual review. Motion and pose conversion were tested with synthetic fixtures because this sample pack does not supply them. See README for scope and limits.

# MocRecovery 1.2.0

Optional inference sets the editor combined flag for adjacent parameters with shared two-axis bindings. Example produces 12 pairs (11 reference pairs plus Body X/Y). Inference is reported explicitly; it does not recover original UI choices exactly. Tests verify the disabled option, unchanged runtime JSON, native physics/folders, output paths and failed-conversion protection. Windows/Cubism visual validation was not performed.

# MocRecovery 1.1.0

Reference: Example(1).cmo3 (original). Baseline: Example(2).cmo3 (recovered).
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
