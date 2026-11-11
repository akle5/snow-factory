using System.Collections.Generic;
using UnityEngine;

namespace SnowFactory.Gameplay
{
    /// <summary>
    /// Builds cohesive, container-conforming, sculpted organic snow meshes that fit strictly inside
    /// 3D container geometry (such as the wheelbarrow tub or shovel scoop pan) without protruding
    /// through walls/floors or looking like disconnected spherical blobs.
    /// </summary>
    public static class ProceduralSnowMeshUtility
    {
        public sealed class ContainerCavityMap
        {
            public readonly int GridResX;
            public readonly int GridResZ;
            public readonly float MinX;
            public readonly float MaxX;
            public readonly float MinZ;
            public readonly float MaxZ;
            public readonly Vector2 CenterXZ;
            public readonly float MinFloorInnerY;
            public readonly float BackRimY;
            public readonly float FrontRimY;

            public readonly bool[] HasSurface;
            public readonly float[] InnerY;
            public readonly float[] OuterY;

            public readonly int AngularSegments;
            public readonly float[] RimRadius;
            public readonly float[] RimEdgeY;

            public ContainerCavityMap(
                int gridResX,
                int gridResZ,
                float minX,
                float maxX,
                float minZ,
                float maxZ,
                Vector2 centerXZ,
                float minFloorInnerY,
                float backRimY,
                float frontRimY,
                bool[] hasSurface,
                float[] innerY,
                float[] outerY,
                int angularSegments,
                float[] rimRadius,
                float[] rimEdgeY)
            {
                GridResX = gridResX;
                GridResZ = gridResZ;
                MinX = minX;
                MaxX = maxX;
                MinZ = minZ;
                MaxZ = maxZ;
                CenterXZ = centerXZ;
                MinFloorInnerY = minFloorInnerY;
                BackRimY = backRimY;
                FrontRimY = frontRimY;
                HasSurface = hasSurface;
                InnerY = innerY;
                OuterY = outerY;
                AngularSegments = angularSegments;
                RimRadius = rimRadius;
                RimEdgeY = rimEdgeY;
            }

            public bool TrySample(float x, float z, out float innerY, out float outerY)
            {
                return TrySampleWithWeight(x, z, 0.001f, out innerY, out outerY);
            }

            public bool TrySampleStrict(float x, float z, out float innerY, out float outerY)
            {
                return TrySampleWithWeight(x, z, 0.72f, out innerY, out outerY);
            }

            private bool TrySampleWithWeight(float x, float z, float minWeight, out float innerY, out float outerY)
            {
                float u = Mathf.InverseLerp(MinX, MaxX, x) * (GridResX - 1);
                float v = Mathf.InverseLerp(MinZ, MaxZ, z) * (GridResZ - 1);

                int x0 = Mathf.Clamp(Mathf.FloorToInt(u), 0, GridResX - 2);
                int z0 = Mathf.Clamp(Mathf.FloorToInt(v), 0, GridResZ - 2);
                int x1 = x0 + 1;
                int z1 = z0 + 1;

                float tx = Mathf.Clamp01(u - x0);
                float tz = Mathf.Clamp01(v - z0);

                int i00 = z0 * GridResX + x0;
                int i10 = z0 * GridResX + x1;
                int i01 = z1 * GridResX + x0;
                int i11 = z1 * GridResX + x1;

                float wSum = 0f;
                float inSum = 0f;
                float outSum = 0f;

                float w00 = (1f - tx) * (1f - tz);
                float w10 = tx * (1f - tz);
                float w01 = (1f - tx) * tz;
                float w11 = tx * tz;

                if (HasSurface[i00]) { wSum += w00; inSum += InnerY[i00] * w00; outSum += OuterY[i00] * w00; }
                if (HasSurface[i10]) { wSum += w10; inSum += InnerY[i10] * w10; outSum += OuterY[i10] * w10; }
                if (HasSurface[i01]) { wSum += w01; inSum += InnerY[i01] * w01; outSum += OuterY[i01] * w01; }
                if (HasSurface[i11]) { wSum += w11; inSum += InnerY[i11] * w11; outSum += OuterY[i11] * w11; }

                if (wSum >= minWeight)
                {
                    innerY = inSum / wSum;
                    outerY = outSum / wSum;
                    return true;
                }

                innerY = MinFloorInnerY;
                outerY = MinFloorInnerY - 0.01f;
                return false;
            }

            public bool TrySampleLocalMaxInnerY(float x, float z, int radiusCells, out float maxInnerY)
            {
                float u = Mathf.InverseLerp(MinX, MaxX, x) * (GridResX - 1);
                float v = Mathf.InverseLerp(MinZ, MaxZ, z) * (GridResZ - 1);
                int cx = Mathf.Clamp(Mathf.RoundToInt(u), 0, GridResX - 1);
                int cz = Mathf.Clamp(Mathf.RoundToInt(v), 0, GridResZ - 1);

                bool found = false;
                float best = float.MinValue;
                int minIx = Mathf.Max(0, cx - radiusCells);
                int maxIx = Mathf.Min(GridResX - 1, cx + radiusCells);
                int minIz = Mathf.Max(0, cz - radiusCells);
                int maxIz = Mathf.Min(GridResZ - 1, cz + radiusCells);

                for (int iz = minIz; iz <= maxIz; iz++)
                {
                    int row = iz * GridResX;
                    for (int ix = minIx; ix <= maxIx; ix++)
                    {
                        int idx = row + ix;
                        if (HasSurface[idx] && InnerY[idx] > best)
                        {
                            best = InnerY[idx];
                            found = true;
                        }
                    }
                }

                maxInnerY = found ? best : MinFloorInnerY;
                return found;
            }
        }

        private const int ClodVariantCount = 4;
        private const int ClodTierCount = 3; // 0: snow, 1: compacted ice, 2: glacier core
        private static readonly Mesh[,] _sharedClodMeshes = new Mesh[ClodVariantCount, ClodTierCount];

        private static readonly Color CarriedLitSnowColor = new Color(0.97f, 0.985f, 1.0f);
        private static readonly Color CarriedSoftShadowSnowColor = new Color(0.54f, 0.72f, 0.91f);
        private static readonly Color CarriedGlacierShadowSnowColor = new Color(0.44f, 0.66f, 0.90f);
        private static readonly Color CarriedSubsurfaceColor = new Color(0.36f, 0.78f, 0.98f);

        public readonly struct CarriedSnowMaterialProfile
        {
            public readonly float MacroScale;
            public readonly float MicroScale;
            public readonly float BumpStrength;
            public readonly float SparkleScale;
            public readonly float SparkleIntensity;
            public readonly float RimIntensity;

            public CarriedSnowMaterialProfile(
                float macroScale,
                float microScale,
                float bumpStrength,
                float sparkleScale,
                float sparkleIntensity,
                float rimIntensity)
            {
                MacroScale = macroScale;
                MicroScale = microScale;
                BumpStrength = bumpStrength;
                SparkleScale = sparkleScale;
                SparkleIntensity = sparkleIntensity;
                RimIntensity = rimIntensity;
            }

            public static readonly CarriedSnowMaterialProfile WheelbarrowMound =
                new CarriedSnowMaterialProfile(1.65f, 8.5f, 0.34f, 44f, 2.6f, 0.48f);

            public static readonly CarriedSnowMaterialProfile ShovelMound =
                new CarriedSnowMaterialProfile(3.2f, 13.0f, 0.32f, 54f, 2.4f, 0.48f);

            public static readonly CarriedSnowMaterialProfile FlyingClod =
                new CarriedSnowMaterialProfile(1.85f, 8.5f, 0.22f, 44f, 2.4f, 0.46f);
        }

