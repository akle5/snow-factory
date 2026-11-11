using System.Collections.Generic;
using UnityEngine;
using SnowFactory.Core;

namespace SnowFactory.Gameplay
{
    /// <summary>
    /// Drivable first-person wheelbarrow vehicle that stores multiple shovel-loads of snow/ice
    /// (preserving terrain hardness multipliers), tilts dynamically with terrain slopes and dumping,
    /// spins its front wheel by traveled distance, and dumps accumulated snow into the fireplace.
    /// </summary>
    public class WheelbarrowController : MonoBehaviour
    {
        private class DumpSnowChunk
        {
            public Transform ChunkTransform;
            public Vector3 StartWorldPos;
            public Vector3 EndWorldPos;
            public float Elapsed;
            public float Duration;
            public float ArcHeight;
            public FireplaceHearth TargetFireplace;
            public int DeliveredScoops;
            public int DeliveredValueUnits;
        }

        [Header("Hierarchy References")]
        [SerializeField] private Transform trayTiltPivot;
        [SerializeField] private Transform wheelPivot;
        [SerializeField] private Transform traySnowRoot;
        [SerializeField] private Collider bodyCollider;
        [SerializeField] private Material snowMaterial;

        [Header("Capacity & Driving")]
        [SerializeField] private int maxCapacity = 24;
        [SerializeField] private float wheelRadius = 0.197f;
        [SerializeField] private float driverFollowDistance = 1.50f;
        [SerializeField] private float driveLiftPitchDeg = 6.0f;
        [SerializeField] private float fireplaceDumpRange = 5.8f;

        private int _currentSnowLoad;
        private int _accumulatedValueUnits;
        private bool _isDriving;
        private FirstPersonController _driver;
        private ShovelController _driverShovel;

        private float _currentLiftPitch;
        private float _currentSlopePitch;
        private float _dumpAnimTimer;
        private Vector3 _lastWheelWorldPos;
        private Material _traySnowMaterialInstance;

        private readonly List<Transform> _trayClumpVisuals = new List<Transform>();
        private readonly List<DumpSnowChunk> _dumpChunks = new List<DumpSnowChunk>();
        private FireplaceHearth _cachedHearth;

        // 14 organic snow mounds layered inside the tub (X: -0.22..+0.25, Y: 0.36..0.60, Z: -0.20..+0.34)
        private static readonly Vector3[] TrayClumpOffsets =
        {
            // Bottom bed (6 clumps covering the floor of the tub)
            new Vector3(-0.11f, 0.37f, -0.10f),
            new Vector3( 0.13f, 0.37f, -0.10f),
            new Vector3( 0.01f, 0.36f,  0.02f),
            new Vector3(-0.13f, 0.38f,  0.14f),
            new Vector3( 0.15f, 0.38f,  0.14f),
            new Vector3( 0.02f, 0.37f,  0.26f),
            // Middle layer (5 clumps filling the upper rim)
            new Vector3(-0.08f, 0.45f, -0.02f),
            new Vector3( 0.10f, 0.45f, -0.01f),
            new Vector3( 0.01f, 0.46f,  0.11f),
            new Vector3(-0.10f, 0.44f,  0.20f),
            new Vector3( 0.11f, 0.44f,  0.21f),
            // Top heaped crest (3 clumps rising above the tub rim)
            new Vector3(-0.05f, 0.54f,  0.05f),
            new Vector3( 0.07f, 0.54f,  0.08f),
            new Vector3( 0.01f, 0.58f,  0.16f)
        };

