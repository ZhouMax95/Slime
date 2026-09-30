using UnityEngine;
using UnityEngine.InputSystem;

namespace SlimeDemo
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(OriginSlimeSoftBody))]
    public sealed class OriginSlimePlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField, Min(0f)] private float moveSpeed = 2.75f;
        [SerializeField, Min(0f)] private float moveResponsiveness = 8f;
        [SerializeField, Min(0f)] private float maxMoveAcceleration = 18f;
        [SerializeField, Min(0f)] private float jumpSpeed = 4.5f;
        [SerializeField] private Transform movementCamera;

        private OriginSlimeSoftBody softBody;

        private void Awake()
        {
            softBody = GetComponent<OriginSlimeSoftBody>();
            if (movementCamera == null && Camera.main != null)
            {
                movementCamera = Camera.main.transform;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                softBody.SetDriveTarget(Vector3.zero, moveResponsiveness, maxMoveAcceleration);
                return;
            }

            Vector2 input = new Vector2(
                ReadAxis(keyboard.aKey.isPressed, keyboard.dKey.isPressed),
                ReadAxis(keyboard.sKey.isPressed, keyboard.wKey.isPressed));

            Vector3 direction = GetCameraRelativeDirection(input);
            softBody.SetDriveTarget(direction * moveSpeed, moveResponsiveness, maxMoveAcceleration);

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                softBody.RequestJump(jumpSpeed);
            }

            if (keyboard.rKey.wasPressedThisFrame)
            {
                softBody.Reinitialize();
            }
        }

        private void OnDisable()
        {
            if (softBody != null)
            {
                softBody.SetDriveTarget(Vector3.zero, moveResponsiveness, maxMoveAcceleration);
            }
        }

        private Vector3 GetCameraRelativeDirection(Vector2 input)
        {
            Vector3 forward = movementCamera != null ? movementCamera.forward : Vector3.forward;
            Vector3 right = movementCamera != null ? movementCamera.right : Vector3.right;
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();

            return Vector3.ClampMagnitude(right * input.x + forward * input.y, 1f);
        }

        private static float ReadAxis(bool negativePressed, bool positivePressed)
        {
            return (positivePressed ? 1f : 0f) - (negativePressed ? 1f : 0f);
        }
    }
}
