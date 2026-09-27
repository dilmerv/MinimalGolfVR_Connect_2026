---
name: apply-polishing-phases
description: Run a visual polish pass on MinimalGolf in small verified phases (UI, ground, decor, shaders, runtime VFX), with screenshots and console checks per phase.
---

# Apply Polishing Phases

Use for visual polish work on MinimalGolf (concept matching, UI restyle,
environment dressing, shader tweaks). Full record of pass 1:
`docs/polish-pass-1.md`. Drive all Editor work through `unity command`
per `AGENTS.md`; never edit live Editor state through other automation.

## Phase loop

Work in small phases (one surface each: HUD, ground, decor, one shader).
Per phase:

1. Capture baseline: `screenshot --view game` and `--view scene` to `Temp/`.
2. Implement via `Temp/*.cs` scripts run with pipeline `eval`/`eval_file`:
   idempotent find-or-create, deterministic seeds, `Undo` for scene edits.
   No `FindObjectsSortMode`
   args (obsolete); use `FindObjectsByType<T>()` /
   `FindObjectsByType<T>(FindObjectsInactive)` and `FindAnyObjectByType`
   instead of `FindFirstObjectByType`.
3. Gate: `editor_status` ready + `recompile_status` completed, no errors.
4. Check console errors; capture game + scene shots; visually inspect before
   calling the phase done.
5. `save_scene` after major operations. Prefab/material/asset edits also
   need `AssetDatabase.SaveAssets()` — `save_scene` does NOT persist them
   (an unsaved prefab lost the whole bevel pass to a crash here once).
   Delete `Temp/*.cs` probes when done.
6. Verify asset edits on disk (grep the `.prefab`/`.mat` for the new
   reference or mtime) — a visual check alone cannot prove persistence.

## Shadow checklist (in this order)

Shadows have failed silently here before. When anything looks unlit, check:

1. Light: `shadows` on, `lightmapBakeType` Realtime, sane color/intensity.
2. Casters: `shadowCastingMode` On for every renderer that should cast
   (levels shipped with 174/182 Off). Ground stays receive-only.
3. Camera: `UniversalAdditionalCameraData.m_RenderShadows` true on
   `CenterEyeAnchor` (it shipped false).
4. URP assets (PC **and** Mobile): main-light shadows supported, shadow
   distance/cascades/bias sane for scene scale.
5. Toon shader: `lightValue` must be `ndotl * shadowAttenuation` only —
   never multiply `mainLight.distanceAttenuation` (its backing uniform is
   unset/0 for these draws; the main light is directional anyway).
6. Still stuck: float a default-material cube above the ground. If it casts
   and toon doesn't, the bug is in the toon path — debug-viz `mainLight`
   terms one at a time (`color`, `shadowAttenuation`, `ndotl`,
   `distanceAttenuation`), then revert the viz.

## Shader and material rules

- A `[Toggle(KEYWORD)]` float and its keyword must be set together and
  verified via `get_material_properties` — keywords get re-synced from the
  float, so keyword-only changes silently drop.
- After shader edits, force reimport and expect magenta in game view until
  variants finish compiling (60–90 s). Wait and re-shoot; never "fix" it.
- Skinned meshes: the shader sees posed positions, and ISDK bind pose
  differs hugely from the rest pose. Never use positional cutoffs on hands —
  bake pose-invariant data (vertex colors) into mesh copies instead (see
  `Minimal Golf → Apply Hand Cuff Meshes`).

## Rebaking the hand cuff (rarely needed)

The one-shot bake script was deleted after use. To rebake (e.g. a cuff
width change): source meshes are ISDK `LeftHand`/`RightHand` (wrist stub at
the bind-space **−Y** end) plus legacy `l_|r_handMeshNode` (wrist at **+X**
end). Paint cuff black (0.02, 0.02, 0.025) within 2.8 cm of the stub end
with a 5 mm blend to white, using an explicit clamp+hermite helper —
`Mathf.SmoothStep` returned wrong values when verified here. Save as
`Assets/MinimalGolf/Models/HandCuff_<mesh>.asset`, run the Apply menu,
and verify on a temporary in-scene hand before deleting it.

