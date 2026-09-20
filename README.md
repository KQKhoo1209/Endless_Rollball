# Endless Rollball

Endless Rollball is a Unity 6 final-year project built around forward ball
movement, procedural track generation, selectable game modes and themed
environments.

## Requirements

- Unity `6000.4.10f1`
- Git with Git LFS installed

## Getting started

1. Clone the repository.
2. Run `git lfs install` and `git lfs pull`.
3. Open the repository root as a project in Unity Hub.
4. Open `Assets/Scenes/MainMenuScene.unity` and enter Play Mode.

The active build scene list contains `MainMenuScene` followed by
`GameplayScene`.

## Game configuration

- Standard and Fast-Paced modes require Easy, Normal or Hard difficulty.
- Endless mode uses its dedicated dynamic difficulty profile.

## Controls

| Action | Keyboard | Gamepad |
| --- | --- | --- |
| Steer | `A` / `D` or Left / Right Arrow | Left stick |
| Jump | Space | South button |

## Project structure

- `Assets/Scripts` — runtime, editor and gameplay systems
- `Assets/EndlessRollball` — track, environment and UI assets
- `Assets/Tests` — Edit Mode and Play Mode tests
- `ArtSource` — source-generation and environment audit utilities

Generated screenshots, reports, caches and local assistant state are excluded
through `.gitignore`. Large binary art files are managed through Git LFS.

## Tests

Use Unity Test Runner to run both the Edit Mode and Play Mode suites. The Play
Mode tests load `GameplayScene`, so the committed build-scene configuration is
required.

## Third-party assets

Third-party asset packs remain subject to their respective licences. Verify
redistribution permission before publishing or mirroring downloaded models,
textures, skyboxes or UI packs. The Kenney UI pack includes its own licence in
its asset folder.
