using UnityEngine;

namespace MinimalGolf
{
    public sealed class GolfBallImpact : MonoBehaviour
    {
        private void OnCollisionEnter(Collision collision)
        {
            CameraImpactShake.Instance?.RegisterImpact(collision.relativeVelocity.magnitude);

            if (collision.collider.gameObject.name == "Green Playing Surface")
                return;

            AudioManager.Instance?.PlayCollisionSfx();
            TrySpawnSparks(collision);
        }

        private void TrySpawnSparks(Collision collision)
        {
            if (BallHitSparks.Instance == null || collision.contactCount == 0)
                return;
            float speed = collision.relativeVelocity.magnitude;
            if (speed < 0.6f)
                return;
            ContactPoint contact = collision.GetContact(0);
            if (Mathf.Abs(contact.normal.y) > 0.65f)
                return; // floors, ramps, landings: ground-like contacts spark nothing
            if (collision.collider.GetComponentInParent<MiniGolfLevel>() == null)
                return; // only level structures (excludes club, planet ground)
            Color hitColor = ResolveHitColor(collision.collider);
            BallHitSparks.Instance.SpawnBurst(contact.point, contact.normal, hitColor, speed);
        }

        internal static Color ResolveHitColor(Collider collider)
        {
            Renderer renderer = collider.GetComponent<Renderer>();
            if (renderer == null)
                renderer = collider.GetComponentInParent<Renderer>();
            if (renderer == null)
                return Color.white;
            Material material = renderer.sharedMaterial;
            if (material == null)
                return Color.white;
            Color tint = Color.white;
            if (material.HasProperty("_BaseColor"))
                tint = material.GetColor("_BaseColor");
            else if (material.HasProperty("_Color"))
                tint = material.color;
            // Textured looks (e.g. white tint x colormap) match only when
            // the tint is multiplied by the texture's mean color. Untextured
            // materials multiply by white, i.e. tint unchanged.
            return tint * AverageTextureColor(material.mainTexture);
        }

        /// <summary>
        /// Mean color of a material texture, used as a multiplier over the
        /// tint. Samples a coarse grid so even large textures stay cheap;
        /// null/unreadable textures return white (tint unchanged).
        /// </summary>
        private static Color AverageTextureColor(Texture texture)
        {
            if (texture is not Texture2D tex || !tex.isReadable)
                return Color.white;
            float r = 0f, g = 0f, b = 0f;
            const int steps = 4;
            for (int x = 0; x < steps; x++)
            {
                for (int y = 0; y < steps; y++)
                {
                    Color c = tex.GetPixelBilinear((x + 0.5f) / steps, (y + 0.5f) / steps);
                    r += c.r;
                    g += c.g;
                    b += c.b;
                }
            }
            float inv = 1f / (steps * steps);
            return new Color(r * inv, g * inv, b * inv, 1f);
        }
    }
}
