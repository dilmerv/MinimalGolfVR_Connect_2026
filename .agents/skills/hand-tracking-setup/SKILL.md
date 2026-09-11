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

## Time budget (120s hard target)

Each `unity command` costs 5–15s, so the budget allows at most ~6 Unity calls.
Rules to stay under it:

- Batch ALL local checks into the single step-0 shell call. Never probe the
  SDK at runtime: every prefab GUID, namespace, and serialized field name is
  hardcoded in the pinned reference below for the SDK in this repo. If a
  future upgrade moves things, update this skill file — do not burn runtime
  discovering it.
- Never run bare `unity command` discovery. The exact commands for Pipeline
  0.6.0-exp.1 are listed below. `eval_file` does NOT exist in this Pipeline
  version — the equivalent is `run_script` (a self-contained `.cs` file with
  a named static entry point, compiled in-memory with no domain reload).
- Never read whole source files to decide what to do — the step-0 greps are
  the decision input. Never enter Play mode and never screenshot: console +
  compile is the verification bar (a deliberate exception to the repo
  `AGENTS.md` screenshot requirement — you handle visual checks yourself).
- No fixed `sleep`s. The only wait is the bounded retry loop after exiting
  Play mode (step 2).

## Settled decisions

1. **Interaction SDK visuals, not Core `OVRHandPrefab`.** ISDK visuals
   reference static FBX meshes, so they render in the Scene view with no Play
   mode and auto-hide when untracked. Do NOT use the `Ghost-Hand` prefabs
   (static grab-pose previews) and do not add the grab stack
   (`HandGrabInteractor`, `SyntheticHand`, `HandGrabStateVisual`) when objects
   need no grab posing.
2. **Project config:** `handTrackingSupport = ControllersAndHands` (value
   `1`). Never touch `handTrackingFrequency`.
3. **Scene layout:** one `OVRHandsDataSource` at the scene root; one
   `OVRHandVisualLeft` / `OVRHandVisualRight` under each rig anchor at
   `TrackingSpace/LeftHandAnchor` / `TrackingSpace/RightHandAnchor`, each via
   `HandVisual.InjectHand(matchingHand)`. Remove legacy Core
   `OVRHandPrefab*` instances so hands don't double-render.
4. **Mandatory rig wiring (prefab leaves these empty — Play-mode
   `AssertionException`s result otherwise):** `OVRCameraRigRef` on the rig
   (`_ovrCameraRig` = the rig, `_requireOvrHands = false`) assigned to both
   sources' `_cameraRigRef`; ONE shared `TrackingToWorldTransformerOVR` on
   the data-source root (injected with the rig ref) assigned to both sources'
   `_trackingToWorldTransformer`. The sources' `_ovrHand` and
   `_handSkeletonProvider` come pre-wired — leave them alone.
5. **Pinch wiring:** resolve a same-side ISDK `Hand` per input object and OR
   `hand.IsConnected && hand.GetFingerIsPinching(HandFinger.Index)` into the
   existing trigger-held path, keeping controller behavior unchanged. The
   gameplay assembly needs an `Oculus.Interaction` asmdef reference.
6. **No test-framework requirement.** If the repo has no harness covering VR
   input, do not add one; verify via recompile + console.

## Pinned reference (current SDK — hardcoded, do NOT re-verify at runtime)

- Prefabs (GUID → path; resolve via `AssetDatabase.GUIDToAssetPath`):
  `50375bfeeea522849bd08dabcf6aeb83` → `.../interaction.ovr/.../Hands/OVRHandsDataSource.prefab`;
  `70d02a90551f23042a882cdceeaf8e3a` → `.../interaction/.../Hands/OVRHandVisualLeft.prefab`;
  `34a22dd67c1e5344591237fdd61e78ec` → `.../interaction/.../Hands/OVRHandVisualRight.prefab`.
  Instantiated object names: `OVRHandsDataSource` (children
  `OVRHandDataSourceLeft`/`...Right`), `OVRHandVisualLeft`/`...Right`.
- Namespaces: `HandVisual` in `Oculus.Interaction`; `Hand`, `Handedness`,
  `HandFinger`, `FromOVRHandDataSource`, `OVRCameraRigRef`,
  `TrackingToWorldTransformerOVR` in `Oculus.Interaction.Input`;
  `OVRCameraRig`, `OVRHand`, `OVRProjectConfig` are global-namespace.
  (`Oculus.Interaction.Hand` does NOT exist — fix the namespace, not the asmdef.)
