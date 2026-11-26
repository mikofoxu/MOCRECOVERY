# MocRecovery 1.8.6

- Decode non-interlaced indexed PNG atlases at 1, 2, 4 and 8 bits per pixel for ArtMesh crops and PSD artwork matching. Palette alpha values are preserved, including partial transparency. Palette entries without an alpha value are opaque.
- Added regression checks comparing indexed and RGBA atlases through artwork sampling and triangle-masked crop generation.
