# Vision System v2 — "The Eye Is a Spotlight"

> **Status: IMPLEMENTED** (2026-07-10). Component map:
> - Depth map camera + shader globals: `VisionController` (creates a hidden "Vision Eye Camera" at runtime)
> - Depth capture into persistent R32F texture + eye VP matrix publish: `VisionEyeDepthCapturePass` + `Shaders/Vision/VisionEyeDepthCopy.shader`
> - Camera routing (eye camera → capture, main camera → fog): `VisionFogRendererFeature`
> - Shadow test (4 body heights, 3x3 PCF, linear-depth bias): `Shaders/Vision/VisionFogComposite.shader`
> - Retired and deleted: `VisibleAreaBuilder`, `VisionFootprintComposer`, `VisionPolygonUnion`, `VisionFootprintPolygonPublisher`, `VisionFootprintMaskTarget`, `VisionPhysicsSetup`, `VisionGroundGenerator`, footprint/uniform-fog shaders. VisionGround proxies are no longer used — the scene objects can be deleted.

Goal: light up every spot on the map where the player could see an enemy (2m tall body,
any part visible counts), respecting aim direction, FoV, elevation up and down, and
dynamic occluders. Everything else is fog.

---

## Part 1 — The Simple Draft

### The core idea in one sentence

**Treat the player's eye as a spotlight, and treat "fog" as that spotlight's shadow.**

Imagine mounting a projector where the player's eye is (1.5m above the ground under the
player), pointed exactly where the player is aiming, with a beam shaped exactly like the
player's first-person camera frustum (103° wide, 70.53° tall). Everything the projector
light lands on is what a first-person player would literally see on their screen.
Everything in shadow — behind a wall, below a ledge lip, past the back edge of high
ground — is exactly what they can't see.

That answers "which *surfaces* can the player see." But your question is slightly
different: "where could the player see an *enemy standing*?" The trick that converts one
into the other:

> A spot on the ground is lit if a **2m-tall body standing on that spot would catch any
> of the projector's light.**

So for every point on the ground, you don't ask "is this floor pixel lit?" — you ask "is
the *head* of a person standing here lit? Is their chest lit? Their knees?" If any of
those catch light, the spot lights up.

### Why this matches every one of your bullet points, for free

* **Top-down camera** — the fog is drawn as a fullscreen overlay on the top-down view;
  the projector is completely independent of the render camera.
* **High ground, partially visible** — the light from a low eye grazes over the ledge
  lip. Ground just past the lip is in the lip's shadow, but a head poking up there still
  catches light → lit. Deeper back, even the head is in shadow → fog. The lit/fogged
  boundary on high ground automatically sits exactly where a 2m enemy would start to be
  hidden. No special casing.
* **Low ground when looking down** — the projector beam simply points down there;
  nothing special happens, it's just geometry the light reaches.
* **Aim-dependent** — the projector rotates with the aim every frame.
* **Head-level aim (cursor ground + 1.5m)** — that just defines the projector's pitch,
  same as your current `visionOrigin`.
* **Fog outside FoV** — anything outside the projector's beam gets no light at all.
* **Dynamic occluders (your added requirement)** — anything you render into the
  projector's view casts a shadow. Another player's body, a smoke cloud mesh, a moving
  door: include it, and it blocks vision. Zero extra logic.

### Why this beats what you've been fighting with

Your current `VisibleAreaBuilder` tries to *construct the shape* of the visible area:
march rays, detect ledges via VisionGround proxy colliders, refine edges, union polygons
with Clipper2, squeeze the result into 64 shader vertices. That is the hard direction of
the problem — the visible region on a map with elevation is not one polygon, it's a
different shape *per elevation*, with holes, and its true silhouette is defined by 3D
occlusion, not 2D outlines. Every iteration you've built has been an approximation of
that shape, and the approximation is why nothing has "truly worked."

The spotlight approach never constructs the shape at all. It answers the question
per-pixel — "is this exact spot visible?" — which is a trivially cheap lookup once the
projector's depth image exists. The shape *emerges* on screen instead of being computed.
This is precisely the reason real-time rendering abandoned geometric shadow volumes in
favor of shadow maps.

Bonus: it's the same math your GPU already does for every shadow-casting light in the
scene, so it's fast, well-documented, and battle-tested.

---

## Part 2 — Technical Implementation

The technique is **shadow mapping**, with the player's eye playing the role of the
light. Two GPU passes replace the entire CPU footprint pipeline.

### Overview

```
Pass 1 (new):    Render a depth-only image of the world FROM THE EYE
                 → the "vision depth map" (a.k.a. shadow map)

Pass 2 (exists): Your VisionFogCompositePass, with the shader's
                 point-in-polygon test replaced by shadow-map sampling
```

