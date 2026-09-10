---
name: hand-tracking-setup
description: Enable Meta hand tracking, add hand visuals, and wire pinch input in a Unity VR project.
---

# Hand Tracking Setup

Add end-to-end hand tracking to a Unity VR project using the Meta XR SDK.
Drive all Editor work through the Unity CLI Pipeline (`unity command`); never
edit live Editor state through other automation.

Decisions below are settled — do NOT spend time re-deriving them via the
`metavr` docs CLI or any other hand-related doc lookup. (This is a deliberate,
narrow exception to the repo's metavr-first rule, scoped to this skill only.)

## Speed budget (30–60s target)

Each `unity command` costs seconds, so minimize round trips:

- Check `editor_status` ONCE. Never run bare `unity command` discovery (huge
  output) or API-probing `eval`s — every API fact needed is enshrined below.
- Prefer instant local checks (`grep` the source/asset/scene files) over Unity
  queries to decide what work remains. Every step is idempotent — skip steps
  whose local check already passes.
- Run ONE combined `eval_file` for config + visuals + read-back logging, not
  separate scripts per concern.
- 30–60s assumes a warm Editor and (if needed) an incremental compile. A cold
  Editor or full recompile can exceed it — that time is environment-bound, not
  skippable. Screenshots are out of scope for this skill (a deliberate exception
  to the repo `AGENTS.md` screenshot requirement — you handle visual checks
  yourself); that saves ~10–20s.

## Settled decisions

1. **Core SDK only, no Interaction SDK.** `OVRHand` + `OVRSkeleton` + `OVRMesh`
   visuals with `OVRHand.GetFingerIsPinching(OVRHand.HandFinger.Index)` pinch
   input suffice when the game does not grab objects (e.g. trigger-pull /
   drag-to-shoot gameplay). Only add the Interaction SDK grab stack
   (`HandGrabInteractor`, `SyntheticHand`, `HandGrabStateVisual`) when objects
   need grab posing.
2. **Project config:** `handTrackingSupport = ControllersAndHands`.
   `handTrackingFrequency` is intentionally left untouched — out of scope for
   this skill, whatever the project already uses stays.
3. **Visuals:** instantiate `Packages/com.meta.xr.sdk.core/Prefabs/OVRHandPrefab.prefab`
   once under each `OVRCameraRig` anchor at `TrackingSpace/LeftHandAnchor` and
   `TrackingSpace/RightHandAnchor`, named `OVRHandPrefabLeft` / `OVRHandPrefabRight`.
   Skip anchors that already have an `OVRHand` child (idempotent).
4. **Pinch wiring:** resolve a same-side `OVRHand` per input object (walk up to the
   `LeftHandAnchor` / `RightHandAnchor` ancestor, fall back to the controller side)
   and OR `hand.IsTracked && hand.GetFingerIsPinching(Index)` into the existing
   trigger-held path, keeping controller behavior unchanged. Resolve lazily so a
   null hand (visuals not installed / not tracked) simply means pinch is inactive.
5. **No maintained-test requirement for this skill.** If the repo has no test
   harness covering VR input, do not add a test framework; verify via recompile +
   console instead (no screenshots — you check visuals yourself).

## Enshrined API facts (do NOT re-probe via `eval`)

- `OVRHand.IsTracked` and `OVRHand.GetFingerIsPinching(HandFinger)` are public.
- `OVRHand.HandType` is `internal` → set via `UnityEditor.SerializedObject`
  (`FindProperty("HandType")`).
- `OVRSkeleton.SetSkeletonType` and `OVRMesh.SetMeshType` are `internal` →
  invoke via reflection (`BindingFlags.Instance | BindingFlags.NonPublic |
  BindingFlags.Public`). Resolve the parameter enum from
  `method.GetParameters()[0].ParameterType` + `Enum.Parse`; never hardcode it.
- `OVRSkeleton.GetSkeletonType()` is public, but `OVRMesh.GetMeshType()` is
  `internal` — a direct call fails compilation. Read the mesh type back via
  reflection as well.
- `OVRRuntimeSettings.Instance.HandSkeletonVersion` containing `OpenXR` means
  use `XRHandLeft`/`XRHandRight` for skeleton + mesh types, else `HandLeft`/`HandRight`.
- Asset YAML value (safe to `grep` locally): `handTrackingSupport: 1` ==
  `ControllersAndHands`.

## Procedure

0. Fast-path pre-checks (local, instant). Skip steps that already pass:
   - `IsHandPinchHeld` present in the gameplay source (e.g. `VRGolfClub.cs`)
     → skip step 1.
   - `handTrackingSupport: 1` in
     `Assets/Oculus/OculusProjectConfig.asset`, AND both `OVRHandPrefabLeft`
     and `OVRHandPrefabRight` in the scene file → skip step 2.
1. (Pinch wiring absent only) Source edit: add a cached `OVRHand`,
   `TryResolveSameSideHand()`, `IsHandPinchHeld()`, and OR the result into
   trigger-held. Then `unity command recompile` and poll `recompile_status`
   ONCE with a generous wait; poll again only if still compiling. (Order
   matters: do Editor-affecting `eval_file` work only after the game assembly
   is freshly compiled — see gotchas.)
2. (Config or visuals missing only) Run ONE `eval_file` that does all three
   in straight-line statements: set `handTrackingSupport` to `ControllersAndHands`
   and commit via
   `OVRProjectConfig.CommitProjectConfig(OVRProjectConfig.CachedProjectConfig)`
   (never touch `handTrackingFrequency`), install/skip visuals per anchor
   (Undo-registered, renamed, `HandType` via `Array.IndexOf(prop.enumNames, …)`,
   skeleton/mesh via reflection), then `Debug.Log` one
   `HANDVIS-READBACK … hand=… skel=… mesh=…` line per anchor.
   Confirm via console logs — do NOT write a second verify script. Expected:
   left = `HandLeft`/`XRHandLeft`/`XRHandLeft`,
   right = `HandRight`/`XRHandRight`/`XRHandRight`.
3. `unity command save_scene` (only if step 1 or 2 ran).
4. Single verification pass: `recompile_status` is `completed` with no errors,
   `editor_status` is `ready`, `get_console_logs --severity Error --limit 20`
   returns zero logs, read-back lines show the expected side/skeleton/mesh.
5. Check `git status` — revert `Assets/Resources/OculusRuntimeSettings.asset`
   if it was dirtied (e.g. by your own simulator toggle) to keep the
   hand-tracking diff minimal.

## Gotchas

- **`enumValueIndex` trap:** `SerializedProperty.enumNames` for `HandType` is ordered
  by enum *value* (`None, HandLeft, HandRight`), NOT `Enum.GetNames` declaration order
  (`HandLeft, HandRight, None`). Never map via `Enum.GetNames(typeof(OVRHand.Hand))` —
  always `Array.IndexOf(prop.enumNames, name)`, or both hands will be mis-sided.
- **`eval_file` takes statements only, no `return`.** The harness appends code after
  the file, so any `return` fails with "Unreachable code detected". Structure scripts
  as straight-line statements (`foreach` + `continue` instead of early returns) with
  read-back logging inline.
- `OVRProjectConfig.CommitProjectConfig` requires an argument:
  `CommitProjectConfig(OVRProjectConfig.CachedProjectConfig)`.
- If a script fails with `NullReferenceException` right after a source edit, the game
  assembly is stale: wait for `recompile_status: completed`, confirm `editor_status`
  is ready, and re-run the same script unchanged.
- `unity command eval` treats Obsolete warnings as errors: use `FindAnyObjectByType`,
  not `FindFirstObjectByType`; use `FindObjectsByType<T>(FindObjectsInactive)` without
  a `FindObjectsSortMode` argument.
- The Meta XR Simulator toggle writes unrelated state (e.g. `fovSimulationEnabled`)
  into `Assets/Resources/OculusRuntimeSettings.asset`. Revert that file if it shows
  up in `git status` to keep the hand-tracking diff minimal.