        public static Material FindSceneSnowMaterial()
        {
            SnowPile[] piles = Object.FindObjectsByType<SnowPile>(FindObjectsSortMode.None);
            Material fallbackMat = null;
            for (int i = 0; i < piles.Length; i++)
            {
                if (piles[i] == null) continue;
                MeshRenderer mr = piles[i].GetComponent<MeshRenderer>();
                if (mr == null || mr.sharedMaterial == null) continue;
                if (fallbackMat == null) fallbackMat = mr.sharedMaterial;
                if (mr.sharedMaterial.name == "M_SnowFloor")
                {
                    return mr.sharedMaterial;
                }
            }

            return fallbackMat;
        }

        public static Material GetOrCreateCarriedSnowMaterial(
            ref Material targetInstance,
            Material baseMaterial,
            string instanceName,
            in CarriedSnowMaterialProfile profile,
            float avgHardnessMultiplier = 1.0f)
        {
            if (targetInstance == null)
            {
                if (baseMaterial == null) return null;

                targetInstance = new Material(baseMaterial)
                {
                    name = string.IsNullOrEmpty(instanceName) ? "M_CarriedSnowRuntime" : instanceName
                };
                targetInstance.SetFloat("_UseObjectSpace", 1f);
                targetInstance.SetFloat("_MacroScale", profile.MacroScale);
                targetInstance.SetFloat("_MicroScale", profile.MicroScale);
                targetInstance.SetFloat("_BumpStrength", profile.BumpStrength);
                targetInstance.SetFloat("_SparkleScale", profile.SparkleScale);
                targetInstance.SetFloat("_SparkleIntensity", profile.SparkleIntensity);
                targetInstance.SetFloat("_RimIntensity", profile.RimIntensity);
                targetInstance.SetColor("_SubsurfaceColor", CarriedSubsurfaceColor);
                targetInstance.SetFloat("_SubsurfaceWrap", 0.52f);
                targetInstance.SetFloat("_SubsurfaceStrength", 0.65f);
            }

            ApplyCarriedSnowHardnessTint(targetInstance, avgHardnessMultiplier);
            return targetInstance;
        }

        public static void ApplyCarriedSnowHardnessTint(Material material, float avgHardnessMultiplier)
        {
            if (material == null) return;

            float safeMult = (!float.IsNaN(avgHardnessMultiplier) && !float.IsInfinity(avgHardnessMultiplier))
                ? Mathf.Clamp(avgHardnessMultiplier, 1f, 3f)
                : 1f;
            float glacierT = Mathf.Clamp01((safeMult - 1f) * 0.5f);
            material.SetColor("_LitSnowColor", CarriedLitSnowColor);
            material.SetColor(
                "_ShadowSnowColor",
                Color.Lerp(CarriedSoftShadowSnowColor, CarriedGlacierShadowSnowColor, glacierT));
        }

        public static Mesh GetOrCreateSharedSnowClodMesh()
        {
            return GetOrCreateSharedSnowClodMesh(0, 0);
        }

        public static Mesh GetOrCreateSharedSnowClodMesh(int variant, int iceTier)
        {
            int vIdx = ((variant % ClodVariantCount) + ClodVariantCount) % ClodVariantCount;
            int tIdx = Mathf.Clamp(iceTier, 0, ClodTierCount - 1);

            if (_sharedClodMeshes[vIdx, tIdx] != null)
            {
                return _sharedClodMeshes[vIdx, tIdx];
            }

            _sharedClodMeshes[vIdx, tIdx] = BuildOrganicSnowClodMesh(17 + vIdx * 31, tIdx);
            return _sharedClodMeshes[vIdx, tIdx];
        }

        public static int HardnessMultiplierToIceTier(float avgMultiplier)
        {
            if (avgMultiplier >= 2.35f) return 2;
            if (avgMultiplier >= 1.45f) return 1;
            return 0;
        }

