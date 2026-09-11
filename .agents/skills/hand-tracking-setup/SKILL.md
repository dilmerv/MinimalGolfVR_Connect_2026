---
name: hand-tracking-setup
description: Enable Meta hand tracking with Interaction SDK hand visuals and wire pinch input in a Unity VR project.
---

# Hand Tracking Setup

Add end-to-end hand tracking to a Unity VR project using the Meta XR
Interaction SDK (`Hand` + `HandVisual` fed by the OVR hand data source).
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
- Run ONE combined `eval_file` for config + data source + visuals + rig wiring
  + read-back logging, not separate scripts per concern.
- Never enter Play mode and never screenshot for this skill: console + compile
  is the verification bar. Play-mode smoke tests (sleeps + captures + stops)
  cost minutes and are out of scope.
- 30–60s assumes a warm Editor and (if needed) an incremental compile. A cold
  Editor or full recompile can exceed it — that time is environment-bound, not
  skippable. Screenshots are out of scope for this skill (a deliberate exception
  to the repo `AGENTS.md` screenshot requirement — you handle visual checks
  yourself); that saves ~10–20s.

## Settled decisions

1. **Interaction SDK visuals, not Core `OVRHandPrefab`.** Core's
   `SkinnedMeshRenderer` ships with no mesh — `OVRMesh` builds it at runtime via
   `OVRPlugin.GetMesh()`, so Core hands are invisible in Edit mode. ISDK's
   `OVRHandVisualLeft/Right` reference static FBX meshes (`OVRHand_L/R.fbx`),
   so they render in the Scene view with no Play mode, track live data at
   runtime, and auto-hide when untracked (`HandVisual.UpdateVisibility` disables
   the renderer unless `Hand.IsTrackedDataValid` — no frozen ghost hands).
   Do NOT use the `Ghost-Hand` prefabs for tracked visuals — those are static
   grab-pose previews for the grab system. Do not add the grab stack
   (`HandGrabInteractor`, `SyntheticHand`, `HandGrabStateVisual`) when objects
   need no grab posing (e.g. trigger-pull / drag-to-shoot gameplay).
2. **Project config:** `handTrackingSupport = ControllersAndHands` — the ISDK
   data source reads Core `OVRHand` tracking under the hood, so this stays
   required. `handTrackingFrequency` is intentionally left untouched — out of
   scope for this skill, whatever the project already uses stays.
3. **Scene layout:** one `OVRHandsDataSource` instance at the scene root (its two
   `Hand` components come pre-wired to the left/right modifier chains — never
   rebuild this by hand); one `HandVisualLeft` / `HandVisualRight` (instantiated
   from `OVRHandVisualLeft/Right`) under each `OVRCameraRig` anchor at
   `TrackingSpace/LeftHandAnchor` / `TrackingSpace/RightHandAnchor`, each
   injected via `HandVisual.InjectHand(matchingHand)`. Anchor parenting is safe:
   `HandVisual` sets its root pose in world space. Remove any legacy Core
   `OVRHandPrefabLeft/Right` instances so hands don't double-render at runtime.
4. **Mandatory rig wiring (the prefab leaves these empty — Play-mode
   `AssertionException`s result otherwise):** add `OVRCameraRigRef` to the
   `OVRCameraRig` (point `_ovrCameraRig` at itself, set `_requireOvrHands =
   false` since anchor `OVRHand`s are gone) and assign it to both sources'
   `_cameraRigRef`; add ONE shared `TrackingToWorldTransformerOVR` (on the
   data-source root, injected with the rig ref) and assign it to both sources'
   `_trackingToWorldTransformer`. The sources' `_ovrHand` and
   `_handSkeletonProvider` come pre-wired — leave them alone.
5. **Pinch wiring:** resolve a same-side ISDK `Hand` per input object (walk up to
   the `LeftHandAnchor` / `RightHandAnchor` ancestor, fall back to the controller
   side, match `Hand.Handedness`) and OR
   `hand.IsConnected && hand.GetFingerIsPinching(HandFinger.Index)` into the
   existing trigger-held path, keeping controller behavior unchanged. Resolve
   lazily so a null hand simply means pinch is inactive. The gameplay assembly
   needs an `Oculus.Interaction` asmdef reference.
6. **No maintained-test requirement for this skill.** If the repo has no test
   harness covering VR input, do not add a test framework; verify via recompile +
   console instead (no screenshots — you check visuals yourself).