## Runtime-VFX rules

- Prefer pooled, manually integrated effects over physics/particles for
  small transient bursts and ambient swarms (`BallHitSparks`,
  `BushBeeSwarms`). Per-instance data needs real materials — the SRP
  Batcher silently ignores `MaterialPropertyBlock` tints.
- Parent dynamic anchors into the moving roots they track (swarm anchors
  into bushes, never world space or the manager). `HideAndDontSave`
  keeps runtime clutter out of the hierarchy — but then debug-counts
  must use `FindObjectsInactive.Include`, since `Exclude` skips hidden
  objects and reports a false zero (in edit mode `Include` misses them
  too — count with `Resources.FindObjectsOfTypeAll`).
- Content gated by reveal/activation timing must lazy-build (retry in
  `Update` until the target set is non-empty); `Awake`-only discovery
  misses late-activating objects.
- `OnValidate` forbids `AddComponent` and `DestroyImmediate`
  (`SendMessage` error); defer edit-mode work with
  `EditorApplication.delayCall` (`#if UNITY_EDITOR` in runtime files),
  where `DestroyImmediate` is the legal cleanup (`Destroy` is illegal
  in edit mode).
- Edit-mode preview checkboxes: one-shots auto-uncheck
  (`BallHitSparks.debugPreviewBurst`); persistent previews stay checked
  with uncheck-to-remove and live rebuild on knob tweaks
  (`BushBeeSwarms.debugPreviewSwarms`).
- Collision tests must start OUTSIDE the target collider — teleports
  into overlap never fire `OnCollisionEnter`. When a hit does nothing,
  isolate paths: direct `SpawnBurst` (tint/render) vs logged collision
  (gating/color resolve).

## Scene-graph rules

- Ground and decor live under `VRCourseAnchor`: `VRCoursePlacement`
  re-poses the anchor at runtime, so world-space dressing detaches.
- Never move `CenterEyeAnchor` for closeups — the OVR rig resets it. Crop
  captures instead (Unity `ImageConversion` in `eval` works).
- Scene-view `screenshot` framing is unreliable; the game view is the
  source of truth.
- Only `RevealOccluder`-tagged renderers (gates/windmill) are reveal-managed.
  Keep new decor untagged with `_REVEAL_CLIP` off.
- Temp verification objects (hands, probe cubes) must be deleted before
  `save_scene`; confirm absence afterwards.

## UI sprite rules

- Rounded/bevel UI comes from baked 9-slice sprites (`RoundedPanel`,
  `RoundedPill`): luminance-only bevel (top rim, bottom shade, face
  gradient) so tints keep their hue; mid-face gray must match the tint
  compensation divisor or hues drift.
- Runtime-recolored elements (pips, power segs) must render their exact
  authored color at the sprite's brightest point — no compensation there.
- (Recovered once: sprite assignments made only in memory were lost to
  a crash — persist + grep-verify per phase-loop steps 5–6.)

## Ball hit sparks

- `BallHitSparks` (`HitFX` root, `Hit Spark.mat` URP Unlit): 64 pooled
  6 mm cubes, gravity + shrink fade, ~0.5 s life, per-spark material
  clones (see Runtime-VFX rules on `MaterialPropertyBlock`).
- `GolfBallImpact` hook gates: speed ≥ 0.6, |contact normal.y| ≤ 0.65
  (excludes ground floors), collider under a `MiniGolfLevel`,
  60 ms throttle.
- Hit color = material tint (`_BaseColor`, else `_Color`) × main-texture
  mean (4×4 `GetPixelBilinear`; colormap textures must be readable).
- Debug: `debugPreviewBurst` checkbox fires a one-shot at the live ball
  (play) or level-0 spawn (edit), sampling the nearest surface color;
  auto-unchecks, logs position + color, watch the Scene view.

## Bee swarms (PROVISIONAL — verification open)

