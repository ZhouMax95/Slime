using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 挂到透视相机：右键旋转、中键上下平移、滚轮缩放。
[RequireComponent(typeof(Camera))]
[DisallowMultipleComponent]
public sealed class CharacterInspectionCamera : MonoBehaviour
{
    [Header("观察目标")]
    [Tooltip("建议指定角色根节点，避免跟随头部或骨骼的细碎运动。")]
    [SerializeField] private Transform target;
    [Tooltip("相对角色位置的世界空间偏移，例如角色脚底向上 1 米。")]
    [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1f, 0f);

    [Header("右键旋转")]
    [Tooltip("鼠标每移动一个像素，旋转多少度。")]
    [SerializeField, Min(0f)] private float rotationSpeed = 0.2f;
    [Tooltip("俯仰角范围，避免相机翻转。")]
    [SerializeField] private Vector2 pitchRange = new Vector2(-75f, 75f);

    [Header("滚轮缩放")]
    [Tooltip("相机到观察中心的距离范围，单位：米。")]
    [SerializeField] private Vector2 distanceRange = new Vector2(0.3f, 8f);
    [Tooltip("每格滚轮的缩放幅度，数值越大缩放越快。")]
    [SerializeField, Min(0f)] private float zoomSpeed = 0.15f;

    [Header("中键上下平移")]
    [Tooltip("平移速度会随观察距离缩放，近看细节时移动得更慢。")]
    [SerializeField, Min(0f)] private float panSpeed = 0.0015f;
    [Tooltip("相对初始观察中心，允许上下移动的距离范围。")]
    [SerializeField] private Vector2 heightRange = new Vector2(-1f, 2f);

    [Header("运动阻尼")]
    [Tooltip("接近目标状态所需的时间。越大越柔和，越小越跟手；松开鼠标后继续平滑收尾。")]
    [SerializeField, Min(0.001f)] private float smoothTime = 0.12f;

    // 目标值只接收输入；当前值通过阻尼逐渐靠近目标值。
    private float desiredYaw, desiredPitch, desiredDistance, desiredHeight;
    private float yaw, pitch, distance, height;
    private float yawVelocity, pitchVelocity, distanceVelocity, heightVelocity;
    private Transform initializedTarget;
    private Camera viewCamera;

    private void OnEnable()
    {
        viewCamera = GetComponent<Camera>();
        initializedTarget = null;
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        // 首次启用或更换目标时，从相机现有位置推导初始视角。
        if (initializedTarget != target)
            InitializeView();

        ReadMouseInput();

        // 使用真实时间，Time.timeScale = 0 时也可以检视角色。
        float deltaTime = Time.unscaledDeltaTime;
        if (deltaTime <= 0f)
            return;

        // ---------- 阻尼：输入停止后，继续靠近目标状态 ----------
        yaw = Mathf.SmoothDampAngle(yaw, desiredYaw, ref yawVelocity,
            smoothTime, Mathf.Infinity, deltaTime);
        pitch = Mathf.SmoothDamp(pitch, desiredPitch, ref pitchVelocity,
            smoothTime, Mathf.Infinity, deltaTime);
        distance = Mathf.SmoothDamp(distance, desiredDistance, ref distanceVelocity,
            smoothTime, Mathf.Infinity, deltaTime);
        height = Mathf.SmoothDamp(height, desiredHeight, ref heightVelocity,
            smoothTime, Mathf.Infinity, deltaTime);

        // ---------- 围绕观察中心定位相机 ----------
        // 平移时，观察中心与相机一起上下移动，保持观察方向不变。
        Vector3 center = target.position + targetOffset + Vector3.up * height;
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 position = center - rotation * Vector3.forward * distance;
        transform.SetPositionAndRotation(position, rotation);
    }

    private void InitializeView()
    {
        Vector3 offset = transform.position - (target.position + targetOffset);
        if (offset.sqrMagnitude < 0.0001f)
            offset = -transform.forward * distanceRange.x;

        // 反推当前相机所在的球面方向，避免固定角度导致开始时跳到角色背面。
        distance = desiredDistance = Mathf.Clamp(offset.magnitude, distanceRange.x, distanceRange.y);
        yaw = desiredYaw = Mathf.Atan2(-offset.x, -offset.z) * Mathf.Rad2Deg;
        pitch = desiredPitch = Mathf.Clamp(
            Mathf.Asin(Mathf.Clamp(offset.normalized.y, -1f, 1f)) * Mathf.Rad2Deg,
            pitchRange.x, pitchRange.y);
        height = desiredHeight = Mathf.Clamp(0f, heightRange.x, heightRange.y);

        yawVelocity = pitchVelocity = distanceVelocity = heightVelocity = 0f;
        initializedTarget = target;
    }

    private void ReadMouseInput()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !Application.isFocused)
            return;

        // 鼠标在相机视口之外或 UI 上时，不接收检视操作。
        if (!viewCamera.pixelRect.Contains(mouse.position.ReadValue()))
            return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        // 鼠标 delta 已经是这一帧的移动量，不再乘 deltaTime。
        Vector2 delta = mouse.delta.ReadValue();
        if (mouse.rightButton.isPressed)
        {
            desiredYaw += delta.x * rotationSpeed;
            desiredPitch = Mathf.Clamp(desiredPitch - delta.y * rotationSpeed, pitchRange.x, pitchRange.y);
        }

        if (mouse.middleButton.isPressed)
        {
            desiredHeight = Mathf.Clamp(
                desiredHeight - delta.y * panSpeed * distance, heightRange.x, heightRange.y);
        }

        // 当前 Input System 默认已统一滚轮刻度；指数缩放保持远近操作感一致。
        float scroll = mouse.scroll.ReadValue().y;
        desiredDistance = Mathf.Clamp(
            desiredDistance * Mathf.Exp(-scroll * zoomSpeed), distanceRange.x, distanceRange.y);
    }

    private void OnValidate()
    {
        // Inspector 中修改范围时，保持上下限合法。
        distanceRange.x = Mathf.Max(0.05f, distanceRange.x);
        distanceRange.y = Mathf.Max(distanceRange.x, distanceRange.y);
        pitchRange.x = Mathf.Clamp(pitchRange.x, -89f, 89f);
        pitchRange.y = Mathf.Clamp(pitchRange.y, pitchRange.x, 89f);
        heightRange.y = Mathf.Max(heightRange.x, heightRange.y);
        smoothTime = Mathf.Max(0.001f, smoothTime);
    }
}