## Enshrined API facts (do NOT re-probe via `eval`)

- **Namespaces (hard requirement):** `Hand`, `Handedness` (`Left = 0, Right = 1`),
  `HandFinger` (`Index = 1`) live in `Oculus.Interaction.Input`;
  `HandVisual` lives in `Oculus.Interaction`; `FromOVRHandDataSource`,
  `OVRCameraRigRef`, `TrackingToWorldTransformerOVR` live in
  `Oculus.Interaction.Input` (interaction.ovr package). `Hand.Handedness`,
  `Hand.IsConnected`, and `Hand.GetFingerIsPinching(finger)` are public.
- **Prefab paths (verify locally before use — an SDK upgrade may move them):**
  `Packages/com.meta.xr.sdk.interaction.ovr/Runtime/Prefabs/Hands/OVRHandsDataSource.prefab`
  and `Packages/com.meta.xr.sdk.interaction/Runtime/Prefabs/Hands/OVRHandVisualLeft.prefab`
  / `OVRHandVisualRight.prefab`. Confirm under
  `Library/PackageCache/com.meta.xr.sdk.interaction*/`.
- `FromOVRHandDataSource.Start()` asserts `CameraRigRef`, then
  `TrackingToWorldTransformer`, then `HandSkeletonProvider`, then `_ovrHand` —
  the first throw masks the later ones, so wire rig ref AND transformer together
  before the first Play. Prefab state: `_cameraRigRef` and
  `_trackingToWorldTransformer` are EMPTY (must wire); `_ovrHand` (correct
  `HandType`: left child `0`, right child `1`, since `OVRPlugin.Hand` is
  `None = -1, HandLeft = 0, HandRight = 1`) and `_handSkeletonProvider` are
  pre-wired (do not touch).
- `HandVisual.InjectHand(IHand)` is public — call it directly after
  instantiating visuals; re-running it on existing visuals is idempotent.
- **Side detection inside edit-mode `eval`:** do NOT read `Hand.Handedness`
  (needs live data). Instead read each root `Hand`'s
  `_iModifyDataFromSourceMono` via `SerializedObject` and match the referenced
  object's GameObject name (`OVRHandDataSourceLeft` / `...Right`).
- Asset YAML value (safe to `grep` locally): `handTrackingSupport: 1` ==
  `ControllersAndHands`.

## Procedure

0. Fast-path pre-checks (local, instant). Skip steps that already pass:
   - `IsHandPinchHeld` using ISDK `Hand` present in the gameplay source (e.g.
     `VRGolfClub.cs`), with `Oculus.Interaction` in the gameplay asmdef
     references → skip step 1.
   - `handTrackingSupport: 1` in
     `Assets/Oculus/OculusProjectConfig.asset`, AND `HandVisualLeft`,
     `HandVisualRight`, `OVRHandsDataSource`, and `OVRCameraRigRef` in the scene
     file → skip step 2.
1. (Pinch wiring absent only) Source edits: add `Oculus.Interaction` to the
   gameplay asmdef `references`, paste the proven block below (adapt the
   `controller` field name to the gameplay class; add
   `using Oculus.Interaction.Input;`) and OR `IsHandPinchHeld()` into the
   existing trigger-held expression, keeping controller behavior unchanged. No
   hierarchy investigation is needed — the walk-up handles clubs under
   controller anchors (e.g. `RightControllerAnchor`) as well as hand anchors.
   Then poll `recompile_status` ONCE with a generous wait; run a forced
   `unity command recompile` only if still compiling/stale (forced recompiles
   can stall on approval — auto-compile after a source edit usually
   suffices). Poll again only if still compiling. (Order matters: do
   Editor-affecting `eval_file` work only after the game assembly is freshly
   compiled — see gotchas.)

   ```csharp
   private Hand sameSideHand;
   private int handResolveNextFrame;

   private void TryResolveSameSideHand()
   {
       if (sameSideHand != null) return;
       if (Time.frameCount < handResolveNextFrame) return;
       handResolveNextFrame = Time.frameCount + 30;
       Transform anchor = transform;
       while (anchor != null && anchor.name != "LeftHandAnchor" && anchor.name != "RightHandAnchor")
           anchor = anchor.parent;
       bool isLeft = controller == OVRInput.Controller.LTouch;
       if (anchor != null)
           isLeft = anchor.name == "LeftHandAnchor";
       Handedness wanted = isLeft ? Handedness.Left : Handedness.Right;
       Hand[] hands = FindObjectsByType<Hand>(FindObjectsInactive.Include, FindObjectsSortMode.None);
       foreach (Hand h in hands)
       {
           if (h == null) continue;
           try { if (h.Handedness == wanted) { sameSideHand = h; return; } }
           catch { continue; }
       }
   }

   private bool IsHandPinchHeld()
   {
       TryResolveSameSideHand();
       if (sameSideHand == null) return false;
       try { return sameSideHand.IsConnected && sameSideHand.GetFingerIsPinching(HandFinger.Index); }
       catch { return false; }
   }
   ```