        private static readonly Vector3[] TrayClumpScales =
        {
            new Vector3(0.32f, 0.15f, 0.32f),
            new Vector3(0.32f, 0.15f, 0.32f),
            new Vector3(0.36f, 0.16f, 0.36f),
            new Vector3(0.31f, 0.15f, 0.31f),
            new Vector3(0.31f, 0.15f, 0.31f),
            new Vector3(0.34f, 0.14f, 0.30f),
            new Vector3(0.33f, 0.16f, 0.33f),
            new Vector3(0.33f, 0.16f, 0.33f),
            new Vector3(0.38f, 0.18f, 0.36f),
            new Vector3(0.30f, 0.15f, 0.30f),
            new Vector3(0.30f, 0.15f, 0.30f),
            new Vector3(0.32f, 0.16f, 0.32f),
            new Vector3(0.31f, 0.16f, 0.31f),
            new Vector3(0.35f, 0.18f, 0.34f)
        };

        public bool IsDriving => _isDriving;
        public int CurrentLoad => _currentSnowLoad;
        public int MaxCapacity => maxCapacity;
        public int AccumulatedValueUnits => _accumulatedValueUnits;
        public bool HasCapacity => _currentSnowLoad < maxCapacity;
        public float DriverFollowDistance => driverFollowDistance;

        private void Awake()
        {
            ResolveReferences();
            BuildTraySnowBed();
            _lastWheelWorldPos = wheelPivot != null ? wheelPivot.position : transform.position;
        }

        private void Start()
        {
            _cachedHearth = Object.FindFirstObjectByType<FireplaceHearth>();
            FactoryEvents.RaiseWheelbarrowLoadChanged(_currentSnowLoad, maxCapacity);
        }

        public void BindReferences(
            Transform tiltPivot,
            Transform frontWheelPivot,
            Transform snowContainer,
            Collider mainCollider,
            Material snowMat)
        {
            trayTiltPivot = tiltPivot;
            wheelPivot = frontWheelPivot;
            traySnowRoot = snowContainer;
            bodyCollider = mainCollider;
            snowMaterial = snowMat;
        }

        private void ResolveReferences()
        {
            if (trayTiltPivot == null) trayTiltPivot = transform.Find("TrayTiltPivot");
            if (wheelPivot == null) wheelPivot = transform.Find("WheelPivot");
            if (traySnowRoot == null && trayTiltPivot != null)
            {
                traySnowRoot = trayTiltPivot.Find("TraySnowRoot");
            }
            if (bodyCollider == null) bodyCollider = GetComponent<Collider>();

            if (snowMaterial == null)
            {
                SnowPile anyPile = Object.FindFirstObjectByType<SnowPile>();
                if (anyPile != null)
                {
                    MeshRenderer mr = anyPile.GetComponent<MeshRenderer>();
                    if (mr != null) snowMaterial = mr.sharedMaterial;
                }
            }

            if (snowMaterial != null && _traySnowMaterialInstance == null)
            {
                _traySnowMaterialInstance = new Material(snowMaterial)
                {
                    name = "M_WheelbarrowSnowRuntime"
                };
            }
        }

        private void BuildTraySnowBed()
        {
            if (traySnowRoot == null) return;

            for (int i = traySnowRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(traySnowRoot.GetChild(i).gameObject);
            }
            _trayClumpVisuals.Clear();

            for (int i = 0; i < TrayClumpOffsets.Length; i++)
            {
                GameObject clump = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                clump.name = $"WheelbarrowSnowClump_{i}";
                clump.transform.SetParent(traySnowRoot, false);
                clump.transform.localPosition = TrayClumpOffsets[i];
                clump.transform.localRotation = Quaternion.Euler(
                    (i * 17f) % 18f - 9f,
                    (i * 53f) % 360f,
                    (i * 23f) % 14f - 7f);
                clump.transform.localScale = TrayClumpScales[i];

                Collider col = clump.GetComponent<Collider>();
                if (col != null) Destroy(col);

                MeshRenderer mr = clump.GetComponent<MeshRenderer>();
                if (mr != null && _traySnowMaterialInstance != null)
                {
                    mr.sharedMaterial = _traySnowMaterialInstance;
                }

                clump.SetActive(false);
                _trayClumpVisuals.Add(clump.transform);
            }
        }

