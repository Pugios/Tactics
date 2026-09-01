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

### Combat (server-authoritative)
- `Health` — `NetworkBehaviour`; server-only mutators (`TakeDamage`, `AddShield`, `Heal`, `ServerRevive`) with shield-first absorption, replicated via `NetworkVariable`s; `OnHealthChanged`/`OnShieldChanged`/`OnDeath` fire on every peer from the replicated values.
- `WeaponController` — owner computes the aim point (world position under the mouse) and sends `ShootServerRpc(aimPoint, weaponId)`; the server re-validates fire cadence, resolves the hit against its own world (wall-penetration thickness check max 1 m; proximity damage rings on the XZ plane: head <0.5m, body <0.75m, leg <1m), and applies damage. `weaponId` indexes the `weaponRegistry` array serialized on the Player prefab so the server reads stats from its own `WeaponData` copy. Ammo/inventory are still client-trusted until the buy/economy systems are networked.
- **Alt-fire (ADS)**: holding right click with a weapon whose `WeaponData.altFireType == AimDownSight` (currently the Vandal) narrows the vision cone by `zoomMultiplier` in tan-space (smoothed in `VisionController`), slows movement (`adsSpeedMultiplier` on `PlayerMovementNetwork`, ×0.76), lowers fire rate (`adsFireRateMultiplier`), and swaps spread to the ADS column. ADS state flows like crouch: `PlayerController.IsAiming` → `PlayerInputTick.Ads` → `PlayerStateSnapshot.IsAds` → server reads `AuthoritativeIsAds`. Spread movement penalties are classified from the player's *chosen inputs* (Walk/Crouch flags in the snapshot), never from measured speed — speed modifiers like ADS must not shift the spread band.
- **Lag compensation**: `HitboxHistory` (on Player and TargetDummy prefabs) records each entity's authoritative position per network tick, server-side only, in a static `All` registry. `WeaponController.ShootServerRpc` rewinds hit resolution by the shooter's transport RTT + remote-view interpolation delay (server-measured — clients never send timestamps), capped at 1 s. The damage model is positional (XZ rings), so no physics-scene rewind is needed. Histories are wiped on `ServerTeleport` so rewound shots can't hit vacated respawn corpses.
- Player death → `PlayerMovementNetwork.ServerHandleDeath` teleports to spawn and revives one frame later (so spike drop sees the corpse position). Target dummies use `AutoRevive` instead.

### Netcode
- `PlayerMovementNetwork` — server-authoritative movement with owner prediction/reconciliation, remote-view snapshot interpolation (`interpolationDelayTicks` behind newest), and `ServerTeleport` (bumps `TeleportCount` in the snapshot so owners clear prediction and views cut instead of gliding). **All movement must go through `Simulate()`** or reconciliation reverts it; `ServerTeleport` is the one sanctioned exception.
- `NetworkSpawnMarker` — scene marker that server-spawns a registered network prefab at its own transform on session start (used for the spike pickup and target dummies; in-scene-placed NetworkObjects are avoided because despawn-destroying them is discouraged by NGO).
- Spike carry state exists twice: the owner's local `HasSpike` (HUD/plant checks) and the server instance's `HasSpike` (authoritative — granted in `SpikePickup.TryGrant`, cleared via `ServerClearSpike` on plant/death).

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
