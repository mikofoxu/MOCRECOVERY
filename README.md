# MOC3 Recovery for Windows — 1.93

Version 1.93 updates the Texture accuracy tab with aligned columns and more row spacing. See [CHANGES.md](CHANGES.md).

Version 1.91 preserves each warp deformer’s transform mode. See [CHANGES.md](CHANGES.md).

Version 1.9 includes editor metadata for recovered physics. See [CHANGES.md](CHANGES.md).

Version 1.8.6 adds indexed PNG atlas support for mesh crops and PSD matching. See [CHANGES.md](CHANGES.md).

Version 1.8.5 groups recovery options into two columns and adds a PSD list and scrollable report with copying. See [CHANGES.md](CHANGES.md).

Version 1.8.4 aligns the file and folder buttons on the left and displays their paths on the right. See [CHANGES.md](CHANGES.md).

Version 1.8.3 fixes the Cancel button layout and enables experimental texture relinking by default when a PSD is attached. See [CHANGES.md](CHANGES.md).

Version 1.8.2 updates physics group recovery. See [CHANGES.md](CHANGES.md).

Version 1.8.1 adds sequential import of multiple PSD files. See [CHANGES.md](CHANGES.md).

Read `WARNING.txt` for responsible-use guidance, the misuse disclaimer, and the scope of the Live2D-code audit.

## Runtime artwork and blend shapes in 1.7.0

Conversion reads the selected MOC3, its runtime companions and any selected PSD. The original editor project is never an input. Each mesh with a supported atlas page gets a cropped, triangle-masked source image unless it already has a verified PSD source link. The recovery ZIP includes individual PNGs and their mesh IDs and atlas bounds under `editor-import/atlas-artwork/`. Crops follow the atlas orientation. PNG extraction currently supports non-interlaced indexed (1/2/4/8-bit), RGB (8-bit) and RGBA (8-bit) pages. Missing or unsupported pages are reported.

Version 1.8 recovers editable warp and ArtMesh blend shapes and their native weight constraints. For Cubism 5.0-compatible runtime exports, it also restores rotation deformer, part and glue blend targets. Output.recovery.txt lists counts for each type. See [CHANGES.md](CHANGES.md) for details.

## Run

**Choose output folder…** sets the destination for the project, report and recovery ZIP for this session. The path beside the button shows the current output folder. **Cancel** stops an active conversion at a cancellation checkpoint and discards temporary output. Once final saving begins, the three files finish saving together.

Extract the ZIP, then double-click **MocRecovery.exe**. Windows 10/11 x64.
No Python, Rust, Visual Studio, or separate .NET installation is needed to run the bundled app.

1. Click **Choose .moc3…** and select the compiled model.
2. Leave its companion files beside it, with the original folder structure. The matching `.model3.json` supplies texture paths and ordering automatically.
3. If necessary, click **Choose textures…**, select atlas PNGs, and use Move up/down to set texture indices 0, 1, 2, etc. Missing texture pages use transparent placeholders.
4. Select multiple PSDs together or add more from another folder. They are processed one at a time. For multiple files, look under numbered folders in `editor-import/psd-source/` and `original/source-artwork/`. Optionally click **Add PSD files…** after selecting the model. Leave it unset if unavailable; **Clear PSDs** clears the selection. Selecting a PSD enables and checks **Try preserving PSD layer names using artwork matching (experimental)** by default. Uncheck it to use the previous name-only behavior. The separate **Attempt PSD texture relinking (experimental)** checkbox turns on with artwork matching whenever you add a new PSD. To skip relinking for this conversion, uncheck it after adding your PSDs. Then leave **Rebuild linked parameter grids (inferred)** checked to combine likely paired sliders, or uncheck it for separate sliders. Click **Create .cmo3**.
5. Read the report in the window. **Open folder** opens the output location.
6. Extract `A.recovery.zip` and read `editor-import/README.txt`. Import its motions onto the recovered model’s animation track in Cubism.

For `A.moc3`, the app writes these files in the selected output folder (the app folder by default):

- `A.cmo3` — reconstructed editable model.
- `Output.recovery.txt` — the recovery/missing-data report shown in the window.
- `A.recovery.zip` — original MOC3, supplied textures, discovered companion files and a JSON dump of the recovered rig.

The app asks before replacing existing output files. If approved, prior outputs are retained as timestamped `.bak` files. Conversion is staged before committing outputs; a failed engine run leaves previous outputs untouched. The input model and companions are never changed.