        public Vector3 GetTrayReceiveWorldPoint()
        {
            if (traySnowRoot != null)
            {
                return traySnowRoot.TransformPoint(new Vector3(0.02f, 0.50f, 0.08f));
            }
            return transform.position + Vector3.up * 0.5f;
        }

        public bool TryReceiveFromShovel(int scoopCount, int totalValueUnits, out int acceptedCount)
        {
            acceptedCount = 0;
            if (scoopCount <= 0 || _currentSnowLoad >= maxCapacity)
            {
                return false;
            }

            int spaceLeft = maxCapacity - _currentSnowLoad;
            acceptedCount = Mathf.Min(scoopCount, spaceLeft);

            // Preserve proportional value units cleanly if only part of the shovel batch fits
            int acceptedValue = acceptedCount == scoopCount
                ? Mathf.Max(acceptedCount, totalValueUnits)
                : Mathf.Max(acceptedCount, Mathf.RoundToInt(totalValueUnits * ((float)acceptedCount / scoopCount)));

            _currentSnowLoad += acceptedCount;
            _accumulatedValueUnits += acceptedValue;

            RefreshTraySnowVisuals();
            FactoryEvents.RaiseWheelbarrowLoadChanged(_currentSnowLoad, maxCapacity);
            return true;
        }

        public bool CanDumpAtFireplace(out FireplaceHearth hearth)
        {
            if (_cachedHearth == null)
            {
                _cachedHearth = Object.FindFirstObjectByType<FireplaceHearth>();
            }
            hearth = _cachedHearth;

            if (hearth == null || _currentSnowLoad <= 0) return false;

            Vector3 trayWorld = GetTrayReceiveWorldPoint();
            Vector3 hearthTarget = hearth.transform.position + Vector3.up * 0.6f;
            float distSq = (new Vector2(trayWorld.x - hearthTarget.x, trayWorld.z - hearthTarget.z)).sqrMagnitude;
            return distSq <= fireplaceDumpRange * fireplaceDumpRange;
        }

        public bool TryDumpIntoFireplace(FireplaceHearth hearth)
        {
            if (hearth == null || _currentSnowLoad <= 0) return false;

            int deliveredScoops = _currentSnowLoad;
            int deliveredValue = Mathf.Max(deliveredScoops, _accumulatedValueUnits);

            _currentSnowLoad = 0;
            _accumulatedValueUnits = 0;
            _dumpAnimTimer = 0.52f;

            RefreshTraySnowVisuals();
            FactoryEvents.RaiseWheelbarrowLoadChanged(_currentSnowLoad, maxCapacity);

            SpawnDumpBurstToFireplace(hearth, deliveredScoops, deliveredValue);
            return true;
        }

        private void SpawnDumpBurstToFireplace(FireplaceHearth hearth, int deliveredScoops, int deliveredValueUnits)
        {
            Vector3 startPos = GetTrayReceiveWorldPoint();
            Vector3 fireCenter = hearth.transform.position + new Vector3(0f, 0.72f, 0f);
            float avgTierValue = (float)deliveredValueUnits / Mathf.Max(1, deliveredScoops);
            Material chunkMat = CreateTintedChunkMaterial(avgTierValue);

            int chunkCount = Mathf.Clamp(Mathf.CeilToInt(deliveredScoops * 0.5f) + 2, 3, 12);
            for (int i = 0; i < chunkCount; i++)
            {
                Vector3 spread = new Vector3(
                    Random.Range(-0.22f, 0.22f),
                    Random.Range(-0.04f, 0.16f),
                    Random.Range(-0.18f, 0.18f));

                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "WheelbarrowDumpChunk";
                go.transform.position = startPos + spread * 0.45f;
                go.transform.localScale = new Vector3(0.24f, 0.14f, 0.22f);
                go.transform.rotation = Random.rotation;

                Collider col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);

                MeshRenderer mr = go.GetComponent<MeshRenderer>();
                if (mr != null && chunkMat != null)
                {
                    mr.sharedMaterial = chunkMat;
                }

                _dumpChunks.Add(new DumpSnowChunk
                {
                    ChunkTransform = go.transform,
                    StartWorldPos = go.transform.position,
                    EndWorldPos = fireCenter + spread * 0.7f,
                    Elapsed = -i * 0.025f,
                    Duration = 0.32f,
                    ArcHeight = 0.55f + (i % 3) * 0.08f,
                    TargetFireplace = i == chunkCount - 1 ? hearth : null,
                    DeliveredScoops = deliveredScoops,
                    DeliveredValueUnits = deliveredValueUnits
                });
            }
        }

