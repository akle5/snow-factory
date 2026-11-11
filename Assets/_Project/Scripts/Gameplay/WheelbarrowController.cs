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
        private const float DumpAnimDuration = 0.56f;
        private const float DumpPeakFraction = 0.30f;
        private const float DumpFirstChunkDelay = 0.032f;

        private class DumpSnowChunk
        {
            public Transform ChunkTransform;
            public Vector3 LocalSpawnOffset;
            public Vector3 P0;
            public Vector3 P1;
            public Vector3 P2;
            public Vector3 EndWorldPos;
            public bool HasLaunched;
            public float Elapsed;
            public float Duration;
            public float ArcHeight;
            public float BoundingRadius;
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
        private float _dumpMoundDrainTimer;
        private float _dumpMoundDrainDuration;
        private float _dumpStartFillRatio;
        private float _dumpAvgHardness = 1f;
        private float _lastDrainVisualFillRatio = -1f;
        private Vector3 _lastWheelWorldPos;
        private Vector3 _lastFrameWbPos;
        private Vector3 _wbVelocity;
        private Material _traySnowMaterialInstance;
        private Material _dumpChunkMaterialInstance;
        private Mesh _traySnowMesh;
        private GameObject _traySnowMoundGo;
        private ProceduralSnowMeshUtility.ContainerCavityMap _cavityMap;

        private readonly List<DumpSnowChunk> _dumpChunks = new List<DumpSnowChunk>();
        private FireplaceHearth _cachedHearth;

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
            _lastFrameWbPos = transform.position;
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

            if (!Application.isPlaying) return;

            ResolveReferences();

            if ((_traySnowMoundGo == null || _traySnowMoundGo.transform.parent != traySnowRoot || _cavityMap == null) && traySnowRoot != null)
            {
                BuildTraySnowBed();
            }
            else if (_traySnowMoundGo != null)
            {
                MeshRenderer mr = _traySnowMoundGo.GetComponent<MeshRenderer>();
                if (mr != null && _traySnowMaterialInstance != null)
                {
                    mr.sharedMaterial = _traySnowMaterialInstance;
                }
            }

            RefreshTraySnowVisuals();
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
                snowMaterial = ProceduralSnowMeshUtility.FindSceneSnowMaterial();
            }

            ProceduralSnowMeshUtility.GetOrCreateCarriedSnowMaterial(
                ref _traySnowMaterialInstance,
                snowMaterial,
                "M_WheelbarrowSnowRuntime",
                ProceduralSnowMeshUtility.CarriedSnowMaterialProfile.WheelbarrowMound);
        }

        private void BuildTraySnowBed()
        {
            if (traySnowRoot == null) return;

            for (int i = traySnowRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(traySnowRoot.GetChild(i).gameObject);
            }

            if (_traySnowMesh != null)
            {
                Destroy(_traySnowMesh);
                _traySnowMesh = null;
            }
            _traySnowMoundGo = null;
            _cavityMap = null;

            Transform bodyTr = traySnowRoot.parent != null ? traySnowRoot.parent.Find("BodyAndTray") : null;
            if (bodyTr == null)
            {
                bodyTr = transform.Find("TrayTiltPivot/BodyAndTray");
            }

            MeshFilter bodyMf = bodyTr != null ? bodyTr.GetComponentInChildren<MeshFilter>() : null;
            if (bodyMf != null && bodyMf.sharedMesh != null)
            {
                Matrix4x4 bodyToSnowLocal;
                if (bodyMf.transform.parent == traySnowRoot.parent && traySnowRoot.parent != null)
                {
                    Matrix4x4 snowLocal = Matrix4x4.TRS(traySnowRoot.localPosition, traySnowRoot.localRotation, traySnowRoot.localScale);
                    Matrix4x4 bodyLocal = Matrix4x4.TRS(bodyMf.transform.localPosition, bodyMf.transform.localRotation, bodyMf.transform.localScale);
                    bodyToSnowLocal = snowLocal.inverse * bodyLocal;
                }
                else
                {
                    bodyToSnowLocal = traySnowRoot.worldToLocalMatrix * bodyMf.transform.localToWorldMatrix;
                }

                _cavityMap = ProceduralSnowMeshUtility.BuildWheelbarrowCavityMap(bodyMf.sharedMesh, bodyToSnowLocal);
            }
            else
            {
                _cavityMap = ProceduralSnowMeshUtility.BuildWheelbarrowCavityMap(null, Matrix4x4.identity);
            }

            _traySnowMesh = new Mesh
            {
                name = "WheelbarrowTraySnowMesh"
            };
            _traySnowMesh.MarkDynamic();

            _traySnowMoundGo = new GameObject("WheelbarrowSnowMound");
            _traySnowMoundGo.transform.SetParent(traySnowRoot, false);
            _traySnowMoundGo.transform.localPosition = Vector3.zero;
            _traySnowMoundGo.transform.localRotation = Quaternion.identity;
            _traySnowMoundGo.transform.localScale = Vector3.one;

            MeshFilter mf = _traySnowMoundGo.AddComponent<MeshFilter>();
            mf.sharedMesh = _traySnowMesh;

            MeshRenderer mr = _traySnowMoundGo.AddComponent<MeshRenderer>();
            if (_traySnowMaterialInstance != null)
            {
                mr.sharedMaterial = _traySnowMaterialInstance;
            }
            else if (snowMaterial != null)
            {
                mr.sharedMaterial = snowMaterial;
            }

            _traySnowMoundGo.SetActive(false);
        }

        public Vector3 GetTrayReceiveWorldPoint()
        {
            if (traySnowRoot != null)
            {
                return traySnowRoot.TransformPoint(new Vector3(0.0175f, 0.54f, -0.06f));
            }
            return transform.position + Vector3.up * 0.54f;
        }

        public Vector3 GetTrayDumpMouthWorldPoint(Vector3 localSpread = default)
        {
            Vector3 mouthLocal = new Vector3(0.0175f, 0.708f, 0.482f) + localSpread;
            if (traySnowRoot != null)
            {
                return traySnowRoot.TransformPoint(mouthLocal);
            }
            return transform.TransformPoint(mouthLocal);
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
            _dumpMoundDrainTimer = 0f;

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
            if (distSq > fireplaceDumpRange * fireplaceDumpRange) return false;

            // Reject dumping from behind the solid brick back wall of the fireplace
            Vector3 mouthHearthLocal = hearth.transform.InverseTransformPoint(GetTrayDumpMouthWorldPoint());
            if (mouthHearthLocal.z < 0.05f) return false;
            if (Mathf.Abs(mouthHearthLocal.x) < 1.28f && mouthHearthLocal.z < 0.25f) return false;

            return true;
        }

        public bool TryDumpIntoFireplace(FireplaceHearth hearth)
        {
            if (hearth == null || _currentSnowLoad <= 0) return false;

            int deliveredScoops = _currentSnowLoad;
            int deliveredValue = Mathf.Max(deliveredScoops, _accumulatedValueUnits);

            _dumpStartFillRatio = Mathf.Clamp01((float)deliveredScoops / Mathf.Max(1, maxCapacity));
            _dumpAvgHardness = Mathf.Clamp((float)deliveredValue / Mathf.Max(1, deliveredScoops), 1f, 3f);
            _lastDrainVisualFillRatio = _dumpStartFillRatio;

            _currentSnowLoad = 0;
            _accumulatedValueUnits = 0;
            _dumpAnimTimer = DumpAnimDuration;

            FactoryEvents.RaiseWheelbarrowLoadChanged(_currentSnowLoad, maxCapacity);

            SpawnDumpBurstToFireplace(hearth, deliveredScoops, deliveredValue);
            return true;
        }

        private void SpawnDumpBurstToFireplace(FireplaceHearth hearth, int deliveredScoops, int deliveredValueUnits)
        {
            Vector3 fireCenter = hearth.transform.position + new Vector3(0f, 0.72f, 0f);
            float avgTierValue = (float)deliveredValueUnits / Mathf.Max(1, deliveredScoops);
            int iceTier = ProceduralSnowMeshUtility.HardnessMultiplierToIceTier(avgTierValue);
            Material baseMat = _traySnowMaterialInstance != null ? _traySnowMaterialInstance : snowMaterial;
            Material chunkMat = ProceduralSnowMeshUtility.GetOrCreateCarriedSnowMaterial(
                ref _dumpChunkMaterialInstance,
                baseMat,
                "M_WheelbarrowDumpChunkRuntime",
                ProceduralSnowMeshUtility.CarriedSnowMaterialProfile.FlyingClod,
                avgTierValue);

            int chunkCount = Mathf.Clamp(Mathf.CeilToInt(deliveredScoops * 0.5f) + 2, 3, 12);
            float staggerStep = chunkCount > 1 ? Mathf.Min(0.016f, 0.165f / (chunkCount - 1)) : 0f;
            _dumpMoundDrainDuration = DumpFirstChunkDelay + (chunkCount - 1) * staggerStep;
            _dumpMoundDrainTimer = _dumpMoundDrainDuration;

            for (int i = 0; i < chunkCount; i++)
            {
                Vector3 localSpread = new Vector3(
                    Random.Range(-0.11f, 0.11f),
                    Random.Range(0.0f, 0.045f),
                    Random.Range(0.0f, 0.045f));
                Vector3 fireSpread = new Vector3(
                    Random.Range(-0.16f, 0.16f),
                    Random.Range(-0.03f, 0.11f),
                    Random.Range(-0.12f, 0.12f));

                GameObject go = new GameObject("WheelbarrowDumpChunk");
                go.transform.position = GetTrayDumpMouthWorldPoint(localSpread);
                Vector3 chunkScale = new Vector3(
                    Random.Range(0.16f, 0.22f),
                    Random.Range(0.13f, 0.18f),
                    Random.Range(0.15f, 0.21f));
                go.transform.localScale = chunkScale;
                go.transform.rotation = Random.rotation;

                MeshFilter mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = ProceduralSnowMeshUtility.GetOrCreateSharedSnowClodMesh(i, iceTier);

                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                if (chunkMat != null)
                {
                    mr.sharedMaterial = chunkMat;
                }

                // Keep staggered chunks hidden until their launch frame while the tub mound visually empties
                go.SetActive(false);

                float boundingRadius = Mathf.Max(chunkScale.x, Mathf.Max(chunkScale.y, chunkScale.z)) * 0.48f;
                _dumpChunks.Add(new DumpSnowChunk
                {
                    ChunkTransform = go.transform,
                    LocalSpawnOffset = localSpread,
                    EndWorldPos = fireCenter + fireSpread,
                    HasLaunched = false,
                    Elapsed = -(DumpFirstChunkDelay + i * staggerStep),
                    Duration = 0.34f,
                    ArcHeight = 0.46f + (i % 3) * 0.07f,
                    BoundingRadius = boundingRadius,
                    TargetFireplace = i == chunkCount - 1 ? hearth : null,
                    DeliveredScoops = deliveredScoops,
                    DeliveredValueUnits = deliveredValueUnits
                });
            }
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
            _lastFrameWbPos = transform.position;
            _wbVelocity = Vector3.zero;
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

            if (Time.deltaTime > 0.0001f)
            {
                Vector3 rawVel = (transform.position - _lastFrameWbPos) / Time.deltaTime;
                rawVel.y = 0f;
                _wbVelocity = Vector3.ClampMagnitude(rawVel, 12f);
            }
            else
            {
                _wbVelocity = Vector3.zero;
            }
            _lastFrameWbPos = transform.position;

            UpdateDumpingTrayMoundDrain();

            // Update projectiles after the wheelbarrow and TrayTiltPivot poses have resolved for this frame
            UpdateDumpChunks();
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
                float p = 1f - (_dumpAnimTimer / DumpAnimDuration);
                // Tip forward briskly to match the snow spill (peaking at DumpPeakFraction), then return smoothly
                float shaped = p < DumpPeakFraction
                    ? Mathf.Sin((p / DumpPeakFraction) * Mathf.PI * 0.5f)
                    : Mathf.Cos(((p - DumpPeakFraction) / (1f - DumpPeakFraction)) * Mathf.PI * 0.5f);
                dumpTilt = Mathf.Clamp01(shaped) * 40f;
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

        private void UpdateDumpingTrayMoundDrain()
        {
            if (_currentSnowLoad > 0 || _traySnowMoundGo == null || _traySnowMesh == null || _cavityMap == null)
            {
                return;
            }

            if (_dumpMoundDrainTimer <= 0f)
            {
                if (_traySnowMoundGo.activeSelf)
                {
                    _traySnowMoundGo.SetActive(false);
                }
                return;
            }

            _dumpMoundDrainTimer = Mathf.Max(0f, _dumpMoundDrainTimer - Time.deltaTime);
            float remainFrac = _dumpMoundDrainDuration > 0.001f
                ? Mathf.Clamp01(_dumpMoundDrainTimer / _dumpMoundDrainDuration)
                : 0f;

            if (remainFrac <= 0.02f)
            {
                _dumpMoundDrainTimer = 0f;
                _traySnowMoundGo.SetActive(false);
                return;
            }

            _traySnowMoundGo.SetActive(true);
            float targetFill = _dumpStartFillRatio * remainFrac;
            if (_lastDrainVisualFillRatio < 0f || Mathf.Abs(_lastDrainVisualFillRatio - targetFill) >= 0.08f)
            {
                _lastDrainVisualFillRatio = targetFill;
                ProceduralSnowMeshUtility.ApplyCarriedSnowHardnessTint(_traySnowMaterialInstance, _dumpAvgHardness);
                ProceduralSnowMeshUtility.UpdateWheelbarrowSnowMesh(_traySnowMesh, _cavityMap, targetFill, _dumpAvgHardness);
            }
        }

        private void UpdateDumpChunks()
        {
            Vector3 wbFwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (wbFwd.sqrMagnitude < 0.0001f) wbFwd = Vector3.forward;
            wbFwd.Normalize();
            Vector3 wbRight = Vector3.Cross(Vector3.up, wbFwd).normalized;

            for (int i = _dumpChunks.Count - 1; i >= 0; i--)
            {
                DumpSnowChunk c = _dumpChunks[i];
                c.Elapsed += Time.deltaTime;
                if (c.Elapsed < 0f) continue;

                FireplaceHearth activeHearth = c.TargetFireplace != null ? c.TargetFireplace : _cachedHearth;

                if (!c.HasLaunched)
                {
                    c.HasLaunched = true;
                    Vector3 p0 = GetTrayDumpMouthWorldPoint(c.LocalSpawnOffset);
                    Vector3 toEndHoriz = Vector3.ProjectOnPlane(c.EndWorldPos - p0, Vector3.up);
                    float horizDist = toEndHoriz.magnitude;
                    Vector3 dirToFire = horizDist > 0.001f ? toEndHoriz / horizDist : wbFwd;
                    float facingDot = Vector3.Dot(wbFwd, dirToFire);
                    float fwdSpeed = Mathf.Max(0f, Vector3.Dot(_wbVelocity, wbFwd));

                    c.Duration = Mathf.Clamp(0.28f + horizDist * 0.035f, 0.32f, 0.48f);

                    Vector3 p1;
                    Vector3 p2;

                    bool hasPortal = activeHearth != null;
                    Vector3 p0Hearth = hasPortal ? activeHearth.transform.InverseTransformPoint(p0) : Vector3.zero;
                    Vector3 portalWorld = Vector3.zero;
                    if (hasPortal)
                    {
                        Vector3 portalLocal = new Vector3(
                            Mathf.Clamp(p0Hearth.x * 0.25f, -0.32f, 0.32f),
                            1.02f,
                            0.94f);
                        portalWorld = activeHearth.transform.TransformPoint(portalLocal);
                    }

                    if (facingDot >= 0.25f)
                    {
                        float forwardPush = Mathf.Clamp(horizDist * 0.36f, 0.18f, 0.65f);
                        if (horizDist > 1.2f)
                        {
                            forwardPush += Mathf.Min(fwdSpeed * 0.04f, horizDist * 0.22f);
                        }
                        float upPush = Mathf.Clamp(0.30f + horizDist * 0.07f + fwdSpeed * 0.03f, 0.30f, 0.62f);
                        Vector3 lateralDodge = dirToFire * (fwdSpeed * 0.045f * (1f - facingDot));

                        p1 = p0 + wbFwd * forwardPush + Vector3.up * upPush + lateralDodge;
                        p2 = Vector3.Lerp(p1, c.EndWorldPos, 0.52f) + Vector3.up * (c.ArcHeight * 0.78f);

                        if (hasPortal && Mathf.Abs(p0Hearth.x) > 0.55f && p0Hearth.z < 2.6f)
                        {
                            p2 = Vector3.Lerp(p2, portalWorld, 0.78f);
                        }
                    }
                    else
                    {
                        // Angled sideways, sprinting past, or facing away: eject clear of the front lip and sweep around the side
                        float sideDot = Vector3.Dot(wbRight, dirToFire);
                        float sideSign = sideDot >= -0.05f ? 1f : -1f;
                        float awayness = Mathf.InverseLerp(0.25f, -1.0f, facingDot);

                        float forwardPush = Mathf.Lerp(0.34f, 0.26f, awayness);
                        float sidePush = sideSign * Mathf.Lerp(0.42f, 0.80f, awayness);
                        float upPush = Mathf.Lerp(0.58f, 0.76f, awayness);

                        p1 = p0 + wbFwd * forwardPush + wbRight * sidePush + Vector3.up * upPush;
                        p2 = Vector3.Lerp(p1, c.EndWorldPos, 0.50f);
                        p2 += wbRight * (sideSign * 0.72f * awayness);
                        p2.y = Mathf.Max(p2.y, Mathf.Max(p0.y, c.EndWorldPos.y) + Mathf.Lerp(0.55f, 0.72f, awayness));

                        if (hasPortal && (Mathf.Abs(p0Hearth.x) > 0.52f || p0Hearth.z < 1.35f))
                        {
                            float portalBlend = Mathf.Lerp(0.75f, 0.45f, awayness);
                            p2 = Vector3.Lerp(p2, portalWorld, portalBlend);
                            if (awayness > 0.35f)
                            {
                                // Keep the midpoint swept laterally outside the wheelbarrow even when blending toward the hearth portal
                                p2 += wbRight * (sideSign * 0.35f * awayness);
                            }
                        }
                    }

                    // Keep trajectories below BrickLintel (y=1.825) and inside side pillars near the hearth opening
                    if (hasPortal)
                    {
                        Vector3 p2H = activeHearth.transform.InverseTransformPoint(p2);
                        if (p2H.z > -0.4f && p2H.z < 1.15f && Mathf.Abs(p2H.x) < 1.35f)
                        {
                            p2H.y = Mathf.Min(p2H.y, 1.48f);
                            if (p2H.z < 0.78f)
                            {
                                p2H.x = Mathf.Clamp(p2H.x, -0.36f, 0.36f);
                            }
                            p2 = activeHearth.transform.TransformPoint(p2H);
                        }
                    }

                    c.P0 = p0;
                    c.P1 = p1;
                    c.P2 = p2;

                    if (c.ChunkTransform != null)
                    {
                        c.ChunkTransform.position = p0;
                        c.ChunkTransform.gameObject.SetActive(true);
                    }
                }

                float t = Mathf.Clamp01(c.Elapsed / c.Duration);
                Vector3 pos = EvaluateCubicBezier(c.P0, c.P1, c.P2, c.EndWorldPos, t);

                if (t < 0.94f)
                {
                    EnforceWheelbarrowAndMasonryClearance(ref pos, c, t, activeHearth);
                }

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

        private void EnforceWheelbarrowAndMasonryClearance(ref Vector3 pos, DumpSnowChunk c, float t, FireplaceHearth hearth)
        {
            float pad = Mathf.Max(0.11f, c.BoundingRadius + 0.025f);

            // 1. Live clearance against tilted tub + handles in TraySnowRoot local space
            if (traySnowRoot != null)
            {
                Vector3 lp = traySnowRoot.InverseTransformPoint(pos);
                Vector3 localPush = Vector3.zero;

                // Tub + frame envelope: x in [-0.38, 0.41], y in [0.00, 0.64], z in [-0.60, 0.44]
                if (TryComputeBoxEscapePush(lp, new Vector3(-0.38f, 0.00f, -0.60f), new Vector3(0.41f, 0.64f, 0.44f), pad, true, out Vector3 tubPush))
                {
                    localPush += tubPush;
                    lp += tubPush;
                }

                // Rear handles envelope: x in [-0.40, 0.43], y in [0.34, 0.71], z in [-1.31, -0.56]
                if (TryComputeBoxEscapePush(lp, new Vector3(-0.40f, 0.34f, -1.31f), new Vector3(0.43f, 0.71f, -0.56f), pad, true, out Vector3 handlePush))
                {
                    localPush += handlePush;
                }

                if (localPush.sqrMagnitude > 0.000001f)
                {
                    Vector3 worldPush = traySnowRoot.TransformVector(localPush);
                    pos += worldPush;
                    // Gently bias control points so subsequent frames stay smoothly clear without snapping back inside
                    float ctrlWeight = Mathf.Clamp(1f - t, 0.2f, 1f);
                    c.P1 += worldPush * (0.45f * ctrlWeight);
                    c.P2 += worldPush * (0.45f * ctrlWeight);
                }
            }

            // 2. Live clearance against front wheel envelope in Wheelbarrow root space
            Vector3 rp = transform.InverseTransformPoint(pos);
            if (TryComputeBoxEscapePush(rp, new Vector3(-0.08f, 0.00f, 0.14f), new Vector3(0.12f, 0.41f, 0.57f), pad, false, out Vector3 wheelPush))
            {
                Vector3 worldWheelPush = transform.TransformVector(wheelPush);
                pos += worldWheelPush;
                c.P1 += worldWheelPush * 0.35f;
            }

            // 3. Live clearance against FireplaceHearth brick side pillars & lintel so chunks enter through the open firebox mouth
            if (hearth != null)
            {
                Vector3 hp = hearth.transform.InverseTransformPoint(pos);
                bool modified = false;

                if (hp.z > -0.40f && hp.z < 0.80f && Mathf.Abs(hp.x) >= 0.46f && Mathf.Abs(hp.x) <= 1.32f && hp.y < 2.35f)
                {
                    float signX = Mathf.Sign(hp.x);
                    if (Mathf.Abs(hp.x) <= 0.72f)
                    {
                        float pushInX = (signX * 0.44f) - hp.x;
                        float pushFrontZ = 0.82f - hp.z;
                        if (Mathf.Abs(pushInX) < pushFrontZ)
                        {
                            hp.x += pushInX;
                        }
                        else
                        {
                            hp.z += pushFrontZ;
                        }
                    }
                    else
                    {
                        float pushOutX = (signX * 1.34f) - hp.x;
                        float pushFrontZ = 0.82f - hp.z;
                        if (Mathf.Abs(pushOutX) < pushFrontZ)
                        {
                            hp.x += pushOutX;
                        }
                        else
                        {
                            hp.z += pushFrontZ;
                        }
                    }
                    modified = true;
                }

                if (hp.z > -0.40f && hp.z < 0.82f && Mathf.Abs(hp.x) < 1.28f && hp.y > 1.66f && hp.y < 2.45f)
                {
                    hp.y = 1.66f;
                    modified = true;
                }

                if (modified)
                {
                    pos = hearth.transform.TransformPoint(hp);
                }
            }
        }

        private static bool TryComputeBoxEscapePush(
            Vector3 p,
            Vector3 minBounds,
            Vector3 maxBounds,
            float pad,
            bool allowUpBias,
            out Vector3 push)
        {
            float minX = minBounds.x - pad;
            float maxX = maxBounds.x + pad;
            float minY = minBounds.y - pad;
            float maxY = maxBounds.y + pad;
            float minZ = minBounds.z - pad;
            float maxZ = maxBounds.z + pad;

            if (p.x <= minX || p.x >= maxX || p.y <= minY || p.y >= maxY || p.z <= minZ || p.z >= maxZ)
            {
                push = Vector3.zero;
                return false;
            }

            float pushLeft = maxX - p.x;   // +X
            float pushRight = p.x - minX;  // -X
            float pushUp = maxY - p.y;     // +Y
            float pushFwd = maxZ - p.z;    // +Z
            float pushBack = p.z - minZ;   // -Z

            // Prefer lifting up (+Y) over the tub rim or pushing sideways/forward rather than pushing down into the floor
            float bestCost = allowUpBias ? pushUp * 0.72f : pushUp;
            push = new Vector3(0f, pushUp, 0f);

            if (pushLeft < bestCost)
            {
                bestCost = pushLeft;
                push = new Vector3(pushLeft, 0f, 0f);
            }
            if (pushRight < bestCost)
            {
                bestCost = pushRight;
                push = new Vector3(-pushRight, 0f, 0f);
            }
            if (pushFwd < bestCost)
            {
                bestCost = pushFwd;
                push = new Vector3(0f, 0f, pushFwd);
            }
            if (pushBack < bestCost)
            {
                push = new Vector3(0f, 0f, -pushBack);
            }

            return true;
        }

        private static Vector3 EvaluateCubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            float uu = u * u;
            float tt = t * t;
            return (uu * u) * p0
                 + (3f * uu * t) * p1
                 + (3f * u * tt) * p2
                 + (tt * t) * p3;
        }

        private void RefreshTraySnowVisuals()
        {
            if (_traySnowMoundGo == null || _traySnowMesh == null || _cavityMap == null) return;

            if (_currentSnowLoad <= 0)
            {
                _traySnowMoundGo.SetActive(false);
                return;
            }

            _traySnowMoundGo.SetActive(true);

            // Keep sunlit snow natural crisp white while subtly deepening alpine shadow tone for higher-tier ice loads.
            float avgMultiplier = Mathf.Clamp((float)_accumulatedValueUnits / _currentSnowLoad, 1f, 3f);
            ProceduralSnowMeshUtility.ApplyCarriedSnowHardnessTint(_traySnowMaterialInstance, avgMultiplier);

            float fillRatio = Mathf.Clamp01((float)_currentSnowLoad / Mathf.Max(1, maxCapacity));
            ProceduralSnowMeshUtility.UpdateWheelbarrowSnowMesh(_traySnowMesh, _cavityMap, fillRatio, avgMultiplier);
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _dumpChunks.Count; i++)
            {
                if (_dumpChunks[i].ChunkTransform != null)
                {
                    Destroy(_dumpChunks[i].ChunkTransform.gameObject);
                }
            }
            _dumpChunks.Clear();

            if (_traySnowMesh != null)
            {
                Destroy(_traySnowMesh);
                _traySnowMesh = null;
            }

            if (_traySnowMaterialInstance != null)
            {
                Destroy(_traySnowMaterialInstance);
                _traySnowMaterialInstance = null;
            }

            if (_dumpChunkMaterialInstance != null)
            {
                Destroy(_dumpChunkMaterialInstance);
                _dumpChunkMaterialInstance = null;
            }
        }
    }
}
