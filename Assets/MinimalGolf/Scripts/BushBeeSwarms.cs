using System.Collections.Generic;
using UnityEngine;

namespace MinimalGolf
{
    /// <summary>
    /// Ambient bee swarms: a small cloud of orbiting bees hovering above
    /// every bush (renderers using the bush material). Zero physics cost —
    /// bees follow analytic orbits in Update. Each swarm anchors into its
    /// bush, so runtime anchor re-posing never detaches them.
    /// Edit-mode preview: check debugPreviewSwarms to build the swarms in
    /// the editor (Scene view); uncheck to remove them.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways] // debug preview builds and bobs in edit mode too
    public sealed class BushBeeSwarms : MonoBehaviour
    {
        [Header("Setup")]
        [SerializeField] private Material beeMaterial;
        [SerializeField] private string bushMaterialName = "Bush Green";
        [SerializeField] private int seed = 1234;

        [Header("Bees")]
        [SerializeField] private Color beeColor = new Color(1f, 0.78f, 0.12f, 1f);
        [SerializeField, Range(0.1f, 4f)] private float brightness = 1.25f;
        [SerializeField] private float minSize = 0.004f;
        [SerializeField] private float maxSize = 0.008f;
        [SerializeField, Min(1)] private int minQuantity = 4;
        [SerializeField, Min(1)] private int maxQuantity = 8;

        [Header("Flight")]
        [SerializeField] private float flyRadius = 0.15f;
        [SerializeField] private float hoverHeight = 0.06f;
        [SerializeField] private float flySpeed = 2.2f;

        [Header("Debug")]
        [Tooltip("Edit-mode test: check to build the swarms in the editor (Scene view). Uncheck to remove. In play, checking rebuilds swarms from the current settings.")]
        [SerializeField] private bool debugPreviewSwarms;

        private struct Bee
        {
            public Transform tr;
            public float angle;
            public float radius;
            public float speed;
            public float baseY;
            public float bobPhase;
            public float bobFreq;
            public float bobAmp;
            public Vector3 prevPos;
            public bool placed;
        }

        private static Mesh beeMesh;
        private readonly List<Bee> bees = new List<Bee>();
        private readonly HashSet<GameObject> swarmedBushes = new HashSet<GameObject>();
        private readonly List<GameObject> swarmAnchors = new List<GameObject>();
        private Material runtimeMaterial;
        private float nextBuildAttempt;
        private bool previewBuilt;
        private bool rebuildPending;

        private void OnValidate()
        {
            if (maxSize < minSize)
                maxSize = minSize;
            if (maxQuantity < minQuantity)
                maxQuantity = minQuantity;
            if (flyRadius < 0.01f)
                flyRadius = 0.01f;
            if (!debugPreviewSwarms)
            {
#if UNITY_EDITOR
                // Unchecking in edit mode removes the preview (play-mode
                // rebuilds auto-uncheck, so never clear there).
                if (previewBuilt && !Application.isPlaying && !rebuildPending)
                {
                    rebuildPending = true;
                    UnityEditor.EditorApplication.delayCall += ClearPreviewDeferred;
                }
#endif
                return;
            }
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // Stays checked while the preview is up, so any knob tweak
                // rebuilds it (debounced: one rebuild per editor tick).
                if (!rebuildPending)
                {
                    rebuildPending = true;
                    UnityEditor.EditorApplication.delayCall += PreviewSwarmsDeferred;
                }
                return;
            }
#endif
            debugPreviewSwarms = false;
            PreviewSwarms();
        }

#if UNITY_EDITOR
        private void PreviewSwarmsDeferred()
        {
            rebuildPending = false;
            UnityEditor.EditorApplication.delayCall -= PreviewSwarmsDeferred;
            // Unchecked again before the tick fired: nothing to build.
            if (!Application.isPlaying && !debugPreviewSwarms)
            {
                ClearPreview();
                return;
            }
            PreviewSwarms();
        }