        private Material CreateTintedChunkMaterial(float avgValueMultiplier)
        {
            if (_traySnowMaterialInstance == null) return snowMaterial;
            return _traySnowMaterialInstance;
        }

        public void EnterDriving(FirstPersonController driver, ShovelController shovel)
        {
            if (_isDriving || driver == null) return;

            _isDriving = true;
            _driver = driver;
            _driverShovel = shovel;

            if (bodyCollider != null)
            {
                bodyCollider.enabled = false;
            }

            // Align driver cleanly behind the handles facing the wheelbarrow forward direction
            Vector3 flatFwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (flatFwd.sqrMagnitude < 0.001f) flatFwd = Vector3.forward;
            flatFwd.Normalize();

            Vector3 desiredDriverPos = transform.position - flatFwd * driverFollowDistance;
            float groundY = SampleGroundHeight(desiredDriverPos + Vector3.up * 1.4f, driver.transform.position.y);
            desiredDriverPos.y = groundY + 0.04f;

            float yaw = Quaternion.LookRotation(flatFwd, Vector3.up).eulerAngles.y;
            driver.SetWheelbarrowDriving(true, this, desiredDriverPos, yaw);

            if (_driverShovel != null)
            {
                _driverShovel.SetWheelbarrowControlActive(true, this);
            }

            _lastWheelWorldPos = wheelPivot != null ? wheelPivot.position : transform.position;
            FactoryEvents.RaiseStatusMessage(
                $"Conduciendo carretilla ({_currentSnowLoad}/{maxCapacity}). [WASD + Ratón] Mover · [Shift] Turbo · [F] Soltar.");
        }

        public void ExitDriving()
        {
            if (!_isDriving) return;

            _isDriving = false;

            if (_driver != null)
            {
                _driver.SetWheelbarrowDriving(false, null, _driver.transform.position, _driver.transform.eulerAngles.y);
            }

            if (_driverShovel != null)
            {
                _driverShovel.SetWheelbarrowControlActive(false, null);
            }

            if (bodyCollider != null)
            {
                bodyCollider.enabled = true;
            }

            _driver = null;
            _driverShovel = null;

            FactoryEvents.RaiseStatusMessage(
                $"Carretilla aparcada ({_currentSnowLoad}/{maxCapacity}). Excava nieve con la pala y échala dentro.");
        }

        /// <summary>
        /// Checks whether pushing the wheelbarrow forward from playerPos would hit a solid non-drivable wall/obstacle.
        /// Ignores flat ground and gentle walkable slopes so curbs and snow hills remain climbable.
        /// </summary>
        public bool CheckFrontObstacle(Vector3 playerWorldPos, Vector3 flatForward, float travelDistance)
        {
            Vector3 probeOrigin = playerWorldPos + Vector3.up * 0.55f + flatForward * (driverFollowDistance * 0.55f);
            float castDistance = driverFollowDistance * 0.55f + Mathf.Max(0.15f, travelDistance) + 0.35f;

            RaycastHit[] hits = Physics.SphereCastAll(probeOrigin, 0.28f, flatForward, castDistance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit h = hits[i];
                if (h.distance <= 0.001f || IsSelfOrDriverCollider(h.collider)) continue;

                // Ignore walkable ground and low slopes (let CharacterController climb snow hills and curbs)
                if (h.normal.y > 0.52f) continue;

                // Ignore small low curbs (< 0.32 m above player foot level)
                if (h.point.y < playerWorldPos.y + 0.32f) continue;

                return true;
            }
            return false;
        }

