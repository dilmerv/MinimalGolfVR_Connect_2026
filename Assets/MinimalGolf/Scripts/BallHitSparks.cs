using UnityEngine;

namespace MinimalGolf
{
    /// <summary>
    /// Pooled burst of tiny cubes spawned where the ball strikes level
    /// structures. Zero physics cost (manual integration); each spark owns
    /// a cloned Unlit material so hit-color tinting survives URP SRP batching.
    /// Touch: trigger domain reload to recreate the Pipeline server descriptor.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways] // debug preview animates in edit mode too
    public sealed class BallHitSparks : MonoBehaviour
    {
        public static BallHitSparks Instance { get; private set; }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static Mesh cubeMesh;

        [Header("Setup")]
        [SerializeField] private Material sparkMaterial;
        [SerializeField, Range(16, 128)] private int poolSize = 64;

        [Header("Burst")]
        [SerializeField] private float minSpeed = 1.0f;
        [SerializeField] private float maxSpeed = 2.8f;
        [SerializeField] private float upBias = 0.9f;
        [SerializeField] private float gravity = 7f;
        [SerializeField] private float drag = 1.2f;
        [SerializeField] private float minLife = 0.45f;
        [SerializeField] private float maxLife = 0.7f;
        [SerializeField] private float baseSize = 0.006f;
        [SerializeField] private float minInterval = 0.06f;

        [Header("Debug")]
        [Tooltip("Edit-mode test: check to fire a one-shot preview burst at the ball (samples the nearest level-surface color, else orange). Auto-unchecks. Watch the Scene view.")]
        [SerializeField] private bool debugPreviewBurst;

        private struct Spark
        {
            public GameObject go;
            public Transform tr;
            public Renderer rend;
            public Material mat;
            public Vector3 vel;
            public Vector3 angVel;
            public float life;
            public float maxLife;
            public float size;
            public bool active;
        }

        private Spark[] pool;
        private int cursor;
        private float lastBurstTime;

        private void Awake()
        {
            lastBurstTime = -10f;
            if (Application.isPlaying)
                EnsureReady();
        }

        private void OnEnable()
        {
            // Fast enter-play skips Awake: OnEnable is the reliable hook.
            if (Application.isPlaying)
                EnsureReady();
        }

        private void OnDisable()
        {
            if (Instance == this)
                Instance = null;
        }

        private void OnValidate()
        {
            if (!debugPreviewBurst)
                return;
            debugPreviewBurst = false;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // Pool building uses AddComponent, which is illegal inside
                // OnValidate — defer past it.
                UnityEditor.EditorApplication.delayCall += PreviewBurstDeferred;
                return;
            }
#endif
            PreviewBurst();
        }

#if UNITY_EDITOR
        private void PreviewBurstDeferred()
        {
            UnityEditor.EditorApplication.delayCall -= PreviewBurstDeferred;
            PreviewBurst();
        }
#endif

