# Audience prefabs

Six reusable audience appearances captured from the tutorial scene:
`Aud_M_01`, `Aud_M_02`, `Aud_M_03`, `Aud_W_01`, `Aud_W_02`, `Aud_W_03`.

Drag an individual `.prefab` into the target scene and adjust its position and rotation.
The prefab root is at the origin with an identity rotation; the source model's size,
internal pose and current material assignments are retained. Chairs are not included.

These are the tutorial's current posed models, not newly configured animated agents.
No tutorial camera, UI, scene manager or baked-lightmap references are carried over.
Shared materials remain shared: edit a material to update all users of that material,
or duplicate it first for a scene-specific appearance.

Original tutorial audience objects and seating are left unchanged.