        private bool IsSelfOrDriverCollider(Collider col)
        {
            if (col == null) return true;
            if (col.transform == transform || col.transform.IsChildOf(transform)) return true;
            if (_driver != null && (col.transform == _driver.transform || col.transform.IsChildOf(_driver.transform))) return true;
            return false;
        }

        private void Update()
        {
            UpdateDumpChunks();
        }

        private void LateUpdate()
        {
            if (_isDriving && _driver != null)
            {
                UpdateDrivenPose();
            }
            else
            {
                UpdateParkedTilt();
            }
        }

        private void UpdateDrivenPose()
        {
            Vector3 flatFwd = Vector3.ProjectOnPlane(_driver.transform.forward, Vector3.up);
            if (flatFwd.sqrMagnitude < 0.001f) flatFwd = Vector3.forward;
            flatFwd.Normalize();

            Vector3 targetPos = _driver.transform.position + flatFwd * driverFollowDistance;

            float rearGroundY = SampleGroundHeight(
                _driver.transform.position + flatFwd * 0.55f + Vector3.up * 1.6f,
                _driver.transform.position.y);
            float frontGroundY = SampleGroundHeight(
                targetPos + flatFwd * 0.36f + Vector3.up * 1.8f,
                rearGroundY);

            // Prevent sinking below floor or floating wildly off terrain
            float targetY = Mathf.Lerp(transform.position.y, frontGroundY, 1f - Mathf.Exp(-18f * Time.deltaTime));
            targetPos.y = targetY;
            transform.position = targetPos;

            float wheelbase = Mathf.Max(0.6f, driverFollowDistance - 0.1f);
            float rawSlopeDeg = Mathf.Atan2(rearGroundY - frontGroundY, wheelbase) * Mathf.Rad2Deg;
            rawSlopeDeg = Mathf.Clamp(rawSlopeDeg, -26f, 26f);
            _currentSlopePitch = Mathf.Lerp(_currentSlopePitch, rawSlopeDeg, 1f - Mathf.Exp(-14f * Time.deltaTime));

            Quaternion headingRot = Quaternion.LookRotation(flatFwd, Vector3.up);
            transform.rotation = headingRot;

            ApplyVisualTilt(driveLiftPitchDeg);
            SpinWheelByDisplacement(flatFwd);
        }

        private void UpdateParkedTilt()
        {
            _currentSlopePitch = Mathf.Lerp(_currentSlopePitch, 0f, 1f - Mathf.Exp(-10f * Time.deltaTime));
            ApplyVisualTilt(0f);
        }

        private void ApplyVisualTilt(float targetLiftPitch)
        {
            _currentLiftPitch = Mathf.Lerp(_currentLiftPitch, targetLiftPitch, 1f - Mathf.Exp(-12f * Time.deltaTime));

            float dumpTilt = 0f;
            if (_dumpAnimTimer > 0f)
            {
                _dumpAnimTimer = Mathf.Max(0f, _dumpAnimTimer - Time.deltaTime);
                float t = 1f - (_dumpAnimTimer / 0.52f);
                dumpTilt = Mathf.Sin(t * Mathf.PI) * 38f;
            }

            if (trayTiltPivot != null)
            {
                // Positive pitch around local X lifts the rear handles (-Z) upward and tips the front lip (+Z) down
                trayTiltPivot.localRotation = Quaternion.Euler(_currentLiftPitch + _currentSlopePitch + dumpTilt, 0f, 0f);
            }
        }

