using System.Collections;
using UnityEngine;

namespace PBalap.Items
{
    /// <summary>Plays a short local burst for the bomb impact visual.</summary>
    public sealed class BombExplosionEffect : MonoBehaviour
    {
        private Vector3 fullScale;

        public void Play(float duration)
        {
            fullScale = transform.localScale;
            transform.localScale = Vector3.zero;
            StartCoroutine(PlayRoutine(Mathf.Max(0.1f, duration)));
        }

        private IEnumerator PlayRoutine(float duration)
        {
            float elapsed = 0f;
            const float growFraction = 0.2f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                float scaleFactor = progress < growFraction
                    ? Mathf.Lerp(0f, 1.25f, progress / growFraction)
                    : Mathf.Lerp(1.25f, 0f, (progress - growFraction) / (1f - growFraction));

                transform.localScale = fullScale * scaleFactor;
                transform.Rotate(Vector3.up, 240f * Time.deltaTime, Space.Self);
                yield return null;
            }

            Destroy(gameObject);
        }
    }
}