2. (Config, data source, visuals, or rig wiring missing only) Run ONE `eval_file`
   that does everything in straight-line statements: set `handTrackingSupport`
   to `ControllersAndHands` and commit via
   `OVRProjectConfig.CommitProjectConfig(OVRProjectConfig.CachedProjectConfig)`
   (never touch `handTrackingFrequency`); instantiate/skip `OVRHandsDataSource`
   at the scene root (Undo-registered); resolve the two root `Hand`s by their
   `_iModifyDataFromSourceMono` parent names; instantiate/skip
   `HandVisualLeft`/`HandVisualRight` per anchor (Undo-registered) and
   `InjectHand` the matching hand; add/skip `OVRCameraRigRef` on the rig
   (`_ovrCameraRig` = the rig, `_requireOvrHands = false`) and assign it to both
   sources; add/skip ONE shared `TrackingToWorldTransformerOVR` on the
   data-source root (injected with the rig ref) and assign it to both sources;
   destroy any legacy `OVRHandPrefab*` instances (anchor-scoped
   `GetComponentsInChildren<OVRHand>(true)`, name-matched — see gotchas); then
   `Debug.Log` one `ISDKVIS-READBACK …` line per anchor (`visual=… hand=injected
   mesh=LeftHand/RightHand`) and one per source
   (`rigRef=True ovrHand=True transformer=True skeleton=True`).
   Confirm via console logs — do NOT write a second verify script.
   Before running it, confirm the three prefab paths exist locally (instant, zero
   Editor calls) and fail fast instead of burning a round trip.
3. `unity command save_scene` (only if step 1 or 2 ran AND the logs show a
   change — skip when visuals/data source/rig wiring already report existing and
   the config was already set; a no-op save still risks a domain reload).
4. Single verification pass in ONE shell call: `recompile_status` is
   `completed` with no errors, `editor_status` is `ready`,
   `get_console_logs --severity Error --limit 20` returns zero logs, and the
   read-back lines show injected visuals with static meshes plus all-`True`
   source wirings. Note: `FromOVRHandDataSource` asserts run only in Play mode,
   so the static all-`True` read-back is the proxy — hand the user a Play-mode
   confirmation ("press Play; the Camera-Rig-Ref / transformer assertions should
   be gone").
5. Check `git status` — revert `Assets/Resources/OculusRuntimeSettings.asset`
   if it was dirtied (e.g. by your own simulator toggle) to keep the
   hand-tracking diff minimal.

## Gotchas

- **Namespace trap:** `Oculus.Interaction.Hand` does NOT exist — `Hand`,
  `Handedness`, and `HandFinger` are in `Oculus.Interaction.Input`, while
  `HandVisual` is in `Oculus.Interaction`. In `eval_file` this surfaces as
  "`Hand` does not exist in the namespace 'Oculus.Interaction' (are you missing
  an assembly reference?)" — fix the namespace, not the asmdef.
- **Assert masking:** the first failing `Start()` assert throws, hiding the rest.
  A clean console for `CameraRigRef` does NOT mean the transformer is wired —
  always wire rig ref AND transformer in the same pass and read back all four
  flags per source.
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
  a `FindObjectsSortMode` argument. (Source files compiled normally may use the
  two-argument overload freely.)
- `GameObject.Find` only finds ACTIVE objects — legacy-prefab cleanup that reports
  "removed 0" while the scene file still references the prefabs means the lookup
  missed, not that the scene is clean. Prefer anchor-scoped
  `GetComponentsInChildren<OVRHand>(true)` + name match, and confirm with
  `grep` on the scene file after saving.
- The Meta XR Simulator toggle writes unrelated state (e.g. `fovSimulationEnabled`)
  into `Assets/Resources/OculusRuntimeSettings.asset`. Revert that file if it shows
  up in `git status` to keep the hand-tracking diff minimal.
