# SpaceTaxi — project guide for AI agents

2D space-taxi game: ship + planet gravity fields, trajectory preview, landing, heat, hull, respawn. Alpha v0.0.2.

**Stack:** Unity `6000.6.0f1` · URP 2D · uGUI + TextMeshPro · new Input System (`UnityEngine.InputSystem` only) · C#.

**Editors installed on this machine:** `C:\Program Files\Unity 6000.0.34f1`, `C:\Program Files\Unity 6000.0.38f1`, `C:\Unity\6000.0.65f1`, `C:\Unity\6000.3.8f1`. The project's version (6000.6.0f1) is **not installed** — don't open the project with an older editor on the original folder; compile-check on a copy or via the `unity-cli` skill.

## Map
| Path | What |
|---|---|
| `Assets/Scripts/Core` | `GameSession` (composition root) → `WorldBuilder` |
| `Assets/Scripts/Physics` | `Ship`, `ShipController`, `GravityField`, `LandingSystem`, `HullSystem`, `TrajectoryPredictor`, `CameraFollow`, `PhysicsConfig` |
| `Assets/Scripts/World` | `WorldBuilder`, `CelestialBody`, `Planet`, `Star`, `LevelAsset` |
| `Assets/Scripts/UI` | Map / Pause / Settings screens, `OnboardComputerPanel`, `SceneFlowButtons` |
| `Assets/Scripts/Audio` | `AudioManager`, `AudioCatalog`, `SoundId`, per-system audio |
| `Assets/Editor` | `LevelEditorWindow` + `LevelEditorSceneView` (custom level editor), `AudioSetupTool`, `StubSpriteBaker` |
| `Assets/Scenes` | `GameScene.unity`, `MainMenu.unity` |
| `Assets/Prefabs` | `Ship.prefab`, `Planet.prefab`, `UI/SettingsUI.prefab` |
| `Assets/ScriptableObjects` | Levels `TestLevel01`, `BigMap01`; `PhysicsConfig_Default`; `AudioCatalog` |

## Conventions
- Namespaces `SpaceTaxi.{Core,Physics,World,UI,Audio}`; `[SerializeField] private` fields + read-only public properties; no public fields.
- English identifiers and comments; small single-responsibility files.
- Physics in `FixedUpdate`; symmetric event unsubscription in `OnDestroy`/`OnDisable`; null-check `GetComponent` results (prefab `Awake` ordering bit us before — see git log).
- Levels are `LevelAsset` ScriptableObjects; new serialized fields must also be exposed in `LevelEditorWindow`/`LevelEditorSceneView`.
- Recent owner decisions live in git log (e.g. "Gravity x2" — don't silently revert).

## Feature pipeline (subagents in `.zcode/agents/`)
`unity-analyst` (plan, read-only) → `unity-scripter` (C#) and/or `unity-ui-builder` (uGUI scenes/prefabs) → `unity-qa` (compile + diff review + smoke build gate).

## Hard-won rules (incidents)
- **Never `git switch`/`checkout` while a Unity Editor is open on this project.** A branch switch under a live editor leaves the open scene's in-memory object graph stale: components hot-relink, some serialized refs silently become null (2026-11-09: `GameSession.passengerCatalog`/`currentShip` → orders stopped spawning; fixed by reloading the scene from disk). After ANY branch switch: ask the user to close the editor first, or reload the open scene and verify wiring (e.g. via `unity-cli` `script eval` on `SerializedObject` fields) before playing.
- Editor + CLI: project has `com.unity.pipeline` (old command names; `unity pipeline upgrade` suggested). Live-state checks beat log-guessing: `unity command script eval --caller plugin --skill unity-cli '<C# with return>'`.
- **Known corrupt asset in the branch:** `Assets/Prefabs/Resources/UI/Speedometer.prefab` fails YAML parse ("Parser Failure at line 7/58", serializedVersion 1 < minimum 6) → HUD speedometer throws at `HudBinder.cs:92`. Needs rebuild or restore from history.
