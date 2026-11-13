using UnityEngine;

public class BoundsSetOutlineWidth : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;

    public Renderer render;

    public float outlineWidth = 4f;
    public float referenceHeight = 0.7f;
    private Material mat;

    void Start()
    {
        if (render == null)
        {
            render = GetComponent<Renderer>();
        }

        mat = render.material;

    }

    void Update()
    {
        Bounds bounds = render.bounds;
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = bounds.center + Vector3.Scale(
                bounds.extents, new Vector3(
                (i & 1) == 0 ? -1 : 1,
                (i & 2) == 0 ? -1 : 1,
                (i & 4) == 0 ? -1 : 1
                )
            );
            Vector3 vp = targetCamera.WorldToViewportPoint(corner);
            if (vp.z <= targetCamera.nearClipPlane) return;
            minY = Mathf.Min(minY, vp.y);
            maxY = Mathf.Max(maxY, vp.y);
        }
 
        float heightInViewport = maxY - minY;

        float width = outlineWidth * heightInViewport / referenceHeight;
        width = Mathf.Clamp(width, 0.5f, outlineWidth);
        mat.SetFloat("_OutlineWidth", width);
    }

}
