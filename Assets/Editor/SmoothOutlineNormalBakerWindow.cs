using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 合并三种烘焙方式，只生成 Mesh 资源，由使用者手动替换 Mesh 和选择材质解码方式。
public sealed class SmoothOutlineNormalBakerWindow : EditorWindow
{
    // 数值与 Shader 的 _OutlineNormalEncoding 一致。
    private enum NormalEncoding { RGB = 0, Oct = 1, HemiOct = 2 }

    [SerializeField] private Mesh sourceMesh;
    [SerializeField] private NormalEncoding encoding = NormalEncoding.HemiOct;

    private static readonly string[] EncodingNames = { "RGB（XYZ）", "八面体（RG）", "半八面体（RG）" };

    [MenuItem("Tools/描边/平滑法线烘焙窗口")]
    private static void OpenWindow()
    {
        GetWindow<SmoothOutlineNormalBakerWindow>("描边法线烘焙");
    }

    private void OnEnable()
    {
        minSize = new Vector2(380f, 210f);
        if (sourceMesh == null)
            ReadSelectedMesh();
    }

    private void OnGUI()
    {
        // ---------- 1. 选择输入和编码方式 ----------
        sourceMesh = (Mesh)EditorGUILayout.ObjectField("原始 Mesh", sourceMesh, typeof(Mesh), false);
        if (GUILayout.Button("读取当前选择的 Mesh"))
            ReadSelectedMesh();

        encoding = (NormalEncoding)EditorGUILayout.Popup("编码方式", (int)encoding, EncodingNames);
        EditorGUILayout.HelpBox(
            "可直接拖入 Mesh 子资源，或选中场景中的网格对象后点击读取。\n" +
            "生成后请手动替换 Mesh，并在材质中选择同名的“描边法线编码”。", MessageType.Info);

        if (encoding == NormalEncoding.HemiOct)
            EditorGUILayout.HelpBox("半八面体沿用原工具的同向法线筛选；RGB 和八面体沿用全部同位置法线求和。", MessageType.Info);

        // ---------- 2. 只保存资源，不修改 Renderer 或材质 ----------
        using (new EditorGUI.DisabledScope(sourceMesh == null || EditorApplication.isPlaying))
        {
            if (GUILayout.Button("烘焙并保存 Mesh"))
                BakeMesh();
        }
    }

    private void ReadSelectedMesh()
    {
        if (Selection.activeObject is Mesh selectedMesh)
        {
            sourceMesh = selectedMesh;
            return;
        }

        GameObject selected = Selection.activeGameObject;
        if (selected == null)
            return;

        SkinnedMeshRenderer skin = selected.GetComponent<SkinnedMeshRenderer>();
        MeshFilter filter = selected.GetComponent<MeshFilter>();
        sourceMesh = skin != null ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
    }

    private void BakeMesh()
    {
        // 沿用原工具的数据检查；Editor 菜单中可读导入 Mesh，无须修改 Read/Write。
        if (sourceMesh.vertexCount == 0 || sourceMesh.normals.Length != sourceMesh.vertexCount
            || sourceMesh.tangents.Length != sourceMesh.vertexCount)
        {
            Debug.LogWarning("网格为空或缺少法线、切线，请检查来源 Mesh。", sourceMesh);
            return;
        }

        string meshName = sourceMesh.name + "_SmoothOutline" + encoding;
        string path = EditorUtility.SaveFilePanelInProject(
            "保存描边 Mesh", meshName, "asset", "选择生成资源的位置");
        if (string.IsNullOrEmpty(path))
            return;
        path = AssetDatabase.GenerateUniqueAssetPath(path);

        Color[] colors = BakeColors(sourceMesh, encoding);
        Mesh baked = Instantiate(sourceMesh);
        baked.name = meshName;
        baked.colors = colors;
        AssetDatabase.CreateAsset(baked, path);
        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(baked);
        Debug.Log("已生成 " + EncodingNames[(int)encoding] + " 描边 Mesh，请手动赋值：" + path, baked);
    }

