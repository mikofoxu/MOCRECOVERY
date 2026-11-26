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