        private void SpinWheelByDisplacement(Vector3 flatFwd)
        {
            if (wheelPivot == null) return;

            Vector3 currentWheelWorldPos = wheelPivot.position;
            Vector3 delta = currentWheelWorldPos - _lastWheelWorldPos;
            _lastWheelWorldPos = currentWheelWorldPos;

            float signedDist = Vector3.Dot(delta, flatFwd);
            if (Mathf.Abs(signedDist) > 0.0001f && wheelRadius > 0.01f)
            {
                float degrees = (signedDist / wheelRadius) * Mathf.Rad2Deg;
                wheelPivot.Rotate(Vector3.right, degrees, Space.Self);
            }
        }

        private float SampleGroundHeight(Vector3 rayOrigin, float fallbackY)
        {
            RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, 4.0f, ~0, QueryTriggerInteraction.Ignore);
            float bestY = float.MinValue;
            bool found = false;

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit h = hits[i];
                if (IsSelfOrDriverCollider(h.collider)) continue;
                if (h.point.y > rayOrigin.y - 0.1f) continue;

                if (h.point.y > bestY)
                {
                    bestY = h.point.y;
                    found = true;
                }
            }

            return found ? bestY : fallbackY;
        }

        private void UpdateDumpChunks()
        {
            for (int i = _dumpChunks.Count - 1; i >= 0; i--)
            {
                DumpSnowChunk c = _dumpChunks[i];
                c.Elapsed += Time.deltaTime;
                if (c.Elapsed < 0f) continue;

                float t = Mathf.Clamp01(c.Elapsed / c.Duration);
                Vector3 pos = Vector3.Lerp(c.StartWorldPos, c.EndWorldPos, t);
                pos.y += 4f * t * (1f - t) * c.ArcHeight;

                if (c.ChunkTransform != null)
                {
                    c.ChunkTransform.position = pos;
                }

                if (t >= 1f)
                {
                    if (c.ChunkTransform != null) Destroy(c.ChunkTransform.gameObject);

                    if (c.TargetFireplace != null)
                    {
                        c.TargetFireplace.DepositSnow(c.DeliveredScoops, c.DeliveredValueUnits);
                    }

                    _dumpChunks.RemoveAt(i);
                }
            }
        }

        private void RefreshTraySnowVisuals()
        {
            if (_trayClumpVisuals.Count == 0) return;

            if (_currentSnowLoad <= 0)
            {
                for (int i = 0; i < _trayClumpVisuals.Count; i++)
                {
                    _trayClumpVisuals[i].gameObject.SetActive(false);
                }
                return;
            }

            // Tint snow pile subtly toward icy cyan/glacier when carrying valuable ice/glacier layers
            if (_traySnowMaterialInstance != null)
            {
                float avgMultiplier = Mathf.Clamp((float)_accumulatedValueUnits / _currentSnowLoad, 1f, 3f);
                float glacierT = Mathf.Clamp01((avgMultiplier - 1f) * 0.5f);
                Color litColor = Color.Lerp(new Color(0.98f, 0.99f, 1.0f), new Color(0.55f, 0.86f, 1.0f), glacierT);
                Color shadowColor = Color.Lerp(new Color(0.58f, 0.73f, 0.89f), new Color(0.16f, 0.45f, 0.88f), glacierT);
                _traySnowMaterialInstance.SetColor("_LitSnowColor", litColor);
                _traySnowMaterialInstance.SetColor("_ShadowSnowColor", shadowColor);
            }

            float fillRatio = Mathf.Clamp01((float)_currentSnowLoad / Mathf.Max(1, maxCapacity));
            int activeCount = Mathf.Clamp(
                Mathf.CeilToInt(fillRatio * _trayClumpVisuals.Count),
                1,
                _trayClumpVisuals.Count);

            for (int i = 0; i < _trayClumpVisuals.Count; i++)
            {
                _trayClumpVisuals[i].gameObject.SetActive(i < activeCount);
            }
        }
    }
}