- `BushBeeSwarms`: orbiters above every `Bush Green` renderer. Knobs:
  `beeColor`, `brightness` (color multiplier), `minSize`/`maxSize`,
  `flyRadius`, `hoverHeight`, `flySpeed`, `minQuantity`/`maxQuantity`
  per bush, `seed` (deterministic). Defaults: 4–8 mm bees, 15 cm
  radius, 6 cm hover, yellow ×1.25, 4–8/bush. One shared Unlit
  material clone; bees face velocity.
- Debug: `debugPreviewSwarms` checkbox builds the swarms in edit
mode (Scene view; box stays on, uncheck removes; knob tweaks
rebuild live); in play, checking rebuilds from current settings.
- OPEN BUG: swarms intermittently never materialize in play (observed
  manager `bees` list filled while zero `BeeSwarm_*`/`Bee` objects
  exist; also observed empty with zero errors). Repro: enter play,
  compare reflected `bees.Count` against a scene-wide `Bee*` count
  (with `FindObjectsInactive.Include` in play; in edit mode
`Include` misses them — use `Resources.FindObjectsOfTypeAll`). Suspects: `HideAndDontSave`
  interplay with play-mode domain reload. Not verified — fix before
  reuse.

## Course stripes

- Toon-shader mow bands (`_STRIPES_ON` toggle float + keyword, both set
  together): object-space bands across the width — uniform count on
  every surface regardless of size/orientation. Enabled on
  `Course Green` (40 grass renderers: fairways/ramps/banks/islands).
  Knobs: `Stripe Count` 6, `Stripe Strength` 0.15, `Stripe Softness`
  0–1 (hard steps → smooth, phase-constant via variable-width
  smoothstep over the band wave).
- Verify with the crank test first (count 3 / strength 0.5): if bands
  appear, the path works and defaults were just subtle.
- After ANY shader edit, re-apply material props once the async
  reimport completes — the import resets new props to defaults
  (verify the `.mat` file, not just the Inspector).
- Edit mode can show transient `Material (Instance)` clones masking
  the asset (stripes vanish in the Game view while play/headset stays
  correct). Verify renderers use the asset and re-point stragglers
  before trusting an edit-mode capture.

## Replay runbook (clean-tree reproduction)

Reproduces the full session on the project from before these changes.
Order matters: shader → materials → shadow stack → dressing → hands →
HUD → VFX. The phase loop above still applies to every phase (baseline
shots, recompile gates, persist + grep-verify, probes deleted). Where
this runbook and `docs/polish-pass-1.md` differ, the runbook wins (its
values were re-extracted from the shipped files).

Companions (copy into place — do NOT retype): `files/Scripts/*.cs`,
`files/Models/*.asset` (mesh data; the generator probes were deleted),
`files/UI/*.png` + `*.meta` (bake output; the `.meta` carries the
9-slice borders — copy it too).

### Phase 1 — Toon shader (ONE edit, ONE reimport)

`Assets/MinimalGolf/Shaders/MinimalGolfToon.shader`. The session spread
this over three passes; do it once to pay a single 60–90 s reimport:

1. Light fix (THE shadow root cause):
`half lightValue = ndotl * mainLight.shadowAttenuation * mainLight.distanceAttenuation;`
→ `half lightValue = ndotl * mainLight.shadowAttenuation;`
2. Properties block additions:
`[Toggle(_CHECKER_ON)] _CheckerEnabled("Ground Checker", Float) = 0`
`_CheckerSize("Checker Size (World M)", Float) = 1.1`
`_CheckerStrength("Checker Strength", Range(0,0.3)) = 0.07`
`[Toggle(_STRIPES_ON)] _StripesEnabled("Mow Stripes", Float) = 0`
`_StripeCount("Stripe Count", Float) = 7`
`_StripeStrength("Stripe Strength", Range(0,0.3)) = 0.08`
`_StripeSoftness("Stripe Softness", Range(0,1)) = 1`
3. `#pragma shader_feature_local _CHECKER_ON` and `_STRIPES_ON`
after the `_REVEAL_CLIP` pragma.
4. Varying: `float3 positionOS : TEXCOORD5;` in `Varyings`, written
in the vertex shader as `output.positionOS = input.positionOS.xyz;`.
CBUFFER decls: `float _CheckerSize; half _CheckerStrength;`
`float _StripeCount; half _StripeStrength; half _StripeSoftness;`.
5. Fragment blocks before `MixFog`:
`#ifdef _CHECKER_ON` world-xz checker × `_CheckerStrength`;
`#ifdef _STRIPES_ON` object-space mow bands:
`stripeU = positionOS.x + 0.5; wave = sin(stripeU * count * 2π);`
`edge = max(softness, 1e-3); stripe = smoothstep(-edge, edge, wave);`
`color *= 1 - stripe * strength;` (phase-constant softness).
Verify: force reimport, wait out magenta, game capture shows a lit
scene. (Vertex-color support already exists in the base shader.)