Extract RAR/ZIP model packs before choosing the MOC3. The app reads files from folders; it does not open RAR archives directly.

## Preservation and limits

The rig reconstruction includes supported geometry, keyforms, parameter binding grids, clipping, UVs, hierarchy, parts, deformers and glue data recovered by the backend. Available CDI parameter and part display names are applied, along with parameter groups. Runtime texture pixels are embedded in the new CMO3.

Physics setups are now rebuilt as native CPhysicsSettingsSource objects from the matching physics3.json. Parameter-folder hierarchy and memberships are rebuilt from cdi3.json. The imported field layout was checked against the supplied original project. Original expression, motion, pose, user data and VTube Studio files remain in the recovery ZIP. Version 1.3.0 additionally prepares importable expression snapshots, pose visibility previews and existing motions under editor-import/. Native expression/pose controllers and CAN3 scenes are not generated. Referenced sound files are included when found. Unreferenced `.exp3.json` files in the input folder are also preserved; shared-model folders can include unrelated expressions.

Original PSD layers, omitted/unused meshes, original GUIDs, deformer names, editor-only controls, comments, and history cannot be fully recovered from compiled data. Unknown missing mesh names/counts cannot be identified without an original CMO3. This app does not compare against an original CMO3, even when an existing output has that name. You must keep and compare that original separately.

The report distinguishes missing referenced files, missing texture pages, companion types not provided (which may never have existed), preserved-but-not-imported settings, and information that cannot be inferred. It cannot exhaustively discover data that compilation discarded. The output is a reconstruction, not the exact original project.

## Implementation

C# WinForms/.NET 10 provides the window, file discovery, texture ordering, reports, backups, transactional output staging and preservation ZIP. A Windows x64 Rust engine is embedded into the EXE and extracted to a temporary folder during conversion. It uses the MIT-licensed `moc2cmo` backend, with a small main-XML setter added to support restoring display names. No Python is used by this Windows app. No network connection is used during conversion.

Source code is included under `source`. The program does not include the user's model/artwork.

## Validation

- Windows x64 EXE published self-contained from Linux.
- Windows engine PE imports inspected: Windows system DLLs only.
- Shared C# workflow exercised end-to-end on Linux using the same Rust engine source with the Example export.
- Confirmed 49 parameters, 37 deformers, 38 meshes and 50 CDI parameter/part names.
- Original/recovered comparison: all 38 mesh triangle arrays and 315 position arrays match by parameter key combination.
- Version 1.1.0 adds 12 native physics setups (26 inputs, 12 outputs, 44 particles), plus the expression parameter folder and root group. All 626 compared physics fields match the original within 0.00001; maximum observed rounding difference: 0.000002. Folder names and memberships match.
- Independent CAFF/XML readback confirmed no unresolved references and unchanged recovered rig JSON compared with the earlier converter.
- Tested same-basename output, texture lookup, preservation ZIP contents, refused overwrite, successful replacement with backup, engine-failure protection, missing textures, multi-dot filenames, invalid input and companion path traversal.

The Windows GUI/EXE has NOT been launched on Windows here. The generated CMO3 has NOT been opened or animated in Cubism Editor here. Those compatibility checks remain necessary.

## Build from source on Windows

Requires .NET 10 SDK, Rust stable (MSVC target), and Visual Studio C++ Build Tools.

```powershell
cd source
cargo build --release --locked --manifest-path engine/Cargo.toml
Copy-Item engine/target/release/moc-recover.exe engine/moc-recover.exe -Force
dotnet publish Gui/MocRecovery.csproj -c Release -r win-x64 --self-contained true -o ../publish
```

Add each release at the top of [CHANGES.md](CHANGES.md). Publishing checks the first version heading and includes this file beside the executable.

The C# test console takes a folder containing the Example runtime export, the native engine path and an EMPTY test output directory. The test program includes a Linux-specific failing-process fixture (`/bin/false`); replace it with a suitable failing executable to run that check on Windows.

## Attribution

Backend: https://github.com/Eatgrapes/moc2cmo
Base revision: `2527e24e9331966fc78516b38141e4e041a13952` (1.1.20).
Its MIT license is included with the source. Modification: `Cmo3Project.set_main_xml`.
The wrapper and C# UI source are provided under the MIT license in `source/LICENSE`.
The .NET runtime and third-party dependencies retain their own licenses. See the included notices and Cargo.lock.

## Version 1.1.0 changes (retained in 1.1.1)

