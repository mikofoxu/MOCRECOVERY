# MocRecovery 1.8

- Added “Guess deformer names from ArtMeshes (experimental)”, enabled by default. A placeholder deformer ID can take a unique mesh label when it directly controls artwork and all controlled meshes agree. Shared candidates, existing labels and descriptive IDs are protected. Clearing the option keeps the stored labels. See Output.recovery.txt for individual decisions.

- Recover editable rotation deformer, part and glue blend shapes from Cubism 5.0-compatible runtime exports. Rotation targets restore origin, angle, scale, opacity and available color offsets. Part targets restore draw order and the exported draw-order group flag. Glue targets restore keyed intensity while preserving the existing mesh bindings and vertex weights.
- Restore exported weight constraints for these targets. Output.recovery.txt lists editable target counts separately for warp deformers, ArtMeshes, parts, rotation deformers and glue.
- Verified the sample model’s ten rotation keys and one part key against its editor project. A synthetic glue target passed end-to-end conversion. The included BlendShapeTests exercise all five target types in little- and big-endian tables. Visual playback in Cubism Editor remains unverified.