### Phase 2 — Materials

Create on the toon shader unless noted (shared toon defaults: ambient
0.34, rim power 3.8, shadow threshold 0.48 / softness 0.035):

- Ground Sage: base (0.350, 0.623, 0.387); checker ON (size 1.1,
strength 0.09); no outline, no rim.
- Bush Green: (0.28, 0.46, 0.26); outline ON width 0.002 color
(0.06, 0.14, 0.08); rim 0.06.
- Rock Gray: (0.66, 0.69, 0.73); outline ON width 0.003 color
(0.10, 0.12, 0.12); rim 0.06.
- Flower Orange: (0.88, 0.51, 0.18); everything else default/off.
- Grass Tuft: (0.33, 0.52, 0.29); everything else default/off.
- Hit Spark: URP Unlit, white. Bee Yellow: URP Unlit, white.
Modify in place:
- Hand White Toon: `_UseVertexColor` 0 → 1 (leave the orphaned
`_WristBand*` floats; harmless).
- Course Green: stripes ON — count 6, strength 0.15, softness 0
(shader default is 1; shipped look is hard-edge), keyword
`_STRIPES_ON` + float `_StripesEnabled` set together.
- StarrySky: horizon height −0.182 → −0.04, falloff 0.01 → 0.06,
horizon color → (0.557, 0.596, 0.443).
Verify every toggle float + keyword pair via `get_material_properties`.

### Phase 3 — Shadow stack

1. URP PC asset: shadow distance 50 → 25, cascades 4 → 2,
normal bias 0.5 → 0.1. URP Mobile asset: distance 50 → 25,
depth bias 1 → 0.5, normal bias 1 → 0.1, soft shadows 0 → 1,
soft quality 2 → 3.
2. `CenterEyeAnchor` → `UniversalAdditionalCameraData`:
`m_RenderShadows` 0 → 1.
3. Level renderers: `shadowCastingMode` ON for all EXCEPT the
ground disc (receive-only), flower heads (off), and the 7
`RevealOccluder`-tagged gates/windmill (reveal-managed — leave).
Session flipped 174 of 182.
4. WARM SUN is pre-existing — VERIFY only, do not restyle:
directional, color (0.99, 0.71, 0.58), intensity 1.25, soft
shadows, strength 0.9, bias 0.02 / normal 0.05, realtime.
Verify: game capture shows sun shadows on the courses. If not,
work the shadow checklist above (probe-cube test isolates the
toon path).

### Phase 4 — Ground + decor

Everything parents under `VRCourseAnchor` (never world space).