- Added a C# CAFF archive reader/writer to enrich the generated project.
- Rebuilt native physics sources, parameter GUID links, normalized ranges, particle chains, weights, output scales, reflection flags, setting names, and FPS from the runtime physics JSON.
- Rebuilt native parameter folders, root group, parent relationships and parameter membership from CDI. Folder colors/open state use defaults because CDI does not preserve them.
- Added XML reference validation and report counts for native imports. Invalid physics setups are skipped individually with an explicit warning; invalid group hierarchies are reported.
- Preserved the 1.0.1 spacing/DPI fix and same-basename output behavior.

Keep the original runtime companion files alongside the MOC3. The new importer does not copy rig data from an original CMO3 and requires no original project at runtime. No Example IDs or settings are hard-coded into the importer.

See [CHANGES.md](CHANGES.md) for remaining differences. Neither Windows GUI execution nor a Cubism Editor open/re-export test has been performed in this environment. Numeric/static matches do not establish complete animated equivalence. Deformer keyform coordinates, interpolation behavior, editor extensions and layer layouts have not all been proven equivalent.

## Version 1.1.1 output location

By default, all generated files are saved beside MocRecovery.exe. For example, with the app in D:\data\MocRecovery and a model elsewhere, A.moc3 produces D:\data\MocRecovery\A.cmo3, Output.recovery.txt and A.recovery.zip. The Open folder button opens this output directory. Texture and companion discovery still uses the input model directory. The selected output directory must be writable. Existing output replacement still requires confirmation and retains timestamped backups.

## Version 1.2.0 — linked parameter controls

Adds an optional, enabled-by-default reconstruction of two-slider grids. Only adjacent parameters in the same group with a shared two-axis keyform binding are linked. The report lists every inferred pair. Original editor linking choices are not preserved in MOC3, so links can differ from the original. No original CMO3 is read by this feature.

For Example, 12 pairs are inferred: the 11 identified in the reference project plus Body X/Y. Disable the checkbox to leave individual sliders. Recovery tests pass with the option on and off, including unchanged runtime geometry/bindings and output placement beside the executable. The Windows executable cross-build succeeds; visual behavior in Windows/Cubism still needs confirmation.

## Version 1.3.0 — companion import and validation

Priority 1: Companion files
- Expressions: generates a one-second motion snapshot with all parameters initially at MOC3 defaults. Add, Multiply and Overwrite are applied at full weight and clamped to parameter ranges. Unknown parameters and clamping are reported. This does not reproduce expression fades, stacking, or an animated base pose.
- Motions: checks segment structure (linear, Bezier, stepped and inverse-stepped), metadata counts, time order and parameter/part targets, then includes the original bytes as an import-ready file. Unsupported targets are reported; events, sounds and model effects are not baked.
- Poses: generates one part-opacity motion preview per group/choice, including linked parts. Automatic switching and fades remain runtime-only.
- Read editor-import/index.json for each file’s source and meaning. Malformed companion data is reported while original files remain preserved.

Priority 2: Validation
- Checks all mesh keyform positions, opacity, rounded draw order, UVs, triangles, clipping references/inversion, culling, binding axes/keys and keyform-grid mappings against emitted CMO3 data.
- Compares sampled local tensor interpolation at bound keys, midpoints, parameter limits and defaults. Reports triangle winding reversals for visual review; these can be intentional.
- Checks finite physics fields, normalization ranges and output-particle indices.
- A checked-data mismatch stops conversion before replacing old outputs.
- Exports validation.json and a parameter-sweep.motion3.json (four seconds per parameter, one parameter at a time with the others at defaults). Import the sweep on a separate animation scene to review the rig.

This is not an independent Cubism Core renderer or physics simulator. World-space deformer behavior, rendered clipping/compositing, dynamic physics and all parameter combinations are not verified. Windows/Cubism visual import testing remains outstanding. No original CMO3 is needed. Artwork extraction is deferred.

Validation evidence: Example produced six expression snapshots; no motion/pose files were supplied for that model. Checked 38 meshes, 315 mesh keyforms and 399 local samples without data mismatches. Nine mesh winding warnings were reported for review, not treated as proven defects. Synthetic fixtures test all expression blend modes, all four motion segment types, linked pose choices, malformed motion handling, and detection of altered clipping/draw order. Existing recovery/output-folder regression checks also passed.

Official motion import instructions: https://docs.live2d.com/en/cubism-editor-manual/export-moc3-motion3-files/