        private void ClearPreviewDeferred()
        {
            rebuildPending = false;
            UnityEditor.EditorApplication.delayCall -= ClearPreviewDeferred;
            ClearPreview();
        }
#endif

        /// <summary>
        /// Builds (or rebuilds) every swarm. In edit mode this is the
        /// debug preview; in play it re-applies the current settings.
        /// </summary>
        private void PreviewSwarms()
        {
            ClearPreview();
            BuildSwarms();
            if (!Application.isPlaying)
            {
                previewBuilt = true;
#if UNITY_EDITOR
                PlaceBees(0f, (float)UnityEditor.EditorApplication.timeSinceStartup);
#endif
                Debug.Log($"[BushBeeSwarms] Edit-mode preview: {bees.Count} bees over {swarmedBushes.Count} bushes. Uncheck to remove.", this);
            }
        }

        /// <summary>
        /// Destroys every swarm anchor (bees go with them) and resets the
        /// build state, using the legal destroy call per mode.
        /// </summary>
        private void ClearPreview()
        {
            foreach (var anchor in swarmAnchors)
            {
                if (anchor == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(anchor);
                else
                    DestroyImmediate(anchor);
            }
            swarmAnchors.Clear();
            bees.Clear();
            swarmedBushes.Clear();
            previewBuilt = false;
        }

        private void Awake()
        {
            if (!Application.isPlaying)
                return;
            BuildSwarms();
        }

        private void OnDestroy()
        {
            if (runtimeMaterial == null)
                return;
            // Plain Destroy is illegal in edit mode (ExecuteAlways objects
            // can be destroyed there); DestroyImmediate is the legal cleanup.
            if (Application.isPlaying)
                Destroy(runtimeMaterial);
            else
                DestroyImmediate(runtimeMaterial);
            runtimeMaterial = null;
        }

        private void BuildSwarms()
        {
            if (beeMaterial == null)
            {
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
                beeMaterial = new Material(unlit);
                Debug.LogWarning("[BushBeeSwarms] No bee material assigned; using runtime Unlit.", this);
            }
            if (runtimeMaterial == null)
            {
                runtimeMaterial = new Material(beeMaterial);
                runtimeMaterial.hideFlags = HideFlags.HideAndDontSave;
                Color tint = new Color(
                    beeColor.r * brightness, beeColor.g * brightness, beeColor.b * brightness, 1f);
                if (runtimeMaterial.HasProperty("_BaseColor"))
                    runtimeMaterial.SetColor("_BaseColor", tint);
                else
                    runtimeMaterial.color = tint;
            }

            Mesh mesh = SharedBeeMesh();
            var bushes = new List<Renderer>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                Material m = r.sharedMaterial;
                if (m != null && m.name.Contains(bushMaterialName))
                    bushes.Add(r);
            }
            bushes.Sort((a, b) => string.Compare(
                a.gameObject.name, b.gameObject.name, System.StringComparison.Ordinal));

            var rng = new System.Random(seed + swarmedBushes.Count * 7919);
            int newBees = 0;
            foreach (var bush in bushes)
            {
                if (!swarmedBushes.Add(bush.gameObject))
                    continue;
                // A checked edit-mode preview can survive a play-mode domain
                // reload untracked: clear its stale anchor before rebuilding
                // so entering play never doubles the visuals.
                var stale = bush.transform.Find("BeeSwarm_" + bush.gameObject.name);
                if (stale != null)
                {
                    if (Application.isPlaying)
                        Destroy(stale.gameObject);
                    else
                        DestroyImmediate(stale.gameObject);
                }
                Bounds bounds = bush.bounds;
                var anchor = new GameObject("BeeSwarm_" + bush.gameObject.name);
                anchor.hideFlags = HideFlags.HideAndDontSave;
                anchor.transform.position = new Vector3(
                    bounds.center.x, bounds.max.y + hoverHeight, bounds.center.z);
                anchor.transform.SetParent(bush.transform, true);
                swarmAnchors.Add(anchor);

                int count = rng.Next(minQuantity, maxQuantity + 1);
                for (int i = 0; i < count; i++)
                {
                    float size = Mathf.Lerp(minSize, maxSize, (float)rng.NextDouble());
                    var go = new GameObject("Bee");
                    go.hideFlags = HideFlags.HideAndDontSave;
                    go.transform.SetParent(anchor.transform, false);
                    var filter = go.AddComponent<MeshFilter>();
                    filter.sharedMesh = mesh;
                    var rend = go.AddComponent<MeshRenderer>();
                    rend.sharedMaterial = runtimeMaterial;
                    rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    rend.receiveShadows = false;
                    go.transform.localScale = new Vector3(size, size * 0.85f, size * 1.5f);
                    float dir = rng.NextDouble() < 0.5 ? -1f : 1f;
                    bees.Add(new Bee
                    {
                        tr = go.transform,
                        angle = (float)(rng.NextDouble() * Mathf.PI * 2f),
                        radius = flyRadius * Mathf.Lerp(0.35f, 1f, (float)rng.NextDouble()),
                        speed = dir * flySpeed * Mathf.Lerp(0.7f, 1.3f, (float)rng.NextDouble()),
                        baseY = hoverHeight * Mathf.Lerp(-0.3f, 0.3f, (float)rng.NextDouble()),
                        bobPhase = (float)(rng.NextDouble() * Mathf.PI * 2f),
                        bobFreq = Mathf.Lerp(2f, 4f, (float)rng.NextDouble()),
                        bobAmp = size * 1.5f,
                        placed = false,
                    });
                    newBees++;
                }
            }
            if (newBees > 0)
                Debug.Log($"[BushBeeSwarms] +{newBees} bees ({bees.Count} total over {swarmedBushes.Count} bushes).", this);
        }

