# MocRecovery 1.7.3

- Recover editable warp and ArtMesh blend targets by runtime object, parameter and key. Objects with several ordinary keyforms use the grid key closest to the ordinary parameter defaults. An exact tie selects the first key on that axis.
- Restore geometry and opacity offsets, ArtMesh draw order and Cubism 5 per-key multiply and screen color offsets. Rebuild native weight limits with their controlling parameters, breakpoint values and weights.
- Report separate warp, ArtMesh and weight breakpoint totals. Rotation deformer, part and glue blend targets are still skipped.
- Include standalone synthetic fixtures in source/BlendShapeTests for an unconstrained warp and a constrained ArtMesh, covering different coordinate spaces, multiple ordinary forms and both byte orders.
