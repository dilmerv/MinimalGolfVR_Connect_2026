---
name: hand-tracking-setup
description: Enable Meta hand tracking, add hand visuals, and wire pinch input in a Unity VR project.
---

# Hand Tracking Setup

Add end-to-end hand tracking to a Unity VR project using the Meta XR SDK. Drive all Editor work through the Unity CLI Pipeline (`unity command`); never edit live Editor state through other automation.

## Procedure

1. Confirm the Editor is reachable: `unity command editor_status` must report `status: ready` with `compiling: false`. Discover commands first with `unity command`.
2. Decide Core vs Interaction SDK via the live docs (do not rely on training data):
   `metavr docs search "hand tracking"` and `metavr docs search "HandVisual SyntheticHand HandGrab"`.
   Rule of thumb: `OVRHand` + `OVRSkeleton` + `OVRMesh` visuals with `OVRHand.GetFingerIsPinching` pinch input suffice when the game does not grab objects. Only add the Interaction SDK grab stack (`HandGrabInteractor`, `SyntheticHand`, `HandGrabStateVisual`) when objects need grab posing.
3. Ensure project-level support with `unity command eval_file`: set `OVRProjectConfig.CachedProjectConfig.handTrackingSupport` to `ControllersAndHands` and `handTrackingFrequency` to `HIGH` for fast-motion gameplay, then `OVRProjectConfig.CommitProjectConfig`. It is already `ControllersAndHands` in many projects; verify, do not assume.
4. Install hand visuals by replicating Meta's `HandTrackingBlockData`: instantiate `Packages/com.meta.xr.sdk.core/Prefabs/OVRHandPrefab.prefab` once under each `OVRCameraRig` hand anchor, configured for `OVRRuntimeSettings.Instance.HandSkeletonVersion`. Skip anchors that already have an `OVRHand` child (idempotent). Save the scene with `unity command save_scene`.
5. Wire pinch into gameplay: resolve a same-side `OVRHand` per input object and OR `hand.IsTracked && hand.GetFingerIsPinching(OVRHand.HandFinger.Index)` into the existing trigger path, keeping controller behavior unchanged.

## Gotchas

- `OVRHand.HandType`, `OVRSkeleton.SetSkeletonType`, and `OVRMesh.SetMeshType` are `internal` in current SDKs. Set `HandType` through `UnityEditor.SerializedObject` (`FindProperty("HandType")`) and the type setters through reflection (`BindingFlags.Instance | BindingFlags.NonPublic`).
- `eval` compiles `return <expr>;` snippets against loaded assemblies. If a wiring script fails with `NullReferenceException` right after a script edit, the game assembly is stale: wait for `recompile_status: completed`, confirm `editor_status` is ready, and re-run the same script unchanged.
- `unity command eval` treats Obsolete warnings as errors: use `FindAnyObjectByType`, not `FindFirstObjectByType`.

## Verification

- `recompile_status` is `completed` with no errors and `editor_status` is `ready`.
- `unity command get_console_logs --severity Error --limit 20` returns zero logs.
- Enter Play mode, capture Game and Scene views (`capture_game_view` / `capture_scene_view` into `Assets/Temp/`), and visually inspect both screenshots before reporting completion. Game view must show tracked hands; a blank Scene capture means its camera is misframed (the Pipeline has no reframe command), not that the setup failed.
