using UnityEngine;

namespace SnowFactory.Gameplay
{
    /// <summary>
    /// Represents an organic snow drift/mound or an imported 3D snow terrain (such as the Sketchfab Low Poly Snow Island)
    /// with real vertex-level deformation when scooped with a shovel.
    /// Supports both procedural drift generation and custom imported meshes with flat-shaded (low-poly) or smooth normals.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SnowPile : MonoBehaviour
    {
        [Header("Snow Pile Capacity")]
        [SerializeField] private int maxScoops = 12;
        [SerializeField] private float minScaleRatio = 0.28f;

        [Header("Custom Mesh Mode (Sketchfab / Imported 3D Assets)")]
        [SerializeField] private bool useExistingMesh;
        [SerializeField] private Mesh sourceCustomMesh;
        [SerializeField] private bool preserveLowPolyFlatShading = true;
        [SerializeField] private bool shrinkOverallScaleOnScoop = true;

        [Header("Organic Drift Shape (Procedural Mode)")]
        [SerializeField] private int radialSegments = 40;
        [SerializeField] private int ringSegments = 18;
        [SerializeField] private float driftRidgeStrength = 0.22f;
        [SerializeField] private float noiseSeedOffset = 13.7f;

        [Header("Localized Excavation")]
        [SerializeField] private float craterRadius = 0.42f;
        [SerializeField] private float craterDepth = 0.26f;
        [SerializeField] private ParticleSystem scoopBurstParticles;

        private int _remainingScoops;
        private Vector3 _initialScale;
        private Vector3 _initialPosition;
        private Collider _collider;
        private MeshRenderer _renderer;
        private MeshFilter _meshFilter;

        private Mesh _deformableMesh;
        private Vector3[] _vertices;
        private int[] _triangles;
        private Color[] _vertexColors;

        public int RemainingScoops => _remainingScoops;
        public int MaxScoops => maxScoops;
        public bool HasSnow => _remainingScoops > 0;

        private void Awake()
        {
            _remainingScoops = maxScoops;
            _initialScale = transform.localScale;
            _initialPosition = transform.localPosition;
            _collider = GetComponent<Collider>();
            _renderer = GetComponent<MeshRenderer>();
            _meshFilter = GetComponent<MeshFilter>();

            InitializeDeformableMesh();
        }

        public void ConfigurePile(int scoops, float seedOffset, ParticleSystem burstFx)
        {
            useExistingMesh = false;
            shrinkOverallScaleOnScoop = true;
            maxScoops = Mathf.Max(1, scoops);
            _remainingScoops = maxScoops;
            noiseSeedOffset = seedOffset;
            scoopBurstParticles = burstFx;
        }

        public void ConfigureCustomMeshTerrain(
            Mesh customMeshAsset,
            int scoops,
            float worldCraterRadius,
            float worldCraterDepth,
            bool flatShadedLowPoly,
            bool shrinkWholeTerrain,
            ParticleSystem burstFx)
        {
            useExistingMesh = true;
            sourceCustomMesh = customMeshAsset;
            preserveLowPolyFlatShading = flatShadedLowPoly;
            shrinkOverallScaleOnScoop = shrinkWholeTerrain;
            maxScoops = Mathf.Max(1, scoops);
            _remainingScoops = maxScoops;
            craterRadius = worldCraterRadius;
            craterDepth = worldCraterDepth;
            scoopBurstParticles = burstFx;
        }

        public void InitializeDeformableMesh()
        {
            if (useExistingMesh)
            {
                CloneSourceCustomMesh();
            }
            else
            {
                BuildOrganicSnowDriftMesh();
            }
        }

        private void CloneSourceCustomMesh()
        {
            if (_meshFilter == null)
            {
                _meshFilter = GetComponent<MeshFilter>();
            }

            Mesh baseMesh = sourceCustomMesh != null ? sourceCustomMesh : _meshFilter.sharedMesh;
            if (baseMesh == null) return;

            _deformableMesh = Instantiate(baseMesh);
            _deformableMesh.name = $"Deformable_{baseMesh.name}";

            _vertices = _deformableMesh.vertices;
            _triangles = _deformableMesh.triangles;

            _vertexColors = _deformableMesh.colors;
            if (_vertexColors == null || _vertexColors.Length != _vertices.Length)
            {
                _vertexColors = new Color[_vertices.Length];
                for (int i = 0; i < _vertexColors.Length; i++)
                {
                    _vertexColors[i] = Color.white;
                }
                _deformableMesh.colors = _vertexColors;
            }

            _meshFilter.sharedMesh = _deformableMesh;

            MeshCollider meshCol = GetComponent<MeshCollider>();
            if (meshCol != null)
            {
                meshCol.sharedMesh = null;
                meshCol.sharedMesh = _deformableMesh;
            }
        }

        /// <summary>
        /// Builds a high-density organic snow-drift dome with wind-swept ridges and natural base skirts.
        /// Also runnable in Edit Mode so the Scene and Game views immediately display the organic drift mesh.
        /// </summary>
        public void BuildOrganicSnowDriftMesh()
        {
            if (useExistingMesh)
            {
                CloneSourceCustomMesh();
                return;
            }

            if (_meshFilter == null)
            {
                _meshFilter = GetComponent<MeshFilter>();
            }

            int rings = Mathf.Max(8, ringSegments);
            int segs = Mathf.Max(16, radialSegments);

            int vertCount = 1 + rings * segs;
            _vertices = new Vector3[vertCount];
            _vertexColors = new Color[vertCount];
            Vector2[] uvs = new Vector2[vertCount];

            // Center peak vertex (computed cleanly as limit of t->0 so there is no pinch/nipple)
            float centerNoise = ComputeDriftNoise(0f, 0f);
            float centerWind = Mathf.Sin(noiseSeedOffset) * 0.04f;
            _vertices[0] = new Vector3(0f, 0.42f + centerNoise * driftRidgeStrength + centerWind, 0f);
            _vertexColors[0] = Color.white;
            uvs[0] = new Vector2(0.5f, 0.5f);

            int vIdx = 1;
            for (int r = 1; r <= rings; r++)
            {
                float t = (float)r / rings; // 0 (center) -> 1 (outer skirt)
                for (int s = 0; s < segs; s++)
                {
                    float angle = (s / (float)segs) * Mathf.PI * 2f;
                    float dirX = Mathf.Cos(angle);
                    float dirZ = Mathf.Sin(angle);

                    // Organic undulating footprint radius (not a perfect circle)
                    float edgeUndulation = 1.0f
                        + (0.11f * Mathf.Sin(angle * 2f + noiseSeedOffset)
                        + 0.06f * Mathf.Cos(angle * 3f - noiseSeedOffset * 0.7f)
                        + 0.04f * Mathf.Sin(angle * 5f + noiseSeedOffset * 1.3f)) * t;

                    float radius = 0.5f * t * edgeUndulation;
                    float x = dirX * radius;
                    float z = dirZ * radius;

                    // Broad natural pillow-like snow drift profile (flat-ish rounded top, smooth curved sides)
                    float broadDome = Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(t, 2.3f)), 1.35f);

                    // Wind-swept ridge crest modulation
                    float ridgeNoise = ComputeDriftNoise(x * 2.0f, z * 2.0f);
                    float windCrest = Mathf.Sin(x * 3.8f + z * 1.8f + noiseSeedOffset) * 0.04f * (1f - t * 0.7f);

                    float y = broadDome * 0.42f + (ridgeNoise * driftRidgeStrength + windCrest) * broadDome - 0.03f * t;

                    _vertices[vIdx] = new Vector3(x, y, z);
                    _vertexColors[vIdx] = Color.white;
                    uvs[vIdx] = new Vector2(0.5f + dirX * t * 0.5f, 0.5f + dirZ * t * 0.5f);
                    vIdx++;
                }
            }

