# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a Unity 3D top-down tactics game inspired by Valorant. It is built with the **Universal Render Pipeline (URP)** and uses the **New Input System**. The project namespace is `Tactics`.

## Assembly Definitions

| File | Assembly | Purpose |
|---|---|---|
| `Assets/Scripts/Tactics.asmdef` | `Tactics` | All runtime game code |
| `Assets/Tests/EditMode/Tactics.Tests.EditMode.asmdef` | `Tactics.Tests.EditMode` | NUnit edit-mode tests |
| `Assets/Editor/Tactics.Editor.asmdef` | `Tactics.Editor` | Editor-only utilities |

## Running Tests

Tests run through Unity's Test Runner (Window → General → Test Runner). The edit-mode tests in `Assets/Tests/EditMode/` use NUnit and do not require entering Play Mode — they test pure-logic classes like `VisionEvaluator` directly.

There are no CLI build or lint commands; all compilation happens inside the Unity Editor.

## Architecture

### Game Loop (`GameManager`)
`Assets/Scripts/Core/GameManager.cs` is a singleton that drives the match lifecycle via a coroutine: **BuyPhase → RoundActive → RoundEnd**, then loops. It fires `OnStateChanged` and `OnTimerUpdated` events that other systems subscribe to. `EconomyManager` is informed at round end.

### Player
- `PlayerController` (`Tactics.Player`) — movement (run/walk/crouch via `CharacterController`), mouse-aim rotation, and centering-camera logic. Exposes `VisionOrigin`, `LookTarget`, and `AimGroundPoint` for downstream systems.
- `TopDownCamera` (`Tactics.Camera`) — orthographic-style top-down camera locked at 90° X rotation. Follows player + mouse midpoint. `QuickAlign` coroutine snaps to a given Y-rotation.

### Vision System
The most complex subsystem. Two parallel layers cooperate:

**CPU layer (entity visibility)**
- `VisionEvaluator` — stateless static methods: cone tests, LOS raycasts, capsule sample-point collection. No Unity scene state; fully testable.
- `VisionConfig` — plain value struct passed to all evaluator calls.
- `EntityVisibilityDriver` — iterates every registered `VisibleEntity` each `LateUpdate` and calls `VisionController.IsEnemyVisibleAt`; toggles their renderers.
- `VisibleEntity` — self-registers in a static list; holds `FeetPosition` and optional capsule overrides.

**GPU layer (fog of war)**
- `VisionController` — owns a hidden perspective "eye camera" placed at the player's head. Each frame it renders depth into `_VisionEyeDepth` (R32F). Sets all `VisionShaderGlobals` for shader consumption. Temporarily disables the player's own renderers during the eye-camera pass to avoid self-occlusion.
- `VisionFogRendererFeature` (URP `ScriptableRendererFeature`) — enqueues `VisionEyeDepthCapturePass` on the eye camera and `VisionFogCompositePass` on all other game cameras.
- `VisionEyeDepthCapturePass` — copies the eye camera's depth buffer into the persistent R32F texture.
- `VisionFogCompositePass` — fullscreen blit pass.
- `VisionFogComposite.shader` — reconstructs world-space positions from the scene depth buffer and for each pixel tests four sample heights (head/chest/knees/feet) against the eye depth map. Pixels not visible are darkened by `_FogStrength`.

**Layer indices** (`VisionLayerMasks`): Wall=6, Ground=7, Dynamic=9. Props are split into Wall (sides) + Ground (top face) child colliders so players can aim onto prop surfaces.

### Combat
- `Health` — takes damage with shield-first absorption; fires `OnHealthChanged`, `OnShieldChanged`, `OnDeath`.
- `WeaponController` — raycast shooting with wall-penetration thickness check (max 1 m). Damage zones are proximity-based on the XZ plane: head (<0.5m), body (<0.75m), leg (<1m). Uses `WeaponData` ScriptableObject for stats.

### Economy (`EconomyManager`)
Valorant-style cred system: round-win (3000), loss-streak bonuses (1900/2400/2900), kill reward (200), plant reward (300). Halftime resets to 800. Fires `OnCredsChanged`.

### Objectives
- `Spike` / `SpikeSite` / `SpikeController` — plant (4 s hold) and defuse (7 s hold) interactions gated on `GameState.RoundActive` and trigger-volume proximity.
- **Plant site geometry is two separate meshes per site**, not one: a flat visual/walkable mesh (cut out of Ground, `PlantSite.mat`, non-convex non-trigger `MeshCollider` for standing on) plus a child object holding a separately modeled *solid* trigger mesh (`SpikeSite`'s `MeshCollider`, Convex + Is Trigger, no renderer). The trigger mesh is a Blender duplicate of the footprint with a Solidify modifier (2 m thickness, raised 1 m) so it has real vertical volume spanning the player's 2 m `CharacterController` height. A convex-hull trigger built directly from the flat footprint mesh doesn't work — a zero-thickness mesh's convex hull is itself degenerate, so a `CharacterController` resting on top of it never truly overlaps it and `OnTriggerEnter` won't fire reliably.

### Sound
`SoundEmitter` tracks last move/shoot emit timestamps and exposes `IsCurrentlySounding` for the `SoundVisualizer` to draw radius circles. No audio clips — it's a pure gameplay-signal system.

## Key Conventions

- Singletons use `Instance` with `Awake` guard (`if (Instance == null) Instance = this; else Destroy(gameObject)`).
- `VisionController.Active` is a static reference to the currently-active controller; the renderer feature accesses it this way.
- Shader property IDs are cached in `VisionShaderGlobals` to avoid per-frame string lookups.
- `[DefaultExecutionOrder(100)]` on `VisionController` and `[DefaultExecutionOrder(101)]` on `EntityVisibilityDriver` ensure vision data is ready before entity visibility is evaluated.
- The eye camera is created at runtime (`HideFlags.DontSave`) and parented to the player; it must not appear in saved scenes.
