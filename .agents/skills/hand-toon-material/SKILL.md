---
name: hand-toon-material
description: Apply the Hand White Toon material to the ISDK hand visuals in the Unity VR scene.
---

# Hand Toon Material

Apply the project's `Hand White Toon` material to every renderer under the
Interaction SDK `HandVisual`s (`OVRHandVisualLeft` / `OVRHandVisualRight`)
added by the hand-tracking setup. Drive all Editor work through the Unity CLI
Pipeline (`unity command`); never edit live Editor state through other
automation.

## Time budget

Each `unity command` costs 5–15s. Batch local checks into single shell calls
and never probe the SDK at runtime: the material GUID and script shape below
are pinned for this repo. If the material asset moves, update this skill file.

## Settled decisions

1. **`HandVisual` never swaps materials at runtime.** It only toggles the
   `SkinnedMeshRenderer` visibility and writes a wrist-scale float through a
   material property block. Setting `sharedMaterial` on the scene renderers
   is therefore persistent and safe.
2. **Apply to ALL renderers under each `HandVisual`**, not a hardcoded name
   list. The OVR visuals currently carry two `SkinnedMeshRenderer`s each
   (`LeftHand` + `l_handMeshNode`, `RightHand` + `r_handMeshNode`), but
   names may shift with SDK upgrades — enumerate, don't assume.
3. **Idempotent:** renderers already on the toon material are skipped, and
   `save_scene` runs only when at least one renderer changed.
4. **Scene files reference materials by GUID, never by name.** Verify with a
   `grep` for the GUID below, not the material name.

## Pinned reference

- Material: `Assets/MinimalGolf/Materials/Hand White Toon.mat`,
  GUID `b43bb954975ba4322adea89ae96e9470`.
- Namespace: `HandVisual` in `Oculus.Interaction` (visuals live under the rig
  hand anchors, added by the hand-tracking setup).
- Pipeline commands (0.6.0-exp.1): `editor_status`, `recompile_status`,
  `run_script --file <project-root-relative .cs> --entry <Type.Method> --timeout_ms 120000`,
  `save_scene`, `get_console_logs --severity Error --limit 20`.

## Procedure

0. ONE pre-check shell call (instant):
   ```bash
   grep -c "b43bb954975ba4322adea89ae96e9470" Assets/MinimalGolf/Scenes/MinimalGolf.unity
   ```
   A count of 4+ means the material is already applied — skip to step 3
   (verify only).
1. Gate (one call): `unity command editor_status && unity command recompile_status`.
   Proceed when `status` is `ready`, no compile/reload is in flight, and
   recompile is `completed`. If `playMode` is `playing`, run
   `unity command editor_stop` first, then poll back with the bounded loop
   (no fixed sleeps):
   `for i in 1 2 3 4 5 6; do unity command editor_status && break || sleep 10; done`.
2. Write the full `Temp/ApplyHandToon.cs` below (outside `Assets/`, so
   writing it triggers no import/domain reload; `--file` resolves against
   the project root) and run it once:
   `unity command run_script --file Temp/ApplyHandToon.cs --entry ApplyHandToon.Main --timeout_ms 120000`.
   It loads the material by GUID, sets `sharedMaterial` on every renderer
   under each `HandVisual`, and logs one `TOON-READBACK` line per changed
   renderer. Confirm via those lines in the result — do NOT write a second
   verify script.
   ```csharp
   using System.Collections.Generic;
   using UnityEditor;
   using UnityEditor.SceneManagement;
   using UnityEngine;
   using UnityEngine.SceneManagement;
   using Oculus.Interaction;

   public static class ApplyHandToon
   {
       public static string Main()
       {
           List<string> log = new List<string>();

           string path = AssetDatabase.GUIDToAssetPath("b43bb954975ba4322adea89ae96e9470");
           if (string.IsNullOrEmpty(path)) { return Fail(log, "toon material guid unresolved"); }
           Material toon = AssetDatabase.LoadAssetAtPath<Material>(path);
           if (toon == null) { return Fail(log, "toon material load failed"); }
           log.Add("material " + path + " ok");

           HandVisual[] visuals = Object.FindObjectsByType<HandVisual>(FindObjectsInactive.Include);
           if (visuals.Length == 0) { return Fail(log, "no HandVisual in scene"); }

           int changed = 0;
           foreach (HandVisual v in visuals)
           {
               Renderer[] renderers = v.GetComponentsInChildren<Renderer>(true);
               foreach (Renderer r in renderers)
               {
                   if (r == null || r.sharedMaterial == toon) continue;
                   Undo.RecordObject(r, "Apply Hand White Toon");
                   r.sharedMaterial = toon;
                   changed++;
                   string line = "TOON-READBACK visual=" + v.gameObject.name
                       + " renderer=" + r.gameObject.name
                       + " type=" + r.GetType().Name
                       + " material=" + r.sharedMaterial.name;
                   Debug.Log(line);
                   log.Add(line);
               }
           }
           log.Add("renderers changed=" + changed.ToString());

           if (changed > 0)
           {
               EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
           }
           string summary = string.Join("\n", log.ToArray());
           Debug.Log("TOON-DONE\n" + summary);
           return summary;
       }

       private static string Fail(List<string> log, string reason)
       {
           log.Add("FAILED " + reason);
           string summary = string.Join("\n", log.ToArray());
           Debug.LogError("TOON-FAILED " + reason + "\n" + summary);
           return summary;
       }
   }
   ```
3. `unity command save_scene` — only if the `run_script` result shows
   `renderers changed=` greater than 0. Skip on a no-op (avoids a needless
   domain reload).
4. Single verification pass in ONE shell call:
   `unity command recompile_status && unity command editor_status && unity command get_console_logs --severity Error --limit 20`.
   Done when recompile is `completed` with no errors, editor is `ready`,
   and zero error logs. Confirm the scene-file GUID count matches the
   changed-renderer count:
   `grep -c "b43bb954975ba4322adea89ae96e9470" Assets/MinimalGolf/Scenes/MinimalGolf.unity`.
   Report failures rather than claiming success.
5. ONE cleanup call: delete `Temp/ApplyHandToon.cs`, then
   `git status --short`. Only `Assets/MinimalGolf/Scenes/MinimalGolf.unity`
   should be modified by this skill.

## Gotchas

- **`run_script` compiles with Obsolete warnings as errors:** use
  `FindObjectsByType<T>(FindObjectsInactive)` with NO `FindObjectsSortMode`
  argument. It needs a full class + named static entry (`Type.Method`);
  `return` is fine; keep everything in the one file.
- **The visuals render in the Scene view with no Play mode** (static FBX
  meshes), so a Scene-view screenshot shows the toon shading without
  entering Play mode. Hands auto-hide when untracked — a missing hand in a
  screenshot is a framing/tracking state, not a material failure; trust the
  `TOON-READBACK` lines + GUID grep instead.