        /// <summary>
        /// Builds a reusable organic, irregular lumpy snow/ice clod mesh using a twice-subdivided
        /// icosahedron (completely free of polar pinching) with deterministic organic displacement
        /// and ice/glacier vertex-color strata.
        /// </summary>
        public static Mesh BuildOrganicSnowClodMesh(int seed, int iceTier = 0)
        {
            List<Vector3> verts = new List<Vector3>(162);
            List<int> tris = new List<int>(960);

            // 1. Seed regular icosahedron
            float phi = (1f + Mathf.Sqrt(5f)) * 0.5f;
            float[] coords =
            {
                -1f,  phi,  0f,   1f,  phi,  0f,  -1f, -phi,  0f,   1f, -phi,  0f,
                 0f, -1f,  phi,   0f,  1f,  phi,   0f, -1f, -phi,   0f,  1f, -phi,
                 phi,  0f, -1f,   phi,  0f,  1f,  -phi,  0f, -1f,  -phi,  0f,  1f
            };
            for (int i = 0; i < coords.Length; i += 3)
            {
                verts.Add(new Vector3(coords[i], coords[i + 1], coords[i + 2]).normalized);
            }

            int[] baseTris =
            {
                0,11,5,  0,5,1,   0,1,7,   0,7,10,  0,10,11,
                1,5,9,   5,11,4,  11,10,2, 10,7,6,  7,1,8,
                3,9,4,   3,4,2,   3,2,6,   3,6,8,   3,8,9,
                4,9,5,   2,4,11,  6,2,10,  8,6,7,   9,8,1
            };
            tris.AddRange(baseTris);

            // 2. Subdivide twice with deduplicated edge midpoints (162 verts, 320 uniform triangles)
            for (int sub = 0; sub < 2; sub++)
            {
                Dictionary<long, int> midpointCache = new Dictionary<long, int>(tris.Count);
                List<int> nextTris = new List<int>(tris.Count * 4);

                int GetMidpoint(int a, int b)
                {
                    int lo = a < b ? a : b;
                    int hi = a < b ? b : a;
                    long key = ((long)lo << 32) | (uint)hi;
                    if (midpointCache.TryGetValue(key, out int existing)) return existing;

                    Vector3 mid = ((verts[a] + verts[b]) * 0.5f).normalized;
                    int idx = verts.Count;
                    verts.Add(mid);
                    midpointCache[key] = idx;
                    return idx;
                }

                for (int t = 0; t < tris.Count; t += 3)
                {
                    int v0 = tris[t];
                    int v1 = tris[t + 1];
                    int v2 = tris[t + 2];

                    int a = GetMidpoint(v0, v1);
                    int b = GetMidpoint(v1, v2);
                    int c = GetMidpoint(v2, v0);

                    nextTris.Add(v0); nextTris.Add(a); nextTris.Add(c);
                    nextTris.Add(v1); nextTris.Add(b); nextTris.Add(a);
                    nextTris.Add(v2); nextTris.Add(c); nextTris.Add(b);
                    nextTris.Add(a);  nextTris.Add(b); nextTris.Add(c);
                }

                tris = nextTris;
            }

            // 3. Apply organic clod displacement and smooth stratified snow/ice colors
            Color[] cols = new Color[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 dir = verts[i];
                float n1 = Mathf.PerlinNoise(dir.x * 1.15f + seed * 1.7f, dir.z * 1.15f + dir.y * 0.9f + seed * 0.61f) - 0.5f;
                float n2 = Mathf.PerlinNoise(dir.y * 2.1f - seed * 0.43f, dir.x * 2.0f + dir.z * 1.8f) - 0.5f;
                float n3 = Mathf.PerlinNoise(dir.z * 3.6f + seed * 0.19f, dir.x * 3.4f - dir.y * 2.7f) - 0.5f;

                float radial = 0.50f * (1.0f + n1 * 0.24f + n2 * 0.11f + n3 * 0.04f);
                Vector3 p = dir * radial;
                p.y *= 0.78f;
                verts[i] = p;

                float ao = Mathf.Clamp01(0.76f + n1 * 0.20f + dir.y * 0.16f);

                float layerG = 1.0f;
                if (iceTier == 1)
                {
                    float strata = Mathf.Clamp01(0.55f + dir.y * 0.28f + dir.x * 0.16f + n1 * 0.35f);
                    layerG = Mathf.Lerp(0.45f, 0.94f, strata);
                }
                else if (iceTier >= 2)
                {
                    float strata = Mathf.Clamp01(0.48f + dir.y * 0.34f + dir.x * 0.20f + n1 * 0.38f);
                    layerG = Mathf.Lerp(0.20f, 0.78f, strata);
                }

                cols[i] = new Color(ao, layerG, 1f, 1f);
            }

            Mesh m = new Mesh
            {
                name = "OrganicSnowClodMesh"
            };
            m.SetVertices(verts);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// Samples the isolated metal tub shell of the wheelbarrow mesh into a high-resolution cavity map
        /// in TraySnowRoot local space. Guarantees zero penetration through the sloped walls or bottom.
        /// </summary>
        public static ContainerCavityMap BuildWheelbarrowCavityMap(Mesh bodyMesh, Matrix4x4 bodyToSnowLocal)
        {
            const int gridX = 112;
            const int gridZ = 112;
            const int angSegs = 96;
            const float rimInset = 0.0115f;

            if (bodyMesh == null)
            {
                return BuildFallbackWheelbarrowCavityMap(gridX, gridZ, angSegs);
            }

            List<Vector3> tubTris = ExtractWheelbarrowTubTriangles(bodyMesh, bodyToSnowLocal, out Bounds tubBounds);
            if (tubTris.Count < 9)
            {
                return BuildFallbackWheelbarrowCavityMap(gridX, gridZ, angSegs);
            }

            float pad = 0.01f;
            float minX = tubBounds.min.x - pad;
            float maxX = tubBounds.max.x + pad;
            float minZ = tubBounds.min.z - pad;
            float maxZ = tubBounds.max.z + pad;

            bool[] hasSurface = new bool[gridX * gridZ];
            float[] innerY = new float[gridX * gridZ];
            float[] outerY = new float[gridX * gridZ];

            RasterizeTrianglesIntoHeightGrid(
                tubTris, gridX, gridZ, minX, maxX, minZ, maxZ,
                hasSurface, innerY, outerY);

            Vector2 centerXZ = new Vector2(
                tubBounds.center.x,
                Mathf.Lerp(tubBounds.min.z, tubBounds.max.z, 0.48f));

            float minFloorY = float.MaxValue;
            for (int iz = 0; iz < gridZ; iz++)
            {
                float gz = Mathf.Lerp(minZ, maxZ, iz / (float)(gridZ - 1));
                if (Mathf.Abs(gz - centerXZ.y) > 0.22f) continue;
                for (int ix = 0; ix < gridX; ix++)
                {
                    float gx = Mathf.Lerp(minX, maxX, ix / (float)(gridX - 1));
                    if (Mathf.Abs(gx - centerXZ.x) > 0.16f) continue;
                    int idx = iz * gridX + ix;
                    if (hasSurface[idx] && innerY[idx] < minFloorY)
                    {
                        minFloorY = innerY[idx];
                    }
                }
            }
            if (minFloorY > 10f) minFloorY = 0.388f;

            float[] rawRimRadius = new float[angSegs];
            float[] rawRimEdgeY = new float[angSegs];

            ContainerCavityMap tempProbe = new ContainerCavityMap(
                gridX, gridZ, minX, maxX, minZ, maxZ, centerXZ, minFloorY, 0.54f, 0.61f,
                hasSurface, innerY, outerY, angSegs, rawRimRadius, rawRimEdgeY);

            for (int a = 0; a < angSegs; a++)
            {
                float theta = (a / (float)angSegs) * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));

                float maxValidD = 0.12f;
                for (float d = 0.02f; d <= 0.72f; d += 0.0015f)
                {
                    Vector2 p = centerXZ + dir * d;
                    if (tempProbe.TrySampleStrict(p.x, p.y, out _, out _))
                    {
                        maxValidD = d;
                    }
                    else if (d > 0.18f)
                    {
                        break;
                    }
                }

                float safeR = Mathf.Max(0.10f, maxValidD - rimInset);
                rawRimRadius[a] = safeR;

                Vector2 rimPt = centerXZ + dir * safeR;
                if (tempProbe.TrySample(rimPt.x, rimPt.y, out float rInY, out _))
                {
                    rawRimEdgeY[a] = rInY;
                }
                else
                {
                    rawRimEdgeY[a] = 0.56f;
                }
            }

            float[] rimRadius = new float[angSegs];
            float[] rimEdgeY = new float[angSegs];
            for (int a = 0; a < angSegs; a++)
            {
                int prev = (a + angSegs - 1) % angSegs;
                int next = (a + 1) % angSegs;
                rimRadius[a] = rawRimRadius[prev] * 0.15f + rawRimRadius[a] * 0.70f + rawRimRadius[next] * 0.15f;
                rimEdgeY[a] = rawRimEdgeY[prev] * 0.20f + rawRimEdgeY[a] * 0.60f + rawRimEdgeY[next] * 0.20f;
            }

            float backRimY = 0.547f;
            float frontRimY = 0.626f;
            for (int a = 0; a < angSegs; a++)
            {
                float theta = (a / (float)angSegs) * Mathf.PI * 2f;
                float sinT = Mathf.Sin(theta);
                if (sinT < -0.94f) backRimY = rimEdgeY[a];
                if (sinT > 0.94f) frontRimY = rimEdgeY[a];
            }

            return new ContainerCavityMap(
                gridX, gridZ, minX, maxX, minZ, maxZ, centerXZ, minFloorY, backRimY, frontRimY,
                hasSurface, innerY, outerY, angSegs, rimRadius, rimEdgeY);
        }

