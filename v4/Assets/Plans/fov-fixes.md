# Project Overview
- Game Title: Tactics Trainer (Valorant-like)
- High-Level Concept: Top-down shooter with tactical elements and FOV-based visibility.
- Players: Single player (currently).
- Target Platform: PC.
- Render Pipeline: URP.

# Game Mechanics
## Field of View & Fog of War
- The player has an infinite FOV cone.
- Everything outside the cone is darkened (Fog of War).
- Static map elements (walls, floors) are always visible but darkened when outside FOV.
- Dynamic elements (enemies, items) are completely hidden when outside FOV.

# UI
- No major UI changes, but the visual field should be clear of artifacts like flickering or whiteouts.

# Key Asset & Context
- `VisionSystem.cs`: Manages FOV mesh and visibility.
- `VisionMask.shader`: Stencil-writing shader for the FOV mesh.
- `FogShroud.shader`: Darkening shader that respects the stencil mask.
- `FogOfWarShroud`: A large plane that renders the fog.

# Implementation Steps
## 1. Fix Stencil and Z-Fighting (Shaders)
- **VisionMask.shader**: Change `ZTest` to `Always` and ensure `ColorMask 0`. (Assigned role: developer)
- **FogShroud.shader**: Ensure it renders in the correct queue and handles alpha correctly. (Assigned role: developer)

## 2. Refine Vision System Logic (VisionSystem.cs)
- **Origin Offset**: Raycast from eye level (e.g., player pivot + 1.5 units) to avoid hitting the ground. (Assigned role: developer)
- **Layer Management**: (Assigned role: developer)
    - Exclude the "Ground" layer from raycasting to prevent floor-level blocking.
    - Set the FOV mesh height to a fixed value (e.g., Y=10) to avoid Z-fighting with floors and players.
- **Detection Radius**: Maintain the "infinite" feel but ensure it doesn't break. (Assigned role: developer)

## 3. Scene and Layer Setup (Editor Script)
- **Create Layers**: Set up "Ground" layer (Layer 7). (Assigned role: developer)
- **Categorize Objects**: (Assigned role: developer)
    - Move all objects with "Ground" or "Floor" in their names to the "Ground" layer.
    - Move objects with "Wall" to the "Wall" layer.
- **Adjust Shroud**: (Assigned role: developer)
    - Move the `FogOfWarShroud` to Y=11 (above FOV mesh, below camera).
    - Ensure it is large enough to cover the entire map.
- **Update VisionSystem**: Configure `obstacleMask` to target `Default` and `Wall` but ignore `Ground`. (Assigned role: developer)

# Verification & Testing
- **Zoom Test**: Verify that zooming in doesn't cause whiteouts (Camera at Y=12, Shroud at Y=11).
- **Flicker Test**: Move around the map, especially spawn, to check for flickering in the FOV cone.
- **Occlusion Test**: Walk behind walls to ensure they correctly block the FOV cone.
- **Ground Test**: Verify that the FOV cone spreads over the floor without being blocked by "invisible" floor edges.
- **Entity Test**: Ensure Target Dummies still disappear when outside the FOV cone.