1. Copy `files/Models/*.asset` → `Assets/MinimalGolf/Models/`.
Mesh data (flat-shaded, non-indexed): GroundCap 897v/1728t
(R=20 m sphere, 7 m-radius cap); Rock 60v/20t; Bush 180v/60t;
GrassBlades 156v/52t; FlowerHeads 180v/60t.
2. Ground: GroundCap mesh + Ground Sage, local pos
(−0.25, −0.03, 2.20), cast OFF, receive ON.
3. Decor — 12 items, local transforms (rot = yaw°, uniform scale):
Rock_A (−1.50,−0.20,0.20) 20° 0.16; Bush_A (1.15,−0.17,0.35)
70° 0.17; Rock_B (2.00,−0.19,2.00) 140° 0.13; Bush_B
(−2.00,−0.11,2.20) 200° 0.19; Rock_C (0.20,−0.12,3.60) 260°
0.15; Bush_C (2.60,−0.34,4.20) 320° 0.16; Rock_D
(−2.90,−0.39,4.60) 45° 0.18; Bush_D (−0.90,−0.32,5.50) 110°
0.15. Rocks → Rock/Rock Gray, cast ON; bushes → Bush/Bush
Green, cast ON. Tufts (groups, no renderer): Tuft_A
(−1.15,−0.12,0.55) 0.08; Tuft_B (−1.70,−0.09,1.85) 0.09;
Tuft_C (0.55,−0.08,3.35) 0.07; Tuft_D (−0.55,−0.26,5.20)
0.09; each with Blades (GrassBlades/Grass Tuft, cast ON) +
Heads (FlowerHeads/Flower Orange, cast OFF).
4. No `RevealOccluder` tags on decor; decor materials keep
`_REVEAL_CLIP` off.
Verify: scene + game captures, dressing sits on the cap surface.

### Phase 5 — Hand cuff

1. Bake rule (reconstructed from the shipped vertex colors —
dark spans 2.86 cm at the documented ends): ISDK `LeftHand` /
`RightHand` → 2.8 cm band from the bind-space −Y end; legacy
`l_|r_handMeshNode` → 2.8 cm band from the +X end. Cuff color
(0.02, 0.02, 0.025), 5 mm hermite blend to white with an
explicit clamp helper — NOT `Mathf.SmoothStep` (verified
wrong here). Save `Models/HandCuff_<mesh>.asset` × 4.
2. Copy `files/Scripts/ApplyHandCuff.cs` →
`Assets/MinimalGolf/Editor/`; hands use Hand White Toon
(vertex colors ON from Phase 2).
3. Run `Minimal Golf → Apply Hand Cuff Meshes`; verify on a
temporary in-scene hand, then delete the temp.
Never use positional shader cutoffs on hands (bind pose ≠ rest
pose by ~0.27 m) — vertex colors are pose-invariant.

### Phase 6 — HUD

1. Copy `files/UI/*.png` + `*.meta` → `Assets/MinimalGolf/UI/`.
Sprite spec: RoundedPanel 128², corner radius ≈ 25 px, border
30, face gray 0.86 (rim ≈ 0.99 top, 0.35 bottom + 1 px dark
outline); RoundedPill 64×32, border 15, face 0.90, brightest
point 1.0. Tints: panels compensate ÷ 0.86 so mid-face hue is
unchanged; runtime-recolored pips/segs render exact colors.
2. Copy `files/Scripts/VRGolfUI.cs` over `Scripts/VRGolfUI.cs`
(changes: completed-pip seafoam → cyan `0x3AD0E6`, course label
→ `HOLE {NN:00} • {NAME}`, pip heights 10/6 → 12/8).
3. Prefab `VR_UI_Root`: every Image → Sliced (30; the fully
transparent backdrop stays Simple). RoundedPanel on all BGs /
toast / complete card / ScoreBox / PlayAgain with tints ÷ 0.86:
panel (0.055, 0.14, 0.19) → (0.0640, 0.1628, 0.2209);
PlayAgain orange → (1.026, 0.593, 0.214); cream surfaces ×
1.1628. RoundedPill on pips (26×8) / power segs / accent bar /
divider with colors EXACT (no compensation). Title 56 Bold
`MINIMAL GOLF`; Course autosize 9–16.
4. REVERT the scene instance to the prefab afterwards — stale
instance overrides survive prefab fixes (the session residue
left scene pips/seg sprites stripped). Verify the scene sprite
set EQUALS the prefab sprite set, element by element.
5. `AssetDatabase.SaveAssets()` + grep the `.prefab` for
RoundedP* refs (visual check alone cannot prove persistence).
Verify: game capture bevels, cropped zoom for closeups (never
move `CenterEyeAnchor`).