        /// <summary>
        /// Generates or updates the cohesive, sculpted organic snow pile mesh inside the wheelbarrow tub.
        /// At low fills it forms a realistic sculpted heap that expands naturally into all corners as load
        /// rises toward 100%, with crisp wind-swept folds and zero outside leakage or rim-bevel artifacts.
        /// </summary>
        public static void UpdateWheelbarrowSnowMesh(
            Mesh targetMesh,
            ContainerCavityMap cavity,
            float fillRatio,
            float avgHardnessMultiplier)
        {
            if (targetMesh == null || cavity == null) return;

            const int radialRings = 22;
            int angSegs = cavity.AngularSegments;

            float clampedFill = Mathf.Clamp01(fillRatio);
            float fillCurve = Mathf.Pow(clampedFill, 0.74f);
            float spreadCurve = Mathf.Pow(clampedFill, 0.60f);
            float glacierT = Mathf.Clamp01((avgHardnessMultiplier - 1f) * 0.5f);

            // 1. Compute radial extent per spoke: compact natural snow heap at 1/24 expanding cleanly to full tub corners by ~14/24..24/24
            float[] spokeMaxRadius = new float[angSegs];
            for (int a = 0; a < angSegs; a++)
            {
                float theta = (a / (float)angSegs) * Mathf.PI * 2f;
                float organicLobe = 1.0f
                    + (0.09f * Mathf.Sin(theta * 2f + 0.8f)
                    + 0.06f * Mathf.Cos(theta * 3f - 1.2f)
                    + 0.04f * Mathf.Sin(theta * 5f + 2.1f)) * (1f - spreadCurve);

                float cov = Mathf.Clamp01(Mathf.Lerp(0.48f, 1.0f, spreadCurve) * organicLobe);
                spokeMaxRadius[a] = cavity.RimRadius[a] * cov;
            }

            // 2. Find exact shoreline radius where snow meets the inner tub wall (at high fill this reaches RimRadius right into every corner)
            float[] contactRadius = new float[angSegs];
            const int marchSteps = 60;

            for (int a = 0; a < angSegs; a++)
            {
                float theta = (a / (float)angSegs) * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));
                float maxR = spokeMaxRadius[a];
                float foundR = maxR * 0.25f;

                for (int s = 0; s <= marchSteps; s++)
                {
                    float u = s / (float)marchSteps;
                    float d = maxR * u;
                    Vector2 p = cavity.CenterXZ + dir * d;

                    if (!cavity.TrySample(p.x, p.y, out float tubInY, out _))
                    {
                        break;
                    }

                    float snowTargetY = EvaluateWheelbarrowSnowTargetY(
                        cavity, p.x, p.y, u, fillCurve, spreadCurve, cavity.RimEdgeY[a]);

                    if (snowTargetY > tubInY + 0.0008f)
                    {
                        foundR = Mathf.Max(foundR, d);
                    }
                    else if (u > 0.25f)
                    {
                        break;
                    }
                }

