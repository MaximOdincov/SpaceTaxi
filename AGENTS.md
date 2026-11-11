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
- **Play mode is started by agents and stopped by agents — nobody else will stop it** (owner asleep/away; other agents don't touch it). ALWAYS run `unity command editor.playmode stop` before finishing a session or handing work over. (2026-11-11, Н5: session started with the editor left in play mode since 22:29 the previous evening.)
- **Creating a TMP_FontAsset via eval**: after `AssetDatabase.CreateAsset(tmp, path)` you MUST `AssetDatabase.AddObjectToAsset(tmp.atlasTexture, tmp)` and `AddObjectToAsset(tmp.material, tmp)`, else the sub-objects are not saved and `m_AtlasTextures` breaks to null after a domain reload (2026-11-11: map open threw `UnassignedReferenceException` in `TryAddCharacterInternal`). Verify via SerializedObject `m_AtlasTextures[0] != null`.
- **Never `git switch`/`checkout` while a Unity Editor is open on this project.** A branch switch under a live editor leaves the open scene's in-memory object graph stale: components hot-relink, some serialized refs silently become null (2026-11-09: `GameSession.passengerCatalog`/`currentShip` → orders stopped spawning; fixed by reloading the scene from disk). After ANY branch switch: ask the user to close the editor first, or reload the open scene and verify wiring (e.g. via `unity-cli` `script eval` on `SerializedObject` fields) before playing.
- **External edits to a committed scene file (including agent commits that changed the .unity) are NOT picked up by the already-open editor** — its in-memory scene stays stale until `EditorSceneManager.OpenScene` reloads it (2026-11-11: `MapScreenBinder.mapFont` was assigned on disk but NULL in the live editor → map rendered the default font). After committing scene changes, reload the scene before runtime checks.
- Editor + CLI: project has `com.unity.pipeline` (old command names; `unity pipeline upgrade` suggested). Live-state checks beat log-guessing: `unity command script eval --caller plugin --skill unity-cli '<C# with return>'`.
- **Known corrupt asset in the branch:** ~~`Assets/Prefabs/Resources/UI/Speedometer.prefab`~~ RESOLVED — the prefab no longer exists; the speedometer is built in code (`SpeedometerGauge.cs`), no YAML parse error, no `HudBinder` throw.
- **CLI eval callbacks don't survive across CLI calls** (Н6): `EditorApplication.update += cb` from `script eval` dies silently when the pipeline relay client disconnects between calls. Deterministic measurements must run inside a SINGLE eval call (manual `Physics2D.Simulate(0.02f)` steps + `Invoke` of private `FixedUpdate` methods via reflection, in game order), or use slow-mo (`Time.timeScale` — but NOT for ParticleSystems with `useUnscaledTime=true`) with CLI polling that resets `EditorApplication.isPaused = false` every poll.
- **Console "Error Pause" catches everything** (Н6): with Error Pause on, ANY console error — including pipeline runner noise — pauses play mode (`isPaused=true`, frozen `Time.time`); `LogEntries.SetErrorPause` is not reachable via reflection in 6000.6.8f1. Guard every poll eval with `if (EditorApplication.isPaused) EditorApplication.isPaused = false;`.
- **`capture_game_view` can return a stale frame while the editor is unfocused** (Н6: three empty captures with 1500+ live particles on screen). For in-game visual acceptance, run `UnityEngine.ScreenCapture.CaptureScreenshot("Temp/…png")` inside an eval (writes relative to the project folder) and read the file — that one is honest.
- **Counting freshly emitted particles**: CLI round-trip latency is ~1.5-2.5s, particle lifetimes are often shorter — read `particleCount` in the SAME eval call that spawns/Emits (Н6: `ShipExplosion.Spawn` then count children in the same call).