### Phase 7 — Hit sparks

1. Copy `files/Scripts/BallHitSparks.cs` → `Scripts/`
(pool 64, speeds 1.0–2.8, upBias 0.9, gravity 7, drag 1.2,
life 0.45–0.7 s, size 6 mm, throttle 60 ms; material Hit
Spark from Phase 2) and `files/Scripts/GolfBallImpact.cs`
over `Scripts/GolfBallImpact.cs` (adds the spark hook: speed
≥ 0.6, |contact normal.y| ≤ 0.65, collider under a
`MiniGolfLevel`; hit color = tint × main-texture 4×4 mean —
the Kenney MinigolfKit `colormap.png` must be readable
(`isReadable: 1` in its `.meta`)).
2. Scene: `HitFX` root + `BallHitSparks` with the spark
material assigned.
3. Verify: `debugPreviewBurst` checkbox fires a one-shot at the
live ball (play) or level-0 spawn (edit) — watch the Scene
view. Collision tests must start OUTSIDE the target collider;
isolate direct-`SpawnBurst` vs logged-collision per the
runtime-VFX rules. Per-spark material clones (never
`MaterialPropertyBlock` — the SRP Batcher ignores it).

### Phase 8 — Bee swarms (PROVISIONAL)

1. Copy `files/Scripts/BushBeeSwarms.cs` → `Scripts/`; material
Bee Yellow from Phase 2.
2. Scene: `BushBees` object + component: bush material
`Bush Green`, seed 1234, bee color (1.0, 0.78, 0.12),
brightness 1.25, size 4–8 mm, quantity 4–8/bush, fly radius
0.15 m, hover 0.06 m, speed 2.2.
3. Debug preview: check `debugPreviewSwarms` to see the
swarms in the Scene view without entering play (uncheck removes
them; knob tweaks rebuild live).
4. OPEN BUG — do NOT record as working until fixed: swarms
intermittently never materialize in play (see `Bee swarms`
section). Gate: reflected `bees.Count` must match a
scene-wide `Bee*` count (`FindObjectsInactive.Include` —
`HideAndDontSave` hides them from `Exclude`).

### Phase 9 — Course stripes

Shader code landed in Phase 1; this phase enables and tunes:

1. Course Green already carries count 6 / strength 0.15 /
softness 0 from Phase 2 — crank-test FIRST (count 3, strength
0.5): bands must appear on fairways/ramps/banks/islands (40
grass renderers). If they do, the path works; restore shipped
values.
2. Softness 0–1 demo: hard steps → smooth bands, constant
phase. Shipped value 0 (hard-edge).
3. Re-point any `Material (Instance)`-masked renderers back to
the Course Green asset before trusting an edit-mode capture
(play/headset stay correct regardless).
4. After ANY shader edit, re-apply material props once the
async reimport completes — imports reset new props to
defaults. Verify the `.mat` file, not the Inspector.

### Final verification

`editor_status` ready + recompile completed, zero console
errors, game + scene captures visually inspected per phase,
all `Temp/*.cs` probes and temp objects deleted (confirm
absence), `AssetDatabase.SaveAssets()` + `save_scene`,
grep-verify `.prefab`/`.mat` persistence on disk. (Meta XR
Simulator package was not installed during the session; if
present, activate it per `AGENTS.md`. `SampleSceneProfile.asset`
carries an incidental DepthOfField shell with mode Off — a no-op,
not part of the replay.)

`files/` index: `Scripts/` — BallHitSparks, BushBeeSwarms,
GolfBallImpact, VRGolfUI, ApplyHandCuff (copy into
`Scripts/` resp. `Editor/`); `Models/` — GroundCap, Rock,
Bush, GrassBlades, FlowerHeads `.asset`; `UI/` —
RoundedPanel + RoundedPill `.png` + `.meta`.

## Time budget

Each `unity command` costs 5–15 s; full recompiles take minutes. Batch reads
into single `eval` calls and poll `recompile_status` instead of guessing.
