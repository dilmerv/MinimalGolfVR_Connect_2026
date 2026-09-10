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

## Settled decisions

1. **Core SDK only, no Interaction SDK.** `OVRHand` + `OVRSkeleton` + `OVRMesh`
   visuals with `OVRHand.GetFingerIsPinching(OVRHand.HandFinger.Index)` pinch
   input suffice when the game does not grab objects (e.g. trigger-pull /
   drag-to-shoot gameplay). Only add the Interaction SDK grab stack
   (`HandGrabInteractor`, `SyntheticHand`, `HandGrabStateVisual`) when objects
   need grab posing.
2. **Project config:** `handTrackingSupport = ControllersAndHands`,
   `handTrackingFrequency = HIGH` (fast-motion gameplay such as a golf swing).
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
   console + screenshots instead.

## Procedure

1. Confirm the Editor is reachable: `unity command editor_status` must report
   `status: ready` with `compiling: false`. Discover commands first with
   `unity command`. (Order matters: do Editor-affecting `eval_file` work only
   after the game assembly is freshly compiled — see gotchas.)
2. Wire pinch into gameplay first (source edit, e.g. `VRGolfClub.cs`): add a
   cached `OVRHand`, `TryResolveSameSideHand()`, `IsHandPinchHeld()`, and OR the
   result into trigger-held. Then `unity command recompile` and poll
   `recompile_status` until `completed` with no errors.
3. Set project-level support with `unity command eval_file`: set
   `OVRProjectConfig.CachedProjectConfig.handTrackingSupport` to
   `ControllersAndHands` and `handTrackingFrequency` to `HIGH`, then
   `OVRProjectConfig.CommitProjectConfig(OVRProjectConfig.CachedProjectConfig)`.
   Verify with a separate `eval` read-back.
4. Install hand visuals with `unity command eval_file`: load the prefab via
   `AssetDatabase.LoadAssetAtPath`, `PrefabUtility.InstantiatePrefab(prefab, anchor)`
   per anchor (skip when an `OVRHand` child exists), register Undo, rename, then:
   - `HandType` through `UnityEditor.SerializedObject` (`FindProperty("HandType")`).
     Map the index via `Array.IndexOf(prop.enumNames, "HandLeft"/"HandRight")`.
   - Skeleton/mesh types through reflection
     (`BindingFlags.Instance | BindingFlags.NonPublic`): if
     `OVRRuntimeSettings.Instance.HandSkeletonVersion` contains `OpenXR`, use
     `XRHandLeft`/`XRHandRight` for `OVRSkeleton.SkeletonType` and
     `OVRMesh.MeshType`; otherwise use `HandLeft`/`HandRight`.
   - Read back and confirm: left anchor = `HandLeft`/`XRHandLeft`/`XRHandLeft`,
     right anchor = `HandRight`/`XRHandRight`/`XRHandRight`.
5. Save the scene with `unity command save_scene`.

## Gotchas

- `OVRHand.HandType`, `OVRSkeleton.SetSkeletonType`, and `OVRMesh.SetMeshType`
  are `internal` in current SDKs. Set `HandType` through `UnityEditor.SerializedObject`
  and the type setters through reflection.
- **`enumValueIndex` trap:** `SerializedProperty.enumNames` for `HandType` is ordered
  by enum *value* (`None, HandLeft, HandRight`), NOT `Enum.GetNames` declaration order
  (`HandLeft, HandRight, None`). Never map via `Enum.GetNames(typeof(OVRHand.Hand))` —
  always `Array.IndexOf(prop.enumNames, name)`, or both hands will be mis-sided.
- **`eval_file` takes statements only, no `return`.** The harness appends code after
  the file, so any `return` fails with "Unreachable code detected". Structure scripts
  as straight-line statements (`foreach` + `continue` instead of early returns) and
  verify results with a separate `eval` call.
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

## Verification

- `recompile_status` is `completed` with no errors and `editor_status` is `ready`.
- `unity command get_console_logs --severity Error --limit 20` returns zero logs.
- Hands present under both anchors with correct side/skeleton/mesh (read-back check).