            int triCount = segs + (rings - 1) * segs * 2;
            _triangles = new int[triCount * 3];
            int tIdx = 0;

            // Inner ring triangles connected to center vertex 0
            for (int s = 0; s < segs; s++)
            {
                int nextS = (s + 1) % segs;
                _triangles[tIdx++] = 0;
                _triangles[tIdx++] = 1 + nextS;
                _triangles[tIdx++] = 1 + s;
            }

            // Outer ring quads
            for (int r = 1; r < rings; r++)
            {
                int ringStart = 1 + (r - 1) * segs;
                int nextRingStart = 1 + r * segs;

                for (int s = 0; s < segs; s++)
                {
                    int nextS = (s + 1) % segs;

                    int v00 = ringStart + s;
                    int v01 = ringStart + nextS;
                    int v10 = nextRingStart + s;
                    int v11 = nextRingStart + nextS;

                    _triangles[tIdx++] = v00;
                    _triangles[tIdx++] = v01;
                    _triangles[tIdx++] = v10;

                    _triangles[tIdx++] = v01;
                    _triangles[tIdx++] = v11;
                    _triangles[tIdx++] = v10;
                }
            }

            _deformableMesh = new Mesh
            {
                name = $"OrganicSnowDrift_{gameObject.name}"
            };
            _deformableMesh.vertices = _vertices;
            _deformableMesh.triangles = _triangles;
            _deformableMesh.uv = uvs;
            _deformableMesh.colors = _vertexColors;
            _deformableMesh.RecalculateNormals();
            _deformableMesh.RecalculateBounds();

