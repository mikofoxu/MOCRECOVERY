# MOC3 Recovery 1.5.0 — detailed changes

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

RedFoxGirl without its original CMO3:
- 38 meshes, 315 mesh keyforms, 829 local mesh samples.
- 37 deformers, 206 deformer keyforms, 837 local deformer samples.
- 12 imported physics setups compared with the supplied physics companion.
- No checked-data mismatches; nine mesh winding-review warnings remain.
- Six expression snapshots plus six fade previews; RedFoxGirl does not supply motion/pose fixtures, so those paths also use synthetic tests.
- Regression checks retain stored IDs, ArtMesh-derived names, SDK 4.2 target, output paths, backups and failed-conversion protection.

Deliberate corruption tests detect altered warp points, rotation angle, parent link, deformer grid access and physics scale. Synthetic companion tests cover expression fade start/midpoint/target/return, all motion segment types, unsupported motion targets, retained fade metadata and Unicode events, invalid events, multi-group pose resets, linked parts and rejection of partial pose exports. Generated review/import motions pass the motion validator. Optional PSD integration is tested separately.

## What is still not verified or rebuilt

No rendered clipping/compositing comparison, exact world-space deformer evaluation, physics time simulation, exhaustive parameter-combination coverage, CAN3 scene generation, native expression/pose controllers, or automatic motion model-effect baking is claimed. Windows/Cubism visual import behavior still needs user testing. Originals and unsupported information remain in the recovery ZIP. This release adds no Live2D SDK/Core dependency.
