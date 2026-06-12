# Project Overview
- Game Title: Valorant Trainer
- High-Level Concept: Top-down tactical shooter trainer focusing on precise aiming and wall penetration.
- Players: Single player.
- Inspiration: Valorant.
- Tone / Art Direction: Prototype / Tactical.
- Target Platform: PC.
- Screen Orientation: Landscape.
- Render Pipeline: URP.

# Game Mechanics
## Core Gameplay Loop
The player practices shooting at target dummies. Success depends on precise mouse placement directly on the enemy and understanding wall penetration limits.
## Controls and Input Methods
- **Mouse Position**: Determines aim direction and target point.
- **Left Click (Attack)**: Fires the weapon.

# UI
- No major UI changes. Existing ammo display will be maintained.

# Key Asset & Context
- `Assets/Scripts/Weapons/WeaponController.cs`: Main logic for shooting, hit detection, and damage calculation.
- `Assets/Scripts/Combat/Health.cs`: Damage reception.
- `Wall` Layer: A new layer used to identify penetrable obstacles.
- `TargetDummy`: The enemy prefab with a `CapsuleCollider`.

# Implementation Steps

1. **Layer and Environment Setup**
   - **Description**: Add a "Wall" layer to the project. Assign this layer to all static environment obstacles (like boxes and walls) to distinguish them from enemies and the player.
   - **Assigned role**: developer
   - **Dependencies**: None
   - **Parallelizable**: Yes

2. **Implement Wall Thickness Calculation**
   - **Description**: Add a helper method to `WeaponController` that calculates the thickness of a wall along a specific ray. This involves a `RaycastAll` and secondary backward raycasts for each wall hit to find exit points.
   - **Assigned role**: developer
   - **Dependencies**: Step 1
   - **Parallelizable**: No

3. **Overhaul Shooting Logic in WeaponController**
   - **Description**: Update `WeaponController.Shoot()` to:
     - 1. Identify if the mouse is hovering over an enemy using a Camera Raycast.
     - 2. If an enemy is targeted, calculate the trajectory from the player to the mouse world position.
     - 3. Calculate total wall penetration along that trajectory using the helper from Step 2.
     - 4. Calculate proximity damage based on mouse distance to enemy center (Perfect < 0.1m, Medium < 0.25m, Low < 0.5m).
     - 5. Apply wall bang multiplier `(1.0 - totalThickness)` and proximity multiplier.
     - 6. Call `TakeDamage()` on the target.
   - **Assigned role**: developer
   - **Dependencies**: Step 2
   - **Parallelizable**: No

4. **Refine Proximity Multipliers**
   - **Description**: Define the damage multipliers in `WeaponController`:
     - **Perfect**: 10.0x (Insta-kill)
     - **Medium**: 1.0x (Standard)
     - **Low**: 0.5x (Reduced)
   - **Assigned role**: developer
   - **Dependencies**: Step 3
   - **Parallelizable**: Yes

# Verification & Testing
- **Perfect Hit**: Aim at the exact center of a TargetDummy. Verify it is killed instantly.
- **Deviation Test**: Aim slightly off-center (within 0.2m). Verify it takes standard damage.
- **Wall Bang (Thin)**: Place a 0.5m thick box between the player and dummy. Aim at the dummy through the box. Verify the dummy takes ~50% damage.
- **Wall Bang (Thick)**: Place a 1.2m thick box. Aim at the dummy. Verify no damage is dealt.
- **Mouse-Only Hit**: Aim at the ground *behind* a dummy so the trajectory passes through them. Verify no damage is dealt (as the mouse is not *on* the dummy).
