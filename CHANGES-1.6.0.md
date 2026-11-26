# MOC3 Recovery 1.6.0

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

RedFoxGirl test result: 8 source links restored from 25 accepted artwork-name candidates; 30 meshes skipped (13 without a unique artwork match, 15 failing the held-out pixel check, 1 with unsupported layer properties, 1 with excessive atlas-to-canvas fit error).
Restored: ArtMesh (mad sign), depressed, nose, blush, face, pupilright, pupilleft, legleft.
Existing recovery regression tests pass. Enabled/disabled PSD integration tests check actual source-reference targets and unchanged active atlas inputs.
Windows/Cubism visual validation remains outstanding; user testing is necessary before relying on source updates or atlas regeneration. No Live2D SDK/Core dependency was added.
