using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MinimalGolf
{
    /// <summary>
    /// Swaps Interaction SDK hand meshes to the baked cuff variants
    /// (Assets/MinimalGolf/Models/HandCuff_*.asset) which carry a black wrist
    /// cuff in vertex colors. Run after adding OVRHandVisuals to the scene
    /// (see hand-tracking setup), alongside the Hand White Toon material
    /// (which reads vertex colors). Idempotent.
    /// </summary>
    public static class ApplyHandCuff
    {
        [MenuItem("Minimal Golf/Apply Hand Cuff Meshes", false, 20)]
        public static void Apply()
        {
            int swapped = SwapInActiveScene();
            Debug.Log($"[HandCuff] Swapped {swapped} hand renderer(s) to cuff meshes.");
            if (swapped > 0)
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            else
                Debug.Log("[HandCuff] No ISDK hand renderers found — add OVRHandVisuals first.");
        }

        public static int SwapInActiveScene()
        {
            int swapped = 0;
            var renderers = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include);
            foreach (SkinnedMeshRenderer smr in renderers)
            {
                if (smr == null || smr.sharedMesh == null) continue;
                string baseName = smr.sharedMesh.name;
                if (baseName.StartsWith("HandCuff_")) continue; // already swapped
                string bakedPath = "Assets/MinimalGolf/Models/HandCuff_" + baseName + ".asset";
                Mesh baked = AssetDatabase.LoadAssetAtPath<Mesh>(bakedPath);
                if (baked == null) continue; // not an ISDK hand mesh
                Undo.RecordObject(smr, "Apply Hand Cuff Mesh");
                smr.sharedMesh = baked;
                swapped++;
            }
            return swapped;
        }
    }
}