### Pass 1 — The vision depth map

Render the scene from the eye's point of view into a **depth texture** (no color). This
records, for every direction inside the FoV, the distance to the nearest occluder.

* **Position / rotation:** `PlayerController.VisionOrigin` (eye at ground + 1.5m,
  pitched toward cursor-ground + 1.5m). Identical to today.
* **Projection:** a standard **perspective projection matrix**. Your FoV numbers
  (103° horizontal / 70.53° vertical) are *exactly* a 16:9 camera frustum
  (`tan(51.5°) / tan(35.265°) ≈ 1.777`) — Valorant's actual first-person camera. So a
  plain Unity camera projection with `fieldOfView = 70.53`, `aspect = 16/9` models the
  player's vision *more* faithfully than your current elliptical angle-cone tests do.
* **What to render:** Wall, Ground, Prop (static occluders) **plus Dynamic** (your new
  requirement). Exclude the local player's own body, or he shadows himself.
* **Render target:** an **RTHandle**, depth-only, format `D32_SFloat` (32-bit depth —
  you'll want the precision, see *Pitfalls*), resolution 2048×2048 to start
  (a **shadow map resolution** trade-off: higher = crisper fog edges, more GPU time).
* **Clip planes:** near as far out as you can tolerate (0.3m is fine), far =
  `MaxViewDistance`. Depth precision depends heavily on the near plane
  (look up: **depth buffer precision**, **reversed-Z**).
* **Where it runs:** a new `ScriptableRenderPass` in your existing
  `VisionFogRendererFeature`, injected at `BeforeRenderingShadows` or similar, drawing
  the occluder layers with a depth-only override material via a `RendererList`.
  (Pragmatic alternative while prototyping: a second disabled `Camera` component you
  render manually with `SubmitRenderRequest` / `Camera.Render` into a depth RT.
  Same result, less URP plumbing, slightly more overhead.)
* Publish two shader globals: the depth texture, and the eye's
  **view-projection matrix** (`GL.GetGPUProjectionMatrix(proj, true) * worldToCamera`)
  as `_VisionEyeVP`.

### Pass 2 — Composite shader changes

Your `VisionFogComposite.shader` already does the hard half: it reconstructs the
**world-space position** of every top-down pixel from the camera depth buffer
(`ComputeWorldSpacePosition`). Keep all of that. Replace `PointInPolygonXZ` with a
shadow test:

```hlsl
// "Could the player see a 2m body standing at worldPos?"
// Sample the body at several heights; any lit sample reveals the spot.
static const float HEIGHTS[3] = { 1.9, 1.1, 0.4 };   // head, torso, legs

half VisionMask(float3 worldPos)
{
    half lit = 0.0h;
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        float3 samplePos = worldPos + float3(0, HEIGHTS[i], 0);

        // 1. Into the eye's clip space
        float4 clip = mul(_VisionEyeVP, float4(samplePos, 1.0));
        if (clip.w <= 0.0) continue;                  // behind the eye
        float3 ndc = clip.xyz / clip.w;               // perspective divide → NDC

        // 2. FoV test == frustum test (this replaces IsInCone entirely)
        if (any(abs(ndc.xy) > 1.0)) continue;         // outside the beam

        // 3. Occlusion test: closer than the nearest occluder in that direction?
        float2 uv = ndc.xy * 0.5 + 0.5;
        #if UNITY_UV_STARTS_AT_TOP
            uv.y = 1.0 - uv.y;
        #endif
        float mapDepth = SAMPLE_TEXTURE2D(_VisionEyeDepth, sampler_VisionEyeDepth, uv).r;
        // with reversed-Z: bigger = closer
        lit = max(lit, ndc.z + _VisionDepthBias > mapDepth ? 1.0h : 0.0h);
    }
    return lit;
}
```

Then exactly as today: `visibility = 1 - fogStrength * (1 - mask)`.

Three heights per pixel = three texture samples: negligible cost. The multi-height
sampling is what implements your "any part of the body" rule — the head sample alone
covers 95% of cases (if the feet are visible over terrain, the head always is), but the
extra samples correctly light spots seen through low gaps and windows where the head
would be hidden but the legs exposed.

Since fog is binary, you'll want the lit/fog edge to not shimmer. Two standard fixes,
apply in this order:

1. **Depth bias** (`_VisionDepthBias`) — prevents **shadow acne** (false self-shadowing
   speckle). Because your sample points float *above* surfaces rather than on them,
   you need much less bias than typical shadows; start tiny. Too much causes
   **peter-panning** (visibility leaking past the top edges of walls).
2. **PCF — percentage-closer filtering** — sample the depth map at a small 3×3
   neighborhood and average the comparisons, giving a soft antialiased fog edge.
   URP's `SampleShadowmap` helpers or a hardware **comparison sampler**
   (`SamplerComparisonState`, `SampleCmpLevelZero`) do this nearly free.

### What this replaces / what stays

| Component | Fate |
|---|---|
| `VisibleAreaBuilder`, `VisionFootprintComposer`, `VisionPolygonUnion` (Clipper2), `VisionFootprintPolygonPublisher` | **Retired.** The depth map replaces the entire footprint-construction pipeline. |
| VisionGround proxy layer + `VisionGroundGenerator` | **Retired.** Ledge handling is inherent to depth testing; no per-map generation step ever again. |
| `VisionFogRendererFeature` / `VisionFogCompositePass` | **Kept**, gains the depth pre-pass. |
| `VisionFogComposite.shader` | **Kept**, polygon test → shadow test (above). |
| `VisionEvaluator` raycasts + `EntityVisibilityDriver` + `VisibleEntity` | **Kept as-is.** Exact CPU raycasts remain the right tool for the gameplay-critical "is this actual enemy rendered" decision. It already samples multiple body points against the same FoV/eye config, so the two systems will agree visually. Long-term you *can* unify by making it sample the same depth map (readback or GPU query), but there's no need to. |
| `VisionConfig` | **Kept** — same numbers now feed a projection matrix instead of ray fans. |

### Pitfalls to expect (and their lookup terms)

* **Shadow acne / depth bias / peter-panning** — the classic bias tuning dance.
  Mitigations: sample heights above the surface (you already do), small constant bias,
  optionally render **back faces** into the depth map instead of front faces.
* **Perspective aliasing** — one depth texel covers a lot of ground far away or at
  grazing angles, so distant fog edges get blocky. Mitigations: PCF, higher resolution.
  If 500m view distance ever really matters, the heavyweight fix is
  **cascaded shadow maps**, but try 2048² + PCF first — top-down zoom levels rarely
  show distant edges closely.
* **Depth precision over a 0.3–500m range** — use `D32_SFloat` and keep **reversed-Z**
  (Unity default) so precision is spent close to the eye.
* **Matrix/platform gotchas** — `GL.GetGPUProjectionMatrix`, `UNITY_UV_STARTS_AT_TOP`,
  UNITY_REVERSED_Z. Nearly every "my shadow map is upside down / inverted" bug is one
  of these three.
* **Pass ordering** — the depth pass must run before the composite pass every frame,
  after aim is updated (`LateUpdate` aim → render). Your `executionOrder` habits from
  `EntityVisibilityDriver` (order 101) apply.

### Suggested build order

1. Depth pass rendering into an RTHandle; verify with the frame debugger /
   `debugShowMask`-style output that you can blit the raw depth to screen.
2. Composite shader: single head-height sample, no bias, no PCF. This alone should
   already look better than the polygon system on your elevated test plane.
3. Add bias tuning + the 3-height loop.
4. Add PCF for soft edges.
5. Include the Dynamic layer in the depth pass; watch other bodies cast vision shadows.
6. Delete the retired classes and the VisionGround scene objects. Enjoy.

### Glossary — terms worth reading up on

| Term | What it is / why you care |
|---|---|
| **Shadow mapping** | The whole technique: depth image from a light (here: the eye), then per-pixel depth comparison. Search: "shadow mapping tutorial", LearnOpenGL's chapter is the canonical intro. |
| **Depth texture / depth buffer** | Per-pixel nearest-surface distance; the "shadow map" is one rendered from the eye. |
| **View-projection matrix / light space** | Transforms a world point into the eye's screen coordinates; how the shader asks "where does this point land in the eye's view?" |
| **NDC (normalized device coordinates)** | Post-perspective-divide space where the frustum is a −1..1 box; the `abs(ndc.xy) > 1` check *is* the FoV test. |
| **Perspective projection / view frustum** | The pyramid-shaped beam; your 103°/70.53° pair is a 16:9 frustum. |
| **Depth bias, shadow acne, peter-panning** | The three keywords of shadow-comparison artifacts and their tuning. |
| **PCF (percentage-closer filtering)** | Averaging several depth comparisons for soft, stable shadow (fog) edges. |
| **Comparison sampler (SampleCmp)** | Hardware that does PCF taps for free. |
| **Reversed-Z** | Depth stored 1→0 near→far for precision; affects every depth comparison you write. |
| **RTHandle / ScriptableRenderPass / RendererList** | The URP-specific plumbing for owning a render target and drawing a filtered set of objects in a custom pass. |
| **Projective texturing** | The general "project an image from a point in space" concept shadow mapping is built on — useful mental model for the eye-as-projector idea. |