        /// <summary>
        /// One-shot preview burst at the ball for edit-mode testing.
        /// Color matches the nearest level surface, like a real hit.
        /// </summary>
        private void PreviewBurst()
        {
            EnsureReady();
            Vector3 at = transform.position + Vector3.up * 0.05f;
            Color color = new Color(1f, 0.55f, 0.15f);
            // Live ball in play; level 0's spawn in edit mode (the hierarchy
            // ball is unpositioned/ignored, so never use it directly).
            Rigidbody liveBall = null;
            MiniGolfLevel level0 = null;
            var game = FindAnyObjectByType<MinimalGolfGame>(FindObjectsInactive.Include);
            if (game != null)
            {
                if (Application.isPlaying && game.CurrentLevel != null)
                    liveBall = game.CurrentLevel.ball;
                if (game.AllLevels != null && game.AllLevels.Length > 0)
                    level0 = game.AllLevels[0];
            }
            if (liveBall != null)
                at = liveBall.position + Vector3.up * 0.02f;
            else if (level0 != null && level0.ballSpawn != null)
                at = level0.ballSpawn.position + Vector3.up * 0.02f;
            Collider best = null;
            float bestDist = 0.5f;
            var levels = FindObjectsByType<MiniGolfLevel>(FindObjectsInactive.Exclude);
            foreach (var level in levels)
            {
                foreach (var c in level.GetComponentsInChildren<Collider>(true))
                {
                    float d = Vector3.Distance(c.bounds.center, at);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = c;
                    }
                }
            }
            if (best != null)
                color = GolfBallImpact.ResolveHitColor(best);
            SpawnBurst(at, Vector3.up, color, 1.5f);
            Debug.Log($"[BallHitSparks] Preview burst at {at}, color {color} from {(best != null ? best.gameObject.name : "fallback")}.");
        }

        private void EnsureReady()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (pool != null)
                return;

            if (sparkMaterial == null)
            {
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
                sparkMaterial = new Material(unlit);
                Debug.LogWarning("[BallHitSparks] No spark material assigned; using runtime Unlit.", this);
            }

            // Deferred preview runs outside OnValidate, so edit mode uses
            // DestroyImmediate (plain Destroy is illegal in edit mode).
            for (int c = transform.childCount - 1; c >= 0; c--)
            {
                GameObject child = transform.GetChild(c).gameObject;
                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }

            Mesh mesh = SharedCubeMesh();
            pool = new Spark[Mathf.Clamp(poolSize, 16, 128)];
            for (int i = 0; i < pool.Length; i++)
            {
                GameObject go = new GameObject("Spark");
                go.hideFlags = HideFlags.HideAndDontSave;
                go.transform.SetParent(transform, false);
                go.SetActive(false);
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var rend = go.AddComponent<MeshRenderer>();
                // Per-spark material clone: URP's SRP Batcher ignores
                // MaterialPropertyBlock, so tinting needs real materials.
                // Only active sparks draw (a handful of 12-tri cubes).
                var mat = new Material(sparkMaterial);
                mat.hideFlags = HideFlags.HideAndDontSave;
                rend.sharedMaterial = mat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
                rend.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                rend.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                pool[i] = new Spark { go = go, tr = go.transform, rend = rend, mat = mat };
            }
        }

        public void SpawnBurst(Vector3 position, Vector3 normal, Color color, float strength)
        {
            if (pool == null || Time.time - lastBurstTime < minInterval)
                return;
            lastBurstTime = Time.time;

            int count = Mathf.Clamp(4 + Mathf.RoundToInt(strength * 1.5f), 4, 10);
            float speed = Mathf.Clamp(0.8f + strength * 0.45f, minSpeed, maxSpeed);
            for (int n = 0; n < count; n++)
            {
                int i = cursor;
                cursor = (cursor + 1) % pool.Length;

                Vector3 dir = normal * 1.25f + Random.insideUnitSphere;
                if (dir.sqrMagnitude < 0.01f)
                    dir = normal;
                dir.Normalize();
                pool[i].vel = dir * (speed * Random.Range(0.55f, 1.1f))
                    + Vector3.up * (upBias * Random.Range(0.4f, 1.1f));
                pool[i].angVel = Random.insideUnitSphere * Random.Range(6f, 18f);
                pool[i].maxLife = Random.Range(minLife, maxLife);
                pool[i].life = pool[i].maxLife;
                pool[i].size = baseSize * Random.Range(0.7f, 1.3f);
                pool[i].tr.position = position + normal * 0.006f + Random.insideUnitSphere * 0.003f;
                pool[i].tr.rotation = Random.rotationUniform;
                pool[i].tr.localScale = new Vector3(pool[i].size, pool[i].size, pool[i].size);

                float jitter = Random.Range(0.9f, 1.08f);
                pool[i].mat.SetColor(BaseColorId, new Color(
                    Mathf.Min(color.r * jitter, 1f),
                    Mathf.Min(color.g * jitter, 1f),
                    Mathf.Min(color.b * jitter, 1f), 1f));
                pool[i].active = true;
                pool[i].go.SetActive(true);
            }
        }

        private void Update()
        {
            if (pool == null)
                return;
            float dt = Time.deltaTime;
            float dragFactor = Mathf.Exp(-drag * dt);
            for (int i = 0; i < pool.Length; i++)
            {
                if (!pool[i].active)
                    continue;
                pool[i].life -= dt;
                if (pool[i].life <= 0f)
                {
                    pool[i].active = false;
                    pool[i].go.SetActive(false);
                    continue;
                }
                pool[i].vel.y -= gravity * dt;
                pool[i].vel *= dragFactor;
                pool[i].tr.position += pool[i].vel * dt;
                pool[i].tr.rotation *= Quaternion.Euler(pool[i].angVel * Mathf.Rad2Deg * dt);
                float t = pool[i].life / pool[i].maxLife;
                float s = pool[i].size * Mathf.Min(1f, t / 0.4f);
                pool[i].tr.localScale = new Vector3(s, s, s);
            }
        }

        private static Mesh SharedCubeMesh()
        {
            if (cubeMesh != null)
                return cubeMesh;
            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            temp.hideFlags = HideFlags.HideAndDontSave;
            cubeMesh = temp.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying)
                Object.Destroy(temp);
            else
                Object.DestroyImmediate(temp);
            return cubeMesh;
        }
    }
}