                contactRadius[a] = Mathf.Min(foundR, cavity.RimRadius[a]);
            }

            float[] smoothedContact = new float[angSegs];
            for (int a = 0; a < angSegs; a++)
            {
                int prev = (a + angSegs - 1) % angSegs;
                int next = (a + 1) % angSegs;
                smoothedContact[a] = Mathf.Min(
                    cavity.RimRadius[a],
                    contactRadius[prev] * 0.20f + contactRadius[a] * 0.60f + contactRadius[next] * 0.20f);
            }

            // 3. Build mesh vertices
            int ringVerts = radialRings - 1;
            int vertCount = 1 + ringVerts * angSegs;
            Vector3[] verts = new Vector3[vertCount];
            Color[] colors = new Color[vertCount];
            float[] reliefVals = new float[vertCount];

            cavity.TrySample(cavity.CenterXZ.x, cavity.CenterXZ.y, out float cInY, out float cOutY);
            float centerTargetY = EvaluateWheelbarrowSnowTargetY(
                cavity,
                cavity.CenterXZ.x,
                cavity.CenterXZ.y,
                0f,
                fillCurve,
                spreadCurve,
                (cavity.BackRimY + cavity.FrontRimY) * 0.5f,
                out float centerRelief);
            float centerY = Mathf.Max(centerTargetY, Mathf.Max(cInY + 0.003f, cOutY + 0.008f));
            verts[0] = new Vector3(cavity.CenterXZ.x, centerY, cavity.CenterXZ.y);
            reliefVals[0] = centerRelief;

            for (int r = 1; r < radialRings; r++)
            {
                float ringT = r / (float)(radialRings - 1);
                bool isOuterSeamRing = (r == radialRings - 1);

                for (int a = 0; a < angSegs; a++)
                {
                    float theta = (a / (float)angSegs) * Mathf.PI * 2f;
                    Vector2 dir = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));
                    float rMax = Mathf.Max(0.05f, smoothedContact[a]);

                    float d = rMax * ringT;
                    if (isOuterSeamRing)
                    {
                        d = Mathf.Max(0.02f, d - 0.0012f);
                    }

                    Vector2 p = cavity.CenterXZ + dir * d;
                    float u = Mathf.Clamp01(d / rMax);

                    cavity.TrySample(p.x, p.y, out float tubInY, out float tubOutY);
                    float targetY = EvaluateWheelbarrowSnowTargetY(
                        cavity, p.x, p.y, u, fillCurve, spreadCurve, cavity.RimEdgeY[a], out float relief);

                    float finalY;
                    if (isOuterSeamRing)
                    {
                        // Seal cleanly at the wall intersection without creating a sharp 6mm bevel drop,
                        // while staying strictly >= outer tub skin + 7.0mm (zero protrusion guaranteed).
                        float tuckedSeamY = Mathf.Min(targetY, tubInY - 0.0005f);
                        finalY = Mathf.Max(tubOutY + 0.0070f, tuckedSeamY);
                    }
                    else
                    {
                        finalY = Mathf.Max(targetY, Mathf.Max(tubInY + 0.0018f, tubOutY + 0.0080f));
                    }

                    int idx = 1 + (r - 1) * angSegs + a;
                    verts[idx] = new Vector3(p.x, finalY, p.y);
                    reliefVals[idx] = relief;
                }
            }

            // 4. Assign soft valley AO (R) & organic marbled snow/glacier ice strata (G)
            for (int i = 0; i < vertCount; i++)
            {
                Vector3 v = verts[i];
                bool isSeam = i >= 1 + (ringVerts - 1) * angSegs;
                float ao = isSeam
                    ? 0.72f
                    : Mathf.Clamp(0.84f + reliefVals[i] * 3.8f, 0.70f, 1.0f);

                float layerG = 1.0f;
                if (glacierT > 0.02f)
                {
                    // Broad domain-warped drift bands + frosted snowy crests (never circular Perlin spots)
                    float warpX = (Mathf.PerlinNoise(v.x * 1.6f + 2.3f, v.z * 1.6f + 7.1f) - 0.5f) * 0.28f;
                    float warpZ = (Mathf.PerlinNoise(v.x * 1.6f + 8.9f, v.z * 1.6f + 1.4f) - 0.5f) * 0.28f;
                    float wx = (v.x + warpX) * 0.78f + (v.z + warpZ) * 0.62f;
                    float wz = -(v.x + warpX) * 0.62f + (v.z + warpZ) * 0.78f;
                    float broadBand = Mathf.PerlinNoise(wx * 1.55f + 3.1f, wz * 2.45f + 5.7f);
                    float veinContrast = Mathf.Clamp((broadBand - 0.48f) * 1.75f, -0.38f, 0.38f);
                    float crestFrost = Mathf.Clamp(reliefVals[i] * 8.5f, -0.12f, 0.32f);
                    float iceWeight = Mathf.Clamp01(glacierT * 0.78f - crestFrost + veinContrast);
                    layerG = Mathf.Lerp(1.0f, 0.12f, iceWeight);
                }

                colors[i] = new Color(ao, layerG, 1f, 1f);
            }

            // 5. Build triangles (+Y upward winding order)
            int triCount = angSegs + (ringVerts - 1) * angSegs * 2;
            int[] tris = new int[triCount * 3];
            int t = 0;

            for (int a = 0; a < angSegs; a++)
            {
                int aNext = (a + 1) % angSegs;
                tris[t++] = 0;
                tris[t++] = 1 + aNext;
                tris[t++] = 1 + a;
            }

            for (int r = 0; r < ringVerts - 1; r++)
            {
                int ringStart = 1 + r * angSegs;
                int nextStart = 1 + (r + 1) * angSegs;

                for (int a = 0; a < angSegs; a++)
                {
                    int aNext = (a + 1) % angSegs;
                    int v00 = ringStart + a;
                    int v01 = ringStart + aNext;
                    int v10 = nextStart + a;
                    int v11 = nextStart + aNext;

                    tris[t++] = v00;
                    tris[t++] = v01;
                    tris[t++] = v11;

                    tris[t++] = v00;
                    tris[t++] = v11;
                    tris[t++] = v10;
                }
            }

            targetMesh.Clear();
            targetMesh.SetVertices(verts);
            targetMesh.SetColors(colors);
            targetMesh.SetTriangles(tris, 0);
            targetMesh.RecalculateNormals();
            targetMesh.RecalculateBounds();
        }

        private static float EvaluateWheelbarrowSnowTargetY(
            ContainerCavityMap cavity,
            float x,
            float z,
            float normalizedRadius,
            float fillCurve,
            float spreadCurve,
            float spokeRimY)
        {
            return EvaluateWheelbarrowSnowTargetY(cavity, x, z, normalizedRadius, fillCurve, spreadCurve, spokeRimY, out _);
        }

        private static float EvaluateWheelbarrowSnowTargetY(
            ContainerCavityMap cavity,
            float x,
            float z,
            float normalizedRadius,
            float fillCurve,
            float spreadCurve,
            float spokeRimY,
            out float reliefOut)
        {
            float zNorm = Mathf.InverseLerp(cavity.MinZ, cavity.MaxZ, z);
            float planeRimY = Mathf.Lerp(cavity.BackRimY, cavity.FrontRimY, zNorm);
            float blendedRimY = Mathf.Lerp(planeRimY, spokeRimY, normalizedRadius * normalizedRadius);

            // At low fill, base starts right on the floor and rises in a domed snow heap; at high fill,
            // the perimeter reaches all the way up to rimEdgeY - 3mm cleanly into every corner.
            float lowFloorTarget = cavity.MinFloorInnerY + 0.002f;
            float fullRimTarget = blendedRimY - 0.003f;
            float rimLiftT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f, 1.0f, fillCurve));
            float baseSurfaceY = Mathf.Lerp(lowFloorTarget, fullRimTarget, rimLiftT);

            float uSq = normalizedRadius * normalizedRadius;
            float domePower = Mathf.Lerp(1.55f, 1.15f, spreadCurve);
            float crownBell = Mathf.Pow(Mathf.Max(0f, 1f - uSq), domePower);
            float edgeFade = Mathf.Clamp01(1f - Mathf.Pow(normalizedRadius, 3.2f));

            float heapLift = Mathf.Lerp(0.088f, 0.145f, fillCurve) * crownBell;

            // Asymmetric sculpted snowdrifts and soft broad wind-swept crests (matching SnowDepositMound_Main feel)
            float rx1 = x * 0.80f + z * 0.60f;
            float rz1 = -x * 0.60f + z * 0.80f;
            float rx2 = x * 0.42f - z * 0.91f;
            float rz2 = x * 0.91f + z * 0.42f;

            float lobeA = Mathf.Exp(-((x - 0.09f) * (x - 0.09f) / 0.044f + (z - 0.05f) * (z - 0.05f) / 0.062f)) * 0.032f;
            float lobeB = Mathf.Exp(-((x + 0.09f) * (x + 0.09f) / 0.048f + (z + 0.14f) * (z + 0.14f) / 0.058f)) * 0.026f;
            float lobeC = Mathf.Exp(-((x + 0.02f) * (x + 0.02f) / 0.045f + (z - 0.18f) * (z - 0.18f) / 0.052f)) * 0.018f;

            float windWave = Mathf.Sin(rx1 * 3.1f + rz1 * 1.4f + 0.4f) * 0.008f;
            float broadFold = (Mathf.PerlinNoise(rx1 * 1.85f + 3.7f, rz1 * 1.75f + 8.1f) - 0.48f) * 0.024f;
            float secondarySwell = (Mathf.PerlinNoise(rx2 * 3.4f + 12.3f, rz2 * 3.1f + 4.6f) - 0.48f) * 0.011f;

            float reliefFactor = Mathf.Lerp(0.68f, 1.0f, fillCurve) * (0.22f + 0.78f * crownBell) * edgeFade;
            float organicRelief = (lobeA + lobeB + lobeC + windWave + broadFold + secondarySwell) * reliefFactor;

            reliefOut = (windWave + broadFold + secondarySwell) * edgeFade;
            return baseSurfaceY + heapLift + organicRelief;
        }

        /// <summary>
        /// Samples the concave inner pan of the Sketchfab Snow Shovel in ScoopHead local space.
        /// Keeps the snow contour cleanly inside the side cheeks, behind the front cutting lip,
        /// and gracefully curved around the raised rear handle collar.
        /// </summary>
        public static ContainerCavityMap BuildShovelPanCavityMap(Mesh plasticMesh, Matrix4x4 plasticToScoopHead)
        {
            const int gridX = 96;
            const int gridZ = 84;
            const int angSegs = 72;

            const float minX = -0.168f;
            const float maxX = 0.168f;
            const float minZ = -0.055f;
            const float maxZ = 0.154f;
            Vector2 centerXZ = new Vector2(0f, 0.076f);

            if (plasticMesh == null)
            {
                return BuildFallbackShovelPanCavityMap(gridX, gridZ, angSegs, minX, maxX, minZ, maxZ, centerXZ);
            }

            Vector3[] srcVerts = plasticMesh.vertices;
            int[] srcTris = plasticMesh.triangles;
            List<Vector3> panTris = new List<Vector3>(srcTris.Length / 2);

            for (int t = 0; t < srcTris.Length; t += 3)
            {
                Vector3 a = plasticToScoopHead.MultiplyPoint3x4(srcVerts[srcTris[t]]);
                Vector3 b = plasticToScoopHead.MultiplyPoint3x4(srcVerts[srcTris[t + 1]]);
                Vector3 c = plasticToScoopHead.MultiplyPoint3x4(srcVerts[srcTris[t + 2]]);

                float triMaxZ = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                float triMinZ = Mathf.Min(a.z, Mathf.Min(b.z, c.z));
                if (triMaxZ < minZ - 0.02f || triMinZ > maxZ + 0.03f) continue;

                panTris.Add(a);
                panTris.Add(b);
                panTris.Add(c);
            }

            if (panTris.Count < 9)
            {
                return BuildFallbackShovelPanCavityMap(gridX, gridZ, angSegs, minX, maxX, minZ, maxZ, centerXZ);
            }

            bool[] hasSurface = new bool[gridX * gridZ];
            float[] innerY = new float[gridX * gridZ];
            float[] outerY = new float[gridX * gridZ];
            RasterizeTrianglesIntoHeightGrid(
                panTris, gridX, gridZ, minX, maxX, minZ, maxZ,
                hasSurface, innerY, outerY);

            float[] rimRadius = new float[angSegs];
            float[] rimEdgeY = new float[angSegs];
            ContainerCavityMap probe = new ContainerCavityMap(
                gridX, gridZ, minX, maxX, minZ, maxZ, centerXZ, -0.025f, -0.010f, 0.080f,
                hasSurface, innerY, outerY, angSegs, rimRadius, rimEdgeY);

            for (int a = 0; a < angSegs; a++)
            {
                float theta = (a / (float)angSegs) * Mathf.PI * 2f;
                float cosT = Mathf.Cos(theta);
                float sinT = Mathf.Sin(theta);

                // Rounded scoop pocket inside side walls (x in [-0.155, +0.155]),
                // reaching cleanly to front lip (z <= +0.144) and wrapping smoothly around
                // the raised rear handle collar without cutting across rear stiffening ribs.
                float halfX = 0.155f;
                float halfZ = sinT >= 0f ? 0.069f : 0.053f;
                float denom = Mathf.Pow(
                    Mathf.Pow(Mathf.Abs(cosT) / halfX, 2.8f) + Mathf.Pow(Mathf.Abs(sinT) / halfZ, 2.8f),
                    1f / 2.8f);
                float r = denom > 0.001f ? 1f / denom : 0.07f;

                if (sinT < -0.22f)
                {
                    float centerAlign = Mathf.Clamp01(1f - Mathf.Abs(cosT) * 2.2f);
                    float rearDepth = Mathf.InverseLerp(-0.22f, -0.95f, sinT);
                    r *= Mathf.Lerp(1.0f, 0.72f, centerAlign * rearDepth);
                }

                rimRadius[a] = r;
                Vector2 rp = centerXZ + new Vector2(cosT, sinT) * r;
                rimEdgeY[a] = probe.TrySample(rp.x, rp.y, out float inY, out _) ? inY : 0.02f;
            }

            return probe;
        }

        /// <summary>
        /// Builds or updates one cohesive, sculpted powdery snow mound inside the shovel scoop pan,
        /// riding cleanly above internal plastic ribs and handle collar (zero clipping notches)
        /// while never piercing the underside of the plastic blade.
        /// </summary>
        public static void UpdateShovelSnowMesh(
            Mesh targetMesh,
            ContainerCavityMap pan,
            float fillRatio,
            float avgHardnessMultiplier)
        {
            if (targetMesh == null || pan == null) return;

            const int radialRings = 16;
            int angSegs = pan.AngularSegments;
            float clampedFill = Mathf.Clamp01(fillRatio);
            float fillCurve = Mathf.Pow(clampedFill, 0.72f);
            float glacierT = Mathf.Clamp01((avgHardnessMultiplier - 1f) * 0.5f);

            // Coverage expands smoothly from 56% (clean center snow heap inside ribs) to 100% (full scoop)
            float coverageScale = Mathf.Lerp(0.56f, 1.0f, fillCurve);

            int ringVerts = radialRings - 1;
            int vertCount = 1 + ringVerts * angSegs;
            Vector3[] verts = new Vector3[vertCount];
            Color[] colors = new Color[vertCount];
            float[] reliefVals = new float[vertCount];

            pan.TrySample(pan.CenterXZ.x, pan.CenterXZ.y, out float cInY, out float cOutY);
            pan.TrySampleLocalMaxInnerY(pan.CenterXZ.x, pan.CenterXZ.y, 1, out float cMaxInY);
            float centerTargetY = EvaluateShovelSnowTargetY(
                pan.CenterXZ.x, pan.CenterXZ.y, 0f, fillCurve, cInY, out float cRel);
            verts[0] = new Vector3(
                pan.CenterXZ.x,
                Mathf.Max(centerTargetY, Mathf.Max(cMaxInY + 0.0030f, cOutY + 0.0055f)),
                pan.CenterXZ.y);
            reliefVals[0] = cRel;

            for (int r = 1; r < radialRings; r++)
            {
                float ringT = r / (float)(radialRings - 1);
                bool isSeam = (r == radialRings - 1);

                for (int a = 0; a < angSegs; a++)
                {
                    float theta = (a / (float)angSegs) * Mathf.PI * 2f;
                    Vector2 dir = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));

                    // Smooth, gentle outline variation without three-lobed cloverleaf scallops
                    float organicRim = 1.0f
                        + (0.018f * Mathf.Sin(theta * 2f + 0.4f) + 0.014f * Mathf.Cos(theta - 0.6f))
                        * (1f - fillCurve * 0.7f);

                    float d = Mathf.Min(pan.RimRadius[a], pan.RimRadius[a] * coverageScale * organicRim) * ringT;
                    Vector2 p = pan.CenterXZ + dir * d;

                    pan.TrySample(p.x, p.y, out float panInY, out float panOutY);
                    pan.TrySampleLocalMaxInnerY(p.x, p.y, isSeam ? 1 : 2, out float localMaxInY);
                    float targetY = EvaluateShovelSnowTargetY(p.x, p.y, ringT, fillCurve, panInY, out float rel);

                    float finalY;
                    if (isSeam)
                    {
                        // Anchor outer perimeter smoothly onto high-res plastic surface (clear of underside)
                        finalY = Mathf.Max(panOutY + 0.0040f, panInY + 0.0014f);
                    }
                    else
                    {
                        // Keep interior dome safely above any adjacent plastic rib peak so no triangle edge clips
                        float ribClearFloor = Mathf.Max(panInY + 0.0030f, localMaxInY + 0.0020f);
                        finalY = Mathf.Max(targetY, Mathf.Max(ribClearFloor, panOutY + 0.0055f));
                    }

                    int idx = 1 + (r - 1) * angSegs + a;
                    verts[idx] = new Vector3(p.x, finalY, p.y);
                    reliefVals[idx] = rel;
                }
            }

            for (int i = 0; i < vertCount; i++)
            {
                Vector3 v = verts[i];
                bool isSeam = i >= 1 + (ringVerts - 1) * angSegs;
                float ao = isSeam
                    ? 0.78f
                    : Mathf.Clamp(0.86f + reliefVals[i] * 7.5f, 0.74f, 1.0f);

                float layerG = 1.0f;
                if (glacierT > 0.02f)
                {
                    float rx = v.x * 0.80f + v.z * 0.60f;
                    float rz = -v.x * 0.60f + v.z * 0.80f;
                    float marble = Mathf.PerlinNoise(rx * 5.8f + 2.1f, rz * 8.2f + 7.4f);
                    float veinContrast = Mathf.Clamp((marble - 0.48f) * 1.45f, -0.32f, 0.32f);
                    float crestFrost = Mathf.Clamp(reliefVals[i] * 14.0f, -0.10f, 0.28f);
                    float iceWeight = Mathf.Clamp01(glacierT * 0.70f - crestFrost + veinContrast);
                    layerG = Mathf.Lerp(1.0f, 0.22f, iceWeight);
                }

                colors[i] = new Color(ao, layerG, 1f, 1f);
            }

            int triCount = angSegs + (ringVerts - 1) * angSegs * 2;
            int[] tris = new int[triCount * 3];
            int t = 0;
            for (int a = 0; a < angSegs; a++)
            {
                int aNext = (a + 1) % angSegs;
                tris[t++] = 0;
                tris[t++] = 1 + aNext;
                tris[t++] = 1 + a;
            }
            for (int r = 0; r < ringVerts - 1; r++)
            {
                int ringStart = 1 + r * angSegs;
                int nextStart = 1 + (r + 1) * angSegs;
                for (int a = 0; a < angSegs; a++)
                {
                    int aNext = (a + 1) % angSegs;
                    int v00 = ringStart + a;
                    int v01 = ringStart + aNext;
                    int v10 = nextStart + a;
                    int v11 = nextStart + aNext;

                    tris[t++] = v00;
                    tris[t++] = v01;
                    tris[t++] = v11;

                    tris[t++] = v00;
                    tris[t++] = v11;
                    tris[t++] = v10;
                }
            }

            targetMesh.Clear();
            targetMesh.SetVertices(verts);
            targetMesh.SetColors(colors);
            targetMesh.SetTriangles(tris, 0);
            targetMesh.RecalculateNormals();
            targetMesh.RecalculateBounds();
        }

        private static float EvaluateShovelSnowTargetY(
            float x,
            float z,
            float u,
            float fillCurve,
            float panInY,
            out float reliefOut)
        {
            // Smooth concave pan baseline following the scoop ramp
            float zSlope = Mathf.InverseLerp(-0.01f, 0.145f, z);
            float smoothRamp = Mathf.Lerp(-0.010f, 0.084f, zSlope * zSlope * (0.4f + 0.6f * zSlope));
            float basePanY = Mathf.Lerp(panInY, Mathf.Max(smoothRamp, panInY), 0.45f);

            // Plush rounded snow pillow maintaining generous shoulder thickness clear of internal ribs,
            // tapering cleanly to the perimeter seam at u=1 (no vertical slab wall, no rib cutouts).
            float crownBell = Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(u, 2.25f)), 0.82f);
            float edgeFade = Mathf.Clamp01(1f - Mathf.Pow(u, 3.0f));
            float moundHeight = Mathf.Lerp(0.044f, 0.076f, fillCurve) * crownBell;

            float rx1 = x * 0.80f + z * 0.60f;
            float rz1 = -x * 0.60f + z * 0.80f;
            float windRidge = Mathf.Sin(rx1 * 12f + rz1 * 6f) * 0.0032f;
            float fold = (Mathf.PerlinNoise(rx1 * 7.5f + 4.3f, rz1 * 7.5f + 1.8f) - 0.5f) * 0.0085f;
            float ridge = (Mathf.PerlinNoise(-rx1 * 14f + 8.1f, rz1 * 14f + 6.2f) - 0.5f) * 0.0040f;
            float relief = (windRidge + fold + ridge) * (0.25f + 0.75f * crownBell) * edgeFade;

            reliefOut = relief;
            return basePanY + moundHeight + relief;
        }

        private static List<Vector3> ExtractWheelbarrowTubTriangles(Mesh bodyMesh, Matrix4x4 bodyToSnowLocal, out Bounds tubBounds)
        {
            Vector3[] verts = bodyMesh.vertices;
            int[] tris = bodyMesh.triangles;

            Dictionary<Vector3Int, int> spatialBuckets = new Dictionary<Vector3Int, int>(verts.Length);
            int[] parent = new int[verts.Length];
            for (int i = 0; i < verts.Length; i++) parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            void Union(int a, int b)
            {
                int ra = Find(a);
                int rb = Find(b);
                if (ra != rb) parent[rb] = ra;
            }

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                Vector3Int key = new Vector3Int(
                    Mathf.RoundToInt(v.x * 2000f),
                    Mathf.RoundToInt(v.y * 2000f),
                    Mathf.RoundToInt(v.z * 2000f));
                if (spatialBuckets.TryGetValue(key, out int existing))
                {
                    Union(existing, i);
                }
                else
                {
                    spatialBuckets[key] = i;
                }
            }

            for (int t = 0; t < tris.Length; t += 3)
            {
                Union(tris[t], tris[t + 1]);
                Union(tris[t], tris[t + 2]);
            }

            Dictionary<int, int> compCounts = new Dictionary<int, int>();
            Dictionary<int, Bounds> compBounds = new Dictionary<int, Bounds>();
            for (int i = 0; i < verts.Length; i++)
            {
                int root = Find(i);
                Vector3 tv = bodyToSnowLocal.MultiplyPoint3x4(verts[i]);
                if (!compCounts.ContainsKey(root))
                {
                    compCounts[root] = 1;
                    compBounds[root] = new Bounds(tv, Vector3.zero);
                }
                else
                {
                    compCounts[root]++;
                    Bounds b = compBounds[root];
                    b.Encapsulate(tv);
                    compBounds[root] = b;
                }
            }

            int selectedRoot = -1;
            int bestCount = 0;
            foreach (var kvp in compBounds)
            {
                int root = kvp.Key;
                Bounds b = kvp.Value;
                int count = compCounts[root];
                if (count >= 200 && b.min.y > 0.30f && b.max.z > 0.32f && count > bestCount)
                {
                    selectedRoot = root;
                    bestCount = count;
                }
            }

            List<Vector3> resultTris = new List<Vector3>(tris.Length / 3);
            tubBounds = new Bounds();
            bool hasBounds = false;

            for (int t = 0; t < tris.Length; t += 3)
            {
                int i0 = tris[t];
                int i1 = tris[t + 1];
                int i2 = tris[t + 2];

                if (selectedRoot >= 0 && Find(i0) != selectedRoot) continue;

                Vector3 v0 = bodyToSnowLocal.MultiplyPoint3x4(verts[i0]);
                Vector3 v1 = bodyToSnowLocal.MultiplyPoint3x4(verts[i1]);
                Vector3 v2 = bodyToSnowLocal.MultiplyPoint3x4(verts[i2]);

                if (selectedRoot < 0)
                {
                    if (v0.y < 0.35f || v1.y < 0.35f || v2.y < 0.35f) continue;
                    if (v0.z < -0.58f || v1.z < -0.58f || v2.z < -0.58f) continue;
                }

                resultTris.Add(v0);
                resultTris.Add(v1);
                resultTris.Add(v2);

                if (!hasBounds)
                {
                    tubBounds = new Bounds(v0, Vector3.zero);
                    hasBounds = true;
                }
                tubBounds.Encapsulate(v0);
                tubBounds.Encapsulate(v1);
                tubBounds.Encapsulate(v2);
            }

            return resultTris;
        }

        private static void RasterizeTrianglesIntoHeightGrid(
            List<Vector3> triVerts,
            int gridX,
            int gridZ,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            bool[] hasSurface,
            float[] innerY,
            float[] outerY)
        {
            for (int i = 0; i < innerY.Length; i++)
            {
                innerY[i] = float.MinValue;
                outerY[i] = float.MaxValue;
            }

            float invSpanX = (gridX - 1) / Mathf.Max(0.001f, maxX - minX);
            float invSpanZ = (gridZ - 1) / Mathf.Max(0.001f, maxZ - minZ);

            for (int t = 0; t < triVerts.Count; t += 3)
            {
                Vector3 v0 = triVerts[t];
                Vector3 v1 = triVerts[t + 1];
                Vector3 v2 = triVerts[t + 2];

                float denom = (v1.z - v2.z) * (v0.x - v2.x) + (v2.x - v1.x) * (v0.z - v2.z);
                if (Mathf.Abs(denom) < 1e-8f) continue;
                float invDenom = 1f / denom;

                float tMinX = Mathf.Min(v0.x, Mathf.Min(v1.x, v2.x));
                float tMaxX = Mathf.Max(v0.x, Mathf.Max(v1.x, v2.x));
                float tMinZ = Mathf.Min(v0.z, Mathf.Min(v1.z, v2.z));
                float tMaxZ = Mathf.Max(v0.z, Mathf.Max(v1.z, v2.z));

                int ix0 = Mathf.Clamp(Mathf.FloorToInt((tMinX - minX) * invSpanX) - 1, 0, gridX - 1);
                int ix1 = Mathf.Clamp(Mathf.CeilToInt((tMaxX - minX) * invSpanX) + 1, 0, gridX - 1);
                int iz0 = Mathf.Clamp(Mathf.FloorToInt((tMinZ - minZ) * invSpanZ) - 1, 0, gridZ - 1);
                int iz1 = Mathf.Clamp(Mathf.CeilToInt((tMaxZ - minZ) * invSpanZ) + 1, 0, gridZ - 1);

                for (int iz = iz0; iz <= iz1; iz++)
                {
                    float gz = Mathf.Lerp(minZ, maxZ, iz / (float)(gridZ - 1));
                    for (int ix = ix0; ix <= ix1; ix++)
                    {
                        float gx = Mathf.Lerp(minX, maxX, ix / (float)(gridX - 1));

                        float w0 = ((v1.z - v2.z) * (gx - v2.x) + (v2.x - v1.x) * (gz - v2.z)) * invDenom;
                        float w1 = ((v2.z - v0.z) * (gx - v2.x) + (v0.x - v2.x) * (gz - v2.z)) * invDenom;
                        float w2 = 1f - w0 - w1;

                        if (w0 >= -0.001f && w1 >= -0.001f && w2 >= -0.001f)
                        {
                            float hitY = w0 * v0.y + w1 * v1.y + w2 * v2.y;
                            int idx = iz * gridX + ix;
                            hasSurface[idx] = true;
                            if (hitY > innerY[idx]) innerY[idx] = hitY;
                            if (hitY < outerY[idx]) outerY[idx] = hitY;
                        }
                    }
                }
            }
        }

        private static ContainerCavityMap BuildFallbackWheelbarrowCavityMap(int gridX, int gridZ, int angSegs)
        {
            const float minX = -0.37f, maxX = 0.405f, minZ = -0.56f, maxZ = 0.43f;
            Vector2 centerXZ = new Vector2(0.0175f, -0.08f);
            bool[] hasSurface = new bool[gridX * gridZ];
            float[] innerY = new float[gridX * gridZ];
            float[] outerY = new float[gridX * gridZ];

            for (int iz = 0; iz < gridZ; iz++)
            {
                float z = Mathf.Lerp(minZ, maxZ, iz / (float)(gridZ - 1));
                for (int ix = 0; ix < gridX; ix++)
                {
                    float x = Mathf.Lerp(minX, maxX, ix / (float)(gridX - 1));
                    float nx = Mathf.Abs(x - centerXZ.x) / 0.35f;
                    float nz = z < 0f ? Mathf.Abs(z + 0.08f) / 0.45f : Mathf.Abs(z + 0.08f) / 0.49f;
                    int idx = iz * gridX + ix;

                    if (nx <= 1f && nz <= 1f)
                    {
                        hasSurface[idx] = true;
                        float wallLift = Mathf.Max(
                            Mathf.Max(0f, (nx - 0.56f) / 0.44f) * 0.19f,
                            z > 0.12f ? (z - 0.12f) * 0.78f : (z < -0.37f ? (-0.37f - z) * 0.95f : 0f));
                        innerY[idx] = 0.388f + wallLift;
                        outerY[idx] = innerY[idx] - 0.012f;
                    }
                }
            }

            float[] rimR = new float[angSegs];
            float[] rimY = new float[angSegs];
            for (int a = 0; a < angSegs; a++)
            {
                float theta = (a / (float)angSegs) * Mathf.PI * 2f;
                float c = Mathf.Cos(theta), s = Mathf.Sin(theta);
                float rx = 0.335f, rz = s > 0f ? 0.46f : 0.43f;
                rimR[a] = 1f / Mathf.Sqrt((c * c) / (rx * rx) + (s * s) / (rz * rz));
                rimY[a] = Mathf.Lerp(0.547f, 0.626f, s * 0.5f + 0.5f);
            }

            return new ContainerCavityMap(
                gridX, gridZ, minX, maxX, minZ, maxZ, centerXZ, 0.388f, 0.547f, 0.626f,
                hasSurface, innerY, outerY, angSegs, rimR, rimY);
        }

        private static ContainerCavityMap BuildFallbackShovelPanCavityMap(
            int gridX, int gridZ, int angSegs,
            float minX, float maxX, float minZ, float maxZ, Vector2 centerXZ)
        {
            bool[] hasSurface = new bool[gridX * gridZ];
            float[] innerY = new float[gridX * gridZ];
            float[] outerY = new float[gridX * gridZ];

            for (int iz = 0; iz < gridZ; iz++)
            {
                float z = Mathf.Lerp(minZ, maxZ, iz / (float)(gridZ - 1));
                float zNorm = Mathf.InverseLerp(minZ, maxZ, z);
                for (int ix = 0; ix < gridX; ix++)
                {
                    int idx = iz * gridX + ix;
                    hasSurface[idx] = true;
                    innerY[idx] = Mathf.Lerp(-0.022f, 0.082f, zNorm);
                    outerY[idx] = innerY[idx] - 0.009f;
                }
            }

            float[] rimR = new float[angSegs];
            float[] rimY = new float[angSegs];
            for (int a = 0; a < angSegs; a++)
            {
                float theta = (a / (float)angSegs) * Mathf.PI * 2f;
                float c = Mathf.Cos(theta), s = Mathf.Sin(theta);
                float rx = 0.150f, rz = s > 0f ? 0.068f : 0.072f;
                rimR[a] = 1f / Mathf.Sqrt((c * c) / (rx * rx) + (s * s) / (rz * rz));
                rimY[a] = Mathf.Lerp(-0.008f, 0.078f, s * 0.5f + 0.5f);
            }

            return new ContainerCavityMap(
                gridX, gridZ, minX, maxX, minZ, maxZ, centerXZ, -0.025f, -0.010f, 0.080f,
                hasSurface, innerY, outerY, angSegs, rimR, rimY);
        }
    }
}
