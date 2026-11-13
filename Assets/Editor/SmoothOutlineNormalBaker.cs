using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 最简描边法线烘焙：同位置顶点的法线求和，再写入顶点色 RGB。
// 只处理当前选中对象自身的 Mesh，不遍历整个角色，方便逐个网格观察。
public static class SmoothOutlineNormalBaker
{
    [MenuItem("Tools/描边/烘焙所选网格的平滑法线")]
    private static void BakeSelected()
    {
        // ---------- 1. 找到场景中选中的网格 ----------
        GameObject selected = Selection.activeGameObject;
        if (selected == null || EditorUtility.IsPersistent(selected) || EditorApplication.isPlaying)
        {
            Debug.LogWarning("请在非运行状态下，选中 Hierarchy 中带网格的对象。");
            return;
        }

        SkinnedMeshRenderer skin = selected.GetComponent<SkinnedMeshRenderer>();
        MeshFilter filter = selected.GetComponent<MeshFilter>();
        Mesh source = skin != null ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
        if (source == null)
        {
            Debug.LogWarning("所选对象需要带有 SkinnedMeshRenderer 或 MeshFilter。", selected);
            return;
        }

        // Editor 菜单中可读取导入网格，无须开启模型的 Read/Write。
        // 必须有原始法线和切线，才能把平滑方向转换到切线空间。
        if (source.normals.Length != source.vertexCount || source.tangents.Length != source.vertexCount)
        {
            Debug.LogWarning("网格缺少法线或切线，请检查模型导入设置。", source);
            return;
        }

        // ---------- 2. 选择副本保存位置 ----------
        string path = EditorUtility.SaveFilePanelInProject(
            "保存平滑描边 Mesh 副本", source.name + "_SmoothOutline", "asset", "选择副本保存位置");
        if (string.IsNullOrEmpty(path))
            return;

        // 即使选中了已有文件，也生成新的文件名，保留原资源。
        path = AssetDatabase.GenerateUniqueAssetPath(path);

        // ---------- 3. 只修改副本的顶点色 ----------
        // Instantiate 会保留 UV、原始法线、切线、骨骼权重、BindPose 和 BlendShape。
        Mesh baked = Object.Instantiate(source);
        baked.name = source.name + "_SmoothOutline";
        baked.colors = BakeColors(source);
        AssetDatabase.CreateAsset(baked, path);
        AssetDatabase.SaveAssets();

        // ---------- 4. 替换当前对象的 Mesh，支持 Ctrl+Z 恢复引用 ----------
        Component target = skin != null ? (Component)skin : filter;
        Undo.RecordObject(target, "替换平滑描边 Mesh");
        if (skin != null)
            skin.sharedMesh = baked;
        else
            filter.sharedMesh = baked;

        // 场景中的 Prefab 实例也要记录这次引用修改。
        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        EditorUtility.SetDirty(target);
        EditorGUIUtility.PingObject(baked);
        Debug.Log("描边法线已写入顶点色 RGB，Alpha 保留。副本：" + path, selected);
    }

    private static Color[] BakeColors(Mesh mesh)
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

        // ---------- 1. 按物体空间位置，累加原始法线 ----------
        // 硬边和 UV 接缝会拆分顶点，但它们的位置可能完全相同。
        // Dictionary 按精确坐标分组，此最简版不合并位置仅仅接近的顶点。
        var normalSums = new Dictionary<Vector3, Vector3>();
        for (int i = 0; i < positions.Length; i++)
        {
            normalSums.TryGetValue(positions[i], out Vector3 sum);
            normalSums[positions[i]] = sum + normals[i].normalized;
        }

        // ---------- 2. 共同方向转换到每个顶点各自的切线空间 ----------
        for (int i = 0; i < positions.Length; i++)
        {
            // 求和后归一化，与先求平均再归一化得到的方向相同。
            Vector3 smoothNormalOS = normalSums[positions[i]].normalized;
            Vector3 normal = normals[i].normalized;

            // 重叠的反向面可能让法线相互抵消，此时退回原法线，避免零向量。
            if (smoothNormalOS.sqrMagnitude < 0.000001f)
                smoothNormalOS = normal;

            Vector4 tangentData = tangents[i];
            Vector3 tangent = new Vector3(tangentData.x, tangentData.y, tangentData.z).normalized;
            Vector3 bitangent = Vector3.Cross(normal, tangent) * tangentData.w;

            // TBN 是正交单位基时，点乘即可得到方向在该基下的三个分量。
            // 同位置顶点的 TBN 可能不同，因此写入的 RGB 不一定相同。
            Vector3 smoothNormalTS = new Vector3(
                Vector3.Dot(smoothNormalOS, tangent),
                Vector3.Dot(smoothNormalOS, bitangent),
                Vector3.Dot(smoothNormalOS, normal)).normalized;

            // [-1,1] 编码到 [0,1]；Shader 用 rgb * 2 - 1 解码。
            colors[i] = new Color(
                smoothNormalTS.x * 0.5f + 0.5f,
                smoothNormalTS.y * 0.5f + 0.5f,
                smoothNormalTS.z * 0.5f + 0.5f,
                colors[i].a);
        }

        return colors;
    }
}
