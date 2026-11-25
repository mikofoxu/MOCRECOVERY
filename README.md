# MOC3 Recovery for Windows — 1.2.0

## Run

Extract the ZIP, then double-click **MocRecovery.exe**. Windows 10/11 x64.
No Python, Rust, Visual Studio, or separate .NET installation is needed to run the bundled app.

1. Click **Choose .moc3…** and select the compiled model.
2. Leave its companion files beside it, with the original folder structure. The matching `.model3.json` supplies texture paths and ordering automatically.
3. If necessary, click **Choose textures…**, select atlas PNGs, and use Move up/down to set texture indices 0, 1, 2, etc. Missing texture pages use transparent placeholders.
4. Leave **Rebuild linked parameter grids (inferred)** checked to combine likely paired sliders, or uncheck it for separate sliders. Click **Create .cmo3**.
5. Read the report in the window. **Open folder** opens the output location.

For `A.moc3`, the app writes these files in the folder containing `MocRecovery.exe`:

- `A.cmo3` — reconstructed editable model.
- `A.recovery.txt` — the recovery/missing-data report shown in the window.
- `A.recovery.zip` — original MOC3, supplied textures, discovered companion files and a JSON dump of the recovered rig.

The app asks before replacing existing output files. If approved, prior outputs are retained as timestamped `.bak` files. Conversion is staged before committing outputs; a failed engine run leaves previous outputs untouched. The input model and companions are never changed.

Extract RAR/ZIP model packs before choosing the MOC3. The app reads files from folders; it does not open RAR archives directly.

## Preservation and limits

The rig reconstruction includes supported geometry, keyforms, parameter binding grids, clipping, UVs, hierarchy, parts, deformers and glue data recovered by the backend. Available CDI parameter and part display names are applied, along with parameter groups. Runtime texture pixels are embedded in the new CMO3.

Physics setups are now rebuilt as native CPhysicsSettingsSource objects from the matching physics3.json. Parameter-folder hierarchy and memberships are rebuilt from cdi3.json. The imported field layout was checked against the supplied original project. Expressions, motions, pose, user data and VTube Studio settings are retained as original companion files in the recovery ZIP; their native Editor setup or animation scenes are not recreated. Referenced sound files are included when found. Unreferenced `.exp3.json` files in the input folder are also preserved; shared-model folders can include unrelated expressions.

Original PSD layers, omitted/unused meshes, original GUIDs, deformer names, editor-only controls, comments, and history cannot be fully recovered from compiled data. Unknown missing mesh names/counts cannot be identified without an original CMO3. This app does not compare against an original CMO3, even when an existing output has that name. You must keep and compare that original separately.

The report distinguishes missing referenced files, missing texture pages, companion types not provided (which may never have existed), preserved-but-not-imported settings, and information that cannot be inferred. It cannot exhaustively discover data that compilation discarded. The output is a reconstruction, not the exact original project.

## Implementation

C# WinForms/.NET 10 provides the window, file discovery, texture ordering, reports, backups, transactional output staging and preservation ZIP. A Windows x64 Rust engine is embedded into the EXE and extracted to a temporary folder during conversion. It uses the MIT-licensed `moc2cmo` backend, with a small main-XML setter added to support restoring display names. No Python is used by this Windows app. No network connection is used during conversion.

Source code is included under `source`. The program does not include the user's model/artwork.

## Validation

- Windows x64 EXE published self-contained from Linux.
- Windows engine PE imports inspected: Windows system DLLs only.
- Shared C# workflow exercised end-to-end on Linux using the same Rust engine source with the RedFoxGirl export.
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

The C# test console takes a folder containing the RedFoxGirl runtime export, the native engine path and an EMPTY test output directory. The test program includes a Linux-specific failing-process fixture (`/bin/false`); replace it with a suitable failing executable to run that check on Windows.

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

Keep the original runtime companion files alongside the MOC3. The new importer does not copy rig data from an original CMO3 and requires no original project at runtime. No RedFoxGirl IDs or settings are hard-coded into the importer.

See COMPARISON.md for remaining differences. Neither Windows GUI execution nor a Cubism Editor open/re-export test has been performed in this environment. Numeric/static matches do not establish complete animated equivalence. Deformer keyform coordinates, interpolation behavior, editor extensions and layer layouts have not all been proven equivalent.

## Version 1.1.1 output location

All generated files are saved beside MocRecovery.exe, using the input basename. For example, with the app in D:\data\MocRecovery and a model elsewhere, A.moc3 produces D:\data\MocRecovery\A.cmo3, A.recovery.txt and A.recovery.zip. The Open folder button opens this output directory. Texture and companion discovery still uses the input model directory. The application directory must be writable. Existing output replacement still requires confirmation and retains timestamped backups.

## Version 1.2.0 — linked parameter controls

Adds an optional, enabled-by-default reconstruction of two-slider grids. Only adjacent parameters in the same group with a shared two-axis keyform binding are linked. The report lists every inferred pair. Original editor linking choices are not preserved in MOC3, so links can differ from the original. No original CMO3 is read by this feature.

For RedFoxGirl, 12 pairs are inferred: the 11 identified in the reference project plus Body X/Y. Disable the checkbox to leave individual sliders. Recovery tests pass with the option on and off, including unchanged runtime geometry/bindings and output placement beside the executable. The Windows executable cross-build succeeds; visual behavior in Windows/Cubism still needs confirmation.
