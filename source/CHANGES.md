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