- Serialized fields: sources `_cameraRigRef` + `_trackingToWorldTransformer`
  (empty in prefab, must wire) and `_ovrHand` + `_handSkeletonProvider`
  (pre-wired); `OVRCameraRigRef._ovrCameraRig` + `_requireOvrHands` (set
  `false`); transformer `_cameraRigRef`; `Hand._iModifyDataFromSourceMono`
  references the sibling source object named `OVRHandDataSourceLeft/Right`
  (this is how sides are detected — never read `Hand.Handedness` in Editor code).
- Config: `OVRProjectConfig.HandTrackingSupport.ControllersAndHands == 1`;
  commit via `CommitProjectConfig(OVRProjectConfig.CachedProjectConfig)`.
- Pipeline commands (0.6.0-exp.1): `editor_status`, `recompile_status`,
  `run_script --file <project-root-relative .cs> --entry <Type.Method> --timeout_ms 120000`,
  `save_scene`, `get_console_logs --severity Error --limit 20`,
  `editor_stop`.

## Procedure

0. ONE pre-check shell call (instant). Run exactly this:
   ```bash
   grep -c "IsHandPinchHeld" Assets/MinimalGolf/Scripts/VRGolfClub.cs; grep -o '"Oculus.Interaction"' Assets/MinimalGolf/MinimalGolf.asmdef; grep "handTrackingSupport" Assets/Oculus/OculusProjectConfig.asset; grep -o "HandVisualLeft\|HandVisualRight\|OVRHandsDataSource\|OVRCameraRigRef" Assets/MinimalGolf/Scenes/MinimalGolf.unity | sort | uniq -c
   ```
   Skip step 1 when pinch is present AND the asmdef reference is present.
   Skip step 2 when config is `1` AND all four scene markers are present.
   Skip both → jump to step 4 (verify only).