## Version 1.4.0 — optional PSD source artwork

PSD is optional. Without it, the recovery workflow remains available using the MOC3, textures and companion files. Select the MOC3 first, then use Add PSD files. Selecting another MOC3 clears the PSD selection to avoid accidental cross-model imports.

With a supported PSD, the CMO3 receives a separate **PSD source artwork** image group. Available layers retain original names, offsets, visibility, opacity and source-folder membership. Cropped raw raster PNGs and a layer/matching inventory are also saved under `editor-import/psd-source/` in the recovery ZIP; the original PSD is preserved under `original/source-artwork/`. PNGs retain raw alpha; opacity and visibility remain separate metadata. The embedded ICC profile is copied into PNGs when present.

Matching is conservative: unique name candidates restore mesh display labels only. Duplicate or absent names are reported. Existing rig texture links remain on the recovered atlas; experimental source-image relinking is available separately in version 1.6.0, but does not add missing meshes or restore missing keyforms. Source folder hierarchy is not substituted for the rig part/deformer hierarchy.

### What the PSD cannot restore

A PSD supplies artwork and layer metadata. It does **not** supply Live2D deformer names or IDs, mesh vertices/triangles, parameter bindings, deformation keyforms, or rigging. Importing a PSD therefore cannot restore those items when they are missing from the compiled model and companion files. For example, a mouth layer can restore the mouth artwork, but not an omitted mouth mesh or its animation setup.

Deformer IDs and friendly display names are different. Our Example investigation found original IDs such as `Warp29` and `Rotation` in the MOC3, but did not find the friendly labels `wholebody` and `HeadRotation`. Version 1.4.3 preserves stored deformer IDs; display names are derived from controlled ArtMeshes when unambiguous. This does not restore separate friendly labels absent from the MOC3. The PSD does not fill this gap. Any names inferred from artwork or affected parameters would be suggestions, not recovered original deformer names.

Supported: standard 8-bit RGB PSD; raw, PackBits/RLE, ZIP and ZIP prediction channel compression. PSB, 16-/32-bit and other color modes are not supported. Raster layer masks, vector masks, effects and non-normal blend appearance are not reconstructed; warnings identify detected cases. Smart objects use their stored raster previews. Original PSD bytes preserve those features for manual work. Very large layers may require substantial memory; individual layers above 180 million pixels are reported as unsupported. If a selected PSD is invalid, conversion reports the error; clear the PSD selection to run without it.

Example verification: 35 raster layers imported; raw RGBA matched an independent PSD reader on all layers. ICC profile preservation checked. There were 29 unique name candidates, two ambiguous fronthair layers, and four unmatched names (Layer 1, Layer 4, mouth, mad sign). These are name candidates, not pixel-verified mesh associations. All 38 rig meshes, 315 keyforms and 399 local interpolation samples continued to pass validation. No-PSD regression tests passed. Synthetic PSD tests cover nested groups, Unicode names, negative offsets, raw and ZIP prediction channels.

The native source-image group still needs visual confirmation in Windows/Cubism. This is the main item to test before relying on the imported source-artwork workflow.

Optional PSD tests: `dotnet run --project source/PsdTests/Psd.Tests.csproj -c Release` runs the small synthetic fixture. Supply PSD, MOC3, platform-appropriate engine and an empty output directory as four arguments to exercise a full import.

## Version 1.4.1 — detect SDK compatibility target

The recovered project target is now selected from the MOC3 header instead of retaining the backend’s fixed 3.0 setting. Supported header mappings are Cubism 3.0, 3.3, 4.0, 4.2, 5.0 and 5.3. Unknown versions are rejected rather than guessed. The detected target is listed in the recovery report.

Example’s header identifies 4.2; its recovered targetVersionNo is now 4020000, matching the reference project. No original CMO3 is needed during conversion. This identifies compiled-format compatibility, not the original Editor build, and does not guarantee full recovery of every version-specific feature. The Windows/Cubism dropdown still needs visual confirmation.

## Version 1.4.2 — preserve stored deformer IDs

Reads the MOC3 deformer ID table and preserves those IDs as both deformer IDs and display names. Descriptive stored IDs remain descriptive; generic stored IDs such as Warp29 remain unchanged instead of being renumbered. No original CMO3 or PSD is required. This does not infer missing friendly names.

The mapping uses the backend’s explicit deformer indices, not name similarity or hierarchy guesses. Invalid, empty, duplicate or unsupported ID data is reported and generated names are retained. Both endianness flags are handled. Example’s 37 IDs and a descriptive UTF-8/special-character test passed, along with the recovery regression suite. Windows/Cubism visual confirmation remains outstanding.