            _meshFilter.sharedMesh = _deformableMesh;

            MeshCollider meshCol = GetComponent<MeshCollider>();
            if (meshCol != null)
            {
                meshCol.sharedMesh = null;
                meshCol.sharedMesh = _deformableMesh;
            }
        }

        private float ComputeDriftNoise(float x, float z)
        {
            float n1 = Mathf.PerlinNoise(x * 1.6f + noiseSeedOffset, z * 1.6f + noiseSeedOffset) - 0.5f;
            float n2 = Mathf.PerlinNoise(x * 3.4f - noiseSeedOffset, z * 3.4f + noiseSeedOffset * 0.5f) - 0.5f;
            return n1 * 0.7f + n2 * 0.3f;
        }

        public bool TryScoop()
        {
            return TryScoopAtPoint(transform.position + Vector3.up * 0.4f, 1.0f);
        }

        public void RefillPile()
        {
            _remainingScoops = maxScoops;
            transform.localScale = _initialScale;
            transform.localPosition = _initialPosition;

            if (_renderer != null) _renderer.enabled = true;
            if (_collider != null) _collider.enabled = true;

            InitializeDeformableMesh();
        }

        public bool TryScoopAtPoint(Vector3 worldHitPoint, float radiusMultiplier = 1.0f, bool ignoreDepletion = false)
        {
            if (!ignoreDepletion && _remainingScoops <= 0)
            {
                return false;
            }

            if (!ignoreDepletion)
            {
                _remainingScoops--;
            }

            // Play snow powder burst at the exact shovel impact point
            if (scoopBurstParticles != null)
            {
                scoopBurstParticles.transform.position = worldHitPoint + Vector3.up * 0.08f;
                scoopBurstParticles.Play();
            }

            CarveCraterAtWorldPoint(worldHitPoint, radiusMultiplier);

            if (shrinkOverallScaleOnScoop && !ignoreDepletion)
            {
                UpdateOverallVolumeScale();
            }
            return true;
        }

        private void CarveCraterAtWorldPoint(Vector3 worldHitPoint, float radiusMultiplier)
        {
            if (_deformableMesh == null || _vertices == null) return;

            if (useExistingMesh)
            {
                // World-space excavation for arbitrary rotated/scaled 3D assets (e.g. Blender Z-up Sketchfab island)
                float worldRadius = craterRadius * Mathf.Clamp(radiusMultiplier, 0.8f, 2.2f);
                float sqrWorldRadius = worldRadius * worldRadius;
                Matrix4x4 localToWorld = transform.localToWorldMatrix;
                Matrix4x4 worldToLocal = transform.worldToLocalMatrix;

                for (int i = 0; i < _vertices.Length; i++)
                {
                    Vector3 vWorld = localToWorld.MultiplyPoint3x4(_vertices[i]);
                    float dx = vWorld.x - worldHitPoint.x;
                    float dz = vWorld.z - worldHitPoint.z;
                    float dy = vWorld.y - worldHitPoint.y;

                    // Horizontal distance + vertical tolerance so only the surface layer near the shovel hit is carved
                    float horizDistSq = dx * dx + dz * dz;
                    if (horizDistSq < sqrWorldRadius && Mathf.Abs(dy) < worldRadius * 1.4f)
                    {
                        float dist = Mathf.Sqrt(horizDistSq);
                        float normalized = dist / worldRadius;

                        float falloff = Mathf.Cos(normalized * Mathf.PI * 0.5f);
                        float depression = falloff * falloff * craterDepth;
                        float rimPush = Mathf.Sin(normalized * Mathf.PI) * (craterDepth * 0.14f) * (1f - falloff);

                        vWorld.y -= (depression - rimPush);
                        _vertices[i] = worldToLocal.MultiplyPoint3x4(vWorld);

                        float currentAo = _vertexColors[i].r;
                        _vertexColors[i] = new Color(Mathf.Max(0.25f, currentAo - falloff * 0.35f), 1f, 1f, 1f);
                    }
                }
            }
            else
            {
                Vector3 localHit = transform.InverseTransformPoint(worldHitPoint);
                float effectiveRadius = craterRadius * Mathf.Clamp(radiusMultiplier, 0.8f, 2.2f);
                float sqrRadius = effectiveRadius * effectiveRadius;

                for (int i = 0; i < _vertices.Length; i++)
                {
                    Vector3 v = _vertices[i];
                    float dx = v.x - localHit.x;
                    float dz = v.z - localHit.z;
                    float distSq = dx * dx + dz * dz;

                    if (distSq < sqrRadius)
                    {
                        float dist = Mathf.Sqrt(distSq);
                        float normalized = dist / effectiveRadius;

                        float falloff = Mathf.Cos(normalized * Mathf.PI * 0.5f);
                        float depression = falloff * falloff * craterDepth;
                        float rimPush = Mathf.Sin(normalized * Mathf.PI) * 0.03f * (1f - falloff);

                        v.y = Mathf.Max(-0.08f, v.y - depression + rimPush);
                        _vertices[i] = v;

                        float currentAo = _vertexColors[i].r;
                        _vertexColors[i] = new Color(Mathf.Max(0.25f, currentAo - falloff * 0.35f), 1f, 1f, 1f);
                    }
                }
            }

            _deformableMesh.vertices = _vertices;
            _deformableMesh.colors = _vertexColors;

            if (useExistingMesh && preserveLowPolyFlatShading && _triangles != null)
            {
                RecalculateFlatNormals(_vertices, _triangles);
            }
            else
            {
                _deformableMesh.RecalculateNormals();
            }

            _deformableMesh.RecalculateBounds();

            MeshCollider meshCol = GetComponent<MeshCollider>();
            if (meshCol != null)
            {
                meshCol.sharedMesh = null;
                meshCol.sharedMesh = _deformableMesh;
            }
        }

        /// <summary>
        /// Computes crisp per-triangle facet normals when vertices are unshared (low-poly art style),
        /// preserving the handcrafted faceted look even after digging craters into the terrain.
        /// </summary>
        private void RecalculateFlatNormals(Vector3[] verts, int[] tris)
        {
            Vector3[] norms = new Vector3[verts.Length];
            int count = tris.Length;
            for (int t = 0; t < count; t += 3)
            {
                int i0 = tris[t];
                int i1 = tris[t + 1];
                int i2 = tris[t + 2];

                Vector3 e1 = verts[i1] - verts[i0];
                Vector3 e2 = verts[i2] - verts[i0];
                Vector3 faceNormal = Vector3.Cross(e1, e2).normalized;

                norms[i0] += faceNormal;
                norms[i1] += faceNormal;
                norms[i2] += faceNormal;
            }

            for (int i = 0; i < norms.Length; i++)
            {
                if (norms[i].sqrMagnitude > 0.0001f)
                {
                    norms[i].Normalize();
                }
                else
                {
                    norms[i] = Vector3.up;
                }
            }

            _deformableMesh.normals = norms;
        }

        private void UpdateOverallVolumeScale()
        {
            if (_remainingScoops <= 0)
            {
                if (_renderer != null) _renderer.enabled = false;
                if (_collider != null) _collider.enabled = false;
                return;
            }

            float t = (float)_remainingScoops / maxScoops;
            float verticalScale = Mathf.Lerp(minScaleRatio, 1f, t);
            float horizontalScale = Mathf.Lerp(0.58f, 1f, t);

            Vector3 newScale = new Vector3(
                _initialScale.x * horizontalScale,
                _initialScale.y * verticalScale,
                _initialScale.z * horizontalScale
            );

            transform.localScale = newScale;

            float heightDiff = (_initialScale.y - newScale.y) * 0.22f;
            transform.localPosition = new Vector3(_initialPosition.x, _initialPosition.y - heightDiff, _initialPosition.z);
        }
    }
}
