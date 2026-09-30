using System.Collections.Generic;
using UnityEngine;

namespace SlimeDemo
{
    /// <summary>
    /// CPU scalar field built from the physics particles. This class has no Mesh dependency,
    /// so a future Compute Shader implementation can replace it without changing the solver.
    /// </summary>
    public sealed class OriginDensityField
    {
        private float[] samples = new float[0];
        private float[] smoothingBuffer = new float[0];
        private int resolution;
        private Bounds bounds;
        private Vector3 sampleSpacing;

        public int Resolution => resolution;
        public Bounds Bounds => bounds;

        public void Rebuild(
            IReadOnlyList<OriginSlimeParticle> particles,
            int requestedResolution,
            float padding,
            float metaballRadius,
            int smoothingPasses)
        {
            resolution = Mathf.Max(2, requestedResolution);
            int requiredSampleCount = resolution * resolution * resolution;
            if (samples.Length != requiredSampleCount)
            {
                samples = new float[requiredSampleCount];
                smoothingBuffer = new float[requiredSampleCount];
            }

            if (particles.Count == 0)
            {
                bounds = new Bounds(Vector3.zero, Vector3.one);
                sampleSpacing = bounds.size / (resolution - 1);
                System.Array.Clear(samples, 0, samples.Length);
                return;
            }

            Vector3 minimum = particles[0].position;
            Vector3 maximum = particles[0].position;
            for (int i = 1; i < particles.Count; i++)
            {
                minimum = Vector3.Min(minimum, particles[i].position);
                maximum = Vector3.Max(maximum, particles[i].position);
            }

            // The field boundary must be outside every kernel so the extracted surface closes.
            float effectivePadding = Mathf.Max(padding, metaballRadius * 1.05f);
            minimum -= Vector3.one * effectivePadding;
            maximum += Vector3.one * effectivePadding;
            bounds.SetMinMax(minimum, maximum);
            sampleSpacing = bounds.size / (resolution - 1);

            float safeRadius = Mathf.Max(0.0001f, metaballRadius);
            float radiusSquared = safeRadius * safeRadius;
            Vector3 boundsMinimum = bounds.min;

            for (int z = 0; z < resolution; z++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    for (int x = 0; x < resolution; x++)
                    {
                        Vector3 samplePosition = boundsMinimum + Vector3.Scale(
                            new Vector3(x, y, z), sampleSpacing);
                        float density = 0f;

                        for (int particleIndex = 0; particleIndex < particles.Count; particleIndex++)
                        {
                            Vector3 offset = samplePosition - particles[particleIndex].position;
                            float distanceSquared = offset.sqrMagnitude;
                            if (distanceSquared >= radiusSquared)
                            {
                                continue;
                            }

                            float distance = Mathf.Sqrt(distanceSquared);
                            float q = 1f - distance / safeRadius;
                            density += q * q * q;
                        }

                        samples[ToIndex(x, y, z)] = density;
                    }
                }
            }

            int passCount = Mathf.Clamp(smoothingPasses, 0, 2);
            for (int pass = 0; pass < passCount; pass++)
            {
                SmoothOnce();
            }
        }

        public float GetSample(int x, int y, int z)
        {
            return samples[ToIndex(x, y, z)];
        }

        public Vector3 GetSamplePosition(int x, int y, int z)
        {
            return bounds.min + Vector3.Scale(new Vector3(x, y, z), sampleSpacing);
        }

        public Vector3 GetSurfaceNormal(Vector3 worldPosition)
        {
            Vector3 gridPosition = new Vector3(
                (worldPosition.x - bounds.min.x) / sampleSpacing.x,
                (worldPosition.y - bounds.min.y) / sampleSpacing.y,
                (worldPosition.z - bounds.min.z) / sampleSpacing.z);

            float left = SampleTrilinear(gridPosition - Vector3.right);
            float right = SampleTrilinear(gridPosition + Vector3.right);
            float down = SampleTrilinear(gridPosition - Vector3.up);
            float up = SampleTrilinear(gridPosition + Vector3.up);
            float back = SampleTrilinear(gridPosition - Vector3.forward);
            float front = SampleTrilinear(gridPosition + Vector3.forward);

            // Density grows toward the center, so low minus high points out of the slime.
            Vector3 normal = new Vector3(
                (left - right) / sampleSpacing.x,
                (down - up) / sampleSpacing.y,
                (back - front) / sampleSpacing.z);
            return normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector3.up;
        }

        private void SmoothOnce()
        {
            for (int z = 0; z < resolution; z++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    for (int x = 0; x < resolution; x++)
                    {
                        float weightedDensity = 0f;
                        float totalWeight = 0f;

                        for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
                        {
                            int neighborZ = z + offsetZ;
                            if (neighborZ < 0 || neighborZ >= resolution)
                            {
                                continue;
                            }

                            for (int offsetY = -1; offsetY <= 1; offsetY++)
                            {
                                int neighborY = y + offsetY;
                                if (neighborY < 0 || neighborY >= resolution)
                                {
                                    continue;
                                }

                                for (int offsetX = -1; offsetX <= 1; offsetX++)
                                {
                                    int neighborX = x + offsetX;
                                    if (neighborX < 0 || neighborX >= resolution)
                                    {
                                        continue;
                                    }

                                    float distance = Mathf.Sqrt(
                                        offsetX * offsetX + offsetY * offsetY + offsetZ * offsetZ);
                                    float weight = 1f - 0.5f * distance;
                                    weightedDensity += samples[ToIndex(neighborX, neighborY, neighborZ)] * weight;
                                    totalWeight += weight;
                                }
                            }
                        }

                        smoothingBuffer[ToIndex(x, y, z)] = weightedDensity / totalWeight;
                    }
                }
            }

            float[] previousSamples = samples;
            samples = smoothingBuffer;
            smoothingBuffer = previousSamples;
        }

        private float SampleTrilinear(Vector3 gridPosition)
        {
            float maximumCoordinate = resolution - 1f;
            float x = Mathf.Clamp(gridPosition.x, 0f, maximumCoordinate);
            float y = Mathf.Clamp(gridPosition.y, 0f, maximumCoordinate);
            float z = Mathf.Clamp(gridPosition.z, 0f, maximumCoordinate);

            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            int z0 = Mathf.FloorToInt(z);
            int x1 = Mathf.Min(x0 + 1, resolution - 1);
            int y1 = Mathf.Min(y0 + 1, resolution - 1);
            int z1 = Mathf.Min(z0 + 1, resolution - 1);

            float tx = x - x0;
            float ty = y - y0;
            float tz = z - z0;

            float density00 = Mathf.Lerp(GetSample(x0, y0, z0), GetSample(x1, y0, z0), tx);
            float density10 = Mathf.Lerp(GetSample(x0, y1, z0), GetSample(x1, y1, z0), tx);
            float density01 = Mathf.Lerp(GetSample(x0, y0, z1), GetSample(x1, y0, z1), tx);
            float density11 = Mathf.Lerp(GetSample(x0, y1, z1), GetSample(x1, y1, z1), tx);
            float density0 = Mathf.Lerp(density00, density10, ty);
            float density1 = Mathf.Lerp(density01, density11, ty);
            return Mathf.Lerp(density0, density1, tz);
        }

        private int ToIndex(int x, int y, int z)
        {
            return x + resolution * (y + resolution * z);
        }
    }
}