## Version 1.4.3 — ArtMesh-based deformer display names

Stored deformer IDs remain unchanged. A deformer controlling ArtMeshes with one distinct display name receives that name, including through nested child deformers. For example, ID Warp21 remains Warp21 while its display name becomes tail. If controlled meshes have different names, or no named meshes are found, the existing display name is retained. Matching uses exact ArtMesh display names; no arbitrary choice or semantic guessing is made.

This naming pass runs after optional PSD import, so it uses the final ArtMesh labels. It changes only deformer localName fields, not IDs, GUIDs, hierarchy, bindings or geometry. Every change and ambiguous result is listed in the report. These labels are inferred conveniences, not recovered original authoring names. No PSD or original CMO3 is required.

Tests confirm the tail example, all 37 original IDs, unchanged non-name XML data, ambiguous-parent handling and the existing recovery checks. Windows/Cubism display still needs visual confirmation.

## Version 1.4.4 — Lower recovery memory use

PSD channels are decoded one row at a time and PNGs are compressed in chunks, avoiding full-size RGBA and channel buffers. CAFF project reads/writes stream the archive instead of creating additional full-file copies. No downscaling or pixel loss is introduced. PSD remains optional. The executable explicitly targets x64.

If recovery fails, the on-screen report now includes the full exception details; memory errors also identify the recovery stage. Copy that report when reporting an error. Large projects still need memory for their compressed image entries and XML; this is not a fixed memory limit or a guarantee that every project fits.

## Version 1.5.0 — Validation and companion import improvements

Read [CHANGES.md](CHANGES.md) for the detailed changes, tests, output filenames and limits. Validation now covers warp/rotation forms and hierarchy, bounded simultaneous parameter samples and source-to-editor physics fields. Companion exports add isolated expression fade previews, complete pose-group snapshots and filtered editor motion copies. Originals remain preserved. The validation folder also contains a paired-parameter review motion and schedule when applicable. These additions do not establish rendered/runtime equivalence.

## Version 1.5.1 — Optional PSD artwork matching

The new checkbox is checked automatically whenever a PSD is selected and disabled without a PSD. It compares PSD layers with atlas artwork inside recovered mesh UV triangles, considering color, transparency, shape/aspect and quarter-turn rotation. Strong, distinct candidates restore mesh display names and record inferred layer associations. IDs, mesh geometry, original source pixels and atlas texture links are unchanged. Identical/weak candidates are reported without forced assignment. This does not reconnect PSD textures or recover original placement transforms.

See [CHANGES.md](CHANGES.md). Matching details are exported to editor-import/psd-source/artwork-matches.json in the recovery ZIP. The existing layers.json Match field explicitly describes the separate name-only check. PSD layers are still imported when matching is unchecked.

## Version 1.6.0 — Experimental PSD texture-source relinking

The new checkbox attempts to restore source-image references for strong, unambiguous artwork matches. It fits a scale/quarter-turn/translation, checks held-out full-resolution PNG samples, and verifies the existing atlas-to-canvas fit. Accepted source references and optional alignment transforms are written into the CMO3. Active atlas texture inputs, UVs, texture state, rig geometry and IDs remain unchanged, following the atlas-backed structure of the supplied original project.

This is source relinking, not an immediate switch to PSD rendering or an atlas rebuild. A source link passing these sampled checks is not proof of identical Cubism behavior; check placement in Cubism before updating source images or rebuilding the atlas. Original authoring transforms, layer identifiers and future PSD reimport behavior are not guaranteed. Results and skip reasons appear on screen and in editor-import/psd-source/texture-relinks.json. Read [CHANGES.md](CHANGES.md) for details and test scope.

## Version 1.6.1 — Percentage progress

Conversion now shows a filled progress bar with a numeric percentage. It advances by recovery stage, PSD layer, experimental link candidate and archived file. These are weighted stage estimates, not elapsed-time estimates; the percentage can pause while a large item or the backend engine is processing. There is no timer-based simulated progress. It reaches 100% only after output files are successfully installed. A failure retains the last percentage with the error report. Selecting another model clears the previous progress; starting a conversion resets it to 0%.

## Version 1.6.2 — Maximized startup

The application opens maximized by default. Standard title-bar controls remain available to restore, minimize or close the window. Recovery behavior is unchanged.
