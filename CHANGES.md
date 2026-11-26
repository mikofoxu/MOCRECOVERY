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
