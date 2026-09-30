using UnityEngine;

namespace SlimeDemo
{
    [DisallowMultipleComponent]
    public sealed class OriginSlimeCameraFollow : MonoBehaviour
    {
        [SerializeField] private OriginSlimeSoftBody target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 3.2f, -6f);
        [SerializeField, Min(0.01f)] private float smoothTime = 0.15f;
        [SerializeField] private float lookHeight = 0.25f;

        private Vector3 followVelocity;

        private void LateUpdate()
        {
            if (target == null)
            {
                target = FindFirstObjectByType<OriginSlimeSoftBody>();
                if (target == null)
                {
                    return;
                }
            }

            Vector3 targetCenter = target.Center;
            transform.position = Vector3.SmoothDamp(
                transform.position,
                targetCenter + offset,
                ref followVelocity,
                smoothTime);
            transform.rotation = Quaternion.LookRotation(targetCenter + Vector3.up * lookHeight - transform.position);
        }
    }
}
