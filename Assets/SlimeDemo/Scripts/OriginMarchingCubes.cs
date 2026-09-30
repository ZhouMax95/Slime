using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlimeDemo
{
    /// <summary>
    /// Standard table-driven Marching Cubes surface extraction on the CPU.
    /// Vertices are intentionally not welded in V0.1 to keep the algorithm direct.
    /// </summary>
    public sealed class OriginMarchingCubes
    {
        private static readonly Vector3Int[] CornerOffsets =
        {
            new Vector3Int(0, 0, 0),
            new Vector3Int(1, 0, 0),
            new Vector3Int(1, 1, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(1, 0, 1),
            new Vector3Int(1, 1, 1),
            new Vector3Int(0, 1, 1)
        };

        private static readonly int[,] EdgeCorners =
        {
            { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
            { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
            { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 }
        };

        private readonly Vector3[] cornerPositions = new Vector3[8];
        private readonly float[] cornerDensities = new float[8];
        private readonly Vector3[] edgeVertices = new Vector3[12];
        private readonly Vector3[] edgeNormals = new Vector3[12];
        private readonly List<Vector3> vertices = new List<Vector3>(32768);
        private readonly List<Vector3> normals = new List<Vector3>(32768);
        private readonly List<int> triangles = new List<int>(32768);

        public int VertexCount => vertices.Count;
        public int TriangleCount => triangles.Count / 3;

        public void BuildMesh(OriginDensityField field, float isoLevel, Matrix4x4 worldToLocal, Mesh mesh)
        {
            vertices.Clear();
            normals.Clear();
            triangles.Clear();
            Matrix4x4 worldNormalToLocal = worldToLocal.inverse.transpose;

            int cellCount = field.Resolution - 1;
            for (int z = 0; z < cellCount; z++)
            {
                for (int y = 0; y < cellCount; y++)
                {
                    for (int x = 0; x < cellCount; x++)
                    {
                        PolygonizeCell(field, isoLevel, worldToLocal, worldNormalToLocal, x, y, z);
                    }
                }
            }

            mesh.Clear(false);
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetNormals(normals);
            mesh.RecalculateBounds();
        }

        private void PolygonizeCell(
            OriginDensityField field,
            float isoLevel,
            Matrix4x4 worldToLocal,
            Matrix4x4 worldNormalToLocal,
            int cellX,
            int cellY,
            int cellZ)
        {
            int cubeIndex = 0;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3Int offset = CornerOffsets[corner];
                int x = cellX + offset.x;
                int y = cellY + offset.y;
                int z = cellZ + offset.z;
                cornerPositions[corner] = field.GetSamplePosition(x, y, z);
                cornerDensities[corner] = field.GetSample(x, y, z);

                if (cornerDensities[corner] > isoLevel)
                {
                    cubeIndex |= 1 << corner;
                }
            }

            int edgeMask = OriginMarchingCubesTables.EdgeTable[cubeIndex];
            if (edgeMask == 0)
            {
                return;
            }

            for (int edge = 0; edge < 12; edge++)
            {
                if ((edgeMask & (1 << edge)) == 0)
                {
                    continue;
                }

                int cornerA = EdgeCorners[edge, 0];
                int cornerB = EdgeCorners[edge, 1];
                Vector3 worldVertex = InterpolateEdge(
                    isoLevel,
                    cornerPositions[cornerA],
                    cornerPositions[cornerB],
                    cornerDensities[cornerA],
                    cornerDensities[cornerB]);
                edgeVertices[edge] = worldToLocal.MultiplyPoint3x4(worldVertex);

                Vector3 localNormal = worldNormalToLocal.MultiplyVector(
                    field.GetSurfaceNormal(worldVertex));
                edgeNormals[edge] = localNormal.sqrMagnitude > 0.000001f
                    ? localNormal.normalized
                    : Vector3.up;
            }

            for (int tableIndex = 0; tableIndex < 16; tableIndex += 3)
            {
                int edgeA = OriginMarchingCubesTables.TriangleTable[cubeIndex, tableIndex];
                if (edgeA < 0)
                {
                    break;
                }

                int edgeB = OriginMarchingCubesTables.TriangleTable[cubeIndex, tableIndex + 1];
                int edgeC = OriginMarchingCubesTables.TriangleTable[cubeIndex, tableIndex + 2];
                Vector3 a = edgeVertices[edgeA];
                Vector3 b = edgeVertices[edgeB];
                Vector3 c = edgeVertices[edgeC];
                Vector3 normalA = edgeNormals[edgeA];
                Vector3 normalB = edgeNormals[edgeB];
                Vector3 normalC = edgeNormals[edgeC];

                if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c)
                    || !IsFinite(normalA) || !IsFinite(normalB) || !IsFinite(normalC))
                {
                    continue;
                }

                int firstVertex = vertices.Count;
                vertices.Add(a);
                vertices.Add(c);
                vertices.Add(b);
                normals.Add(normalA);
                normals.Add(normalC);
                normals.Add(normalB);
                triangles.Add(firstVertex);
                triangles.Add(firstVertex + 1);
                triangles.Add(firstVertex + 2);
            }
        }

        private static Vector3 InterpolateEdge(
            float isoLevel,
            Vector3 positionA,
            Vector3 positionB,
            float densityA,
            float densityB)
        {
            const float epsilon = 0.00001f;
            float densityDifference = densityB - densityA;
            if (Mathf.Abs(densityDifference) < epsilon)
            {
                return (positionA + positionB) * 0.5f;
            }

            float t = Mathf.Clamp01((isoLevel - densityA) / densityDifference);
            return Vector3.Lerp(positionA, positionB, t);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
