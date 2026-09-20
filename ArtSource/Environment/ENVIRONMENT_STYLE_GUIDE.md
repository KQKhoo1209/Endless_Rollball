# Endless Rollball Environment Style Guide

## Visual direction

- Stylized low-poly forms with clear silhouettes and limited material families.
- Route readability takes priority over decorative density, especially for older adults and first-time IMU users.
- Futuristic Street uses dark blue structure, teal surfaces, cyan light, and yellow guidance.
- Water Theme Park uses white structure, aqua and blue water elements, coral landmarks, and yellow guidance.
- Ancient Dungeon uses neutral stone, warm stone accents, dark metal, orange ember light, and yellow guidance.
- The transition tunnel stays neutral blue-grey with consistent cyan directional light.

## Modular contract

- Units: metres.
- Blender authoring: `+Y` forward and `+Z` up.
- Unity import: `+Z` forward.
- Roadside and tunnel modules occupy `z = 0` to `z = 24 m` in Unity.
- Normal roadside decoration stays outside `x = -5.5 m` to `x = 5.5 m`.
- Below-track water and distant backdrops may cross the route footprint when they cannot obstruct the player.
- Overhead visual elements maintain at least `9 m` clear width and `6 m` clear height.
- Environment prefabs are visual-only and never contain colliders, rigidbodies, `TrackSegment`, or obstacle scripts.

## Materials and readability

- Use URP Lit materials with GPU instancing enabled.
- Route-edge guidance remains bright yellow or theme-accented cyan.
- Static hazards remain red; moving hazards remain orange.
- Do not theme hazards with low-contrast stone, water, or background colours.
- Use emission sparingly on route lights, signs, tunnel guides, and flame effects.

## Asset naming

- Futuristic Street: `ENV_FS_*`
- Water Theme Park: `ENV_WP_*`
- Ancient Dungeon: `ENV_AD_*`
- Transition Tunnel: `ENV_TR_*`
- Material assets: `MAT_ENV_*`
- Blender mesh objects use `<material-key>__<object-name>` so Unity can assign project materials deterministically.

## Performance budget

- Use repeated 24 m scenery modules instead of unique full-level meshes.
- Use no more than one material per mesh renderer where practical.
- Large backdrop prefabs provide `LOD0` and `LOD1` renderer groups.
- Ambient particle systems use no collision and a maximum of 160 particles.
- Validate eight active track segments and eight scenery modules at 1920×1080, targeting 60 FPS on the development machine.