        private void Update()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // Preview placement (Time is frozen in edit mode, so orbits
                // hold still while the bob keeps them visibly alive).
                if (previewBuilt)
                    PlaceBees(0f, (float)UnityEditor.EditorApplication.timeSinceStartup);
                return;
            }
#endif
            if (bees.Count == 0)
            {
                // Bushes may activate after us (level reveal) — retry cheaply.
                if (Time.time >= nextBuildAttempt)
                {
                    nextBuildAttempt = Time.time + 0.5f;
                    BuildSwarms();
                }
                return;
            }
            if (bees[0].tr == null)
            {
                // Transforms went stale (e.g. fast enter-play skipped the
                // reload): drop everything and let the lazy build redo it.
                ClearPreview();
                return;
            }
            PlaceBees(Time.deltaTime, Time.time);
        }

        private void PlaceBees(float dt, float t)
        {
            for (int i = 0; i < bees.Count; i++)
            {
                Bee b = bees[i];
                b.angle += b.speed * dt;
                Vector3 local = new Vector3(
                    Mathf.Cos(b.angle) * b.radius,
                    b.baseY + Mathf.Sin(t * b.bobFreq + b.bobPhase) * b.bobAmp,
                    Mathf.Sin(b.angle) * b.radius);
                b.tr.localPosition = local;
                if (!b.placed)
                {
                    b.prevPos = local;
                    b.placed = true;
                }
                Vector3 delta = local - b.prevPos;
                if (delta.sqrMagnitude > 1e-10f)
                    b.tr.localRotation = Quaternion.LookRotation(delta.normalized);
                b.prevPos = local;
                bees[i] = b;
            }
        }

        private static Mesh SharedBeeMesh()
        {
            if (beeMesh != null)
                return beeMesh;
            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            temp.hideFlags = HideFlags.HideAndDontSave;
            beeMesh = temp.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying)
                Object.Destroy(temp);
            else
                Object.DestroyImmediate(temp);
            return beeMesh;
        }
    }
}
