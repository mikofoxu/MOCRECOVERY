# MOC3 Recovery 1.5.1

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
RedFoxGirl: 25 accepted artwork-name matches, 3 ambiguous and 10 insufficiently close. ArtMesh is correctly labeled mad sign. Both fronthair meshes remain ambiguous because their PSD candidate artwork is indistinguishable. No original CMO3 was used by matching.
Unit checks cover pixel decoding, unrelated IDs, identical candidates, unrelated images, transparent layers, quarter-turn rotation, tiny images and default-enabled plans.
Both enabled and disabled full-PSD integration paths and the recovery regression suite are tested. The enabled integration run peaked at 472,400 KiB RSS (about 461 MiB) in the Linux test environment; this is not a Windows memory guarantee.
Windows/Cubism visual testing remains outstanding. No Live2D SDK/Core dependency was added.