    private static Color[] BakeColors(Mesh mesh, NormalEncoding encoding)
    {
        Vector3[] positions = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector4[] tangents = mesh.tangents;
        Color[] colors = mesh.colors;

        // 原网格没有顶点色时，默认 Alpha = 1。
        if (colors.Length != positions.Length)
        {
            colors = new Color[positions.Length];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = Color.white;
        }

        // ---------- 1. 保留原三种工具各自的法线求和方式 ----------
        var normalSums = new Dictionary<Vector3, Vector3>();
        Vector3[] hemiNormalSums = null;
        if (encoding == NormalEncoding.HemiOct)
        {
            // 半八面体：先按位置归组，再按每个顶点的原始法线筛选同向法线。
            var samePositionNormals = new Dictionary<Vector3, List<Vector3>>();
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 normal = normals[i].normalized;
                if (!samePositionNormals.TryGetValue(positions[i], out List<Vector3> normalsList))
                {
                    normalsList = new List<Vector3>();
                    samePositionNormals[positions[i]] = normalsList;
                }
                normalsList.Add(normal);
            }

            hemiNormalSums = new Vector3[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 originalNormal = normals[i].normalized;
                Vector3 sum = Vector3.zero;
                foreach (Vector3 n in samePositionNormals[positions[i]])
                {
                    if (Vector3.Dot(originalNormal, n) >= 0f)
                        sum += n;
                }
                hemiNormalSums[i] = sum;
            }
        }
        else
        {
            // RGB / 八面体：直接累加全部同位置顶点的法线。
            for (int i = 0; i < positions.Length; i++)
            {
                normalSums.TryGetValue(positions[i], out Vector3 sum);
                normalSums[positions[i]] = sum + normals[i].normalized;
            }
        }

        // ---------- 2. 沿用原来的物体空间 -> 切线空间转换 ----------
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 smoothNormalOS = (encoding == NormalEncoding.HemiOct
                ? hemiNormalSums[i] : normalSums[positions[i]]).normalized;
            Vector3 normal = normals[i].normalized;
            if (smoothNormalOS.sqrMagnitude < 0.000001f)
                smoothNormalOS = normal;

            Vector4 tangentData = tangents[i];
            Vector3 tangent = new Vector3(tangentData.x, tangentData.y, tangentData.z).normalized;
            Vector3 bitangent = Vector3.Cross(normal, tangent) * tangentData.w;
            Vector3 smoothNormalTS = new Vector3(
                Vector3.Dot(smoothNormalOS, tangent),
                Vector3.Dot(smoothNormalOS, bitangent),
                Vector3.Dot(smoothNormalOS, normal)).normalized;

            // ---------- 3. 按所选方式编码，保留原来的 Alpha ----------
            if (encoding == NormalEncoding.RGB)
            {
                colors[i] = new Color(
                    smoothNormalTS.x * 0.5f + 0.5f,
                    smoothNormalTS.y * 0.5f + 0.5f,
                    smoothNormalTS.z * 0.5f + 0.5f,
                    colors[i].a);
            }
            else
            {
                Vector3 encoded = encoding == NormalEncoding.Oct
                    ? OctEncode(smoothNormalTS) : HemiOctEncode(smoothNormalTS);
                colors[i] = new Color(
                    encoded.x * 0.5f + 0.5f,
                    encoded.y * 0.5f + 0.5f,
                    0f,
                    colors[i].a);
            }
        }

        return colors;
    }

    // 保留全八面体工具中的折叠公式。
    private static Vector3 OctEncode(Vector3 n)
    {
        n /= Mathf.Max(Mathf.Abs(n.x) + Mathf.Abs(n.y) + Mathf.Abs(n.z), 1e-5f);
        if (n.z < 0f)
        {
            float x = (1f - Mathf.Abs(n.y)) * (n.x >= 0f ? 1f : -1f);
            float y = (1f - Mathf.Abs(n.x)) * (n.y >= 0f ? 1f : -1f);
            n.x = x;
            n.y = y;
        }
        return n;
    }

    // 保留半八面体工具中的正半球编码公式。
    private static Vector3 HemiOctEncode(Vector3 n)
    {
        float sum = Mathf.Abs(n.x) + Mathf.Abs(n.y) + Mathf.Abs(n.z);
        Vector3 normal = n / Mathf.Max(sum, 0.000001f);
        float x = normal.x + normal.y;
        float y = normal.x - normal.y;
        return new Vector3(x, y, 0f);
    }
}