1. (Pinch wiring absent only) Four local edits, no Unity calls: add
   `"Oculus.Interaction"` to the gameplay asmdef `references`; in the
   gameplay class add `using Oculus.Interaction.Input;`, the two fields
   below, the two methods below (adapt the `controller` field name), and OR
   `IsHandPinchHeld()` into the existing trigger-held expression:
   ```csharp
   private Hand sameSideHand;
   private int handResolveNextFrame;
   ```
   ```csharp
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
   (Game code compiled normally may use the two-argument
   `FindObjectsByType` overload; the single-argument restriction below is
   `run_script`-only.)
2. (Config, data source, visuals, or rig wiring missing only) ONE status
   gate, ONE script write, ONE `run_script`:
   - Gate (one call): `unity command editor_status && unity command recompile_status`.
     Proceed when `status` is `ready`, no compile/reload is in flight, and
     recompile is `completed`. If `playMode` is `playing`, the Editor was
     left in Play mode (scene edits made there are lost) — run
     `unity command editor_stop`, then poll back with the bounded loop
     (no fixed sleeps):
     `for i in 1 2 3 4 5 6; do unity command editor_status && break || sleep 10; done`.
     If recompile is still running, same loop on `recompile_status`.
   - Write the full `Temp/ISDKHandSetup.cs` below (outside `Assets/`, so
     writing it triggers no import/domain reload; `--file` resolves against
     the project root) and run it once:
     `unity command run_script --file Temp/ISDKHandSetup.cs --entry ISDKHandSetup.Main --timeout_ms 120000`.
     It sets the config, creates/skips the data source, resolves both hands
     by modifier parent name, creates/skips + injects both visuals, wires
     rig ref + transformer, removes legacy prefabs, and logs one
     `ISDKVIS-READBACK` line per anchor and per source. Confirm via those
     lines in the result — do NOT write a second verify script.
   ```csharp
   using System.Collections.Generic;
   using UnityEditor;
   using UnityEditor.SceneManagement;
   using UnityEngine;
   using UnityEngine.SceneManagement;
   using Oculus.Interaction;
   using Oculus.Interaction.Input;

   public static class ISDKHandSetup
   {
       public static string Main()
       {
           List<string> log = new List<string>();

           OVRProjectConfig cfg = OVRProjectConfig.CachedProjectConfig;
           cfg.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
           OVRProjectConfig.CommitProjectConfig(OVRProjectConfig.CachedProjectConfig);
           log.Add("config handTrackingSupport=" + ((int)OVRProjectConfig.CachedProjectConfig.handTrackingSupport).ToString());

           OVRCameraRig rig = Object.FindAnyObjectByType<OVRCameraRig>();
           if (rig == null) { return Fail(log, "no OVRCameraRig in scene"); }
           Transform leftAnchor = rig.leftHandAnchor;
           Transform rightAnchor = rig.rightHandAnchor;
           if (leftAnchor == null || rightAnchor == null) { return Fail(log, "missing hand anchors"); }

           GameObject dsPrefab = LoadPrefab("50375bfeeea522849bd08dabcf6aeb83", log);
           GameObject visLeftPrefab = LoadPrefab("70d02a90551f23042a882cdceeaf8e3a", log);
           GameObject visRightPrefab = LoadPrefab("34a22dd67c1e5344591237fdd61e78ec", log);
           if (dsPrefab == null || visLeftPrefab == null || visRightPrefab == null) { return Fail(log, "prefab load failed"); }

           GameObject dsRoot = GameObject.Find("OVRHandsDataSource");
           FromOVRHandDataSource[] sceneSources = Object.FindObjectsByType<FromOVRHandDataSource>(FindObjectsInactive.Include);
           if (dsRoot == null && sceneSources.Length > 0)
           {
               Transform t = sceneSources[0].transform.parent;
               dsRoot = (t != null ? t.gameObject : sceneSources[0].gameObject);
           }
           if (dsRoot == null)
           {
               dsRoot = (GameObject)PrefabUtility.InstantiatePrefab(dsPrefab);
               dsRoot.name = dsPrefab.name;
               Undo.RegisterCreatedObjectUndo(dsRoot, "Add OVRHandsDataSource");
               log.Add("datasource created");
           }
           else
           {
               log.Add("datasource existing");
           }

           Hand leftHand = null;
           Hand rightHand = null;
           Hand[] hands = dsRoot.GetComponentsInChildren<Hand>(true);
           foreach (Hand h in hands)
           {
               SerializedObject hso = new SerializedObject(h);
               SerializedProperty hp = hso.FindProperty("_iModifyDataFromSourceMono");
               Object src = (hp != null ? hp.objectReferenceValue : null);
               string owner = "";
               if (src is Component) { owner = ((Component)src).gameObject.name; }
               if (owner == "OVRHandDataSourceLeft") { leftHand = h; }
               else if (owner == "OVRHandDataSourceRight") { rightHand = h; }
           }
           if (leftHand == null || rightHand == null) { return Fail(log, "hand resolve failed"); }
           log.Add("hands resolved");

           EnsureVisual(leftAnchor, visLeftPrefab, leftHand, "Left", log);
           EnsureVisual(rightAnchor, visRightPrefab, rightHand, "Right", log);

           OVRCameraRigRef rigRef = rig.GetComponent<OVRCameraRigRef>();
           if (rigRef == null)
           {
               rigRef = Undo.AddComponent<OVRCameraRigRef>(rig.gameObject);
               log.Add("rigref created");
           }
           else
           {
               log.Add("rigref existing");
           }
           Undo.RecordObject(rigRef, "Wire OVRCameraRigRef");
           SerializedObject rso = new SerializedObject(rigRef);
           SetObjectRef(rso, "_ovrCameraRig", rig);
           SerializedProperty reqProp = rso.FindProperty("_requireOvrHands");
           if (reqProp != null) { reqProp.boolValue = false; }
           rso.ApplyModifiedProperties();

           TrackingToWorldTransformerOVR xform = dsRoot.GetComponent<TrackingToWorldTransformerOVR>();
           if (xform == null)
           {
               xform = Undo.AddComponent<TrackingToWorldTransformerOVR>(dsRoot);
               log.Add("transformer created");
           }
           else
           {
               log.Add("transformer existing");
           }
           Undo.RecordObject(xform, "Wire transformer");
           SerializedObject xso = new SerializedObject(xform);
           SetObjectRef(xso, "_cameraRigRef", rigRef);
           xso.ApplyModifiedProperties();

           FromOVRHandDataSource[] sources = dsRoot.GetComponentsInChildren<FromOVRHandDataSource>(true);
           foreach (FromOVRHandDataSource s in sources)
           {
               Undo.RecordObject(s, "Wire hand data source");
               SerializedObject sso = new SerializedObject(s);
               SetObjectRef(sso, "_cameraRigRef", rigRef);
               SetObjectRef(sso, "_trackingToWorldTransformer", xform);
               sso.ApplyModifiedProperties();
               SerializedObject read = new SerializedObject(s);
               string line = "ISDKVIS-READBACK source=" + s.gameObject.name
                   + " rigRef=" + HasRef(read, "_cameraRigRef")
                   + " ovrHand=" + HasRef(read, "_ovrHand")
                   + " transformer=" + HasRef(read, "_trackingToWorldTransformer")
                   + " skeleton=" + HasRef(read, "_handSkeletonProvider");
               Debug.Log(line);
               log.Add(line);
           }

           int removed = 0;
           Transform[] anchors = new Transform[] { leftAnchor, rightAnchor };
           foreach (Transform a in anchors)
           {
               OVRHand[] legacy = a.GetComponentsInChildren<OVRHand>(true);
               foreach (OVRHand o in legacy)
               {
                   if (o != null && o.gameObject.name.Contains("OVRHandPrefab"))
                   {
                       Undo.DestroyObjectImmediate(o.gameObject);
                       removed++;
                   }
               }
           }
           log.Add("legacy removed=" + removed.ToString());

           EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
           string summary = string.Join("\n", log.ToArray());
           Debug.Log("ISDKVIS-DONE\n" + summary);
           return summary;
       }

       private static void EnsureVisual(Transform anchor, GameObject prefab, Hand hand, string side, List<string> log)
       {
           HandVisual visual = anchor.GetComponentInChildren<HandVisual>(true);
           if (visual == null)
           {
               GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, anchor);
               go.name = prefab.name;
               Undo.RegisterCreatedObjectUndo(go, "Add HandVisual" + side);
               visual = go.GetComponentInChildren<HandVisual>(true);
               log.Add("visual" + side + " created");
           }
           else
           {
               log.Add("visual" + side + " existing");
           }
           if (visual != null && hand != null) { visual.InjectHand(hand); }
           string mesh = "none";
           if (visual != null)
           {
               SkinnedMeshRenderer smr = visual.GetComponentInChildren<SkinnedMeshRenderer>(true);
               if (smr != null && smr.sharedMesh != null) { mesh = smr.sharedMesh.name; }
           }
           string line = "ISDKVIS-READBACK anchor=" + anchor.name
               + " visual=" + (visual != null ? visual.gameObject.name : "missing")
               + " hand=" + (hand != null ? "injected" : "MISSING")
               + " mesh=" + mesh;
           Debug.Log(line);
           log.Add(line);
       }

       private static GameObject LoadPrefab(string guid, List<string> log)
       {
           string path = AssetDatabase.GUIDToAssetPath(guid);
           if (string.IsNullOrEmpty(path))
           {
               log.Add("guid " + guid + " unresolved");
               return null;
           }
           GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
           log.Add("prefab " + path + (prefab == null ? " NULL" : " ok"));
           return prefab;
       }

       private static void SetObjectRef(SerializedObject so, string prop, Object val)
       {
           SerializedProperty p = so.FindProperty(prop);
           if (p != null) { p.objectReferenceValue = val; }
       }

       private static bool HasRef(SerializedObject so, string prop)
       {
           SerializedProperty p = so.FindProperty(prop);
           return p != null && p.objectReferenceValue != null;
       }

       private static string Fail(List<string> log, string reason)
       {
           log.Add("FAILED " + reason);
           string summary = string.Join("\n", log.ToArray());
           Debug.LogError("ISDKVIS-FAILED " + reason + "\n" + summary);
           return summary;
       }
   }
   ```
3. `unity command save_scene` — only if step 1 or 2 ran AND the
   `run_script` result contains `created`. Skip on a no-op (avoids a
   needless domain reload).
4. Single verification pass in ONE shell call:
   `unity command recompile_status && unity command editor_status && unity command get_console_logs --severity Error --limit 20`.
   Done when recompile is `completed` with no errors, editor is `ready`,
   zero error logs, and the step-2 read-back shows injected visuals with
   static meshes plus all-`True` source wirings. Note:
   `FromOVRHandDataSource` asserts run only in Play mode, so the static
   all-`True` read-back is the proxy — hand the user a Play-mode
   confirmation ("press Play; the Camera-Rig-Ref / transformer assertions
   should be gone"). Report failures rather than claiming success.
5. ONE cleanup call: delete `Temp/ISDKHandSetup.cs`, then
   `git status --short`. Revert `Assets/Resources/OculusRuntimeSettings.asset`
   if listed (the simulator toggle dirties it — unrelated to this skill).

## Gotchas

- **`run_script` compiles with Obsolete warnings as errors:** use
  `FindAnyObjectByType`, never `FindFirstObjectByType`; use
  `FindObjectsByType<T>(FindObjectsInactive)` with NO `FindObjectsSortMode`
  argument. It needs a full class + named static entry (`Type.Method`);
  `return` is fine; keep everything in the one file.
- **Assert masking:** the first failing `Start()` assert throws, hiding the
  rest. A clean console for `CameraRigRef` does NOT mean the transformer is
  wired — the script wires rig ref AND transformer in the same pass and
  reads back all four flags per source.
- **`GameObject.Find` only finds ACTIVE objects** — the script falls back to
  `FindObjectsByType` for the data source and uses anchor-scoped
  `GetComponentsInChildren<OVRHand>(true)` + name match for legacy cleanup.
  Confirm no `OVRHandPrefab*` remains with a scene-file `grep` after saving.
- If `run_script` fails with `NullReferenceException` right after a source
  edit, the game assembly is stale: wait for `recompile_status: completed`,
  confirm `editor_status` is `ready`, and re-run unchanged.

