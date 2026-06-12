# Project Overview
- Game Title: Valorant Trainer (Top-Down)
- High-Level Concept: A top-down tactical shooter training game modeled after Valorant, focusing on correct mechanics like movement, vision, and sound management.
- Players: Single player (local prototype), designed for future 5v5 online multiplayer.
- Inspiration / Reference Games: Valorant
- Tone / Art Direction: Clean, tactical, URP-based 3D.
- Target Platform: PC (Windows)
- Screen Orientation / Resolution: Landscape 1920x1080
- Render Pipeline: URP

# Game Mechanics
## Core Gameplay Loop
1. Buy Phase: Purchase weapons and abilities.
2. Round Start: Move to tactical positions.
3. Combat: Engage enemies using vision, sound, and abilities.
4. Objective: Plant/Defuse Spike or eliminate the opposing team.
5. Round End: Award Creds and transition to next round.

## Controls and Input Methods
- WASD: Move relative to player facing (W = towards mouse).
- Mouse: Rotate player character.
- Shift: Walk (sneaking, no sound).
- Ctrl: Crouch (no sound, tighter spread).
- Left Click: Primary Fire.
- Right Click: Alt Fire / Zoom.
- E: Interact (Plant/Defuse).
- B: Open Buy Menu.

# UI
- HUD: Health, Armor, Ammo, Creds, Ability Icons, Minimap.
- Buy Menu: Grid of weapons and abilities with costs.
- Round Timer: Central top display.
- Kill Feed: Top right.

# Key Asset & Context
- `PlayerController.cs`: Handles WASD movement relative to mouse direction and rotation.
- `VisionSystem.cs`: Manages raycast-based FOV and entity visibility.
- `WeaponBase.cs`: ScriptableObject or Base Class for all weapons (Classic, Vandal, etc.).
- `GameManager.cs`: Handles round states (Buy, Active, Post-Round).
- `EconomyManager.cs`: Manages Creds and rewards.
- `PearlMap`: A 1:1 blockout of the Pearl map layout.

# Implementation Steps
## Phase 1: Foundations (Movement & Camera)
1. **Input Configuration**: Update `InputSystem_Actions` with specific bindings (Walk, Interact, Buy).
2. **Player Controller**: Implement `PlayerController.cs` for mouse-relative movement and rotation.
3. **Camera System**: Setup a top-down camera following the player at a fixed height.
4. **Verification**: Player can move and rotate correctly in an empty scene.

## Phase 2: Combat & Health
1. **Weapon System**: Implement `WeaponBase` and `WeaponInstance`. Create the `Classic` sidearm.
2. **Shooting Logic**: Implement raycast shooting with distance-based damage and spread.
3. **Health & Shields**: Implement HP and Armor logic.
4. **Verification**: Player can shoot targets, take damage, and see health/ammo change.

## Phase 3: Vision & Sound
1. **FOV Cone**: Implement raycast vision cone using a Mesh or logic-based approach.
2. **Visibility Logic**: Hide enemies outside the FOV or behind walls.
3. **Sound Indicators**: Implement sound radius for running/shooting and map markers.
4. **Verification**: Enemies are only visible in the cone; sound circles appear on the map when sprinting.

## Phase 4: Round Logic & Economy
1. **Game State Machine**: Implement states (BuyPhase, RoundActive, RoundEnd).
2. **Economy Manager**: Implement Cred awarding logic.
3. **Buy Menu UI**: Create a basic UI to purchase weapons.
4. **Verification**: Rounds cycle correctly, Creds are awarded, and weapons can be bought.

## Phase 5: Map & Objective
1. **Pearl Blockout**: Create a basic 3D blockout of the Pearl map.
2. **Spike System**: Implement plant/defuse logic and site triggers.
3. **Verification**: Can plant spike on site A/B, timer counts down, defuse possible.

# Verification & Testing
- **Movement Test**: Ensure W always moves towards the mouse cursor.
- **Vision Test**: Verify walls correctly block vision and smokes obscure players.
- **Shooting Test**: Check damage falloff and spread values against Valorant wiki stats.
- **Economy Test**: Verify loss streak bonuses and kill rewards match the project context.
